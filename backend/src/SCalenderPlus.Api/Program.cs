using SCalenderPlus.Api.Auth;
using SCalenderPlus.Api.Hosting;
using SCalenderPlus.Api.OpenApi;
using SCalenderPlus.Api.Problems;
using SCalenderPlus.Api.RateLimiting;
using SCalenderPlus.Application;
using SCalenderPlus.Application.Auditing;
using SCalenderPlus.Infrastructure;
using SCalenderPlus.Infrastructure.Hosting;

namespace SCalenderPlus.Api;

public sealed class Program
{
    /// <summary>Port inside the container (<c>ASPNETCORE_HTTP_PORTS</c> overrides).</summary>
    public const int DefaultHttpPort = 8080;

    private Program()
    {
    }

    public static async Task<int> Main(string[] args)
    {
        switch (args)
        {
            case [HealthcheckCommand.Name, .. var healthcheckArgs]:
                return await HealthcheckCommand.RunAsync(healthcheckArgs, DefaultHttpPort).ConfigureAwait(false);
            case [MigrateCommand.Name, .. var migrateArgs]:
                return await MigrateCommand.RunAsync(migrateArgs).ConfigureAwait(false);
        }

        var builder = WebApplication.CreateBuilder(args);
        if (BuildTimeDocument.IsGenerating)
        {
            builder.Configuration.AddInMemoryCollection(BuildTimeDocument.PlaceholderSettings);
        }

        builder.WebHost.UseDefaultHttpPort(builder.Configuration, DefaultHttpPort);
        builder.AddPlatformObservability("scalenderplus-api");

        builder.Services
            .AddApplication(builder.Configuration)
            .AddInfrastructure(builder.Configuration)
            .AddDatabaseAutoMigration()
            .AddPlatformHealthChecks();
        builder.Services.AddTrustedForwardedHeaders(builder.Configuration);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IActorContext, HttpActorContext>();
        builder.Services.AddApiAuthentication();
        builder.Services.AddApiRateLimiting(builder.Configuration);
        builder.Services.AddApiProblemDetails();
        builder.Services.AddValidation();
        builder.Services.AddApiDocument();
        if (BuildTimeDocument.IsGenerating)
        {
            builder.Services.UseInMemoryKeyRing();
        }

        var app = builder.Build();

        // Order matters: client scheme/address first, then errors → RFC 9457 problem details for everything below,
        // then routing (endpoint metadata), the session cookie, rate limits (per IP / session), CSRF and the
        // endpoint's authorization requirements.
        // No CORS middleware: the api is same-origin only, cross-origin preflights get no Access-Control-* headers.
        app.UseForwardedHeaders();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseRouting();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseCsrfProtection();
        app.UseAuthorization();

        app.MapPlatformHealthEndpoints();
        app.MapApiDocument();
        app.MapApiV1();

        return await HostRunner.RunAsync(app).ConfigureAwait(false);
    }
}
