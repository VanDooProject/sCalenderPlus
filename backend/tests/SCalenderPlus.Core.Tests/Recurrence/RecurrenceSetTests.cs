using NodaTime;
using SCalenderPlus.Core.Events;
using SCalenderPlus.Core.Recurrence;

namespace SCalenderPlus.Core.Tests.Recurrence;

/// <summary>Series expansion: DST, all-day, RDATE/EXDATE, series bounds, lookups and caps (data-model.md §9, §10).</summary>
public sealed class RecurrenceSetTests
{
    private static readonly DateTimeZone _berlin = DateTimeZoneProviders.Tzdb["Europe/Berlin"];

    [Fact]
    public void A_weekly_event_keeps_its_local_time_across_DST()
    {
        // Mondays 18:00–20:00 Berlin from 19 Oct 2026; summer time ends on 25 Oct 2026.
        var set = Timed("2026-10-19T18:00", "2026-10-19T20:00", "FREQ=WEEKLY;COUNT=4");

        var items = set.Between(Instant.FromUtc(2026, 10, 1, 0, 0), Instant.FromUtc(2026, 12, 1, 0, 0)).Items;

        Assert.Equal(4, items.Count);
        Assert.All(items, o => Assert.Equal(new LocalTime(18, 0), o.Times.StartLocal!.Value.TimeOfDay));
        Assert.All(items, o => Assert.Equal(new LocalTime(20, 0), o.Times.EndLocal!.Value.TimeOfDay));
        Assert.Equal(
            [Instant.FromUtc(2026, 10, 19, 16, 0), Instant.FromUtc(2026, 10, 26, 17, 0), Instant.FromUtc(2026, 11, 2, 17, 0), Instant.FromUtc(2026, 11, 9, 17, 0)],
            items.Select(o => o.Times.StartUtc));
        Assert.Equal(Instant.FromUtc(2026, 11, 9, 19, 0), set.LastEnd());
    }

    [Fact]
    public void Occurrences_in_a_DST_gap_shift_forward_and_keep_the_exact_duration()
    {
        // Daily 02:30–03:30; on 29 Mar 2026 02:30 does not exist in Berlin.
        var set = Timed("2026-03-27T02:30", "2026-03-27T03:30", "FREQ=DAILY;COUNT=4");

        var items = set.Between(Instant.FromUtc(2026, 3, 1, 0, 0), Instant.FromUtc(2026, 4, 1, 0, 0)).Items;

        var gap = items[2];
        Assert.Equal(new LocalDateTime(2026, 3, 29, 2, 30), gap.RecurrenceId); // the key stays the nominal time
        Assert.Equal(new LocalDateTime(2026, 3, 29, 3, 30), gap.Times.StartLocal);
        Assert.Equal(new LocalDateTime(2026, 3, 29, 4, 30), gap.Times.EndLocal);
        Assert.Equal(Duration.FromHours(1), gap.Times.EndUtc - gap.Times.StartUtc);
        Assert.Equal(gap.RecurrenceId, set.FindByStart(gap.Times.StartUtc)!.RecurrenceId);
        Assert.Equal(new LocalTime(2, 30), items[3].Times.StartLocal!.Value.TimeOfDay);
    }

    [Fact]
    public void All_day_series_repeat_dates_with_their_length()
    {
        var first = EventTimes.AllDayEvent(new LocalDate(2026, 12, 24), new LocalDate(2026, 12, 26))!;
        var set = new RecurrenceSet(first, Rule("FREQ=YEARLY;UNTIL=20281224", allDay: true, first));

        var items = set.Between(Instant.FromUtc(2026, 1, 1, 0, 0), Instant.FromUtc(2030, 1, 1, 0, 0)).Items;

        Assert.Equal([new LocalDate(2026, 12, 24), new LocalDate(2027, 12, 24), new LocalDate(2028, 12, 24)], items.Select(o => o.Times.StartDate!.Value));
        Assert.All(items, o => Assert.Equal(o.Times.StartDate!.Value.PlusDays(2), o.Times.EndDate));
        Assert.All(items, o => Assert.Equal(o.Times.StartDate!.Value.AtMidnight(), o.RecurrenceId));
        Assert.Equal(EventTimes.PaddedEnd(new LocalDate(2028, 12, 26)), set.LastEnd());
        Assert.Null(set.FindByStart(items[0].Times.StartUtc)); // all-day occurrences are found by date
        Assert.NotNull(set.Find(new LocalDate(2027, 12, 24).AtMidnight()));
        Assert.Null(set.Find(new LocalDate(2027, 12, 25).AtMidnight()));
    }

