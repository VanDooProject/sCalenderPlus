using SCalenderPlus.Core.Groups;

namespace SCalenderPlus.Core.Permissions;

/// <summary>
/// The permission algorithm of docs/architecture/permissions.md §3–§4 — the single source of truth for who
/// may see and change what. Pure and deterministic: plain inputs, no I/O, no clock; the result does not depend
/// on the order of grants or overrides. Every resolution returns a trace for the access explainer.
/// </summary>
public static class PermissionEngine
{
    /// <summary>§4.1: the level of <paramref name="principal"/> on <paramref name="calendar"/>.</summary>
    public static CalendarAccess ResolveCalendar(PrincipalContext principal, CalendarAcl calendar)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(calendar);

        var steps = new List<ResolutionStep>();
        var level = CalendarLevelOf(principal, calendar, steps);
        steps.Add(new(ResolutionStepKind.CalendarResult) { CalendarLevel = level });
        return new(level, steps);
    }

    /// <summary>§4.2: the level of <paramref name="principal"/> on <paramref name="ev"/> of <paramref name="calendar"/>.</summary>
    public static EventAccess Resolve(PrincipalContext principal, CalendarAcl calendar, EventAcl ev)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(ev);
        if (ev.CalendarId != calendar.CalendarId)
        {
            throw new ArgumentException("The event belongs to another calendar.", nameof(ev));
        }

        var steps = new List<ResolutionStep>();
        var lc = CalendarLevelOf(principal, calendar, steps);
        steps.Add(new(ResolutionStepKind.CalendarResult) { CalendarLevel = lc });

        // Exceptions inherit the series ACL (MVP).
        var source = ev;
        if (ev.Series is { } series)
        {
            steps.Add(new(ResolutionStepKind.InheritedFromSeries) { ResourceId = series.EventId });
            source = series;
        }

        var level = EventLevelOf(principal, calendar, source, lc, steps);
        steps.Add(new(ResolutionStepKind.Result) { EventLevel = level });
        return new(level, lc, steps);
    }

    /// <summary>
    /// §3 matching. <paramref name="calendarLevel"/> is the principal's level on the calendar: <c>everyone</c>
    /// and <c>anonymous</c> only match its audience (level ≥ <c>free_busy</c>), never strangers.
    /// </summary>
    public static bool Matches(Principal rule, PrincipalContext principal, CalendarLevel calendarLevel)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(principal);

        var inAudience = calendarLevel >= CalendarLevel.FreeBusy;
        return rule.Type switch
        {
            PrincipalType.User => rule.Id.GetValueOrDefault() == principal.UserId.GetValueOrDefault(),
            PrincipalType.Group => principal.RoleIn(rule.Id.GetValueOrDefault()) >= rule.MinRole,
            PrincipalType.Anonymous => principal.IsAnonymous & inAudience,
            _ => inAudience,
        };
    }

    private static CalendarLevel CalendarLevelOf(PrincipalContext principal, CalendarAcl calendar, List<ResolutionStep> steps)
    {
        if (principal.IsAnonymous)
        {
            // Grants name users and groups only, so a link holder's level is the link's — on its own calendar.
            var link = principal.LinkCalendarId.GetValueOrDefault() == calendar.CalendarId ? principal.LinkLevel : CalendarLevel.None;
            steps.Add(new(ResolutionStepKind.ShareLink) { CalendarLevel = link });
            return link;
        }

        var owner = calendar.Owner;
        var ownerGroupRole = calendar.IsGroupOwned ? principal.RoleIn(owner.Id.GetValueOrDefault()) : null;
        var isOwner = calendar.IsGroupOwned ? ownerGroupRole == GroupRole.Owner : owner.Id.GetValueOrDefault() == principal.UserId.GetValueOrDefault();
        if (isOwner)
        {
            steps.Add(new(ResolutionStepKind.CalendarOwner) { Principal = owner });
            return CalendarLevel.Owner;
        }

        var level = CalendarLevel.None;
        foreach (var grant in calendar.Grants)
        {
            if (Matches(grant.Principal, principal, CalendarLevel.None))
            {
                steps.Add(new(ResolutionStepKind.GrantMatched) { Principal = grant.Principal, CalendarLevel = grant.Level });
                level = PermissionLevels.Max(level, grant.Level);
            }
        }

        if (ownerGroupRole is { } role)
        {
            var roleDefault = calendar.RoleDefaults.For(role);
            steps.Add(new(ResolutionStepKind.GroupRoleDefault) { Principal = owner, Role = role, CalendarLevel = roleDefault });
            level = PermissionLevels.Max(level, roleDefault);
        }

        return level;
    }

    private static EventLevel EventLevelOf(PrincipalContext principal, CalendarAcl calendar, EventAcl ev, CalendarLevel lc, List<ResolutionStep> steps)
    {
        // Step 1 – floors that overrides can never reduce.
        if (lc >= CalendarLevel.Manage)
        {
            steps.Add(new(ResolutionStepKind.ManagerFloor) { EventLevel = EventLevel.Manage });
            return EventLevel.Manage;
        }

        if (ev.CreatorUserId is { } creator && creator == principal.UserId)
        {
            if (!calendar.CreatorsManageOwnEvents)
            {
                steps.Add(new(ResolutionStepKind.CreatorFloorDisabled));
            }
            else if (lc < CalendarLevel.Contribute)
            {
                steps.Add(new(ResolutionStepKind.CreatorFloorLapsed) { CalendarLevel = lc });
            }
            else
            {
                steps.Add(new(ResolutionStepKind.CreatorFloor) { EventLevel = EventLevel.Manage });
                return EventLevel.Manage;
            }
        }

        // Step 2 – base level derived from the calendar.
        var baseLevel = PermissionLevels.ImpliedEventLevel(lc);
        steps.Add(new(ResolutionStepKind.BaseLevel) { CalendarLevel = lc, EventLevel = baseLevel });

        // Step 3 – overrides: the most specific matching tier wins, ties are unions (max).
        var topTier = 0;
        var level = EventLevel.None;
        var matched = new List<EventOverride>();
        foreach (var candidate in ev.Overrides)
        {
            if (Matches(candidate.Principal, principal, lc))
            {
                matched.Add(candidate);
                var tier = candidate.Principal.Tier;
                level = tier > topTier ? candidate.Level : tier == topTier ? PermissionLevels.Max(level, candidate.Level) : level;
                topTier = Math.Max(topTier, tier);
            }
        }

        foreach (var match in matched)
        {
            steps.Add(new(ResolutionStepKind.OverrideMatched)
            {
                Principal = match.Principal,
                EventLevel = match.Level,
                Tier = match.Principal.Tier,
                Decisive = match.Principal.Tier == topTier,
            });
        }

        if (matched.Count == 0)
        {
            steps.Add(new(ResolutionStepKind.NoMatchingOverride) { EventLevel = baseLevel });
            level = baseLevel;
        }
        else
        {
            // The override replaces the base: lower (restrict) or, for user/group tiers only, higher (elevate).
            steps.Add(new(ResolutionStepKind.OverrideApplied) { EventLevel = level, Tier = topTier });
            if (topTier <= Principal.Anonymous.Tier)
            {
                var restricted = PermissionLevels.Min(level, baseLevel);
                steps.Add(new(ResolutionStepKind.RestrictOnly) { EventLevel = restricted, Applied = restricted < level });
                level = restricted;
            }
        }

        // Step 4 – caps: manage only via floors; link holders at most min(read, link level).
        if (level > PermissionLevels.MaxOverrideLevel)
        {
            level = PermissionLevels.MaxOverrideLevel;
            steps.Add(new(ResolutionStepKind.OverrideCap) { EventLevel = level });
        }

        if (principal.IsAnonymous)
        {
            var ceiling = PermissionLevels.Min(EventLevel.Read, PermissionLevels.ImpliedEventLevel(principal.LinkLevel));
            level = PermissionLevels.Min(level, ceiling);
            steps.Add(new(ResolutionStepKind.LinkCeiling) { EventLevel = level });
        }

        return level;
    }
}
