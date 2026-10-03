using SCalenderPlus.Core.Groups;

namespace SCalenderPlus.Core.Permissions;

/// <summary>
/// The permission algorithm of docs/architecture/permissions.md §3–§4 — the single source of truth for who
/// may see and change what. Pure and deterministic: plain inputs, no I/O, no clock; the result does not depend
/// on the order of grants or overrides. <see cref="Resolve"/>/<see cref="ResolveCalendar"/> return a trace for
/// the access explainer; <see cref="ResolveLevel"/>/<see cref="ResolveCalendarLevel"/> are the same algorithm
/// without a trace, for hot paths.
/// </summary>
public static class PermissionEngine
{
    /// <summary>§4.1: the level of <paramref name="principal"/> on <paramref name="calendar"/>, with its trace (for the explainer).</summary>
    public static CalendarAccess ResolveCalendar(PrincipalContext principal, CalendarAcl calendar)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(calendar);

        var steps = new List<ResolutionStep>();
        var level = CalendarLevelOf(principal, calendar, steps);
        steps.Add(new(ResolutionStepKind.CalendarResult) { CalendarLevel = level });
        return new(level, steps);
    }

    /// <summary>§4.1 without a trace: the same level as <see cref="ResolveCalendar"/>, allocation-free (for listings and feeds).</summary>
    public static CalendarLevel ResolveCalendarLevel(PrincipalContext principal, CalendarAcl calendar)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(calendar);

        return CalendarLevelOf(principal, calendar, null);
    }

    /// <summary>§4.2: the level of <paramref name="principal"/> on <paramref name="ev"/> of <paramref name="calendar"/>, with its trace (for the explainer).</summary>
    public static EventAccess Resolve(PrincipalContext principal, CalendarAcl calendar, EventAcl ev)
    {
        CheckEvent(principal, calendar, ev);

        var steps = new List<ResolutionStep>();
        var lc = CalendarLevelOf(principal, calendar, steps);
        steps.Add(new(ResolutionStepKind.CalendarResult) { CalendarLevel = lc });

        // Exceptions inherit the series ACL (MVP).
        if (ev.Series is { } series)
        {
            steps.Add(new(ResolutionStepKind.InheritedFromSeries) { ResourceId = series.EventId });
        }

        var level = EventLevelOf(principal, calendar, ev.Series ?? ev, lc, steps);
        steps.Add(new(ResolutionStepKind.Result) { EventLevel = level });
        return new(level, lc, steps);
    }

    /// <summary>
    /// §4.2 without a trace: the same level as <see cref="Resolve"/>, but allocation-free — the hot path of
    /// window queries and feeds, which resolve every event of a listing.
    /// </summary>
    public static EventLevel ResolveLevel(PrincipalContext principal, CalendarAcl calendar, EventAcl ev)
    {
        CheckEvent(principal, calendar, ev);

        var lc = CalendarLevelOf(principal, calendar, null);
        return EventLevelOf(principal, calendar, ev.Series ?? ev, lc, null);
    }

    /// <summary>
    /// §3 matching. <paramref name="calendarLevel"/> is the principal's level on the calendar: <c>everyone</c>
    /// and <c>anonymous</c> only match its audience (level ≥ <c>free_busy</c>), never strangers.
    /// </summary>
    public static bool Matches(Principal rule, PrincipalContext principal, CalendarLevel calendarLevel)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(principal);

        return MatchesCore(rule, principal, calendarLevel);
    }

    private static void CheckEvent(PrincipalContext principal, CalendarAcl calendar, EventAcl ev)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(ev);
        if (ev.CalendarId != calendar.CalendarId)
        {
            throw new ArgumentException("The event belongs to another calendar.", nameof(ev));
        }
    }

    private static bool MatchesCore(Principal rule, PrincipalContext principal, CalendarLevel calendarLevel)
    {
        var inAudience = calendarLevel >= CalendarLevel.FreeBusy;
        return rule.Type switch
        {
            PrincipalType.User => rule.Id.GetValueOrDefault() == principal.UserId.GetValueOrDefault(),
            PrincipalType.Group => principal.RoleIn(rule.Id.GetValueOrDefault()) >= rule.MinRole,
            PrincipalType.Anonymous => principal.IsAnonymous & inAudience,
            _ => inAudience,
        };
    }

    // The resolution itself. `steps` is null on the untraced path: `steps?.Add(new …)` then neither allocates the
    // step nor the list, and index loops avoid boxed enumerators of the IReadOnlyList inputs.
    private static CalendarLevel CalendarLevelOf(PrincipalContext principal, CalendarAcl calendar, List<ResolutionStep>? steps)
    {
        if (principal.IsAnonymous)
        {
            // Grants name users and groups only, so a link holder's level is the link's — on its own calendar.
            var link = principal.LinkCalendarId.GetValueOrDefault() == calendar.CalendarId ? principal.LinkLevel : CalendarLevel.None;
            steps?.Add(new(ResolutionStepKind.ShareLink) { CalendarLevel = link });
            return link;
        }

        var owner = calendar.Owner;
        var ownerGroupRole = calendar.IsGroupOwned ? principal.RoleIn(owner.Id.GetValueOrDefault()) : null;
        var isOwner = calendar.IsGroupOwned ? ownerGroupRole == GroupRole.Owner : owner.Id.GetValueOrDefault() == principal.UserId.GetValueOrDefault();
        if (isOwner)
        {
            steps?.Add(new(ResolutionStepKind.CalendarOwner) { Principal = owner });
            return CalendarLevel.Owner;
        }

        var level = CalendarLevel.None;
        var grants = calendar.Grants;
        for (var i = 0; i < grants.Count; i++)
        {
            var grant = grants[i];
            if (MatchesCore(grant.Principal, principal, CalendarLevel.None))
            {
                steps?.Add(new(ResolutionStepKind.GrantMatched) { Principal = grant.Principal, CalendarLevel = grant.Level });
                level = PermissionLevels.Max(level, grant.Level);
            }
        }

        if (ownerGroupRole is { } role)
        {
            var roleDefault = calendar.RoleDefaults.For(role);
            steps?.Add(new(ResolutionStepKind.GroupRoleDefault) { Principal = owner, Role = role, CalendarLevel = roleDefault });
            level = PermissionLevels.Max(level, roleDefault);
        }

        return level;
    }

    private static EventLevel EventLevelOf(PrincipalContext principal, CalendarAcl calendar, EventAcl ev, CalendarLevel lc, List<ResolutionStep>? steps)
    {
        // Step 1 – floors that overrides can never reduce.
        if (lc >= CalendarLevel.Manage)
        {
            steps?.Add(new(ResolutionStepKind.ManagerFloor) { EventLevel = EventLevel.Manage });
            return EventLevel.Manage;
        }

        if (ev.CreatorUserId is { } creator && creator == principal.UserId)
        {
            if (!calendar.CreatorsManageOwnEvents)
            {
                steps?.Add(new(ResolutionStepKind.CreatorFloorDisabled));
            }
            else if (lc < CalendarLevel.Contribute)
            {
                steps?.Add(new(ResolutionStepKind.CreatorFloorLapsed) { CalendarLevel = lc });
            }
            else
            {
                steps?.Add(new(ResolutionStepKind.CreatorFloor) { EventLevel = EventLevel.Manage });
                return EventLevel.Manage;
            }
        }

        // Step 2 – base level derived from the calendar.
        var baseLevel = PermissionLevels.ImpliedEventLevel(lc);
        steps?.Add(new(ResolutionStepKind.BaseLevel) { CalendarLevel = lc, EventLevel = baseLevel });

        // Step 3 – overrides: the most specific matching tier wins, ties are unions (max). Tier 0 = no match.
        var topTier = 0;
        var level = EventLevel.None;
        var overrides = ev.Overrides;
        for (var i = 0; i < overrides.Count; i++)
        {
            var candidate = overrides[i];
            if (!MatchesCore(candidate.Principal, principal, lc))
            {
                continue;
            }

            var tier = candidate.Principal.Tier;
            if (tier > topTier)
            {
                topTier = tier;
                level = candidate.Level;
            }
            else if (tier == topTier)
            {
                level = PermissionLevels.Max(level, candidate.Level);
            }
        }

        if (steps is not null)
        {
            TraceMatchedOverrides(principal, overrides, lc, topTier, steps);
        }

        if (topTier == 0)
        {
            steps?.Add(new(ResolutionStepKind.NoMatchingOverride) { EventLevel = baseLevel });
            level = baseLevel;
        }
        else
        {
            // The override replaces the base: lower (restrict) or, for user/group tiers only, higher (elevate).
            steps?.Add(new(ResolutionStepKind.OverrideApplied) { EventLevel = level, Tier = topTier });
            if (topTier <= Principal.Anonymous.Tier)
            {
                var restricted = PermissionLevels.Min(level, baseLevel);
                steps?.Add(new(ResolutionStepKind.RestrictOnly) { EventLevel = restricted, Applied = restricted < level });
                level = restricted;
            }
        }

        // Step 4 – caps: manage only via floors; link holders at most min(read, link level).
        if (level > PermissionLevels.MaxOverrideLevel)
        {
            level = PermissionLevels.MaxOverrideLevel;
            steps?.Add(new(ResolutionStepKind.OverrideCap) { EventLevel = level });
        }

        if (principal.IsAnonymous)
        {
            var ceiling = PermissionLevels.Min(EventLevel.Read, PermissionLevels.ImpliedEventLevel(principal.LinkLevel));
            level = PermissionLevels.Min(level, ceiling);
            steps?.Add(new(ResolutionStepKind.LinkCeiling) { EventLevel = level });
        }

        return level;
    }

    private static void TraceMatchedOverrides(PrincipalContext principal, IReadOnlyList<EventOverride> overrides, CalendarLevel lc, int topTier, List<ResolutionStep> steps)
    {
        foreach (var candidate in overrides)
        {
            if (MatchesCore(candidate.Principal, principal, lc))
            {
                steps.Add(new(ResolutionStepKind.OverrideMatched)
                {
                    Principal = candidate.Principal,
                    EventLevel = candidate.Level,
                    Tier = candidate.Principal.Tier,
                    Decisive = candidate.Principal.Tier == topTier,
                });
            }
        }
    }
}
