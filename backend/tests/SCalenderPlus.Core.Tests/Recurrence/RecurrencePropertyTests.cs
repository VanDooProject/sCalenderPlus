using CsCheck;
using NodaTime;
using SCalenderPlus.Core.Events;
using SCalenderPlus.Core.Recurrence;

namespace SCalenderPlus.Core.Tests.Recurrence;

/// <summary>
/// Property tests of the expansion invariants over random rules of the supported subset (CsCheck): occurrences are
/// sorted, overlap the window, respect COUNT and EXDATE, keep the local time of day, split windows add up, the
/// arithmetic fast-forward matches a scan from the start, and every occurrence is found again by its id.
/// </summary>
public sealed class RecurrencePropertyTests
{
    private const int Iterations = 400;

    private static readonly string[] _zones = ["Europe/Berlin", "America/New_York", "Australia/Sydney", "UTC", "Asia/Kolkata"];
    private static readonly string[] _days = ["MO", "TU", "WE", "TH", "FR", "SA", "SU"];

    private static readonly Gen<string> _rule =
        from freq in Gen.OneOfConst("DAILY", "WEEKLY", "MONTHLY", "YEARLY")
        from interval in Gen.Int[1, 4]
        from byDay in Gen.OneOfConst(_days).Array[0, 3]
        from ordinal in Gen.Int[-2, 3]
        from byMonthDay in Gen.Int[-3, 31].Where(d => d != 0).Array[0, 2]
        from byMonth in Gen.Int[1, 12].Array[0, 3]
        from setPos in Gen.Int[-2, 2].Where(p => p != 0).Array[0, 1]
        from wkst in Gen.OneOfConst(_days)
        select Compose(freq, interval, byDay, ordinal, byMonthDay, byMonth, setPos, wkst);

    private static readonly Gen<Case> _case =
        from rule in _rule
        from zone in Gen.OneOfConst(_zones)
        from allDay in Gen.Bool
        from startDay in Gen.Int[0, 3650]
        from minute in Gen.Int[0, (24 * 4) - 1]
        from length in Gen.Int[0, 3 * 24 * 4]
        from count in Gen.Int[1, 40]
        from useCount in Gen.Bool
        from windowDay in Gen.Int[-30, 4000]
        from windowDays in Gen.Int[1, 396]
        from exdates in Gen.Int[0, 120].Array[0, 5]
        select Build(rule, zone, allDay, startDay, minute, length, useCount ? count : null, windowDay, windowDays, exdates);

    [Fact]
    public void Occurrences_are_sorted_within_the_window_and_unique() =>
        _case.Sample(
            c =>
            {
                var items = c.Set.Between(c.From, c.To).Items;
                return items.Zip(items.Skip(1)).All(p => p.First.Times.StartUtc <= p.Second.Times.StartUtc)
                    && items.All(o => o.Times.Overlaps(c.From, c.To, null))
                    && items.Select(o => o.RecurrenceId).Distinct().Count() == items.Count;
            },
            iter: Iterations);

    [Fact]
    public void COUNT_bounds_the_occurrences_and_EXDATEs_never_appear() =>
        _case.Sample(
            c =>
            {
                var all = c.Set.Between(c.Set.First.StartUtc - Duration.FromDays(1), Instant.FromUtc(9000, 1, 1, 0, 0), int.MaxValue).Items;
                var window = c.Set.Between(c.From, c.To).Items;
                return (c.Set.Rule.Count is not { } count || all.Count <= count)
                    && window.All(o => !c.Set.ExDates.Contains(o.RecurrenceId))
                    && (c.Set.Rule.Count is null || c.Set.LastEnd() is { } end && all.All(o => o.Times.EndUtc <= end));
            },
            iter: Iterations / 4);

    [Fact]
    public void Occurrences_keep_the_first_occurrences_time_of_day() =>
        _case.Sample(
            c => c.Set.Between(c.From, c.To).Items.All(o =>
                o.RecurrenceId.TimeOfDay == c.Set.FirstRecurrenceId.TimeOfDay
                && (o.Times.AllDay || o.Times.StartLocal == o.RecurrenceId || IsInGap(o))),
            iter: Iterations);

    [Fact]
    public void Split_windows_add_up() =>
        _case.Sample(
            c =>
            {
                var middle = c.From + ((c.To - c.From) / 2);
                var whole = c.Set.Between(c.From, c.To, int.MaxValue).Items.Select(o => o.RecurrenceId).ToHashSet();
                var halves = c.Set.Between(c.From, middle, int.MaxValue).Items.Concat(c.Set.Between(middle, c.To, int.MaxValue).Items).Select(o => o.RecurrenceId).ToHashSet();
                return whole.SetEquals(halves);
            },
            iter: Iterations);

