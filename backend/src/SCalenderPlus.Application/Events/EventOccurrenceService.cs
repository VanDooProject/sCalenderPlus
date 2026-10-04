using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Text;
using SCalenderPlus.Application.Auditing;
using SCalenderPlus.Application.Calendars;
using SCalenderPlus.Application.Common;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Core.Calendars;
using SCalenderPlus.Core.Events;
using SCalenderPlus.Core.Permissions;
using SCalenderPlus.Core.Recurrence;

namespace SCalenderPlus.Application.Events;

/// <summary>Merge-patch of one occurrence: <c>null</c> members stay unchanged; only these fields can differ per occurrence.</summary>
/// <param name="Description">Empty removes it for this occurrence.</param>
/// <param name="Location">Empty removes it for this occurrence.</param>
public sealed record OccurrenceChanges(
    string? Title = null,
    string? Description = null,
    string? Location = null,
    EventStatus? Status = null,
    EventTransparency? Transparency = null,
    EventTimeInput? Start = null,
    EventTimeInput? End = null);

/// <summary>The changed occurrence as the actor sees it (<see cref="EventView.Occurrence"/> set) and how requested times were resolved.</summary>
public sealed record OccurrenceResult(EventView View, IReadOnlyList<TimeAdjustment> Adjustments);

