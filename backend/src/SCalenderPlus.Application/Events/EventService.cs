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

namespace SCalenderPlus.Application.Events;

/// <summary>
/// A time value of the API (data-model.md §10): timed <c>{ dateTime, timeZone }</c> (wall clock, ISO
/// <c>yyyy-MM-ddTHH:mm[:ss]</c>, IANA zone) or all-day <c>{ date }</c> (<c>yyyy-MM-dd</c>).
/// </summary>
public sealed record EventTimeInput(string? DateTime = null, string? TimeZone = null, string? Date = null);

/// <summary>The details of an event (everything but times and calendar); <c>null</c> = default on create, unchanged on update.</summary>
/// <param name="Description">Markdown subset, ≤ 20,000 characters; empty removes it on update.</param>
/// <param name="Color"><c>#rrggbb</c>; empty removes it on update (the calendar's color applies).</param>
/// <param name="Categories">≤ 20 texts of ≤ 50 characters; an empty list removes them.</param>
public sealed record EventDetails(
    string? Title = null,
    string? Description = null,
    string? Location = null,
    string? Url = null,
    EventStatus? Status = null,
    EventTransparency? Transparency = null,
    string? Color = null,
    IReadOnlyList<string>? Categories = null);

/// <summary>A new event in <see cref="CalendarId"/>: single, or a series with <paramref name="Recurrence"/>.</summary>
/// <param name="Uid">iCalendar UID to keep (imports, CalDAV); default <c>{id}@scalenderplus</c>.</param>
public sealed record NewEvent(Guid CalendarId, EventTimeInput Start, EventTimeInput End, EventDetails Details, string? Uid = null, EventRecurrenceInput? Recurrence = null);

/// <summary>
/// Merge-patch of an event: <c>null</c> members stay unchanged (see <see cref="EventDetails"/> for removals).
/// <paramref name="RecurrenceGiven"/> with a null <paramref name="Recurrence"/> turns a series into a single event.
/// </summary>
public sealed record EventChanges(EventDetails Details, EventTimeInput? Start = null, EventTimeInput? End = null, bool RecurrenceGiven = false, EventRecurrenceInput? Recurrence = null);

/// <summary>
/// The outcome of a create/update: the event as the actor sees it, how requested times were resolved (DST) and
/// the exceptions of a series that no longer matched an occurrence and were dropped (api recurrence ids).
/// </summary>
public sealed record EventResult(EventView View, IReadOnlyList<TimeAdjustment> Adjustments, IReadOnlyList<string>? DroppedExceptions = null);

/// <summary>What applying <see cref="EventChanges"/> did besides changing the row.</summary>
internal sealed record ChangeOutcome(IReadOnlyList<TimeAdjustment> Adjustments, IReadOnlyList<string> DroppedExceptions);

