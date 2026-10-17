using SCalenderPlus.Application.Import;

namespace SCalenderPlus.Application.Tests.Import;

public sealed class IcsReaderTests
{
    [Fact]
    public void Reads_components_properties_and_parameters()
    {
        var roots = IcsReader.Read(
            "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nDTSTART;TZID=\"W. Europe Standard Time\";X-A=1,2:20261013T093000\r\nSUMMARY;LANGUAGE=de:Wochen\r\n runde\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n",
            10,
            out var error,
            out _);

        Assert.Null(error);
        var vevent = Assert.Single(Assert.Single(roots!).Components);
        Assert.Equal("VEVENT", vevent.Name);
        var start = vevent.First("DTSTART")!;
        Assert.Equal("W. Europe Standard Time", start.Param("TZID"));
        Assert.Equal("1", start.Param("X-A"));
        Assert.Equal("20261013T093000", start.Value);
        Assert.Equal("Wochenrunde", vevent.First("SUMMARY")!.Value); // unfolded
    }

    [Fact]
    public void Accepts_bare_line_feeds_tabs_as_folding_and_blank_lines()
    {
        var roots = IcsReader.Read("begin:vcalendar\n\nBEGIN:VEVENT\nDESCRIPTION:a\n\tb\nEND:VEVENT\nEND:VCALENDAR", 10, out var error, out _);

        Assert.Null(error);
        Assert.Equal("ab", Assert.Single(Assert.Single(roots!).Components).First("DESCRIPTION")!.Value);
    }

    [Theory]
    [InlineData("Not a calendar at all")]
    [InlineData("BEGIN:VEVENT\r\nEND:VEVENT\r\n")]
    [InlineData("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nSUMMARY:x\r\n")] // truncated
    [InlineData("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nEND:VTODO\r\nEND:VCALENDAR\r\n")]
    [InlineData("BEGIN:VCALENDAR\r\nDTSTART;TZID=\"open:20261013\r\nEND:VCALENDAR\r\n")]
    [InlineData("")]
    public void Refuses_what_is_not_an_icalendar_object(string text)
    {
        Assert.Null(IcsReader.Read(text, 10, out var error, out var detail));
        Assert.Equal(IcsReadError.Invalid, error);
        Assert.False(string.IsNullOrEmpty(detail));
    }

    [Fact]
    public void Stops_at_the_event_limit()
    {
        var text = "BEGIN:VCALENDAR\r\n" + string.Concat(Enumerable.Repeat("BEGIN:VEVENT\r\nEND:VEVENT\r\n", 4)) + "END:VCALENDAR\r\n";

        Assert.NotNull(IcsReader.Read(text, 4, out _, out _));
        Assert.Null(IcsReader.Read(text, 3, out var error, out _));
        Assert.Equal(IcsReadError.TooManyEvents, error);
    }

    [Fact]
    public void Refuses_deep_nesting()
    {
        var text = "BEGIN:VCALENDAR\r\n" + string.Concat(Enumerable.Repeat("BEGIN:X\r\n", 20)) + string.Concat(Enumerable.Repeat("END:X\r\n", 20)) + "END:VCALENDAR\r\n";

        Assert.Null(IcsReader.Read(text, 10, out var error, out _));
        Assert.Equal(IcsReadError.Invalid, error);
    }

    [Theory]
    [InlineData(@"a\, b\; c\\d\ne", "a, b; c\\d\ne")]
    [InlineData(@"line\Nnext", "line\nnext")]
    [InlineData("plain", "plain")]
    public void Unescapes_text(string raw, string expected) => Assert.Equal(expected, IcsReader.UnescapeText(raw));

    [Fact]
    public void Splits_lists_on_unescaped_commas() =>
        Assert.Equal(["Board", "Club, Inc", "x"], IcsReader.SplitList(@"Board,Club\, Inc,x"));
}
