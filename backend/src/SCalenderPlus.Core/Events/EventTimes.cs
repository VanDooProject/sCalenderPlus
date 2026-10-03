using NodaTime;
using NodaTime.TimeZones;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Core.Events;

/// <summary>Why a requested wall-clock time was resolved differently (data-model.md §10).</summary>
public enum TimeAdjustmentKind
{
    /// <summary>The local time does not exist (DST gap, clocks jump forward): shifted forward by the gap.</summary>
    ShiftedForwardInGap,

    /// <summary>The local time exists twice (DST overlap, clocks fall back): the earlier offset was used.</summary>
    AmbiguousEarlierOffset,
}

/// <summary>A requested wall-clock time of <see cref="Field"/> (<c>start</c> or <c>end</c>) and what it resolved to.</summary>
public sealed record TimeAdjustment(string Field, TimeAdjustmentKind Kind, LocalDateTime Requested, LocalDateTime Resolved, Instant Utc);

/// <summary>
/// The times of an event (data-model.md §10): timed events are a wall clock in an IANA zone (authoritative) plus
/// the derived UTC instants; all-day events are floating dates (end exclusive) whose UTC bounds are padded by
/// <see cref="AllDayPadding"/> on both sides, so a window query in any viewer zone (UTC−12 … UTC+14) finds them.
/// Build them with <see cref="Timed"/> or <see cref="AllDayEvent"/>.
/// </summary>
public sealed record EventTimes(
    bool AllDay,
    LocalDateTime? StartLocal,
    LocalDateTime? EndLocal,
    LocalDate? StartDate,
    LocalDate? EndDate,
    string? TimeZone,
    Instant StartUtc,
    Instant EndUtc,
    IReadOnlyList<TimeAdjustment> Adjustments)
{
    /// <summary>The earliest zone offset is UTC+14 and the latest UTC−12; ±14 h covers both.</summary>
    public static readonly Duration AllDayPadding = Duration.FromHours(14);

    /// <summary>Earliest supported date (both kinds).</summary>
    public static readonly LocalDate MinDate = new(1, 1, 2);

    /// <summary>Latest supported date (both kinds); the padding must stay inside NodaTime's instant range.</summary>
    public static readonly LocalDate MaxDate = new(9998, 12, 31);

    /// <summary>
    /// A timed event from <paramref name="start"/> to <paramref name="end"/> (wall clock) in <paramref name="zone"/>.
    /// Sub-second parts are dropped. A nonexistent local time (DST gap) is shifted forward by the gap — the stored
    /// wall clock is the shifted one — and an ambiguous one (DST overlap) takes the earlier offset; both are
    /// reported in <see cref="Adjustments"/>. Returns null when the end lies before the start (after resolution).
    /// </summary>
    public static EventTimes? Timed(LocalDateTime start, LocalDateTime end, DateTimeZone zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var adjustments = new List<TimeAdjustment>(2);
        var (startLocal, startUtc) = Resolve("start", Truncate(start), zone, adjustments);
        var (endLocal, endUtc) = Resolve("end", Truncate(end), zone, adjustments);
        return endUtc < startUtc
            ? null
            : new EventTimes(false, startLocal, endLocal, null, null, zone.Id, startUtc, endUtc, adjustments);
    }

    /// <summary>An all-day event on the dates <paramref name="start"/> … <paramref name="endExclusive"/> − 1; null unless the end is after the start.</summary>
    public static EventTimes? AllDayEvent(LocalDate start, LocalDate endExclusive) =>
        endExclusive <= start
            ? null
            : new EventTimes(true, null, null, start, endExclusive, null, PaddedStart(start), PaddedEnd(endExclusive), []);

    /// <summary>A bound before the earliest instant <paramref name="date"/> begins anywhere (UTC+14).</summary>
    public static Instant PaddedStart(LocalDate date) => date.AtMidnight().InUtc().ToInstant() - AllDayPadding;

    /// <summary>A bound after the latest instant <paramref name="endExclusive"/> begins anywhere (UTC−12).</summary>
    public static Instant PaddedEnd(LocalDate endExclusive) => endExclusive.AtMidnight().InUtc().ToInstant() + AllDayPadding;

    /// <summary>True when <paramref name="date"/> is within <see cref="MinDate"/> … <see cref="MaxDate"/>.</summary>
    public static bool IsSupported(LocalDate date) => date >= MinDate && date <= MaxDate;

    /// <summary>
    /// Whether this event overlaps the window [<paramref name="from"/>, <paramref name="to"/>) for a viewer in
    /// <paramref name="viewerZone"/>: timed events by their instants (a zero-length event at <paramref name="from"/>
    /// counts), all-day events by their dates as they fall in the viewer's zone; without a zone, all-day events
    /// count when they overlap in some zone (their padded bounds).
    /// </summary>
    public bool Overlaps(Instant from, Instant to, DateTimeZone? viewerZone)
    {
        if (!AllDay)
        {
            return StartUtc < to && (EndUtc > from || (EndUtc == StartUtc && StartUtc >= from));
        }

        if (viewerZone is null)
        {
            return StartUtc < to && EndUtc > from;
        }

        var start = viewerZone.AtStartOfDay(StartDate!.Value).ToInstant();
        var end = viewerZone.AtStartOfDay(EndDate!.Value).ToInstant();
        return start < to && end > from;
    }

    private static LocalDateTime Truncate(LocalDateTime value) =>
        value.With(TimeAdjusters.TruncateToSecond);

    private static (LocalDateTime Local, Instant Utc) Resolve(string field, LocalDateTime local, DateTimeZone zone, List<TimeAdjustment> adjustments)
    {
        var mapping = zone.MapLocal(local);
        var resolved = Resolvers.LenientResolver(mapping); // gap: shift forward; overlap: earlier offset
        var utc = resolved.ToInstant();
        switch (mapping.Count)
        {
            case 0:
                adjustments.Add(new TimeAdjustment(field, TimeAdjustmentKind.ShiftedForwardInGap, local, resolved.LocalDateTime, utc));
                break;
            case 2:
                adjustments.Add(new TimeAdjustment(field, TimeAdjustmentKind.AmbiguousEarlierOffset, local, resolved.LocalDateTime, utc));
                break;
        }

        return (resolved.LocalDateTime, utc);
    }
}

/// <summary>What a viewer may see of an event (permissions.md §2.1, api.md §4 "Event representation").</summary>
public static class EventVisibility
{
    /// <summary>
    /// The level the viewer effectively has: <paramref name="level"/>, except that <c>free_busy</c> viewers do not
    /// see transparent ("free") events at all (<c>none</c>: not listed, 404).
    /// </summary>
    public static EventLevel Effective(EventLevel level, EventTransparency transparency) =>
        level == EventLevel.FreeBusy && transparency == EventTransparency.Transparent ? EventLevel.None : level;

    /// <summary>Viewers below <c>read</c> get the busy projection (times, all-day flag, transparency; no details).</summary>
    public static bool IsBusyOnly(EventLevel level) => level < EventLevel.Read;
}
