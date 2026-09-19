using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Domain;

namespace po_prostu_silka.Application.Notifications;

/// <summary>
/// Removes one browser endpoint's Web Push registration.
/// </summary>
public static class Unsubscribe
{
    public static async Task<IResult> HandleAsync(
        [FromBody] SubscribeRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        IPushSubscriptionStore store,
        CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(principal);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        // Scoped to the caller, so one member cannot delete another's subscription by guessing an
        // endpoint.
        await store.RemoveAsync(userId, request.Endpoint, cancellationToken);
        return Results.NoContent();
    }
}
