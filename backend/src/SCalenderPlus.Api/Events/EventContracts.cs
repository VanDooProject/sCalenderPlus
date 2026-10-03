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
    public static (EventTimeResponse Start, EventTimeResponse End) From(Event ev)
    {
        ArgumentNullException.ThrowIfNull(ev);
        return ev.AllDay
            ? (AllDay(ev.StartDate!.Value), AllDay(ev.EndDate!.Value))
            : (new(LocalDateTimePattern.ExtendedIso.Format(ev.StartLocal!.Value), ev.TimeZone, ev.StartUtc.ToDateTimeOffset(), null),
               new(LocalDateTimePattern.ExtendedIso.Format(ev.EndLocal!.Value), ev.TimeZone, ev.EndUtc.ToDateTimeOffset(), null));
    }

    private static EventTimeResponse AllDay(NodaTime.LocalDate date) => new(null, null, null, LocalDatePattern.Iso.Format(date));
}

/// <param name="DisplayName">Null when the account no longer exists.</param>
public sealed record EventCreatorResponse(Guid Id, string? DisplayName);

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
    /// <summary>The representation of <paramref name="view"/> (busy projection below <c>read</c>), without <c>etag</c> and <c>warnings</c>.</summary>
    public static EventResponse From(EventView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var ev = view.Event;
        var (start, end) = EventTimeResponse.From(ev);
        var transparency = Format(ev.Transparency);
        var level = PermissionLevels.Format(view.Level);
        var shared = view.SharedWithMe ? true : (bool?)null;
        if (EventVisibility.IsBusyOnly(view.Level))
        {
            return new EventResponse(
                ev.Id, ev.CalendarId, null, null, null, null, null, null, null, null, start, end, ev.AllDay, transparency,
                null, null, null, null, null, level, shared, null, null);
        }

        return new EventResponse(
            ev.Id,
            ev.CalendarId,
            ev.Uid,
            ev.Title,
            ev.Description,
            ev.Location,
            ev.Url,
            Format(ev.Status),
            ev.Color,
            [.. ev.Categories],
            start,
            end,
            ev.AllDay,
            transparency,
            ev.HasOverrides,
            ev.Sequence,
            ev.CreatorUserId is { } creator ? new EventCreatorResponse(creator, view.CreatorName) : null,
            ev.CreatedAt.ToDateTimeOffset(),
            ev.UpdatedAt.ToDateTimeOffset(),
            level,
            shared,
            null,
            null);
    }

    /// <summary>The representation with its <c>etag</c> member set (list items).</summary>
    public EventResponse WithEtag() => this with { Etag = Hosting.ETags.Of(this with { Etag = null, Warnings = null }) };

    /// <summary>The <c>ETag</c> header value: hash of the representation (without <c>etag</c>/<c>warnings</c>).</summary>
    public string HeaderETag() => Hosting.ETags.Of(this with { Etag = null, Warnings = null });

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

/// <summary>Recurrence rules (RFC 5545). Not supported yet: any value is <c>422 recurrence_not_supported</c>.</summary>
public sealed class EventRecurrenceRequest
{
    public string? Rrule { get; init; }

    public IReadOnlyList<string>? Rdates { get; init; }

    public IReadOnlyList<string>? Exdates { get; init; }
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

    /// <summary>Not supported yet (M2-E): leave out or send null.</summary>
    public EventRecurrenceRequest? Recurrence { get; init; }

    public EventDetails Details() => new(Title, Description, Location, Url, ParseStatus(Status), ParseTransparency(Transparency), Color, Categories);

    private static EventStatus? ParseStatus(string? value) => value switch
    {
        null => null,
        "confirmed" => EventStatus.Confirmed,
        "tentative" => EventStatus.Tentative,
        "cancelled" => EventStatus.Cancelled,
        _ => throw Validation.Failed("status", "Use confirmed, tentative or cancelled."),
    };

    private static EventTransparency? ParseTransparency(string? value) => value switch
    {
        null => null,
        "opaque" => EventTransparency.Opaque,
        "transparent" => EventTransparency.Transparent,
        _ => throw Validation.Failed("transparency", "Use opaque or transparent."),
    };
}

/// <summary>A new single event. Needs <c>contribute</c> on the calendar.</summary>
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

    public static EventWindowQuery Parse(DateTimeOffset? from, DateTimeOffset? to, Guid[]? calendarIds, string? timeZone)
    {
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

        return new EventWindowQuery(start, end, calendarIds is { Length: > 0 } ? [.. calendarIds.Distinct()] : null, zone);
    }
}
