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
/// The signed-in account as the SPA models it.
/// </summary>
public static class GetCurrentUser
{
    public static async Task<IResult> HandleAsync(
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
}
