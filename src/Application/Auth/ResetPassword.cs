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
/// Completes a password reset against a minted token (FR-026).
/// </summary>
public static class ResetPassword
{
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
    public static async Task<IResult> HandleAsync(
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
}
