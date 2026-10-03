using Microsoft.EntityFrameworkCore;
using NodaTime;
using SCalenderPlus.Application.Auditing;
using SCalenderPlus.Application.Common;
using SCalenderPlus.Application.Events;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Application.Users;
using SCalenderPlus.Core.Calendars;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Application.Calendars;

/// <summary>A grant with the display name of the user or the name of the group it names.</summary>
public sealed record GrantView(CalendarGrantEntry Grant, string PrincipalName);

/// <summary>
/// Calendar grants (issue #42, docs/architecture/permissions.md §4.5): calendar managers add, change and remove
/// <c>user</c> and <c>group[minRole]</c> grants with levels <c>free_busy</c> … <c>manage</c>
/// (<see cref="AccessPolicy.CanGrant"/>: never <c>owner</c>, never above their own level; for changes and
/// removals the old level too). A change that would take away the actor's own <c>manage</c> level is refused
/// (<c>409 permission_self_lockout</c>). Every change bumps <c>calendars.acl_version</c> and the
/// <c>acl_version</c> of the user or group it names, and is audited on the calendar. Removing or lowering a grant
/// revokes the individual event shares of the people who lose level through it (<see cref="EventShareRevocation"/>,
/// permissions.md §4.6) unless the caller opts out.
/// </summary>
public sealed class CalendarGrantService(
    IAppDbContext db,
    CalendarAccessLoader access,
    AclVersions aclVersions,
    EventShareRevocation shares,
    IUserDirectory users,
    IAuditLog audit,
    IClock clock)
{
    /// <summary>The calendar's grants (managers), ordered by id.</summary>
    public async Task<Page<GrantView>> ListAsync(Guid actorId, Guid calendarId, PageRequest page, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(actorId, calendarId, CalendarAction.ManageSharing, cancellationToken: cancellationToken).ConfigureAwait(false);
        var grants = db.CalendarGrants.AsNoTracking().Where(g => g.CalendarId == calendarId);
        if (page.After is { } after)
        {
            grants = grants.Where(g => g.Id > after);
        }

        var rows = await grants.OrderBy(g => g.Id).Take(page.Limit + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        return page.ToPage(await ViewsAsync(rows, cancellationToken).ConfigureAwait(false), v => v.Grant.Id);
    }

    /// <summary>
    /// Grants <paramref name="level"/> to <paramref name="principal"/>. Selectable principals: users who share a
    /// group with the actor or already see the calendar, groups the actor belongs to or that already hold a grant
    /// on it (else <c>400 validation_failed</c>, also for unknown ids); not the owner. One grant per principal
    /// (<c>409 conflict</c>).
    /// </summary>
    public async Task<GrantView> CreateAsync(Guid actorId, Guid calendarId, Principal principal, CalendarLevel level, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return await db.InTransactionAsync(async ct =>
        {
            var loaded = await access.RequireAsync(actorId, calendarId, CalendarAction.ManageSharing, forUpdate: true, ct).ConfigureAwait(false);
            EnsureNotFrozen(loaded);
            EnsureCanGrant(loaded, level);
            await EnsureSelectableAsync(loaded, principal, ct).ConfigureAwait(false);
            if (loaded.Grants.Any(g => g.Principal == principal))
            {
                throw CalendarErrors.GrantExists();
            }

            var grant = CalendarGrantEntry.For(calendarId, principal, level);
            grant.CreatedBy = actorId;
            grant.CreatedAt = grant.UpdatedAt = clock.Now();
            db.CalendarGrants.Add(grant);

            await BumpAsync(loaded.Calendar, grant, ct).ConfigureAwait(false);
            audit.Record(CalendarAuditActions.GrantCreated, CalendarAuditActions.ResourceType, calendarId.ToString(), null, State(grant), await SubjectAsync(loaded, ct).ConfigureAwait(false));
            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateException exception) when (exception is not DbUpdateConcurrencyException)
            {
                throw CalendarErrors.GrantExists(); // a concurrent grant for the same principal (unique index)
            }

            return (await ViewsAsync([grant], ct).ConfigureAwait(false))[0];
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <param name="precondition">Checks If-Match against the grant's current state (after authorization); throws to refuse.</param>
    /// <param name="revokeEventShares">When the level is lowered: also revoke the individual event shares of those who lose level.</param>
    public async Task<GrantView> UpdateAsync(
        Guid actorId,
        Guid calendarId,
        Guid grantId,
        CalendarLevel level,
        Action<CalendarGrantEntry>? precondition = null,
        bool revokeEventShares = true,
        CancellationToken cancellationToken = default) =>
        await db.InTransactionAsync(async ct =>
        {
            var (loaded, grant) = await LoadGrantAsync(actorId, calendarId, grantId, precondition, ct).ConfigureAwait(false);
            EnsureCanGrant(loaded, grant.Level);
            EnsureCanGrant(loaded, level);
            if (grant.Level != level)
            {
                var before = State(grant);
                var oldLevel = grant.Level;
                grant.Level = level;
                grant.UpdatedAt = clock.Now();
                EnsureNoSelfLockout(loaded, loaded.Grants);
                if (revokeEventShares && level < oldLevel)
                {
                    await shares.OnGrantChangedAsync(loaded.Acl, loaded.Calendar.ToAcl(loaded.Grants), grant.Principal, ct).ConfigureAwait(false);
                }

                await BumpAsync(loaded.Calendar, grant, ct).ConfigureAwait(false);
                audit.Record(CalendarAuditActions.GrantUpdated, CalendarAuditActions.ResourceType, calendarId.ToString(), before, State(grant), await SubjectAsync(loaded, ct).ConfigureAwait(false));
                await SaveAsync(ct).ConfigureAwait(false);
            }

            return (await ViewsAsync([grant], ct).ConfigureAwait(false))[0];
        }, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Removes the grant and (unless <paramref name="revokeEventShares"/> is false) the individual event shares of the
    /// people who lose level through it (permissions.md §4.6).
    /// </summary>
    public async Task DeleteAsync(
        Guid actorId,
        Guid calendarId,
        Guid grantId,
        Action<CalendarGrantEntry>? precondition = null,
        bool revokeEventShares = true,
        CancellationToken cancellationToken = default) =>
        await db.InTransactionAsync(async ct =>
        {
            var (loaded, grant) = await LoadGrantAsync(actorId, calendarId, grantId, precondition, ct).ConfigureAwait(false);
            EnsureCanGrant(loaded, grant.Level);
            var remaining = loaded.Grants.Where(g => g.Id != grantId).ToList();
            EnsureNoSelfLockout(loaded, remaining);
            if (revokeEventShares)
            {
                await shares.OnGrantChangedAsync(loaded.Acl, loaded.Calendar.ToAcl(remaining), grant.Principal, ct).ConfigureAwait(false);
            }

            await BumpAsync(loaded.Calendar, grant, ct).ConfigureAwait(false);
            audit.Record(CalendarAuditActions.GrantRemoved, CalendarAuditActions.ResourceType, calendarId.ToString(), State(grant), null, await SubjectAsync(loaded, ct).ConfigureAwait(false));
            db.CalendarGrants.Remove(grant);
            await SaveAsync(ct).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);

    /// <summary>Audit snapshot of a grant: principal (<c>user:{id}</c>, <c>group:{id}[role]</c>) and level.</summary>
    internal static GrantState State(CalendarGrantEntry grant) =>
        new(grant.Id, grant.Principal.ToString(), PermissionLevels.Format(grant.Level));

    private async Task<(CalendarAccess Access, CalendarGrantEntry Grant)> LoadGrantAsync(
        Guid actorId,
        Guid calendarId,
        Guid grantId,
        Action<CalendarGrantEntry>? precondition,
        CancellationToken cancellationToken)
    {
        var loaded = await access.RequireAsync(actorId, calendarId, CalendarAction.ManageSharing, forUpdate: true, cancellationToken).ConfigureAwait(false);
        var grant = loaded.Grants.SingleOrDefault(g => g.Id == grantId) ?? throw CalendarErrors.GrantNotFound();
        precondition?.Invoke(grant);
        EnsureNotFrozen(loaded);
        return (loaded, grant);
    }

    private static void EnsureNotFrozen(CalendarAccess loaded)
    {
        if (loaded.Calendar.FrozenAt is not null)
        {
            throw CalendarErrors.Frozen();
        }
    }

    private static void EnsureCanGrant(CalendarAccess loaded, CalendarLevel level)
    {
        if (!AccessPolicy.CanGrant(loaded.Level, level))
        {
            throw Validation.Failed("level", $"Grants carry free_busy, read, contribute, edit or manage, at most your own level ({PermissionLevels.Format(loaded.Level)}).");
        }
    }

    private static void EnsureNoSelfLockout(CalendarAccess loaded, IReadOnlyList<CalendarGrantEntry> grantsAfter)
    {
        if (PermissionEngine.ResolveCalendarLevel(loaded.Principal, loaded.Calendar.ToAcl(grantsAfter)) < CalendarLevel.Manage)
        {
            throw CalendarErrors.SelfLockout();
        }
    }

    private async Task EnsureSelectableAsync(CalendarAccess loaded, Principal principal, CancellationToken cancellationToken)
    {
        if (principal == loaded.Calendar.Owner || (principal.Type == PrincipalType.Group && principal.Id == loaded.Calendar.OwnerGroupId))
        {
            throw Validation.Failed("principal", loaded.Calendar.IsGroupOwned
                ? "The owning group's members get the role defaults; change those instead."
                : "The owner already has every right.");
        }

        var target = principal.Id!.Value;
        var selectable = principal.Type == PrincipalType.Group
            ? loaded.Principal.Groups.ContainsKey(target) || loaded.Grants.Any(g => g.PrincipalType == PrincipalType.Group && g.PrincipalId == target)
            : await IsSelectableUserAsync(loaded, target, cancellationToken).ConfigureAwait(false);
        if (!selectable)
        {
            throw Validation.Failed("principal", principal.Type == PrincipalType.Group
                ? "Pick one of your groups, or a group that already has access to this calendar."
                : "Pick someone who shares a group with you or already sees this calendar.");
        }
    }

    private async Task<bool> IsSelectableUserAsync(CalendarAccess loaded, Guid userId, CancellationToken cancellationToken)
    {
        var actorGroups = loaded.Principal.Groups.Keys.ToList();
        if (await db.GroupMembers.AnyAsync(m => m.UserId == userId && actorGroups.Contains(m.GroupId), cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        var exists = (await users.GetAsync([userId], cancellationToken).ConfigureAwait(false)).Count == 1;
        return exists
            && PermissionEngine.ResolveCalendarLevel(await access.PrincipalAsync(userId, cancellationToken).ConfigureAwait(false), loaded.Acl) >= CalendarLevel.FreeBusy;
    }

    private async Task BumpAsync(Calendar calendar, CalendarGrantEntry grant, CancellationToken cancellationToken)
    {
        await aclVersions.BumpCalendarAsync(calendar.Id, cancellationToken).ConfigureAwait(false);
        await aclVersions.BumpPrincipalsAsync([grant.Principal], cancellationToken).ConfigureAwait(false);
    }

    private Task<Guid> SubjectAsync(CalendarAccess loaded, CancellationToken cancellationToken) =>
        CalendarAudit.BillingSubjectAsync(db, loaded.Calendar, cancellationToken);

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw CalendarErrors.Changed();
        }
    }

    private async Task<IReadOnlyList<GrantView>> ViewsAsync(IReadOnlyList<CalendarGrantEntry> grants, CancellationToken cancellationToken)
    {
        var userIds = grants.Where(g => g.PrincipalType == PrincipalType.User).Select(g => g.PrincipalId).ToList();
        var groupIds = grants.Where(g => g.PrincipalType == PrincipalType.Group).Select(g => g.PrincipalId).Distinct().ToList();
        var people = await users.GetAsync(userIds, cancellationToken).ConfigureAwait(false);
        var groups = groupIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Groups.AsNoTracking().Where(g => groupIds.Contains(g.Id))
                .ToDictionaryAsync(g => g.Id, g => g.Name, cancellationToken).ConfigureAwait(false);
        return [.. grants.Select(g => new GrantView(g, Name(g)))];

        string Name(CalendarGrantEntry g) =>
            g.PrincipalType == PrincipalType.Group
                ? groups.GetValueOrDefault(g.PrincipalId, string.Empty)
                : people.TryGetValue(g.PrincipalId, out var user) ? user.DisplayName : string.Empty;
    }

    internal sealed record GrantState(Guid GrantId, string Principal, string Level);
}
