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
}

/// <param name="Outcome">What happened.</param>
/// <param name="Error">Provider detail for diagnostics. Never contains a secret.</param>
public readonly record struct DeliveryResult(DeliveryOutcome Outcome, string? Error = null)
{
    public static DeliveryResult Success() => new(DeliveryOutcome.Success);

    public static DeliveryResult Transient(string error) => new(DeliveryOutcome.Transient, error);

    public static DeliveryResult Permanent(string error) => new(DeliveryOutcome.Permanent, error);

    public static DeliveryResult SubscriptionGone(string error) => new(DeliveryOutcome.SubscriptionGone, error);
}
