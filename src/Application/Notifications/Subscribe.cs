using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Domain;

namespace po_prostu_silka.Application.Notifications;

/// <summary>
/// Registers one browser endpoint for Web Push against the calling account.
/// </summary>
public static class Subscribe
{
    public static async Task<IResult> HandleAsync(
        [FromBody] SubscribeRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        IPushSubscriptionStore store,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(principal);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Endpoint)
            || string.IsNullOrWhiteSpace(request.P256dh)
            || string.IsNullOrWhiteSpace(request.Auth))
        {
            return Results.BadRequest();
        }

        // Upsert, not insert. A browser re-issues the same endpoint when it re-subscribes, so
        // inserting blindly would accumulate duplicates and fan out duplicate push messages.
        await store.UpsertAsync(
            userId, request.Endpoint, request.P256dh, request.Auth,
            timeProvider.GetUtcNow(), cancellationToken);

        return Results.NoContent();
    }
}
