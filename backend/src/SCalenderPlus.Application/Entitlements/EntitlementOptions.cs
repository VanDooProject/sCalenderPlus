using Microsoft.Extensions.Options;
using SCalenderPlus.Core.Entitlements;

namespace SCalenderPlus.Application.Entitlements;

/// <summary>How plans are resolved (environment variable <c>Billing__Provider</c>).</summary>
public enum BillingProvider
{
    /// <summary>Self-host: every subject has plan <see cref="Plan.SelfHost"/> (unlimited unless <c>Plans__SelfHost__*</c> says otherwise). Default.</summary>
    None,

    /// <summary>
    /// SaaS: plans come from Stripe subscriptions; until billing lands every subject has plan <see cref="Plan.Free"/>,
    /// after the reverse trial (<see cref="PlansOptions.TrialDays"/>) with <see cref="Plan.Pro"/>'s limits.
    /// </summary>
    Stripe,
}

/// <summary>Billing settings (<c>Billing__*</c>); Stripe keys follow with the Stripe integration.</summary>
public sealed class BillingOptions
{
    public const string SectionName = "Billing";

    public BillingProvider Provider { get; set; } = BillingProvider.None;
}

/// <summary>The limits of one plan; empty (null) = unlimited. Configured as <c>Plans__{Plan}__{Limit}</c>.</summary>
public sealed class PlanLimitOptions
{
    public int? OwnedCalendars { get; set; }

    public int? OwnedGroups { get; set; }

    public int? MembersPerGroup { get; set; }

    public int? EventsWithOverrides { get; set; }

    public int? OverridesPerEvent { get; set; }

    public PlanLimitValues ToValues() => new(OwnedCalendars, OwnedGroups, MembersPerGroup, EventsWithOverrides, OverridesPerEvent);
}

/// <summary>
/// Plan limits (<c>Plans__*</c>, e.g. <c>Plans__Free__MembersPerGroup=20</c>, <c>Plans__SelfHost__OwnedGroups=5</c>).
/// The defaults are the numbers of docs/product/plans.md; configuration overrides single values, so marketing
/// can tune limits without a release. Self-host is unlimited by default.
/// </summary>
public sealed class PlansOptions
{
    public const string SectionName = "Plans";

    /// <summary>
    /// Reverse trial (<c>Plans__TrialDays</c>): with a billing provider, every account has <see cref="Plan.Pro"/> for
    /// this many days from its creation, then falls back to <see cref="Plan.Free"/> (over-limit usage stays, only
    /// growth is refused). 0 disables the trial; at most <see cref="MaxTrialDays"/>. Self-host is not affected.
    /// </summary>
    public int TrialDays { get; set; } = 90;

    /// <summary>Upper bound of <see cref="TrialDays"/> (ten years): larger values overflow the trial's end and are surely typos.</summary>
    public const int MaxTrialDays = 3650;

    public PlanLimitOptions Free { get; set; } = new() { OwnedCalendars = 3, OwnedGroups = 1, MembersPerGroup = 15, EventsWithOverrides = 10, OverridesPerEvent = 3 };

    public PlanLimitOptions Pro { get; set; } = new() { OwnedCalendars = 30, OwnedGroups = 10, MembersPerGroup = 150, OverridesPerEvent = 25 };

    public PlanLimitOptions Team { get; set; } = new() { OwnedCalendars = 300, MembersPerGroup = 1000, OverridesPerEvent = 100 };

    public PlanLimitOptions SelfHost { get; set; } = new();

    public PlanLimitOptions For(Plan plan) => plan switch
    {
        Plan.Free => Free,
        Plan.Pro => Pro,
        Plan.Team => Team,
        Plan.SelfHost => SelfHost,
        _ => throw new ArgumentOutOfRangeException(nameof(plan), plan, "Unknown plan."),
    };
}

/// <summary>Fails start-up on negative limits and on trial days outside 0 … 3650 (a typo must not silently lock everyone out or open everything).</summary>
internal sealed class PlansOptionsValidator : IValidateOptions<PlansOptions>
{
    public ValidateOptionsResult Validate(string? name, PlansOptions options)
    {
        var failures = new List<string>();
        if (options.TrialDays is < 0 or > PlansOptions.MaxTrialDays)
        {
            failures.Add($"{PlansOptions.SectionName}:{nameof(PlansOptions.TrialDays)} must be 0 … {PlansOptions.MaxTrialDays} (0 disables the trial).");
        }

        foreach (var plan in Enum.GetValues<Plan>())
        {
            var limits = options.For(plan).ToValues();
            foreach (var limit in PlanLimits.All)
            {
                if (limits.Get(limit) is < 0)
                {
                    failures.Add($"{PlansOptions.SectionName}:{plan}:{limit} must be empty (unlimited) or ≥ 0.");
                }
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
