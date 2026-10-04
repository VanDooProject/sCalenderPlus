using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using NodaTime;
using SCalenderPlus.Core.Permissions;
using SCalenderPlus.Core.Recurrence;

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

    /// <summary>
    /// RFC 5545 RRULE of a series master in canonical form (<see cref="RecurrenceRule.ToString"/>, <c>UNTIL</c>
    /// bound to the series: UTC for timed, a date for all-day series); null for single events.
    /// </summary>
    public string? Rrule { get; set; }

    /// <summary><c>RDATE</c>s of a series: wall clock in <see cref="TimeZone"/> (all-day: dates at midnight).</summary>
    public IList<LocalDateTime> RDates { get; set; } = [];

    /// <summary><c>EXDATE</c>s of a series: nominal starts of excluded occurrences, like <see cref="RDates"/>.</summary>
    public IList<LocalDateTime> ExDates { get; set; } = [];

    /// <summary>
    /// End of the last occurrence of a series (moved exceptions included); null = infinite (or not recurring).
    /// Maintained by <see cref="RefreshSeriesBounds"/>.
    /// </summary>
    public Instant? SeriesUntilUtc { get; set; }

    /// <summary>Start of a moved exception that lies before the first occurrence (it widens <c>occurs_range</c>); null otherwise.</summary>
    public Instant? SeriesStartUtc { get; set; }

    /// <summary>iCalendar <c>RELATED-TO</c>: the UID of the series this one was split from ("this and following").</summary>
    public string? RelatedTo { get; set; }

    /// <summary>Modified and cancelled occurrences of a series (<c>event_exceptions</c>).</summary>
    public List<EventExceptionEntry> Exceptions { get; set; } = [];

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

    /// <summary>A series master (has an RRULE).</summary>
    public bool IsSeries => Rrule is not null;

    /// <summary>
    /// The upper bound of <c>occurs_range</c>: the end of a single event, <see cref="SeriesUntilUtc"/> of a series
    /// (null = infinite). Plan limits count events with overrides while active (<c>PlanLimits.IsActive</c>).
    /// </summary>
    public Instant? OccursUntil => Rrule is null ? EndUtc : SeriesUntilUtc;

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

    /// <summary>The recurrence set of a series (first occurrence = the stored times); null for single events.</summary>
    public RecurrenceSet? Recurrence() =>
        Rrule is null
            ? null
            : new RecurrenceSet(Times, RecurrenceRule.Parse(Rrule, out var problem) ?? throw new InvalidOperationException($"Stored RRULE of event {Id} is invalid: {problem!.Message}"), [.. RDates], [.. ExDates]);

    /// <summary>
    /// The occurrences of a series overlapping <c>[from, to)</c> by their instants (all-day: padded bounds), with
    /// <see cref="Exceptions"/> applied — cancelled ones left out, moved ones where they moved to — by start, at most
    /// <paramref name="max"/> (<see cref="OccurrenceList.Truncated"/> beyond, and when <paramref name="budget"/> ran out).
    /// </summary>
    public OccurrenceList Occurrences(Instant from, Instant to, int max = RecurrenceSet.MaxPerWindow, ExpansionBudget? budget = null)
    {
        var set = Recurrence() ?? throw new InvalidOperationException("Only series have occurrences.");
        var window = set.Between(from, to, max, budget);
        var exceptions = Exceptions.ToDictionary(x => x.RecurrenceId);
        var items = new List<EventOccurrence>(window.Items.Count);
        foreach (var occurrence in window.Items)
        {
            var exception = exceptions.GetValueOrDefault(occurrence.RecurrenceId);
            if (exception is not ({ Cancelled: true } or { IsMoved: true }))
            {
                items.Add(new EventOccurrence(this, occurrence, exception));
            }
        }

        // Moved occurrences count where they are now (stored exceptions always belong to the set, see RefreshExceptions).
        foreach (var exception in Exceptions.Where(x => x is { IsMoved: true, Cancelled: false }))
        {
            if (exception.MovedTimes(TimeZone)!.Overlaps(from, to, null))
            {
                items.Add(new EventOccurrence(this, set.At(exception.RecurrenceId), exception));
            }
        }

        items.Sort((a, b) => a.Times.StartUtc != b.Times.StartUtc ? a.Times.StartUtc.CompareTo(b.Times.StartUtc) : a.RecurrenceId.CompareTo(b.RecurrenceId));
        return items.Count > max
            ? new OccurrenceList([.. items.Take(max)], true)
            : new OccurrenceList(items, window.Truncated);
    }

    /// <summary>
    /// The occurrence with <paramref name="recurrenceId"/> as viewers see it, or null when the series has none —
    /// also when it is cancelled, unless <paramref name="includeCancelled"/>.
    /// </summary>
    public EventOccurrence? FindOccurrence(LocalDateTime recurrenceId, bool includeCancelled = false)
    {
        var original = Recurrence()?.Find(recurrenceId);
        var exception = Exceptions.FirstOrDefault(x => x.RecurrenceId == recurrenceId);
        return original is null || (exception is { Cancelled: true } && !includeCancelled) ? null : new EventOccurrence(this, original, exception);
    }

    /// <summary>
    /// Recomputes <see cref="SeriesUntilUtc"/> (the rule's last occurrence and moved exceptions; null when infinite)
    /// and <see cref="SeriesStartUtc"/> after a change of the times, the recurrence or the exceptions.
    /// </summary>
    public void RefreshSeriesBounds()
    {
        if (Recurrence() is not { } set)
        {
            SeriesUntilUtc = null;
            SeriesStartUtc = null;
            return;
        }

        var moved = Exceptions.Where(x => x is { IsMoved: true, Cancelled: false }).ToList();
        SeriesUntilUtc = set.LastEnd() is { } last ? moved.Select(x => x.EndUtc!.Value).Append(last).Max() : null;
        var earliest = moved.Select(x => x.StartUtc!.Value).DefaultIfEmpty(StartUtc).Min();
        SeriesStartUtc = earliest < StartUtc ? earliest : null;
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

/// <summary>Occurrences of a series in a window, by start; <see cref="Truncated"/> when capped.</summary>
public sealed record OccurrenceList(IReadOnlyList<EventOccurrence> Items, bool Truncated);

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
