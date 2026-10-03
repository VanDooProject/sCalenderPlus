using Microsoft.Extensions.Options;
using NodaTime;
using Npgsql;
using SCalenderPlus.Infrastructure.Persistence;

namespace SCalenderPlus.Infrastructure.Jobs;

/// <summary>A job claimed by one worker for one attempt (<see cref="Attempt"/> = attempts after the claim).</summary>
internal sealed record ClaimedJob(Guid Id, string Type, string Payload, int Attempt, int MaxAttempts);

/// <summary>
/// SQL of the job life cycle. Every state change after the claim is conditional on the lease
/// (<c>locked_by</c> + <c>attempts</c>), so a worker that lost its lease can never complete, retry or
/// dead-letter a job another worker has claimed since.
/// </summary>
internal sealed class JobStore : IAsyncDisposable
{
    private const string LeaseCondition = "id = @id AND locked_by = @worker AND attempts = @attempt";
    private const int MaxErrorLength = 4000;

    // Built on first use: hosted services are constructed before startup validation, which must report
    // a missing connection string as a configuration error rather than fail here.
    private readonly Lazy<NpgsqlDataSource> _dataSourceLazy;

    public JobStore(IOptions<DatabaseOptions> options) =>
        _dataSourceLazy = new(() =>
        {
            var builder = new NpgsqlDataSourceBuilder(DbContextOptionsConfiguration.WithDefaults(options.Value.ConnectionString));
            builder.UseNodaTime();
            return builder.Build();
        });

    private NpgsqlDataSource DataSource => _dataSourceLazy.Value;

    /// <summary>
    /// Claims the next due job: <c>FOR UPDATE SKIP LOCKED</c> lets concurrent workers pass over rows another
    /// transaction is claiming, and the lease makes the job invisible to others until it expires.
    /// </summary>
    public async Task<ClaimedJob?> ClaimAsync(string worker, Instant now, Duration lease, CancellationToken cancellationToken)
    {
        await using var command = DataSource.CreateCommand(
            """
            UPDATE jobs AS j
            SET locked_by = @worker, locked_until = @lease_until, attempts = j.attempts + 1
            FROM (
                SELECT id FROM jobs
                WHERE dead_at IS NULL AND run_at <= @now AND (locked_until IS NULL OR locked_until <= @now)
                ORDER BY run_at, id
                LIMIT 1
                FOR UPDATE SKIP LOCKED
            ) AS next
            WHERE j.id = next.id
            RETURNING j.id, j.type, j.payload::text, j.attempts, j.max_attempts
            """);
        command.Parameters.AddWithValue("worker", worker);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("lease_until", now + lease);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new ClaimedJob(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetInt32(4));
    }

    /// <summary>Extends the lease; false when it was lost (expired and claimed by another worker).</summary>
    public Task<bool> RenewLeaseAsync(ClaimedJob job, string worker, Instant leaseUntil, CancellationToken cancellationToken) =>
        ExecuteAsync(job, worker, $"UPDATE jobs SET locked_until = @value WHERE {LeaseCondition}", leaseUntil, cancellationToken);

    /// <summary>Success: the job is removed.</summary>
    public Task<bool> CompleteAsync(ClaimedJob job, string worker, CancellationToken cancellationToken) =>
        ExecuteAsync(job, worker, $"DELETE FROM jobs WHERE {LeaseCondition}", null, cancellationToken);

    /// <summary>Failed attempt: release the lease and run again at <paramref name="runAt"/>.</summary>
    public Task<bool> RetryAsync(ClaimedJob job, string worker, Instant runAt, string error, CancellationToken cancellationToken) =>
        ExecuteAsync(
            job,
            worker,
            $"UPDATE jobs SET locked_by = NULL, locked_until = NULL, run_at = @value, last_error = @error WHERE {LeaseCondition}",
            runAt,
            cancellationToken,
            error);

    /// <summary>Permanent failure or attempts exhausted: keep the row as dead letter.</summary>
    public Task<bool> DeadLetterAsync(ClaimedJob job, string worker, Instant now, string error, CancellationToken cancellationToken) =>
        ExecuteAsync(
            job,
            worker,
            $"UPDATE jobs SET locked_by = NULL, locked_until = NULL, dead_at = @value, last_error = @error WHERE {LeaseCondition}",
            now,
            cancellationToken,
            error);

    /// <summary>Shutdown before the handler finished: release without consuming the attempt.</summary>
    public Task<bool> ReleaseAsync(ClaimedJob job, string worker, CancellationToken cancellationToken) =>
        ExecuteAsync(job, worker, $"UPDATE jobs SET locked_by = NULL, locked_until = NULL, attempts = attempts - 1 WHERE {LeaseCondition}", null, cancellationToken);

    private async Task<bool> ExecuteAsync(
        ClaimedJob job,
        string worker,
        string sql,
        Instant? value,
        CancellationToken cancellationToken,
        string? error = null)
    {
        await using var command = DataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", job.Id);
        command.Parameters.AddWithValue("worker", worker);
        command.Parameters.AddWithValue("attempt", job.Attempt);
        if (value is { } instant)
        {
            command.Parameters.AddWithValue("value", instant);
        }

        if (error is not null)
        {
            command.Parameters.AddWithValue("error", error.Length > MaxErrorLength ? error[..MaxErrorLength] : error);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public ValueTask DisposeAsync() => _dataSourceLazy.IsValueCreated ? _dataSourceLazy.Value.DisposeAsync() : ValueTask.CompletedTask;
}
