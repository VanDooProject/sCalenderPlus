using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Core.Tests.Permissions;

/// <summary>§4.5 calendar actions, §2.1 event actions, §4.6 move and the 404/403 semantics of §8.</summary>
public sealed class AccessPolicyTests
{
    [Theory]
    [InlineData(CalendarAction.View, CalendarLevel.FreeBusy)]
    [InlineData(CalendarAction.CreateEvent, CalendarLevel.Contribute)]
    [InlineData(CalendarAction.UpdateSettings, CalendarLevel.Manage)]
    [InlineData(CalendarAction.ManageSharing, CalendarLevel.Manage)]
    [InlineData(CalendarAction.ConfigureImport, CalendarLevel.Manage)]
    [InlineData(CalendarAction.Delete, CalendarLevel.Owner)]
    [InlineData(CalendarAction.Transfer, CalendarLevel.Owner)]
    public void Calendar_actions_need_their_level(CalendarAction action, CalendarLevel required)
    {
        Assert.Equal(required, AccessPolicy.RequiredLevel(action));
        Assert.Equal(AccessCheck.Allowed, AccessPolicy.Check(required, action));
        Assert.Equal(AccessCheck.Allowed, AccessPolicy.Check(CalendarLevel.Owner, action));
        Assert.Equal(AccessCheck.NotFound, AccessPolicy.Check(CalendarLevel.None, action));
        if (required > CalendarLevel.FreeBusy)
        {
            Assert.Equal(AccessCheck.Forbidden, AccessPolicy.Check(required - 1, action));
        }
    }

    [Theory]
    [InlineData(EventAction.ViewBusy, EventLevel.FreeBusy)]
    [InlineData(EventAction.ViewDetails, EventLevel.Read)]
    [InlineData(EventAction.Edit, EventLevel.Edit)]
    [InlineData(EventAction.Delete, EventLevel.Edit)]
    [InlineData(EventAction.ChangeOverrides, EventLevel.Manage)]
    [InlineData(EventAction.Move, EventLevel.Manage)]
    public void Event_actions_need_their_level(EventAction action, EventLevel required)
    {
        Assert.Equal(required, AccessPolicy.RequiredLevel(action));
        Assert.Equal(AccessCheck.Allowed, AccessPolicy.Check(required, action));
        Assert.Equal(AccessCheck.NotFound, AccessPolicy.Check(EventLevel.None, action));
        if (required > EventLevel.FreeBusy)
        {
            Assert.Equal(AccessCheck.Forbidden, AccessPolicy.Check(required - 1, action));
        }
    }

    [Fact]
    public void Unknown_actions_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AccessPolicy.RequiredLevel((CalendarAction)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => AccessPolicy.RequiredLevel((EventAction)99));
    }

    [Theory]
    [InlineData(EventLevel.Manage, CalendarLevel.Contribute, true)]
    [InlineData(EventLevel.Manage, CalendarLevel.Read, false)]
    [InlineData(EventLevel.Edit, CalendarLevel.Owner, false)]
    public void Moving_needs_manage_on_the_event_and_contribute_on_the_target(EventLevel source, CalendarLevel target, bool allowed) =>
        Assert.Equal(allowed, AccessPolicy.CanMoveEvent(source, target));

    [Theory]
    [InlineData(CalendarLevel.Manage, CalendarLevel.Manage, true)]
    [InlineData(CalendarLevel.Manage, CalendarLevel.FreeBusy, true)]
    [InlineData(CalendarLevel.Owner, CalendarLevel.Manage, true)]
    [InlineData(CalendarLevel.Owner, CalendarLevel.Owner, false)]
    [InlineData(CalendarLevel.Manage, CalendarLevel.None, false)]
    [InlineData(CalendarLevel.Edit, CalendarLevel.Read, false)]
    public void Managers_grant_up_to_their_level_but_never_owner(CalendarLevel actor, CalendarLevel granted, bool allowed) =>
        Assert.Equal(allowed, AccessPolicy.CanGrant(actor, granted));

    [Theory]
    [InlineData(CalendarLevel.Manage, CalendarLevel.None, true)]
    [InlineData(CalendarLevel.Manage, CalendarLevel.Manage, true)]
    [InlineData(CalendarLevel.Owner, CalendarLevel.Owner, false)]
    [InlineData(CalendarLevel.Edit, CalendarLevel.Read, false)]
    [InlineData(CalendarLevel.Manage, (CalendarLevel)(-1), false)]
    public void Managers_set_role_defaults_up_to_their_level(CalendarLevel actor, CalendarLevel level, bool allowed) =>
        Assert.Equal(allowed, AccessPolicy.CanSetRoleDefault(actor, level));

    [Theory]
    [InlineData(CalendarLevel.Manage, CalendarLevel.FreeBusy, true)]
    [InlineData(CalendarLevel.Owner, CalendarLevel.Read, true)]
    [InlineData(CalendarLevel.Owner, CalendarLevel.Contribute, false)]
    [InlineData(CalendarLevel.Edit, CalendarLevel.Read, false)]
    public void Managers_create_free_busy_or_read_share_links(CalendarLevel actor, CalendarLevel level, bool allowed) =>
        Assert.Equal(allowed, AccessPolicy.CanCreateShareLink(actor, level));
}
