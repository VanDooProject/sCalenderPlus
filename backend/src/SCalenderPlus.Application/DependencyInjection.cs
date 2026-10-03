using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SCalenderPlus.Application.Configuration;

namespace SCalenderPlus.Application;

public static class DependencyInjection
{
    /// <summary>Registers application services and validated options (fail fast on start).</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AppOptions>()
            .Bind(configuration.GetSection(AppOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
