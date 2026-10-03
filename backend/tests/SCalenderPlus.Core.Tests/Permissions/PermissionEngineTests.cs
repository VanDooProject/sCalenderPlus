using SCalenderPlus.Core.Groups;
using SCalenderPlus.Core.Permissions;
using static SCalenderPlus.Core.Permissions.ResolutionStepKind;

namespace SCalenderPlus.Core.Tests.Permissions;

/// <summary>The rules of §3–§4.2 one by one (the worked examples are in <see cref="WorkedExamplesTests"/>).</summary>
public sealed class PermissionEngineTests
{
    internal static readonly Guid CalendarId = Guid.CreateVersion7();
    internal static readonly Guid Owner = Guid.CreateVersion7();
    internal static readonly Guid Anna = Guid.CreateVersion7();
    internal static readonly Guid Ben = Guid.CreateVersion7();
    internal static readonly Guid Club = Guid.CreateVersion7();
    internal static readonly Guid Board = Guid.CreateVersion7();

    private static CalendarAcl UserCalendar(params CalendarGrant[] grants) => new(CalendarId, Principal.User(Owner), grants);

    private static EventAcl Event(Guid? creator, params EventOverride[] overrides) => new(Guid.CreateVersion7(), CalendarId, creator, overrides);

    private static PrincipalContext User(Guid id, params (Guid Group, GroupRole Role)[] groups) =>
        PrincipalContext.ForUser(id, groups.ToDictionary(g => g.Group, g => g.Role));

    [Fact]
    public void The_owner_of_a_user_calendar_is_owner()
    {
        var access = PermissionEngine.ResolveCalendar(User(Owner), UserCalendar());

        Assert.Equal(CalendarLevel.Owner, access.Level);
        Assert.Equal([new(CalendarOwner) { Principal = Principal.User(Owner) }, new(CalendarResult) { CalendarLevel = CalendarLevel.Owner }], access.Steps);
    }

    [Fact]
    public void Every_role_owner_of_the_owning_group_is_owner_and_other_roles_get_role_defaults()
    {
        var calendar = new CalendarAcl(CalendarId, Principal.Group(Club), roleDefaults: new(CalendarLevel.Edit, CalendarLevel.Read, CalendarLevel.FreeBusy));

        Assert.Equal(CalendarLevel.Owner, PermissionEngine.ResolveCalendar(User(Anna, (Club, GroupRole.Owner)), calendar).Level);
        Assert.Equal(CalendarLevel.Edit, PermissionEngine.ResolveCalendar(User(Anna, (Club, GroupRole.Admin)), calendar).Level);
        Assert.Equal(CalendarLevel.Read, PermissionEngine.ResolveCalendar(User(Anna, (Club, GroupRole.Member)), calendar).Level);
        Assert.Equal(CalendarLevel.FreeBusy, PermissionEngine.ResolveCalendar(User(Anna, (Club, GroupRole.Viewer)), calendar).Level);
        Assert.Equal(CalendarLevel.None, PermissionEngine.ResolveCalendar(User(Anna, (Board, GroupRole.Owner)), calendar).Level);

        var steps = PermissionEngine.ResolveCalendar(User(Anna, (Club, GroupRole.Member)), calendar).Steps;
        Assert.Equal(new ResolutionStep(GroupRoleDefault) { Principal = Principal.Group(Club), Role = GroupRole.Member, CalendarLevel = CalendarLevel.Read }, steps[0]);
    }

