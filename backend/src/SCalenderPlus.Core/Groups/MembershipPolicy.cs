namespace SCalenderPlus.Core.Groups;

/// <summary>Outcome of a membership rule check; anything but <see cref="Allowed"/> refuses the change.</summary>
public enum MembershipVerdict
{
    Allowed,

    /// <summary>The actor's role does not allow it (403); <see cref="MembershipDecision.RequiredRole"/> says what would.</summary>
    Forbidden,

    /// <summary>The change would leave the group without an owner (409).</summary>
    LastOwner,

    /// <summary>The billing owner must hand over billing to another owner first (409).</summary>
    BillingOwnerTransferRequired,

    /// <summary>Billing can only go to a member with role owner (409).</summary>
    BillingOwnerMustBeOwner,

    /// <summary>The group is over its plan limit: no invites or role changes (409).</summary>
    GroupFrozen,
}

public sealed record MembershipDecision(MembershipVerdict Verdict, GroupRole? RequiredRole = null)
{
    public static MembershipDecision Allow { get; } = new(MembershipVerdict.Allowed);

    public bool IsAllowed => Verdict == MembershipVerdict.Allowed;

    internal static MembershipDecision Forbid(GroupRole required) => new(MembershipVerdict.Forbidden, required);
}

/// <summary>The target of a membership change as the rules see it.</summary>
/// <param name="Role">The target's current role.</param>
/// <param name="IsSelf">The actor changes their own membership.</param>
/// <param name="IsBillingOwner">The target is the group's billing owner.</param>
/// <param name="OwnerCount">Owners of the group (including the target, if an owner).</param>
public sealed record MembershipTarget(GroupRole Role, bool IsSelf, bool IsBillingOwner, int OwnerCount);

/// <summary>
/// Who may change whose membership (docs/architecture/permissions.md §6.1), as pure functions:
/// <list type="bullet">
/// <item>Owners manage everyone and assign every role, including owner.</item>
/// <item>Admins manage members and viewers only and assign at most <c>member</c>: they cannot demote, remove or
/// promote to admins/owners (only owners promote to admin or owner).</item>
/// <item>Members and viewers manage nobody; everyone may leave and lower their own role, nobody raises it.</item>
/// <item>A group always keeps an owner (last-owner protection), and the billing owner stays an owner until
/// billing is transferred to another owner.</item>
/// <item>Frozen groups (over the plan limit) allow no role changes or invites; removals and leaving stay
/// possible (cleaning up is how the group gets back under the limit).</item>
/// </list>
/// </summary>
public static class MembershipPolicy
{
    /// <summary>The highest role <paramref name="actor"/> may give others (role changes and invites); null = none.</summary>
    public static GroupRole? MaxAssignableRole(GroupRole actor) => actor switch
    {
        GroupRole.Owner => GroupRole.Owner,
        GroupRole.Admin => GroupRole.Member,
        _ => null,
    };

    /// <summary>Invite links can be forwarded freely, so they carry at most <c>member</c>.</summary>
    public const GroupRole MaxLinkInviteRole = GroupRole.Member;

    /// <summary>Whether <paramref name="actor"/> may manage (change the role of, remove) a member with <paramref name="target"/> role.</summary>
    public static bool CanManage(GroupRole actor, GroupRole target) =>
        actor == GroupRole.Owner || (actor == GroupRole.Admin && target <= GroupRole.Member);

    /// <summary>Who may see and revoke the group's invites and create new ones.</summary>
    public static bool CanManageInvites(GroupRole actor) => MaxAssignableRole(actor) is not null;

    public static MembershipDecision CheckRoleChange(GroupRole actor, MembershipTarget target, GroupRole newRole, bool groupFrozen)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.IsSelf)
        {
            if (newRole > target.Role)
            {
                return MembershipDecision.Forbid(GroupRole.Owner);
            }
        }
        else if (!CanManage(actor, target.Role) || newRole > MaxAssignableRole(actor))
        {
            return MembershipDecision.Forbid(target.Role >= GroupRole.Admin || newRole >= GroupRole.Admin ? GroupRole.Owner : GroupRole.Admin);
        }

        if (newRole == target.Role)
        {
            return MembershipDecision.Allow;
        }

        if (groupFrozen)
        {
            return new(MembershipVerdict.GroupFrozen);
        }

        return target.Role == GroupRole.Owner ? CheckOwnerLeaving(target) : MembershipDecision.Allow;
    }

    /// <summary>Removing a member, or leaving (<see cref="MembershipTarget.IsSelf"/>).</summary>
    public static MembershipDecision CheckRemoval(GroupRole actor, MembershipTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!target.IsSelf && !CanManage(actor, target.Role))
        {
            return MembershipDecision.Forbid(target.Role >= GroupRole.Admin ? GroupRole.Owner : GroupRole.Admin);
        }

        return target.Role == GroupRole.Owner ? CheckOwnerLeaving(target) : MembershipDecision.Allow;
    }

    /// <summary>
    /// Handing billing to another member: only the current billing owner may do it (it decides whose plan governs
    /// the group), and only to a member with role owner.
    /// </summary>
    /// <param name="newBillingOwnerRole">The recipient's role; null when not a member.</param>
    public static MembershipDecision CheckBillingTransfer(GroupRole actor, bool actorIsBillingOwner, GroupRole? newBillingOwnerRole)
    {
        if (actor != GroupRole.Owner || !actorIsBillingOwner)
        {
            return MembershipDecision.Forbid(GroupRole.Owner);
        }

        return newBillingOwnerRole == GroupRole.Owner ? MembershipDecision.Allow : new(MembershipVerdict.BillingOwnerMustBeOwner);
    }

    private static MembershipDecision CheckOwnerLeaving(MembershipTarget owner)
    {
        if (owner.OwnerCount <= 1)
        {
            return new(MembershipVerdict.LastOwner);
        }

        return owner.IsBillingOwner ? new(MembershipVerdict.BillingOwnerTransferRequired) : MembershipDecision.Allow;
    }
}
