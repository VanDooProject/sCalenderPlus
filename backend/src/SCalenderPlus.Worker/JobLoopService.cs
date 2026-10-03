namespace SCalenderPlus.Worker;

/// <summary>
/// Skeleton of the worker's job loop. Job processing (Postgres queue, schedules) arrives in M1.
/// </summary>
internal sealed partial class JobLoopService(ILogger<JobLoopService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(logger);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }

        LogStopped(logger);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Job loop started")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Job loop stopped")]
    private static partial void LogStopped(ILogger logger);
}
