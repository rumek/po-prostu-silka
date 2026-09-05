using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Domain;

namespace po_prostu_silka.Application.Auth;

public record LoginRequest(string Email, string Password);

/// <summary>
/// Registration input. The five contact fields land here with S-13 and are required: the columns
/// behind them are nullable only so accounts created before that slice remain readable.
/// </summary>
public record RegisterRequest(
    string Email,
    string Password,
    string DisplayName,
    string PhoneNumber,
    string Street,
    string HouseNumber,
    string PostalCode,
    string City);

/// <summary>
/// Why the login failure is named: S-02's blocked members need a different message from a wrong
/// password. Callers must not treat <c>invalid_credentials</c> as "no such account" - it also covers
/// a wrong password.
///
/// <c>pending_approval</c> is no longer reachable from /login: S-01 inverted that rule and a pending
/// member now receives a session (see LoginAsync). The literal is kept because the SPA's
/// LoginFailureReason union still carries it and removing it is churn for no gain.
/// </summary>
public record LoginFailure(string Reason);

/// <summary>
/// Why registration failed. Never echoes Identity's raw error text to the client.
///
/// The <c>invalid_phone</c> / <c>invalid_street</c> / <c>invalid_house_number</c> /
/// <c>invalid_postal_code</c> / <c>invalid_city</c> codes come from <see cref="ContactDetails"/>,
/// which is also what <c>PUT /api/profile</c> answers with - one vocabulary, two endpoints.
/// </summary>
public record RegisterFailure(string Reason);

/// <summary>
/// The session payload every authenticated screen reads.
///
/// The contact fields ride along rather than sitting behind their own GET (S-13): the profile form
/// pre-fills from session state, so the SPA needs no second round trip on a 5-DTU tier - and the
/// screen that prompts an incomplete account to fill them in can tell they are empty without asking.
/// They are nullable here and only here: accounts created before S-13 have none, and that is exactly
/// what the prompt keys off.
///
/// DisplayName and Email are deliberately absent from every write surface. The gym owns the name on
/// the membership; no endpoint in this app lets anyone change either.
/// </summary>
public record CurrentUser(
    string Id,
    string Email,
    string DisplayName,
    string Status,
    string[] Roles,
    string? PhoneNumber,
    string? Street,
    string? HouseNumber,
    string? PostalCode,
    string? City);

