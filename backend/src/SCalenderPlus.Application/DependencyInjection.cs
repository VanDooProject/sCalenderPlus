using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SCalenderPlus.Application.Accounts;
using SCalenderPlus.Application.Calendars;
using SCalenderPlus.Application.Configuration;
using SCalenderPlus.Application.Email;
using SCalenderPlus.Application.Entitlements;
using SCalenderPlus.Application.Events;
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

        services.AddOptions<BillingOptions>()
            .Bind(configuration.GetSection(BillingOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<PlansOptions>()
            .Bind(configuration.GetSection(PlansOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<PlansOptions>, PlansOptionsValidator>();
        services.TryAddScoped<IEntitlementService, EntitlementService>();

        services.AddSingleton<AccountEmails>();
        services.AddSingleton<EmailDomainPolicy>();
        services.AddScoped<IEmailOutbox, EmailOutbox>();

        services.AddScoped<GroupService>();
        services.AddScoped<GroupMembershipService>();
        services.AddScoped<GroupInviteService>();
        services.AddSingleton<GroupEmails>();

        services.AddScoped<CalendarAccessLoader>();
        services.AddScoped<AclVersions>();
        services.AddScoped<CalendarService>();
        services.AddScoped<CalendarGrantService>();
        services.AddScoped<CalendarGroupLifecycle>();

        services.TryAddScoped<IEventOverrideSource, EventOverrideSource>();
        services.AddScoped<EventQueryService>();
        services.AddScoped<EventWriter>();
        services.AddScoped<EventService>();
        services.AddScoped<EventOverrideService>();
        services.AddScoped<EventAccessExplainer>();
        services.AddScoped<EventMoveService>();
        services.AddScoped<EventOccurrenceService>();
        services.AddScoped<EventShareRevocation>();
        services.AddScoped<IGroupMembershipObserver>(sp => sp.GetRequiredService<EventShareRevocation>());

        return services;
    }

    /// <summary>
    /// Worker only: the handlers of the jobs use cases enqueue. The api only enqueues (<see cref="IJobScheduler"/>);
    /// handlers depend on senders (<see cref="IEmailSender"/>, …) that only the worker registers
    /// (<c>AddJobProcessing</c>), so registering them in the api would fail its start-up validation.
    /// </summary>
    public static IServiceCollection AddJobHandlers(this IServiceCollection services)
    {
        services.AddScoped<IJobHandler, SendEmailJobHandler>();

        return services;
    }
}
