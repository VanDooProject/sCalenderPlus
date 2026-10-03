using SCalenderPlus.Application;
using SCalenderPlus.Infrastructure;
using SCalenderPlus.Infrastructure.Hosting;

namespace SCalenderPlus.Worker;

public sealed class Program
{
    /// <summary>Internal health port (<c>ASPNETCORE_HTTP_PORTS</c> overrides). The worker has no other HTTP surface.</summary>
    public const int DefaultHttpPort = 8081;

    private Program()
    {
    }

    public static async Task<int> Main(string[] args)
    {
        if (args is [HealthcheckCommand.Name, .. var healthcheckArgs])
        {
            return await HealthcheckCommand.RunAsync(healthcheckArgs, DefaultHttpPort).ConfigureAwait(false);
        }

        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseDefaultHttpPort(builder.Configuration, DefaultHttpPort);
        builder.AddPlatformObservability("scalenderplus-worker");

        builder.Services
            .AddApplication(builder.Configuration)
            .AddInfrastructure(builder.Configuration)
            .AddJobProcessing(builder.Configuration);

        builder.Services.AddSingleton<JobLoopHeartbeat>();
        builder.Services.AddHostedService<JobLoopService>();
        builder.Services.AddPlatformHealthChecks()
            .AddCheck<JobLoopHealthCheck>("job-loop", tags: [HealthEndpoints.ReadyTag]);

        var app = builder.Build();

        app.MapPlatformHealthEndpoints();

        return await HostRunner.RunAsync(app).ConfigureAwait(false);
    }
}
