using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SCalenderPlus.Infrastructure.Persistence;

namespace SCalenderPlus.Infrastructure.Hosting;

/// <summary>
/// <c>GET /health/live</c> (process up, no dependencies) and <c>GET /health/ready</c> (all checks tagged
/// <see cref="ReadyTag"/>). Responses list check names and statuses only — no descriptions or exceptions.
/// </summary>
public static class HealthEndpoints
{
    public const string LivePath = "/health/live";
    public const string ReadyPath = "/health/ready";
    public const string ReadyTag = "ready";

    private static readonly TimeSpan _databaseTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Adds the readiness check for the database (reachable, migrations applied).</summary>
    public static IHealthChecksBuilder AddPlatformHealthChecks(this IServiceCollection services) =>
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", HealthStatus.Unhealthy, [ReadyTag], _databaseTimeout);

    /// <summary>
    /// Maps the health endpoints as minimal API endpoints (rather than <c>MapHealthChecks</c>) so they carry
    /// OpenAPI metadata and appear in the API document and the typed client (docs/architecture/api.md §4, System).
    /// </summary>
    public static IEndpointRouteBuilder MapPlatformHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(string.Empty).WithTags(OpenApiTag);

        group.MapGet(LivePath, (HttpContext context, HealthCheckService health) => CheckAsync(context, health, _ => false))
            .WithName("getHealthLive")
            .WithSummary("Liveness: the process is up (no dependency checks).")
            .Produces<HealthResponse>(StatusCodes.Status200OK)
            .Produces<HealthResponse>(StatusCodes.Status503ServiceUnavailable);

        group.MapGet(ReadyPath, (HttpContext context, HealthCheckService health) => CheckAsync(context, health, check => check.Tags.Contains(ReadyTag)))
            .WithName("getHealthReady")
            .WithSummary("Readiness: database reachable and migrated (worker: job loop heartbeat).")
            .Produces<HealthResponse>(StatusCodes.Status200OK)
            .Produces<HealthResponse>(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    /// <summary>OpenAPI tag of the unversioned system endpoints.</summary>
    public const string OpenApiTag = "System";

    /// <summary>Same status mapping as <c>MapHealthChecks</c>: Healthy/Degraded → 200, Unhealthy → 503.</summary>
    private static async Task CheckAsync(HttpContext context, HealthCheckService health, Func<HealthCheckRegistration, bool> predicate)
    {
        var report = await health.CheckHealthAsync(predicate, context.RequestAborted).ConfigureAwait(false);
        context.Response.StatusCode = report.Status == HealthStatus.Unhealthy
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status200OK;
        await WriteResponseAsync(context, report).ConfigureAwait(false);
    }

    private static Task WriteResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        context.Response.Headers.CacheControl = "no-store";
        var body = new HealthResponse(
            report.Status.ToString(),
            report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString(), StringComparer.Ordinal));
        return JsonSerializer.SerializeAsync(context.Response.Body, body, HealthJsonContext.Default.HealthResponse, context.RequestAborted);
    }
}