/// <summary>
/// Occurrence edits of a series (issue #51, data-model.md §9, api.md §4): <b>this occurrence</b> upserts an
/// exception keyed by its recurrence id (changed fields, moved times) or cancels it; <b>this and following</b>
/// splits the series — the original ends before the occurrence (<c>UNTIL</c>, or a smaller <c>COUNT</c>), a new
/// series (new UID, <c>RELATED-TO</c> the original) starts there with the original's creator, a copy of its overrides
/// (no plan check: no new privacy decision, permissions.md §4.6), the later RDATEs/EXDATEs and exceptions, and the
/// requested changes; <b>all</b> is <see cref="EventService.UpdateAsync"/> on the series. Every edit needs
/// <c>edit</c> on the series (exceptions have no ACL of their own) and its <c>ETag</c>, logs an <c>upsert</c> of the
/// series (and of the new one, plus an <c>acl</c> row when overrides were copied) and is audited.
/// </summary>
public sealed class EventOccurrenceService(
    IAppDbContext db,
    EventQueryService queries,
    EventService events,
    EventWriter writer,
    AclVersions aclVersions,
    IAuditLog audit,
    IClock clock)
{
    /// <summary>
    /// Changes one occurrence. Refusals: <c>404</c> (no level on the series, not a series, no such occurrence —
    /// cancelled ones included), <c>403</c> (below <c>edit</c>), <c>428/412</c>, <c>409 calendar_frozen</c>,
    /// <c>400 validation_failed</c> (invalid values; times of another kind or zone than the series').
    /// </summary>
    public async Task<OccurrenceResult> UpdateAsync(Guid actorId, Guid eventId, string recurrenceId, OccurrenceChanges changes, Action<EventView>? precondition = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var view = await events.RequireAsync(actorId, eventId, EventAction.Edit, precondition, cancellationToken).ConfigureAwait(false);
        var series = view.Event;
        var occurrence = EventRecurrences.Find(series, recurrenceId);
        var now = clock.Now();
        var exception = occurrence.Exception ?? new EventExceptionEntry { Id = Guid.CreateVersion7(), RecurrenceId = occurrence.RecurrenceId, CreatedAt = now };
        var before = OccurrenceAudit.State(occurrence.Exception);

        // Values equal to the series' inherit (null), so later changes of the series reach the occurrence.
        if (changes.Title is not null)
        {
            var title = EventService.ValidTitle(changes.Title);
            exception.Title = title == series.Title ? null : title;
        }

        exception.Description = Text(changes.Description, exception.Description, series.Description, Event.DescriptionMaxLength, "description");
        exception.Location = Text(changes.Location, exception.Location, series.Location, Event.LocationMaxLength, "location");
        if (changes.Status is { } status)
        {
            exception.Status = status == series.Status ? null : status;
        }

        if (changes.Transparency is { } transparency)
        {
            exception.Transparency = transparency == series.Transparency ? null : transparency;
        }

        IReadOnlyList<TimeAdjustment> adjustments = [];
        if (changes.Start is not null || changes.End is not null)
        {
            var times = OccurrenceTimes(series, occurrence, changes.Start, changes.End);
            adjustments = times.Adjustments;
            var original = occurrence.Original.Times;
            var unmoved = times.StartUtc == original.StartUtc && times.EndUtc == original.EndUtc && times.StartLocal == original.StartLocal && times.StartDate == original.StartDate && times.EndDate == original.EndDate;
            exception.Move(unmoved ? null : times);
        }

        var after = OccurrenceAudit.Of(exception);
        if (after == before || (occurrence.Exception is null && exception.IsEmpty))
        {
            return new OccurrenceResult(Occurrence(view, occurrence.RecurrenceId), adjustments);
        }

        exception.UpdatedAt = now;
        if (occurrence.Exception is null)
        {
            writer.AddException(series, exception);
        }
        else if (exception.IsEmpty)
        {
            writer.RemoveException(series, exception); // back to the series' values: no exception needed
        }

        if (before?.Times != after.Times || before?.Status != after.Status)
        {
            series.Sequence++;
        }

        await SaveSeriesAsync(view, EventAuditActions.OccurrenceUpdated, before, after, now, cancellationToken).ConfigureAwait(false);
        var changed = await queries.ViewAsync(actorId, series, view.Calendar, view.Acl, cancellationToken).ConfigureAwait(false);
        return new OccurrenceResult(Occurrence(changed, occurrence.RecurrenceId), adjustments);
    }

    /// <summary>Cancels one occurrence (an exception with <c>cancelled</c>; exported as EXDATE). Refusals as <see cref="UpdateAsync"/>.</summary>
    public async Task CancelAsync(Guid actorId, Guid eventId, string recurrenceId, Action<EventView>? precondition = null, CancellationToken cancellationToken = default)
    {
        var view = await events.RequireAsync(actorId, eventId, EventAction.Edit, precondition, cancellationToken).ConfigureAwait(false);
        var series = view.Event;
        var occurrence = EventRecurrences.Find(series, recurrenceId);
        var now = clock.Now();
        var before = OccurrenceAudit.State(occurrence.Exception);
        var exception = occurrence.Exception ?? new EventExceptionEntry { Id = Guid.CreateVersion7(), RecurrenceId = occurrence.RecurrenceId, CreatedAt = now };
        exception.Cancelled = true;
        exception.Title = exception.Description = exception.Location = null;
        exception.Status = null;
        exception.Transparency = null;
        exception.Move(null);
        exception.UpdatedAt = now;
        if (occurrence.Exception is null)
        {
            writer.AddException(series, exception);
        }

        series.Sequence++;
        await SaveSeriesAsync(view, EventAuditActions.OccurrenceCancelled, before, OccurrenceAudit.Of(exception), now, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Splits the series at the occurrence <paramref name="recurrenceId"/> ("this and following") and applies
    /// <paramref name="changes"/> to the new series, which is returned. Refusals as <see cref="UpdateAsync"/>, plus
    /// <c>400 validation_failed</c> at the first occurrence (that is an edit of the whole series) and the
    /// validation of <see cref="EventService.UpdateAsync"/> for the changes.
    /// </summary>
    public async Task<EventResult> SplitAsync(Guid actorId, Guid eventId, string recurrenceId, EventChanges changes, Action<EventView>? precondition = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var (created, calendar, acl, outcome) = await db.InTransactionAsync(async ct =>
        {
            await db.LockAsync(eventId, ct).ConfigureAwait(false); // serializes with override replacements (they are copied)
            var view = await events.RequireAsync(actorId, eventId, EventAction.Edit, precondition, ct).ConfigureAwait(false);
            var series = view.Event;
            var occurrence = EventRecurrences.Find(series, recurrenceId, includeCancelled: true);
            var set = series.Recurrence()!;
            var key = occurrence.RecurrenceId;
            if (key <= set.FirstRecurrenceId)
            {
                throw Validation.Failed("recurrenceId", "This is the first occurrence: change the whole series with PATCH /events/{id} instead.");
            }

            var before = new SplitState(RecurrenceValues.Audit(series), null);
            var now = clock.Now();
            var rule = set.Rule;
            RecurrenceRule first, following;
            if (rule.Count is { } count)
            {
                var earlier = set.RuleCountBefore(key);
                (first, following) = (rule with { Count = earlier }, rule with { Count = Math.Max(1, count - earlier) });
            }
            else
            {
                first = series.AllDay
                    ? rule with { UntilDate = key.Date.PlusDays(-1), UntilUtc = null }
                    : rule with { UntilUtc = occurrence.Original.Times.StartUtc - Duration.FromSeconds(1), UntilDate = null };
                following = rule;
            }

            var created = new Event
            {
                Id = Guid.CreateVersion7(),
                CalendarId = series.CalendarId,
                CreatorUserId = series.CreatorUserId, // permissions.md §4.6: an edit user who splits gains no creator floor
                Title = series.Title,
                Description = series.Description,
                Location = series.Location,
                Url = series.Url,
                Status = series.Status,
                Transparency = series.Transparency,
                Color = series.Color,
                Categories = [.. series.Categories],
                Rrule = following.ToString(),
                RDates = [.. series.RDates.Where(r => r >= key)],
                ExDates = [.. series.ExDates.Where(r => r >= key)],
                CreatedAt = now,
                UpdatedAt = now,
            };
            created.Uid = Event.NativeUid(created.Id);
            created.RelatedTo = series.Uid;
            created.SetTimes(occurrence.Original.Times);
            writer.Add(created);
            foreach (var exception in series.Exceptions.Where(x => x.RecurrenceId >= key).ToList())
            {
                EventWriter.MoveException(exception, series, created);
            }

            series.Rrule = first.ToString();
            series.RDates = [.. series.RDates.Where(r => r < key)];
            series.ExDates = [.. series.ExDates.Where(r => r < key)];
            series.RefreshSeriesBounds();
            series.Sequence++;
            series.UpdatedAt = now;
            writer.Changed(series);

            var outcome = events.Apply(created, changes, view.Calendar.DefaultTimeZone);
            await CopyOverridesAsync(series, created, ct).ConfigureAwait(false);

            var subject = await CalendarAudit.BillingSubjectAsync(db, view.Calendar, ct).ConfigureAwait(false);
            audit.Record(EventAuditActions.Split, EventAuditActions.ResourceType, series.Id.ToString(), before, new SplitState(RecurrenceValues.Audit(series), created.Id), subject);
            audit.Record(EventAuditActions.Created, EventAuditActions.ResourceType, created.Id.ToString(), null, new CreatedBySplit(EventAudit.State(created), series.Id, EventRecurrences.Format(occurrence.Original)), subject);
            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw EventErrors.Changed();
            }

            return (created, view.Calendar, view.Acl, outcome);
        }, cancellationToken).ConfigureAwait(false);

        var result = await queries.ViewAsync(actorId, created, calendar, acl, cancellationToken).ConfigureAwait(false);
        return new EventResult(result, outcome.Adjustments, outcome.DroppedExceptions);
    }

    /// <summary>
    /// Copies the overrides of <paramref name="series"/> to <paramref name="created"/> as they are (authors and dates
    /// kept; no plan check, permissions.md §4.6) and records the permission change: <c>acl</c> sync row, the
    /// calendar's and the named principals' <c>acl_version</c>.
    /// </summary>
    private async Task CopyOverridesAsync(Event series, Event created, CancellationToken cancellationToken)
    {
        var rows = await db.EventOverrides.AsNoTracking().Where(o => o.EventId == series.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        created.HasOverrides = rows.Count > 0;
        if (rows.Count == 0)
        {
            return;
        }

        foreach (var row in rows)
        {
            var copy = EventOverrideEntry.For(created.Id, row.ToOverride());
            copy.CreatedBy = row.CreatedBy;
            copy.CreatedAt = row.CreatedAt;
            db.EventOverrides.Add(copy);
        }

        writer.AclChanged(created);
        await aclVersions.BumpCalendarAsync(created.CalendarId, cancellationToken).ConfigureAwait(false);
        await aclVersions.BumpPrincipalsAsync([.. rows.Select(r => r.Principal).Where(p => !p.IsRestrictOnly)], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Records an occurrence change on the series: bounds, <c>updated_at</c>, sync log, audit; saves (concurrent change → 412).</summary>
    private async Task SaveSeriesAsync(EventView view, string action, OccurrenceAudit.ExceptionState? before, OccurrenceAudit.ExceptionState after, Instant now, CancellationToken cancellationToken)
    {
        var series = view.Event;
        series.RefreshSeriesBounds();
        series.UpdatedAt = now;
        writer.Changed(series);
        audit.Record(action, EventAuditActions.ResourceType, series.Id.ToString(), before, after, await CalendarAudit.BillingSubjectAsync(db, view.Calendar, cancellationToken).ConfigureAwait(false));
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw EventErrors.Changed();
        }
    }

    /// <summary>
    /// The occurrence's new times: the requested start/end (missing ones keep the occurrence's wall clock), in the
    /// series' kind and zone; DST adjustments as for events.
    /// </summary>
    private static EventTimes OccurrenceTimes(Event series, EventOccurrence occurrence, EventTimeInput? start, EventTimeInput? end)
    {
        if ((start?.TimeZone ?? series.TimeZone) != series.TimeZone || (end?.TimeZone is { } endZone && endZone != series.TimeZone))
        {
            throw Validation.Failed("start.timeZone", "An occurrence keeps the series' time zone: leave out timeZone.");
        }

        var current = occurrence.Times;
        var times = EventService.ParseTimes(start ?? EventService.StartInput(current), end ?? EventService.EndInput(current, true), series.TimeZone ?? "UTC");
        return times.AllDay == series.AllDay
            ? times
            : throw Validation.Failed("start", series.AllDay ? "This is an all-day series: give dates." : "This is a timed series: give dateTime values.");
    }

    /// <summary>A text override: <c>null</c> keeps the current override, empty removes the value for the occurrence, the series' value inherits.</summary>
    private static string? Text(string? requested, string? current, string? series, int maxLength, string field)
    {
        if (requested is null)
        {
            return current;
        }

        var value = EventService.Optional(requested, maxLength, field);
        return value == series ? null : value ?? string.Empty;
    }

    private static EventView Occurrence(EventView view, LocalDateTime recurrenceId) =>
        view with { Occurrence = view.Event.FindOccurrence(recurrenceId) };

    private sealed record SplitState(string? Recurrence, Guid? NewEventId);

    private sealed record CreatedBySplit(EventAudit.EventState Event, Guid SplitFrom, string RecurrenceId);
}

/// <summary>Audit snapshots of exceptions.</summary>
internal static class OccurrenceAudit
{
    public static ExceptionState? State(EventExceptionEntry? exception) => exception is null ? null : Of(exception);

    public static ExceptionState Of(EventExceptionEntry exception) =>
            new ExceptionState(
                LocalDateTimePattern.ExtendedIso.Format(exception.RecurrenceId),
                exception.Cancelled,
                exception.Title,
                exception.Description,
                exception.Location,
                exception.Status?.ToString().ToLowerInvariant(),
                exception.Transparency?.ToString().ToLowerInvariant(),
                exception.MovedTimes(null) is { } times ? EventAudit.Times(times) : null);

    public sealed record ExceptionState(
        string RecurrenceId,
        bool Cancelled,
        string? Title,
        string? Description,
        string? Location,
        string? Status,
        string? Transparency,
        EventAudit.TimesState? Times);
}
