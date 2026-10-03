using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace SCalenderPlus.Infrastructure.Persistence;

internal static class DbContextOptionsConfiguration
{
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    /// <summary>Provider setup shared by the runtime registration and the design-time factory.</summary>
    public static DbContextOptionsBuilder UseAppDatabase(this DbContextOptionsBuilder builder, string connectionString) =>
        builder
            .UseNpgsql(WithDefaults(connectionString), npgsql =>
            {
                npgsql.UseNodaTime();
                npgsql.MigrationsHistoryTable(MigrationsHistoryTable);
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name);
            })
            .UseSnakeCaseNamingConvention();

    /// <summary>
    /// Npgsql defaults <c>Gss Encryption Mode</c> to <c>Prefer</c> and tries to load <c>libgssapi_krb5</c> on
    /// every new physical connection; the chiseled images don't ship it, so the runtime writes a plain-text
    /// "Cannot load library" line into the JSON log stream. Default it to <c>Disable</c> unless the connection
    /// string sets it explicitly (Kerberos users keep full control).
    /// </summary>
    internal static string WithDefaults(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        try
        {
            var keys = new DbConnectionStringBuilder { ConnectionString = connectionString }.Keys.Cast<string>();
            if (keys.Any(k => k.Replace(" ", string.Empty, StringComparison.Ordinal).StartsWith("gssenc", StringComparison.OrdinalIgnoreCase)))
            {
                return connectionString;
            }

            return new NpgsqlConnectionStringBuilder(connectionString) { GssEncryptionMode = GssEncryptionMode.Disable }.ConnectionString;
        }
        catch (ArgumentException)
        {
            // Malformed: leave it to Npgsql to report when connecting (health check / migrate failure).
            return connectionString;
        }
    }
}
