using System.Diagnostics;
using SCalenderPlus.Core.Groups;

namespace SCalenderPlus.Core.Permissions;

/// <summary>What an actor may do with an event's overrides (§4.4).</summary>
public enum OverrideRights
{
    /// <summary>No floor on the event: overrides are read-only (<c>edit</c> and below, incl. override-granted <c>edit</c>).</summary>
    None,

    /// <summary>Creator floor: own event, but no external sharing (<c>creatorsMayShareExternally = false</c>).</summary>
    InternalOnly,

    /// <summary>Calendar <c>manage</c>/<c>owner</c>, or creator floor with <c>creatorsMayShareExternally</c>.</summary>
    IncludingExternal,
}

/// <summary>Outcome of <see cref="OverridePolicy.EvaluateChange"/>; anything but <see cref="Allowed"/> refuses the whole change.</summary>
public enum OverrideChangeVerdict
{
    Allowed,

    /// <summary>The actor's event level is <c>none</c>: 404.</summary>
    NotFound,

    /// <summary>The actor sees the event but holds no floor: 403 <c>insufficient_permission</c> (required <c>manage</c>).</summary>
    Forbidden,

    /// <summary>An entry is invalid (see <see cref="OverrideChangeDecision.Violations"/>): 422 (<c>validation_failed</c>).</summary>
    Invalid,

    /// <summary>An added or raised entry shares with principals outside the calendar's audience: 403 <c>external_sharing_not_allowed</c>.</summary>
    ExternalSharingNotAllowed,
}

/// <summary>Why one proposed override is refused.</summary>
public enum OverrideViolationReason
{
    /// <summary>Level above <c>edit</c> (rule 9): 422.</summary>
    LevelTooHigh,

    /// <summary>The same principal appears twice in the proposed set: 422.</summary>
    DuplicatePrincipal,

    /// <summary>A group the actor is not a member of and that neither owns nor holds a grant on the calendar: 422.</summary>
    GroupNotSelectable,

    /// <summary>External principal without the right to share externally: 403 <c>external_sharing_not_allowed</c>.</summary>
    ExternalSharing,
}

public sealed record OverrideViolation(EventOverride Override, OverrideViolationReason Reason);

public sealed record OverrideChangeDecision(OverrideChangeVerdict Verdict, IReadOnlyList<OverrideViolation> Violations)
{
    public bool IsAllowed => Verdict == OverrideChangeVerdict.Allowed;
}

/// <summary>
/// Who may change an event's overrides and which overrides they may set (§4.4), as pure functions over the
/// engine. Only floor holders change overrides and floors ignore overrides, so an allowed change can never
/// lock its actor out (asserted).
/// </summary>
public static class OverridePolicy
{
    /// <summary>The override rights of <paramref name="actor"/> on <paramref name="ev"/> (exceptions: those on the series).</summary>
    public static OverrideRights RightsOf(PrincipalContext actor, CalendarAcl calendar, EventAcl ev) =>
        RightsOf(PermissionEngine.Resolve(actor, calendar, ev), calendar);

    /// <summary>
    /// The lowest calendar level every principal matched by <paramref name="principal"/> is guaranteed to hold:
    /// for a user their level (<paramref name="userCalendarLevels"/>; unknown users and pending email shares
    /// have <c>none</c>), for <c>group:G[r]</c> the best of the owner group's role defaults for roles ≥ r and
    /// the grants to <c>G[r']</c> with r' ≤ r. <c>everyone</c>/<c>anonymous</c> stay inside the audience by
    /// definition (§3) and get <see cref="CalendarLevel.Owner"/> here.
    /// </summary>
    public static CalendarLevel GuaranteedCalendarLevel(Principal principal, CalendarAcl calendar, IReadOnlyDictionary<Guid, CalendarLevel> userCalendarLevels)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(userCalendarLevels);

        if (principal.Type == PrincipalType.User)
        {
            return userCalendarLevels.GetValueOrDefault(principal.Id.GetValueOrDefault(), CalendarLevel.None);
        }

        if (principal.IsRestrictOnly)
        {
            return CalendarLevel.Owner;
        }

        var groupId = principal.Id.GetValueOrDefault();
        var minRole = principal.MinRole.GetValueOrDefault();
        var level = CalendarLevel.None;
        if (calendar.IsGroupOwned & calendar.Owner.Id.GetValueOrDefault() == groupId)
        {
            level = CalendarLevel.Owner;
            foreach (var role in GroupRoles.All)
            {
                level = role >= minRole ? PermissionLevels.Min(level, calendar.RoleDefaults.For(role)) : level;
            }
        }

        foreach (var grant in calendar.Grants)
        {
            var covers = grant.Principal.Type == PrincipalType.Group
                & grant.Principal.Id.GetValueOrDefault() == groupId
                & grant.Principal.MinRole.GetValueOrDefault() <= minRole;
            level = covers ? PermissionLevels.Max(level, grant.Level) : level;
        }

