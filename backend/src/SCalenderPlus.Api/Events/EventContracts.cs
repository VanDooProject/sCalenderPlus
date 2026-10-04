using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using NodaTime.Text;
using SCalenderPlus.Application.Common;
using SCalenderPlus.Application.Events;
using SCalenderPlus.Core.Events;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Api.Events;

/// <summary>
/// A time value (api.md §1): timed <c>{ dateTime, timeZone, utc }</c> (wall clock in an IANA zone; <c>utc</c> is
/// the derived instant, read-only) or all-day <c>{ date }</c> (end dates are exclusive).
/// </summary>
public sealed record EventTimeResponse(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DateTime,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TimeZone,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? Utc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Date)
{
    public static (EventTimeResponse Start, EventTimeResponse End) From(EventTimes times)
    {
        ArgumentNullException.ThrowIfNull(times);
        return times.AllDay
            ? (AllDay(times.StartDate!.Value), AllDay(times.EndDate!.Value))
            : (new(LocalDateTimePattern.ExtendedIso.Format(times.StartLocal!.Value), times.TimeZone, times.StartUtc.ToDateTimeOffset(), null),
               new(LocalDateTimePattern.ExtendedIso.Format(times.EndLocal!.Value), times.TimeZone, times.EndUtc.ToDateTimeOffset(), null));
    }

    private static EventTimeResponse AllDay(NodaTime.LocalDate date) => new(null, null, null, LocalDatePattern.Iso.Format(date));
}

/// <param name="DisplayName">Null when the account no longer exists.</param>
public sealed record EventCreatorResponse(Guid Id, string? DisplayName);

/// <summary>The recurrence of a series (RFC 5545 values, data-model.md §9).</summary>
/// <param name="Rrule">Canonical RRULE: UNTIL is a UTC date-time for timed series and a date for all-day series.</param>
/// <param name="Rdates">Extra occurrences: wall clock in the series' zone (timed) or dates (all-day).</param>
/// <param name="Exdates">Excluded occurrences (their nominal start), like rdates.</param>
public sealed record EventRecurrenceResponse(
    string Rrule,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Rdates,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Exdates)
{
    public static EventRecurrenceResponse? From(Event ev)
    {
        ArgumentNullException.ThrowIfNull(ev);
        return ev.Rrule is null
            ? null
            : new(ev.Rrule, ev.RDates.Count > 0 ? RecurrenceValues.Format(ev.RDates, ev.AllDay) : null, ev.ExDates.Count > 0 ? RecurrenceValues.Format(ev.ExDates, ev.AllDay) : null);
    }
}

/// <summary>
/// A modified or cancelled occurrence of a series ("exception", keyed by RECURRENCE-ID). Members that are absent
/// inherit the series' values. The busy projection lists only exceptions that change busy time — cancelled, moved
/// or with their own transparency — with recurrenceId, cancelled, start, end and transparency; a transparent
/// ("free") occurrence comes without its times, as free_busy viewers never see transparent events.
/// </summary>
/// <param name="RecurrenceId">The occurrence's original start: a UTC instant (timed) or a date (all-day).</param>
/// <param name="Start">Where the occurrence moved to (absent: not moved).</param>
public sealed record EventExceptionResponse(
    string RecurrenceId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Cancelled,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Title,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Location,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Transparency,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] EventTimeResponse? Start,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] EventTimeResponse? End)
{
    /// <summary>The exceptions of a series in order (busy projection with <paramref name="busy"/>).</summary>
    public static IReadOnlyList<EventExceptionResponse>? From(Event series, bool busy)
    {
        ArgumentNullException.ThrowIfNull(series);
        if (series.Recurrence() is not { } set || series.Exceptions.Count == 0)
        {
            return null;
        }

        var exceptions = busy ? series.Exceptions.Where(ChangesBusyTime) : series.Exceptions;
        IReadOnlyList<EventExceptionResponse> items = [.. exceptions.OrderBy(x => x.RecurrenceId).Select(x =>
        {
            var moved = x.MovedTimes(series.TimeZone) is { } times ? EventTimeResponse.From(times) : ((EventTimeResponse, EventTimeResponse)?)null;
            var transparency = x.Transparency is { } t ? EventResponse.Format(t) : null;
            var id = EventRecurrences.Format(set.At(x.RecurrenceId));
            if (busy && !x.Cancelled && (x.Transparency ?? series.Transparency) == EventTransparency.Transparent)
            {
                moved = null; // free: hidden from free_busy viewers like a transparent event, so not where it moved to
            }

            return busy
                ? new EventExceptionResponse(id, x.Cancelled ? true : null, null, null, null, null, transparency, moved?.Item1, moved?.Item2)
                : new EventExceptionResponse(id, x.Cancelled ? true : null, x.Title, x.Description, x.Location, x.Status is { } s ? EventResponse.Format(s) : null, transparency, moved?.Item1, moved?.Item2);
        })];
        return items.Count == 0 ? null : items;
    }

    /// <summary>Whether the exception changes when the series is busy: cancelled, moved or with its own transparency (not title-like fields).</summary>
    private static bool ChangesBusyTime(EventExceptionEntry exception) =>
        exception.Cancelled || exception.IsMoved || exception.Transparency is not null;
}

