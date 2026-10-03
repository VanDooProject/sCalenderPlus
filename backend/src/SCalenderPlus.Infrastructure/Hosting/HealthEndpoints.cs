using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
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

    public static IEndpointRouteBuilder MapPlatformHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks(LivePath, new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteResponseAsync,
        }).ExcludeFromDescription();

        endpoints.MapHealthChecks(ReadyPath, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
            ResponseWriter = WriteResponseAsync,
        }).ExcludeFromDescription();

        return endpoints;
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
