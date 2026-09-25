using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Domain.Notifications;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.Notifications;

/// <summary>
/// Surfaces delivery trouble on /health, so it is visible from a URL — and alertable — instead of
/// only to whoever happens to read the log stream.
///
/// <para>
/// THREE SIGNALS, because each one misses what the others catch:
/// <list type="bullet">
/// <item>dead-lettered rows past <see cref="OutboxOptions.FailedThreshold"/> — messages given up on;</item>
/// <item>the oldest undelivered row past its allowance, judged PER CHANNEL — messages stuck, whatever
/// the reason: a backoff, a worker that is running but not getting through. This is the one a
/// failed count alone never shows. Push is always judged against
/// <see cref="OutboxOptions.MaxUndeliveredAge"/>. Email is too, except while the lane is
/// THROTTLED, when it gets <see cref="OutboxOptions.ThrottledMaxUndeliveredAge"/> instead: the
/// managed sender domain's hard 10-per-hour cap is a known limit, not a fault, and must not page the
/// owner the way a dead worker does (S-28);</item>
/// <item>no finished pass for <see cref="OutboxOptions.WorkerStallAfter"/> — the worker itself hung
/// or throwing, which an empty outbox would otherwise hide until the next cancellation.</item>
/// </list>
/// </para>
///
/// <para>
/// "THROTTLED" IS READ FROM THE TABLE, not from the heartbeat: the lane counts as throttled while any
/// undelivered email carries <see cref="AcsEmailSender.ThrottledReason"/>. A throttled row keeps that
/// <c>LastError</c> until it is sent, which survives a recycle and lasts as long as the provider's
/// <c>Retry-After</c> wait (up to an hour). An in-memory "throttled at" stamp would not: nothing
/// re-throttles during that wait to refresh it, and a recycle forgets it.
/// </para>
///
/// <para>
/// Degraded, never Unhealthy: a delivery backlog is not the site being down, and reporting it as
/// such would make /health useless as the "can the app reach its data" signal F-01 built it to be.
/// Degraded still answers 200, so an alert must match the body <c>Healthy</c>, not the status code
/// — the deploy runbook says how.
/// </para>
/// </summary>
public class OutboxHealthCheck(
    AppDbContext db,
    IOptions<OutboxOptions> options,
    OutboxWorkerHeartbeat heartbeat,
    TimeProvider timeProvider) : IHealthCheck
{
    private readonly OutboxOptions _options = options.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();

        var failed = await db.OutboxMessages
            .CountAsync(m => m.Status == OutboxStatus.Failed, cancellationToken);

        var undelivered = db.OutboxMessages
            .Where(m => m.Status == OutboxStatus.Pending || m.Status == OutboxStatus.Claimed);

        var oldestByChannel = await undelivered
            .GroupBy(m => m.Channel)
            .Select(g => new { Channel = g.Key, Oldest = g.Min(m => m.CreatedAt) })
            .ToDictionaryAsync(x => x.Channel, x => x.Oldest, cancellationToken);

        var emailThrottled = await undelivered
            .AnyAsync(
                m => m.Channel == NotificationChannel.Email && m.LastError == AcsEmailSender.ThrottledReason,
                cancellationToken);

        var pushAge = AgeOf(NotificationChannel.Push);
        var emailAge = AgeOf(NotificationChannel.Email);
        var emailAllowance = emailThrottled ? _options.ThrottledMaxUndeliveredAge : _options.MaxUndeliveredAge;
        var sinceLastPass = now - heartbeat.LastPassAt;

        var data = new Dictionary<string, object>
        {
            ["failed"] = failed,
            ["oldestUndeliveredPushMinutes"] = Math.Round(pushAge.TotalMinutes, 1),
            ["oldestUndeliveredEmailMinutes"] = Math.Round(emailAge.TotalMinutes, 1),
            ["emailThrottled"] = emailThrottled,
            ["secondsSinceLastPass"] = Math.Round(sinceLastPass.TotalSeconds),
        };

        var problems = new List<string>();

        if (failed > _options.FailedThreshold)
        {
            problems.Add(
                $"{failed} outbox message(s) failed delivery (threshold {_options.FailedThreshold}).");
        }

        if (pushAge > _options.MaxUndeliveredAge)
        {
            problems.Add(
                $"The oldest undelivered push is {pushAge.TotalMinutes:F0} min old " +
                $"(threshold {_options.MaxUndeliveredAge.TotalMinutes:F0} min).");
        }

        if (emailAge > emailAllowance)
        {
            problems.Add(
                $"The oldest undelivered email is {emailAge.TotalMinutes:F0} min old " +
                $"(threshold {emailAllowance.TotalMinutes:F0} min" +
                (emailThrottled ? ", the email lane is throttled)." : ")."));
        }

        if (sinceLastPass > _options.WorkerStallAfter)
        {
            problems.Add(
                $"The outbox worker has not finished a pass for {sinceLastPass.TotalMinutes:F0} min " +
                $"(threshold {_options.WorkerStallAfter.TotalMinutes:F0} min).");
        }

        return problems.Count > 0
            ? HealthCheckResult.Degraded(string.Join(" ", problems), data: data)
            : HealthCheckResult.Healthy(
                $"{failed} failed outbox message(s)" + (emailThrottled ? "; the email lane is throttled." : "."),
                data);

        TimeSpan AgeOf(NotificationChannel channel) =>
            oldestByChannel.TryGetValue(channel, out var oldest) ? now - oldest : TimeSpan.Zero;
    }
}
