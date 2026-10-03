using SCalenderPlus.Core.Calendars;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Core.Tests.Calendars;

public sealed class CalendarTests
{
    private static readonly Guid _userId = new("30000000-0000-0000-0000-000000000001");
    private static readonly Guid _groupId = new("10000000-0000-0000-0000-000000000001");

    [Fact]
    public void A_user_calendar_is_owned_by_the_user()
    {
        var calendar = new Calendar { Id = Guid.CreateVersion7(), OwnerUserId = _userId };

        Assert.False(calendar.IsGroupOwned);
        Assert.Equal(Principal.User(_userId), calendar.Owner);
    }

    [Fact]
    public void A_group_calendar_is_owned_by_the_whole_group()
    {
        var calendar = new Calendar { Id = Guid.CreateVersion7(), OwnerGroupId = _groupId };

        Assert.True(calendar.IsGroupOwned);
        Assert.Equal(Principal.Group(_groupId), calendar.Owner);
    }

    [Fact]
    public void A_calendar_without_owner_is_invalid() =>
        Assert.Throws<InvalidOperationException>(() => new Calendar { Id = Guid.CreateVersion7() }.Owner);

    [Fact]
    public void The_acl_carries_settings_and_only_the_calendars_own_grants()
    {
        var calendar = new Calendar
        {
            Id = Guid.CreateVersion7(),
            OwnerGroupId = _groupId,
            RoleDefaults = new GroupRoleDefaults(CalendarLevel.Edit, CalendarLevel.Read, CalendarLevel.None),
            CreatorsManageOwnEvents = false,
            CreatorsMayShareExternally = true,
        };
        var mine = CalendarGrantEntry.For(calendar.Id, Principal.Group(Guid.CreateVersion7(), GroupRole.Member), CalendarLevel.Contribute);
        var other = CalendarGrantEntry.For(Guid.CreateVersion7(), Principal.User(_userId), CalendarLevel.Manage);

        var acl = calendar.ToAcl([mine, other]);

        Assert.Equal(calendar.Id, acl.CalendarId);
        Assert.Equal(Principal.Group(_groupId), acl.Owner);
        Assert.Equal([new CalendarGrant(mine.Principal, CalendarLevel.Contribute)], acl.Grants);
        Assert.Equal(calendar.RoleDefaults, acl.RoleDefaults);
        Assert.False(acl.CreatorsManageOwnEvents);
        Assert.True(acl.CreatorsMayShareExternally);
    }

    [Fact]
    public void Grant_entries_store_the_principal_columns()
    {
        var calendarId = Guid.CreateVersion7();
        var user = CalendarGrantEntry.For(calendarId, Principal.User(_userId), CalendarLevel.Read);
        var group = CalendarGrantEntry.For(calendarId, Principal.Group(_groupId), CalendarLevel.FreeBusy);

        Assert.Equal((PrincipalType.User, _userId, (GroupRole?)null), (user.PrincipalType, user.PrincipalId, user.MinRole));
        Assert.Equal((PrincipalType.Group, _groupId, (GroupRole?)GroupRole.Viewer), (group.PrincipalType, group.PrincipalId, group.MinRole));
        Assert.Equal(calendarId, user.CalendarId);
        Assert.NotEqual(user.Id, group.Id);
        Assert.Equal(new CalendarGrant(Principal.Group(_groupId), CalendarLevel.FreeBusy), group.ToGrant());
    }

    [Fact]
    public void Grant_entries_refuse_what_grants_cannot_carry()
    {
        Assert.Throws<ArgumentException>(() => CalendarGrantEntry.For(Guid.CreateVersion7(), Principal.Everyone, CalendarLevel.Read));
        Assert.Throws<ArgumentOutOfRangeException>(() => CalendarGrantEntry.For(Guid.CreateVersion7(), Principal.User(_userId), CalendarLevel.Owner));
    }

    [Theory]
    [InlineData("#4f46e5", true)]
    [InlineData("#AA00ff", true)]
    [InlineData("4f46e5", false)]
    [InlineData("#4f46e", false)]
    [InlineData("#4f46e5a", false)]
    [InlineData("#gggggg", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Colors_are_hex_rgb(string? color, bool valid) => Assert.Equal(valid, Calendar.IsValidColor(color));
}
