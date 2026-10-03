using SCalenderPlus.Core.Groups;
using static SCalenderPlus.Core.Groups.GroupRole;

namespace SCalenderPlus.Core.Tests;

public sealed class MembershipPolicyTests
{
    private static MembershipTarget Other(GroupRole role, int owners = 2, bool billing = false) => new(role, IsSelf: false, billing, owners);

    private static MembershipTarget Self(GroupRole role, int owners = 2, bool billing = false) => new(role, IsSelf: true, billing, owners);

    [Theory]
    [InlineData(Owner, Owner)]
    [InlineData(Admin, Member)]
    public void Owners_assign_every_role_admins_at_most_member(GroupRole actor, GroupRole max) =>
        Assert.Equal(max, MembershipPolicy.MaxAssignableRole(actor));

    [Theory]
    [InlineData(Member)]
    [InlineData(Viewer)]
    public void Members_and_viewers_assign_nothing(GroupRole actor)
    {
        Assert.Null(MembershipPolicy.MaxAssignableRole(actor));
        Assert.False(MembershipPolicy.CanManageInvites(actor));
    }

    [Theory]
    [InlineData(Owner, Owner, true)]
    [InlineData(Owner, Admin, true)]
    [InlineData(Admin, Admin, false)]
    [InlineData(Admin, Owner, false)]
    [InlineData(Admin, Member, true)]
    [InlineData(Admin, Viewer, true)]
    [InlineData(Member, Viewer, false)]
    [InlineData(Viewer, Viewer, false)]
    public void Admins_cannot_touch_admins_or_owners(GroupRole actor, GroupRole target, bool expected) =>
        Assert.Equal(expected, MembershipPolicy.CanManage(actor, target));

    [Theory]
    [InlineData(Admin, Member, Viewer, MembershipVerdict.Allowed, null)]
    [InlineData(Admin, Viewer, Member, MembershipVerdict.Allowed, null)]
    [InlineData(Admin, Member, Admin, MembershipVerdict.Forbidden, Owner)] // only owners promote to admin
    [InlineData(Admin, Admin, Member, MembershipVerdict.Forbidden, Owner)] // admins cannot demote admins
    [InlineData(Admin, Owner, Admin, MembershipVerdict.Forbidden, Owner)]
    [InlineData(Member, Viewer, Member, MembershipVerdict.Forbidden, Admin)]
    [InlineData(Viewer, Member, Viewer, MembershipVerdict.Forbidden, Admin)]
    [InlineData(Owner, Member, Admin, MembershipVerdict.Allowed, null)]
    [InlineData(Owner, Member, Owner, MembershipVerdict.Allowed, null)]
    [InlineData(Owner, Owner, Admin, MembershipVerdict.Allowed, null)] // another owner, 2 owners
    public void Role_changes_of_others(GroupRole actor, GroupRole target, GroupRole newRole, MembershipVerdict verdict, GroupRole? required)
    {
        var decision = MembershipPolicy.CheckRoleChange(actor, Other(target), newRole, groupFrozen: false);

        Assert.Equal(verdict, decision.Verdict);
        Assert.Equal(required, decision.RequiredRole);
    }

    [Theory]
    [InlineData(Admin, Member, MembershipVerdict.Allowed)]
    [InlineData(Member, Viewer, MembershipVerdict.Allowed)]
    [InlineData(Member, Admin, MembershipVerdict.Forbidden)]
    [InlineData(Viewer, Member, MembershipVerdict.Forbidden)]
    [InlineData(Owner, Admin, MembershipVerdict.Allowed)] // 2 owners
    public void Everyone_may_lower_their_own_role_nobody_raises_it(GroupRole role, GroupRole newRole, MembershipVerdict verdict) =>
        Assert.Equal(verdict, MembershipPolicy.CheckRoleChange(role, Self(role), newRole, groupFrozen: false).Verdict);

    [Fact]
    public void The_last_owner_cannot_be_demoted_nor_demote_themselves()
    {
        Assert.Equal(MembershipVerdict.LastOwner, MembershipPolicy.CheckRoleChange(Owner, Self(Owner, owners: 1), Admin, false).Verdict);
        Assert.Equal(MembershipVerdict.LastOwner, MembershipPolicy.CheckRoleChange(Owner, Other(Owner, owners: 1), Member, false).Verdict);
    }

    [Fact]
    public void The_billing_owner_stays_an_owner_until_billing_is_transferred()
    {
        Assert.Equal(MembershipVerdict.BillingOwnerTransferRequired, MembershipPolicy.CheckRoleChange(Owner, Self(Owner, billing: true), Admin, false).Verdict);
        Assert.Equal(MembershipVerdict.BillingOwnerTransferRequired, MembershipPolicy.CheckRoleChange(Owner, Other(Owner, billing: true), Member, false).Verdict);
        Assert.Equal(MembershipVerdict.BillingOwnerTransferRequired, MembershipPolicy.CheckRemoval(Owner, Self(Owner, billing: true)).Verdict);
        Assert.Equal(MembershipVerdict.BillingOwnerTransferRequired, MembershipPolicy.CheckRemoval(Owner, Other(Owner, billing: true)).Verdict);
    }

