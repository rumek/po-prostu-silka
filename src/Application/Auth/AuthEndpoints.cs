using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Domain;

namespace po_prostu_silka.Application.Auth;

public record LoginRequest(string Email, string Password);

/// <summary>
/// Why the login failure is named: S-01 renders the awaiting-approval screen off
/// <c>pending_approval</c>, and S-02's blocked members need a different message. Callers must not
/// treat <c>invalid_credentials</c> as "no such account" - it also covers a wrong password.
/// </summary>
public record LoginFailure(string Reason);

public record CurrentUser(string Id, string Email, string DisplayName, string Status, string[] Roles);

/// <summary>
/// The authentication surface: establish a session, inspect it, end it.
///
/// Registration is deliberately absent - S-01 (registration-and-approval) owns it, because it also
/// owns the approval semantics that decide what a newly created account is allowed to do.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", LoginAsync).AllowAnonymous();
        group.MapPost("/logout", LogoutAsync).RequireAuthorization();

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
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return Results.Json(new LoginFailure("invalid_credentials"), statusCode: 401);
        }

        // Password is checked BEFORE status, deliberately. Reporting "pending" or "blocked" to
        // someone who has not proved they own the account would leak both that the address is
        // registered and what state it is in.
        var passwordResult = await signInManager.CheckPasswordSignInAsync(
            user, request.Password, lockoutOnFailure: true);

        if (!passwordResult.Succeeded)
        {
            return Results.Json(new LoginFailure("invalid_credentials"), statusCode: 401);
        }

        if (user.Status != AccountStatus.Active)
        {
            var reason = user.Status == AccountStatus.Pending ? "pending_approval" : "blocked";
            return Results.Json(new LoginFailure(reason), statusCode: 401);
        }

        // isPersistent: true is what makes the 30-day window survive closing the browser - without
        // it the cookie is a session cookie and mobile members re-login constantly (PRD FR-002).
        await signInManager.SignInAsync(user, isPersistent: true);

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
            roles));
    }

    private static async Task<CurrentUser> BuildCurrentUserAsync(
        ApplicationUser user,
        UserManager<ApplicationUser> userManager)
    {
        var roles = await userManager.GetRolesAsync(user);

        return new CurrentUser(
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            user.Status.ToString(),
            [.. roles]);
    }
}