/// <summary>A requested time was resolved differently (data-model.md §10): show it to the user.</summary>
/// <param name="Code"><c>time_shifted_dst_gap</c> (the local time does not exist: shifted forward) or <c>time_ambiguous_earlier_offset</c> (exists twice: the earlier offset was used).</param>
/// <param name="Field"><c>start</c> or <c>end</c>.</param>
/// <param name="Requested">The requested wall clock.</param>
/// <param name="Resolved">The stored wall clock.</param>
/// <param name="Utc">The resulting instant.</param>
public sealed record EventWarningResponse(string Code, string Field, string Message, string Requested, string Resolved, DateTimeOffset Utc)
{
    public const string ShiftedInGap = "time_shifted_dst_gap";
    public const string AmbiguousEarlier = "time_ambiguous_earlier_offset";

    public static EventWarningResponse From(TimeAdjustment adjustment)
    {
        ArgumentNullException.ThrowIfNull(adjustment);
        var requested = LocalDateTimePattern.ExtendedIso.Format(adjustment.Requested);
        var resolved = LocalDateTimePattern.ExtendedIso.Format(adjustment.Resolved);
        return adjustment.Kind == TimeAdjustmentKind.ShiftedForwardInGap
            ? new(ShiftedInGap, adjustment.Field, $"{requested} does not exist in this time zone (daylight saving time starts); the event uses {resolved}.", requested, resolved, adjustment.Utc.ToDateTimeOffset())
            : new(AmbiguousEarlier, adjustment.Field, $"{requested} occurs twice in this time zone (daylight saving time ends); the event uses the first one.", requested, resolved, adjustment.Utc.ToDateTimeOffset());
    }
}

