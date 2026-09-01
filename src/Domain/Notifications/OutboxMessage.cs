namespace po_prostu_silka.Domain.Notifications;

/// <summary>
/// One queued message: a single recipient, a single channel, already rendered.
///
/// The rendering happens at enqueue time, not at send time. That is deliberate - the worker delivers
/// bytes and never re-renders, so attempt 3 says exactly what attempt 1 said even if the underlying
/// data changed in between. It also means a partial failure has somewhere to record itself: 8 of 20
/// recipients delivered is 8 rows Sent and 12 still Pending, rather than one ambiguous row.
///
/// infrastructure.md:79 is why this table exists at all: App Service recycles without warning, so a
/// fire-and-forget send loop silently drops whatever was in flight.
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; set; }

    public NotificationChannel Channel { get; set; }

    /// <summary>Email address for <see cref="NotificationChannel.Email"/>; the owning user id for push.</summary>
    public string Recipient { get; set; } = string.Empty;

    /// <summary>Email subject, or the push notification title.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Rendered email body, or the push payload.</summary>
    public string Body { get; set; } = string.Empty;

    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;

    public int AttemptCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When this row next becomes eligible to claim. Exponential backoff moves this forward.</summary>
    public DateTimeOffset NextAttemptAt { get; set; }

    /// <summary>
    /// When the current lease was taken. Null unless Claimed. A ClaimedAt older than the lease
    /// timeout means the worker that took it died - the row returns to Pending and is retried.
    /// </summary>
    public DateTimeOffset? ClaimedAt { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    /// <summary>Last provider error, for diagnosing Failed rows. Never contains a secret.</summary>
    public string? LastError { get; set; }
}
