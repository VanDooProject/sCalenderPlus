using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NodaTime;
using SCalenderPlus.Application.Auditing;
using SCalenderPlus.Application.Email;
using SCalenderPlus.Application.Jobs;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Infrastructure.Auditing;
using SCalenderPlus.Infrastructure.Email;
using SCalenderPlus.Infrastructure.Jobs;
using SCalenderPlus.Infrastructure.Persistence;
using SCalenderPlus.Infrastructure.Security;

namespace SCalenderPlus.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers infrastructure implementations of application ports.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddPersistence(configuration);
        services.AddPersistentDataProtection();
        services.AddScoped<IJobScheduler, PostgresJobScheduler>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.TryAddScoped<IActorContext, SystemActorContext>(); // the api registers the HTTP request's actor

        return services;
    }

    /// <summary>
    /// Worker only: job processing (<see cref="JobRunner"/>, <c>Jobs__*</c>) and the senders job handlers
    /// need (SMTP, <c>Smtp__*</c> — validated on start, so the api does not need SMTP settings).
    /// </summary>
    public static IServiceCollection AddJobProcessing(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<JobQueueOptions>()
            .Bind(configuration.GetSection(JobQueueOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => o.LeaseRenewalInterval < o.LeaseDuration, "Jobs:LeaseRenewalInterval must be shorter than Jobs:LeaseDuration.")
            .ValidateOnStart();
        services.AddSingleton<JobStore>();
        services.AddSingleton(sp => new JobRunner(
            sp.GetRequiredService<JobStore>(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IOptions<JobQueueOptions>>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<JobRunner>>()));

        services.AddOptions<SmtpOptions>()
            .Bind(configuration.GetSection(SmtpOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IEmailSender, SmtpEmailSender>();

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
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
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
