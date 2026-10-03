using Microsoft.EntityFrameworkCore;
using NodaTime;
using SCalenderPlus.Application.Auditing;
using SCalenderPlus.Application.Common;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Application.Users;
using SCalenderPlus.Core.Groups;

namespace SCalenderPlus.Application.Groups;

/// <summary>A member as another member sees them.</summary>
/// <param name="Email">Only for admins and owners (they manage invites); null for members and viewers.</param>
public sealed record MemberView(Guid UserId, string DisplayName, string? Email, GroupRole Role, bool IsBillingOwner, Instant JoinedAt);

/// <summary>
/// Membership management (issue #35, docs/architecture/permissions.md §6.1, rules in
/// <see cref="MembershipPolicy"/>): list members, change roles, remove members, leave, transfer billing.
/// Every change runs in one transaction that also bumps the member's <c>acl_version</c>, notifies
/// <see cref="IGroupMembershipObserver"/>s (M2: revoke the user's event shares) and records an audit event.
/// Changes touch the group row, so concurrent changes of one group (two owners demoting each other) cannot both
/// pass the last-owner check: the later one fails with <c>412 precondition_failed</c>.
/// </summary>
public sealed class GroupMembershipService(
    IAppDbContext db,
    IAuditLog audit,
    IUserDirectory users,
    IEnumerable<IGroupMembershipObserver> observers,
    IClock clock)
{
    /// <summary>Members ordered by user id; viewers get 403 when the group hides the list from them.</summary>
    public async Task<Page<MemberView>> ListAsync(Guid actorId, Guid groupId, PageRequest page, CancellationToken cancellationToken = default)
    {
        var (group, actor) = await LoadAsync(actorId, groupId, tracked: false, cancellationToken).ConfigureAwait(false);
        if (!GroupPolicy.CanSeeMemberList(actor.Role, group.MemberListVisibility))
        {
            throw GroupErrors.InsufficientRole(GroupRole.Member, actor.Role);
        }

        var members = db.GroupMembers.AsNoTracking().Where(m => m.GroupId == groupId);
        if (page.After is { } after)
        {
            members = members.Where(m => m.UserId > after);
        }

        var rows = await members.OrderBy(m => m.UserId).Take(page.Limit + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        var profiles = await users.GetAsync([.. rows.Select(m => m.UserId)], cancellationToken).ConfigureAwait(false);
        var showEmail = MembershipPolicy.CanManageInvites(actor.Role);
        return page.ToPage([.. rows.Select(m => ToView(m, group, profiles, showEmail))], v => v.UserId);
    }

    /// <param name="precondition">Checks If-Match against the target's current state (after authorization).</param>
    /// <param name="revokeEventShares">On demotion: also revoke the user's individual event shares (M2 observer).</param>
    public async Task<MemberView> ChangeRoleAsync(
        Guid actorId,
        Guid groupId,
        Guid userId,
        GroupRole newRole,
        bool revokeEventShares = true,
        Action<MemberView>? precondition = null,
        CancellationToken cancellationToken = default)
    {
        return await db.InTransactionAsync(async ct =>
        {
            var (group, actor) = await LoadAsync(actorId, groupId, tracked: true, ct).ConfigureAwait(false);
            var member = await FindMemberAsync(groupId, userId, ct).ConfigureAwait(false);
            var target = await TargetAsync(group, member, actorId, ct).ConfigureAwait(false);

            var decision = MembershipPolicy.CheckRoleChange(actor.Role, target, newRole, group.FrozenAt is not null);
            if (!decision.IsAllowed)
            {
                throw GroupErrors.From(decision, actor.Role);
            }

            var profiles = await users.GetAsync([userId], ct).ConfigureAwait(false);
            var showEmail = MembershipPolicy.CanManageInvites(actor.Role);
            precondition?.Invoke(ToView(member, group, profiles, showEmail));
            if (member.Role == newRole)
            {
                return ToView(member, group, profiles, showEmail);
            }

            var oldRole = member.Role;
            member.Role = newRole;
            member.UpdatedAt = clock.Now();
            Touch(group);
            await users.BumpAclVersionAsync([userId], ct).ConfigureAwait(false);
            await NotifyAsync(new MembershipChange(groupId, userId, oldRole, newRole, MembershipChangeKind.RoleChanged, revokeEventShares && newRole < oldRole), ct).ConfigureAwait(false);
            audit.Record(
                GroupAuditActions.MemberRoleChanged,
                GroupAuditActions.ResourceType,
                groupId.ToString(),
                new MemberState(userId, GroupRoles.Format(oldRole)),
                new MemberState(userId, GroupRoles.Format(newRole)),
                group.OwnerUserId);
            await SaveAsync(ct).ConfigureAwait(false);
            return ToView(member, group, profiles, showEmail);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes a member, or — when <paramref name="userId"/> is the actor — leaves the group.</summary>
    /// <param name="revokeEventShares">Also revoke the user's individual event shares on the group's calendars (M2 observer).</param>
    public async Task RemoveAsync(
        Guid actorId,
        Guid groupId,
        Guid userId,
        bool revokeEventShares = true,
        Action<MemberView>? precondition = null,
        CancellationToken cancellationToken = default)
    {
        await db.InTransactionAsync(async ct =>
        {
            var (group, actor) = await LoadAsync(actorId, groupId, tracked: true, ct).ConfigureAwait(false);
            var member = await FindMemberAsync(groupId, userId, ct).ConfigureAwait(false);
            var target = await TargetAsync(group, member, actorId, ct).ConfigureAwait(false);

            var decision = MembershipPolicy.CheckRemoval(actor.Role, target);
            if (!decision.IsAllowed)
            {
                throw GroupErrors.From(decision, actor.Role);
            }

            if (precondition is not null)
            {
                var profiles = await users.GetAsync([userId], ct).ConfigureAwait(false);
                precondition(ToView(member, group, profiles, MembershipPolicy.CanManageInvites(actor.Role)));
            }

            var kind = target.IsSelf ? MembershipChangeKind.Left : MembershipChangeKind.Removed;
            db.GroupMembers.Remove(member);
            Touch(group);
            await users.BumpAclVersionAsync([userId], ct).ConfigureAwait(false);
            await NotifyAsync(new MembershipChange(groupId, userId, member.Role, null, kind, revokeEventShares), ct).ConfigureAwait(false);
            audit.Record(
                target.IsSelf ? GroupAuditActions.MemberLeft : GroupAuditActions.MemberRemoved,
                GroupAuditActions.ResourceType,
                groupId.ToString(),
                new MemberState(userId, GroupRoles.Format(member.Role)),
                new { RevokeEventShares = revokeEventShares },
                group.OwnerUserId);
            await SaveAsync(ct).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Makes another owner the billing owner (only the current billing owner may).</summary>
    public async Task<GroupView> TransferBillingAsync(Guid actorId, Guid groupId, Guid newBillingOwnerId, CancellationToken cancellationToken = default)
    {
        var (group, actor) = await LoadAsync(actorId, groupId, tracked: true, cancellationToken).ConfigureAwait(false);
        var recipient = await db.GroupMembers.AsNoTracking()
            .SingleOrDefaultAsync(m => m.GroupId == groupId && m.UserId == newBillingOwnerId, cancellationToken).ConfigureAwait(false);

        var decision = MembershipPolicy.CheckBillingTransfer(actor.Role, group.OwnerUserId == actorId, recipient?.Role);
        if (!decision.IsAllowed)
        {
            throw GroupErrors.From(decision, actor.Role, actor.Role == GroupRole.Owner ? "Only the billing owner can transfer billing." : null);
        }

        if (group.OwnerUserId != newBillingOwnerId)
        {
            var before = new { BillingOwnerId = group.OwnerUserId };
            group.OwnerUserId = newBillingOwnerId;
            Touch(group);
            audit.Record(GroupAuditActions.BillingOwnerTransferred, GroupAuditActions.ResourceType, groupId.ToString(), before, new { BillingOwnerId = newBillingOwnerId }, newBillingOwnerId);
            await SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        var count = await db.GroupMembers.CountAsync(m => m.GroupId == groupId, cancellationToken).ConfigureAwait(false);
        return new GroupView(group, actor.Role, count);
    }

    private async Task<(Group Group, GroupMember Actor)> LoadAsync(Guid actorId, Guid groupId, bool tracked, CancellationToken cancellationToken)
    {
        var actor = await db.GroupMembers.AsNoTracking()
            .SingleOrDefaultAsync(m => m.GroupId == groupId && m.UserId == actorId, cancellationToken).ConfigureAwait(false)
            ?? throw GroupErrors.GroupNotFound();
        var groups = tracked ? db.Groups : db.Groups.AsNoTracking();
        var group = await groups.SingleAsync(g => g.Id == groupId, cancellationToken).ConfigureAwait(false);
        return (group, actor);
    }

    /// <summary>
    /// Updates the group row in the same save, so its <c>xmin</c> check serializes concurrent membership changes of
    /// the group. Marked modified explicitly: an unchanged value (same clock reading) would otherwise skip the
    /// UPDATE, and with it the concurrency check.
    /// </summary>
    private void Touch(Group group)
    {
        group.UpdatedAt = clock.Now();
        db.Groups.Entry(group).Property(g => g.UpdatedAt).IsModified = true;
    }

    private async Task<GroupMember> FindMemberAsync(Guid groupId, Guid userId, CancellationToken cancellationToken) =>
        await db.GroupMembers.SingleOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId, cancellationToken).ConfigureAwait(false)
        ?? throw GroupErrors.MemberNotFound();

    private async Task<MembershipTarget> TargetAsync(Group group, GroupMember member, Guid actorId, CancellationToken cancellationToken)
    {
        var owners = await db.GroupMembers.CountAsync(m => m.GroupId == group.Id && m.Role == GroupRole.Owner, cancellationToken).ConfigureAwait(false);
        return new MembershipTarget(member.Role, member.UserId == actorId, member.UserId == group.OwnerUserId, owners);
    }

    private static MemberView ToView(GroupMember member, Group group, IReadOnlyDictionary<Guid, UserSummary> profiles, bool showEmail)
    {
        var profile = profiles.GetValueOrDefault(member.UserId);
        return new MemberView(member.UserId, profile?.DisplayName ?? string.Empty, showEmail ? profile?.Email : null, member.Role, member.UserId == group.OwnerUserId, member.JoinedAt);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw GroupErrors.Changed();
        }
    }

    private async Task NotifyAsync(MembershipChange change, CancellationToken cancellationToken)
    {
        foreach (var observer in observers)
        {
            await observer.OnMembershipChangedAsync(change, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed record MemberState(Guid UserId, string Role);
}
