using Microsoft.EntityFrameworkCore;
using NodaTime;
using SCalenderPlus.Application.Auditing;
using SCalenderPlus.Application.Calendars;
using SCalenderPlus.Application.Common;
using SCalenderPlus.Application.Entitlements;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Core.Entitlements;
using SCalenderPlus.Core.Events;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Application.Events;

/// <summary>
/// Moving an event to another calendar (issue #48, permissions.md §4.6): the mover needs event level
/// <c>manage</c> (<see cref="AccessPolicy.CanMoveEvent"/>) and at least <c>contribute</c> on the target. The
/// overrides travel with the event and are re-validated as if the mover set them in the target
/// (<see cref="OverridePolicy.InvalidInTarget"/> → <c>409 override_invalid_in_target</c> listing them), they count
/// against the target owner's plan, the UID must be free in the target (<c>409 uid_conflict</c>). Both calendars'
/// <c>acl_version</c> and those of the principals the overrides name are bumped, the sync log records a delete in
/// the source and an upsert in the target, and the move is audited.
/// </summary>
public sealed class EventMoveService(
    IAppDbContext db,
    CalendarAccessLoader calendars,
    EventQueryService queries,
    EventWriter writer,
    IEventOverrideSource overrides,
    AclVersions aclVersions,
    IEntitlementService entitlements,
    IAuditLog audit,
    IClock clock)
{
    /// <summary>
    /// Moves the event. Refusals in order: <c>404</c> (no level on the event), <c>403 insufficient_permission</c>
    /// (event below <c>manage</c>), <c>412/428</c> (<paramref name="precondition"/>), <c>409 calendar_frozen</c>
    /// (source), <c>400 validation_failed</c> (already in the target), <c>404</c> (target unknown or invisible),
    /// <c>403 insufficient_permission</c> (target below <c>contribute</c>, calendar levels), <c>409 calendar_frozen</c>
    /// (target), <c>409 override_invalid_in_target</c>, <c>402 plan_limit_reached</c>, <c>409 uid_conflict</c>.
    /// </summary>
    /// <param name="precondition">Checks If-Match against the event's current state (after authorization); throws to refuse.</param>
    public async Task<EventView> MoveAsync(Guid actorId, Guid eventId, Guid targetCalendarId, Action<EventView>? precondition = null, CancellationToken cancellationToken = default)
    {
        var (ev, target) = await db.InTransactionAsync(async ct =>
        {
            await db.LockAsync(eventId, ct).ConfigureAwait(false); // serializes with override replacements of the event
            var view = await queries.GetAsync(actorId, eventId, forUpdate: true, ct).ConfigureAwait(false);
            if (AccessPolicy.Check(view.Level, EventAction.Move) != AccessCheck.Allowed)
            {
                throw EventErrors.InsufficientLevel(AccessPolicy.RequiredLevel(EventAction.Move), view.Level);
            }

            precondition?.Invoke(view);
            if (view.Calendar.FrozenAt is not null)
            {
                throw CalendarErrors.Frozen();
            }

            var ev = view.Event;
            if (targetCalendarId == ev.CalendarId)
            {
                throw Validation.Failed("targetCalendarId", "The event is already in this calendar.");
            }

            var target = await calendars.LoadAsync(actorId, targetCalendarId, cancellationToken: ct).ConfigureAwait(false);
            if (!AccessPolicy.CanMoveEvent(view.Level, target.Level))
            {
                throw CalendarErrors.InsufficientLevel(CalendarLevel.Contribute, target.Level);
            }

            if (target.Calendar.FrozenAt is not null)
            {
                throw CalendarErrors.Frozen(); // plans.md: no move-in
            }

            var current = ev.HasOverrides
                ? (await overrides.ForEventsAsync([ev.Id], ct).ConfigureAwait(false)).GetValueOrDefault(ev.Id) ?? []
                : [];
            await EnsureOverridesTravelAsync(target, ev, current, ct).ConfigureAwait(false);

            var now = clock.Now();
            var sourceSubject = await CalendarAudit.BillingSubjectAsync(db, view.Calendar, ct).ConfigureAwait(false);
            var targetSubject = await CalendarAudit.BillingSubjectAsync(db, target.Calendar, ct).ConfigureAwait(false);
            if (current.Count > 0 && targetSubject != sourceSubject)
            {
                // The overrides are new to the target owner's plan (a move within one plan changes no count).
                await db.LockAsync(targetSubject, ct).ConfigureAwait(false);
                var active = await queries.ActiveEventsWithOverridesAsync(targetSubject, now, ct).ConfigureAwait(false);
                await entitlements.EnsureCanChangeEventOverridesAsync(
                    targetSubject,
                    new OverrideUsage(active, PlanLimits.IsActive(ev.OccursUntil, now), 0, current.Count, OnlyRemovals: false),
                    ct).ConfigureAwait(false);
            }

            await db.LockAsync(target.Calendar.Id, ct).ConfigureAwait(false); // the UID check of event creation takes the same lock
            if (await queries.UidTakenAsync(target.Calendar.Id, ev.Uid, ct).ConfigureAwait(false))
            {
                throw EventErrors.UidConflict();
            }

            var source = ev.CalendarId;
            ev.CalendarId = target.Calendar.Id;
            ev.UpdatedAt = now;
            writer.Moved(ev, source);
            await aclVersions.BumpCalendarAsync(source, ct).ConfigureAwait(false);
            await aclVersions.BumpCalendarAsync(target.Calendar.Id, ct).ConfigureAwait(false);
            await aclVersions.BumpPrincipalsAsync([.. current.Select(o => o.Principal).Where(p => !p.IsRestrictOnly)], ct).ConfigureAwait(false);

            var before = new MoveState(source);
            var after = new MoveState(target.Calendar.Id);
            audit.Record(EventAuditActions.Moved, EventAuditActions.ResourceType, ev.Id.ToString(), before, after, targetSubject);
            if (sourceSubject != targetSubject)
            {
                audit.Record(EventAuditActions.Moved, EventAuditActions.ResourceType, ev.Id.ToString(), before, after, sourceSubject);
            }

            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw EventErrors.Changed();
            }

            return (ev, target);
        }, cancellationToken).ConfigureAwait(false);

        return await queries.ViewAsync(actorId, ev, target.Calendar, target.Acl, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>§4.6: the overrides must be ones the mover could set in the target, else <c>409 override_invalid_in_target</c>.</summary>
    private async Task EnsureOverridesTravelAsync(Calendars.CalendarAccess target, Event ev, IReadOnlyList<EventOverride> current, CancellationToken cancellationToken)
    {
        if (current.Count == 0)
        {
            return;
        }

        var named = current.Where(o => o.Principal.Type == PrincipalType.User).Select(o => o.Principal.Id!.Value).ToList();
        var levels = await calendars.CalendarLevelsAsync(named, target.Acl, cancellationToken).ConfigureAwait(false);
        var invalid = OverridePolicy.InvalidInTarget(target.Principal, target.Acl, ev.ToAcl(current), levels);
        if (invalid.Count == 0)
        {
            return;
        }

        var moved = new EventAcl(ev.Id, target.Calendar.Id, ev.CreatorUserId, current);
        var reason = OverridePolicy.RightsOf(target.Principal, target.Acl, moved) == OverrideRights.None
            ? OverrideErrors.NoOverrideRightsInTarget
            : OverrideErrors.ExternalSharingReason;
        throw OverrideErrors.InvalidInTarget([.. invalid.Select(o => new OverrideProblem(o, reason))]);
    }

    private sealed record MoveState(Guid CalendarId);
}
