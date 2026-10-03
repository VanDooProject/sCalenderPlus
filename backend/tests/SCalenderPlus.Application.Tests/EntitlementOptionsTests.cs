using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SCalenderPlus.Application.Entitlements;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.Core.Entitlements;

namespace SCalenderPlus.Application.Tests;

/// <summary>Issue #52: plan limits and billing mode from configuration, validated on start.</summary>
public sealed class EntitlementOptionsTests
{
    [Fact]
    public void Defaults_are_self_host_unlimited_and_the_plans_md_numbers()
    {
        using var provider = BuildProvider([]);
        provider.GetRequiredService<IStartupValidator>().Validate();

        Assert.Equal(BillingProvider.None, provider.GetRequiredService<IOptions<BillingOptions>>().Value.Provider);
        var plans = provider.GetRequiredService<IOptions<PlansOptions>>().Value;
        Assert.Equal(new PlanLimitValues(3, 1, 15, 10, 3), plans.For(Plan.Free).ToValues());
        Assert.Equal(new PlanLimitValues(30, 10, 150, null, 25), plans.For(Plan.Pro).ToValues());
        Assert.Equal(new PlanLimitValues(300, null, 1000, null, 100), plans.For(Plan.Team).ToValues());
        Assert.Equal(PlanLimitValues.Unlimited, plans.For(Plan.SelfHost).ToValues());
        Assert.Throws<ArgumentOutOfRangeException>(() => plans.For((Plan)99));
    }

    [Fact]
    public void Single_limits_and_the_provider_are_configurable()
    {
        using var provider = BuildProvider(new()
        {
            ["Billing:Provider"] = "stripe",
            ["Plans:Free:MembersPerGroup"] = "20",
            ["Plans:SelfHost:OwnedGroups"] = "5",
        });
        provider.GetRequiredService<IStartupValidator>().Validate();

        Assert.Equal(BillingProvider.Stripe, provider.GetRequiredService<IOptions<BillingOptions>>().Value.Provider);
        var plans = provider.GetRequiredService<IOptions<PlansOptions>>().Value;
        Assert.Equal(new PlanLimitValues(3, 1, 20, 10, 3), plans.Free.ToValues());
        Assert.Equal(5, plans.SelfHost.OwnedGroups);
    }

    [Fact]
    public void Negative_limits_fail_startup_validation()
    {
        using var provider = BuildProvider(new() { ["Plans:Pro:OverridesPerEvent"] = "-1" });

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Equal(typeof(PlansOptions), ex.OptionsType);
        Assert.Contains("Plans:Pro:OverridesPerEvent", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_billing_providers_fail_on_start()
    {
        using var provider = BuildProvider(new() { ["Billing:Provider"] = "paypal" });

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
    }

    [Theory]
    [InlineData(PlanLimit.OwnedCalendars)]
    [InlineData(PlanLimit.OwnedGroups)]
    [InlineData(PlanLimit.MembersPerGroup)]
    [InlineData(PlanLimit.EventsWithOverrides)]
    [InlineData(PlanLimit.OverridesPerEvent)]
    public void Refusals_are_402_problems_with_the_limit(PlanLimit limit)
    {
        var ex = EntitlementErrors.LimitReached(Plan.Free, new PlanLimitExceeded(limit, 10, 10));

        Assert.Equal(ErrorCodes.PlanLimitReached, ex.Code);
        Assert.Contains("10", ex.Detail, StringComparison.Ordinal);
        var problemLimit = Assert.IsType<Dictionary<string, object?>>(ex.Extensions["limit"]);
        Assert.Equal(PlanLimits.Key(limit), problemLimit["key"]);
        Assert.Equal(10, problemLimit["max"]);
        Assert.Equal(10, problemLimit["used"]);
        Assert.Equal("free", ex.Extensions["plan"]);
    }

    [Theory]
    [InlineData(Plan.Free, "free")]
    [InlineData(Plan.Pro, "pro")]
    [InlineData(Plan.Team, "team")]
    [InlineData(Plan.SelfHost, "selfhost")]
    public void Plans_have_api_names(Plan plan, string name) => Assert.Equal(name, EntitlementErrors.PlanName(plan));

    [Fact]
    public void Unknown_plans_have_no_name() => Assert.Throws<ArgumentOutOfRangeException>(() => EntitlementErrors.PlanName((Plan)99));

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        settings["App:PublicBaseUrl"] = "https://app.example.com";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddApplication(configuration).BuildServiceProvider();
    }
}
