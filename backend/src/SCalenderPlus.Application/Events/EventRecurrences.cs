using NodaTime;
using NodaTime.Text;
using SCalenderPlus.Core.Events;
using SCalenderPlus.Core.Recurrence;

namespace SCalenderPlus.Application.Events;

/// <summary>
/// Recurrence of a series as the api takes it (RFC 5545 values): <see cref="Rrule"/> (the supported subset of
/// <see cref="RecurrenceRule"/>), <see cref="RDates"/> and <see cref="ExDates"/> as wall clock in the series' zone
/// (<c>2026-11-09T18:00:00</c>) for timed series and dates (<c>2026-11-09</c>) for all-day series.
/// </summary>
public sealed record EventRecurrenceInput(string? Rrule, IReadOnlyList<string>? RDates = null, IReadOnlyList<string>? ExDates = null);

/// <summary>Parsing and validation of recurrence input, and the api form of recurrence ids.</summary>
public static class EventRecurrences
{
    private static readonly LocalDateTimePattern[] _localPatterns =
    [
        LocalDateTimePattern.ExtendedIso,
        LocalDateTimePattern.CreateWithInvariantCulture("uuuu'-'MM'-'dd'T'HH':'mm"),
    ];

    private static readonly InstantPattern[] _instantPatterns =
    [
        InstantPattern.ExtendedIso,
        InstantPattern.CreateWithInvariantCulture("uuuuMMdd'T'HHmmss'Z'"),
    ];

    private static readonly LocalDatePattern[] _datePatterns =
    [
        LocalDatePattern.Iso,
        LocalDatePattern.CreateWithInvariantCulture("uuuuMMdd"),
    ];

    /// <summary>
    /// Makes <paramref name="ev"/> (its times already set) a series with <paramref name="input"/>: the rule in
    /// canonical form with <c>UNTIL</c> bound to the series (<see cref="RecurrenceRule.ForSeries"/>), RDATEs (not
    /// before the first occurrence, ≤ <see cref="RecurrenceSet.MaxRDates"/>) and EXDATEs
    /// (≤ <see cref="RecurrenceSet.MaxExDates"/>). <c>422 recurrence_invalid</c> / <c>recurrence_not_supported</c>.
    /// </summary>
    public static void Apply(Event ev, EventRecurrenceInput input)
    {
        ArgumentNullException.ThrowIfNull(ev);
        ArgumentNullException.ThrowIfNull(input);
        var rule = RecurrenceRule.Parse(input.Rrule, out var problem) ?? throw EventErrors.Recurrence(problem!);
        var rdates = Dates(input.RDates, ev.AllDay, "recurrence.rdates", RecurrenceSet.MaxRDates);
        var exdates = Dates(input.ExDates, ev.AllDay, "recurrence.exdates", RecurrenceSet.MaxExDates);
        SetRule(ev, rule);
        var first = ev.AllDay ? ev.StartDate!.Value.AtMidnight() : ev.StartLocal!.Value;
        if (rdates.Any(r => r < first))
        {
            throw EventErrors.Recurrence(new RecurrenceProblem(false, "recurrence.rdates", "RDATEs cannot be before the first occurrence (the start)."));
        }

        ev.RDates = rdates;
        ev.ExDates = exdates;
    }

    /// <summary>
    /// Binds <paramref name="rule"/> to the event's (possibly changed) times and stores it: <c>UNTIL</c> becomes a
    /// UTC instant for timed and a date for all-day series; before the first occurrence → <c>422 recurrence_invalid</c>.
    /// </summary>
    public static void SetRule(Event ev, RecurrenceRule rule)
    {
        ArgumentNullException.ThrowIfNull(ev);
        ArgumentNullException.ThrowIfNull(rule);
        var zone = ev.AllDay ? null : DateTimeZoneProviders.Tzdb[ev.TimeZone!];
        var firstDate = ev.AllDay ? ev.StartDate!.Value : ev.StartLocal!.Value.Date;
        var bound = rule.ForSeries(ev.AllDay, zone, ev.StartUtc, firstDate, out var problem) ?? throw EventErrors.Recurrence(problem!);
        ev.Rrule = bound.ToString();
    }

