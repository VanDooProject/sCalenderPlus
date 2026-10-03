using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SCalenderPlus.Infrastructure.Persistence;

namespace SCalenderPlus.Infrastructure.Hosting;

/// <summary>
/// <c>migrate</c>: one-shot command (the compose <c>migrate</c> service) that applies EF Core migrations under
/// an advisory lock and exits 0 on success (also when already up to date), 1 on failure.
/// </summary>
public static partial class MigrateCommand
{
    public const string Name = "migrate";

    public static async Task<int> RunAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            EnvironmentName = HostEnvironment.Name,
        });
        builder.Services.AddPersistence(builder.Configuration);

        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SCalenderPlus.Migrate");
        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            shutdown.Cancel();
        };

        try
        {
            host.Services.GetRequiredService<IStartupValidator>().Validate();

            await using var scope = host.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().MigrateAsync(shutdown.Token).ConfigureAwait(false);
            return HostRunner.Success;
        }
        catch (Exception ex) when (HostRunner.IsConfigurationError(ex))
        {
            LogInvalidConfiguration(logger, ex);
            return HostRunner.Failure;
        }
#pragma warning disable CA1031 // A CLI entry point reports every failure as a non-zero exit code.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogFailed(logger, ex);
            return HostRunner.Failure;
        }
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "Invalid configuration; cannot migrate")]
    private static partial void LogInvalidConfiguration(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Database migration failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