/// <summary>
/// An event as the caller sees it (api.md §4 "Event representation"). Callers below <c>read</c>
/// (<c>myLevel: free_busy</c>) get the busy projection only — <c>id</c>, <c>calendarId</c>, <c>title: null</c>
/// (render a localized "Busy"), <c>start</c>, <c>end</c>, <c>allDay</c>, <c>transparency</c>, <c>myLevel</c>:
/// every other member is omitted server-side. Members that are null are omitted too.
/// </summary>
/// <param name="Uid">iCalendar UID (stable; native events <c>{id}@scalenderplus</c>).</param>
/// <param name="Title">Null in the busy projection.</param>
/// <param name="Status"><c>confirmed</c>, <c>tentative</c> or <c>cancelled</c>.</param>
/// <param name="Transparency"><c>opaque</c> (busy) or <c>transparent</c> (free).</param>
/// <param name="Color"><c>#rrggbb</c>; absent = the calendar's color.</param>
/// <param name="HasOverrides">The event has its own permissions (overrides).</param>
/// <param name="Sequence">iCalendar SEQUENCE (incremented on time/status changes).</param>
/// <param name="MyLevel">The caller's effective level: <c>free_busy</c>, <c>read</c>, <c>edit</c> or <c>manage</c> (permissions.md §2.1).</param>
/// <param name="SharedWithMe">True when the event reaches the caller only through its own permissions, from a calendar they cannot see ("Shared with me").</param>
/// <param name="Etag">The ETag of <c>GET /events/{id}</c> (list items; for If-Match without a GET).</param>
/// <param name="Warnings">Create/update only: requested times that were adjusted (DST).</param>
/// <remarks>
/// Series masters carry <see cref="Recurrence"/> and <see cref="Exceptions"/>; occurrences (window with
/// <c>expand=occurrences</c>, occurrence edits) carry <see cref="OccurrenceId"/>, <see cref="RecurrenceId"/> and
/// the series' <see cref="Recurrence"/>, with the occurrence's own times and fields (exceptions applied).
/// </remarks>
public sealed record EventResponse(
    Guid Id,
    Guid CalendarId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Uid,
    string? Title,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Location,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Url,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Color,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Categories,
    EventTimeResponse Start,
    EventTimeResponse End,
    bool AllDay,
    string Transparency,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? HasOverrides,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Sequence,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] EventCreatorResponse? CreatedBy,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? CreatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? UpdatedAt,
    string MyLevel,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? SharedWithMe,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Etag,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<EventWarningResponse>? Warnings)
{
    /// <summary>The recurrence of a series (masters and occurrences); absent for single events.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EventRecurrenceResponse? Recurrence { get; init; }

    /// <summary>Series masters: the modified and cancelled occurrences.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<EventExceptionResponse>? Exceptions { get; init; }

    /// <summary>iCalendar RELATED-TO: the UID of the series this one was split from ("this and following").</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RelatedTo { get; init; }

    /// <summary>Occurrences: <c>{id}:{recurrenceId}</c>, stable across edits of the occurrence.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OccurrenceId { get; init; }

    /// <summary>Occurrences: the original start (RECURRENCE-ID) — a UTC instant (timed) or a date (all-day); the key of occurrence edits.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RecurrenceId { get; init; }

    /// <summary>Occurrences: true when the occurrence differs from the series (moved or changed fields).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Modified { get; init; }

    /// <summary>Update/split of a series only: recurrence ids of exceptions that no longer matched an occurrence and were dropped.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? DroppedExceptions { get; init; }

    /// <summary>
    /// The representation of <paramref name="view"/> (busy projection below <c>read</c>), without <c>etag</c>,
    /// <c>warnings</c> and <c>droppedExceptions</c>: the event, the series master, or one occurrence of it.
    /// </summary>
    public static EventResponse From(EventView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var ev = view.Event;
        var occurrence = view.Occurrence;
        var times = occurrence?.Times ?? ev.Times;
        var (start, end) = EventTimeResponse.From(times);
        var transparency = Format(occurrence?.Transparency ?? ev.Transparency);
        var level = PermissionLevels.Format(view.Level);
        var shared = view.SharedWithMe ? true : (bool?)null;
        var busy = EventVisibility.IsBusyOnly(view.Level);
        var recurrenceId = occurrence is null ? null : EventRecurrences.Format(occurrence.Original);
        var series = new
        {
            Recurrence = EventRecurrenceResponse.From(ev),
            Exceptions = occurrence is null ? EventExceptionResponse.From(ev, busy) : null,
            OccurrenceId = recurrenceId is null ? null : $"{ev.Id}:{recurrenceId}",
        };
        if (busy)
        {
            return new EventResponse(
                ev.Id, ev.CalendarId, null, null, null, null, null, null, null, null, start, end, times.AllDay, transparency,
                null, null, null, null, null, level, shared, null, null)
            {
                Recurrence = series.Recurrence,
                Exceptions = series.Exceptions,
                OccurrenceId = series.OccurrenceId,
                RecurrenceId = recurrenceId,
            };
        }

        return new EventResponse(
            ev.Id,
            ev.CalendarId,
            ev.Uid,
            occurrence?.Title ?? ev.Title,
            occurrence is null ? ev.Description : occurrence.Description,
            occurrence is null ? ev.Location : occurrence.Location,
            ev.Url,
            Format(occurrence?.Status ?? ev.Status),
            ev.Color,
            [.. ev.Categories],
            start,
            end,
            times.AllDay,
            transparency,
            ev.HasOverrides,
            ev.Sequence,
            ev.CreatorUserId is { } creator ? new EventCreatorResponse(creator, view.CreatorName) : null,
            ev.CreatedAt.ToDateTimeOffset(),
            ev.UpdatedAt.ToDateTimeOffset(),
            level,
            shared,
            null,
            null)
        {
            Recurrence = series.Recurrence,
            Exceptions = series.Exceptions,
            RelatedTo = ev.RelatedTo,
            OccurrenceId = series.OccurrenceId,
            RecurrenceId = recurrenceId,
            Modified = occurrence?.IsModified == true ? true : null,
        };
    }

    /// <summary>The representation with its <c>etag</c> member set (list items).</summary>
    public EventResponse WithEtag() => this with { Etag = HeaderETag() };

    /// <summary>The <c>ETag</c> header value: hash of the representation (without <c>etag</c>/<c>warnings</c>/<c>droppedExceptions</c>).</summary>
    public string HeaderETag() => Hosting.ETags.Of(this with { Etag = null, Warnings = null, DroppedExceptions = null });

    /// <summary>
    /// The <c>ETag</c> of the event (a series: of its master, which carries the exceptions) — the If-Match of
    /// every change, also of occurrence edits.
    /// </summary>
    public static string SeriesETag(EventView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return From(view with { Occurrence = null }).HeaderETag();
    }

    public static string Format(EventStatus status) => status switch
    {
        EventStatus.Tentative => "tentative",
        EventStatus.Cancelled => "cancelled",
        _ => "confirmed",
    };

    public static string Format(EventTransparency transparency) =>
        transparency == EventTransparency.Transparent ? "transparent" : "opaque";
}

