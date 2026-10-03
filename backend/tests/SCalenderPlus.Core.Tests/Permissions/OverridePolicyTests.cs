using SCalenderPlus.Core.Groups;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Core.Tests.Permissions;

/// <summary>§4.4: who may change overrides, external sharing, principal selection; §4.6: re-validation on move.</summary>
public sealed class OverridePolicyTests
{
    internal static readonly Guid CalendarId = Guid.CreateVersion7();
    internal static readonly Guid Owner = Guid.CreateVersion7();
    internal static readonly Guid Anna = Guid.CreateVersion7();
    internal static readonly Guid Ben = Guid.CreateVersion7();
    internal static readonly Guid Club = Guid.CreateVersion7();
    internal static readonly Guid Board = Guid.CreateVersion7();
    internal static readonly Guid Stranger = Guid.CreateVersion7();

    internal static readonly IReadOnlyDictionary<Guid, CalendarLevel> NoUsers = new Dictionary<Guid, CalendarLevel>();

    /// <summary>Club-owned (viewers free_busy, members contribute), Board granted read for members and up, Anna contributes directly.</summary>
    private static CalendarAcl Calendar(bool shareExternally = false) => new(
        CalendarId,
        Principal.Group(Club),
        [new(Principal.Group(Board, GroupRole.Member), CalendarLevel.Read), new(Principal.User(Anna), CalendarLevel.Contribute), new(Principal.User(Ben), CalendarLevel.FreeBusy)],
        new(CalendarLevel.Manage, CalendarLevel.Contribute, CalendarLevel.FreeBusy),
        creatorsMayShareExternally: shareExternally);

    private static EventAcl Event(Guid creator, params EventOverride[] overrides) => new(Guid.CreateVersion7(), CalendarId, creator, overrides);

    private static PrincipalContext User(Guid id, params (Guid Group, GroupRole Role)[] groups) =>
        PrincipalContext.ForUser(id, groups.ToDictionary(g => g.Group, g => g.Role));

    private static OverrideChangeDecision Change(PrincipalContext actor, EventAcl ev, params EventOverride[] proposed) =>
        OverridePolicy.EvaluateChange(actor, Calendar(), ev, proposed, Levels());

    private static Dictionary<Guid, CalendarLevel> Levels() => new()
    {
        [Anna] = CalendarLevel.Contribute,
        [Ben] = CalendarLevel.FreeBusy,
        [Owner] = CalendarLevel.Owner,
    };

    [Fact]
    public void Rights_follow_the_floors()
    {
        var calendar = Calendar();

        Assert.Equal(OverrideRights.IncludingExternal, OverridePolicy.RightsOf(User(Owner, (Club, GroupRole.Owner)), calendar, Event(Anna)));
        Assert.Equal(OverrideRights.IncludingExternal, OverridePolicy.RightsOf(User(Ben, (Club, GroupRole.Admin)), calendar, Event(Anna)));
        Assert.Equal(OverrideRights.InternalOnly, OverridePolicy.RightsOf(User(Anna), calendar, Event(Anna)));
        Assert.Equal(OverrideRights.IncludingExternal, OverridePolicy.RightsOf(User(Anna), Calendar(shareExternally: true), Event(Anna)));
        Assert.Equal(OverrideRights.None, OverridePolicy.RightsOf(User(Anna), calendar, Event(Ben)));
        Assert.Equal(OverrideRights.None, OverridePolicy.RightsOf(User(Anna), calendar, Event(Ben, new EventOverride(Principal.User(Anna), EventLevel.Edit))));
    }

    [Fact]
    public void Guaranteed_levels_of_users_come_from_their_calendar_level()
    {
        Assert.Equal(CalendarLevel.Contribute, OverridePolicy.GuaranteedCalendarLevel(Principal.User(Anna), Calendar(), Levels()));
        Assert.Equal(CalendarLevel.None, OverridePolicy.GuaranteedCalendarLevel(Principal.User(Stranger), Calendar(), Levels()));
        Assert.Equal(CalendarLevel.Owner, OverridePolicy.GuaranteedCalendarLevel(Principal.Everyone, Calendar(), Levels()));
        Assert.Equal(CalendarLevel.Owner, OverridePolicy.GuaranteedCalendarLevel(Principal.Anonymous, Calendar(), Levels()));
    }

    [Theory]
    [InlineData(GroupRole.Viewer, CalendarLevel.FreeBusy)] // viewers only see busy blocks
    [InlineData(GroupRole.Member, CalendarLevel.Contribute)]
    [InlineData(GroupRole.Admin, CalendarLevel.Manage)]
    [InlineData(GroupRole.Owner, CalendarLevel.Owner)]
    public void The_owner_group_is_guaranteed_its_weakest_role_default_from_the_minimum_role(GroupRole minRole, CalendarLevel expected) =>
        Assert.Equal(expected, OverridePolicy.GuaranteedCalendarLevel(Principal.Group(Club, minRole), Calendar(), NoUsers));

