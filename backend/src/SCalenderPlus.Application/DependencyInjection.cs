using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SCalenderPlus.Application.Configuration;
using SCalenderPlus.Application.Email;
using SCalenderPlus.Application.Jobs;

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

        services.AddScoped<IEmailOutbox, EmailOutbox>();
        services.AddScoped<IJobHandler, SendEmailJobHandler>();

        return services;
    }
}
