using NodaTime;
using NodaTime.Text;
using SCalenderPlus.Core.Events;
using SCalenderPlus.Core.Recurrence;

namespace SCalenderPlus.Core.Tests.Recurrence;

/// <summary>
/// Golden tests: the examples of RFC 5545 §3.8.5.3 (DTSTART in America/New_York, 09:00 local) that use the
/// supported subset, expanded by <see cref="RecurrenceSet"/>; expected dates exactly as listed in the RFC.
/// </summary>
public sealed class RecurrenceExamplesTests
{
    private const string NewYork = "America/New_York";

    public static TheoryData<string, string, string, string[]> Examples => new()
    {
        { "daily, 10 occurrences", "19970902", "FREQ=DAILY;COUNT=10", ["19970902", "19970903", "19970904", "19970905", "19970906", "19970907", "19970908", "19970909", "19970910", "19970911"] },
        { "every 10 days, 5 occurrences", "19970902", "FREQ=DAILY;INTERVAL=10;COUNT=5", ["19970902", "19970912", "19970922", "19971002", "19971012"] },
        { "weekly for 10 occurrences (across the end of DST)", "19970902", "FREQ=WEEKLY;COUNT=10", ["19970902", "19970909", "19970916", "19970923", "19970930", "19971007", "19971014", "19971021", "19971028", "19971104"] },
        {
            "every other week on Monday, Wednesday and Friday until 24 December 1997", "19970901", "FREQ=WEEKLY;INTERVAL=2;UNTIL=19971224T000000Z;WKST=SU;BYDAY=MO,WE,FR",
            ["19970901", "19970903", "19970905", "19970915", "19970917", "19970919", "19970929", "19971001", "19971003", "19971013", "19971015", "19971017", "19971027", "19971029", "19971031", "19971110", "19971112", "19971114", "19971124", "19971126", "19971128", "19971208", "19971210", "19971212", "19971222"]
        },
        { "weekly on Tuesday and Thursday for five weeks (UNTIL)", "19970902", "FREQ=WEEKLY;UNTIL=19971007T000000Z;WKST=SU;BYDAY=TU,TH", ["19970902", "19970904", "19970909", "19970911", "19970916", "19970918", "19970923", "19970925", "19970930", "19971002"] },
        { "weekly on Tuesday and Thursday for five weeks (COUNT)", "19970902", "FREQ=WEEKLY;COUNT=10;WKST=SU;BYDAY=TU,TH", ["19970902", "19970904", "19970909", "19970911", "19970916", "19970918", "19970923", "19970925", "19970930", "19971002"] },
        { "monthly on the first Friday, 10 occurrences", "19970905", "FREQ=MONTHLY;COUNT=10;BYDAY=1FR", ["19970905", "19971003", "19971107", "19971205", "19980102", "19980206", "19980306", "19980403", "19980501", "19980605"] },
        { "every other month on the first and last Sunday, 10 occurrences", "19970907", "FREQ=MONTHLY;INTERVAL=2;COUNT=10;BYDAY=1SU,-1SU", ["19970907", "19970928", "19971102", "19971130", "19980104", "19980125", "19980301", "19980329", "19980503", "19980531"] },
        { "monthly on the second-to-last Monday for 6 months", "19970922", "FREQ=MONTHLY;COUNT=6;BYDAY=-2MO", ["19970922", "19971020", "19971117", "19971222", "19980119", "19980216"] },
        { "monthly on the 2nd and 15th, 10 occurrences", "19970902", "FREQ=MONTHLY;COUNT=10;BYMONTHDAY=2,15", ["19970902", "19970915", "19971002", "19971015", "19971102", "19971115", "19971202", "19971215", "19980102", "19980115"] },
        { "monthly on the first and last day, 10 occurrences", "19970930", "FREQ=MONTHLY;COUNT=10;BYMONTHDAY=1,-1", ["19970930", "19971001", "19971031", "19971101", "19971130", "19971201", "19971231", "19980101", "19980131", "19980201"] },
        {
            "every 18 months on the 10th to 15th, 10 occurrences", "19970910", "FREQ=MONTHLY;INTERVAL=18;COUNT=10;BYMONTHDAY=10,11,12,13,14,15",
            ["19970910", "19970911", "19970912", "19970913", "19970914", "19970915", "19990310", "19990311", "19990312", "19990313"]
        },
        { "yearly in June and July, 10 occurrences", "19970610", "FREQ=YEARLY;COUNT=10;BYMONTH=6,7", ["19970610", "19970710", "19980610", "19980710", "19990610", "19990710", "20000610", "20000710", "20010610", "20010710"] },
        { "every other year in January to March, 10 occurrences", "19970310", "FREQ=YEARLY;INTERVAL=2;COUNT=10;BYMONTH=1,2,3", ["19970310", "19990110", "19990210", "19990310", "20010110", "20010210", "20010310", "20030110", "20030210", "20030310"] },
        { "monthly, invalid dates are skipped", "20070115", "FREQ=MONTHLY;BYMONTHDAY=15,30;COUNT=5", ["20070115", "20070130", "20070215", "20070315", "20070330"] },
        { "WKST=MO decides the weeks", "19970805", "FREQ=WEEKLY;INTERVAL=2;COUNT=4;BYDAY=TU,SU;WKST=MO", ["19970805", "19970810", "19970819", "19970824"] },
        { "WKST=SU decides the weeks", "19970805", "FREQ=WEEKLY;INTERVAL=2;COUNT=4;BYDAY=TU,SU;WKST=SU", ["19970805", "19970817", "19970819", "19970831"] },
        { "the third Tuesday, Wednesday or Thursday of the month, 3 occurrences", "19970904", "FREQ=MONTHLY;COUNT=3;BYDAY=TU,WE,TH;BYSETPOS=3", ["19970904", "19971007", "19971106"] },
    };

