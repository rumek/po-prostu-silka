namespace po_prostu_silka.Domain.Notifications;

/// <summary>
/// A browser's Web Push subscription. One member may have several - a phone and a laptop are
/// separate subscriptions with separate endpoints and keys.
///
/// A subscription can die at any time (permissions revoked, browser data cleared, the push service
/// expiring it). The push service reports that as 404/410 on the next send, and the worker deletes
/// the row rather than retrying it forever.
/// </summary>
public class PushSubscription
{
    public Guid Id { get; set; }

    /// <summary>FK to AspNetUsers. Cascade-deleted with the member.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// The push service URL this browser listens on. Unique: a browser re-issues the same endpoint
    /// when it re-subscribes, and that uniqueness is what makes subscribe an upsert rather than a
    /// source of duplicates.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Client public key for payload encryption (base64url).</summary>
    public string P256dh { get; set; } = string.Empty;

    /// <summary>Client auth secret for payload encryption (base64url).</summary>
    public string Auth { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public ApplicationUser? User { get; set; }
}
