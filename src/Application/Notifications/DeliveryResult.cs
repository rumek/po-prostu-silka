namespace po_prostu_silka.Application.Notifications;

/// <summary>
/// How a delivery attempt ended. The worker's entire retry decision rests on this distinction, so it
/// is part of the channel contract rather than something inferred from exception types — an adapter
/// knows whether a failure is worth retrying, and the worker does not.
/// </summary>
public enum DeliveryOutcome
{
    /// <summary>Handed to the provider. Mark Sent.</summary>
    Success = 0,

    /// <summary>Throttle, timeout, 5xx. Retry with backoff until the attempt cap.</summary>
    Transient = 1,

    /// <summary>Rejected address, malformed payload. Mark Failed now — retrying only burns quota.</summary>
    Permanent = 2,

    /// <summary>
    /// Push only: 404/410 from the push service. The subscription is dead, so the worker deletes it
    /// and still marks the message Sent — push is best-effort and email is the guaranteed channel.
    /// </summary>
    SubscriptionGone = 3,

    /// <summary>
    /// Deliberately not sent, and never will be: a push too old to be news, or an address on a
    /// reserved domain that cannot exist. Mark Sent with the reason — it is neither a failure to
    /// retry nor one for the count /health reports.
    /// </summary>
    Dropped = 4,

    /// <summary>
    /// The provider said "not now" (429) and named when. Wait that long and try again WITHOUT
    /// counting an attempt: a throttle says nothing about the message, and counting it would
    /// dead-letter real mail after a few hours of an over-quota sender. A backlog this leaves is
    /// what /health's undelivered-age check reports.
    /// </summary>
    Throttled = 5,
}

/// <param name="Outcome">What happened.</param>
/// <param name="Error">Provider detail for diagnostics. Never contains a secret.</param>
/// <param name="RetryAfter">Only for <see cref="DeliveryOutcome.Throttled"/>: how long to wait.</param>
public readonly record struct DeliveryResult(
    DeliveryOutcome Outcome, string? Error = null, TimeSpan? RetryAfter = null)
{
    public static DeliveryResult Success() => new(DeliveryOutcome.Success);

    public static DeliveryResult Transient(string error) => new(DeliveryOutcome.Transient, error);

    public static DeliveryResult Permanent(string error) => new(DeliveryOutcome.Permanent, error);

    public static DeliveryResult SubscriptionGone(string error) => new(DeliveryOutcome.SubscriptionGone, error);

    public static DeliveryResult Dropped(string reason) => new(DeliveryOutcome.Dropped, reason);

    public static DeliveryResult Throttled(string error, TimeSpan retryAfter) =>
        new(DeliveryOutcome.Throttled, error, retryAfter);
}
