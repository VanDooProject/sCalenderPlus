using SCalenderPlus.Core.Groups;

namespace SCalenderPlus.Core.Permissions;

/// <summary>What a <see cref="ResolutionStep"/> records; the steps of one resolution read top to bottom like §4.1/§4.2.</summary>
public enum ResolutionStepKind
{
    /// <summary>Anonymous: the share link's level on this calendar (<c>none</c> for a link of another calendar). <see cref="ResolutionStep.CalendarLevel"/>.</summary>
    ShareLink,

    /// <summary>The principal owns the calendar, or is a role-owner of the owning group (<see cref="ResolutionStep.Principal"/> = the owner).</summary>
    CalendarOwner,

    /// <summary>A matching calendar grant (<see cref="ResolutionStep.Principal"/>, <see cref="ResolutionStep.CalendarLevel"/>).</summary>
    GrantMatched,

    /// <summary>The owning group's role default (<see cref="ResolutionStep.Principal"/> = owner group, <see cref="ResolutionStep.Role"/>, <see cref="ResolutionStep.CalendarLevel"/>).</summary>
    GroupRoleDefault,

    /// <summary>Result of §4.1: the maximum of the steps above (<see cref="ResolutionStep.CalendarLevel"/>).</summary>
    CalendarResult,

    /// <summary>A recurrence exception resolves with its series' ACL (<see cref="ResolutionStep.ResourceId"/> = series id).</summary>
    InheritedFromSeries,

    /// <summary>Calendar <c>manage</c>/<c>owner</c>: <c>manage</c>, overrides ignored (rule 5).</summary>
    ManagerFloor,

    /// <summary>The creator with ≥ <c>contribute</c>: <c>manage</c>, overrides ignored (rule 6).</summary>
    CreatorFloor,

    /// <summary>The creator, but the calendar disabled the creator floor (<c>creatorsManageOwnEvents = false</c>).</summary>
    CreatorFloorDisabled,

    /// <summary>The creator, but with less than <c>contribute</c> on the calendar: the floor lapsed.</summary>
    CreatorFloorLapsed,

    /// <summary>The event level implied by the calendar level (<see cref="ResolutionStep.CalendarLevel"/> → <see cref="ResolutionStep.EventLevel"/>).</summary>
    BaseLevel,

    /// <summary>A matching override (<see cref="ResolutionStep.Principal"/>, <see cref="ResolutionStep.EventLevel"/>, <see cref="ResolutionStep.Tier"/>); <see cref="ResolutionStep.Decisive"/> when on the winning tier.</summary>
    OverrideMatched,

    /// <summary>No override matches: the base level stays.</summary>
    NoMatchingOverride,

    /// <summary>The most specific tier's best level replaces the base (<see cref="ResolutionStep.EventLevel"/>, <see cref="ResolutionStep.Tier"/>).</summary>
    OverrideApplied,

    /// <summary><c>everyone</c>/<c>anonymous</c> tier: <c>min(override, base)</c> (<see cref="ResolutionStep.EventLevel"/> = result, <see cref="ResolutionStep.Applied"/> = it lowered the override).</summary>
    RestrictOnly,

    /// <summary>The override level was capped at <c>edit</c> (<c>manage</c> only through floors).</summary>
    OverrideCap,

    /// <summary>Anonymous: <c>min(level, read, link level)</c> (<see cref="ResolutionStep.EventLevel"/> = result).</summary>
    LinkCeiling,

    /// <summary>The effective level (<see cref="ResolutionStep.EventLevel"/>).</summary>
    Result,
}

/// <summary>One step of a resolution trace, for the access explainer (<c>GET /events/{id}/access/explain</c>).</summary>
public sealed record ResolutionStep(ResolutionStepKind Kind)
{
    public Principal? Principal { get; init; }

    public GroupRole? Role { get; init; }

    public CalendarLevel? CalendarLevel { get; init; }

    public EventLevel? EventLevel { get; init; }

    /// <summary>Specificity tier of <see cref="Principal"/> (overrides).</summary>
    public int? Tier { get; init; }

    /// <summary>The override is on the winning tier.</summary>
    public bool? Decisive { get; init; }

    /// <summary>The step changed the level (restrict-only).</summary>
    public bool? Applied { get; init; }

    /// <summary>A related resource (the series of an exception).</summary>
    public Guid? ResourceId { get; init; }
}

/// <summary>Result of §4.1: a principal's level on a calendar and how it came about.</summary>
public sealed record CalendarAccess(CalendarLevel Level, IReadOnlyList<ResolutionStep> Steps);

/// <summary>Result of §4.2: a principal's level on an event, their level on its calendar, and the full trace.</summary>
public sealed record EventAccess(EventLevel Level, CalendarLevel CalendarLevel, IReadOnlyList<ResolutionStep> Steps)
{
    /// <summary><c>manage</c> comes only from a floor; floor holders may change the event's overrides (§4.4).</summary>
    public bool HasFloor => Level == EventLevel.Manage;
}
