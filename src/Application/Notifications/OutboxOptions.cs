namespace po_prostu_silka.Application.Notifications;

/// <summary>
/// Knobs for the delivery worker. Defaults are the values the F-03 plan settled on; they are
/// configurable so they can be tightened in tests (which cannot wait 15 seconds per pass) without
/// changing behaviour in production.
/// </summary>
public class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>
    /// How often the worker looks for work. The NFR asks for "within minutes, not hours", so this
    /// has enormous margin — it is deliberately not tuned for latency, because a shorter interval
    /// only adds queries against a 5-DTU tier.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Rows claimed per pass. Bounds the blast radius of a recycle: everything claimed and not yet
    /// sent has to wait out the lease before another pass retries it. S-05 will fan out to every
    /// booked member of a class, so this is the number to revisit if delivery feels slow.
    /// </summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>
    /// How long a claim is honoured before another pass may steal it. Must comfortably exceed the
    /// slowest plausible send; too short and two workers send the same message, too long and a
    /// recycle strands rows for that duration.
    /// </summary>
    public TimeSpan LeaseTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Attempts before a transient failure becomes terminal.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// Backoff schedule, indexed by AttemptCount. Rides out a provider incident of real length
    /// rather than burning all attempts inside a few minutes.
    /// </summary>
    public TimeSpan[] BackoffSchedule { get; set; } =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(4),
    ];

    /// <summary>
    /// How long delivered rows are kept. Bounds growth against the 2GB Basic cap that F-01 flagged
    /// for this change, while still answering "did the member get last week's cancellation?".
    /// Failed rows are never pruned — they are the diagnostic record.
    /// </summary>
    public TimeSpan SentRetention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>How often the prune sweep runs. Far less often than the send loop needs to.</summary>
    public TimeSpan PruneInterval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>Failed-row count at which /health reports Degraded.</summary>
    public int FailedThreshold { get; set; } = 10;
}
