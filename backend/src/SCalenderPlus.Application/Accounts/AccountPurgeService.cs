using Microsoft.EntityFrameworkCore;
using NodaTime;
using SCalenderPlus.Application.Auditing;
using SCalenderPlus.Application.Calendars;
using SCalenderPlus.Application.Common;
using SCalenderPlus.Application.Email;
using SCalenderPlus.Application.Events;
using SCalenderPlus.Application.Groups;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Application.Users;
using SCalenderPlus.Core.Calendars;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Application.Accounts;

/// <summary>
/// Purges an account after its grace period (permissions.md §4.6 "User deleted", GDPR Art. 17), in one transaction
/// serialized with cancelling (advisory lock on the user id):
/// <list type="number">
/// <item>Groups: one the account is the only member of is deleted with its calendars; otherwise the membership ends —
/// as the last owner, the longest-standing member of the highest role becomes owner, and billing goes to an owner
/// (<c>group.owner_handed_over</c>, <c>group.billing_owner_transferred</c>). Group calendars survive.</item>
/// <item>Personal calendars are deleted with their events (shared ones blocked the request; any shared since are
/// deleted too — the grace period was the notice).</item>
/// <item>Grants and event overrides naming the account are removed (calendars, events and named principals get their
/// <c>acl_version</c> / sync rows), so its id lingers in no ACL; events it created elsewhere keep
/// <c>creator_user_id</c> as a tombstone (no floor, no name).</item>
/// <item>The audit trail is pseudonymised (<see cref="IAccountStore.PseudonymiseAuditAsync"/>), the final email is
/// queued, <c>user.deleted</c> is recorded and the account row is deleted (feed links, prefs, Identity data cascade).</item>
/// </list>
/// A request that was cancelled or replaced meanwhile (<c>deleted_at</c> ≠ <paramref name="requestedAt"/>) is a no-op.
/// </summary>
public sealed class AccountPurgeService(
    IAppDbContext db,
    IAccountStore accounts,
    IUserDirectory users,
    GroupService groups,
    CalendarService calendars,
    EventShareRevocation shares,
    AclVersions aclVersions,
    IAuditLog audit,
    IEmailOutbox outbox,
    AccountEmails emails,
    IClock clock)
{
    /// <summary>Purges the account if <paramref name="requestedAt"/> is still its deletion request and the grace period is over.</summary>
    /// <returns>Whether the account was purged.</returns>
    public Task<bool> PurgeAsync(Guid userId, Instant requestedAt, CancellationToken cancellationToken = default) =>
        db.InTransactionAsync(async ct =>
        {
            await db.LockAsync(userId, ct).ConfigureAwait(false);
            var account = await accounts.FindAsync(userId, ct).ConfigureAwait(false);
            if (account?.DeletedAt is not { } deletedAt || deletedAt != requestedAt)
            {
                return false; // cancelled, re-requested (its own job follows) or already purged
            }

            if (clock.Now() < AccountDeletionService.PurgeAt(deletedAt))
            {
                throw new InvalidOperationException($"The grace period of account {userId} is not over yet."); // retried later
            }

            var summary = new PurgeSummary();
            await LeaveGroupsAsync(userId, summary, ct).ConfigureAwait(false);
            await DeletePersonalCalendarsAsync(userId, summary, ct).ConfigureAwait(false);
            await RemoveGrantsAsync(userId, summary, ct).ConfigureAwait(false);
            await shares.RemoveUserOverridesAsync(userId, ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            await accounts.PseudonymiseAuditAsync(userId, account.Email, ct).ConfigureAwait(false);
            outbox.Queue(emails.AccountDeleted(account.Email, account.DisplayName, account.Locale));
            audit.Record(AccountAuditActions.Deleted, AccountAuditActions.ResourceType, userId.ToString(), null, summary, userId);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await accounts.DeleteAsync(userId, ct).ConfigureAwait(false);
            return true;
        }, cancellationToken);

    private async Task LeaveGroupsAsync(Guid userId, PurgeSummary summary, CancellationToken cancellationToken)
    {
        var groupIds = await db.GroupMembers.AsNoTracking().Where(m => m.UserId == userId).Select(m => m.GroupId).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var groupId in groupIds)
        {
            var members = await db.GroupMembers.Where(m => m.GroupId == groupId).ToListAsync(cancellationToken).ConfigureAwait(false);
            if (members.Count == 1)
            {
                // Alone in the group: it goes, with its calendars (nobody else sees them).
                var owned = await db.Calendars.Where(c => c.OwnerGroupId == groupId).ToListAsync(cancellationToken).ConfigureAwait(false);
                foreach (var calendar in owned)
                {
                    await DeleteCalendarAsync(calendar, cancellationToken).ConfigureAwait(false);
                    summary.CalendarsDeleted++;
                }

                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                var lonely = await db.Groups.SingleAsync(g => g.Id == groupId, cancellationToken).ConfigureAwait(false); // fresh: deleting calendars bumped its acl_version
                await groups.RemoveAsync(lonely, cancellationToken).ConfigureAwait(false);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                summary.GroupsDeleted++;
                continue;
            }

            var group = await db.Groups.SingleAsync(g => g.Id == groupId, cancellationToken).ConfigureAwait(false);
            var leaving = members.Single(m => m.UserId == userId);
            var others = members.Where(m => m.UserId != userId).ToList();
            var owners = others.Where(m => m.Role == GroupRole.Owner).OrderBy(m => m.JoinedAt).ThenBy(m => m.UserId).ToList();
            var now = clock.Now();
            if (owners.Count == 0)
            {
                var successor = others.OrderByDescending(m => m.Role).ThenBy(m => m.JoinedAt).ThenBy(m => m.UserId).First();
                var oldRole = successor.Role;
                successor.Role = GroupRole.Owner;
                successor.UpdatedAt = now;
                owners.Add(successor);
                await users.BumpAclVersionAsync([successor.UserId], cancellationToken).ConfigureAwait(false);
                audit.Record(
                    GroupAuditActions.OwnerHandedOver,
                    GroupAuditActions.ResourceType,
                    groupId.ToString(),
                    new { UserId = successor.UserId, Role = GroupRoles.Format(oldRole) },
                    new { UserId = successor.UserId, Role = GroupRoles.Format(GroupRole.Owner), Reason = "account_deleted" },
                    group.OwnerUserId);
                summary.GroupsHandedOver++;
            }

            if (group.OwnerUserId == userId)
            {
                group.OwnerUserId = owners[0].UserId;
                audit.Record(GroupAuditActions.BillingOwnerTransferred, GroupAuditActions.ResourceType, groupId.ToString(), new { BillingOwnerId = userId }, new { BillingOwnerId = group.OwnerUserId, Reason = "account_deleted" }, group.OwnerUserId);
            }

            audit.Record(
                GroupAuditActions.MemberRemoved,
                GroupAuditActions.ResourceType,
                groupId.ToString(),
                new { UserId = userId, Role = GroupRoles.Format(leaving.Role) },
                new { Reason = "account_deleted" },
                group.OwnerUserId);
            db.GroupMembers.Remove(leaving);
            group.UpdatedAt = now; // serializes with concurrent membership changes (xmin)
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            summary.GroupsLeft++;
        }
    }

    private async Task DeletePersonalCalendarsAsync(Guid userId, PurgeSummary summary, CancellationToken cancellationToken)
    {
        var owned = await db.Calendars.Where(c => c.OwnerUserId == userId).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var calendar in owned)
        {
            await DeleteCalendarAsync(calendar, cancellationToken).ConfigureAwait(false);
            summary.CalendarsDeleted++;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task DeleteCalendarAsync(Calendar calendar, CancellationToken cancellationToken)
    {
        var grants = await db.CalendarGrants.Where(g => g.CalendarId == calendar.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        await calendars.RemoveAsync(calendar, grants, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Grants naming the account on calendars that survive (shared with it): removed, audited on each calendar.</summary>
    private async Task RemoveGrantsAsync(Guid userId, PurgeSummary summary, CancellationToken cancellationToken)
    {
        var grants = await db.CalendarGrants
            .Where(g => g.PrincipalType == PrincipalType.User && g.PrincipalId == userId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (grants.Count == 0)
        {
            return;
        }

        var calendarIds = grants.Select(g => g.CalendarId).Distinct().ToList();
        var subjects = await CalendarAudit.BillingSubjectsAsync(db, calendarIds, cancellationToken).ConfigureAwait(false);
        await aclVersions.BumpCalendarsAsync(calendarIds, cancellationToken).ConfigureAwait(false);
        foreach (var grant in grants)
        {
            audit.Record(
                CalendarAuditActions.GrantRemovedWithUser,
                CalendarAuditActions.ResourceType,
                grant.CalendarId.ToString(),
                CalendarGrantService.State(grant),
                null,
                subjects.TryGetValue(grant.CalendarId, out var subject) ? subject : (Guid?)null);
        }

        db.CalendarGrants.RemoveRange(grants);
        summary.GrantsRemoved = grants.Count;
    }

    /// <summary>The <c>user.deleted</c> audit snapshot: counts only.</summary>
    private sealed class PurgeSummary
    {
        public int GroupsDeleted { get; set; }

        public int GroupsLeft { get; set; }

        public int GroupsHandedOver { get; set; }

        public int CalendarsDeleted { get; set; }

        public int GrantsRemoved { get; set; }
    }
}