    /// <summary>
    /// The api form of an occurrence's recurrence id: the UTC instant of its original start for timed series
    /// (<c>2026-11-09T17:00:00Z</c>), its date for all-day series (<c>2026-11-09</c>).
    /// </summary>
    public static string Format(Occurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        return occurrence.Times.AllDay
            ? LocalDatePattern.Iso.Format(occurrence.Times.StartDate!.Value)
            : InstantPattern.ExtendedIso.Format(occurrence.Times.StartUtc);
    }

    /// <summary>
    /// The occurrence of <paramref name="series"/> with the api recurrence id <paramref name="recurrenceId"/>
    /// (also the compact iCalendar forms <c>20261109T170000Z</c> / <c>20261109</c>); <c>404</c> when the event is no
    /// series or has no such occurrence (cancelled ones only with <paramref name="includeCancelled"/>).
    /// </summary>
    public static EventOccurrence Find(Event series, string? recurrenceId, bool includeCancelled = false)
    {
        ArgumentNullException.ThrowIfNull(series);
        if (series.Recurrence() is not { } set || recurrenceId is null)
        {
            throw EventErrors.OccurrenceNotFound();
        }

        LocalDateTime? key = null;
        if (series.AllDay)
        {
            key = _datePatterns.Select(p => p.Parse(recurrenceId)).FirstOrDefault(r => r.Success)?.Value.AtMidnight();
        }
        else if (_instantPatterns.Select(p => p.Parse(recurrenceId)).FirstOrDefault(r => r.Success) is { } instant)
        {
            key = set.FindByStart(instant.Value)?.RecurrenceId;
        }

        return key is { } id && series.FindOccurrence(id, includeCancelled) is { } occurrence
            ? occurrence
            : throw EventErrors.OccurrenceNotFound();
    }

    private static List<LocalDateTime> Dates(IReadOnlyList<string>? values, bool allDay, string field, int max)
    {
        if (values is null)
        {
            return [];
        }

        if (values.Count > max)
        {
            throw EventErrors.Recurrence(new RecurrenceProblem(false, field, $"At most {max} values."));
        }

        var result = new SortedSet<LocalDateTime>();
        foreach (var value in values)
        {
            LocalDateTime? parsed = allDay
                ? _datePatterns.Select(p => p.Parse(value ?? string.Empty)).FirstOrDefault(r => r.Success)?.Value.AtMidnight()
                : _localPatterns.Select(p => p.Parse(value ?? string.Empty)).FirstOrDefault(r => r.Success)?.Value;
            if (parsed is not { } date || !EventTimes.IsSupported(date.Date))
            {
                throw EventErrors.Recurrence(new RecurrenceProblem(
                    false,
                    field,
                    allDay
                        ? "All-day series take dates like 2026-11-09."
                        : "Timed series take wall-clock times in the series' zone like 2026-11-09T18:00:00 (no offset)."));
            }

            result.Add(date);
        }

        return [.. result];
    }
}

/// <summary>Formats stored recurrence values for the api and audit.</summary>
public static class RecurrenceValues
{
    /// <summary>RDATE/EXDATE values: wall clock (timed) or dates (all-day).</summary>
    public static IReadOnlyList<string> Format(IEnumerable<LocalDateTime> values, bool allDay) =>
        [.. values.Select(v => allDay ? LocalDatePattern.Iso.Format(v.Date) : LocalDateTimePattern.ExtendedIso.Format(v))];

    /// <summary>Re-binds the stored rule to the event's current times (after a change of kind or zone).</summary>
    internal static void Rebind(Event ev)
    {
        if (ev.Rrule is not null)
        {
            EventRecurrences.SetRule(ev, RecurrenceRule.Parse(ev.Rrule, out _)!);
        }
    }

    /// <summary>The audit form of a series' recurrence: rule, RDATEs and EXDATEs; null for single events.</summary>
    internal static string? Audit(Event ev) =>
        ev.Rrule is null
            ? null
            : string.Join(" ", new[] { "RRULE:" + ev.Rrule }
                .Concat(ev.RDates.Count > 0 ? ["RDATE:" + string.Join(',', Format(ev.RDates, ev.AllDay))] : [])
                .Concat(ev.ExDates.Count > 0 ? ["EXDATE:" + string.Join(',', Format(ev.ExDates, ev.AllDay))] : []));
}
