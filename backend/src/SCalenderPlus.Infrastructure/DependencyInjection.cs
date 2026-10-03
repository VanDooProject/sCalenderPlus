using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;

namespace SCalenderPlus.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers infrastructure implementations of application ports.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IClock>(SystemClock.Instance);

        return services;
    }
}
