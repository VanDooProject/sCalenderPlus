using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SCalenderPlus.Application.Accounts;
using SCalenderPlus.Application.Configuration;
using SCalenderPlus.Application.Email;
using SCalenderPlus.Application.Groups;
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

        services.AddOptions<SignUpOptions>()
            .Bind(configuration.GetSection(SignUpOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<AccountEmails>();
        services.AddSingleton<EmailDomainPolicy>();
        services.AddScoped<IEmailOutbox, EmailOutbox>();
        services.AddScoped<IJobHandler, SendEmailJobHandler>();

        services.AddScoped<GroupService>();
        services.AddScoped<GroupMembershipService>();
        services.AddScoped<GroupInviteService>();
        services.AddSingleton<GroupEmails>();
        services.TryAddScoped<IGroupEntitlements, UnlimitedGroupEntitlements>(); // M2: entitlement service

        return services;
    }
}
