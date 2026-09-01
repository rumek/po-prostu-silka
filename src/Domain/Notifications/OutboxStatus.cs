namespace po_prostu_silka.Domain.Notifications;

/// <summary>
/// The outbox row lifecycle. The worker moves rows between these states; nothing else should.
///
/// Explicit numeric values, and do not reorder: this persists as an int, so renumbering would
/// silently reinterpret every existing row. <see cref="Pending"/> is 0 so a row inserted without an
/// explicit status is queued rather than accidentally considered delivered.
/// </summary>
public enum OutboxStatus
{
    /// <summary>Queued. Eligible for claiming once NextAttemptAt has passed.</summary>
    Pending = 0,

    /// <summary>Leased by a worker pass. A stale ClaimedAt returns the row to Pending - that is what
    /// stops an App Service recycle mid-send from stranding it forever.</summary>
    Claimed = 1,

    /// <summary>Handed to the provider successfully. Pruned after the retention window.</summary>
    Sent = 2,

    /// <summary>Terminal. Either a permanent failure or the attempt cap was exhausted. Never pruned,
    /// and counted by the /health check - this state is the delivery-failure signal.</summary>
    Failed = 3,
}
