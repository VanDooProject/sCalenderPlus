using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NodaTime;
using SCalenderPlus.Infrastructure.Persistence;

namespace SCalenderPlus.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers infrastructure implementations of application ports.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddPersistence(configuration);

        return services;
    }

    /// <summary>Registers <see cref="AppDbContext"/> (Npgsql, NodaTime, snake_case) and the migrator.</summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .Configure(o => o.ConnectionString = configuration.GetConnectionString(DatabaseOptions.ConnectionStringName) ?? string.Empty)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddDbContext<AppDbContext>((sp, options) =>
            options.UseAppDatabase(sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString));
        services.AddScoped<DatabaseMigrator>();

        return services;
    }

    /// <summary>Migrates on startup when <c>Database:AutoMigrate</c> is true (development convenience, api only).</summary>
    public static IServiceCollection AddDatabaseAutoMigration(this IServiceCollection services)
    {
        services.AddHostedService<AutoMigrationService>();
        return services;
    }
}