    [Fact]
    public void Grants_are_additive_and_group_grants_respect_the_minimum_role()
    {
        var calendar = UserCalendar(
            new(Principal.Group(Club), CalendarLevel.FreeBusy),
            new(Principal.Group(Club, GroupRole.Admin), CalendarLevel.Edit),
            new(Principal.User(Anna), CalendarLevel.Read));

        Assert.Equal(CalendarLevel.Read, PermissionEngine.ResolveCalendar(User(Anna, (Club, GroupRole.Member)), calendar).Level);
        Assert.Equal(CalendarLevel.Edit, PermissionEngine.ResolveCalendar(User(Anna, (Club, GroupRole.Admin)), calendar).Level);
        Assert.Equal(CalendarLevel.FreeBusy, PermissionEngine.ResolveCalendar(User(Ben, (Club, GroupRole.Viewer)), calendar).Level);
        Assert.Equal(CalendarLevel.None, PermissionEngine.ResolveCalendar(User(Ben), calendar).Level);

        var steps = PermissionEngine.ResolveCalendar(User(Anna, (Club, GroupRole.Admin)), calendar).Steps;
        Assert.Equal(3, steps.Count(s => s.Kind == GrantMatched));
    }

    [Fact]
    public void Group_role_defaults_and_grants_combine_by_maximum()
    {
        var calendar = new CalendarAcl(
            CalendarId,
            Principal.Group(Club),
            [new(Principal.User(Anna), CalendarLevel.Edit)],
            new(CalendarLevel.Manage, CalendarLevel.Read, CalendarLevel.Read));

        Assert.Equal(CalendarLevel.Edit, PermissionEngine.ResolveCalendar(User(Anna, (Club, GroupRole.Member)), calendar).Level);
        Assert.Equal(CalendarLevel.Manage, PermissionEngine.ResolveCalendar(User(Anna, (Club, GroupRole.Admin)), calendar).Level);
    }

    [Fact]
    public void A_share_link_only_opens_its_own_calendar()
    {
        var calendar = UserCalendar(new CalendarGrant(Principal.Group(Club), CalendarLevel.Edit));

        var own = PermissionEngine.ResolveCalendar(PrincipalContext.ForShareLink(CalendarId, CalendarLevel.Read), calendar);
        var other = PermissionEngine.ResolveCalendar(PrincipalContext.ForShareLink(Guid.CreateVersion7(), CalendarLevel.Read), calendar);

        Assert.Equal(CalendarLevel.Read, own.Level);
        Assert.Equal(new ResolutionStep(ShareLink) { CalendarLevel = CalendarLevel.Read }, own.Steps[0]);
        Assert.Equal(CalendarLevel.None, other.Level);
        Assert.Equal(EventLevel.None, PermissionEngine.Resolve(PrincipalContext.ForShareLink(Guid.CreateVersion7(), CalendarLevel.Read), calendar, Event(null, new EventOverride(Principal.Anonymous, EventLevel.Read))).Level);
    }

    [Fact]
    public void Link_holders_get_at_most_the_link_level()
    {
        var calendar = UserCalendar();
        var link = PrincipalContext.ForShareLink(CalendarId, CalendarLevel.Read);

        Assert.Equal(EventLevel.Read, PermissionEngine.Resolve(link, calendar, Event(Owner)).Level);
        Assert.Equal(EventLevel.FreeBusy, PermissionEngine.Resolve(link, calendar, Event(Owner, new EventOverride(Principal.Anonymous, EventLevel.FreeBusy))).Level);

        // anonymous (tier 2) beats everyone (tier 1); both restrict only.
        var both = Event(Owner, new EventOverride(Principal.Everyone, EventLevel.None), new EventOverride(Principal.Anonymous, EventLevel.Edit));
        var access = PermissionEngine.Resolve(link, calendar, both);
        Assert.Equal(EventLevel.Read, access.Level);
        Assert.Contains(new ResolutionStep(OverrideApplied) { EventLevel = EventLevel.Edit, Tier = 2 }, access.Steps);
        Assert.Contains(new ResolutionStep(RestrictOnly) { EventLevel = EventLevel.Read, Applied = true }, access.Steps);
        Assert.Contains(new ResolutionStep(LinkCeiling) { EventLevel = EventLevel.Read }, access.Steps);
    }

