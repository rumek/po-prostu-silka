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
/// <item>the oldest undelivered row past <see cref="OutboxOptions.MaxUndeliveredAge"/> — messages
/// stuck, whatever the reason: a throttle, a backoff, a worker that is running but not getting
/// through. This is the one a failed count alone never shows;</item>
/// <item>no finished pass for <see cref="OutboxOptions.WorkerStallAfter"/> — the worker itself hung
/// or throwing, which an empty outbox would otherwise hide until the next cancellation.</item>
/// </list>
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

        var oldestUndelivered = await db.OutboxMessages
            .Where(m => m.Status == OutboxStatus.Pending || m.Status == OutboxStatus.Claimed)
            .MinAsync(m => (DateTimeOffset?)m.CreatedAt, cancellationToken);

        var undeliveredAge = oldestUndelivered is null ? TimeSpan.Zero : now - oldestUndelivered.Value;
        var sinceLastPass = now - heartbeat.LastPassAt;

        var data = new Dictionary<string, object>
        {
            ["failed"] = failed,
            ["oldestUndeliveredMinutes"] = Math.Round(undeliveredAge.TotalMinutes, 1),
            ["secondsSinceLastPass"] = Math.Round(sinceLastPass.TotalSeconds),
        };

        var problems = new List<string>();

        if (failed > _options.FailedThreshold)
        {
            problems.Add(
                $"{failed} outbox message(s) failed delivery (threshold {_options.FailedThreshold}).");
        }

        if (undeliveredAge > _options.MaxUndeliveredAge)
        {
            problems.Add(
                $"The oldest undelivered outbox message is {undeliveredAge.TotalMinutes:F0} min old " +
                $"(threshold {_options.MaxUndeliveredAge.TotalMinutes:F0} min).");
        }

        if (sinceLastPass > _options.WorkerStallAfter)
        {
            problems.Add(
                $"The outbox worker has not finished a pass for {sinceLastPass.TotalMinutes:F0} min " +
                $"(threshold {_options.WorkerStallAfter.TotalMinutes:F0} min).");
        }

        return problems.Count > 0
            ? HealthCheckResult.Degraded(string.Join(" ", problems), data: data)
            : HealthCheckResult.Healthy($"{failed} failed outbox message(s).", data);
    }
}
