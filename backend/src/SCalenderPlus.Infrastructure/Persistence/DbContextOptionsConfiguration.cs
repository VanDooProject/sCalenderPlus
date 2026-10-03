using Microsoft.EntityFrameworkCore;

namespace SCalenderPlus.Infrastructure.Persistence;

internal static class DbContextOptionsConfiguration
{
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    /// <summary>Provider setup shared by the runtime registration and the design-time factory.</summary>
    public static DbContextOptionsBuilder UseAppDatabase(this DbContextOptionsBuilder builder, string connectionString) =>
        builder
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.UseNodaTime();
                npgsql.MigrationsHistoryTable(MigrationsHistoryTable);
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name);
            })
            .UseSnakeCaseNamingConvention();
}
