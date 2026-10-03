using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using SCalenderPlus.Application.Jobs;
using SCalenderPlus.Infrastructure.Persistence;

namespace SCalenderPlus.Infrastructure.Jobs;

internal sealed class PostgresJobScheduler(AppDbContext db, IClock clock) : IJobScheduler
{
    public Guid Enqueue<TPayload>(string type, TPayload payload, EnqueueOptions? options = null)
    {
        var job = NewJob(type, payload, options, dedupeKey: null);
        db.Set<Job>().Add(job);
        return job.Id;
    }

    public async Task<bool> EnqueueUniqueAsync<TPayload>(
        string type,
        TPayload payload,
        string dedupeKey,
        EnqueueOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dedupeKey);

        var job = NewJob(type, payload, options, dedupeKey);
        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO jobs (id, type, payload, run_at, attempts, max_attempts, dedupe_key, created_at)
            VALUES ({job.Id}, {job.Type}, CAST({job.Payload} AS jsonb), {job.RunAt}, 0, {job.MaxAttempts}, {job.DedupeKey}, {job.CreatedAt})
            ON CONFLICT (dedupe_key) WHERE dedupe_key IS NOT NULL AND dead_at IS NULL DO NOTHING
            """,
            cancellationToken).ConfigureAwait(false);
        return inserted == 1;
    }

    private Job NewJob<TPayload>(string type, TPayload payload, EnqueueOptions? options, string? dedupeKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        var now = clock.GetCurrentInstant();
        var maxAttempts = options?.MaxAttempts ?? EnqueueOptions.DefaultMaxAttempts;
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);

        return new Job
        {
            Id = Guid.CreateVersion7(),
            Type = type,
            Payload = JsonSerializer.Serialize(payload, JobContext.PayloadJson),
            RunAt = options?.RunAt ?? now,
            MaxAttempts = maxAttempts,
            DedupeKey = dedupeKey,
            CreatedAt = now,
        };
    }
}
