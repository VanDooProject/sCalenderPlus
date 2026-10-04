using NodaTime;
using NodaTime.TimeZones;
using SCalenderPlus.Core.Events;

namespace SCalenderPlus.Core.Recurrence;

/// <summary>
/// One occurrence of a series: <see cref="RecurrenceId"/> is its nominal start as the rule generates it — the wall
/// clock in the series' zone (all-day: the date at midnight), the iCalendar RECURRENCE-ID and the key of exceptions —
/// and <see cref="Times"/> its actual times (a start in a DST gap is shifted forward like <see cref="EventTimes.Timed"/>).
/// </summary>
public sealed record Occurrence(LocalDateTime RecurrenceId, EventTimes Times);

/// <summary>Occurrences of a window, by start; <see cref="Truncated"/> when the series had more than the cap there.</summary>
public sealed record OccurrenceWindow(IReadOnlyList<Occurrence> Items, bool Truncated);

/// <summary>
/// The recurrence set of a series master (RFC 5545 §3.8.5, data-model.md §9): the first occurrence's times, the
/// <see cref="RecurrenceRule"/>, extra dates (<c>RDATE</c>) and excluded dates (<c>EXDATE</c>), all as wall clock
/// in the series' zone (all-day: dates at midnight). Occurrences keep the first occurrence's **local time of day**
/// across DST changes; their length is the first occurrence's exact duration (timed) or number of days (all-day).
/// <c>COUNT</c> counts the rule's occurrences (the first included) before <c>EXDATE</c> removes any; <c>RDATE</c>s
/// add to it (an <c>RDATE</c> equal to a rule occurrence counts once). Expansion is lazy and bounded
/// (<see cref="MaxPerWindow"/>, <see cref="RuleDates.MaxExaminedDays"/>).
/// </summary>
public sealed class RecurrenceSet
{
    /// <summary>Occurrences of one series in one window at most (api.md §4; a 13-month window of a daily series has ≈ 400).</summary>
    public const int MaxPerWindow = 1000;

    /// <summary>Most <c>RDATE</c>s of a series.</summary>
    public const int MaxRDates = 100;

    /// <summary>Most <c>EXDATE</c>s of a series.</summary>
    public const int MaxExDates = 1000;

    private readonly HashSet<LocalDateTime> _exdates;
    private readonly DateTimeZone? _zone;
    private readonly Duration _duration;
    private readonly int _days;