/// <summary>The events of a window, ordered by start (api.md §4: not paged; bounded by the window size).</summary>
/// <param name="Truncated">More than 5,000 events matched: only the first are returned; query a smaller window.</param>
public sealed record EventWindowResponse(IReadOnlyList<EventResponse> Items, bool Truncated);

/// <summary>A time value: <c>{ dateTime, timeZone? }</c> (timed; zone default: the calendar's, or the start's for <c>end</c>) or <c>{ date }</c> (all-day).</summary>
public sealed class EventTimeRequest
{
    /// <summary>Local wall clock, e.g. <c>2026-11-02T18:00:00</c> (no offset).</summary>
    [StringLength(32)]
    public string? DateTime { get; init; }

    /// <summary>IANA time zone id, e.g. <c>Europe/Berlin</c>.</summary>
    [StringLength(Event.TimeZoneMaxLength)]
    public string? TimeZone { get; init; }

    /// <summary>All-day: <c>2026-11-02</c> (end dates are exclusive).</summary>
    [StringLength(16)]
    public string? Date { get; init; }

    public EventTimeInput ToInput() => new(DateTime, TimeZone, Date);
}

/// <summary>
/// Recurrence (RFC 5545, data-model.md §9). <c>rrule</c>: FREQ DAILY | WEEKLY | MONTHLY | YEARLY with INTERVAL,
/// COUNT (≤ 5000) or UNTIL, BYMONTH, BYMONTHDAY, BYDAY (ordinals like 2MO / -1FR for monthly and yearly rules),
/// BYSETPOS and WKST; other parts → 422 recurrence_not_supported, malformed values → 422 recurrence_invalid.
/// The start is the first occurrence (it always counts). Occurrences keep the start's local time across DST.
/// </summary>
public sealed class EventRecurrenceRequest
{
    /// <summary>The RRULE value, e.g. <c>FREQ=WEEKLY;BYDAY=MO,WE</c> (an <c>RRULE:</c> prefix is accepted).</summary>
    [StringLength(Core.Recurrence.RecurrenceRule.MaxLength + 6)]
    public string? Rrule { get; init; }

    /// <summary>Extra occurrences (≤ 100, not before the start): wall clock in the series' zone (<c>2026-11-09T18:00:00</c>) or dates for all-day series.</summary>
    public IReadOnlyList<string>? Rdates { get; init; }

    /// <summary>Excluded occurrences (≤ 1000), by their start like <c>rdates</c>.</summary>
    public IReadOnlyList<string>? Exdates { get; init; }

    public EventRecurrenceInput ToInput() => new(Rrule, Rdates, Exdates);
}

