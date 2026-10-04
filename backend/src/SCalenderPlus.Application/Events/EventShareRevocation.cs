using Microsoft.EntityFrameworkCore;
using SCalenderPlus.Application.Auditing;
using SCalenderPlus.Application.Calendars;
using SCalenderPlus.Application.Groups;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Core.Calendars;
using SCalenderPlus.Core.Events;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Application.Events;

/// <summary>
/// "Membership removal revokes event shares" (issue #49, permissions.md §4.6): when someone loses calendar level —
/// removed from a group, demoted, leaving, the group deleted, a grant removed or lowered, or the owning group's role
/// defaults lowered — the <c>user:</c>
/// overrides naming them on events of the calendars where their level dropped are deleted if they give more than
/// the calendar now gives them (individual shares; restrictions such as <c>user:X → none</c> stay), unless the
/// remover opted out (<see cref="MembershipChange.RevokeEventShares"/>, <c>?revokeEventShares=false</c>). An entry
/// whose removal would raise the person (another group's entry would then decide for them) stays too. When a group
/// is deleted, every <c>group:</c> override naming it goes. Each touched event gets <c>has_overrides</c> maintained,
/// an <c>acl</c> sync row and an audit record; calendars and named users get their <c>acl_version</c> bumped.
/// Runs inside the caller's transaction; the caller saves.
/// </summary>
public sealed class EventShareRevocation(
    IAppDbContext db,
    CalendarAccessLoader calendars,
    EventQueryService queries,
    EventWriter writer,
    AclVersions aclVersions,
    IAuditLog audit) : IGroupMembershipObserver
{
    /// <summary>Removals, demotions, leaving and group deletion (not joins or promotions).</summary>
    public async Task OnMembershipChangedAsync(MembershipChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (!change.RevokeEventShares || change.Kind == MembershipChangeKind.Joined || (change.NewRole is { } newRole && newRole >= change.OldRole))
        {
            return;
        }

        // The group's calendars: owned by it, or granting it a level. Stored state is the state before the change.
        var affected = await db.Calendars.AsNoTracking()
            .Where(c => c.OwnerGroupId == change.GroupId
                || db.CalendarGrants.Any(g => g.CalendarId == c.Id && g.PrincipalType == PrincipalType.Group && g.PrincipalId == change.GroupId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (affected.Count == 0)
        {
            return;
        }

        var acls = await calendars.AclsAsync(affected, cancellationToken).ConfigureAwait(false);
        var memberships = await db.GroupMembers.AsNoTracking()
            .Where(m => m.UserId == change.UserId)
            .ToDictionaryAsync(m => m.GroupId, m => m.Role, cancellationToken).ConfigureAwait(false);
        if (change.OldRole is { } oldRole)
        {
            memberships[change.GroupId] = oldRole;
        }

        var before = PrincipalContext.ForUser(change.UserId, new Dictionary<Guid, GroupRole>(memberships));
        if (change.NewRole is { } role)
        {
            memberships[change.GroupId] = role;
        }
        else
        {
            memberships.Remove(change.GroupId);
        }

        var after = PrincipalContext.ForUser(change.UserId, memberships);
        var losses = new Dictionary<Guid, Loss>();
        AddLoss(losses, before, after, acls.Values, acls.Values);
        await RevokeAsync(losses, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A grant of <paramref name="before"/> was removed or lowered (<paramref name="after"/>): revokes the shares of
    /// the users it named — the user, or the group's members — whose level on the calendar dropped.
    /// </summary>
    public async Task OnGrantChangedAsync(CalendarAcl before, CalendarAcl after, Principal grantee, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(grantee);
        var granteeId = grantee.Id!.Value;
        var userIds = grantee.Type == PrincipalType.User
            ? [granteeId]
            : await db.GroupMembers.AsNoTracking().Where(m => m.GroupId == granteeId).Select(m => m.UserId).ToListAsync(cancellationToken).ConfigureAwait(false);
        await RevokeForAsync(before, after, userIds, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The owning group's role defaults of a calendar were lowered (<paramref name="before"/> → <paramref name="after"/>):
    /// revokes the shares of the group's members whose level on the calendar dropped.
    /// </summary>
    public async Task OnRoleDefaultsChangedAsync(CalendarAcl before, CalendarAcl after, Guid ownerGroupId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var userIds = await db.GroupMembers.AsNoTracking().Where(m => m.GroupId == ownerGroupId).Select(m => m.UserId).ToListAsync(cancellationToken).ConfigureAwait(false);
        await RevokeForAsync(before, after, userIds, cancellationToken).ConfigureAwait(false);
    }

    private async Task RevokeForAsync(CalendarAcl before, CalendarAcl after, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        var principals = await calendars.PrincipalsAsync(userIds, cancellationToken).ConfigureAwait(false);
        var losses = new Dictionary<Guid, Loss>();
        foreach (var principal in principals.Values)
        {
            AddLoss(losses, principal, principal, [before], [after]);
        }

        await RevokeAsync(losses, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The group is being deleted: removes every override naming it (any minimum role).</summary>
    public async Task RemoveGroupOverridesAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        var rows = await db.EventOverrides
            .Where(o => o.PrincipalType == PrincipalType.Group && o.PrincipalId == groupId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (rows.Count > 0)
        {
            var events = await queries.ForLifecycleAsync([.. rows.Select(r => r.EventId).Distinct()], cancellationToken: cancellationToken).ConfigureAwait(false);
            await RemoveAsync(rows, events, EventAuditActions.OverridesRemovedWithGroup, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Records the calendars where <paramref name="before"/>'s level is higher than <paramref name="after"/>'s (same order of ACLs).</summary>
    private static void AddLoss(Dictionary<Guid, Loss> losses, PrincipalContext before, PrincipalContext after, IEnumerable<CalendarAcl> aclsBefore, IEnumerable<CalendarAcl> aclsAfter)
    {
        foreach (var (aclBefore, aclAfter) in aclsBefore.Zip(aclsAfter))
        {
            if (PermissionEngine.ResolveCalendarLevel(after, aclAfter) < PermissionEngine.ResolveCalendarLevel(before, aclBefore))
            {
                var userId = after.UserId!.Value;
                if (!losses.TryGetValue(userId, out var loss))
                {
                    losses[userId] = loss = new Loss(after, []);
                }

                loss.Calendars[aclAfter.CalendarId] = aclAfter;
            }
        }
    }

    private async Task RevokeAsync(Dictionary<Guid, Loss> losses, CancellationToken cancellationToken)
    {
        if (losses.Count == 0)
        {
            return;
        }

        var userIds = losses.Keys.ToList();
        var candidates = await db.EventOverrides
            .Where(o => o.PrincipalType == PrincipalType.User && userIds.Contains(o.PrincipalId!.Value) && o.Level > EventLevel.None)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            return;
        }

        // Only events of the calendars where someone lost level (their shares elsewhere are untouched).
        var lossCalendars = losses.Values.SelectMany(l => l.Calendars.Keys).ToHashSet();
        var events = await queries.ForLifecycleAsync([.. candidates.Select(r => r.EventId).Distinct()], lossCalendars, cancellationToken).ConfigureAwait(false);
        var byId = events.ToDictionary(e => e.Id);
        var eventIds = byId.Keys.ToList();
        var rowsByEvent = (await db.EventOverrides.Where(o => eventIds.Contains(o.EventId)).ToListAsync(cancellationToken).ConfigureAwait(false))
            .Where(Live)
            .ToLookup(o => o.EventId);

        var doomed = new List<EventOverrideEntry>();
        foreach (var row in candidates.Where(Live))
        {
            var loss = losses[row.PrincipalId!.Value];
            if (!byId.TryGetValue(row.EventId, out var ev) || !loss.Calendars.TryGetValue(ev.CalendarId, out var acl))
            {
                continue;
            }

            // A share: more than the calendar now gives them. Removed unless that would raise them (another tier decides).
            var ceiling = PermissionLevels.ImpliedEventLevel(PermissionEngine.ResolveCalendarLevel(loss.After, acl));
            var current = rowsByEvent[ev.Id].Where(r => !doomed.Contains(r)).ToList();
            var withEntry = PermissionEngine.ResolveLevel(loss.After, acl, ev.ToAcl([.. current.Select(r => r.ToOverride())]));
            var withoutEntry = PermissionEngine.ResolveLevel(loss.After, acl, ev.ToAcl([.. current.Where(r => r != row).Select(r => r.ToOverride())]));
            if (row.Level > ceiling && withoutEntry <= withEntry)
            {
                doomed.Add(row);
            }
        }

        await RemoveAsync(doomed, events, EventAuditActions.OverridesRevoked, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes <paramref name="doomed"/> and maintains each touched event (has_overrides, sync log, audit, acl versions).</summary>
    private async Task RemoveAsync(List<EventOverrideEntry> doomed, IReadOnlyList<Event> events, string action, CancellationToken cancellationToken)
    {
        if (doomed.Count == 0)
        {
            return;
        }

        var byId = events.ToDictionary(e => e.Id);
        var removed = doomed.ToHashSet();
        var eventIds = doomed.Select(r => r.EventId).Distinct().ToList();
        var rows = (await db.EventOverrides.Where(o => eventIds.Contains(o.EventId)).ToListAsync(cancellationToken).ConfigureAwait(false))
            .Where(Live)
            .ToLookup(o => o.EventId);
        var calendarIds = eventIds.Select(id => byId[id].CalendarId).Distinct().ToList();
        var subjects = await CalendarAudit.BillingSubjectsAsync(db, calendarIds, cancellationToken).ConfigureAwait(false);

        foreach (var eventId in eventIds)
        {
            var ev = byId[eventId];
            var before = rows[eventId].ToList();
            var after = before.Where(r => !removed.Contains(r)).ToList();
            foreach (var row in before.Except(after))
            {
                db.EventOverrides.Remove(row);
            }

            ev.HasOverrides = after.Count > 0;
            writer.AclChanged(ev);
            audit.Record(
                action,
                EventAuditActions.ResourceType,
                ev.Id.ToString(),
                OverrideAudit.State(before.Select(r => r.ToOverride())),
                OverrideAudit.State(after.Select(r => r.ToOverride())),
                subjects.GetValueOrDefault(ev.CalendarId));
        }

        await aclVersions.BumpCalendarsAsync(calendarIds, cancellationToken).ConfigureAwait(false);

        // Named users only: a deleted group's row goes with it (bumping it would fail the group's own concurrency check).
        await aclVersions.BumpPrincipalsAsync([.. doomed.Where(r => r.PrincipalType == PrincipalType.User).Select(r => r.Principal).Distinct()], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Not already removed in this unit of work (e.g. the group's own entries before its members' are revoked).</summary>
    private bool Live(EventOverrideEntry row) => db.EventOverrides.Entry(row).State != EntityState.Deleted;

    /// <summary>A user's principal after the change and the calendars (ACLs after the change) where their level dropped.</summary>
    private sealed record Loss(PrincipalContext After, Dictionary<Guid, CalendarAcl> Calendars);
}