    [Theory]
    [MemberData(nameof(Examples))]
    public void Finite_examples_of_RFC_5545(string name, string start, string rule, string[] expected)
    {
        Assert.NotNull(name);
        var set = Series(start, rule);

        var dates = set.Between(Instant.FromUtc(1990, 1, 1, 0, 0), Instant.FromUtc(2010, 1, 1, 0, 0)).Items;

        Assert.Equal(expected, dates.Select(o => Format(o.RecurrenceId.Date)));
        Assert.All(dates, o => Assert.Equal(new LocalTime(9, 0), o.Times.StartLocal!.Value.TimeOfDay)); // local time kept across DST
    }

    public static TheoryData<string, string, string, string, string[]> OpenEnded => new()
    {
        { "every other day", "19970902", "FREQ=DAILY;INTERVAL=2", "19970912", ["19970902", "19970904", "19970906", "19970908", "19970910"] },
        { "every Tuesday, every other month", "19970902", "FREQ=MONTHLY;INTERVAL=2;BYDAY=TU", "19980401", ["19970902", "19970909", "19970916", "19970923", "19970930", "19971104", "19971111", "19971118", "19971125", "19980106", "19980113", "19980120", "19980127", "19980303", "19980310", "19980317", "19980324", "19980331"] },
        { "monthly on the third-to-last day", "19970928", "FREQ=MONTHLY;BYMONTHDAY=-3", "19980301", ["19970928", "19971029", "19971128", "19971229", "19980129", "19980226"] },
        { "the 20th Monday of the year", "19970519", "FREQ=YEARLY;BYDAY=20MO", "20000101", ["19970519", "19980518", "19990517"] },
        { "every Thursday in March", "19970313", "FREQ=YEARLY;BYMONTH=3;BYDAY=TH", "20000101", ["19970313", "19970320", "19970327", "19980305", "19980312", "19980319", "19980326", "19990304", "19990311", "19990318", "19990325"] },
        { "every Thursday in June, July and August", "19970605", "FREQ=YEARLY;BYDAY=TH;BYMONTH=6,7,8", "19980101", ["19970605", "19970612", "19970619", "19970626", "19970703", "19970710", "19970717", "19970724", "19970731", "19970807", "19970814", "19970821", "19970828"] },
        { "the first Saturday after the first Sunday", "19970913", "FREQ=MONTHLY;BYDAY=SA;BYMONTHDAY=7,8,9,10,11,12,13", "19980701", ["19970913", "19971011", "19971108", "19971213", "19980110", "19980207", "19980307", "19980411", "19980509", "19980613"] },
        { "US presidential election day", "19961105", "FREQ=YEARLY;INTERVAL=4;BYMONTH=11;BYDAY=TU;BYMONTHDAY=2,3,4,5,6,7,8", "20050101", ["19961105", "20001107", "20041102"] },
        { "the second-to-last weekday of the month", "19970929", "FREQ=MONTHLY;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=-2", "19980401", ["19970929", "19971030", "19971127", "19971230", "19980129", "19980226", "19980330"] },
    };