    [Fact]
    public void RDATEs_add_and_EXDATEs_remove_occurrences()
    {
        var first = Times("2026-11-02T18:00", "2026-11-02T19:00");
        var set = new RecurrenceSet(
            first,
            Rule("FREQ=WEEKLY;COUNT=3", false, first),
            rdates: [new LocalDateTime(2026, 11, 4, 12, 0), new LocalDateTime(2026, 11, 9, 18, 0), new LocalDateTime(2026, 12, 25, 10, 0)],
            exdates: [new LocalDateTime(2026, 11, 16, 18, 0), new LocalDateTime(2026, 12, 25, 10, 0)]);

        var items = set.Between(Instant.FromUtc(2026, 11, 1, 0, 0), Instant.FromUtc(2027, 1, 1, 0, 0)).Items;

        // 2 Nov, the RDATE 4 Nov 12:00, 9 Nov (rule and RDATE: once); 16 Nov and the RDATE 25 Dec are excluded.
        Assert.Equal(
            [new LocalDateTime(2026, 11, 2, 18, 0), new LocalDateTime(2026, 11, 4, 12, 0), new LocalDateTime(2026, 11, 9, 18, 0)],
            items.Select(o => o.RecurrenceId));
        Assert.Equal(Instant.FromUtc(2026, 11, 9, 18, 0), set.LastEnd());
        Assert.NotNull(set.Find(new LocalDateTime(2026, 11, 4, 12, 0)));
        Assert.Null(set.Find(new LocalDateTime(2026, 11, 16, 18, 0)));
        Assert.Null(set.Find(new LocalDateTime(2026, 11, 23, 18, 0))); // beyond COUNT
        Assert.Null(set.Find(new LocalDateTime(2026, 11, 3, 18, 0))); // not a Monday
        Assert.Null(set.Find(new LocalDateTime(2026, 10, 26, 18, 0))); // before the first
        Assert.Null(set.Find(new LocalDateTime(2026, 11, 9, 9, 0))); // another time of day
        Assert.Equal(2, set.RDates.Count(r => r.Month == 11));
        Assert.Equal(2, set.ExDates.Count);
        Assert.Equal(new LocalDateTime(2026, 11, 2, 18, 0), set.FirstRecurrenceId);
    }

    [Fact]
    public void Series_bounds_follow_COUNT_UNTIL_and_RDATEs()
    {
        var first = Times("2026-11-02T18:00", "2026-11-02T19:00");
        Assert.Null(new RecurrenceSet(first, Rule("FREQ=DAILY", false, first)).LastEnd());
        Assert.Equal(Instant.FromUtc(2026, 11, 11, 18, 0), new RecurrenceSet(first, Rule("FREQ=DAILY;COUNT=10", false, first)).LastEnd());
        Assert.Equal(Instant.FromUtc(2026, 11, 30, 18, 0), new RecurrenceSet(first, Rule("FREQ=DAILY;UNTIL=20261130", false, first)).LastEnd());
        Assert.Equal(Instant.FromUtc(2036, 11, 2, 18, 0), new RecurrenceSet(first, Rule("FREQ=YEARLY;UNTIL=20361231", false, first)).LastEnd());

        // An RDATE after the rule's end extends the series; a fully excluded series ends with its first occurrence.
        Assert.Equal(Instant.FromUtc(2027, 1, 1, 11, 0), new RecurrenceSet(first, Rule("FREQ=DAILY;COUNT=2", false, first), [new LocalDateTime(2027, 1, 1, 11, 0)]).LastEnd());
        Assert.Equal(first.EndUtc, new RecurrenceSet(first, Rule("FREQ=DAILY;COUNT=1", false, first), exdates: [new LocalDateTime(2026, 11, 2, 18, 0)]).LastEnd());

        // A rare rule whose UNTIL is decades away is searched backwards in growing windows (29 Feb).
        var leap = Times("2028-02-29T09:00", "2028-02-29T10:00");
        Assert.Equal(Instant.FromUtc(2096, 2, 29, 9, 0), new RecurrenceSet(leap, Rule("FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=29;UNTIL=20990101", false, leap)).LastEnd());
        Assert.Equal(leap.EndUtc, new RecurrenceSet(leap, Rule("FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=29;UNTIL=20290101", false, leap)).LastEnd());
    }

