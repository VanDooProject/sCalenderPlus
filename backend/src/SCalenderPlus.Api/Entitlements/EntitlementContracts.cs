using SCalenderPlus.Application.Entitlements;
using SCalenderPlus.Core.Entitlements;

namespace SCalenderPlus.Api.Entitlements;

/// <summary>The limits of a plan; <c>null</c> = unlimited.</summary>
/// <param name="OwnedCalendars">Calendars owned by the user or by groups they are the billing owner of.</param>
/// <param name="OwnedGroups">Groups the user is the billing owner of.</param>
/// <param name="MembersPerGroup">Members of one group the user bills.</param>
/// <param name="EventsWithOverrides">Upcoming events with their own permissions, in calendars the user's plan governs.</param>
/// <param name="OverridesPerEvent">Permission entries on one event.</param>
public sealed record PlanLimitsResponse(int? OwnedCalendars, int? OwnedGroups, int? MembersPerGroup, int? EventsWithOverrides, int? OverridesPerEvent)
{
    public static PlanLimitsResponse From(PlanLimitValues limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        return new(limits.OwnedCalendars, limits.OwnedGroups, limits.MembersPerGroup, limits.EventsWithOverrides, limits.OverridesPerEvent);
    }
}

/// <summary>The caller's plan and its limits (usage counts follow later).</summary>
/// <param name="Plan"><c>free</c>, <c>pro</c>, <c>team</c> or <c>selfhost</c>.</param>
/// <param name="TrialEndsAt">End of the reverse trial while it runs (plan <c>pro</c>); null otherwise.</param>
public sealed record EntitlementsResponse(string Plan, DateTimeOffset? TrialEndsAt, PlanLimitsResponse Limits)
{
    public static EntitlementsResponse From(PlanEntitlements entitlements)
    {
        ArgumentNullException.ThrowIfNull(entitlements);
        return new(EntitlementErrors.PlanName(entitlements.Plan), entitlements.TrialEndsAt?.ToDateTimeOffset(), PlanLimitsResponse.From(entitlements.Limits));
    }
}
