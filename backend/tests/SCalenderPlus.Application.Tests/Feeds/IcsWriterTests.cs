using System.Text;
using NodaTime;
using SCalenderPlus.Application.Feeds;

namespace SCalenderPlus.Application.Tests.Feeds;

public sealed class IcsWriterTests
{
    [Fact]
    public void Text_values_are_escaped_and_control_characters_dropped()
    {
        Assert.Equal(@"a\\b\;c\,d\ne" + "\tf", IcsWriter.EscapeText("a\\b;c,d\r\ne\tf\u0007"));
    }

    [Fact]
    public void Lines_are_folded_at_75_octets_without_splitting_utf8_sequences()
    {
        var value = string.Concat(Enumerable.Repeat("ä🎉x", 40));

        var output = new IcsWriter().Text("SUMMARY", value).ToString();

        var lines = output.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length > 1);
        Assert.All(lines, line => Assert.True(Encoding.UTF8.GetByteCount(line) <= 75, $"{Encoding.UTF8.GetByteCount(line)} octets: {line}"));
        Assert.All(lines.Skip(1), line => Assert.StartsWith(" ", line, StringComparison.Ordinal));
        Assert.DoesNotContain('�', output);
        var unfolded = output.Replace("\r\n ", string.Empty, StringComparison.Ordinal);
        Assert.Equal("SUMMARY:" + value + "\r\n", unfolded);
    }

    [Theory]
    [InlineData(3600, "+0100")]
    [InlineData(-18000, "-0500")]
    [InlineData(20700, "+0545")]
    [InlineData(-2670, "-004430")]
    [InlineData(0, "+0000")]
    public void Offsets_have_hours_minutes_and_seconds_when_needed(int seconds, string expected) =>
        Assert.Equal(expected, IcsWriter.FormatOffset(Offset.FromSeconds(seconds)));

    [Fact]
    public void Date_times_are_formatted_as_rfc5545_values()
    {
        var output = new IcsWriter()
            .Zoned("DTSTART", "Europe/Berlin", new LocalDateTime(2026, 11, 2, 18, 0))
            .Date("DTEND", new LocalDate(2026, 11, 3))
            .Utc("DTSTAMP", Instant.FromUtc(2026, 10, 1, 9, 30, 5))
            .ToString();

        Assert.Equal("DTSTART;TZID=Europe/Berlin:20261102T180000\r\nDTEND;VALUE=DATE:20261103\r\nDTSTAMP:20261001T093005Z\r\n", output);
    }
}
