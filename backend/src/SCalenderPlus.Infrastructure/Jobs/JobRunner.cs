using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;
using SCalenderPlus.Application.Jobs;

namespace SCalenderPlus.Infrastructure.Jobs;

/// <summary>
/// Runs one job at a time for a worker slot: claim (SKIP LOCKED + lease), execute the handler in its own DI
/// scope while renewing the lease, then complete, retry with exponential backoff, or dead-letter.
/// At-least-once: a crashed worker's job runs again once its lease expires.
/// </summary>
public sealed partial class JobRunner
{
    public const string ActivitySourceName = "SCalenderPlus.Jobs";

    private static readonly ActivitySource _activitySource = new(ActivitySourceName);

    private readonly JobStore _store;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly IOptions<JobQueueOptions> _optionsAccessor;
    private readonly ILogger<JobRunner> _logger;

    internal JobRunner(JobStore store, IServiceScopeFactory scopeFactory, IClock clock, IOptions<JobQueueOptions> options, ILogger<JobRunner> logger)
    {
        _store = store;
        _scopeFactory = scopeFactory;
        _clock = clock;
        _optionsAccessor = options; // read on use: constructed before startup validation runs
        _logger = logger;
    }

    private JobQueueOptions Options => _optionsAccessor.Value;

    /// <summary>
    /// Claims and processes the next due job. Returns false when none was due. <paramref name="onProgress"/>
    /// is called after every claim attempt and lease renewal (the worker's liveness heartbeat).
    /// </summary>
    public async Task<bool> RunNextAsync(string workerId, Action? onProgress, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        var job = await _store.ClaimAsync(workerId, _clock.GetCurrentInstant(), Duration.FromTimeSpan(Options.LeaseDuration), cancellationToken).ConfigureAwait(false);
        onProgress?.Invoke();
        if (job is null)
        {
            return false;
        }

        using var activity = _activitySource.StartActivity($"job {job.Type}", ActivityKind.Consumer);
        activity?.SetTag("job.id", job.Id);
        activity?.SetTag("job.type", job.Type);
        activity?.SetTag("job.attempt", job.Attempt);
        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["JobId"] = job.Id, ["JobType"] = job.Type, ["Attempt"] = job.Attempt });

        if (job.Attempt > job.MaxAttempts)
        {
            // Only reachable when earlier attempts crashed or lost their lease without recording a failure.
            await DeadLetterAsync(job, workerId, $"Attempts exhausted ({job.MaxAttempts}); the last attempt did not finish (worker crash or lost lease).").ConfigureAwait(false);
            return true;
        }

        await ExecuteAsync(job, workerId, onProgress, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task ExecuteAsync(ClaimedJob job, string workerId, Action? onProgress, CancellationToken stoppingToken)
    {
        using var leaseLost = new CancellationTokenSource();
        using var handlerToken = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, leaseLost.Token);
        using var stopRenewal = new CancellationTokenSource();
        var renewal = RenewLeaseAsync(job, workerId, onProgress, leaseLost, stopRenewal.Token);
        var started = Stopwatch.GetTimestamp();

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetServices<IJobHandler>().FirstOrDefault(h => string.Equals(h.Type, job.Type, StringComparison.Ordinal))
                ?? throw new PermanentJobFailureException($"No handler registered for job type '{job.Type}'.");

            await handler.HandleAsync(new JobContext(job.Id, job.Type, job.Attempt, job.MaxAttempts, job.Payload), handlerToken.Token).ConfigureAwait(false);

            await StopAsync(stopRenewal, renewal).ConfigureAwait(false);
            if (await _store.CompleteAsync(job, workerId, CancellationToken.None).ConfigureAwait(false))
            {
                var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                LogSucceeded(_logger, elapsed);
            }
            else
            {
                LogLeaseLost(_logger);
            }
        }
        catch (OperationCanceledException) when (leaseLost.IsCancellationRequested)
        {
            await StopAsync(stopRenewal, renewal).ConfigureAwait(false);
            LogLeaseLost(_logger);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await StopAsync(stopRenewal, renewal).ConfigureAwait(false);
            await _store.ReleaseAsync(job, workerId, CancellationToken.None).ConfigureAwait(false);
            LogReleased(_logger);
        }
        catch (PermanentJobFailureException ex)
        {
            await StopAsync(stopRenewal, renewal).ConfigureAwait(false);
            await DeadLetterAsync(job, workerId, Describe(ex), ex).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Any handler failure is recorded on the job and retried; the worker keeps running.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            await StopAsync(stopRenewal, renewal).ConfigureAwait(false);
            if (job.Attempt >= job.MaxAttempts)
            {
                await DeadLetterAsync(job, workerId, Describe(ex), ex).ConfigureAwait(false);
            }
            else
            {
                var delay = RetryDelay(job.Attempt, Options.BaseRetryDelay, Options.MaxRetryDelay, Random.Shared.NextDouble());
                await _store.RetryAsync(job, workerId, _clock.GetCurrentInstant() + Duration.FromTimeSpan(delay), Describe(ex), CancellationToken.None).ConfigureAwait(false);
                LogRetry(_logger, ex, job.Attempt, job.MaxAttempts, delay);
            }
        }
    }

    /// <summary>
    /// Exponential backoff before attempt <paramref name="failedAttempt"/> + 1: base · 2^(attempt−1), capped at
    /// <paramref name="max"/>, with ±20 % jitter (<paramref name="random"/> in [0, 1)) so failed jobs spread out.
    /// </summary>
    internal static TimeSpan RetryDelay(int failedAttempt, TimeSpan baseDelay, TimeSpan max, double random)
    {
        var exponent = Math.Min(failedAttempt - 1, 30);
        var raw = Math.Min(baseDelay.TotalMilliseconds * Math.Pow(2, exponent), max.TotalMilliseconds);
        var jittered = raw * (0.8 + (0.4 * random));
        return TimeSpan.FromMilliseconds(Math.Min(jittered, max.TotalMilliseconds));
    }

    private async Task RenewLeaseAsync(ClaimedJob job, string workerId, Action? onProgress, CancellationTokenSource leaseLost, CancellationToken stop)
    {
        using var timer = new PeriodicTimer(Options.LeaseRenewalInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stop).ConfigureAwait(false))
            {
                try
                {
                    var leaseUntil = _clock.GetCurrentInstant() + Duration.FromTimeSpan(Options.LeaseDuration);
                    if (!await _store.RenewLeaseAsync(job, workerId, leaseUntil, stop).ConfigureAwait(false))
                    {
                        await leaseLost.CancelAsync().ConfigureAwait(false);
                        return;
                    }

                    onProgress?.Invoke();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A database hiccup: the lease is still valid for a while; try again on the next tick.
                    LogRenewalFailed(_logger, ex);
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Handler finished.
        }
    }

    private static async Task StopAsync(CancellationTokenSource stopRenewal, Task renewal)
    {
        await stopRenewal.CancelAsync().ConfigureAwait(false);
        await renewal.ConfigureAwait(false);
    }

    private async Task DeadLetterAsync(ClaimedJob job, string workerId, string error, Exception? exception = null)
    {
        await _store.DeadLetterAsync(job, workerId, _clock.GetCurrentInstant(), error, CancellationToken.None).ConfigureAwait(false);
        LogDeadLettered(_logger, exception, job.Attempt, error);
    }

    private static string Describe(Exception ex) => $"{ex.GetType().FullName}: {ex.Message}";

    [LoggerMessage(Level = LogLevel.Information, Message = "Job succeeded in {ElapsedMs:F0} ms")]
    private static partial void LogSucceeded(ILogger logger, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job attempt {Attempt}/{MaxAttempts} failed; retrying in {Delay}")]
    private static partial void LogRetry(ILogger logger, Exception exception, int attempt, int maxAttempts, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job dead-lettered after attempt {Attempt}: {Error}")]
    private static partial void LogDeadLettered(ILogger logger, Exception? exception, int attempt, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job lease lost; another worker may run it again")]
    private static partial void LogLeaseLost(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Job released on shutdown")]
    private static partial void LogReleased(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Renewing the job lease failed")]
    private static partial void LogRenewalFailed(ILogger logger, Exception exception);
}