/// <summary>Members shared by create and update.</summary>
public abstract class EventDetailsRequest
{
    /// <summary>1–500 characters (trimmed).</summary>
    [StringLength(Event.TitleMaxLength)]
    public string? Title { get; init; }

    /// <summary>Markdown subset, at most 20,000 characters.</summary>
    [StringLength(Event.DescriptionMaxLength)]
    public string? Description { get; init; }

    [StringLength(Event.LocationMaxLength)]
    public string? Location { get; init; }

    /// <summary>Absolute http(s) URL.</summary>
    [StringLength(Event.UrlMaxLength)]
    public string? Url { get; init; }

    /// <summary><c>confirmed</c> (default), <c>tentative</c> or <c>cancelled</c>.</summary>
    public string? Status { get; init; }

    /// <summary><c>opaque</c> (default, busy) or <c>transparent</c> (free).</summary>
    public string? Transparency { get; init; }

    /// <summary><c>#rrggbb</c>; default: the calendar's color.</summary>
    [StringLength(Event.ColorLength)]
    public string? Color { get; init; }

    /// <summary>At most 20 texts of up to 50 characters (no commas).</summary>
    public IReadOnlyList<string>? Categories { get; init; }

    /// <summary>
    /// Makes the event a series (data-model.md §9). On PATCH, <c>null</c> turns a series into a single event (its
    /// first occurrence) and drops its exceptions; absent leaves the recurrence unchanged.
    /// </summary>
    public EventRecurrenceRequest? Recurrence
    {
        get => _recurrence;
        init
        {
            _recurrence = value;
            RecurrenceGiven = true;
        }
    }

    /// <summary>The request carried <c>recurrence</c> (also as an explicit <c>null</c>).</summary>
    [JsonIgnore]
    public bool RecurrenceGiven { get; private init; }

    private readonly EventRecurrenceRequest? _recurrence;

    public EventDetails Details() => new(Title, Description, Location, Url, ParseStatus(Status), ParseTransparency(Transparency), Color, Categories);

    internal static EventStatus? ParseStatus(string? value) => value switch
    {
        null => null,
        "confirmed" => EventStatus.Confirmed,
        "tentative" => EventStatus.Tentative,
        "cancelled" => EventStatus.Cancelled,
        _ => throw Validation.Failed("status", "Use confirmed, tentative or cancelled."),
    };

    internal static EventTransparency? ParseTransparency(string? value) => value switch
    {
        null => null,
        "opaque" => EventTransparency.Opaque,
        "transparent" => EventTransparency.Transparent,
        _ => throw Validation.Failed("transparency", "Use opaque or transparent."),
    };
}

/// <summary>A new event — single, or a series with <c>recurrence</c>. Needs <c>contribute</c> on the calendar.</summary>
public sealed class CreateEventRequest : EventDetailsRequest
{
    [Required]
    public Guid? CalendarId { get; init; }

    [Required]
    public EventTimeRequest? Start { get; init; }

    [Required]
    public EventTimeRequest? End { get; init; }

    /// <summary>iCalendar UID to keep (e.g. when copying from another calendar app); default <c>{id}@scalenderplus</c>. Unique per calendar (409 uid_conflict).</summary>
    [StringLength(Event.UidMaxLength)]
    public string? Uid { get; init; }
}

/// <summary>
/// JSON Merge Patch of an event (<c>application/merge-patch+json</c>): absent or <c>null</c> members stay
/// unchanged; an empty <c>description</c>, <c>location</c>, <c>url</c> or <c>color</c> and an empty
/// <c>categories</c> list remove the value. Times: give <c>start</c> and/or <c>end</c> (switching between timed
/// and all-day needs both).
/// </summary>
public sealed class UpdateEventRequest : EventDetailsRequest
{
    public EventTimeRequest? Start { get; init; }

    public EventTimeRequest? End { get; init; }
}

/// <summary>
/// JSON Merge Patch of one occurrence of a series ("this occurrence"): absent or <c>null</c> members stay unchanged;
/// values equal to the series' make the occurrence follow the series again; an empty <c>description</c> or
/// <c>location</c> removes it for this occurrence. Times move the occurrence (same kind and zone as the series).
/// </summary>
public sealed class UpdateOccurrenceRequest
{
    /// <summary>1–500 characters (trimmed).</summary>
    [StringLength(Event.TitleMaxLength)]
    public string? Title { get; init; }