    [Theory]
    [InlineData(GroupRole.Viewer, CalendarLevel.None)] // the grant needs role member
    [InlineData(GroupRole.Member, CalendarLevel.Read)]
    [InlineData(GroupRole.Owner, CalendarLevel.Read)]
    public void Granted_groups_are_guaranteed_the_grants_covering_the_minimum_role(GroupRole minRole, CalendarLevel expected)
    {
        Assert.Equal(expected, OverridePolicy.GuaranteedCalendarLevel(Principal.Group(Board, minRole), Calendar(), NoUsers));
        Assert.Equal(CalendarLevel.None, OverridePolicy.GuaranteedCalendarLevel(Principal.Group(Stranger, minRole), Calendar(), NoUsers));
    }

    [Fact]
    public void A_user_owned_calendar_guarantees_nothing_to_a_group_with_the_owners_id()
    {
        var calendar = new CalendarAcl(CalendarId, Principal.User(Owner));

        Assert.Equal(CalendarLevel.None, OverridePolicy.GuaranteedCalendarLevel(Principal.Group(Owner), calendar, NoUsers));
        Assert.False(OverridePolicy.IsSelectableGroup(Owner, User(Anna), calendar));
    }

    [Theory]
    [InlineData(PrincipalType.User, EventLevel.Read, false)] // Anna contributes
    [InlineData(PrincipalType.Group, EventLevel.Read, true)] // Club viewers only have free_busy
    [InlineData(PrincipalType.Group, EventLevel.FreeBusy, false)] // … which they already have
    [InlineData(PrincipalType.Group, EventLevel.None, false)] // restricting is not sharing
    [InlineData(PrincipalType.Everyone, EventLevel.Edit, false)]
    [InlineData(PrincipalType.Anonymous, EventLevel.Edit, false)]
    public void External_sharing_gives_outsiders_more_than_the_calendar(PrincipalType type, EventLevel level, bool external)
    {
        var principal = type switch
        {
            PrincipalType.User => Principal.User(Anna),
            PrincipalType.Group => Principal.Group(Club),
            _ => Principal.Create(type, null, null),
        };

        Assert.Equal(external, OverridePolicy.IsExternalSharing(new(principal, level), Calendar(), Levels()));
    }

    [Fact]
    public void Selectable_groups_are_mine_the_owner_group_or_granted()
    {
        var calendar = Calendar();

        Assert.True(OverridePolicy.IsSelectableGroup(Club, User(Anna), calendar));
        Assert.True(OverridePolicy.IsSelectableGroup(Board, User(Anna), calendar));
        Assert.True(OverridePolicy.IsSelectableGroup(Stranger, User(Anna, (Stranger, GroupRole.Viewer)), calendar));
        Assert.False(OverridePolicy.IsSelectableGroup(Stranger, User(Anna), calendar));
        Assert.False(OverridePolicy.IsSelectableGroup(Anna, User(Anna), calendar)); // a user grant is not a group grant
    }

