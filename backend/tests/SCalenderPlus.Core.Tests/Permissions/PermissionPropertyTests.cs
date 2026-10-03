using CsCheck;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Core.Tests.Permissions;

/// <summary>Property tests of the invariants of docs/architecture/permissions.md §8 (CsCheck, random worlds of <see cref="Scenario"/>).</summary>
public sealed class PermissionPropertyTests
{
    private const int Iterations = 3000;

    [Fact]
    public void Managers_and_owners_never_get_less_than_manage() =>
        Scenario.Any.Sample(s => s.CalendarLevel < CalendarLevel.Manage || s.Resolve().Level == EventLevel.Manage, iter: Iterations);

    [Fact]
    public void Floors_are_never_lowered_by_overrides() =>
        Scenario.Any.Sample(s => !s.HasFloor || s.Resolve().Level == EventLevel.Manage, iter: Iterations);

    [Fact]
    public void No_override_yields_manage() =>
        Scenario.Any.Sample(s => s.HasFloor || s.Resolve().Level <= EventLevel.Edit, iter: Iterations);

    [Fact]
    public void Manage_comes_only_from_a_floor() =>
        Scenario.Any.Sample(s => (s.Resolve().Level == EventLevel.Manage) == s.HasFloor, iter: Iterations);

    [Fact]
    public void Anonymous_never_exceeds_read_nor_the_link_level() =>
        Scenario.Any.Sample(
            s => !s.Viewer.IsAnonymous
                || (s.Resolve().Level <= EventLevel.Read
                    && s.Resolve().Level <= PermissionLevels.ImpliedEventLevel(s.Viewer.LinkLevel)
                    && s.CalendarLevel <= s.Viewer.LinkLevel),
            iter: Iterations);

    [Fact]
    public void Links_of_other_calendars_see_nothing() =>
        Scenario.Any.Sample(
            s => s.Viewer.LinkCalendarId is not { } linked || linked == s.Calendar.CalendarId || s.Resolve().Level == EventLevel.None,
            iter: Iterations);

    [Fact]
    public void Adding_a_calendar_grant_never_reduces_a_calendar_level() =>
        Gen.Select(Scenario.Any, Scenario.Grant).Sample(
            t =>
            {
                var (s, grant) = t;
                var more = s.WithGrants([.. s.Calendar.Grants, grant]);
                return more.CalendarLevel >= s.CalendarLevel;
            },
            iter: Iterations);

    [Fact]
    public void Adding_a_calendar_grant_never_reduces_an_event_level() =>
        Gen.Select(Scenario.Any, Scenario.Grant).Sample(
            t =>
            {
                var (s, grant) = t;
                var more = s.WithGrants([.. s.Calendar.Grants, grant]);
                return more.Resolve().Level >= s.Resolve().Level;
            },
            iter: Iterations);

    [Fact]
    public void Without_overrides_the_event_level_is_implied_by_the_calendar_level_unless_a_floor_applies() =>
        Scenario.Any.Sample(
            s =>
            {
                var plain = s.WithEvent([]);
                return plain.HasFloor || plain.Resolve().Level == PermissionLevels.ImpliedEventLevel(plain.CalendarLevel);
            },
            iter: Iterations);

    [Fact]
    public void Everyone_and_anonymous_overrides_never_elevate() =>
        Gen.Select(Scenario.Any, Scenario.RestrictOnlyOverride.Array[1, 4]).Sample(
            t =>
            {
                var (s, restrictOnly) = t;
                var plain = s.WithEvent([]).Resolve().Level;
                return s.WithEvent(restrictOnly).Resolve().Level <= plain;
            },
            iter: Iterations);

    [Fact]
    public void Everyone_and_anonymous_overrides_never_reach_strangers() =>
        Scenario.Any.Sample(
            s =>
            {
                var restrictOnly = (s.Event.Series ?? s.Event).Overrides.Where(o => o.Principal.IsRestrictOnly).ToList();
                return s.CalendarLevel > CalendarLevel.None || s.WithEvent(restrictOnly).Resolve().Level == EventLevel.None;
            },
            iter: Iterations);

    [Fact]
    public void A_matching_user_override_decides_for_users_without_a_floor() =>
        Gen.Select(Scenario.Any, Scenario.AnyOverrideLevel).Sample(
            t =>
            {
                var (s, level) = t;
                if (s.Viewer.UserId is not { } user)
                {
                    return true;
                }

                var withUser = s.WithEvent([.. (s.Event.Series ?? s.Event).Overrides.Where(o => o.Principal != Principal.User(user)), new(Principal.User(user), level)]);
                return withUser.HasFloor || withUser.Resolve().Level == PermissionLevels.Min(level, EventLevel.Edit);
            },
            iter: Iterations);

