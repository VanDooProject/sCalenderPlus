using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace SCalenderPlus.IntegrationTests.Infrastructure;

/// <summary>
/// Test-only hooks around the real request pipeline of a <see cref="HostFactory{TProgram}"/> host, without
/// adding endpoints to the app (they would show up in the OpenAPI document and the authorization matrix):
/// <list type="bullet">
/// <item>before the pipeline: the connection's remote address is taken from <see cref="RemoteIpHeader"/>
/// (TestServer has none), so forwarded-header handling can be exercised;</item>
/// <item>after the pipeline (only reached when no endpoint matched): requests below <see cref="PathPrefix"/>
/// are answered by the registered handlers, e.g. to throw an exception through the real error handling.</item>
/// <item><see cref="AddEndpoints"/>: test-only endpoints below <see cref="PathPrefix"/> with their own routing
/// and authorization (after the real authentication), to exercise endpoint conventions such as policies;
/// they are not part of the OpenAPI document.</item>
/// </list>
/// </summary>
internal static class TestPipeline
{
    public const string RemoteIpHeader = "X-Test-Remote-Ip";
    public const string PathPrefix = "/__test";

    public static void Add(IServiceCollection services, IReadOnlyDictionary<string, RequestDelegate> handlers) =>
        services.AddSingleton<IStartupFilter>(new Filter(handlers));

    /// <summary>Maps endpoints (paths must start with <see cref="PathPrefix"/>) that run after the real pipeline.</summary>
    public static void AddEndpoints(IServiceCollection services, Action<IEndpointRouteBuilder> map) =>
        services.AddSingleton<IStartupFilter>(new EndpointFilter(map));

    private sealed class EndpointFilter(Action<IEndpointRouteBuilder> map) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.MapWhen(
                context => context.Request.Path.StartsWithSegments(PathPrefix, StringComparison.Ordinal),
                branch =>
                {
                    branch.UseRouting();
                    branch.UseAuthorization();
                    branch.UseEndpoints(map);
                });
        };
    }

    private sealed class Filter(IReadOnlyDictionary<string, RequestDelegate> handlers) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(RemoteIpHeader, out var ip))
                {
                    context.Connection.RemoteIpAddress = IPAddress.Parse(ip.ToString());
                }

                return nextMiddleware(context);
            });

            next(app);

            app.Use((context, nextMiddleware) =>
                context.Request.Path.StartsWithSegments(PathPrefix, StringComparison.Ordinal)
                && handlers.TryGetValue(context.Request.Path.Value![PathPrefix.Length..], out var handler)
                    ? handler(context)
                    : nextMiddleware(context));
        };
    }
}