    [Fact]
    public void Anonymous_overrides_do_not_match_users()
    {
        var calendar = UserCalendar(new CalendarGrant(Principal.User(Anna), CalendarLevel.Read));

        var access = PermissionEngine.Resolve(User(Anna), calendar, Event(Owner, new EventOverride(Principal.Anonymous, EventLevel.None)));

        Assert.Equal(EventLevel.Read, access.Level);
        Assert.Contains(access.Steps, s => s.Kind == NoMatchingOverride);
    }

    [Fact]
    public void Ties_on_a_tier_are_unions()
    {
        var calendar = UserCalendar(new CalendarGrant(Principal.User(Anna), CalendarLevel.Read));
        var ev = Event(Owner, new EventOverride(Principal.Group(Club), EventLevel.None), new EventOverride(Principal.Group(Board), EventLevel.Edit));

        Assert.Equal(EventLevel.Edit, PermissionEngine.Resolve(User(Anna, (Club, GroupRole.Viewer), (Board, GroupRole.Viewer)), calendar, ev).Level);
        Assert.Equal(EventLevel.None, PermissionEngine.Resolve(User(Anna, (Club, GroupRole.Viewer)), calendar, ev).Level);
    }

    [Fact]
    public void A_group_none_override_hides_the_event_even_from_members_with_other_access()
    {
        var calendar = UserCalendar(new CalendarGrant(Principal.Group(Board), CalendarLevel.Edit));
        var ev = Event(Owner, new EventOverride(Principal.Group(Club), EventLevel.None));

        Assert.Equal(EventLevel.None, PermissionEngine.Resolve(User(Anna, (Club, GroupRole.Member), (Board, GroupRole.Member)), calendar, ev).Level);
    }

    [Fact]
    public void Overrides_cap_at_edit_even_when_stored_with_manage()
    {
        var access = PermissionEngine.Resolve(User(Anna), UserCalendar(), Event(Owner, new EventOverride(Principal.User(Anna), EventLevel.Manage)));

        Assert.Equal(EventLevel.Edit, access.Level);
        Assert.Contains(new ResolutionStep(OverrideCap) { EventLevel = EventLevel.Edit }, access.Steps);
    }

    [Fact]
    public void A_lower_tier_after_a_higher_one_does_not_change_the_result()
    {
        var calendar = UserCalendar(new CalendarGrant(Principal.User(Anna), CalendarLevel.Read));
        var ev = Event(Owner, new EventOverride(Principal.User(Anna), EventLevel.FreeBusy), new EventOverride(Principal.Everyone, EventLevel.Read), new EventOverride(Principal.Group(Club), EventLevel.Edit));

        var access = PermissionEngine.Resolve(User(Anna, (Club, GroupRole.Member)), calendar, ev);

        Assert.Equal(EventLevel.FreeBusy, access.Level);
        Assert.Equal([true, false, false], access.Steps.Where(s => s.Kind == OverrideMatched).Select(s => s.Decisive!.Value));
    }

    [Theory]
    [InlineData(CalendarLevel.Contribute, true, EventLevel.Manage, CreatorFloor)]
    [InlineData(CalendarLevel.Edit, true, EventLevel.Manage, CreatorFloor)]
    [InlineData(CalendarLevel.Read, true, EventLevel.None, CreatorFloorLapsed)]
    [InlineData(CalendarLevel.Contribute, false, EventLevel.None, CreatorFloorDisabled)]
    public void Creators_keep_manage_while_they_contribute_unless_the_calendar_disables_it(CalendarLevel level, bool manageOwn, EventLevel expected, ResolutionStepKind step)
    {
        var calendar = new CalendarAcl(CalendarId, Principal.User(Owner), [new(Principal.User(Anna), level)], creatorsManageOwnEvents: manageOwn);

        var access = PermissionEngine.Resolve(User(Anna), calendar, Event(Anna, new EventOverride(Principal.User(Anna), EventLevel.None)));

        Assert.Equal(expected, access.Level);
        Assert.Contains(access.Steps, s => s.Kind == step);
    }