    [Fact]
    public void A_creator_may_hide_restrict_and_elevate_inside_the_audience()
    {
        var decision = Change(
            User(Anna),
            Event(Anna),
            new EventOverride(Principal.Group(Club), EventLevel.None),
            new EventOverride(Principal.Everyone, EventLevel.FreeBusy),
            new EventOverride(Principal.Group(Board, GroupRole.Member), EventLevel.Edit),
            new EventOverride(Principal.User(Ben), EventLevel.FreeBusy),
            new EventOverride(Principal.User(Stranger), EventLevel.None));

        Assert.Equal(OverrideChangeVerdict.Allowed, decision.Verdict);
        Assert.Empty(decision.Violations);
        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void A_creator_may_not_share_with_outsiders()
    {
        var external = new EventOverride(Principal.User(Ben), EventLevel.Read);
        var group = new EventOverride(Principal.Group(Club), EventLevel.Read);

        var decision = Change(User(Anna), Event(Anna), external, group);

        Assert.Equal(OverrideChangeVerdict.ExternalSharingNotAllowed, decision.Verdict);
        Assert.Equal([new(external, OverrideViolationReason.ExternalSharing), new(group, OverrideViolationReason.ExternalSharing)], decision.Violations);
    }

    [Fact]
    public void Managers_may_share_with_outsiders()
    {
        var decision = Change(User(Ben, (Club, GroupRole.Admin)), Event(Anna), new EventOverride(Principal.User(Stranger), EventLevel.Edit), new EventOverride(Principal.Group(Club), EventLevel.Read));

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void Existing_external_shares_may_be_kept_lowered_or_removed_by_the_creator()
    {
        var shared = Event(Anna, new EventOverride(Principal.User(Stranger), EventLevel.Edit), new EventOverride(Principal.Group(Stranger), EventLevel.Read));

        Assert.True(Change(User(Anna), shared, new EventOverride(Principal.User(Stranger), EventLevel.Edit), new EventOverride(Principal.Group(Stranger), EventLevel.Read)).IsAllowed);
        Assert.True(Change(User(Anna), shared, new EventOverride(Principal.User(Stranger), EventLevel.FreeBusy)).IsAllowed);
        Assert.True(Change(User(Anna), shared).IsAllowed);
        Assert.Equal(OverrideChangeVerdict.ExternalSharingNotAllowed, Change(User(Anna), Event(Anna, new EventOverride(Principal.User(Stranger), EventLevel.FreeBusy)), new EventOverride(Principal.User(Stranger), EventLevel.Read)).Verdict);
    }

    [Fact]
    public void Invalid_entries_win_over_external_sharing()
    {
        var tooHigh = new EventOverride(Principal.User(Anna), EventLevel.Manage);
        var duplicate = new EventOverride(Principal.Everyone, EventLevel.Read);
        var unknownGroup = new EventOverride(Principal.Group(Stranger), EventLevel.None);
        var external = new EventOverride(Principal.User(Stranger), EventLevel.Read);

        var decision = Change(User(Anna), Event(Anna), tooHigh, duplicate, duplicate, unknownGroup, external);

        Assert.Equal(OverrideChangeVerdict.Invalid, decision.Verdict);
        Assert.Equal(
            [
                new(tooHigh, OverrideViolationReason.LevelTooHigh),
                new(duplicate, OverrideViolationReason.DuplicatePrincipal),
                new(unknownGroup, OverrideViolationReason.GroupNotSelectable),
                new(external, OverrideViolationReason.ExternalSharing),
            ],
            decision.Violations);
    }

    [Fact]
    public void Without_a_floor_nothing_may_be_changed()
    {
        Assert.Equal(OverrideChangeVerdict.Forbidden, Change(User(Anna), Event(Ben)).Verdict);
        Assert.Equal(OverrideChangeVerdict.NotFound, Change(User(Stranger), Event(Anna)).Verdict);
        Assert.Equal(OverrideChangeVerdict.Forbidden, Change(PrincipalContext.ForShareLink(CalendarId, CalendarLevel.Read), Event(Anna)).Verdict);
    }

    [Fact]
    public void Overrides_of_an_exception_are_those_of_its_series()
    {
        var series = Event(Anna, new EventOverride(Principal.User(Stranger), EventLevel.Read));
        var exception = EventAcl.ExceptionOf(series, Guid.CreateVersion7());

        Assert.True(Change(User(Anna), exception, new EventOverride(Principal.User(Stranger), EventLevel.Read)).IsAllowed);
    }

    [Fact]
    public void Moving_keeps_overrides_the_mover_could_set_in_the_target()
    {
        var target = new CalendarAcl(Guid.CreateVersion7(), Principal.User(Owner), [new(Principal.User(Anna), CalendarLevel.Contribute)]);
        var outsider = new EventOverride(Principal.User(Ben), EventLevel.Read);
        var inside = new EventOverride(Principal.User(Anna), EventLevel.Edit);
        var hide = new EventOverride(Principal.Everyone, EventLevel.None);
        var ev = Event(Anna, outsider, inside, hide);
        var levels = new Dictionary<Guid, CalendarLevel> { [Anna] = CalendarLevel.Contribute };

        Assert.Equal([outsider], OverridePolicy.InvalidInTarget(User(Anna), target, ev, levels));
        Assert.Empty(OverridePolicy.InvalidInTarget(User(Owner), target, ev, levels));
        Assert.Equal([outsider], OverridePolicy.InvalidInTarget(User(Anna), target, EventAcl.ExceptionOf(ev, Guid.CreateVersion7()), levels));
    }

    [Fact]
    public void Null_inputs_are_rejected()
    {
        var calendar = Calendar();
        var user = User(Anna);

        Assert.Throws<ArgumentNullException>(() => OverridePolicy.GuaranteedCalendarLevel(null!, calendar, NoUsers));
        Assert.Throws<ArgumentNullException>(() => OverridePolicy.GuaranteedCalendarLevel(Principal.Everyone, null!, NoUsers));
        Assert.Throws<ArgumentNullException>(() => OverridePolicy.GuaranteedCalendarLevel(Principal.Everyone, calendar, null!));
        Assert.Throws<ArgumentNullException>(() => OverridePolicy.IsExternalSharing(null!, calendar, NoUsers));
        Assert.Throws<ArgumentNullException>(() => OverridePolicy.IsSelectableGroup(Club, null!, calendar));
        Assert.Throws<ArgumentNullException>(() => OverridePolicy.IsSelectableGroup(Club, user, null!));
        Assert.Throws<ArgumentNullException>(() => OverridePolicy.EvaluateChange(user, calendar, Event(Anna), null!, NoUsers));
        Assert.Throws<ArgumentNullException>(() => OverridePolicy.EvaluateChange(user, calendar, Event(Anna), [], null!));
        Assert.Throws<ArgumentNullException>(() => OverridePolicy.EvaluateChange(user, calendar, Event(Anna), [null!], NoUsers));
        Assert.Throws<ArgumentNullException>(() => OverridePolicy.InvalidInTarget(user, calendar, null!, NoUsers));
    }
}
