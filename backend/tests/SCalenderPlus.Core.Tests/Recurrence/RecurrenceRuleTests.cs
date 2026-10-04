using NodaTime;
using SCalenderPlus.Core.Recurrence;

namespace SCalenderPlus.Core.Tests.Recurrence;

/// <summary>Parsing, validation and canonical form of the supported RRULE subset.</summary>
public sealed class RecurrenceRuleTests
{
    [Theory]
    [InlineData("FREQ=WEEKLY", "FREQ=WEEKLY")]
    [InlineData("rrule:freq=weekly;byday=fr,mo", "FREQ=WEEKLY;BYDAY=MO,FR")]
    [InlineData("FREQ=MONTHLY;INTERVAL=1;BYDAY=-1FR,+2MO", "FREQ=MONTHLY;BYDAY=2MO,-1FR")]
    [InlineData("FREQ=YEARLY;BYMONTH=7,1;BYMONTHDAY=-1,15;COUNT=3", "FREQ=YEARLY;COUNT=3;BYMONTH=1,7;BYMONTHDAY=-1,15")]
    [InlineData("FREQ=MONTHLY;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=-1,1;WKST=SU", "FREQ=MONTHLY;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=-1,1;WKST=SU")]
    [InlineData("FREQ=DAILY;INTERVAL=3;UNTIL=20261231T235959Z", "FREQ=DAILY;INTERVAL=3;UNTIL=20261231T235959Z")]
    [InlineData("FREQ=DAILY;UNTIL=20261231", "FREQ=DAILY;UNTIL=20261231")]
    [InlineData("FREQ=DAILY;UNTIL=20261231T180000", "FREQ=DAILY;UNTIL=20261231T180000")]
    [InlineData(" FREQ=WEEKLY ; WKST=MO ", "FREQ=WEEKLY")]
    public void Parses_into_the_canonical_form(string text, string canonical)
    {
        var rule = RecurrenceRule.Parse(text, out var problem);

        Assert.Null(problem);
        Assert.Equal(canonical, rule!.ToString());
        Assert.Equal(canonical, RecurrenceRule.Parse(canonical, out _)!.ToString()); // stable
    }

