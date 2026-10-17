using SCalenderPlus.Application.Feeds;
using SCalenderPlus.Core.Events;
using SCalenderPlus.Core.Permissions;
using static SCalenderPlus.Application.Tests.Feeds.FeedFixtures;

namespace SCalenderPlus.Application.Tests.Feeds;

/// <summary>
/// No stored value can add lines to a feed (ical-caldav.md §1.2): CR, LF and the other line-breaking characters are
/// dropped from every content line, whatever property writes them; and <c>RELATED-TO</c> only names events of the
/// same document by the UID they are written with there.
/// </summary>
public sealed class FeedInjectionTests
{
    private const string Injection = "\r\nATTENDEE:mailto:eve@evil.example\r\nEND:VEVENT\r\nBEGIN:VEVENT\nX-EVIL:1\u0085X-NEL:1\u2028X-LS:1";

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\u0085")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    [InlineData("\u0000")]
    [InlineData("\u007f")]
    public void A_raw_value_never_starts_a_line_of_its_own(string separator)
    {
        var output = new IcsWriter().Raw("URL", "https://x.example/a" + separator + "ATTENDEE:mailto:eve@evil.example").ToString();

        Assert.Equal("URL:https://x.example/aATTENDEE:mailto:eve@evil.example\r\n", output);
    }

    [Fact]
    public void Tzid_parameters_are_quoted_and_cleaned()
    {
        Assert.Equal("Europe/Berlin", IcsWriter.ParamValue("Europe/Berlin"));
        Assert.Equal("\"Evil:x;y\"", IcsWriter.ParamValue("Evil:x;y\""));
        Assert.Equal("DTSTART;TZID=\"A:B\":20261102T180000\r\n", new IcsWriter().Zoned("DTSTART", "A:B", Local("2026-11-02T18:00:00")).ToString());
    }

    [Fact]
    public void Stored_values_with_line_breaks_render_without_injected_lines()
    {
        var ev = Timed(1, "Training" + Injection, "2026-11-03T10:00:00", "2026-11-03T11:00:00", uid: "uid-1@x.example" + Injection);
        ev.Url = "https://x.example/a" + Injection;
        ev.Description = "Bring shoes" + Injection;
        ev.Location = "Pitch" + Injection;
        ev.Categories = ["sport" + Injection];
        ev.RelatedTo = "uid-1@x.example" + Injection;
        var series = Timed(2, "Weekly", "2026-11-03T12:00:00", "2026-11-03T13:00:00").Series("FREQ=WEEKLY;COUNT=3" + Injection);
        var busy = Timed(3, "Busy one", "2026-11-04T10:00:00", "2026-11-04T11:00:00");
        busy.CoverTitle = "Away" + Injection;

        foreach (var nativeUids in new[] { true, false })
        {
            var ics = FeedRenderer.Render(Document([ev.At(EventLevel.Read), series.At(EventLevel.Edit), busy.At(EventLevel.FreeBusy)], nativeUids: nativeUids));

            var lines = ics.Split("\r\n");
            Assert.DoesNotContain(lines, l => l.StartsWith("ATTENDEE", StringComparison.Ordinal) || l.StartsWith("X-EVIL", StringComparison.Ordinal)
                || l.StartsWith("X-NEL", StringComparison.Ordinal) || l.StartsWith("X-LS", StringComparison.Ordinal));
            Assert.Equal(3, lines.Count(l => l == "BEGIN:VEVENT"));
            Assert.Equal(3, lines.Count(l => l == "END:VEVENT"));
            Assert.DoesNotContain('\n', ics.Replace("\r\n", string.Empty, StringComparison.Ordinal));
            Assert.DoesNotContain('\r', ics.Replace("\r\n", string.Empty, StringComparison.Ordinal));
            Assert.DoesNotContain('\u0085', ics);
            Assert.DoesNotContain('\u2028', ics);
            Assert.All(lines.Where(l => l.Length > 0 && !l.StartsWith(' ')), l => Assert.Matches("^[A-Z][A-Z0-9-]*[;:]", l));
        }
    }

    [Fact]
    public void Related_to_names_only_events_of_the_same_feed_by_their_written_uid()
    {
        var original = Timed(1, "Training", "2026-11-03T10:00:00", "2026-11-03T11:00:00", uid: "imported-series@club.example");
        var split = Timed(2, "Training (new time)", "2026-11-10T11:00:00", "2026-11-10T12:00:00");
        split.RelatedTo = original.Uid;
        var orphan = Timed(3, "Hike", "2026-11-11T11:00:00", "2026-11-11T12:00:00");
        orphan.RelatedTo = "not-in-this-feed@elsewhere.example";

        // Single-calendar feed: the target keeps its stored UID, and so does the reference.
        var native = FeedRenderer.Render(Document([original.At(EventLevel.Read), split.At(EventLevel.Read), orphan.At(EventLevel.Read)]));
        Assert.Contains("RELATED-TO:imported-series@club.example\r\n", native, StringComparison.Ordinal);
        Assert.DoesNotContain("not-in-this-feed", native, StringComparison.Ordinal);

        // Multi-calendar feed: UIDs are rewritten, the reference with them; the stored UID never appears.
        var rewritten = FeedRenderer.Render(Document([original.At(EventLevel.Read), split.At(EventLevel.Read)], nativeUids: false));
        Assert.Contains($"RELATED-TO:{Id(1)}@scalenderplus\r\n", rewritten, StringComparison.Ordinal);
        Assert.DoesNotContain("imported-series", rewritten, StringComparison.Ordinal);

        // The target busy-only, absent, or in another calendar: no RELATED-TO at all.
        Assert.DoesNotContain("RELATED-TO", FeedRenderer.Render(Document([original.At(EventLevel.FreeBusy), split.At(EventLevel.Read)])), StringComparison.Ordinal);
        Assert.DoesNotContain("RELATED-TO", FeedRenderer.Render(Document([split.At(EventLevel.Read)], nativeUids: false)), StringComparison.Ordinal);
        original.CalendarId = Id(901);
        Assert.DoesNotContain("RELATED-TO", FeedRenderer.Render(Document([original.At(EventLevel.Read), split.At(EventLevel.Read)])), StringComparison.Ordinal);
    }
}
