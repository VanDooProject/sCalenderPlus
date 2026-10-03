using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using NodaTime;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Core.Events;

/// <summary>iCalendar <c>STATUS</c> of an event, stored as <c>smallint</c>.</summary>
public enum EventStatus : short
{
    Confirmed = 0,
    Tentative = 1,
    Cancelled = 2,
}

/// <summary>iCalendar <c>TRANSP</c>: whether the event blocks time (<see cref="Opaque"/>, "busy") or not.</summary>
public enum EventTransparency : short
{
    Opaque = 0,

    /// <summary>"Free": omitted entirely for <c>free_busy</c> viewers (permissions.md §2.1).</summary>
    Transparent = 1,
}

/// <summary>
/// An event (<c>events</c>, docs/architecture/data-model.md §4): timed (wall clock <see cref="StartLocal"/> /
/// <see cref="EndLocal"/> in <see cref="TimeZone"/>, authoritative) or all-day (<see cref="StartDate"/> /
/// exclusive <see cref="EndDate"/>, floating). <see cref="StartUtc"/> / <see cref="EndUtc"/> are derived
/// (<see cref="EventTimes"/>); for all-day events they are padded by ±14 h so window queries in every viewer zone
/// find them. Events are soft-deleted (<see cref="DeletedAt"/>) for sync and restore. Rows are read only through
/// the permission-aware event query service (an architecture test enforces it).
/// </summary>
[SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "The domain term (data-model.md 'events'); no VB consumers.")]
public sealed partial class Event
{
    public const int TitleMaxLength = 500;
    public const int DescriptionMaxLength = 20_000;
    public const int LocationMaxLength = 1000;
    public const int UrlMaxLength = 2000;
    public const int UidMaxLength = 255;
    public const int TimeZoneMaxLength = 64;
    public const int ColorLength = 7;
    public const int MaxCategories = 20;
    public const int CategoryMaxLength = 50;

    /// <summary>Domain of native UIDs: <c>{id}@scalenderplus</c>.</summary>
    public const string UidDomain = "scalenderplus";

    public Guid Id { get; set; }

    public Guid CalendarId { get; set; }

    /// <summary>iCalendar UID, unique per calendar among live events; <c>{id}@scalenderplus</c> for native events.</summary>
    public string Uid { get; set; } = string.Empty;

    /// <summary>Null for system/import events; kept as tombstone after the user is deleted (no FK, no floor then).</summary>
    public Guid? CreatorUserId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Location { get; set; }

    public string? Url { get; set; }

    public EventStatus Status { get; set; }

    public EventTransparency Transparency { get; set; }

    /// <summary><c>#rrggbb</c> (lowercase); null = the calendar's color.</summary>
    public string? Color { get; set; }

    /// <summary>iCalendar <c>CATEGORIES</c> (free text).</summary>
    public IList<string> Categories { get; set; } = [];

    public bool AllDay { get; set; }

    public LocalDateTime? StartLocal { get; set; }

    public LocalDateTime? EndLocal { get; set; }

    public LocalDate? StartDate { get; set; }

    /// <summary>Exclusive.</summary>
    public LocalDate? EndDate { get; set; }

    /// <summary>IANA zone of timed events; null for all-day events.</summary>
    public string? TimeZone { get; set; }

    public Instant StartUtc { get; set; }

    public Instant EndUtc { get; set; }

    /// <summary>RFC 5545 RRULE of a series master (M2-E); always null until recurrence is supported.</summary>
    public string? Rrule { get; set; }

    /// <summary>End of the last occurrence of a series (M2-E); null = infinite (or not recurring).</summary>
    public Instant? SeriesUntilUtc { get; set; }

    /// <summary>Fast path of the permission engine: only events with overrides need them loaded (M2-D).</summary>
    public bool HasOverrides { get; set; }

    /// <summary>iCalendar SEQUENCE, incremented on significant changes (times, status).</summary>
    public int Sequence { get; set; }

    public Instant? DeletedAt { get; set; }

    public Instant CreatedAt { get; set; }

    public Instant UpdatedAt { get; set; }

    /// <summary>Optimistic concurrency token (PostgreSQL <c>xmin</c>).</summary>
    public uint Version { get; set; }

    public bool IsDeleted => DeletedAt is not null;

    /// <summary>The stored times as <see cref="EventTimes"/> (no adjustments).</summary>
    public EventTimes Times => AllDay
        ? new EventTimes(true, null, null, StartDate, EndDate, null, StartUtc, EndUtc, [])
        : new EventTimes(false, StartLocal, EndLocal, null, null, TimeZone, StartUtc, EndUtc, []);

    /// <summary>Copies the times (and derived instants) into the row.</summary>
    public void SetTimes(EventTimes times)
    {
        ArgumentNullException.ThrowIfNull(times);
        AllDay = times.AllDay;
        StartLocal = times.StartLocal;
        EndLocal = times.EndLocal;
        StartDate = times.StartDate;
        EndDate = times.EndDate;
        TimeZone = times.TimeZone;
        StartUtc = times.StartUtc;
        EndUtc = times.EndUtc;
    }

    /// <summary>The engine's view of this event with <paramref name="overrides"/> (none until M2-D stores them).</summary>
    public EventAcl ToAcl(IReadOnlyList<EventOverride>? overrides = null) => new(Id, CalendarId, CreatorUserId, overrides);

    /// <summary>The UID of a native event: <c>{id}@scalenderplus</c> (also the opaque UID of multi-calendar feeds).</summary>
    public static string NativeUid(Guid id) => $"{id}@{UidDomain}";

    /// <summary>
    /// RFC 5545-safe UID: 1–255 printable ASCII characters without spaces, quotes, backslashes, commas or
    /// semicolons (they need escaping in TEXT and would not survive every client).
    /// </summary>
    public static bool IsValidUid(string? uid) =>
        uid is { Length: > 0 and <= UidMaxLength } && uid.All(c => c is >= '!' and <= '~' and not ('"' or '\\' or ',' or ';'));

    /// <summary><c>#rrggbb</c> hex color (either case).</summary>
    public static bool IsValidColor(string? color) => color is { Length: ColorLength } && ColorPattern().IsMatch(color);

    [GeneratedRegex("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ColorPattern();
}

/// <summary>Kind of a <see cref="CalendarChange"/> (stored as <c>smallint</c>).</summary>
public enum CalendarChangeKind : short
{
    /// <summary>The event was created or changed.</summary>
    Upsert = 0,

    /// <summary>The event was deleted (or left the calendar).</summary>
    Delete = 1,

    /// <summary>The event's ACL changed (M2-D): viewers must re-evaluate it.</summary>
    Acl = 2,
}

/// <summary>
/// One row of the sync log <c>calendar_changes</c> (data-model.md §4): every event change in a calendar, in commit
/// order of <see cref="Seq"/> (bigserial). Powers CalDAV <c>sync-collection</c>, webhooks and <c>/changes?since=</c>.
/// </summary>
public sealed class CalendarChange
{
    public long Seq { get; set; }

    public Guid CalendarId { get; set; }

    public Guid EventId { get; set; }

    public CalendarChangeKind Change { get; set; }

    public Instant At { get; set; }
}
