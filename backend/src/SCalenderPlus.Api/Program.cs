using SCalenderPlus.Application;
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
        builder.WebHost.UseDefaultHttpPort(builder.Configuration, DefaultHttpPort);
        builder.AddPlatformObservability("scalenderplus-api");

        builder.Services
            .AddApplication(builder.Configuration)
            .AddInfrastructure(builder.Configuration)
            .AddDatabaseAutoMigration()
            .AddPlatformHealthChecks();

        var app = builder.Build();

        app.MapPlatformHealthEndpoints();

        return await HostRunner.RunAsync(app).ConfigureAwait(false);
    }
}