/// <summary>
/// Events CRUD (issues #44, #50, api.md §4): creating needs <c>contribute</c> on the calendar
/// (<see cref="CalendarAction.CreateEvent"/>), reading ≥ <c>free_busy</c>, changing and deleting ≥ <c>edit</c> on
/// the event — levels from the permission engine through <see cref="EventQueryService"/> (none → 404, too low →
/// 403). Frozen calendars refuse changes (<c>409 calendar_frozen</c>). Every mutation is audited and appended to the
/// sync log (<see cref="EventWriter"/>); deletes are soft. A <c>recurrence</c> makes the event a series master
/// (data-model.md §9); changing a series here is "all occurrences": its exceptions are re-keyed by the shift of the
/// first occurrence and dropped (reported) when they no longer match an occurrence.
/// </summary>
public sealed class EventService(
    IAppDbContext db,
    CalendarAccessLoader calendars,
    EventQueryService queries,
    EventWriter writer,
    IAuditLog audit,
    IClock clock)
{
    private static readonly LocalDateTimePattern[] _dateTimePatterns =
    [
        LocalDateTimePattern.ExtendedIso,
        LocalDateTimePattern.CreateWithInvariantCulture("uuuu'-'MM'-'dd'T'HH':'mm"),
    ];

    public async Task<EventResult> CreateAsync(Guid actorId, NewEvent request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var calendar = await calendars.RequireAsync(actorId, request.CalendarId, CalendarAction.CreateEvent, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (calendar.Calendar.FrozenAt is not null)
        {
            throw CalendarErrors.Frozen();
        }

        var times = ParseTimes(request.Start, request.End, calendar.Calendar.DefaultTimeZone);
        var now = clock.Now();
        var ev = new Event
        {
            Id = Guid.CreateVersion7(),
            CalendarId = calendar.Calendar.Id,
            CreatorUserId = actorId,
            Title = ValidTitle(request.Details.Title),
            Status = request.Details.Status ?? EventStatus.Confirmed,
            Transparency = request.Details.Transparency ?? EventTransparency.Opaque,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ev.Uid = request.Uid is null ? Event.NativeUid(ev.Id) : ValidUid(request.Uid);
        ApplyOptionalDetails(ev, request.Details);
        ev.SetTimes(times);
        if (request.Recurrence is not null)
        {
            EventRecurrences.Apply(ev, request.Recurrence);
            ev.RefreshSeriesBounds();
        }

        await db.InTransactionAsync(async ct =>
        {
            if (request.Uid is not null)
            {
                // Serializes UID checks per calendar until the commit (the partial unique index is the backstop).
                await db.LockAsync(ev.CalendarId, ct).ConfigureAwait(false);
                if (await queries.UidTakenAsync(ev.CalendarId, ev.Uid, ct).ConfigureAwait(false))
                {
                    throw EventErrors.UidConflict();
                }
            }

            writer.Add(ev);
            audit.Record(EventAuditActions.Created, EventAuditActions.ResourceType, ev.Id.ToString(), null, EventAudit.State(ev), await SubjectAsync(calendar.Calendar, ct).ConfigureAwait(false));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);

        var view = await queries.ViewAsync(actorId, ev, calendar.Calendar, calendar.Acl, cancellationToken).ConfigureAwait(false);
        return new EventResult(view, times.Adjustments);
    }

    /// <exception cref="Errors.AppException"><c>not_found</c> unless the actor sees the event (≥ <c>free_busy</c>; transparent events need ≥ <c>read</c>).</exception>
    public Task<EventView> GetAsync(Guid actorId, Guid eventId, CancellationToken cancellationToken = default) =>
        queries.GetAsync(actorId, eventId, cancellationToken: cancellationToken);

    /// <summary>The events of a window as the actor sees them (issue #45).</summary>
    public Task<EventWindow> WindowAsync(Guid actorId, EventWindowQuery query, CancellationToken cancellationToken = default) =>
        queries.WindowAsync(actorId, query, cancellationToken);

    /// <param name="precondition">Checks If-Match against the current state (after authorization); throws to refuse.</param>
    public async Task<EventResult> UpdateAsync(Guid actorId, Guid eventId, EventChanges changes, Action<EventView>? precondition = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var view = await RequireAsync(actorId, eventId, EventAction.Edit, precondition, cancellationToken).ConfigureAwait(false);
        var ev = view.Event;
        var before = EventAudit.State(ev);
        var outcome = Apply(ev, changes, view.Calendar.DefaultTimeZone);
        var after = EventAudit.State(ev);
        if (after == before)
        {
            return new EventResult(view, outcome.Adjustments);
        }

        if (before.Times != after.Times || before.Status != after.Status || before.Recurrence != after.Recurrence)
        {
            ev.Sequence++; // RFC 5545: significant revision
        }

        ev.UpdatedAt = clock.Now();
        writer.Changed(ev);
        audit.Record(EventAuditActions.Updated, EventAuditActions.ResourceType, ev.Id.ToString(), before, after, await SubjectAsync(view.Calendar, cancellationToken).ConfigureAwait(false));
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        return new EventResult(
            await queries.ViewAsync(actorId, ev, view.Calendar, view.Acl, cancellationToken).ConfigureAwait(false),
            outcome.Adjustments,
            outcome.DroppedExceptions);
    }

    /// <summary>Soft-deletes the event (≥ <c>edit</c>): it disappears for everyone, the sync log records a delete.</summary>
    public async Task DeleteAsync(Guid actorId, Guid eventId, Action<EventView>? precondition = null, CancellationToken cancellationToken = default)
    {
        var view = await RequireAsync(actorId, eventId, EventAction.Delete, precondition, cancellationToken).ConfigureAwait(false);
        var ev = view.Event;
        ev.DeletedAt = ev.UpdatedAt = clock.Now();
        writer.Changed(ev);
        audit.Record(EventAuditActions.Deleted, EventAuditActions.ResourceType, ev.Id.ToString(), EventAudit.State(ev), null, await SubjectAsync(view.Calendar, cancellationToken).ConfigureAwait(false));
        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Loads the event for update and checks <paramref name="action"/> (404/403), the precondition (428/412) and the freeze (409).</summary>
    internal async Task<EventView> RequireAsync(Guid actorId, Guid eventId, EventAction action, Action<EventView>? precondition, CancellationToken cancellationToken)
    {
        var view = await queries.GetAsync(actorId, eventId, forUpdate: true, cancellationToken).ConfigureAwait(false);
        if (AccessPolicy.Check(view.Level, action) != AccessCheck.Allowed)
        {
            throw EventErrors.InsufficientLevel(AccessPolicy.RequiredLevel(action), view.Level);
        }

        precondition?.Invoke(view);
        return view.Calendar.FrozenAt is null ? view : throw CalendarErrors.Frozen();
    }

    /// <summary>
    /// Applies <paramref name="changes"/> to the (tracked) event: times, details and recurrence. For a series
    /// whose first occurrence moves (in wall clock), RDATEs/EXDATEs (unless the recurrence is given anew) and the
    /// exceptions' keys shift by the same amount (data-model.md §9 "all"); exceptions that no longer match an
    /// occurrence — or all of them when the series switches between timed and all-day or stops recurring — are
    /// dropped. Moved exceptions keep their times (re-resolved in a new zone). Refreshes the series bounds.
    /// </summary>
    internal ChangeOutcome Apply(Event ev, EventChanges changes, string defaultZone)
    {
        var oldSet = ev.Recurrence();
        var (oldAllDay, oldZone) = (ev.AllDay, ev.TimeZone);
        IReadOnlyList<TimeAdjustment> adjustments = [];
        if (changes.Start is not null || changes.End is not null)
        {
            var current = ev.Times;
            var times = ParseTimes(changes.Start ?? StartInput(current), changes.End ?? EndInput(current, changes.Start is null), defaultZone);
            adjustments = times.Adjustments;
            ev.SetTimes(times); // unchanged values leave the row unchanged
        }

        var details = changes.Details;
        ev.Title = details.Title is null ? ev.Title : ValidTitle(details.Title);
        ev.Status = details.Status ?? ev.Status;
        ev.Transparency = details.Transparency ?? ev.Transparency;
        ApplyOptionalDetails(ev, details);

        var kindChanged = oldAllDay != ev.AllDay;
        var newFirst = ev.AllDay ? ev.StartDate!.Value.AtMidnight() : ev.StartLocal!.Value;
        var shift = oldSet is null ? Period.Zero : Period.Between(oldSet.FirstRecurrenceId, newFirst, PeriodUnits.Days | PeriodUnits.Hours | PeriodUnits.Minutes | PeriodUnits.Seconds);
        if (changes.RecurrenceGiven)
        {
            if (changes.Recurrence is null)
            {
                ev.Rrule = null;
                ev.RDates = [];
                ev.ExDates = [];
            }
            else
            {
                EventRecurrences.Apply(ev, changes.Recurrence);
            }
        }
        else if (oldSet is not null)
        {
            LocalDateTime Shift(LocalDateTime value) => kindChanged ? value.Date.PlusDays(shift.Days).At(newFirst.TimeOfDay) : value.Plus(shift);
            ev.RDates = [.. ev.RDates.Select(Shift)];
            ev.ExDates = [.. ev.ExDates.Select(Shift)];
            RecurrenceValues.Rebind(ev); // UNTIL follows a change of kind or zone (and must stay after the start)
        }

        var dropped = new List<string>();
        if (oldSet is not null)
        {
            var newSet = ev.Recurrence();
            foreach (var exception in ev.Exceptions.ToList())
            {
                var key = exception.RecurrenceId.Plus(shift);
                if (newSet is null || kindChanged || newSet.Find(key) is null)
                {
                    dropped.Add(EventRecurrences.Format(oldSet.At(exception.RecurrenceId)));
                    writer.RemoveException(ev, exception);
                    continue;
                }

                exception.RecurrenceId = key;
                if (exception.IsMoved && !ev.AllDay && ev.TimeZone != oldZone)
                {
                    exception.Move(EventTimes.Timed(exception.StartLocal!.Value, exception.EndLocal!.Value, DateTimeZoneProviders.Tzdb[ev.TimeZone!]));
                }
            }
        }

        ev.RefreshSeriesBounds();
        return new ChangeOutcome(adjustments, dropped);
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

    private Task<Guid> SubjectAsync(Calendar calendar, CancellationToken cancellationToken) =>
        CalendarAudit.BillingSubjectAsync(db, calendar, cancellationToken);

    /// <summary>
    /// Parses and resolves start/end (data-model.md §10): both all-day (<c>date</c>) or both timed (<c>dateTime</c>,
    /// one zone: <c>start.timeZone</c>, default <paramref name="defaultZone"/>; <c>end.timeZone</c> may be left out).
    /// </summary>
    internal static EventTimes ParseTimes(EventTimeInput start, EventTimeInput end, string defaultZone)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(end);
        if (start.Date is not null)
        {
            if (start.DateTime is not null || start.TimeZone is not null)
            {
                throw Validation.Failed("start", "Give either date (all-day) or dateTime with timeZone, not both.");
            }

            if (end.Date is null || end.DateTime is not null || end.TimeZone is not null)
            {
                throw Validation.Failed("end", "An all-day event ends on a date (exclusive): give end.date.");
            }

            var startDate = ParseDate(start.Date, "start.date");
            var endDate = ParseDate(end.Date, "end.date");
            return EventTimes.AllDayEvent(startDate, endDate)
                ?? throw Validation.Failed("end.date", "The end date is exclusive and must be after the start date.");
        }

        if (start.DateTime is null)
        {
            throw Validation.Failed("start", "Give dateTime (with timeZone) for a timed event, or date for an all-day event.");
        }

        if (end.DateTime is null || end.Date is not null)
        {
            throw Validation.Failed("end", "A timed event ends at a dateTime: give end.dateTime.");
        }

        var zoneId = start.TimeZone ?? defaultZone;
        var zone = (zoneId.Length <= Event.TimeZoneMaxLength ? DateTimeZoneProviders.Tzdb.GetZoneOrNull(zoneId) : null)
            ?? throw EventErrors.TimeZoneInvalid("start.timeZone", zoneId);
        if (end.TimeZone is not null && !string.Equals(end.TimeZone, zone.Id, StringComparison.Ordinal))
        {
            throw Validation.Failed("end.timeZone", "Start and end share one time zone: leave out end.timeZone or repeat start.timeZone.");
        }

        var startLocal = ParseDateTime(start.DateTime, "start.dateTime");
        var endLocal = ParseDateTime(end.DateTime, "end.dateTime");
        return EventTimes.Timed(startLocal, endLocal, zone)
            ?? throw Validation.Failed("end.dateTime", "The end must not be before the start.");
    }

    internal static EventTimeInput StartInput(EventTimes current) =>
        current.AllDay
            ? new EventTimeInput(Date: LocalDatePattern.Iso.Format(current.StartDate!.Value))
            : new EventTimeInput(LocalDateTimePattern.ExtendedIso.Format(current.StartLocal!.Value), current.TimeZone);

    /// <summary>The stored end as input; without its zone when the start changes (the end follows the start's zone).</summary>
    internal static EventTimeInput EndInput(EventTimes current, bool keepZone) =>
        current.AllDay
            ? new EventTimeInput(Date: LocalDatePattern.Iso.Format(current.EndDate!.Value))
            : new EventTimeInput(LocalDateTimePattern.ExtendedIso.Format(current.EndLocal!.Value), keepZone ? current.TimeZone : null);

    private static LocalDate ParseDate(string value, string field)
    {
        var result = LocalDatePattern.Iso.Parse(value);
        return result.Success && EventTimes.IsSupported(result.Value)
            ? result.Value
            : throw Validation.Failed(field, "Use a date like 2026-11-02 (years 1–9998).");
    }

    private static LocalDateTime ParseDateTime(string value, string field)
    {
        foreach (var pattern in _dateTimePatterns)
        {
            var result = pattern.Parse(value);
            if (result.Success && EventTimes.IsSupported(result.Value.Date))
            {
                return result.Value;
            }
        }

        throw Validation.Failed(field, "Use a local date and time like 2026-11-02T18:00:00 (no offset; the zone goes into timeZone).");
    }

    internal static void ApplyOptionalDetails(Event ev, EventDetails details)
    {
        ev.Description = details.Description is null ? ev.Description : Optional(details.Description, Event.DescriptionMaxLength, "description");
        ev.Location = details.Location is null ? ev.Location : Optional(details.Location, Event.LocationMaxLength, "location");
        ev.Url = details.Url is null ? ev.Url : ValidUrl(details.Url);
        ev.Color = details.Color is null ? ev.Color : ValidColor(details.Color);
        ev.Categories = details.Categories is null ? ev.Categories : ValidCategories(details.Categories);
    }

    internal static string ValidTitle(string? title)
    {
        var trimmed = title?.Trim() ?? string.Empty;
        return trimmed.Length is >= 1 and <= Event.TitleMaxLength
            ? trimmed
            : throw Validation.Failed("title", $"The title must have 1 to {Event.TitleMaxLength} characters.");
    }

    internal static string? Optional(string value, int maxLength, string field)
    {
        var trimmed = value.Trim();
        return trimmed.Length > maxLength
            ? throw Validation.Failed(field, $"At most {maxLength} characters.")
            : trimmed.Length == 0 ? null : trimmed;
    }

    private static string? ValidUrl(string url)
    {
        var trimmed = url.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        return trimmed.Length <= Event.UrlMaxLength
            && Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? trimmed
            : throw Validation.Failed("url", "Use an absolute http(s) URL.");
    }

    private static string? ValidColor(string color) =>
        color.Length == 0 ? null
        : Event.IsValidColor(color) ? color.ToLowerInvariant()
        : throw Validation.Failed("color", "Use a hex color like #4f46e5.");

    private static List<string> ValidCategories(IReadOnlyList<string> categories)
    {
        var valid = categories.Select(c => c?.Trim() ?? string.Empty).Where(c => c.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        return valid.Count <= Event.MaxCategories && valid.All(c => c.Length <= Event.CategoryMaxLength && !c.Contains(',', StringComparison.Ordinal))
            ? valid
            : throw Validation.Failed("categories", $"At most {Event.MaxCategories} categories of up to {Event.CategoryMaxLength} characters, without commas.");
    }

    private static string ValidUid(string uid) =>
        Event.IsValidUid(uid)
            ? uid
            : throw Validation.Failed("uid", $"Use 1 to {Event.UidMaxLength} printable ASCII characters without spaces, quotes, backslashes, commas or semicolons.");
}

/// <summary>Audit snapshots of events.</summary>
internal static class EventAudit
{
    public static EventState State(Event ev) =>
        new(
            ev.CalendarId,
            ev.Uid,
            ev.Title,
            ev.Description,
            ev.Location,
            ev.Url,
            ev.Status.ToString().ToLowerInvariant(),
            ev.Transparency.ToString().ToLowerInvariant(),
            ev.Color,
            string.Join(",", ev.Categories),
            Times(ev.Times),
            RecurrenceValues.Audit(ev),
            ev.IsSeries ? ev.Exceptions.Count : null);

    public static TimesState Times(EventTimes times) =>
        times.AllDay
            ? new TimesState(true, LocalDatePattern.Iso.Format(times.StartDate!.Value), LocalDatePattern.Iso.Format(times.EndDate!.Value), null)
            : new TimesState(false, LocalDateTimePattern.ExtendedIso.Format(times.StartLocal!.Value), LocalDateTimePattern.ExtendedIso.Format(times.EndLocal!.Value), times.TimeZone);

    public sealed record EventState(
        Guid CalendarId,
        string Uid,
        string Title,
        string? Description,
        string? Location,
        string? Url,
        string Status,
        string Transparency,
        string? Color,
        string Categories,
        TimesState Times,
        string? Recurrence,
        int? Exceptions);

    public sealed record TimesState(bool AllDay, string Start, string End, string? TimeZone);
}