    [Theory]
    [InlineData("FREQ=HOURLY")]
    [InlineData("FREQ=MINUTELY")]
    [InlineData("FREQ=SECONDLY")]
    [InlineData("FREQ=DAILY;BYHOUR=9")]
    [InlineData("FREQ=DAILY;BYMINUTE=30")]
    [InlineData("FREQ=DAILY;BYSECOND=1")]
    [InlineData("FREQ=YEARLY;BYYEARDAY=100")]
    [InlineData("FREQ=YEARLY;BYWEEKNO=20")]
    [InlineData("FREQ=YEARLY;RSCALE=CHINESE")]
    [InlineData("FREQ=MONTHLY;SKIP=FORWARD")]
    [InlineData("FREQ=WEEKLY;X-NAME=1")]
    public void Valid_but_unsupported_parts_are_not_supported(string text)
    {
        Assert.Null(RecurrenceRule.Parse(text, out var problem));
        Assert.True(problem!.NotSupported, problem.Message);
        Assert.Equal("recurrence.rrule", problem.Field);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("WEEKLY")]
    [InlineData("FREQ=")]
    [InlineData("=WEEKLY")]
    [InlineData("INTERVAL=2")]
    [InlineData("FREQ=FORTNIGHTLY")]
    [InlineData("FREQ=WEEKLY;FREQ=DAILY")]
    [InlineData("FREQ=WEEKLY;FOO=1")]
    [InlineData("FREQ=WEEKLY;INTERVAL=0")]
    [InlineData("FREQ=WEEKLY;INTERVAL=1001")]
    [InlineData("FREQ=WEEKLY;INTERVAL=-1")]
    [InlineData("FREQ=WEEKLY;COUNT=0")]
    [InlineData("FREQ=WEEKLY;COUNT=5001")]
    [InlineData("FREQ=WEEKLY;COUNT=2;UNTIL=20261231")]
    [InlineData("FREQ=WEEKLY;UNTIL=2026-12-31")]
    [InlineData("FREQ=WEEKLY;BYMONTH=13")]
    [InlineData("FREQ=WEEKLY;BYMONTH=-1")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=0")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=32")]
    [InlineData("FREQ=WEEKLY;BYMONTHDAY=1")]
    [InlineData("FREQ=WEEKLY;BYDAY=XX")]
    [InlineData("FREQ=WEEKLY;BYDAY=M")]
    [InlineData("FREQ=WEEKLY;BYDAY=0MO")]
    [InlineData("FREQ=WEEKLY;BYDAY=1MO")]
    [InlineData("FREQ=DAILY;BYDAY=-1FR")]
    [InlineData("FREQ=MONTHLY;BYDAY=6MO")]
    [InlineData("FREQ=YEARLY;BYMONTH=3;BYDAY=6MO")]
    [InlineData("FREQ=YEARLY;BYDAY=54MO")]
    [InlineData("FREQ=MONTHLY;BYSETPOS=1")]
    [InlineData("FREQ=MONTHLY;BYDAY=MO;BYSETPOS=367")]
    [InlineData("FREQ=WEEKLY;WKST=XX")]
    public void Malformed_rules_are_invalid(string? text)
    {
        Assert.Null(RecurrenceRule.Parse(text, out var problem));
        Assert.False(problem!.NotSupported, problem.Message);
    }

    [Fact]
    public void Overly_long_rules_are_invalid() =>
        Assert.Null(RecurrenceRule.Parse("FREQ=WEEKLY;BYDAY=" + string.Join(',', Enumerable.Repeat("MO", 200)), out _));

    [Fact]
    public void Yearly_ordinals_reach_53_without_BYMONTH()
    {
        Assert.NotNull(RecurrenceRule.Parse("FREQ=YEARLY;BYDAY=53MO,-53FR", out _));
        Assert.True(RecurrenceRule.Parse("FREQ=DAILY", out _)!.IsFinite is false);
        Assert.True(RecurrenceRule.Parse("FREQ=DAILY;COUNT=2", out _)!.IsFinite);
    }

    [Fact]
    public void Until_binds_to_the_series()
    {
        var berlin = DateTimeZoneProviders.Tzdb["Europe/Berlin"];
        var start = Instant.FromUtc(2026, 11, 2, 17, 0);
        var date = new LocalDate(2026, 11, 2);

        // Timed: a date means "through that day" in the series' zone; a floating time is read in the zone.
        var byDate = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20261130", out _)!.ForSeries(false, berlin, start, date, out _)!;
        Assert.Equal(Instant.FromUtc(2026, 11, 30, 22, 59, 59), byDate.UntilUtc);
        Assert.Equal("FREQ=DAILY;UNTIL=20261130T225959Z", byDate.ToString());
        var byLocal = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20261130T180000", out _)!.ForSeries(false, berlin, start, date, out _)!;
        Assert.Equal(Instant.FromUtc(2026, 11, 30, 17, 0), byLocal.UntilUtc);
        var byUtc = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20261130T170000Z", out _)!.ForSeries(false, berlin, start, date, out _)!;
        Assert.Equal(Instant.FromUtc(2026, 11, 30, 17, 0), byUtc.UntilUtc);

        // All-day: dates (a date-time is cut to its date).
        Assert.Equal(new LocalDate(2026, 11, 30), RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20261130T230000Z", out _)!.ForSeries(true, null, start, date, out _)!.UntilDate);
        Assert.Equal(new LocalDate(2026, 11, 30), RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20261130T120000", out _)!.ForSeries(true, null, start, date, out _)!.UntilDate);
        Assert.Equal("FREQ=DAILY;UNTIL=20261130", RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20261130", out _)!.ForSeries(true, null, start, date, out _)!.ToString());

        // Without UNTIL nothing changes; UNTIL before the start is refused.
        var open = RecurrenceRule.Parse("FREQ=DAILY", out _)!;
        Assert.Same(open, open.ForSeries(false, berlin, start, date, out _));
        Assert.Null(RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20261101", out _)!.ForSeries(false, berlin, start, date, out var timedProblem));
        Assert.False(timedProblem!.NotSupported);
        Assert.Null(RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20261101", out _)!.ForSeries(true, null, start, date, out var allDayProblem));
        Assert.NotNull(allDayProblem);
    }

    [Fact]
    public void Weekday_entries_format_like_iCalendar()
    {
        Assert.Equal("MO", new WeekdayEntry(0, IsoDayOfWeek.Monday).ToString());
        Assert.Equal("-1SU", new WeekdayEntry(-1, IsoDayOfWeek.Sunday).ToString());
        Assert.Equal("SA", RecurrenceRule.DayCode(IsoDayOfWeek.Saturday));
    }
}
