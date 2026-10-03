using SCalenderPlus.Core.Groups;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Core.Tests.Permissions;

/// <summary>Levels, principals and the ACL inputs of the engine: names, conversions and rejected invalid states.</summary>
public sealed class PermissionModelTests
{
    internal static readonly Guid Id = Guid.CreateVersion7();

    [Theory]
    [InlineData("none", CalendarLevel.None, EventLevel.None)]
    [InlineData("free_busy", CalendarLevel.FreeBusy, EventLevel.FreeBusy)]
    [InlineData("read", CalendarLevel.Read, EventLevel.Read)]
    [InlineData("contribute", CalendarLevel.Contribute, EventLevel.Read)]
    [InlineData("edit", CalendarLevel.Edit, EventLevel.Edit)]
    [InlineData("manage", CalendarLevel.Manage, EventLevel.Manage)]
    [InlineData("owner", CalendarLevel.Owner, EventLevel.Manage)]
    public void Calendar_levels_have_names_and_imply_event_levels(string name, CalendarLevel level, EventLevel implied)
    {
        Assert.Equal(name, PermissionLevels.Format(level));
        Assert.True(PermissionLevels.TryParse(name, out CalendarLevel parsed));
        Assert.Equal(level, parsed);
        Assert.Equal(implied, PermissionLevels.ImpliedEventLevel(level));
    }

    [Theory]
    [InlineData("none", EventLevel.None)]
    [InlineData("free_busy", EventLevel.FreeBusy)]
    [InlineData("read", EventLevel.Read)]
    [InlineData("edit", EventLevel.Edit)]
    [InlineData("manage", EventLevel.Manage)]
    public void Event_levels_have_names(string name, EventLevel level)
    {
        Assert.Equal(name, PermissionLevels.Format(level));
        Assert.True(PermissionLevels.TryParse(name, out EventLevel parsed));
        Assert.Equal(level, parsed);
    }

    [Theory]
    [InlineData("Read")]
    [InlineData("2")]
    [InlineData("")]
    [InlineData(null)]
    public void Other_level_names_are_rejected(string? value)
    {
        Assert.False(PermissionLevels.TryParse(value, out CalendarLevel _));
        Assert.False(PermissionLevels.TryParse(value, out EventLevel _));
    }

    [Fact]
    public void Contribute_has_no_event_level_name()
    {
        Assert.False(PermissionLevels.TryParse("contribute", out EventLevel _));
        Assert.False(PermissionLevels.TryParse("owner", out EventLevel _));
    }

    [Fact]
    public void Levels_are_ordered_and_stored_as_documented()
    {
        Assert.Equal([0, 1, 2, 3, 4, 5, 6], PermissionLevels.CalendarLevels.Select(l => (int)l));
        Assert.Equal([0, 1, 2, 3, 4], PermissionLevels.EventLevels.Select(l => (int)l));
        Assert.Equal(EventLevel.Edit, PermissionLevels.MaxOverrideLevel);
        Assert.Equal(EventLevel.Read, PermissionLevels.Min(EventLevel.Read, EventLevel.Edit));
        Assert.Equal(CalendarLevel.Edit, PermissionLevels.Max(CalendarLevel.Read, CalendarLevel.Edit));
    }