        return level;
    }

    /// <summary>
    /// Rule 7 / §4.4: the override shares outside the calendar's audience — its principal is not guaranteed
    /// <c>read</c> on the calendar and the override gives more than the calendar does (<c>none</c> and other
    /// restrictions of outsiders are not sharing).
    /// </summary>
    public static bool IsExternalSharing(EventOverride entry, CalendarAcl calendar, IReadOnlyDictionary<Guid, CalendarLevel> userCalendarLevels)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var guaranteed = GuaranteedCalendarLevel(entry.Principal, calendar, userCalendarLevels);
        return guaranteed < CalendarLevel.Read & entry.Level > PermissionLevels.ImpliedEventLevel(guaranteed);
    }

    /// <summary>§4.4 principal selection: groups the actor belongs to, the owner group, or groups holding a grant on the calendar.</summary>
    public static bool IsSelectableGroup(Guid groupId, PrincipalContext actor, CalendarAcl calendar)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(calendar);

        var selectable = actor.Groups.ContainsKey(groupId) | (calendar.IsGroupOwned & calendar.Owner.Id.GetValueOrDefault() == groupId);
        foreach (var grant in calendar.Grants)
        {
            selectable |= grant.Principal.Type == PrincipalType.Group & grant.Principal.Id.GetValueOrDefault() == groupId;
        }

        return selectable;
    }

    /// <summary>
    /// Validates replacing the overrides of <paramref name="ev"/> by <paramref name="proposed"/> (atomic
    /// replace). Removals are always allowed to rights holders; entries that are unchanged or only lowered are
    /// not re-checked for selection and external sharing (they decide nothing new); added and raised entries
    /// are. <paramref name="userCalendarLevels"/>: calendar levels of the users named by proposed entries.
    /// </summary>
    public static OverrideChangeDecision EvaluateChange(
        PrincipalContext actor,
        CalendarAcl calendar,
        EventAcl ev,
        IReadOnlyList<EventOverride> proposed,
        IReadOnlyDictionary<Guid, CalendarLevel> userCalendarLevels)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        ArgumentNullException.ThrowIfNull(userCalendarLevels);

        var access = PermissionEngine.Resolve(actor, calendar, ev);
        if (access.Level == EventLevel.None)
        {
            return new(OverrideChangeVerdict.NotFound, []);
        }

        var rights = RightsOf(access, calendar);
        if (rights == OverrideRights.None)
        {
            return new(OverrideChangeVerdict.Forbidden, []);
        }

        var source = ev.Series ?? ev;
        var current = new Dictionary<Principal, EventLevel>();
        foreach (var existing in source.Overrides)
        {
            current[existing.Principal] = PermissionLevels.Max(current.GetValueOrDefault(existing.Principal), existing.Level);
        }

        var violations = new List<OverrideViolation>();
        var seen = new HashSet<Principal>();
        foreach (var entry in proposed)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (entry.Level > PermissionLevels.MaxOverrideLevel)
            {
                violations.Add(new(entry, OverrideViolationReason.LevelTooHigh));
            }

            if (!seen.Add(entry.Principal))
            {
                violations.Add(new(entry, OverrideViolationReason.DuplicatePrincipal));
            }

            var unchangedOrLowered = current.TryGetValue(entry.Principal, out var before) && entry.Level <= before;
            if (unchangedOrLowered)
            {
                continue;
            }

            if (entry.Principal.Type == PrincipalType.Group && !IsSelectableGroup(entry.Principal.Id.GetValueOrDefault(), actor, calendar))
            {
                violations.Add(new(entry, OverrideViolationReason.GroupNotSelectable));
            }

            if (rights == OverrideRights.InternalOnly && IsExternalSharing(entry, calendar, userCalendarLevels))
            {
                violations.Add(new(entry, OverrideViolationReason.ExternalSharing));
            }
        }

        var invalid = false;
        var external = false;
        foreach (var violation in violations)
        {
            var isExternal = violation.Reason == OverrideViolationReason.ExternalSharing;
            external |= isExternal;
            invalid |= !isExternal;
        }

        var verdict = invalid ? OverrideChangeVerdict.Invalid
            : external ? OverrideChangeVerdict.ExternalSharingNotAllowed
            : OverrideChangeVerdict.Allowed;

        Debug.Assert(
            verdict != OverrideChangeVerdict.Allowed
                || PermissionEngine.Resolve(actor, calendar, new EventAcl(source.EventId, source.CalendarId, source.CreatorUserId, proposed)).Level == EventLevel.Manage,
            "An allowed override change must not lock its actor out (floors ignore overrides).");
        return new(verdict, violations);
    }

    /// <summary>
    /// §4.6 move: the overrides of <paramref name="ev"/> that <paramref name="mover"/> could not set in
    /// <paramref name="target"/> — external sharing there without external rights there (calendar
    /// <c>manage</c>, or the creator floor with <c>creatorsMayShareExternally</c> in the target).
    /// Non-empty → 409 <c>override_invalid_in_target</c> listing them.
    /// </summary>
    public static IReadOnlyList<EventOverride> InvalidInTarget(
        PrincipalContext mover,
        CalendarAcl target,
        EventAcl ev,
        IReadOnlyDictionary<Guid, CalendarLevel> userCalendarLevelsInTarget)
    {
        ArgumentNullException.ThrowIfNull(ev);
        var moved = new EventAcl(ev.EventId, target.CalendarId, ev.CreatorUserId, (ev.Series ?? ev).Overrides);
        var external = RightsOf(PermissionEngine.Resolve(mover, target, moved), target) == OverrideRights.IncludingExternal;

        var invalid = new List<EventOverride>();
        foreach (var entry in moved.Overrides)
        {
            if (!external && IsExternalSharing(entry, target, userCalendarLevelsInTarget))
            {
                invalid.Add(entry);
            }
        }

        return invalid;
    }

    private static OverrideRights RightsOf(EventAccess access, CalendarAcl calendar) =>
        access.Level < EventLevel.Manage ? OverrideRights.None
        : access.CalendarLevel >= CalendarLevel.Manage | calendar.CreatorsMayShareExternally ? OverrideRights.IncludingExternal
        : OverrideRights.InternalOnly;
}
