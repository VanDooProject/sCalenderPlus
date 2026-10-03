using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SCalenderPlus.Infrastructure.Persistence;

/// <summary>
/// Readiness: the database is reachable and has every migration this build knows about. Newer migrations
/// applied by a later release are fine (expand/contract keeps the previous version compatible).
/// </summary>
internal sealed class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var pending = await db.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false);
            return pending.Any()
                ? HealthCheckResult.Unhealthy("Database schema is behind: pending migrations.")
                : HealthCheckResult.Healthy();
        }
#pragma warning disable CA1031 // Any failure to reach the database means "not ready".
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return HealthCheckResult.Unhealthy("Database unreachable.", ex);
        }
    }
}
