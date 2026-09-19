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
/// Re-issues the session cookie so a changed role or status reaches the caller.
/// </summary>
public static class RefreshSession
{
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
    public static async Task<IResult> HandleAsync(
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
        return Results.Ok(await CurrentUserBuilder.BuildCurrentUserAsync(user, userManager, members));
    }
}