    [Fact]
    public void Windows_far_from_the_start_are_reached_without_scanning_from_the_start()
    {
        var first = Times("1900-01-01T08:00", "1900-01-01T09:00");
        var set = new RecurrenceSet(first, Rule("FREQ=DAILY", false, first));

        var items = set.Between(Instant.FromUtc(9000, 1, 1, 0, 0), Instant.FromUtc(9000, 1, 8, 0, 0)).Items;

        Assert.Equal(7, items.Count);
        Assert.Equal(new LocalDate(9000, 1, 1), items[0].RecurrenceId.Date);
        Assert.Empty(set.Between(Instant.FromUtc(1800, 1, 1, 0, 0), Instant.FromUtc(1899, 1, 1, 0, 0)).Items);
    }

    [Fact]
    public void Rules_that_never_match_terminate()
    {
        var first = Times("2026-01-01T08:00", "2026-01-01T09:00");
        var set = new RecurrenceSet(first, Rule("FREQ=MONTHLY;BYMONTH=2;BYMONTHDAY=30", false, first));
        Assert.Single(set.Between(Instant.FromUtc(2026, 1, 1, 0, 0), Instant.FromUtc(2027, 1, 1, 0, 0)).Items); // the first only

        var daily = new RecurrenceSet(first, Rule("FREQ=DAILY;COUNT=3;BYMONTH=2;BYMONTHDAY=30", false, first));
        Assert.Equal(first.EndUtc, daily.LastEnd()); // gives up after the scan budget: the first occurrence only
        Assert.Single(daily.Between(Instant.FromUtc(2026, 1, 1, 0, 0), Instant.FromUtc(2027, 1, 1, 0, 0)).Items);

        var yearly = new RecurrenceSet(first, Rule("FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=30;BYSETPOS=1", false, first));
        Assert.Null(yearly.LastEnd());
        Assert.Empty(yearly.Between(Instant.FromUtc(9990, 1, 1, 0, 0), Instant.FromUtc(9998, 12, 1, 0, 0)).Items);
    }

    [Fact]
    public void Expansion_is_capped_per_window()
    {
        var first = Times("2026-01-01T08:00", "2026-01-01T09:00");
        var set = new RecurrenceSet(first, Rule("FREQ=DAILY", false, first));

        var window = set.Between(Instant.FromUtc(2026, 1, 1, 0, 0), Instant.FromUtc(2027, 1, 1, 0, 0), max: 100);

        Assert.True(window.Truncated);
        Assert.Equal(100, window.Items.Count);
        Assert.False(set.Between(Instant.FromUtc(2026, 1, 1, 0, 0), Instant.FromUtc(2027, 1, 1, 0, 0)).Truncated);
    }

    [Fact]
    public void Long_events_overlapping_the_window_start_are_found()
    {
        // Weekly three-day events: the one starting 30 Oct overlaps a window from 1 Nov.
        var first = Times("2026-10-23T09:00", "2026-10-26T09:00");
        var set = new RecurrenceSet(first, Rule("FREQ=WEEKLY", false, first));

        var items = set.Between(Instant.FromUtc(2026, 11, 1, 0, 0), Instant.FromUtc(2026, 11, 2, 0, 0)).Items;

        Assert.Equal(new LocalDateTime(2026, 10, 30, 9, 0), Assert.Single(items).RecurrenceId);
    }

    internal static RecurrenceSet Timed(string start, string end, string rule)
    {
        var first = Times(start, end);
        return new RecurrenceSet(first, Rule(rule, false, first));
    }

    internal static EventTimes Times(string start, string end) =>
        EventTimes.Timed(LocalDateTime.FromDateTime(DateTime.Parse(start, System.Globalization.CultureInfo.InvariantCulture)), LocalDateTime.FromDateTime(DateTime.Parse(end, System.Globalization.CultureInfo.InvariantCulture)), _berlin)!;

    internal static RecurrenceRule Rule(string text, bool allDay, EventTimes first)
    {
        var rule = RecurrenceRule.Parse(text, out var problem) ?? throw new InvalidOperationException(problem!.Message);
        var date = first.AllDay ? first.StartDate!.Value : first.StartLocal!.Value.Date;
        return rule.ForSeries(allDay, allDay ? null : _berlin, first.StartUtc, date, out problem) ?? throw new InvalidOperationException(problem!.Message);
    }
}