    public RecurrenceSet(EventTimes first, RecurrenceRule rule, IReadOnlyCollection<LocalDateTime>? rdates = null, IReadOnlyCollection<LocalDateTime>? exdates = null)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(rule);
        First = first;
        Rule = rule;
        RDates = [.. (rdates ?? []).Distinct().Order()];
        _exdates = [.. exdates ?? []];
        if (first.AllDay)
        {
            _days = Period.Between(first.StartDate!.Value, first.EndDate!.Value, PeriodUnits.Days).Days;
        }
        else
        {
            _zone = DateTimeZoneProviders.Tzdb[first.TimeZone!];
            _duration = first.EndUtc - first.StartUtc;
        }
    }

    public EventTimes First { get; }

    public RecurrenceRule Rule { get; }

    public IReadOnlyList<LocalDateTime> RDates { get; }

    public IReadOnlyCollection<LocalDateTime> ExDates => _exdates;

    /// <summary>The nominal start of the first occurrence (the series' RECURRENCE-ID origin).</summary>
    public LocalDateTime FirstRecurrenceId => First.AllDay ? First.StartDate!.Value.AtMidnight() : First.StartLocal!.Value;

    /// <summary>
    /// The occurrences whose times overlap <c>[from, to)</c> by their instants (all-day: padded bounds, see
    /// <see cref="EventTimes.Overlaps"/> without a viewer zone), by start, at most <paramref name="max"/>. With a
    /// <paramref name="budget"/> the rule stops when it is used up (truncated: occurrences may be missing).
    /// </summary>
    public OccurrenceWindow Between(Instant from, Instant to, int max = MaxPerWindow, ExpansionBudget? budget = null)
    {
        var items = new List<Occurrence>();
        var slack = First.AllDay ? Duration.FromDays(_days + 2) : _duration + Duration.FromDays(2);
        var scanFrom = (from - slack).InUtc().Date;
        foreach (var occurrence in RuleOccurrences(Rule.Count is null ? scanFrom : null, (to + Duration.FromDays(2)).InUtc().Date, budget))
        {
            if (occurrence.Times.StartUtc >= to)
            {
                break;
            }

            if (!_exdates.Contains(occurrence.RecurrenceId) && occurrence.Times.Overlaps(from, to, null))
            {
                items.Add(occurrence);
            }
        }

        // An RDATE equal to an overlapping rule occurrence is already listed (one occurrence per start).
        var known = items.Select(o => o.RecurrenceId).ToHashSet();
        foreach (var rdate in RDates.Where(r => !_exdates.Contains(r) && !known.Contains(r)))
        {
            var occurrence = At(rdate);
            if (occurrence.Times.Overlaps(from, to, null))
            {
                items.Add(occurrence);
            }
        }

        items.Sort((a, b) => a.Times.StartUtc.CompareTo(b.Times.StartUtc));
        return items.Count > max ? new OccurrenceWindow([.. items.Take(max)], true) : new OccurrenceWindow(items, budget?.IsExhausted == true);
    }

    /// <summary>The occurrence with nominal start <paramref name="recurrenceId"/>, or null when the set has none (excluded included).</summary>
    public Occurrence? Find(LocalDateTime recurrenceId)
    {
        if (_exdates.Contains(recurrenceId))
        {
            return null;
        }

        if (RDates.Contains(recurrenceId) || IsRuleOccurrence(recurrenceId))
        {
            return At(recurrenceId);
        }

        return null;
    }

    /// <summary>The occurrence that (originally) starts at <paramref name="startUtc"/> (timed series), or null.</summary>
    public Occurrence? FindByStart(Instant startUtc)
    {
        if (First.AllDay)
        {
            return null;
        }

        return Between(startUtc - Duration.FromSeconds(1), startUtc + Duration.FromSeconds(1), int.MaxValue).Items
            .FirstOrDefault(o => o.Times.StartUtc == startUtc);
    }

    /// <summary>
    /// The end of the last occurrence (excluded ones left out), or null when the series is infinite. For
    /// <c>UNTIL</c> rules the last occurrence is searched backwards from <c>UNTIL</c> in growing windows.
    /// </summary>
    public Instant? LastEnd()
    {
        if (!Rule.IsFinite)
        {
            return null;
        }

        Instant? last = null;
        if (Rule.Count is not null)
        {
            foreach (var occurrence in RuleOccurrences(null, null))
            {
                if (!_exdates.Contains(occurrence.RecurrenceId))
                {
                    last = Max(last, occurrence.Times.EndUtc);
                }
            }
        }
        else
        {
            var untilDate = Rule.UntilDate ?? Rule.UntilUtc!.Value.Plus(Duration.FromDays(1)).InUtc().Date;
            var firstDate = First.AllDay ? First.StartDate!.Value : First.StartLocal!.Value.Date;
            foreach (var lookback in (int[])[400, 4000, 40000])
            {
                var scanFrom = untilDate.PlusDays(-lookback);
                foreach (var occurrence in RuleOccurrences(scanFrom > firstDate ? scanFrom : null, untilDate))
                {
                    if (!_exdates.Contains(occurrence.RecurrenceId))
                    {
                        last = Max(last, occurrence.Times.EndUtc);
                    }
                }

                if (last is not null || scanFrom <= firstDate)
                {
                    break;
                }
            }
        }

        foreach (var rdate in RDates.Where(r => !_exdates.Contains(r)))
        {
            last = Max(last, At(rdate).Times.EndUtc);
        }

        return last ?? First.EndUtc; // everything excluded: the series ends with its (excluded) first occurrence
    }

    /// <summary>
    /// How many occurrences the rule generates before <paramref name="recurrenceId"/> (EXDATEs not applied, RDATEs
    /// not counted): the <c>COUNT</c> of the first part when a series is split there.
    /// </summary>
    public int RuleCountBefore(LocalDateTime recurrenceId)
    {
        var count = 0;
        foreach (var occurrence in RuleOccurrences(null, recurrenceId.Date))
        {
            if (occurrence.RecurrenceId >= recurrenceId)
            {
                break;
            }

            count++;
        }

        return count;
    }

    /// <summary>The occurrence that starts at <paramref name="recurrenceId"/> (whether or not the set contains it).</summary>
    public Occurrence At(LocalDateTime recurrenceId)
    {
        if (First.AllDay)
        {
            var date = recurrenceId.Date;
            return new Occurrence(date.AtMidnight(), EventTimes.AllDayEvent(date, date.PlusDays(_days))!);
        }

        var start = _zone!.ResolveLocal(recurrenceId, Resolvers.LenientResolver);
        var end = start.ToInstant() + _duration;
        return new Occurrence(
            recurrenceId,
            new EventTimes(false, start.LocalDateTime, end.InZone(_zone).LocalDateTime, null, null, _zone.Id, start.ToInstant(), end, []));
    }

    /// <summary>Whether the rule (not an RDATE) generates <paramref name="recurrenceId"/> (EXDATE not considered).</summary>
    public bool IsRuleOccurrence(LocalDateTime recurrenceId)
    {
        if (recurrenceId.TimeOfDay != FirstRecurrenceId.TimeOfDay || recurrenceId < FirstRecurrenceId)
        {
            return false;
        }

        foreach (var occurrence in RuleOccurrences(Rule.Count is null ? recurrenceId.Date : null, recurrenceId.Date))
        {
            if (occurrence.RecurrenceId >= recurrenceId)
            {
                return occurrence.RecurrenceId == recurrenceId;
            }
        }

        return false;
    }

    /// <summary>The rule's occurrences in order, with <c>COUNT</c> and <c>UNTIL</c> applied (EXDATE not applied).</summary>
    private IEnumerable<Occurrence> RuleOccurrences(LocalDate? scanFrom, LocalDate? scanTo, ExpansionBudget? budget = null)
    {
        var firstDate = FirstRecurrenceId.Date;
        var time = FirstRecurrenceId.TimeOfDay;
        var count = 0;
        foreach (var date in RuleDates.Enumerate(Rule, firstDate, scanFrom, scanTo, budget))
        {
            if (Rule.UntilDate is { } untilDate && date > untilDate)
            {
                yield break;
            }

            var occurrence = At(date + time);
            if (Rule.UntilUtc is { } untilUtc && occurrence.Times.StartUtc > untilUtc)
            {
                yield break;
            }

            yield return occurrence;
            if (Rule.Count is { } max && ++count >= max)
            {
                yield break;
            }
        }
    }

    private static Instant Max(Instant? a, Instant b) => a is { } value && value > b ? value : b;
}
