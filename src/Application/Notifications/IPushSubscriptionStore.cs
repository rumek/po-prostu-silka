using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Notifications;

namespace po_prostu_silka.Application.Notifications;

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
