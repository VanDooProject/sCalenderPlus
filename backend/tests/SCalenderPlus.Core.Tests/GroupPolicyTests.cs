using SCalenderPlus.Core.Groups;

namespace SCalenderPlus.Core.Tests;

public sealed class GroupPolicyTests
{
    [Fact]
    public void Roles_are_ordered_viewer_member_admin_owner()
    {
        Assert.True(GroupRole.Viewer < GroupRole.Member);
        Assert.True(GroupRole.Member < GroupRole.Admin);
        Assert.True(GroupRole.Admin < GroupRole.Owner);
        Assert.Equal([0, 1, 2, 3], GroupRoles.All.Select(r => (int)r));
    }

    [Theory]
    [InlineData("viewer", GroupRole.Viewer)]
    [InlineData("member", GroupRole.Member)]
    [InlineData("admin", GroupRole.Admin)]
    [InlineData("owner", GroupRole.Owner)]
    public void Roles_round_trip_as_lowercase_names(string value, GroupRole role)
    {
        Assert.True(GroupRoles.TryParse(value, out var parsed));
        Assert.Equal(role, parsed);
        Assert.Equal(value, GroupRoles.Format(role));
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("3")]
    [InlineData("")]
    [InlineData(null)]
    public void Other_role_values_are_rejected(string? value) => Assert.False(GroupRoles.TryParse(value, out _));

    [Theory]
    [InlineData("all_members", MemberListVisibility.AllMembers)]
    [InlineData("members_and_above", MemberListVisibility.MembersAndAbove)]
    public void Visibility_round_trips(string value, MemberListVisibility visibility)
    {
        Assert.True(MemberListVisibilities.TryParse(value, out var parsed));
        Assert.Equal(visibility, parsed);
        Assert.Equal(value, MemberListVisibilities.Format(visibility));
        Assert.False(MemberListVisibilities.TryParse(value.ToUpperInvariant(), out _));
    }

    [Theory]
    [InlineData(GroupRole.Viewer, true, false, false)]
    [InlineData(GroupRole.Member, true, false, false)]
    [InlineData(GroupRole.Admin, true, true, false)]
    [InlineData(GroupRole.Owner, true, true, true)]
    public void Members_view_admins_update_owners_delete(GroupRole role, bool view, bool update, bool delete)
    {
        Assert.Equal(view, GroupPolicy.Allows(role, GroupAction.View));
        Assert.Equal(update, GroupPolicy.Allows(role, GroupAction.Update));
        Assert.Equal(delete, GroupPolicy.Allows(role, GroupAction.Delete));
    }

    [Theory]
    [InlineData(GroupRole.Viewer, MemberListVisibility.AllMembers, true)]
    [InlineData(GroupRole.Viewer, MemberListVisibility.MembersAndAbove, false)]
    [InlineData(GroupRole.Member, MemberListVisibility.MembersAndAbove, true)]
    [InlineData(GroupRole.Owner, MemberListVisibility.MembersAndAbove, true)]
    public void Viewers_see_the_member_list_unless_hidden_from_them(GroupRole role, MemberListVisibility visibility, bool expected) =>
        Assert.Equal(expected, GroupPolicy.CanSeeMemberList(role, visibility));
}