    [Fact]
    public void Fast_forward_matches_a_scan_from_the_start() =>
        _case.Where(c => c.Set.Rule.Count is null).Sample(
            c =>
            {
                // The same rule with a COUNT it cannot reach in the window is enumerated from the first occurrence.
                var counted = new RecurrenceSet(c.Set.First, c.Set.Rule with { Count = RecurrenceRule.MaxCount }, c.Set.RDates, c.Set.ExDates);
                var fast = c.Set.Between(c.From, c.To, int.MaxValue).Items.Select(o => o.RecurrenceId).ToList();
                var slow = counted.Between(c.From, c.To, int.MaxValue).Items.Select(o => o.RecurrenceId).ToList();
                return slow.Count >= RecurrenceRule.MaxCount - 1 || fast.SequenceEqual(slow) || slow.Count == 0 && counted.LastEnd() < c.From;
            },
            iter: Iterations / 4);

    [Fact]
    public void Every_occurrence_is_found_by_its_id() =>
        _case.Sample(
            c => c.Set.Between(c.From, c.To).Items.Take(5).All(o =>
                c.Set.Find(o.RecurrenceId) == o
                && (o.Times.AllDay || c.Set.FindByStart(o.Times.StartUtc)?.RecurrenceId == o.RecurrenceId)),
            iter: Iterations);

    private static bool IsInGap(Occurrence occurrence) =>
        DateTimeZoneProviders.Tzdb[occurrence.Times.TimeZone!].MapLocal(occurrence.RecurrenceId).Count == 0;

    private static string Compose(string freq, int interval, string[] byDay, int ordinal, int[] byMonthDay, int[] byMonth, int[] setPos, string wkst)
    {
        var parts = new List<string> { "FREQ=" + freq, "INTERVAL=" + interval, "WKST=" + wkst };
        if (byDay.Length > 0)
        {
            var withOrdinal = freq is "MONTHLY" or "YEARLY" && ordinal != 0;
            parts.Add("BYDAY=" + string.Join(',', byDay.Distinct().Select((d, i) => withOrdinal && i == 0 ? ordinal + d : d)));
        }

        if (byMonthDay.Length > 0 && freq != "WEEKLY")
        {
            parts.Add("BYMONTHDAY=" + string.Join(',', byMonthDay.Distinct()));
        }

        if (byMonth.Length > 0)
        {
            parts.Add("BYMONTH=" + string.Join(',', byMonth.Distinct()));
        }

        if (setPos.Length > 0 && (byDay.Length > 0 || byMonth.Length > 0 || (byMonthDay.Length > 0 && freq != "WEEKLY")))
        {
            parts.Add("BYSETPOS=" + setPos[0]);
        }

        return string.Join(';', parts);
    }

    private static Case Build(string ruleText, string zoneId, bool allDay, int startDay, int quarter, int length, int? count, int windowDay, int windowDays, int[] exdates)
    {
        var zone = DateTimeZoneProviders.Tzdb[zoneId];
        var date = new LocalDate(2020, 1, 1).PlusDays(startDay);
        var start = date.At(LocalTime.Midnight.PlusMinutes(quarter * 15));
        var first = allDay
            ? EventTimes.AllDayEvent(date, date.PlusDays(1 + (length / 96)))!
            : EventTimes.Timed(start, start.PlusMinutes(length * 15), zone)!;
        var rule = RecurrenceRule.Parse(ruleText, out var problem) ?? throw new InvalidOperationException(ruleText + ": " + problem!.Message);
        rule = (rule with { Count = count }).ForSeries(allDay, allDay ? null : zone, first.StartUtc, date, out problem)!;
        var origin = allDay ? date.AtMidnight() : first.StartLocal!.Value;
        var set = new RecurrenceSet(first, rule, null, [.. exdates.Select(d => origin.PlusDays(d))]);
        var from = date.PlusDays(windowDay).AtMidnight().InUtc().ToInstant();
        return new Case(ruleText, set, from, from + Duration.FromDays(windowDays));
    }

    private sealed record Case(string Rule, RecurrenceSet Set, Instant From, Instant To)
    {
        public override string ToString() => $"{Rule} from {Set.First.StartLocal ?? Set.First.StartDate!.Value.AtMidnight()} {Set.First.TimeZone} count={Set.Rule.Count} window {From}–{To}";
    }
}
