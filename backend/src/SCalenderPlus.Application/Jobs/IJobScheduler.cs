using NodaTime;

namespace SCalenderPlus.Application.Jobs;

/// <summary>
/// The Postgres job queue (docs/architecture/overview.md §6). Jobs are processed by the worker with
/// at-least-once semantics: handlers must be idempotent.
/// </summary>
public interface IJobScheduler
{
    /// <summary>
    /// Stages a job in the current unit of work; it is inserted by <c>IAppDbContext.SaveChangesAsync</c>
    /// together with the business data (transactional outbox) and never runs if that save fails.
    /// </summary>
    Guid Enqueue<TPayload>(string type, TPayload payload, EnqueueOptions? options = null);

    /// <summary>
    /// Inserts a job immediately (in the ambient transaction, if any) unless a job with the same
    /// <paramref name="dedupeKey"/> is still pending or running, e.g. for cron schedules on several replicas.
    /// Returns false when deduplicated.
    /// </summary>
    Task<bool> EnqueueUniqueAsync<TPayload>(
        string type,
        TPayload payload,
        string dedupeKey,
        EnqueueOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <param name="RunAt">Earliest start (default: now).</param>
/// <param name="MaxAttempts">Attempts before the job is dead-lettered (default <see cref="DefaultMaxAttempts"/>).</param>
public sealed record EnqueueOptions(Instant? RunAt = null, int? MaxAttempts = null)
{
    public const int DefaultMaxAttempts = 10;
}
