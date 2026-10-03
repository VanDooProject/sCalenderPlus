using SCalenderPlus.Api.Hosting;
using SCalenderPlus.Api.OpenApi;
using SCalenderPlus.Api.Problems;
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
        builder.Services.AddApiProblemDetails();
        builder.Services.AddValidation();
        builder.Services.AddApiDocument();
        if (BuildTimeDocument.IsGenerating)
        {
            builder.Services.UseInMemoryKeyRing();
        }

        var app = builder.Build();

        // Order matters: client scheme/address first, then errors → RFC 9457 problem details for everything below.
        app.UseForwardedHeaders();
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        app.MapPlatformHealthEndpoints();
        app.MapApiDocument();
        app.MapApiV1();

        return await HostRunner.RunAsync(app).ConfigureAwait(false);
    }
}
