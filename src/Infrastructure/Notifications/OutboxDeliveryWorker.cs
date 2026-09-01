using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Domain.Notifications;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.Notifications;

/// <summary>
/// Drains the outbox. This is the piece infrastructure.md:79 demands: App Service recycles without
/// warning, so a fire-and-forget send loop silently drops whatever was in flight, and the
/// "no missed cancellations" guardrail depends on that not happening.
///
/// Delivery is AT-LEAST-ONCE, deliberately. A crash between "the provider accepted the message" and
/// "the row is marked Sent" resends on the next lease expiry. Duplicating a cancellation email is
/// acceptable; losing one is not. Do not "fix" this into at-most-once.
/// </summary>
public class OutboxDeliveryWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxDeliveryWorker> logger) : BackgroundService
{
    private readonly OutboxOptions _options = options.Value;
    private DateTimeOffset _lastPrune = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Outbox worker started (interval {Interval}s, batch {Batch}, lease {Lease}m).",
            _options.PollInterval.TotalSeconds, _options.BatchSize, _options.LeaseTimeout.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunPassAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never let a pass kill the host. An unhandled exception in a BackgroundService
                // tears the whole process down - the same failure class as F-02's unguarded seeder.
                logger.LogError(ex, "Outbox pass failed; continuing.");
            }

            try
            {
                await Task.Delay(_options.PollInterval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Outbox worker stopping.");
    }

    /// <summary>One full pass. Public so tests can drive it deterministically instead of waiting.</summary>
    public async Task RunPassAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        var pushSender = scope.ServiceProvider.GetRequiredService<IPushSender>();

        var now = timeProvider.GetUtcNow();

        await ReclaimStaleLeasesAsync(db, now, cancellationToken);

        var claimed = await ClaimBatchAsync(db, now, cancellationToken);
        foreach (var message in claimed)
        {
            try
            {
                await DeliverAsync(db, message, emailSender, pushSender, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Isolate the batch. Without this, a throw on one message (a transient SQL throttle
                // during SaveChangesAsync is the likely candidate on a 5-DTU tier) aborts the whole
                // pass, and every message after it stays Claimed and undelivered until the lease
                // expires minutes later - rather than being retried on the next pass seconds later.
                logger.LogError(ex, "Outbox {Id} threw during delivery; continuing the batch.", message.Id);
            }
        }

        // Prune and the full status aggregate share a cadence deliberately. Counting every pass
        // would scan the whole table every 15 seconds forever, and the cost grows fastest exactly
        // when delivery is failing - Failed rows are never pruned - which is when you least want
        // the diagnostic query itself to be expensive.
        var withCounts = now - _lastPrune >= _options.PruneInterval;
        if (withCounts)
        {
            await PruneAsync(db, now, cancellationToken);
            _lastPrune = now;
        }

        await HeartbeatAsync(db, claimed.Count, withCounts, cancellationToken);
    }

    /// <summary>
    /// Returns rows whose worker died mid-send. Without this, every recycle permanently strands
    /// whatever was in flight - the exact failure the outbox exists to prevent.
    /// </summary>
    private async Task ReclaimStaleLeasesAsync(
        AppDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now - _options.LeaseTimeout;

        var reclaimed = await db.OutboxMessages
            .Where(m => m.Status == OutboxStatus.Claimed && m.ClaimedAt != null && m.ClaimedAt < cutoff)
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.Status, OutboxStatus.Pending)
                      .SetProperty(m => m.ClaimedAt, (DateTimeOffset?)null)
                      .SetProperty(m => m.ClaimToken, (Guid?)null),
                cancellationToken);

        if (reclaimed > 0)
        {
            logger.LogWarning(
                "Reclaimed {Count} outbox row(s) from an abandoned lease.", reclaimed);
        }
    }

    /// <summary>
    /// Claims a bounded batch for this pass.
    ///
    /// Two app instances coexist briefly during every deploy, so two things must hold: no row may be
    /// claimed twice, and this pass must be able to tell which rows IT claimed. The atomic
    /// ExecuteUpdate gives the first - a losing instance matches zero Pending rows. The per-pass
    /// ClaimToken gives the second. Correlating the read-back on a timestamp instead would be
    /// unsound: two instances can produce the same one, and the loser would then deliver the
    /// winner's batch.
    /// </summary>
    private async Task<List<OutboxMessage>> ClaimBatchAsync(
        AppDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // A GUID, not the timestamp: two instances polling on the same schedule can produce
        // identical timestamps, and the loser's read-back would then adopt the winner's rows.
        var claimToken = Guid.NewGuid();

        var eligibleIds = await db.OutboxMessages
            .Where(m => m.Status == OutboxStatus.Pending && m.NextAttemptAt <= now)
            .OrderBy(m => m.NextAttemptAt)
            .Take(_options.BatchSize)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

        if (eligibleIds.Count == 0)
        {
            return [];
        }

        // Two guards, and both are needed. The Status == Pending predicate re-checked inside the
        // UPDATE stops a losing instance from overwriting a row someone else claimed; the ClaimToken
        // written here is what lets the read-back below select only the rows THIS update touched.
        await db.OutboxMessages
            .Where(m => eligibleIds.Contains(m.Id) && m.Status == OutboxStatus.Pending)
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.Status, OutboxStatus.Claimed)
                      .SetProperty(m => m.ClaimedAt, now)
                      .SetProperty(m => m.ClaimToken, claimToken),
                cancellationToken);

        return await db.OutboxMessages
            .Where(m => eligibleIds.Contains(m.Id)
                        && m.Status == OutboxStatus.Claimed
                        && m.ClaimToken == claimToken)
            .ToListAsync(cancellationToken);
    }

    private async Task DeliverAsync(
        AppDbContext db,
        OutboxMessage message,
        IEmailSender emailSender,
        IPushSender pushSender,
        CancellationToken cancellationToken)
    {
        DeliveryResult result;

        if (message.Channel == NotificationChannel.Email)
        {
            result = await emailSender.SendAsync(
                message.Recipient, message.Subject, message.Body, cancellationToken);
        }
        else
        {
            // Parse first, then compare Guid to Guid. Comparing s.Id.ToString() would push a CONVERT
            // onto every row and lose the primary-key seek - on a hot path, against the tightest
            // resource in the stack. A Recipient that does not parse is a malformed row, treated the
            // same as a subscription that no longer exists.
            var subscription = Guid.TryParse(message.Recipient, out var subscriptionId)
                ? await db.PushSubscriptions
                    .FirstOrDefaultAsync(s => s.Id == subscriptionId, cancellationToken)
                : null;

            result = subscription is null
                // The subscription was deleted between enqueue and send. Nothing to retry against.
                ? DeliveryResult.SubscriptionGone("subscription_missing")
                : await pushSender.SendAsync(
                    subscription, message.Subject, message.Body, cancellationToken);

            if (result.Outcome == DeliveryOutcome.SubscriptionGone && subscription is not null)
            {
                db.PushSubscriptions.Remove(subscription);
            }
        }

        var now = timeProvider.GetUtcNow();

        switch (result.Outcome)
        {
            case DeliveryOutcome.Success:
            // A dead subscription is not a delivery failure: push is best-effort, email carries the
            // guarantee. Marking Sent keeps it out of the failure count that /health reports.
            case DeliveryOutcome.SubscriptionGone:
                message.Status = OutboxStatus.Sent;
                message.SentAt = now;
                message.ClaimedAt = null;
                message.ClaimToken = null;
                message.LastError = result.Error;
                break;

            case DeliveryOutcome.Permanent:
                message.Status = OutboxStatus.Failed;
                message.ClaimedAt = null;
                message.ClaimToken = null;
                message.AttemptCount++;
                message.LastError = result.Error;
                logger.LogWarning(
                    "Outbox {Id} failed permanently: {Error}", message.Id, result.Error);
                break;

            case DeliveryOutcome.Transient:
                message.AttemptCount++;
                message.ClaimedAt = null;
                message.ClaimToken = null;
                message.LastError = result.Error;

                if (message.AttemptCount >= _options.MaxAttempts)
                {
                    message.Status = OutboxStatus.Failed;
                    logger.LogWarning(
                        "Outbox {Id} failed after {Attempts} attempts: {Error}",
                        message.Id, message.AttemptCount, result.Error);
                }
                else
                {
                    message.Status = OutboxStatus.Pending;
                    message.NextAttemptAt = now + BackoffFor(message.AttemptCount);
                }

                break;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Backoff for the Nth attempt, clamped to the last entry in the schedule.</summary>
    private TimeSpan BackoffFor(int attemptCount)
    {
        var schedule = _options.BackoffSchedule;
        if (schedule.Length == 0)
        {
            return TimeSpan.FromMinutes(1);
        }

        var index = Math.Clamp(attemptCount - 1, 0, schedule.Length - 1);
        return schedule[index];
    }

    /// <summary>
    /// Keeps the table bounded against the 2GB Basic cap F-01 flagged for this change. Sent rows go;
    /// Failed rows stay, because they are the diagnostic record.
    /// </summary>
    private async Task PruneAsync(AppDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now - _options.SentRetention;

        var pruned = await db.OutboxMessages
            .Where(m => m.Status == OutboxStatus.Sent && m.SentAt != null && m.SentAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        if (pruned > 0)
        {
            logger.LogInformation("Pruned {Count} delivered outbox row(s).", pruned);
        }
    }

    /// <summary>
    /// One line per pass. This is the "heartbeat log line and outbox-failure count" the roadmap asks
    /// for - it answers both "is the worker alive?" and "is delivery quietly broken?".
    /// </summary>
    private async Task HeartbeatAsync(
        AppDbContext db, int processed, bool withCounts, CancellationToken cancellationToken)
    {
        if (!withCounts)
        {
            // The cheap line: proves the worker is alive without touching the table.
            logger.LogInformation("Outbox heartbeat: processed {Processed}.", processed);
            return;
        }

        var counts = await db.OutboxMessages
            .GroupBy(m => m.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int For(OutboxStatus s) => counts.FirstOrDefault(c => c.Status == s)?.Count ?? 0;

        logger.LogInformation(
            "Outbox heartbeat: processed {Processed}, pending {Pending}, claimed {Claimed}, failed {Failed}.",
            processed, For(OutboxStatus.Pending), For(OutboxStatus.Claimed), For(OutboxStatus.Failed));
    }
}