    [Fact]
    public void The_order_of_grants_and_overrides_does_not_matter() =>
        Scenario.Any.Sample(
            s =>
            {
                var reversed = s.WithGrants([.. s.Calendar.Grants.Reverse()])
                    .WithEvent([.. (s.Event.Series ?? s.Event).Overrides.Reverse()]);
                return reversed.Resolve().Level == s.Resolve().Level;
            },
            iter: Iterations);

    [Fact]
    public void Exceptions_resolve_like_their_series() =>
        Scenario.Any.Sample(
            s =>
            {
                var series = s.Event.Series ?? s.Event;
                var exception = EventAcl.ExceptionOf(series, Guid.CreateVersion7());
                return PermissionEngine.Resolve(s.Viewer, s.Calendar, exception).Level == PermissionEngine.Resolve(s.Viewer, s.Calendar, series).Level;
            },
            iter: Iterations);

    [Fact]
    public void The_trace_ends_with_the_result_and_records_the_calendar_level() =>
        Scenario.Any.Sample(
            s =>
            {
                var access = s.Resolve();
                return access.Steps[^1] == new ResolutionStep(ResolutionStepKind.Result) { EventLevel = access.Level }
                    && access.Steps.Single(x => x.Kind == ResolutionStepKind.CalendarResult).CalendarLevel == access.CalendarLevel
                    && access.CalendarLevel == s.CalendarLevel;
            },
            iter: Iterations);

    [Fact]
    public void Only_floor_holders_may_change_overrides() =>
        Gen.Select(Scenario.Valid, Scenario.ValidOverride.Array[0, 4]).Sample(
            t =>
            {
                var (s, proposed) = t;
                var decision = OverridePolicy.EvaluateChange(s.Viewer, s.Calendar, s.Event, proposed, s.UserLevels());
                var level = s.Resolve().Level;
                return decision.Verdict switch
                {
                    OverrideChangeVerdict.NotFound => level == EventLevel.None,
                    OverrideChangeVerdict.Forbidden => level is > EventLevel.None and < EventLevel.Manage,
                    _ => level == EventLevel.Manage,
                };
            },
            iter: Iterations);

    [Fact]
    public void An_allowed_override_change_never_locks_out_its_actor() =>
        Gen.Select(Scenario.Valid, Scenario.ValidOverride.Array[0, 5]).Sample(
            t =>
            {
                var (s, proposed) = t;
                var decision = OverridePolicy.EvaluateChange(s.Viewer, s.Calendar, s.Event, proposed, s.UserLevels());
                return !decision.IsAllowed || s.WithEvent(proposed).Resolve().Level == EventLevel.Manage;
            },
            iter: Iterations);

    [Fact]
    public void Creators_without_external_rights_never_raise_anyone_outside_the_audience_above_the_calendar() =>
        Gen.Select(
            Scenario.CreatorWithoutExternalRights,
            Scenario.ValidOverride.Array[1, 3]).Sample(
            t =>
            {
                var (s, proposed) = t;
                var levels = s.UserLevels();
                var decision = OverridePolicy.EvaluateChange(s.Viewer, s.Calendar, s.WithEvent([]).Event, proposed, levels);
                if (!decision.IsAllowed)
                {
                    return true;
                }

                // Every user below read on the calendar ends up with at most what the calendar gives them.
                var after = s.WithEvent(proposed);
                return Scenario.Users.Where(u => levels[u] < CalendarLevel.Read).All(u =>
                    PermissionEngine.Resolve(after.ContextOf(u), after.Calendar, after.Event).Level
                        <= PermissionLevels.Max(PermissionLevels.ImpliedEventLevel(levels[u]), FloorOf(after, u)));
            },
            iter: Iterations);

    [Fact]
    public void External_sharing_is_refused_exactly_when_it_would_raise_an_outsider() =>
        Gen.Select(
            Scenario.CreatorWithoutExternalRights,
            Scenario.UserPrincipal,
            Scenario.OverrideLevel).Sample(
            t =>
            {
                var (s, principal, level) = t;
                var levels = s.UserLevels();
                var target = levels[principal.Id!.Value];
                var decision = OverridePolicy.EvaluateChange(s.Viewer, s.Calendar, s.WithEvent([]).Event, [new(principal, level)], levels);
                var raisesOutsider = target < CalendarLevel.Read && level > PermissionLevels.ImpliedEventLevel(target);
                return decision.Verdict == (raisesOutsider ? OverrideChangeVerdict.ExternalSharingNotAllowed : OverrideChangeVerdict.Allowed);
            },
            iter: Iterations);

    [Fact]
    public void Overrides_above_edit_are_always_refused() =>
        Gen.Select(Scenario.Valid, Scenario.AnyPrincipal).Sample(
            t =>
            {
                var (s, principal) = t;
                var decision = OverridePolicy.EvaluateChange(s.Viewer, s.Calendar, s.Event, [new(principal, EventLevel.Manage)], s.UserLevels());
                return decision.Verdict is OverrideChangeVerdict.NotFound or OverrideChangeVerdict.Forbidden or OverrideChangeVerdict.Invalid;
            },
            iter: Iterations);