    [StringLength(Event.DescriptionMaxLength)]
    public string? Description { get; init; }

    [StringLength(Event.LocationMaxLength)]
    public string? Location { get; init; }

    /// <summary><c>confirmed</c>, <c>tentative</c> or <c>cancelled</c> (to drop the occurrence, use DELETE).</summary>
    public string? Status { get; init; }

    /// <summary><c>opaque</c> or <c>transparent</c>.</summary>
    public string? Transparency { get; init; }

    public EventTimeRequest? Start { get; init; }

    public EventTimeRequest? End { get; init; }

    public OccurrenceChanges ToChanges() =>
        new(Title, Description, Location, EventDetailsRequest.ParseStatus(Status), EventDetailsRequest.ParseTransparency(Transparency), Start?.ToInput(), End?.ToInput());
}

/// <summary>
/// "This and following": splits the series at <c>recurrenceId</c> into a new series (new UID, related to the
/// original), with the members of a merge patch applied to the new series (absent = as the original).
/// </summary>
public sealed class SplitEventRequest : EventDetailsRequest
{
    /// <summary>The occurrence where the new series starts (not the first one): its <c>recurrenceId</c>.</summary>
    [Required]
    [StringLength(64)]
    public string? RecurrenceId { get; init; }

    public EventTimeRequest? Start { get; init; }

    public EventTimeRequest? End { get; init; }

    public EventChanges ToChanges() => new(Details(), Start?.ToInput(), End?.ToInput(), RecurrenceGiven, Recurrence?.ToInput());
}

/// <summary>Moves an event to another calendar (permissions.md §4.6).</summary>
public sealed class MoveEventRequest
{
    /// <summary>A calendar you have at least contribute on.</summary>
    [Required]
    public Guid? TargetCalendarId { get; init; }
}

/// <summary>Validation of the window query parameters (api.md §4).</summary>
internal static class EventWindows
{
    /// <summary>Longest window (api.md §4): 13 months.</summary>
    public const int MaxMonths = 13;

    public const int MaxCalendarIds = 200;

    /// <summary>The <c>expand</c> value that returns series as occurrences.</summary>
    public const string Occurrences = "occurrences";

    public static EventWindowQuery Parse(DateTimeOffset? from, DateTimeOffset? to, Guid[]? calendarIds, string? timeZone, string? expand = null)
    {
        if (expand is not (null or Occurrences))
        {
            throw Validation.Failed("expand", "Use expand=occurrences (or leave it out for series masters).");
        }

        var start = from is { } f ? NodaTime.Instant.FromDateTimeOffset(f) : throw Validation.Failed("from", "Give the window start as an RFC 3339 instant, e.g. 2026-11-01T00:00:00Z.");
        var end = to is { } t ? NodaTime.Instant.FromDateTimeOffset(t) : throw Validation.Failed("to", "Give the window end as an RFC 3339 instant, e.g. 2026-12-01T00:00:00Z.");
        if (end <= start)
        {
            throw Validation.Failed("to", "The window end must be after its start.");
        }

        if (end > start.InUtc().LocalDateTime.PlusMonths(MaxMonths).InUtc().ToInstant())
        {
            throw Validation.Failed("to", $"A window spans at most {MaxMonths} months.");
        }

        if (calendarIds is { Length: > MaxCalendarIds })
        {
            throw Validation.Failed("calendarIds", $"At most {MaxCalendarIds} calendars per query.");
        }

        NodaTime.DateTimeZone? zone = null;
        if (timeZone is not null)
        {
            zone = (timeZone.Length <= Event.TimeZoneMaxLength ? NodaTime.DateTimeZoneProviders.Tzdb.GetZoneOrNull(timeZone) : null)
                ?? throw EventErrors.TimeZoneInvalid("timeZone", timeZone);
        }

        return new EventWindowQuery(start, end, calendarIds is { Length: > 0 } ? [.. calendarIds.Distinct()] : null, zone, expand == Occurrences);
    }
}
