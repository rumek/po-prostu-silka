using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Auth;

public record LoginRequest(string Email, string Password);

/// <summary>
/// Registration input. THREE FIELDS, and that is the whole slice (S-17, IR-04): an address to sign
/// in with, a password, and the invitation code that says which member record this account attaches
/// to.
///
/// <para>
/// The display name, the phone number and the four address fields used to arrive here and no longer
/// do. They come from the <see cref="Member"/> the code claims — the club entered that person into
/// its records before handing the code over, so asking them to type it again would only produce a
/// second, competing copy. What the club does not hold, the member fills in later through
/// <c>PUT /api/profile</c>, which is where those five fields stay required.
/// </para>
/// </summary>
/// <param name="MemberCode">
/// REQUIRED since S-17 (IR-05). Registration is claim-only: there is no branch that creates a fresh
/// member record any more, so a request without a code cannot produce an account at all. Not
/// defaulted, so the compiler refuses a caller that forgets it.
/// </param>
public record RegisterRequest(
    string Email,
    string Password,
    string MemberCode);

/// <summary>
/// Why the login failure is named: S-02's blocked members need a different message from a wrong
/// password. Callers must not treat <c>invalid_credentials</c> as "no such account" - it also covers
/// a wrong password.
///
/// <c>pending_approval</c> is unreachable and has been since S-01, which let a pending member sign in
/// rather than refusing them; S-16 then removed the pending state entirely. The literal survives in
/// the SPA's LoginFailureReason union, and removing it there is churn for no gain — nothing sends
/// it.
/// </summary>
public record LoginFailure(string Reason);

/// <summary>Asks for a reset link. The only field is the address to send it to.</summary>
public record ForgotPasswordRequest(string Email);

/// <summary>
/// Sets a new password from an emailed token. The email travels with the token because Identity's
/// tokens are validated against a specific user - the token alone does not identify one.
/// </summary>
public record ResetPasswordRequest(string Email, string Token, string NewPassword);

/// <summary>
/// Why the reset failed.
///
/// <c>invalid_token</c> deliberately covers an unknown address, a malformed token, a token belonging
/// to someone else, an already-used token AND an expired one. Splitting those apart would hand an
/// anonymous caller the account-enumeration oracle that <c>/forgot-password</c> is built to deny -
/// "expired" means the address exists. One code, and the screen says "poproś o nowy link".
/// </summary>
public record ResetPasswordFailure(string Reason);

/// <summary>
/// An in-session password change (S-13). The current password is required and is the whole
/// authorisation for the change - a live cookie alone is not enough, because an unattended session
/// is the exact scenario this guards against.
/// </summary>
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>
/// Why the change failed. Two codes, both 400: <c>invalid_current_password</c> and
/// <c>invalid_new_password</c>. Identity's raw error text is never forwarded - same reasoning as
/// <see cref="RegisterFailure"/>.
///
/// Unlike /login, disclosure is not a concern here: the caller has already proved they own the
/// session, so telling them which of the two passwords was the problem leaks nothing and is the
/// difference between a fixable form and a dead end.
/// </summary>
public record ChangePasswordFailure(string Reason);

/// <summary>
/// Why registration failed. Never echoes Identity's raw error text to the client.
///
/// <para>
/// S-14 adds two. <c>invalid_member_code</c> (400) is a format failure - what was typed could not be
/// a code at all. <c>unknown_member_code</c> (409) covers "no member holds it", "it expired" and "it
/// was revoked" as ONE answer, deliberately: distinguishing them would confirm to a stranger that a
/// code once existed, and the same reasoning already collapses ResetPasswordFailure's four causes
/// into <c>invalid_token</c>. S-17 makes <c>invalid_member_code</c> the answer to a MISSING code too,
/// which is the same thing now that registration is claim-only.
/// </para>
///
/// <para>
/// S-17 also RETIRES five. <c>invalid_display_name</c> and the <see cref="ContactDetails"/> codes -
/// <c>invalid_phone</c> / <c>invalid_street</c> / <c>invalid_house_number</c> /
/// <c>invalid_postal_code</c> / <c>invalid_city</c> - are unreachable from this endpoint, because the
/// fields behind them stopped arriving. They remain the vocabulary of <c>PUT /api/profile</c>, which
/// still requires all five; nothing here produces them.
/// </para>
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
    string? City,
    Guid? MemberId,
    string? MembershipStatus);