    [Fact]
    public void Unknown_levels_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PermissionLevels.ImpliedEventLevel((CalendarLevel)7));
        Assert.Throws<ArgumentOutOfRangeException>(() => PermissionLevels.Format((CalendarLevel)7));
        Assert.Throws<ArgumentOutOfRangeException>(() => PermissionLevels.Format((EventLevel)5));
    }

    [Fact]
    public void Principals_have_tiers_from_user_4_to_everyone_1()
    {
        Assert.Equal(4, Principal.User(Id).Tier);
        Assert.Equal(3, Principal.Group(Id).Tier);
        Assert.Equal(2, Principal.Anonymous.Tier);
        Assert.Equal(1, Principal.Everyone.Tier);
        Assert.False(Principal.User(Id).IsRestrictOnly);
        Assert.False(Principal.Group(Id).IsRestrictOnly);
        Assert.True(Principal.Anonymous.IsRestrictOnly);
        Assert.True(Principal.Everyone.IsRestrictOnly);
    }

    [Fact]
    public void Principals_compare_by_value_and_print_like_the_docs()
    {
        Assert.Equal(Principal.Group(Id), Principal.Group(Id, GroupRole.Viewer));
        Assert.NotEqual(Principal.Group(Id), Principal.Group(Id, GroupRole.Admin));
        Assert.Equal($"user:{Id}", Principal.User(Id).ToString());
        Assert.Equal($"group:{Id}[admin]", Principal.Group(Id, GroupRole.Admin).ToString());
        Assert.Equal("anonymous", Principal.Anonymous.ToString());
        Assert.Equal("everyone", Principal.Everyone.ToString());
    }

    [Fact]
    public void Principals_are_created_from_stored_columns()
    {
        Assert.Equal(Principal.User(Id), Principal.Create(PrincipalType.User, Id, null));
        Assert.Equal(Principal.Group(Id, GroupRole.Member), Principal.Create(PrincipalType.Group, Id, GroupRole.Member));
        Assert.Equal(Principal.Group(Id), Principal.Create(PrincipalType.Group, Id, null));
        Assert.Same(Principal.Anonymous, Principal.Create(PrincipalType.Anonymous, null, null));
        Assert.Same(Principal.Everyone, Principal.Create(PrincipalType.Everyone, null, null));
    }

    public static TheoryData<PrincipalType, Guid?, GroupRole?> InvalidPrincipals { get; } = new()
    {
        { PrincipalType.User, null, null },
        { PrincipalType.User, Id, GroupRole.Member },
        { PrincipalType.Group, null, GroupRole.Member },
        { PrincipalType.Anonymous, Id, null },
        { PrincipalType.Anonymous, null, GroupRole.Member },
        { PrincipalType.Everyone, Id, null },
        { PrincipalType.Everyone, null, GroupRole.Member },
        { (PrincipalType)4, null, null },
    };

    [Theory]
    [MemberData(nameof(InvalidPrincipals))]
    public void Inconsistent_stored_principals_are_rejected(PrincipalType type, Guid? id, GroupRole? minRole) =>
        Assert.Throws<ArgumentException>(() => Principal.Create(type, id, minRole));

    [Fact]
    public void Principal_ids_and_roles_must_be_valid()
    {
        Assert.Throws<ArgumentException>(() => Principal.User(Guid.Empty));
        Assert.Throws<ArgumentException>(() => Principal.Group(Guid.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => Principal.Group(Id, (GroupRole)4));
    }

    [Fact]
    public void Contexts_are_users_or_share_link_holders()
    {
        var user = PrincipalContext.ForUser(Id);
        Assert.False(user.IsAnonymous);
        Assert.Empty(user.Groups);
        Assert.Null(user.LinkCalendarId);
        Assert.Equal(CalendarLevel.None, user.LinkLevel);
        Assert.Null(user.RoleIn(Id));

        var member = PrincipalContext.ForUser(Id, new Dictionary<Guid, GroupRole> { [Id] = GroupRole.Admin });
        Assert.Equal(GroupRole.Admin, member.RoleIn(Id));

        var link = PrincipalContext.ForShareLink(Id, CalendarLevel.Read);
        Assert.True(link.IsAnonymous);
        Assert.Null(link.UserId);
        Assert.Equal(Id, link.LinkCalendarId);
        Assert.Equal(CalendarLevel.Read, link.LinkLevel);
        Assert.Equal(CalendarLevel.FreeBusy, PrincipalContext.ForShareLink(Id, CalendarLevel.FreeBusy).LinkLevel);
    }

    [Theory]
    [InlineData(CalendarLevel.None)]
    [InlineData(CalendarLevel.Contribute)]
    [InlineData(CalendarLevel.Manage)]
    public void Share_links_carry_free_busy_or_read(CalendarLevel level) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PrincipalContext.ForShareLink(Id, level));

    [Fact]
    public void Users_need_an_id() => Assert.Throws<ArgumentException>(() => PrincipalContext.ForUser(Guid.Empty));

    [Fact]
    public void Grants_name_users_or_groups_with_free_busy_to_manage()
    {
        Assert.Equal(CalendarLevel.Manage, new CalendarGrant(Principal.User(Id), CalendarLevel.Manage).Level);
        Assert.Equal(Principal.Group(Id), new CalendarGrant(Principal.Group(Id), CalendarLevel.FreeBusy).Principal);
        Assert.Throws<ArgumentException>(() => new CalendarGrant(Principal.Everyone, CalendarLevel.Read));
        Assert.Throws<ArgumentException>(() => new CalendarGrant(Principal.Anonymous, CalendarLevel.Read));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CalendarGrant(Principal.User(Id), CalendarLevel.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CalendarGrant(Principal.User(Id), CalendarLevel.Owner));
        Assert.Throws<ArgumentNullException>(() => new CalendarGrant(null!, CalendarLevel.Read));
    }

    [Fact]
    public void Role_defaults_map_roles_and_owners_are_always_owner()
    {
        var defaults = new GroupRoleDefaults(CalendarLevel.Edit, CalendarLevel.Read, CalendarLevel.None);

        Assert.Equal(CalendarLevel.Owner, defaults.For(GroupRole.Owner));
        Assert.Equal(CalendarLevel.Edit, defaults.For(GroupRole.Admin));
        Assert.Equal(CalendarLevel.Read, defaults.For(GroupRole.Member));
        Assert.Equal(CalendarLevel.None, defaults.For(GroupRole.Viewer));
        Assert.Throws<ArgumentOutOfRangeException>(() => defaults.For((GroupRole)4));
        Assert.Equal(new GroupRoleDefaults(CalendarLevel.Manage, CalendarLevel.Contribute, CalendarLevel.Read), GroupRoleDefaults.Default);
    }

    [Theory]
    [InlineData(CalendarLevel.Owner)]
    [InlineData((CalendarLevel)(-1))]
    public void Role_defaults_are_none_to_manage(CalendarLevel level)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GroupRoleDefaults(level, CalendarLevel.Read, CalendarLevel.Read));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GroupRoleDefaults(CalendarLevel.Read, level, CalendarLevel.Read));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GroupRoleDefaults(CalendarLevel.Read, CalendarLevel.Read, level));
    }

    [Fact]
    public void Calendars_are_owned_by_a_user_or_a_group_with_safe_defaults()
    {
        var calendar = new CalendarAcl(Id, Principal.User(Id));
        Assert.False(calendar.IsGroupOwned);
        Assert.Empty(calendar.Grants);
        Assert.Same(GroupRoleDefaults.Default, calendar.RoleDefaults);
        Assert.True(calendar.CreatorsManageOwnEvents);
        Assert.False(calendar.CreatorsMayShareExternally);

        var groupOwned = new CalendarAcl(Id, Principal.Group(Id, GroupRole.Admin));
        Assert.True(groupOwned.IsGroupOwned);
        Assert.Equal(Principal.Group(Id), groupOwned.Owner); // the role restriction of an owner is meaningless

        Assert.Throws<ArgumentException>(() => new CalendarAcl(Id, Principal.Everyone));
        Assert.Throws<ArgumentNullException>(() => new CalendarAcl(Id, null!));
    }

    [Fact]
    public void Exceptions_inherit_the_series_acl()
    {
        var series = new EventAcl(Id, Id, Id, [new(Principal.Everyone, EventLevel.None)]);
        var exception = EventAcl.ExceptionOf(series, Guid.CreateVersion7());

        Assert.Same(series, exception.Series);
        Assert.Equal(series.CalendarId, exception.CalendarId);
        Assert.Equal(series.CreatorUserId, exception.CreatorUserId);
        Assert.Equal(series.Overrides, exception.Overrides);
        Assert.Empty(new EventAcl(Id, Id, null).Overrides);
        Assert.Null(series.Series);
        Assert.Throws<ArgumentException>(() => EventAcl.ExceptionOf(exception, Guid.CreateVersion7()));
        Assert.Throws<ArgumentNullException>(() => EventAcl.ExceptionOf(null!, Id));
        Assert.Throws<ArgumentNullException>(() => new EventOverride(null!, EventLevel.Read));
    }

    [Fact]
    public void Floors_mark_override_rights_on_the_access()
    {
        Assert.True(new EventAccess(EventLevel.Manage, CalendarLevel.Contribute, []).HasFloor);
        Assert.False(new EventAccess(EventLevel.Edit, CalendarLevel.Edit, []).HasFloor);
    }
}
