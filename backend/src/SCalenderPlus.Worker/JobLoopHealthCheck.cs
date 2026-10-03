using Microsoft.Extensions.Diagnostics.HealthChecks;
using NodaTime;

namespace SCalenderPlus.Worker;

/// <summary>Ready only while the job loop beats: last heartbeat younger than <see cref="MaxAge"/>.</summary>
internal sealed class JobLoopHealthCheck(JobLoopHeartbeat heartbeat, IClock clock) : IHealthCheck
{
    public static readonly Duration MaxAge = Duration.FromMinutes(2);

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var last = heartbeat.LastBeat;
        var result = last is { } beat && clock.GetCurrentInstant() - beat < MaxAge
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Job loop heartbeat is missing or stale.");
        return Task.FromResult(result);
    }
}
