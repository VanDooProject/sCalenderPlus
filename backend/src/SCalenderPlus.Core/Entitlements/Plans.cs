using NodaTime;

namespace SCalenderPlus.Core.Entitlements;

/// <summary>Plans of docs/product/plans.md. <see cref="SelfHost"/> applies to every subject with <c>Billing__Provider=none</c>.</summary>
public enum Plan
{
    Free,
    Pro,
    Team,
    SelfHost,
}

/// <summary>Countable plan limits (docs/product/plans.md "Limits"); API keys via <see cref="PlanLimits.Key"/>.</summary>
public enum PlanLimit
{
    /// <summary>Calendars whose plan subject is the billing owner (user-owned and owned by groups they bill).</summary>
    OwnedCalendars,

    /// <summary>Groups whose billing owner is the subject.</summary>
    OwnedGroups,

    /// <summary>Members of one group (the group's billing owner's plan).</summary>
    MembersPerGroup,

    /// <summary>Active (not ended) events with at least one permission override, per calendar plan subject.</summary>
    EventsWithOverrides,

    /// <summary>Override entries on one event.</summary>
    OverridesPerEvent,
}

/// <summary>The limits of one plan; <c>null</c> = unlimited.</summary>
public sealed record PlanLimitValues(int? OwnedCalendars, int? OwnedGroups, int? MembersPerGroup, int? EventsWithOverrides, int? OverridesPerEvent)
{
    public static PlanLimitValues Unlimited { get; } = new(null, null, null, null, null);

    public int? Get(PlanLimit limit) => limit switch
    {
        PlanLimit.OwnedCalendars => OwnedCalendars,
        PlanLimit.OwnedGroups => OwnedGroups,
        PlanLimit.MembersPerGroup => MembersPerGroup,
        PlanLimit.EventsWithOverrides => EventsWithOverrides,
        PlanLimit.OverridesPerEvent => OverridesPerEvent,
        _ => throw new ArgumentOutOfRangeException(nameof(limit), limit, "Unknown plan limit."),
    };
}

/// <summary>A refused change: <paramref name="Used"/> of <paramref name="Max"/> are in use (the problem member <c>limit: { key, max, used }</c>).</summary>
public sealed record PlanLimitExceeded(PlanLimit Limit, int Max, int Used)
{
    public string Key => PlanLimits.Key(Limit);
}

/// <summary>
/// How one override change (atomic replace of an event's overrides) affects the plan usage; measured by the
/// override use case in its transaction.
/// </summary>
/// <param name="ActiveEventsWithOverrides">Active events with overrides of the calendar's plan subject, including this event if it already counts.</param>
/// <param name="EventIsActive">The event has not ended (<see cref="PlanLimits.IsActive"/>).</param>
/// <param name="CurrentEntries">Override entries the event has now.</param>
/// <param name="ProposedEntries">Override entries after the change.</param>
/// <param name="OnlyRemovals">Every proposed entry exists already with the same or a higher level (nothing added or raised).</param>
public sealed record OverrideUsage(int ActiveEventsWithOverrides, bool EventIsActive, int CurrentEntries, int ProposedEntries, bool OnlyRemovals);

/// <summary>Pure plan-limit rules (docs/product/plans.md): what counts, and when a change exceeds a limit.</summary>
public static class PlanLimits
{
    public static IReadOnlyList<PlanLimit> All { get; } =
        [PlanLimit.OwnedCalendars, PlanLimit.OwnedGroups, PlanLimit.MembersPerGroup, PlanLimit.EventsWithOverrides, PlanLimit.OverridesPerEvent];

    /// <summary>The stable API key of a limit (snake case, e.g. <c>events_with_overrides</c>).</summary>
    public static string Key(PlanLimit limit) => limit switch
    {
        PlanLimit.OwnedCalendars => "owned_calendars",
        PlanLimit.OwnedGroups => "owned_groups",
        PlanLimit.MembersPerGroup => "members_per_group",
        PlanLimit.EventsWithOverrides => "events_with_overrides",
        PlanLimit.OverridesPerEvent => "overrides_per_event",
        _ => throw new ArgumentOutOfRangeException(nameof(limit), limit, "Unknown plan limit."),
    };

    /// <summary>
    /// Adding <paramref name="adding"/> to <paramref name="used"/> stays within <paramref name="max"/>
    /// (<c>null</c> = unlimited); null when allowed. Over-limit usage after a downgrade stays — only growth is refused.
    /// </summary>
    public static PlanLimitExceeded? CheckAdd(PlanLimit limit, int? max, int used, int adding = 1) =>
        max is { } m && adding > 0 && used + adding > m ? new PlanLimitExceeded(limit, m, used) : null;

    /// <summary>
    /// An event is active until its last occurrence ended: <paramref name="occursUntil"/> is the upper bound of
    /// <c>occurs_range</c> (end of a single event, <c>series_until_utc</c> of a series; null = infinite series,
    /// always active). Past events do not count against <see cref="PlanLimit.EventsWithOverrides"/>.
    /// </summary>
    public static bool IsActive(Instant? occursUntil, Instant now) => occursUntil is not { } until || until > now;

    /// <summary>
    /// The override limits of plans.md: removing is always allowed (also over the limit after a downgrade);
    /// otherwise the event may carry at most <see cref="PlanLimitValues.OverridesPerEvent"/> entries, and an
    /// active event that gains its first overrides needs room in <see cref="PlanLimitValues.EventsWithOverrides"/>
    /// (an owner already over that limit may add nothing anywhere).
    /// </summary>
    public static PlanLimitExceeded? CheckOverrideChange(PlanLimitValues limits, OverrideUsage usage)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(usage);
        if (usage.OnlyRemovals)
        {
            return null;
        }

        if (limits.OverridesPerEvent is { } perEvent && usage.ProposedEntries > perEvent)
        {
            return new(PlanLimit.OverridesPerEvent, perEvent, usage.CurrentEntries);
        }

        if (!usage.EventIsActive)
        {
            return null;
        }

        // An event that has overrides already counts; one that gains its first ones adds one.
        var after = usage.ActiveEventsWithOverrides + (usage.CurrentEntries > 0 ? 0 : 1);
        return limits.EventsWithOverrides is { } max && after > max
            ? new(PlanLimit.EventsWithOverrides, max, usage.ActiveEventsWithOverrides)
            : null;
    }
}
