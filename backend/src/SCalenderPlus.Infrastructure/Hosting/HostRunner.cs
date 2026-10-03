using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SCalenderPlus.Infrastructure.Hosting;

/// <summary>Runs a host and maps fatal startup errors to a non-zero process exit code.</summary>
public static partial class HostRunner
{
    public const int Success = 0;
    public const int Failure = 1;

    public static async Task<int> RunAsync(IHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        try
        {
            // Not host.RunAsync(): it disposes the host before we could log the failure.
            await host.StartAsync().ConfigureAwait(false);
            await host.WaitForShutdownAsync().ConfigureAwait(false);
            return Success;
        }
        catch (Exception ex) when (IsConfigurationError(ex))
        {
            // Misconfiguration (ValidateOnStart): fail fast so the orchestrator keeps the old version running.
            var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerCategory);
            LogInvalidConfiguration(logger, ex);
            return Failure;
        }
        finally
        {
            if (host is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                host.Dispose();
            }
        }
    }

    internal const string LoggerCategory = "SCalenderPlus.Startup";

    /// <summary>
    /// <see cref="IStartupValidator"/> throws a single <see cref="OptionsValidationException"/>, or an
    /// <see cref="AggregateException"/> when several options classes are invalid.
    /// </summary>
    internal static bool IsConfigurationError(Exception ex) =>
        ex is OptionsValidationException
        || (ex is AggregateException aggregate && aggregate.InnerExceptions.All(e => e is OptionsValidationException));

    [LoggerMessage(Level = LogLevel.Critical, Message = "Invalid configuration; refusing to start")]
    private static partial void LogInvalidConfiguration(ILogger logger, Exception exception);
}
