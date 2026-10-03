using Microsoft.EntityFrameworkCore;
using SCalenderPlus.Application.Auditing;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Application.Calendars;

/// <summary>
/// Calendar rules of the group lifecycle (issue #43, docs/architecture/permissions.md §4.6, data-model.md §2),
/// called by the group use cases inside their transaction.
/// </summary>
public sealed class CalendarGroupLifecycle(IAppDbContext db, AclVersions aclVersions, IAuditLog audit)
{
    /// <summary>
    /// Before a group is deleted: refuses while the group owns calendars (<c>409 group_has_calendars</c>; the
    /// <c>calendars.owner_group_id</c> FK is <c>RESTRICT</c>, so the rule also holds in the database), then removes
    /// every grant naming the group, bumping the affected calendars' <c>acl_version</c> and auditing each removal
    /// on its calendar. The members' <c>users.acl_version</c> is bumped by the group use case.
    /// </summary>
    public async Task OnGroupDeletingAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        var owned = await db.Calendars.CountAsync(c => c.OwnerGroupId == groupId, cancellationToken).ConfigureAwait(false);
        if (owned > 0)
        {
            throw CalendarErrors.GroupHasCalendars(owned);
        }

        var grants = await db.CalendarGrants
            .Where(g => g.PrincipalType == PrincipalType.Group && g.PrincipalId == groupId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (grants.Count > 0)
        {
            var calendarIds = grants.Select(g => g.CalendarId).Distinct().ToList();
            var subjects = await db.Calendars.AsNoTracking()
                .Where(c => calendarIds.Contains(c.Id))
                .Select(c => new
                {
                    c.Id,
                    Subject = c.OwnerUserId ?? db.Groups.Where(g => g.Id == c.OwnerGroupId).Select(g => (Guid?)g.OwnerUserId).FirstOrDefault(),
                })
                .ToDictionaryAsync(c => c.Id, c => c.Subject, cancellationToken).ConfigureAwait(false);
            foreach (var calendarId in calendarIds)
            {
                await aclVersions.BumpCalendarAsync(calendarId, cancellationToken).ConfigureAwait(false);
            }

            foreach (var grant in grants)
            {
                audit.Record(
                    CalendarAuditActions.GrantRemovedWithGroup,
                    CalendarAuditActions.ResourceType,
                    grant.CalendarId.ToString(),
                    CalendarGrantService.State(grant),
                    null,
                    subjects.GetValueOrDefault(grant.CalendarId));
            }

            db.CalendarGrants.RemoveRange(grants);
        }

        // TODO(M2-D, #43): also delete the event overrides naming the group (event_overrides.principal_type = 1,
        // principal_id = groupId), reset events.has_overrides where none remain and bump the affected calendars'
        // acl_version — same transaction, audited per event.
    }
}
