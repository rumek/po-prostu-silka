using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace po_prostu_silka.Infrastructure.TestData;

/// <summary>
/// Turns a <c>TestDataSeed:Reset=true</c> left on - the omission deploy-plan.md's reseed procedure
/// warns about - into an alert instead of a silent wipe on the next recycle (S-28). App Service
/// recycles without warning, and the Staging database also holds the real-address accounts added
/// for each client demo.
///
/// <para>
/// Reads configuration only and never touches the database. "Would wipe" is
/// <see cref="TestDataSeeder.WouldWipe"/>, the same predicate the seeder's wipe is gated on.
/// Degraded rather than Unhealthy for the reason <c>OutboxHealthCheck</c> gives: the site is up, and
/// the availability test matches the body <c>Healthy</c>, so Degraded is enough to page.
/// </para>
/// </summary>
public class TestDataResetHealthCheck(IConfiguration configuration, IHostEnvironment environment) : IHealthCheck
{
    public const string ArmedMessage = "TestDataSeed:Reset is on: the next restart wipes the database";

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var options = TestDataSeeder.ReadOptions(configuration);

        return Task.FromResult(TestDataSeeder.WouldWipe(options, environment)
            ? HealthCheckResult.Degraded(ArmedMessage)
            : HealthCheckResult.Healthy());
    }
}