    [Fact]
    public void Traced_and_untraced_resolution_agree() =>
        Scenario.Any.Sample(
            s => PermissionEngine.ResolveLevel(s.Viewer, s.Calendar, s.Event) == s.Resolve().Level
                && PermissionEngine.ResolveCalendarLevel(s.Viewer, s.Calendar) == s.CalendarLevel,
            iter: Iterations);

    [Fact]
    public void Overrides_only_affect_the_principals_they_match() =>
        Gen.Select(Scenario.Any, Scenario.Override).Sample(
            t =>
            {
                var (s, extra) = t;
                var before = s.Resolve();
                return PermissionEngine.Matches(extra.Principal, s.Viewer, before.CalendarLevel)
                    || s.WithEvent([.. (s.Event.Series ?? s.Event).Overrides, extra]).Resolve().Level == before.Level;
            },
            iter: Iterations);

    [Fact]
    public void Adding_a_none_override_never_raises_anyone() =>
        Gen.Select(Scenario.Any, Scenario.AnyPrincipal).Sample(
            t =>
            {
                var (s, principal) = t;
                var more = s.WithEvent([.. (s.Event.Series ?? s.Event).Overrides, new(principal, EventLevel.None)]);
                return more.Resolve().Level <= s.Resolve().Level;
            },
            iter: Iterations);

    /// <summary>
    /// Atomic replace from any existing set (e.g. shares and exclusions a manager made): a creator without
    /// external rights never leaves an outsider with more than they had before or the calendar gives them —
    /// removals included (review 2026-10 permission engine, finding P1).
    /// </summary>
    [Fact]
    public void Creators_without_external_rights_never_raise_an_outsider_by_any_change() =>
        Gen.Select(
            Scenario.CreatorWithoutExternalRights,
            Scenario.ValidOverride.Array[0, 4],
            Gen.Int[0, 15]).Sample(
            t =>
            {
                var (s, added, keepMask) = t;
                var current = (s.Event.Series ?? s.Event).Overrides;
                var proposed = current.Where((_, i) => (keepMask & (1 << i)) != 0).Concat(added).ToList();
                var levels = s.UserLevels();
                var decision = OverridePolicy.EvaluateChange(s.Viewer, s.Calendar, s.Event, proposed, levels);
                if (!decision.IsAllowed)
                {
                    return true;
                }

                var after = s.WithEvent(proposed);
                return Scenario.Users.Where(u => levels[u] < CalendarLevel.Read).All(u =>
                {
                    var was = PermissionEngine.ResolveLevel(s.ContextOf(u), s.Calendar, s.Event);
                    var now = PermissionEngine.ResolveLevel(after.ContextOf(u), after.Calendar, after.Event);
                    return now <= PermissionLevels.Max(PermissionLevels.Max(was, PermissionLevels.ImpliedEventLevel(levels[u])), FloorOf(after, u));
                });
            },
            iter: Iterations);

    /// <summary>§4.6: overrides that survive a move are ones the mover could have set in the target themselves.</summary>
    [Fact]
    public void A_move_carries_only_overrides_the_mover_could_set_in_the_target() =>
        Gen.Select(Scenario.Valid, Scenario.CalendarGen, Scenario.ValidOverride.Array[1, 4]).Sample(
            t =>
            {
                var (s, other, overrides) = t;
                var target = new CalendarAcl(Scenario.OtherCalendarId, other.Owner, other.Grants, other.RoleDefaults, other.CreatorsManageOwnEvents, other.CreatorsMayShareExternally);
                if (s.Viewer.UserId is null || PermissionEngine.ResolveCalendarLevel(s.Viewer, target) < CalendarLevel.Contribute)
                {
                    return true; // AccessPolicy.CanMoveEvent refuses the move anyway.
                }

                var levels = Scenario.Users.ToDictionary(u => u, u => PermissionEngine.ResolveCalendarLevel(s.ContextOf(u), target));
                var ev = new EventAcl(Guid.CreateVersion7(), Scenario.CalendarId, (s.Event.Series ?? s.Event).CreatorUserId, overrides);
                if (OverridePolicy.InvalidInTarget(s.Viewer, target, ev, levels).Count > 0)
                {
                    return true;
                }

                var fresh = new EventAcl(ev.EventId, target.CalendarId, ev.CreatorUserId);
                var decision = OverridePolicy.EvaluateChange(s.Viewer, target, fresh, overrides, levels);
                return decision.Verdict is OverrideChangeVerdict.Allowed or OverrideChangeVerdict.Invalid // selection/duplicates are not re-checked on moves
                    && decision.Violations.All(v => v.Reason != OverrideViolationReason.ExternalSharing);
            },
            iter: Iterations);

    private static EventLevel FloorOf(Scenario s, Guid user) =>
        (s with { Viewer = s.ContextOf(user) }).HasFloor ? EventLevel.Manage : EventLevel.None;
}