    [Fact]
    public void Tombstoned_or_system_creators_have_no_floor()
    {
        var calendar = UserCalendar(new CalendarGrant(Principal.User(Anna), CalendarLevel.Contribute));

        Assert.Equal(EventLevel.Read, PermissionEngine.Resolve(User(Anna), calendar, Event(null)).Level);
        Assert.Equal(EventLevel.Read, PermissionEngine.Resolve(User(Anna), calendar, Event(Ben)).Level);
    }

    [Fact]
    public void Link_holders_are_never_creators()
    {
        var link = PrincipalContext.ForShareLink(CalendarId, CalendarLevel.Read);

        Assert.Equal(EventLevel.Read, PermissionEngine.Resolve(link, UserCalendar(), Event(Owner)).Level);
    }

    [Fact]
    public void Exceptions_resolve_with_the_series_acl_and_say_so()
    {
        var series = Event(Owner, new EventOverride(Principal.Everyone, EventLevel.None));
        var exception = EventAcl.ExceptionOf(series, Guid.CreateVersion7());
        var calendar = UserCalendar(new CalendarGrant(Principal.User(Anna), CalendarLevel.Edit));

        var access = PermissionEngine.Resolve(User(Anna), calendar, exception);

        Assert.Equal(EventLevel.None, access.Level);
        Assert.Contains(new ResolutionStep(InheritedFromSeries) { ResourceId = series.EventId }, access.Steps);
    }

    [Fact]
    public void Events_of_another_calendar_are_rejected()
    {
        var ev = new EventAcl(Guid.CreateVersion7(), Guid.CreateVersion7(), null);

        Assert.Throws<ArgumentException>(() => PermissionEngine.Resolve(User(Anna), UserCalendar(), ev));
    }

    [Fact]
    public void Everyone_matches_the_audience_only()
    {
        Assert.True(PermissionEngine.Matches(Principal.Everyone, User(Anna), CalendarLevel.FreeBusy));
        Assert.False(PermissionEngine.Matches(Principal.Everyone, User(Anna), CalendarLevel.None));
        Assert.True(PermissionEngine.Matches(Principal.Anonymous, PrincipalContext.ForShareLink(CalendarId, CalendarLevel.FreeBusy), CalendarLevel.FreeBusy));
        Assert.False(PermissionEngine.Matches(Principal.Anonymous, PrincipalContext.ForShareLink(CalendarId, CalendarLevel.FreeBusy), CalendarLevel.None));
        Assert.False(PermissionEngine.Matches(Principal.Anonymous, User(Anna), CalendarLevel.Read));
        Assert.False(PermissionEngine.Matches(Principal.User(Anna), PrincipalContext.ForShareLink(CalendarId, CalendarLevel.Read), CalendarLevel.Read));
        Assert.True(PermissionEngine.Matches(Principal.Group(Club, GroupRole.Member), User(Anna, (Club, GroupRole.Admin)), CalendarLevel.None));
        Assert.False(PermissionEngine.Matches(Principal.Group(Club, GroupRole.Admin), User(Anna, (Club, GroupRole.Member)), CalendarLevel.None));
    }

    [Fact]
    public void Null_inputs_are_rejected()
    {
        var calendar = UserCalendar();
        var ev = Event(null);
        var user = User(Anna);

        Assert.Throws<ArgumentNullException>(() => PermissionEngine.ResolveCalendar(null!, calendar));
        Assert.Throws<ArgumentNullException>(() => PermissionEngine.ResolveCalendar(user, null!));
        Assert.Throws<ArgumentNullException>(() => PermissionEngine.Resolve(null!, calendar, ev));
        Assert.Throws<ArgumentNullException>(() => PermissionEngine.Resolve(user, null!, ev));
        Assert.Throws<ArgumentNullException>(() => PermissionEngine.Resolve(user, calendar, null!));
        Assert.Throws<ArgumentNullException>(() => PermissionEngine.Matches(null!, user, CalendarLevel.Read));
        Assert.Throws<ArgumentNullException>(() => PermissionEngine.Matches(Principal.Everyone, null!, CalendarLevel.Read));
    }
}
