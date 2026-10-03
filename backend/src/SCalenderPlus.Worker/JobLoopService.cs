namespace SCalenderPlus.Worker;

/// <summary>
/// Skeleton of the worker's job loop: it only beats the heartbeat for now. Job processing (Postgres queue,
/// schedules) arrives in M1.
/// </summary>
internal sealed partial class JobLoopService(JobLoopHeartbeat heartbeat, ILogger<JobLoopService> logger) : BackgroundService
{
    private static readonly TimeSpan _interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(logger);

        using var timer = new PeriodicTimer(_interval);
        try
        {
            do
            {
                heartbeat.Beat();
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
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
