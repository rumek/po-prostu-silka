using System.Diagnostics;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;

namespace po_prostu_silka.Api.Telemetry;

/// <summary>
/// Requests, exceptions and warnings to Application Insights (S-28, GL-03) - and nothing else, because
/// the workspace has a 0.1 GB daily cap and the cap is the backstop, not the plan.
///
/// <para>
/// REGISTERED ONLY WHEN <c>APPLICATIONINSIGHTS_CONNECTION_STRING</c> IS SET. The integration tests,
/// Development and CI have none, so they run with no exporter at all rather than one that fails to
/// send. Only the App Service carries the setting.
/// </para>
///
/// <para>
/// THE WORKER IS THE COST HOTSPOT. Two outbox lanes poll SQL every 15 seconds - thousands of SQL
/// dependency spans a day with no request above them - plus an Information line per pass. So logs
/// export from Warning up, and <see cref="BackgroundSqlFilter"/> drops a SQL span that has no parent.
/// Traces are rate limited to about one a second, which at this club's traffic keeps nearly every
/// request anyway.
/// </para>
/// </summary>
public static class Telemetry
{
    public const string ConnectionStringKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    public static WebApplicationBuilder AddAzureMonitorTelemetry(this WebApplicationBuilder builder)
    {
        if (string.IsNullOrWhiteSpace(builder.Configuration[ConnectionStringKey]))
        {
            return builder;
        }

        // Before UseAzureMonitor: a processor only affects the exporters registered after it.
        builder.Services.ConfigureOpenTelemetryTracerProvider(
            (_, tracing) => tracing.AddProcessor(new BackgroundSqlFilter()));

        builder.Services.AddOpenTelemetry().UseAzureMonitor(options =>
        {
            options.TracesPerSecond = 1;

            // Off, so a warning or an exception is exported even when its request was sampled out.
            // Logs are already cut to Warning and up, so keeping all of them costs little and is
            // exactly the telemetry this exists to keep.
            options.EnableTraceBasedLogsSampler = false;
        });

        builder.Logging.AddFilter<OpenTelemetryLoggerProvider>(string.Empty, LogLevel.Warning);

        return builder;
    }

    /// <summary>
    /// Drops a SQL span that has no parent: the outbox worker's polling, and startup's seeding. A
    /// query made while serving a request has that request as its parent and is kept.
    /// </summary>
    private sealed class BackgroundSqlFilter : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity activity)
        {
            var isSql = activity.GetTagItem("db.system") is not null
                || activity.GetTagItem("db.system.name") is not null;

            if (isSql && activity.Parent is null && activity.ParentSpanId == default)
            {
                activity.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
            }
        }
    }
}
