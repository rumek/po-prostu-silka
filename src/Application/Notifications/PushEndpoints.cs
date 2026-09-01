using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Notifications;

namespace po_prostu_silka.Application.Notifications;

public record SubscribeRequest(string Endpoint, string P256dh, string Auth);

public record VapidKeyResponse(string PublicKey);

/// <summary>
/// Lets a browser register itself for Web Push.
///
/// Everything here uses bare RequireAuthorization(), NOT the ActiveMember policy: a Pending member's
/// device may subscribe before approval, and the account-approved notification is precisely the
/// message they are waiting for.
/// </summary>
public static class PushEndpoints
{
    public static IEndpointRouteBuilder MapPushEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/push").WithTags("Push").RequireAuthorization();

        group.MapGet("/vapid-key", GetVapidKey);
        group.MapPost("/subscribe", SubscribeAsync);
        group.MapPost("/unsubscribe", UnsubscribeAsync);

        return app;
    }

    /// <summary>
    /// The application server's public key. Public by design — the browser needs it to subscribe.
    /// Authenticated anyway, because nothing anonymous needs it and a uniform surface is simpler.
    /// </summary>
    private static IResult GetVapidKey(IVapidPublicKey key) =>
        string.IsNullOrWhiteSpace(key.PublicKey)
            ? Results.Problem("Push is not configured on this server.", statusCode: 503)
            : Results.Ok(new VapidKeyResponse(key.PublicKey));

    private static async Task<IResult> SubscribeAsync(
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

    private static async Task<IResult> UnsubscribeAsync(
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

/// <summary>Exposes the VAPID public key without Application referencing the options type directly.</summary>
public interface IVapidPublicKey
{
    string PublicKey { get; }
}

/// <summary>
/// Narrow seam over the subscription table, so Application does not reference EF Core
/// (AGENTS.md layering). Implemented in Infrastructure.
/// </summary>
public interface IPushSubscriptionStore
{
    Task UpsertAsync(
        string userId, string endpoint, string p256dh, string auth,
        DateTimeOffset now, CancellationToken cancellationToken);

    Task RemoveAsync(string userId, string endpoint, CancellationToken cancellationToken);

    Task<IReadOnlyList<PushSubscription>> GetForUserAsync(
        string userId, CancellationToken cancellationToken);
}
