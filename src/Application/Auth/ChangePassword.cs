using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Application.Persistence;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Application.Auth;

/// <summary>
/// Changes the password of the signed-in account (FR-025).
/// </summary>
public static class ChangePassword
{
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
    public static async Task<IResult> HandleAsync(
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
}