/// <summary>
/// The authentication surface: create an account, establish a session, inspect it, refresh it,
/// end it.
///
/// Registration lands here with S-01 (registration-and-approval), whose approval half S-16 removed.
/// What a newly created account may do is therefore everything a member may do — read the schedule,
/// their classes and their karnet — and the one thing it cannot is be booked into a class, because
/// that needs a karnet the club issues at the desk.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", LoginAsync).AllowAnonymous();
        // RATE LIMITED SINCE S-16, and the limiter is not optional garnish: it is what took the
        // approval gate's place as the anti-spam control when registration started producing accounts
        // that work. See RateLimitPolicies.Register.
        group.MapPost("/register", RegisterAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Register);
        group.MapPost("/logout", LogoutAsync).RequireAuthorization();

        // Bare RequireAuthorization(), NEVER the ActiveMember policy - a pending member has to be
        // able to call the one endpoint that stops them being pending.
        group.MapPost("/refresh", RefreshAsync).RequireAuthorization();

        // RequireAuthorization() and NOT the ActiveMember policy. The original reason (a Pending
        // member must be able to read their own status) died with approval; the rule still holds for
        // a BLOCKED session that has not yet been refused a claim, and for /profile, which an account
        // must reach to supply contact details it may not have.
        group.MapGet("/me", GetCurrentUser).RequireAuthorization();

        // Bare RequireAuthorization(), not ActiveMember: a member owns their password whatever their
        // membership says, and nothing about changing it depends on being able to train.
        group.MapPost("/change-password", ChangePasswordAsync).RequireAuthorization();

        // The only anonymous endpoint in this app that sends mail on demand, so it is the only one
        // that carries a rate limit (Program.cs). AllowAnonymous by definition - a member who can
        // sign in does not need it.
        group.MapPost("/forgot-password", ForgotPasswordAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.ForgotPassword);

        // Not rate-limited: it sends nothing, and a wrong token is already refused. The token's
        // single-use guarantee is the security stamp, which ResetPasswordAsync rotates.
        group.MapPost("/reset-password", ResetPasswordAsync).AllowAnonymous();

        return app;
    }

    private static async Task<IResult> LoginAsync(
        [FromBody] LoginRequest request,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IMemberStore members)
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

        // BLOCKED IS THE ONLY REFUSAL LEFT (S-16). There used to be a Pending branch here that let
        // an unapproved member sign in and land on an awaiting-approval screen; nothing produces
        // Pending any more, so the branch was unreachable and is gone. Blocked stays refused -
        // handing a 30-day cookie to someone whose access was revoked inverts what Blocked is for.
        if (user.Status == AccountStatus.Blocked)
        {
            return Results.Json(new LoginFailure("blocked"), statusCode: 401);
        }

        // isPersistent: true is what makes the 30-day window survive closing the browser - without
        // it the cookie is a session cookie and mobile members re-login constantly (PRD FR-002).
        await signInManager.SignInAsync(user, isPersistent: true);

        return Results.Ok(await BuildCurrentUserAsync(user, userManager, members));
    }

    /// <summary>
    /// Creates an ACTIVE account and signs it in immediately (S-16, MP-03).
    ///
    /// <para>
    /// THE ACCOUNT WORKS THE MOMENT IT EXISTS. Approval is gone: a new member reaches the dashboard
    /// straight away and sees the schedule, their (empty) karnet and their (empty) plan. What they
    /// cannot do is train, because being booked into a class requires a karnet the club issues at the
    /// desk - see MembershipPass. The gate moved; it did not disappear.
    /// </para>
    ///
    /// <para>
    /// ASYMMETRY, ON PURPOSE: this endpoint discloses that an email is already registered, while
    /// /login deliberately refuses to distinguish a wrong password from an unknown address. The trade
    /// SURVIVES S-16 but its justification had to be restated, because the old one no longer holds:
    /// it used to be that silence would strand a member waiting forever for an approval that was
    /// never coming, and there is no approval now. What remains is plainer and still decisive - with
    /// no email-confirmation flow in scope, silence tells a member who forgot they had signed up to
    /// keep retrying a password that will never work, with nothing anywhere to explain why. For a
    /// single gym, "this address belongs to a member here" is close to worthless to an attacker.
    /// Do not align the two endpoints without re-deciding that.
    /// </para>
    ///
    /// <para>
    /// RATE LIMITED, per client IP, and that is the anti-spam control now. It used to be the approval
    /// gate itself, whose accepted cost was junk accumulating as Pending rows nobody would approve;
    /// once registration produces a working account that cost changes shape entirely. See
    /// RateLimitPolicies.Register for what the limiter does and does not promise.
    /// </para>
    /// </summary>
    private static async Task<IResult> RegisterAsync(
        [FromBody] RegisterRequest request,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IMemberStore members,
        IMemberQuery memberQuery,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory)
    {
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

        // THE CODE IS RESOLVED BEFORE ANYTHING IS CREATED, and since S-17 it is also REQUIRED: this
        // block sits where the display-name and contact-detail validation used to, so a codeless
        // request still costs one round trip and leaves no account behind.
        //
        // A missing code and a malformed one answer the same 400. They are the same failure now -
        // "what you sent could not be an invitation" - and the register screen never lets either
        // happen, because the field is prefilled from the link and readonly.
        if (!MemberAccessCode.TryNormalise(request.MemberCode, out var normalisedCode))
        {
            return Results.Json(new RegisterFailure("invalid_member_code"), statusCode: 400);
        }

        var claimed = await members.FindByAccessCodeAsync(normalisedCode, CancellationToken.None);

        // ONE ANSWER FOR EVERY CAUSE: no such code, already claimed, expired, revoked, or the
        // member blocked since it was issued. Telling them apart would confirm to a stranger that
        // a code once existed and what became of it - and a block must not be claimable around.
        if (claimed is null
            || claimed.UserId is not null
            || claimed.AccessCodeExpiresAt is null
            || claimed.AccessCodeExpiresAt <= timeProvider.GetUtcNow()
            || claimed.Status != MembershipStatus.Active)
        {
            return Results.Json(new RegisterFailure("unknown_member_code"), statusCode: 409);
        }

        // BOTH TABLES, not just Identity's. Every branch below writes request.Email into a Member row
        // guarded by IX_Members_Email, so an address the club already recorded for someone with no
        // account is unavailable even though no ACCOUNT holds it. Asking userManager alone let that
        // case through to SaveChangesAsync, where the unique violation surfaced as a 500 and the
        // compensating delete undid a real account on every retry - shutting a walk-in out of
        // registering with their own address, which is the flow this slice exists for.
        //
        // exceptMemberId is the member being claimed: their own address must not collide with itself
        // when the code they typed belongs to the row that already holds it.
        if (await memberQuery.EmailExistsAsync(request.Email, claimed.Id, CancellationToken.None))
        {
            return Results.Json(new RegisterFailure("email_taken"), statusCode: 409);
        }

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,

            // THE CLUB'S NAME, NOT A TYPED ONE (S-17). The form stopped asking, so the record is the
            // only source - and it always has one, because the admin surface refuses a blank display
            // name on both create and edit (MemberAdminEndpoints).
            DisplayName = claimed.DisplayName,
            // ACTIVE, not Pending (S-16, MP-03). AccountStatus.Pending is retired rather than
            // removed - its numeric value stays reserved - but nothing produces it any more.
            Status = AccountStatus.Active,
            CreatedAt = timeProvider.GetUtcNow(),

            // THE ADDRESS IS NOT WRITTEN HERE ANY MORE (S-14 Phase 8). It lives on the Member, which
            // is the only place it can live once a person may exist without an account at all; the
            // four columns on AspNetUsers go in the next release. PhoneNumber stays because it is
            // Identity's own column and does not go anywhere — see ProfileEndpoints for why keeping
            // it in step is worth one assignment.
            //
            // Copied FROM the claimed record since S-17, and null when the club never took one. The
            // member supplies it through /profile, which is the one place that still asks.
            PhoneNumber = claimed.PhoneNumber,
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

        // THE CLUB'S OWN RECORD OF THIS PERSON (S-14), linked to the account just created.
        //
        // Registration no longer PRODUCES a member - S-17 left it only able to link one - so the
        // producers that keep "an account with no member" unreachable are now the admin surface, the
        // AddMembers backfill and AdminSeeder. The guarantee itself is unchanged and still
        // load-bearing: the membership claim refuses an account without a member everywhere it is
        // checked, and the code required above is precisely what supplies one here.
        //
        // A SECOND SAVE, not part of CreateAsync's. Identity commits the account itself, so there is no
        // way to make these one write short of an explicit transaction - and an explicit transaction
        // here would have to run through Database.CreateExecutionStrategy().ExecuteAsync, because
        // EnableRetryOnFailure is on (Program.cs) and throws at RUNTIME otherwise. The compensation
        // below is the cheaper answer, and it mirrors what the role-assignment failure already does.
        // CLAIMING, NOT CREATING, and since S-17 there is no other branch: this account attaches to
        // the record the club has been keeping, so the bookings and the active plan already pointing
        // at it are simply there when the member first signs in. The branch that used to make a fresh
        // Member for a codeless registration is gone with the codeless registration itself.
        claimed.UserId = user.Id;
        claimed.ClaimedAt = user.CreatedAt;

        // Consumed. Nulling the code is what makes it single-use - there is no separate "used"
        // flag to forget to set - and the expiry goes with it, for the reason revoke gives.
        claimed.AccessCode = null;
        claimed.AccessCodeExpiresAt = null;

        // THE EMAIL IS FILLED IN, NEVER REPLACED, and this is the one contact field registration
        // still writes. It is what gives a record entered at the desk without an address - the case
        // this slice exists for - the login address as its contact; where the club already recorded
        // one, Identity's copy is authoritative and the member's own stands.
        //
        // The phone and the four address fields USED TO OVERWRITE here, on the argument that the
        // person is the better source for their own details. They stopped arriving with S-17, so the
        // club's copy is now the only one there is. Anything it lacks the member supplies through
        // /profile, which profile.html already prompts for.
        claimed.Email ??= request.Email;
        claimed.ConcurrencyStamp = Guid.NewGuid().ToString();

        try
        {
            // NOT TrySaveChangesAsync, which would swallow the one failure that matters here. The
            // unique index on Members.UserId is what settles two people racing the same code: the
            // loser's UPDATE is rejected, this throws, and the compensating delete below removes their
            // account - leaving the code consumed by the winner and the loser free to register
            // normally.
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Same reasoning as the role-assignment rollback above: an account with no member record is
            // unrecoverable through anything this endpoint ships - it would fail every policy, and
            // nothing in the admin surface creates a member for an existing account. Undo the
            // registration and let them retry rather than leave an account that can be approved and
            // still cannot do anything.
            var memberLogger = loggerFactory.CreateLogger(typeof(AuthEndpoints));
            memberLogger.LogError(
                ex,
                "Member record could not be linked for new user {UserId}; deleting the account.",
                user.Id);

            // DISCARD FIRST, OR THE COMPENSATION SILENTLY FAILS. The failed save left the rejected
            // change tracked, so DeleteAsync's own SaveChanges would re-send it, hit the same
            // violation, and throw - leaving exactly the orphaned account this block exists to remove.
            // The account itself is already committed (CreateAsync saved it), so it is re-read rather
            // than reused: the instance we hold belongs to the graph just thrown away.
            unitOfWork.DiscardChanges();

            var orphan = await userManager.FindByIdAsync(user.Id);
            if (orphan is not null)
            {
                await userManager.DeleteAsync(orphan);
            }

            return Results.Problem("Registration could not be completed.", statusCode: 500);
        }

        // Signed in immediately, and persistent for the same reason login is: the member closes the
        // tab, waits hours for approval, and must not have to re-enter credentials to check.
        await signInManager.SignInAsync(user, isPersistent: true);

        // Same shape /login returns, so the SPA has one code path for "you now have a session".
        return Results.Ok(await BuildCurrentUserAsync(user, userManager, members));
    }

    /// <summary>
    /// Re-mints the caller's claims from their current row, without ending the session.
    ///
    /// Why this exists: the ActiveMember/Admin policies read the account_status CLAIM from the
    /// cookie, not the database (AuthorizationPolicies), and that claim is re-minted only when the
    /// security-stamp validator refreshes - on the interval set in Program.cs, which is the one
    /// place that number is stated. So a status or role change made by the admin does not reach the
    /// cookie until that interval elapses, while /me (which reads the database) reports it at once —
    /// and the two disagreeing is what this endpoint exists to resolve.
    ///
    /// Since S-16 the change that matters here is a BLOCK, and the staleness runs the dangerous way:
    /// the cookie is PERMISSIVE while the database is not. A block through POST /{id}/block also
    /// rotates the security stamp, which forces re-validation on its own — this endpoint is what
    /// covers every other path, and what a client calls when it wants its claims current now.
    ///
    /// RefreshSignInAsync re-runs AppUserClaimsPrincipalFactory against the current entity, so status
    /// and roles are both corrected in one round-trip. It is safe to call whatever the caller's
    /// claims currently say, which is why it carries a bare RequireAuthorization().
    /// </summary>
    private static async Task<IResult> RefreshAsync(
        ClaimsPrincipal principal,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IMemberStore members)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        await signInManager.RefreshSignInAsync(user);
        return Results.Ok(await BuildCurrentUserAsync(user, userManager, members));
    }

    /// <summary>
    /// Replaces the caller's password, keeping the session they are using and killing every other.
    ///
    /// <para>
    /// REFRESHSIGNINASYNC IS LOAD-BEARING, NOT TIDINESS. ChangePasswordAsync rotates the security
    /// stamp, which invalidates every cookie for this user - the caller's included. The validator
    /// re-checks on the interval set in Program.cs, so without the refresh the member who just
    /// changed their password is silently signed out a couple of minutes later, on a screen that
    /// told them it worked. The refresh re-issues THIS cookie against the new stamp; every other
    /// session still dies on its own next check, which is exactly what a password change should do.
    /// </para>
    /// </summary>
    private static async Task<IResult> ChangePasswordAsync(
        [FromBody] ChangePasswordRequest request,
        ClaimsPrincipal principal,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        // A JSON null reaches here despite the non-nullable record - the same compile-time-only
        // contract LoginAsync and RegisterAsync guard against. ChangePasswordAsync would throw on a
        // null current password rather than answer.
        if (string.IsNullOrEmpty(request.CurrentPassword))
        {
            return Results.Json(
                new ChangePasswordFailure("invalid_current_password"), statusCode: 400);
        }

        if (string.IsNullOrEmpty(request.NewPassword))
        {
            return Results.Json(new ChangePasswordFailure("invalid_new_password"), statusCode: 400);
        }

        var changed = await userManager.ChangePasswordAsync(
            user, request.CurrentPassword, request.NewPassword);

        if (!changed.Succeeded)
        {
            // PasswordMismatch is what a wrong CURRENT password produces; everything else here is
            // the policy refusing the NEW one. Mapped rather than forwarded, for the reason
            // RegisterAsync gives: the raw descriptions are English and unlocalised.
            var reason = changed.Errors.Any(e =>
                e.Code.Equals("PasswordMismatch", StringComparison.Ordinal))
                ? "invalid_current_password"
                : "invalid_new_password";

            return Results.Json(new ChangePasswordFailure(reason), statusCode: 400);
        }

        // AFTER the change and BEFORE the response - see the summary. Moving or removing this line
        // does not fail a build or a unit test; it fails two minutes later, in production.
        await signInManager.RefreshSignInAsync(user);

        return Results.NoContent();
    }

    /// <summary>
    /// Emails a reset link, if the address belongs to an account.
    ///
    /// <para>
    /// THIS ENDPOINT ANSWERS THE SAME THING NO MATTER WHAT. 200, empty body, for a registered
    /// address, an unregistered one, a Pending account, a Blocked account, a throttled repeat and a
    /// misconfigured BaseUrl alike. Every branch below returns <c>Results.Ok()</c>, because a
    /// difference in status code or body is an account-enumeration oracle. F-02's implementation
    /// review flagged exactly this shape on /login
    /// (context/archive/2026-08-31-auth-identity-foundation/reviews/impl-review.md:93-101).
    /// </para>
    ///
    /// <para>
    /// WHAT IS NOT EQUALISED: LATENCY. The unknown-address and throttled branches return early,
    /// before the token mint, the render and the commit that the found-user path performs, so a
    /// registered address costs measurably more than an unregistered one. That is a known, accepted
    /// gap - the plan asked for comparable work and this does not deliver it (see the "Adapted
    /// during implementation." note on Phase 4 item 6 in
    /// context/changes/member-profile-edit/plan.md). Exploiting it needs many samples through a
    /// 5-per-minute cap and hosting jitter; equalising it would mean holding an anonymous request on
    /// a thread for a fixed budget, on the one endpoint an anonymous caller can spam. Do not
    /// "restore" the guarantee by deleting this paragraph - either close the gap or leave both.
    /// </para>
    ///
    /// <para>
    /// ASYMMETRY WITH /register, ON PURPOSE. Registration discloses <c>email_taken</c> because
    /// silence would strand a real member. Here silence strands nobody: someone who mistypes their
    /// address simply gets no email and tries again. This endpoint sits on /login's side of that
    /// line.
    /// </para>
    ///
    /// <para>
    /// STATUS IS NOT CHECKED. A Blocked member can request a reset and use it. Branching on status
    /// would reintroduce the oracle from the other direction, and a blocked account is refused at
    /// /login regardless - a new password gets them nothing.
    /// </para>
    /// </summary>
    private static async Task<IResult> ForgotPasswordAsync(
        [FromBody] ForgotPasswordRequest request,
        UserManager<ApplicationUser> userManager,
        IPasswordResetNotification notification,
        IPasswordResetThrottle throttle,
        IUnitOfWork unitOfWork,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return Results.Ok();
        }

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return Results.Ok();
        }

        // Consumes the window. A refused attempt still falls through to the same Ok() below - the
        // throttle decides what is SENT, never what is ANSWERED.
        if (!throttle.TryAcquire(request.Email))
        {
            return Results.Ok();
        }

        // SWALLOWED ON PURPOSE - the one place in this app where that is correct. There is no
        // UseExceptionHandler here, so an unhandled throw would answer 500 for a registered address
        // while an unregistered one still answered 200: the enumeration oracle this endpoint exists
        // to deny, appearing exactly under the load that makes it easiest to measure. SQL throttling
        // on Basic DTU is the realistic trigger. The member gets no email and retries; nobody learns
        // anything from the response.
        try
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            notification.Notify(user, token);

            // Notify enqueues without saving, like every other notification here, so the outbox row
            // only exists after this commit.
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            loggerFactory
                .CreateLogger(typeof(AuthEndpoints))
                .LogError(
                    exception,
                    "Password reset could not be queued. The caller was answered normally so the "
                    + "failure does not disclose whether the address is registered.");
        }

        return Results.Ok();
    }

    /// <summary>
    /// Consumes a reset token and sets the new password.
    ///
    /// <para>
    /// SINGLE USE COMES FROM THE SECURITY STAMP, NOT FROM ANYTHING HERE. ResetPasswordAsync rotates
    /// the stamp, and the token was generated against the old one, so replaying the same link fails
    /// validation the second time. Nothing marks the token as used, and nothing needs to.
    /// </para>
    ///
    /// <para>
    /// The member is deliberately NOT signed in on success. They are sent to /login, which both
    /// proves the new password works and is the only thing an existing session of theirs - now
    /// invalidated by the same stamp rotation - could sensibly do next.
    /// </para>
    /// </summary>
    private static async Task<IResult> ResetPasswordAsync(
        [FromBody] ResetPasswordRequest request,
        UserManager<ApplicationUser> userManager)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Token))
        {
            return Results.Json(new ResetPasswordFailure("invalid_token"), statusCode: 400);
        }

        if (string.IsNullOrEmpty(request.NewPassword))
        {
            return Results.Json(new ResetPasswordFailure("invalid_new_password"), statusCode: 400);
        }

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            // invalid_token, NOT a distinct "no such account" - see the comment on
            // ResetPasswordFailure. The caller holding a link for an address that does not exist is
            // indistinguishable from one holding a bad token, and must stay that way.
            return Results.Json(new ResetPasswordFailure("invalid_token"), statusCode: 400);
        }

        var reset = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!reset.Succeeded)
        {
            // InvalidToken covers malformed, expired and already-used. Everything else here is the
            // password policy refusing the new password, which the member CAN act on.
            var reason = reset.Errors.Any(e =>
                e.Code.Equals("InvalidToken", StringComparison.Ordinal))
                ? "invalid_token"
                : "invalid_new_password";

            return Results.Json(new ResetPasswordFailure(reason), statusCode: 400);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> LogoutAsync(SignInManager<ApplicationUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> GetCurrentUser(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        IMemberStore members)
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

        // The MEMBERSHIP half does cost a read, unlike the roles above, and deliberately: it lives on
        // a different row, and taking it from the cookie's claim would make /me report a status that
        // is up to one validation interval stale — which is exactly the staleness this endpoint
        // exists to let the SPA see past.
        var member = await members.FindByUserIdAsync(user.Id, CancellationToken.None);

        return Results.Ok(new CurrentUser(
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            user.Status.ToString(),
            roles,

            // FROM THE MEMBER SINCE S-14 PHASE 8. The account's copies are frozen and the columns go
            // in the next release; reading them here would resurrect an address the member corrected
            // through their profile.
            member?.PhoneNumber,
            member?.Street,
            member?.HouseNumber,
            member?.PostalCode,
            member?.City,
            member?.Id,
            member?.Status.ToString()));
    }

    /// <summary>
    /// Builds the session payload from an entity. Internal rather than private: ProfileEndpoints
    /// returns the same shape after a save, and a second copy of this projection is exactly how the
    /// two would drift the next time CurrentUser grows a field.
    ///
    /// <para>
    /// <c>MemberId</c> and <c>MembershipStatus</c> are NULLABLE on the wire and must stay that way.
    /// They are null exactly when the account has no member row — the state the claims factory treats
    /// as a tripwire — and the SPA reads that as "signed in but unusable" rather than inventing a
    /// status. Filling them in with a default here would hide the same failure the policies exist to
    /// surface.
    /// </para>
    /// </summary>
    internal static async Task<CurrentUser> BuildCurrentUserAsync(
        ApplicationUser user,
        UserManager<ApplicationUser> userManager,
        IMemberStore members)
    {
        var roles = await userManager.GetRolesAsync(user);
        var member = await members.FindByUserIdAsync(user.Id, CancellationToken.None);

        return new CurrentUser(
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            user.Status.ToString(),
            [.. roles],

            // From the MEMBER since S-14 Phase 8 — see GetCurrentUser for why.
            member?.PhoneNumber,
            member?.Street,
            member?.HouseNumber,
            member?.PostalCode,
            member?.City,
            member?.Id,
            member?.Status.ToString());
    }
}