    [Fact]
    public void Frozen_groups_allow_no_role_changes_but_no_ops_and_removals()
    {
        Assert.Equal(MembershipVerdict.GroupFrozen, MembershipPolicy.CheckRoleChange(Owner, Other(Member), Viewer, groupFrozen: true).Verdict);
        Assert.Equal(MembershipVerdict.Allowed, MembershipPolicy.CheckRoleChange(Owner, Other(Member), Member, groupFrozen: true).Verdict);
        Assert.Equal(MembershipVerdict.Forbidden, MembershipPolicy.CheckRoleChange(Member, Other(Viewer), Viewer, groupFrozen: true).Verdict);
        Assert.Equal(MembershipVerdict.Allowed, MembershipPolicy.CheckRemoval(Owner, Other(Member)).Verdict);
    }

    [Theory]
    [InlineData(Owner, Owner, MembershipVerdict.Allowed, null)]
    [InlineData(Owner, Admin, MembershipVerdict.Allowed, null)]
    [InlineData(Admin, Member, MembershipVerdict.Allowed, null)]
    [InlineData(Admin, Viewer, MembershipVerdict.Allowed, null)]
    [InlineData(Admin, Admin, MembershipVerdict.Forbidden, Owner)]
    [InlineData(Member, Viewer, MembershipVerdict.Forbidden, Admin)]
    public void Removing_others(GroupRole actor, GroupRole target, MembershipVerdict verdict, GroupRole? required)
    {
        var decision = MembershipPolicy.CheckRemoval(actor, Other(target));

        Assert.Equal(verdict, decision.Verdict);
        Assert.Equal(required, decision.RequiredRole);
    }

    [Theory]
    [InlineData(Viewer)]
    [InlineData(Member)]
    [InlineData(Admin)]
    public void Everyone_may_leave(GroupRole role) =>
        Assert.True(MembershipPolicy.CheckRemoval(role, Self(role)).IsAllowed);

    [Fact]
    public void The_last_owner_cannot_leave_or_be_removed()
    {
        Assert.Equal(MembershipVerdict.LastOwner, MembershipPolicy.CheckRemoval(Owner, Self(Owner, owners: 1)).Verdict);
        Assert.True(MembershipPolicy.CheckRemoval(Owner, Self(Owner, owners: 2)).IsAllowed);
    }

    [Theory]
    [InlineData(Owner, true, Owner, MembershipVerdict.Allowed)]
    [InlineData(Owner, true, Admin, MembershipVerdict.BillingOwnerMustBeOwner)]
    [InlineData(Owner, true, null, MembershipVerdict.BillingOwnerMustBeOwner)]
    [InlineData(Owner, false, Owner, MembershipVerdict.Forbidden)]
    [InlineData(Admin, false, Owner, MembershipVerdict.Forbidden)]
    public void Only_the_billing_owner_transfers_billing_and_only_to_an_owner(GroupRole actor, bool isBillingOwner, GroupRole? recipient, MembershipVerdict verdict) =>
        Assert.Equal(verdict, MembershipPolicy.CheckBillingTransfer(actor, isBillingOwner, recipient).Verdict);

    [Fact]
    public void Links_carry_at_most_member() => Assert.Equal(Member, MembershipPolicy.MaxLinkInviteRole);

    [Theory]
    [InlineData(Owner, Owner, false, MembershipVerdict.Allowed, null)]
    [InlineData(Owner, Admin, false, MembershipVerdict.Allowed, null)]
    [InlineData(Owner, Member, true, MembershipVerdict.Allowed, null)]
    [InlineData(Owner, Admin, true, MembershipVerdict.LinkRoleTooHigh, null)]
    [InlineData(Admin, Member, false, MembershipVerdict.Allowed, null)]
    [InlineData(Admin, Viewer, true, MembershipVerdict.Allowed, null)]
    [InlineData(Admin, Admin, false, MembershipVerdict.Forbidden, Owner)]
    [InlineData(Member, Viewer, true, MembershipVerdict.Forbidden, Admin)]
    [InlineData(Viewer, Viewer, false, MembershipVerdict.Forbidden, Admin)]
    public void Invites_carry_at_most_what_the_inviter_may_assign_and_links_at_most_member(GroupRole actor, GroupRole role, bool isLink, MembershipVerdict verdict, GroupRole? required)
    {
        var decision = MembershipPolicy.CheckInvite(actor, role, isLink, groupFrozen: false);

        Assert.Equal(verdict, decision.Verdict);
        Assert.Equal(required, decision.RequiredRole);
    }

    [Fact]
    public void Frozen_groups_take_no_invites() =>
        Assert.Equal(MembershipVerdict.GroupFrozen, MembershipPolicy.CheckInvite(Owner, Member, isLink: true, groupFrozen: true).Verdict);

    [Theory]
    [InlineData(Owner, Owner, true)]
    [InlineData(Admin, Member, true)]
    [InlineData(Admin, Admin, false)]
    [InlineData(Member, Viewer, false)]
    public void Invites_are_revoked_by_whoever_could_have_created_them(GroupRole actor, GroupRole inviteRole, bool allowed) =>
        Assert.Equal(allowed, MembershipPolicy.CheckInviteRevocation(actor, inviteRole).IsAllowed);
}