/// <summary>
/// The authentication surface: create an account, establish a session, inspect it, refresh it,
/// end it.
///
/// Registration lands here with S-01 (registration-and-approval), which also owns the approval
/// semantics that decide what a newly created account is allowed to do: nothing, until an admin
/// approves it.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", LoginAsync).AllowAnonymous();
        group.MapPost("/register", RegisterAsync).AllowAnonymous();
        group.MapPost("/logout", LogoutAsync).RequireAuthorization();

        // Bare RequireAuthorization(), NEVER the ActiveMember policy - a pending member has to be
        // able to call the one endpoint that stops them being pending.
        group.MapPost("/refresh", RefreshAsync).RequireAuthorization();

        // RequireAuthorization() and NOT the ActiveMember policy: a Pending member must be able to
        // read their own status, or S-01 cannot tell the awaiting-approval screen from a logged-out
        // visitor.
        group.MapGet("/me", GetCurrentUser).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> LoginAsync(
        [FromBody] LoginRequest request,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager)
    {
        // The record's strings are non-nullable, but {"email": null} deserialises to null all the
        // same - nullable reference types are a compile-time contract, not a runtime one - and
        // FindByEmailAsync would then throw ArgumentNullException on an anonymous endpoint. Answer
        // the same non-disclosing 401 a wrong address gets, rather than a 500.
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.Json(new LoginFailure("invalid_credentials"), statusCode: 401);
        }

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return Results.Json(new LoginFailure("invalid_credentials"), statusCode: 401);
        }

        // Password is checked BEFORE status, deliberately. Reporting "pending" or "blocked" to
        // someone who has not proved they own the account would leak both that the address is
        // registered and what state it is in.
        //
        // ASYMMETRY, ON PURPOSE: /register DOES disclose that an address is taken (409 email_taken).
        // Do not "fix" one endpoint to match the other - see RegisterAsync for why registration
        // chooses disclosure and login chooses silence.
        var passwordResult = await signInManager.CheckPasswordSignInAsync(
            user, request.Password, lockoutOnFailure: true);

        if (!passwordResult.Succeeded)
        {
            return Results.Json(new LoginFailure("invalid_credentials"), statusCode: 401);
        }

        // Pending is deliberately NOT refused: the PRD's Access Control section and roadmap S-01 both
        // specify that a pending member signs in and sees an awaiting-approval screen. Content is
        // gated by the ActiveMember policy, not by refusing the session. Blocked stays refused -
        // handing a 30-day cookie to someone whose access was revoked inverts what Blocked is for.
        if (user.Status == AccountStatus.Blocked)
        {
            return Results.Json(new LoginFailure("blocked"), statusCode: 401);
        }

        // isPersistent: true is what makes the 30-day window survive closing the browser - without
        // it the cookie is a session cookie and mobile members re-login constantly (PRD FR-002).
        await signInManager.SignInAsync(user, isPersistent: true);

        return Results.Ok(await BuildCurrentUserAsync(user, userManager));
    }

    /// <summary>
    /// Creates a Pending account and signs it in immediately (D1).
    ///
    /// ASYMMETRY, ON PURPOSE: this endpoint discloses that an email is already registered, while
    /// /login deliberately refuses to distinguish a wrong password from an unknown address. The
    /// trade is not an oversight. With no email-confirmation flow in scope, silence would strand a
    /// real member who forgot they had signed up: they retry, see success, and wait forever for the
    /// approval of an account that was never created - and nothing else would ever tell them. For a
    /// single gym, "this address belongs to a member here" is close to worthless to an attacker.
    /// Do not align the two endpoints without re-deciding that.
    ///
    /// There is no rate limiting and no CAPTCHA either: FR-001 names the approval gate itself as the
    /// mitigation. The accepted cost is that junk registrations accumulate as Pending rows.
    /// </summary>
    private static async Task<IResult> RegisterAsync(
        [FromBody] RegisterRequest request,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory)
    {
        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        if (displayName.Length == 0)
        {
            return Results.Json(new RegisterFailure("invalid_display_name"), statusCode: 400);
        }

        // Same runtime-vs-compile-time gap as LoginAsync: a null in the JSON body reaches here
        // despite the non-nullable record, and FindByEmailAsync would throw. Answer in this
        // endpoint's own vocabulary instead of 500-ing.
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return Results.Json(new RegisterFailure("invalid_email"), statusCode: 400);
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.Json(new RegisterFailure("invalid_password"), statusCode: 400);
        }

        // Contact details are validated BEFORE the duplicate-email lookup and long before
        // CreateAsync, so a malformed submission creates nothing and costs one round trip.
        if (!ContactDetails.TryCreate(
                request.PhoneNumber,
                request.Street,
                request.HouseNumber,
                request.PostalCode,
                request.City,
                out var contact,
                out var contactFailure))
        {
            return Results.Json(new RegisterFailure(contactFailure), statusCode: 400);
        }

        if (await userManager.FindByEmailAsync(request.Email) is not null)
        {
            return Results.Json(new RegisterFailure("email_taken"), statusCode: 409);
        }

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = displayName,
            Status = AccountStatus.Pending,
            CreatedAt = timeProvider.GetUtcNow(),
            PhoneNumber = contact.PhoneNumber,
            Street = contact.Street,
            HouseNumber = contact.HouseNumber,
            PostalCode = contact.PostalCode,
            City = contact.City,
        };

        var created = await userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            // Map Identity's error codes to our own vocabulary rather than forwarding its text: the
            // raw descriptions are English, unlocalised, and occasionally leak policy detail.
            var codes = created.Errors.Select(e => e.Code).ToArray();

            // Duplicate FIRST, and as a 409 rather than a 400. The FindByEmailAsync check above
            // catches this in the ordinary case, but two simultaneous registrations of the same
            // address both pass it and the loser lands here. Left to the "Email" branch below it
            // would answer 400 invalid_email - telling someone their perfectly valid address is
            // malformed, and hiding the "Zaloguj się" link the real email_taken path offers.
            if (codes.Any(c => c.StartsWith("Duplicate", StringComparison.Ordinal)))
            {
                return Results.Json(new RegisterFailure("email_taken"), statusCode: 409);
            }

            var reason = codes.Any(c => c.Contains("Password", StringComparison.Ordinal))
                ? "invalid_password"
                : codes.Any(c => c.Contains("Email", StringComparison.Ordinal)
                    || c.Contains("UserName", StringComparison.Ordinal))
                    ? "invalid_email"
                    : "invalid_registration";

            return Results.Json(new RegisterFailure(reason), statusCode: 400);
        }

        var roleAssigned = await userManager.AddToRoleAsync(user, ApplicationRoles.User);
        if (!roleAssigned.Succeeded)
        {
            // A role-less account is unrecoverable through anything this slice ships: it satisfies
            // the ActiveMember policy's status check, fails its RequireRole, and the admin surface
            // here is approve-only. Better to undo the registration and let them retry than to leave
            // an account that can be approved and still cannot do anything.
            var logger = loggerFactory.CreateLogger(typeof(AuthEndpoints));
            logger.LogError(
                "Role assignment failed for new user {UserId}; deleting the account. Errors: {Errors}",
                user.Id,
                string.Join("; ", roleAssigned.Errors.Select(e => e.Description)));

            await userManager.DeleteAsync(user);
            return Results.Problem("Registration could not be completed.", statusCode: 500);
        }

        // Signed in immediately, and persistent for the same reason login is: the member closes the
        // tab, waits hours for approval, and must not have to re-enter credentials to check.
        await signInManager.SignInAsync(user, isPersistent: true);

        // Same shape /login returns, so the SPA has one code path for "you now have a session".
        return Results.Ok(await BuildCurrentUserAsync(user, userManager));
    }

    /// <summary>
    /// Re-mints the caller's claims from their current row, without ending the session.
    ///
    /// Why this exists: the ActiveMember/Admin policies read the account_status CLAIM from the
    /// cookie, not the database (AuthorizationPolicies), and that claim is re-minted only when the
    /// security-stamp validator refreshes - on the interval set in Program.cs, which is the one
    /// place that number is stated. So a member approved by the admin keeps a cookie that says
    /// Pending until that interval elapses, while /me (which reads the
    /// database) correctly reports Active. Without this endpoint the SPA routes them into the app on
    /// the strength of /me and every ActiveMember call then returns 403.
    ///
    /// RefreshSignInAsync re-runs AppUserClaimsPrincipalFactory against the current entity, so status
    /// and roles are both corrected in one round-trip. It is safe to call while still Pending - it
    /// simply re-mints Pending claims - so the awaiting screen's button calls it unconditionally and
    /// reads the status from the response.
    /// </summary>
    private static async Task<IResult> RefreshAsync(
        ClaimsPrincipal principal,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        await signInManager.RefreshSignInAsync(user);
        return Results.Ok(await BuildCurrentUserAsync(user, userManager));
    }

    private static async Task<IResult> LogoutAsync(SignInManager<ApplicationUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> GetCurrentUser(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.GetUserAsync(principal);

        // The cookie authenticated, but the row is gone - a deleted account with a live cookie.
        // This lookup is kept deliberately: it is that check, and it returns a fresh DisplayName
        // and Status rather than whatever was true when the cookie was last refreshed.
        if (user is null)
        {
            return Results.Unauthorized();
        }

        // Roles come from the cookie's claims, not a second query. AppUserClaimsPrincipalFactory
        // mints one role claim per role at sign-in and on every security-stamp refresh, so this is
        // the same data - and /me is called on every SPA cold load against a 5-DTU tier.
        var roles = principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();

        return Results.Ok(new CurrentUser(
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            user.Status.ToString(),
            roles,
            user.PhoneNumber,
            user.Street,
            user.HouseNumber,
            user.PostalCode,
            user.City));
    }

    /// <summary>
    /// Builds the session payload from an entity. Internal rather than private: ProfileEndpoints
    /// returns the same shape after a save, and a second copy of this projection is exactly how the
    /// two would drift the next time CurrentUser grows a field.
    /// </summary>
    internal static async Task<CurrentUser> BuildCurrentUserAsync(
        ApplicationUser user,
        UserManager<ApplicationUser> userManager)
    {
        var roles = await userManager.GetRolesAsync(user);

        return new CurrentUser(
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            user.Status.ToString(),
            [.. roles],
            user.PhoneNumber,
            user.Street,
            user.HouseNumber,
            user.PostalCode,
            user.City);
    }
}
