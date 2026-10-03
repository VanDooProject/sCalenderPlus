using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace SCalenderPlus.Infrastructure.Persistence;

/// <summary>
/// Applies EF Core migrations under a Postgres session-level advisory lock, so concurrent runners
/// (several replicas, a re-run deploy) serialize instead of racing. Idempotent.
/// </summary>
public sealed partial class DatabaseMigrator(AppDbContext db, ILogger<DatabaseMigrator> logger)
{
    /// <summary>Arbitrary application-wide key for <c>pg_advisory_lock</c> ("SCALMIGR").</summary>
    internal const long AdvisoryLockKey = 0x5343_414C_4D49_4752;

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        // Keep one connection open so the lock and the migrations share the same session.
        await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();

            LogAcquiringLock(logger);
            await ExecuteAsync(connection, "SELECT pg_advisory_lock(@key)", cancellationToken).ConfigureAwait(false);
            try
            {
                var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();
                if (pending.Count == 0)
                {
                    LogUpToDate(logger);
                    return;
                }

                var names = string.Join(", ", pending);
                LogApplying(logger, pending.Count, names);
                await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
                LogApplied(logger, pending.Count);
            }
            finally
            {
                // Not cancellable: the lock must be released even when the caller gave up.
                await ExecuteAsync(connection, "SELECT pg_advisory_unlock(@key)", CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("key", AdvisoryLockKey);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Waiting for the migration advisory lock")]
    private static partial void LogAcquiringLock(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Database schema is up to date")]
    private static partial void LogUpToDate(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying {Count} migration(s): {Migrations}")]
    private static partial void LogApplying(ILogger logger, int count, string migrations);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied {Count} migration(s)")]
    private static partial void LogApplied(ILogger logger, int count);
}
