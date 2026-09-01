using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Domain.Notifications;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.Notifications;

/// <summary>Infrastructure side of <see cref="IPushSubscriptionStore"/>.</summary>
public class PushSubscriptionStore(AppDbContext db) : IPushSubscriptionStore
{
    public async Task UpsertAsync(
        string userId, string endpoint, string p256dh, string auth,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Endpoint is uniquely indexed, so this is the whole reason subscribe is idempotent.
        // Re-subscribing from the same browser rotates the keys rather than adding a row.
        var existing = await db.PushSubscriptions
            .FirstOrDefaultAsync(s => s.Endpoint == endpoint, cancellationToken);

        if (existing is not null)
        {
            existing.UserId = userId;
            existing.P256dh = p256dh;
            existing.Auth = auth;
        }
        else
        {
            db.PushSubscriptions.Add(new PushSubscription
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Endpoint = endpoint,
                P256dh = p256dh,
                Auth = auth,
                CreatedAt = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(string userId, string endpoint, CancellationToken cancellationToken) =>
        await db.PushSubscriptions
            .Where(s => s.UserId == userId && s.Endpoint == endpoint)
            .ExecuteDeleteAsync(cancellationToken);

    public async Task<IReadOnlyList<PushSubscription>> GetForUserAsync(
        string userId, CancellationToken cancellationToken) =>
        await db.PushSubscriptions
            .Where(s => s.UserId == userId)
            .ToListAsync(cancellationToken);
}

/// <summary>Surfaces the configured VAPID public key to the endpoint layer.</summary>
public class VapidPublicKeyProvider(IOptions<VapidOptions> options) : IVapidPublicKey
{
    public string PublicKey { get; } = options.Value.PublicKey;
}
