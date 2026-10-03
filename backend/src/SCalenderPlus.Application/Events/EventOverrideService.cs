using Microsoft.EntityFrameworkCore;
using NodaTime;
using SCalenderPlus.Application.Auditing;
using SCalenderPlus.Application.Calendars;
using SCalenderPlus.Application.Common;
using SCalenderPlus.Application.Entitlements;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Application.Users;
using SCalenderPlus.Core.Calendars;
using SCalenderPlus.Core.Entitlements;
using SCalenderPlus.Core.Events;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Application.Events;

/// <summary>A stored override with the display name of the user or the name of the group it names (null for <c>anonymous</c>/<c>everyone</c>).</summary>
public sealed record OverrideView(EventOverrideEntry Entry, string? PrincipalName);

/// <summary>An event's overrides as a floor holder sees them, ordered by principal (users, groups, anonymous, everyone).</summary>
public sealed record EventOverridesView(EventView Event, IReadOnlyList<OverrideView> Overrides);

/// <summary>
/// Event permission overrides (issue #46, permissions.md §4.4): read and replaced as a whole set by the people who
/// may change them — floor holders (calendar <c>manage</c>/<c>owner</c>, the creator with the creator floor), i.e.
/// event level <c>manage</c> (<see cref="EventAction.ChangeOverrides"/>). Others who see the event get
/// <c>403 insufficient_permission</c>, everyone else <c>404</c>. A replacement is validated by
/// <see cref="OverridePolicy.EvaluateChange"/> (plus user selection, see <see cref="ReplaceAsync"/>), counted
/// against the calendar owner's plan, maintains <c>events.has_overrides</c>, bumps the <c>acl_version</c> of the
/// calendar and of the principals whose entries changed, logs an <c>acl</c> sync row and is audited.
/// </summary>
public sealed class EventOverrideService(
    IAppDbContext db,
    CalendarAccessLoader calendars,
    EventQueryService queries,
    EventWriter writer,
    AclVersions aclVersions,
    IEntitlementService entitlements,
    IUserDirectory users,
    IAuditLog audit,
    IClock clock)
{
    /// <summary>The overrides of the event (event level <c>manage</c>).</summary>
    public async Task<EventOverridesView> GetAsync(Guid actorId, Guid eventId, CancellationToken cancellationToken = default)
    {
        var view = await queries.GetAsync(actorId, eventId, cancellationToken: cancellationToken).ConfigureAwait(false);
        RequireRights(view);
        var rows = await db.EventOverrides.AsNoTracking().Where(o => o.EventId == eventId).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new EventOverridesView(view, await ViewsAsync(rows, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Replaces the event's overrides by <paramref name="proposed"/> (atomic). Refusals: <c>404</c> (level
    /// <c>none</c>), <c>403 insufficient_permission</c> (no floor), <c>412/428</c> (<paramref name="precondition"/>),
    /// <c>409 calendar_frozen</c> (frozen calendar, unless the change only removes or lowers entries),
    /// <c>422 override_invalid</c> (level above <c>edit</c>, a principal twice, a group or user the actor may not
    /// select), <c>403 external_sharing_not_allowed</c> (sharing outside the calendar's audience without the right
    /// to), <c>402 plan_limit_reached</c>. Selectable users (added entries only): people who share a group with the
    /// actor or see the calendar (unknown ids answer the same); pending email shares come later.
    /// </summary>
    /// <param name="precondition">Checks If-Match against the current overrides (after authorization); throws to refuse.</param>
    public async Task<EventOverridesView> ReplaceAsync(
        Guid actorId,
        Guid eventId,
        IReadOnlyList<EventOverride> proposed,
        Action<EventOverridesView>? precondition = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        await db.InTransactionAsync(async ct =>
        {
            await db.LockAsync(eventId, ct).ConfigureAwait(false); // serializes replacements of one event's set
            var view = await queries.GetAsync(actorId, eventId, forUpdate: true, ct).ConfigureAwait(false);
            RequireRights(view);
            var rows = await db.EventOverrides.Where(o => o.EventId == eventId).ToListAsync(ct).ConfigureAwait(false);
            precondition?.Invoke(new EventOverridesView(view, await ViewsAsync(rows, ct).ConfigureAwait(false)));

            var ev = view.Event;
            var current = rows.Select(r => r.ToOverride()).ToList();
            var onlyRemovals = proposed.All(p => current.Any(c => c.Principal == p.Principal && p.Level <= c.Level));
            if (view.Calendar.FrozenAt is not null && !onlyRemovals)
            {
                throw CalendarErrors.Frozen(); // plans.md: frozen calendars allow override removal only
            }

            var actor = await calendars.PrincipalAsync(actorId, ct).ConfigureAwait(false);
            var named = current.Concat(proposed).Where(o => o.Principal.Type == PrincipalType.User).Select(o => o.Principal.Id!.Value).Distinct().ToList();
            var levels = await calendars.CalendarLevelsAsync(named, view.Acl, ct).ConfigureAwait(false);
            var decision = OverridePolicy.EvaluateChange(actor, view.Acl, ev.ToAcl(current), proposed, levels);
            var unselectable = await UnselectableUsersAsync(actor, current, proposed, levels, ct).ConfigureAwait(false);
            ThrowIfRefused(decision, unselectable);

            var now = clock.Now();
            var subject = await CalendarAudit.BillingSubjectAsync(db, view.Calendar, ct).ConfigureAwait(false);
            await db.LockAsync(subject, ct).ConfigureAwait(false); // plan-limit count and change under one lock per plan subject
            var active = await queries.ActiveEventsWithOverridesAsync(subject, now, ct).ConfigureAwait(false);
            await entitlements.EnsureCanChangeEventOverridesAsync(
                subject,
                new OverrideUsage(active, PlanLimits.IsActive(ev.OccursUntil, now), current.Count, proposed.Count, onlyRemovals),
                ct).ConfigureAwait(false);

            var changed = Apply(ev, rows, proposed, actorId, now);
            if (changed.Count == 0)
            {
                return true;
            }

            ev.HasOverrides = proposed.Count > 0;
            writer.AclChanged(ev);
            await aclVersions.BumpCalendarAsync(ev.CalendarId, ct).ConfigureAwait(false);
            await aclVersions.BumpPrincipalsAsync([.. changed.Where(p => !p.IsRestrictOnly)], ct).ConfigureAwait(false);
            audit.Record(EventAuditActions.OverridesChanged, EventAuditActions.ResourceType, ev.Id.ToString(), OverrideAudit.State(current), OverrideAudit.State(proposed), subject);
            await SaveAsync(ct).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);

        return await GetAsync(actorId, eventId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Only floor holders see and change overrides (<see cref="EventAction.ChangeOverrides"/>).</summary>
    private static void RequireRights(EventView view)
    {
        if (AccessPolicy.Check(view.Level, EventAction.ChangeOverrides) != AccessCheck.Allowed)
        {
            throw EventErrors.InsufficientLevel(AccessPolicy.RequiredLevel(EventAction.ChangeOverrides), view.Level);
        }
    }

    /// <summary>
    /// §4.4 user selection for added entries: users who see the calendar (<paramref name="levels"/> ≥
    /// <c>free_busy</c>) or share a group with the actor. Unknown ids fail the same way (no existence leak).
    /// </summary>
    private async Task<IReadOnlyList<EventOverride>> UnselectableUsersAsync(
        PrincipalContext actor,
        IReadOnlyList<EventOverride> current,
        IReadOnlyList<EventOverride> proposed,
        IReadOnlyDictionary<Guid, CalendarLevel> levels,
        CancellationToken cancellationToken)
    {
        var outsiders = proposed
            .Where(p => p.Principal.Type == PrincipalType.User && current.All(c => c.Principal != p.Principal))
            .Where(p => levels.GetValueOrDefault(p.Principal.Id!.Value) == CalendarLevel.None)
            .ToList();
        if (outsiders.Count == 0)
        {
            return [];
        }

        var principals = await calendars.PrincipalsAsync([.. outsiders.Select(p => p.Principal.Id!.Value)], cancellationToken).ConfigureAwait(false);
        return [.. outsiders.Where(p => !principals[p.Principal.Id!.Value].Groups.Keys.Any(actor.Groups.ContainsKey))];
    }

    private static void ThrowIfRefused(OverrideChangeDecision decision, IReadOnlyList<EventOverride> unselectable)
    {
        if (decision.Verdict is OverrideChangeVerdict.NotFound or OverrideChangeVerdict.Forbidden)
        {
            throw new InvalidOperationException("The actor's rights were checked before: the engine must agree."); // defensive
        }

        var invalid = decision.Violations
            .Where(v => v.Reason is not (OverrideViolationReason.ExternalSharing or OverrideViolationReason.RemovalExposesExternalShare))
            .Select(v => new OverrideProblem(v.Override, OverrideErrors.Reason(v.Reason)))
            .Concat(unselectable.Select(o => new OverrideProblem(o, OverrideErrors.UserNotSelectable)))
            .ToList();
        if (invalid.Count > 0)
        {
            throw OverrideErrors.Invalid(invalid);
        }

        if (decision.Verdict == OverrideChangeVerdict.ExternalSharingNotAllowed)
        {
            throw OverrideErrors.ExternalSharing([.. decision.Violations.Select(v => new OverrideProblem(v.Override, OverrideErrors.Reason(v.Reason)))]);
        }
    }

    /// <summary>
    /// Stages the difference between the stored <paramref name="rows"/> and <paramref name="proposed"/>: removed
    /// entries are deleted, changed ones get the new level (and the actor as author), new ones are added.
    /// Returns the principals whose entries changed.
    /// </summary>
    private List<Principal> Apply(Event ev, List<EventOverrideEntry> rows, IReadOnlyList<EventOverride> proposed, Guid actorId, Instant now)
    {
        var changed = new List<Principal>();
        foreach (var row in rows)
        {
            var next = proposed.FirstOrDefault(p => p.Principal == row.Principal);
            if (next is null)
            {
                db.EventOverrides.Remove(row);
                changed.Add(row.Principal);
            }
            else if (next.Level != row.Level)
            {
                row.Level = next.Level;
                row.CreatedBy = actorId;
                row.CreatedAt = now;
                changed.Add(row.Principal);
            }
        }

        foreach (var entry in proposed.Where(p => rows.All(r => r.Principal != p.Principal)))
        {
            var row = EventOverrideEntry.For(ev.Id, entry);
            row.CreatedBy = actorId;
            row.CreatedAt = now;
            db.EventOverrides.Add(row);
            changed.Add(entry.Principal);
        }

        return changed;
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw EventErrors.Changed();
        }
    }

    private async Task<IReadOnlyList<OverrideView>> ViewsAsync(IReadOnlyList<EventOverrideEntry> rows, CancellationToken cancellationToken)
    {
        var userIds = rows.Where(r => r.PrincipalType == PrincipalType.User).Select(r => r.PrincipalId!.Value).Distinct().ToList();
        var groupIds = rows.Where(r => r.PrincipalType == PrincipalType.Group).Select(r => r.PrincipalId!.Value).Distinct().ToList();
        var people = await users.GetAsync(userIds, cancellationToken).ConfigureAwait(false);
        var groups = groupIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Groups.AsNoTracking().Where(g => groupIds.Contains(g.Id))
                .ToDictionaryAsync(g => g.Id, g => g.Name, cancellationToken).ConfigureAwait(false);
        return [.. OverrideAudit.Ordered(rows).Select(r => new OverrideView(r, r.PrincipalType switch
        {
            PrincipalType.User => people.TryGetValue(r.PrincipalId!.Value, out var user) ? user.DisplayName : null,
            PrincipalType.Group => groups.GetValueOrDefault(r.PrincipalId!.Value),
            _ => null,
        }))];
    }
}

/// <summary>Audit snapshots and the stable order of override sets.</summary>
internal static class OverrideAudit
{
    /// <summary>Users, groups, anonymous, everyone; then by id and role (the order of responses and ETags).</summary>
    public static IEnumerable<EventOverrideEntry> Ordered(IEnumerable<EventOverrideEntry> rows) =>
        rows.OrderBy(r => r.PrincipalType).ThenBy(r => r.PrincipalId).ThenBy(r => r.MinRole);

    /// <summary><c>{ overrides: ["user:{id} → read", …] }</c> in a stable order.</summary>
    public static OverridesState State(IEnumerable<EventOverride> overrides) =>
        new([.. overrides
            .OrderBy(o => o.Principal.Type).ThenBy(o => o.Principal.Id).ThenBy(o => o.Principal.MinRole)
            .Select(o => $"{o.Principal} → {PermissionLevels.Format(o.Level)}")]);

    public sealed record OverridesState(IReadOnlyList<string> Overrides);
}
