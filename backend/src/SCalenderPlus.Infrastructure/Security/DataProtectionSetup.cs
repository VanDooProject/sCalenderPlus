using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using SCalenderPlus.Infrastructure.Persistence;

namespace SCalenderPlus.Infrastructure.Security;

internal static class DataProtectionSetup
{
    /// <summary>
    /// Shared application discriminator: the api and the worker must read each other's protected payloads
    /// (e.g. tokens issued by the api, emails rendered by the worker).
    /// </summary>
    public const string ApplicationName = "scalenderplus";

    /// <summary>
    /// Persists the Data Protection key ring in PostgreSQL (<c>data_protection_keys</c>), so cookies and
    /// protected tokens survive restarts and work across replicas of read-only containers without volumes
    /// (docs/deployment/coolify.md §4–5).
    /// </summary>
    public static IServiceCollection AddPersistentDataProtection(this IServiceCollection services)
    {
        services.AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToDbContext<AppDbContext>();
        return services;
    }
}
