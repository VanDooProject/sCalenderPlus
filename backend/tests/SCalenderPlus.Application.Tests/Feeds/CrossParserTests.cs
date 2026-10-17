using System.Text;
using Ical.Net.DataTypes;
using NodaTime;
using SCalenderPlus.Application.Feeds;
using IcalCalendar = Ical.Net.Calendar;

namespace SCalenderPlus.Application.Tests.Feeds;

/// <summary>
/// Cross-parser validation (ical-caldav.md §1.5): every golden file must parse with Ical.Net — a second, independent
/// iCalendar implementation — with the same events, times and texts, and every generated <c>VTIMEZONE</c>, evaluated
/// by Ical.Net's recurrence engine, must produce exactly the transitions of the bundled tzdb. (The frontend suite
/// parses the same files with ical.js.) CI fails on unparsable output.
/// </summary>
public sealed class CrossParserTests
{
    public static TheoryData<string> GoldenFiles => [.. GoldenFile.All().Select(f => Path.GetFileName(f))];

    [Fact]
    public void There_are_golden_files() => Assert.True(GoldenFiles.Count >= 10, "golden files missing");

    [Theory]
    [MemberData(nameof(GoldenFiles))]
    public void Golden_file_parses_with_ical_net(string name)
    {
        var text = File.ReadAllText(Path.Combine(GoldenFile.Directory(), name), Encoding.UTF8);

        var calendar = IcalCalendar.Load(text);

        Assert.NotNull(calendar);
        Assert.Equal("2.0", calendar.Version);
        Assert.Equal(FeedRenderer.ProductId, calendar.ProductId);
        Assert.Equal(Count(text, "BEGIN:VEVENT"), calendar.Events.Count);
        Assert.Equal(Count(text, "BEGIN:VTIMEZONE"), calendar.TimeZones.Count);
        foreach (var ev in calendar.Events)
        {
            Assert.False(string.IsNullOrEmpty(ev.Uid));
            Assert.NotNull(ev.DtStart);
            Assert.NotNull(ev.DtEnd);
            Assert.False(string.IsNullOrEmpty(ev.Summary));
            Assert.True(ev.DtEnd! >= ev.DtStart!, $"{name}: {ev.Uid} ends before it starts");
            if (ev.DtStart!.TzId is { } tzid && !ev.DtStart.IsUtc)
            {
                // Ical.Net resolves the TZID itself: the instant must be the one our wall clock + zone means.
                var zone = DateTimeZoneProviders.Tzdb[tzid];
                var local = new LocalDateTime(ev.DtStart.Year, ev.DtStart.Month, ev.DtStart.Day, ev.DtStart.Hour, ev.DtStart.Minute, ev.DtStart.Second);
                Assert.Equal(zone.AtLeniently(local).ToInstant().ToDateTimeUtc(), ev.DtStart.AsUtc);
            }

            if (ev.RecurrenceRule is not null)
            {
                Assert.NotEmpty(ev.GetOccurrences(ev.DtStart).Take(3)); // the rule evaluates
            }
        }
    }

    /// <remarks>
    /// The fixture avoids a backslash right before an <c>n</c>: we escape it as <c>\\n</c> (RFC 5545), which Ical.Net 5.2
    /// decodes wrongly as backslash + newline (it unescapes <c>\n</c> first); ical.js decodes it correctly.
    /// </remarks>
    [Fact]
    public void Texts_survive_escaping_and_folding()
    {
        var text = File.ReadAllText(Path.Combine(GoldenFile.Directory(), "read.en.ics"), Encoding.UTF8);

        var calendar = IcalCalendar.Load(text);

        var board = calendar!.Events.Single(e => e.Uid == "board-1@club.example");
        Assert.Equal("🔒 Board meeting", board.Summary);
        Assert.StartsWith("Agenda:\n1. Budget; 2. Plans, misc \\ notes\n\n—\n🔒 Read-only for you · View in sCalenderPlus: https://app.example.test/e/", board.Description, StringComparison.Ordinal);
        Assert.Equal("Club house, room 2", board.Location);
        Assert.Equal(["board", "finance"], board.Categories);
        var long_ = calendar.Events.Single(e => e.Summary!.Contains("Jahreshauptversammlung", StringComparison.Ordinal));
        Assert.Equal("🔒 Jahreshauptversammlung mit Wahlen, Berichten und Ehrungen – bitte pünktlich 🎉 (Vereinsheim, großer Saal)", long_.Summary);
    }

    [Theory]
    [InlineData("Europe/Berlin")]
    [InlineData("America/New_York")]
    [InlineData("Australia/Sydney")]
    [InlineData("Asia/Kolkata")]
    [InlineData("Africa/Casablanca")]
    [InlineData("America/Sao_Paulo")]
    [InlineData("Pacific/Chatham")]
    [InlineData("UTC")]
    public void Generated_vtimezone_reproduces_the_tzdb_transitions(string zoneId)
    {
        var zone = DateTimeZoneProviders.Tzdb[zoneId];
        var earliest = Instant.FromUtc(2023, 3, 1, 0, 0);
        var writer = new IcsWriter();
        writer.Begin("VCALENDAR").Raw("VERSION", "2.0").Raw("PRODID", FeedRenderer.ProductId);
        VTimeZoneBuilder.Write(writer, zone, earliest, FeedFixtures.Now);
        writer.End("VCALENDAR");

        var vtimezone = Assert.Single(IcalCalendar.Load(writer.ToString())!.TimeZones);

        // Every onset of every observance (DTSTART, RDATE, RRULE), as an instant with its offsets.
        var end = Instant.FromUtc(2036, 1, 1, 0, 0);
        var onsets = new SortedDictionary<Instant, (Offset From, Offset To)>();
        foreach (var observance in vtimezone.TimeZoneInfos)
        {
            var from = Offset.FromTimeSpan(observance.OffsetFrom!.Offset);
            var to = Offset.FromTimeSpan(observance.OffsetTo!.Offset);
            foreach (var occurrence in observance.GetOccurrences(observance.DtStart!))
            {
                var start = occurrence.Period!.StartTime!;
                var instant = new LocalDateTime(start.Year, start.Month, start.Day, start.Hour, start.Minute, start.Second).WithOffset(from).ToInstant();
                if (instant >= end)
                {
                    break;
                }

                onsets[instant] = (from, to);
            }
        }

        // The tzdb's view: the offset in effect at every instant from the earliest time on.
        var expected = Transitions(zone, earliest, end);
        var covered = onsets.Where(o => o.Key > earliest).ToDictionary(o => o.Key, o => o.Value);
        Assert.Equal(expected, covered);

        // And the observance in effect at the earliest time has the right offset.
        var initial = onsets.Last(o => o.Key <= earliest).Value.To;
        Assert.Equal(zone.GetUtcOffset(earliest), initial);
    }

    private static Dictionary<Instant, (Offset From, Offset To)> Transitions(DateTimeZone zone, Instant from, Instant to)
    {
        var transitions = new Dictionary<Instant, (Offset, Offset)>();
        Offset? previous = null;
        foreach (var interval in zone.GetZoneIntervals(new Interval(from, to)))
        {
            if (previous is { } before && interval.HasStart && interval.Start > from && before != interval.WallOffset)
            {
                transitions[interval.Start] = (before, interval.WallOffset);
            }

            previous = interval.WallOffset;
        }

        return transitions;
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
