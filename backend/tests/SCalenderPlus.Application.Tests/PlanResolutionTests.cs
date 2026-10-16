using NodaTime;
using SCalenderPlus.Application.Entitlements;
using SCalenderPlus.Core.Entitlements;

namespace SCalenderPlus.Application.Tests;

/// <summary>Reverse trial: Pro limits for <c>Plans__TrialDays</c> days from the account's creation, then Free; self-host unaffected.</summary>
public sealed class PlanResolutionTests
{
    private static readonly Instant _created = Instant.FromUtc(2026, 1, 1, 12, 0);

    [Fact]
    public void A_new_account_has_pro_limits_until_the_trial_ends()
    {
        var plans = new PlansOptions();

        var entitlements = PlanResolution.Resolve(BillingProvider.Stripe, plans, _created, _created + Duration.FromDays(89));

        Assert.Equal(Plan.Pro, entitlements.Plan);
        Assert.Equal(plans.Pro.ToValues(), entitlements.Limits);
        Assert.Equal(_created + Duration.FromDays(90), entitlements.TrialEndsAt);
    }

    [Fact]
    public void After_the_trial_the_account_falls_back_to_free()
    {
        var plans = new PlansOptions();

        var entitlements = PlanResolution.Resolve(BillingProvider.Stripe, plans, _created, _created + Duration.FromDays(90));

        Assert.Equal(Plan.Free, entitlements.Plan);
        Assert.Equal(plans.Free.ToValues(), entitlements.Limits);
        Assert.Null(entitlements.TrialEndsAt);
    }

    [Fact]
    public void Configured_trial_days_set_the_end()
    {
        var plans = new PlansOptions { TrialDays = 14 };

        Assert.Equal(Plan.Pro, PlanResolution.Resolve(BillingProvider.Stripe, plans, _created, _created + Duration.FromDays(13)).Plan);
        Assert.Equal(Plan.Free, PlanResolution.Resolve(BillingProvider.Stripe, plans, _created, _created + Duration.FromDays(14)).Plan);
    }

    [Fact]
    public void Zero_trial_days_disable_the_trial()
    {
        var plans = new PlansOptions { TrialDays = 0 };

        Assert.False(PlanResolution.HasTrial(BillingProvider.Stripe, plans));
        var entitlements = PlanResolution.Resolve(BillingProvider.Stripe, plans, _created, _created);
        Assert.Equal(Plan.Free, entitlements.Plan);
        Assert.Null(entitlements.TrialEndsAt);
    }

    [Fact]
    public void Unknown_accounts_get_no_trial() =>
        Assert.Equal(Plan.Free, PlanResolution.Resolve(BillingProvider.Stripe, new PlansOptions(), null, _created).Plan);

    [Fact]
    public void A_groups_trial_counts_from_the_older_of_billing_owner_and_group()
    {
        var older = _created - Duration.FromDays(400);

        Assert.Equal(older, PlanResolution.TrialAnchor(_created, older)); // billing transferred to a new account
        Assert.Equal(older, PlanResolution.TrialAnchor(older, _created)); // a new group of an old account
        Assert.Equal(_created, PlanResolution.TrialAnchor(_created, null)); // personal resources
        Assert.Null(PlanResolution.TrialAnchor(null, older)); // unknown billing owner: no trial
    }

    [Fact]
    public void Self_host_is_unaffected_by_the_trial()
    {
        var plans = new PlansOptions();

        Assert.False(PlanResolution.HasTrial(BillingProvider.None, plans));
        var entitlements = PlanResolution.Resolve(BillingProvider.None, plans, _created, _created);
        Assert.Equal(Plan.SelfHost, entitlements.Plan);
        Assert.Equal(PlanLimitValues.Unlimited, entitlements.Limits);
        Assert.Null(entitlements.TrialEndsAt);
    }
}
