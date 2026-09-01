using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using po_prostu_silka.Application.Notifications;
using po_prostu_silka.Domain.Notifications;
using po_prostu_silka.Infrastructure.Persistence;

namespace po_prostu_silka.Infrastructure.Notifications;

/// <summary>
/// Surfaces dead-lettered messages on /health, so silent delivery failure is visible from a URL
/// instead of only to whoever happens to read the log stream.
///
/// Degraded, never Unhealthy: a delivery backlog is not the site being down, and reporting it as
/// such would make /health useless as the "can the app reach its data" signal F-01 built it to be.
/// </summary>
public class OutboxHealthCheck(AppDbContext db, IOptions<OutboxOptions> options) : IHealthCheck
{
    private readonly OutboxOptions _options = options.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var failed = await db.OutboxMessages
            .CountAsync(m => m.Status == OutboxStatus.Failed, cancellationToken);

        var data = new Dictionary<string, object> { ["failed"] = failed };

        return failed > _options.FailedThreshold
            ? HealthCheckResult.Degraded(
                $"{failed} outbox message(s) failed delivery (threshold {_options.FailedThreshold}).",
                data: data)
            : HealthCheckResult.Healthy($"{failed} failed outbox message(s).", data);
    }
}
