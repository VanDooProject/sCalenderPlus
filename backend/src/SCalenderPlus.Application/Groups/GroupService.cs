using Microsoft.EntityFrameworkCore;
using NodaTime;
using SCalenderPlus.Application.Auditing;
using SCalenderPlus.Application.Calendars;
using SCalenderPlus.Application.Common;
using SCalenderPlus.Application.Entitlements;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Application.Users;
using SCalenderPlus.Core.Groups;

namespace SCalenderPlus.Application.Groups;

/// <summary>A group as one of its members sees it.</summary>
/// <param name="MyRole">The caller's role.</param>
public sealed record GroupView(Group Group, GroupRole MyRole, int MemberCount);

/// <summary>Merge-patch of a group's settings: <c>null</c> = unchanged; an empty description removes it.</summary>
public sealed record GroupChanges(string? Name = null, string? Description = null, MemberListVisibility? MemberListVisibility = null);

/// <summary>
/// Groups CRUD (issue #34, docs/architecture/permissions.md §6): the creator becomes owner and billing owner;
/// every member sees the group, admins change its settings, owners delete it. Non-members get 404 for every
/// group (no existence leaks), members without the role <c>403 insufficient_permission</c>. Mutations are
/// audited and bump the <c>acl_version</c> of the affected users.
/// </summary>
public sealed class GroupService(
    IAppDbContext db,
    IAuditLog audit,
    IUserDirectory users,
    IEntitlementService entitlements,
    IEnumerable<IGroupMembershipObserver> observers,
    CalendarGroupLifecycle calendars,
    IClock clock)
{
    public async Task<GroupView> CreateAsync(Guid actorId, string name, string? description, CancellationToken cancellationToken = default)
    {
        var group = new Group
        {
            Id = Guid.CreateVersion7(),
            Name = ValidName(name),
            Description = ValidDescription(description),
            OwnerUserId = actorId,
            MemberListVisibility = MemberListVisibility.AllMembers,
        };
        group.CreatedAt = group.UpdatedAt = clock.Now();
        var owner = new GroupMember { GroupId = group.Id, UserId = actorId, Role = GroupRole.Owner, JoinedAt = group.CreatedAt, UpdatedAt = group.CreatedAt };

        await entitlements.EnsureCanCreateGroupAsync(actorId, cancellationToken).ConfigureAwait(false);
        return await db.InTransactionAsync(async ct =>
        {
            db.Groups.Add(group);
            db.GroupMembers.Add(owner);
            await users.BumpAclVersionAsync([actorId], ct).ConfigureAwait(false);
            await NotifyAsync(new MembershipChange(group.Id, actorId, null, GroupRole.Owner, MembershipChangeKind.Joined), ct).ConfigureAwait(false);
            audit.Record(GroupAuditActions.Created, GroupAuditActions.ResourceType, group.Id.ToString(), null, GroupAudit.State(group), group.OwnerUserId);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return new GroupView(group, GroupRole.Owner, 1);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <exception cref="Errors.AppException"><c>not_found</c> unless the actor is a member.</exception>
    public async Task<GroupView> GetAsync(Guid actorId, Guid groupId, CancellationToken cancellationToken = default)
    {
        var view = await (
            from m in db.GroupMembers.AsNoTracking()
            where m.UserId == actorId && m.GroupId == groupId
            join g in db.Groups.AsNoTracking() on m.GroupId equals g.Id
            select new { Group = g, m.Role, Count = db.GroupMembers.Count(x => x.GroupId == g.Id) })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return view is null ? throw GroupErrors.GroupNotFound() : new GroupView(view.Group, view.Role, view.Count);
    }

    /// <summary>The actor's groups with their role, ordered by group id (creation order).</summary>
    public async Task<Page<GroupView>> ListMineAsync(Guid actorId, PageRequest page, CancellationToken cancellationToken = default)
    {
        var memberships = db.GroupMembers.AsNoTracking().Where(m => m.UserId == actorId);
        if (page.After is { } after)
        {
            memberships = memberships.Where(m => m.GroupId > after);
        }

        var rows = await (
            from m in memberships
            join g in db.Groups.AsNoTracking() on m.GroupId equals g.Id
            orderby g.Id
            select new { Group = g, m.Role, Count = db.GroupMembers.Count(x => x.GroupId == g.Id) })
            .Take(page.Limit + 1)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return page.ToPage([.. rows.Select(r => new GroupView(r.Group, r.Role, r.Count))], v => v.Group.Id);
    }

    /// <param name="precondition">
    /// Checks the caller's precondition (If-Match) against the current state after authorization and before the
    /// change; throws to refuse. A concurrent change after this check fails with <c>412 precondition_failed</c>.
    /// </param>
    public async Task<GroupView> UpdateAsync(Guid actorId, Guid groupId, GroupChanges changes, Action<GroupView>? precondition = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var (group, role) = await LoadForAsync(actorId, groupId, GroupAction.Update, precondition, cancellationToken).ConfigureAwait(false);

        var before = GroupAudit.State(group);
        group.Name = changes.Name is null ? group.Name : ValidName(changes.Name);
        group.Description = changes.Description is null ? group.Description : ValidDescription(changes.Description);
        group.MemberListVisibility = changes.MemberListVisibility ?? group.MemberListVisibility;
        var after = GroupAudit.State(group);

        if (after != before)
        {
            group.UpdatedAt = clock.Now();
            audit.Record(GroupAuditActions.Updated, GroupAuditActions.ResourceType, group.Id.ToString(), before, after, group.OwnerUserId);
            await SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        var count = await db.GroupMembers.CountAsync(m => m.GroupId == groupId, cancellationToken).ConfigureAwait(false);
        return new GroupView(group, role, count);
    }

    /// <summary>
    /// Deletes the group for real with its memberships and invites (owners only). Every former member's
    /// <c>acl_version</c> is bumped and membership observers learn about each ended membership. A group that
    /// still owns calendars cannot be deleted (<c>409 group_has_calendars</c>); grants naming the group are
    /// removed with it (<see cref="CalendarGroupLifecycle"/>).
    /// </summary>
    public async Task DeleteAsync(Guid actorId, Guid groupId, Action<GroupView>? precondition = null, CancellationToken cancellationToken = default)
    {
        await db.InTransactionAsync(async ct =>
        {
            var (group, _) = await LoadForAsync(actorId, groupId, GroupAction.Delete, precondition, ct).ConfigureAwait(false);
            await calendars.OnGroupDeletingAsync(groupId, ct).ConfigureAwait(false);
            var members = await db.GroupMembers.Where(m => m.GroupId == groupId).ToListAsync(ct).ConfigureAwait(false);

            await users.BumpAclVersionAsync([.. members.Select(m => m.UserId)], ct).ConfigureAwait(false);
            foreach (var member in members)
            {
                await NotifyAsync(new MembershipChange(groupId, member.UserId, member.Role, null, MembershipChangeKind.GroupDeleted), ct).ConfigureAwait(false);
            }

            audit.Record(
                GroupAuditActions.Deleted,
                GroupAuditActions.ResourceType,
                group.Id.ToString(),
                GroupAudit.State(group) with { MemberCount = members.Count },
                null,
                group.OwnerUserId);
            db.GroupMembers.RemoveRange(members);
            db.Groups.Remove(group);
            await SaveAsync(ct).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(Group Group, GroupRole Role)> LoadForAsync(Guid actorId, Guid groupId, GroupAction action, Action<GroupView>? precondition, CancellationToken cancellationToken)
    {
        var membership = await db.GroupMembers.AsNoTracking()
            .SingleOrDefaultAsync(m => m.GroupId == groupId && m.UserId == actorId, cancellationToken).ConfigureAwait(false)
            ?? throw GroupErrors.GroupNotFound();
        if (!GroupPolicy.Allows(membership.Role, action))
        {
            throw GroupErrors.InsufficientRole(GroupPolicy.RequiredRole(action), membership.Role);
        }

        var group = await db.Groups.SingleAsync(g => g.Id == groupId, cancellationToken).ConfigureAwait(false);
        if (precondition is not null)
        {
            var count = await db.GroupMembers.CountAsync(m => m.GroupId == groupId, cancellationToken).ConfigureAwait(false);
            precondition(new GroupView(group, membership.Role, count));
        }

        return (group, membership.Role);
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

    private static string ValidName(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        return trimmed.Length is >= 1 and <= Group.NameMaxLength
            ? trimmed
            : throw Validation.Failed("name", $"The name must have 1 to {Group.NameMaxLength} characters.");
    }

    private static string? ValidDescription(string? description)
    {
        var trimmed = description?.Trim();
        return trimmed?.Length > Group.DescriptionMaxLength
            ? throw Validation.Failed("description", $"The description must have at most {Group.DescriptionMaxLength} characters.")
            : string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}

/// <summary>Audit snapshots of groups.</summary>
internal static class GroupAudit
{
    public static GroupState State(Group group) =>
        new(group.Name, group.Description, MemberListVisibilities.Format(group.MemberListVisibility), group.OwnerUserId, null);

    public sealed record GroupState(string Name, string? Description, string MemberListVisibility, Guid BillingOwnerId, int? MemberCount);
}