    [Theory]
    [MemberData(nameof(OpenEnded))]
    public void Open_ended_examples_of_RFC_5545(string name, string start, string rule, string before, string[] expected)
    {
        Assert.NotNull(name);
        var to = LocalDatePattern.CreateWithInvariantCulture("uuuuMMdd").Parse(before).Value.AtMidnight().InUtc().ToInstant();

        var dates = Series(start, rule).Between(Instant.FromUtc(1990, 1, 1, 0, 0), to).Items;

        Assert.Equal(expected, dates.Select(o => Format(o.RecurrenceId.Date)));
    }

    [Fact]
    public void Every_Friday_the_13th_with_the_first_date_excluded()
    {
        var set = Series("19970902", "FREQ=MONTHLY;BYDAY=FR;BYMONTHDAY=13", exdates: [new LocalDateTime(1997, 9, 2, 9, 0)]);

        var dates = set.Between(Instant.FromUtc(1997, 1, 1, 0, 0), Instant.FromUtc(2001, 1, 1, 0, 0)).Items;

        Assert.Equal(["19980213", "19980313", "19981113", "19990813", "20001013"], dates.Select(o => Format(o.RecurrenceId.Date)));
    }

    [Fact]
    public void Daily_until_a_UTC_instant_and_every_day_in_January_for_three_years()
    {
        var daily = Series("19970902", "FREQ=DAILY;UNTIL=19971224T000000Z").Between(Instant.FromUtc(1997, 1, 1, 0, 0), Instant.FromUtc(1999, 1, 1, 0, 0)).Items;
        Assert.Equal(113, daily.Count);
        Assert.Equal("19971223", Format(daily[^1].RecurrenceId.Date));

        var yearly = Series("19980101", "FREQ=YEARLY;UNTIL=20000131T140000Z;BYMONTH=1;BYDAY=SU,MO,TU,WE,TH,FR,SA").Between(Instant.FromUtc(1997, 1, 1, 0, 0), Instant.FromUtc(2002, 1, 1, 0, 0)).Items;
        var dailyInJanuary = Series("19980101", "FREQ=DAILY;UNTIL=20000131T140000Z;BYMONTH=1").Between(Instant.FromUtc(1997, 1, 1, 0, 0), Instant.FromUtc(2002, 1, 1, 0, 0)).Items;
        Assert.Equal(93, yearly.Count);
        Assert.Equal(yearly.Select(o => o.RecurrenceId), dailyInJanuary.Select(o => o.RecurrenceId));
        Assert.All(yearly, o => Assert.Equal(1, o.RecurrenceId.Month));
    }

    private static RecurrenceSet Series(string start, string rule, IReadOnlyCollection<LocalDateTime>? exdates = null)
    {
        var date = LocalDatePattern.CreateWithInvariantCulture("uuuuMMdd").Parse(start).Value;
        var zone = DateTimeZoneProviders.Tzdb[NewYork];
        var times = EventTimes.Timed(date.At(new LocalTime(9, 0)), date.At(new LocalTime(10, 0)), zone)!;
        var parsed = RecurrenceRule.Parse(rule, out var problem) ?? throw new InvalidOperationException(problem!.Message);
        var bound = parsed.ForSeries(false, zone, times.StartUtc, date, out problem) ?? throw new InvalidOperationException(problem!.Message);
        return new RecurrenceSet(times, bound, null, exdates);
    }

    private static string Format(LocalDate date) => LocalDatePattern.CreateWithInvariantCulture("uuuuMMdd").Format(date);
}
