using Microsoft.Extensions.Options;
using SCalenderPlus.Infrastructure.Jobs;

namespace SCalenderPlus.Worker;

/// <summary>
/// The worker's job loop: <see cref="JobQueueOptions.Concurrency"/> slots each claim and run one job at a time
/// from the Postgres queue, polling while it is empty. Every claim attempt and lease renewal beats the
/// heartbeat, so an idle or busy (long job) worker stays ready; a loop that stops making progress does not.
/// </summary>
internal sealed partial class JobLoopService(
    JobRunner runner,
    JobLoopHeartbeat heartbeat,
    IOptions<JobQueueOptions> options,
    ILogger<JobLoopService> logger) : BackgroundService
{
    /// <summary>Upper bound for the pause after an unexpected error (e.g. database unreachable).</summary>
    private static readonly TimeSpan _maxErrorBackoff = TimeSpan.FromSeconds(30);

    /// <summary>Identifies this process in <c>jobs.locked_by</c>.</summary>
    public string WorkerId { get; } = $"{Environment.MachineName}/{Environment.ProcessId}/{Guid.NewGuid().ToString("N")[..8]}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var concurrency = options.Value.Concurrency;
        LogStarted(logger, WorkerId, concurrency);
        heartbeat.Beat();

        await Task.WhenAll(Enumerable.Range(0, concurrency).Select(_ => RunSlotAsync(stoppingToken))).ConfigureAwait(false);

        LogStopped(logger);
    }

    private async Task RunSlotAsync(CancellationToken stoppingToken)
    {
        var poll = options.Value.PollInterval;
        var errorBackoff = poll;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await runner.RunNextAsync(WorkerId, heartbeat.Beat, stoppingToken).ConfigureAwait(false))
                {
                    await Task.Delay(poll, stoppingToken).ConfigureAwait(false);
                }

                errorBackoff = poll;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // The loop must survive any failure (database outage); it backs off and retries.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogLoopError(logger, ex, errorBackoff);
                try
                {
                    await Task.Delay(errorBackoff, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                errorBackoff = TimeSpan.FromTicks(Math.Min(errorBackoff.Ticks * 2, _maxErrorBackoff.Ticks));
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Job loop started as {WorkerId} with {Concurrency} slot(s)")]
    private static partial void LogStarted(ILogger logger, string workerId, int concurrency);

    [LoggerMessage(Level = LogLevel.Information, Message = "Job loop stopped")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job loop error; retrying in {Backoff}")]
    private static partial void LogLoopError(ILogger logger, Exception exception, TimeSpan backoff);
}
