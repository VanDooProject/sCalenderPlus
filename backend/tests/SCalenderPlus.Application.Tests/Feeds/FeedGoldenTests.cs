using SCalenderPlus.Application.Feeds;
using SCalenderPlus.Core.Events;
using SCalenderPlus.Core.Feeds;
using SCalenderPlus.Core.Permissions;
using static SCalenderPlus.Application.Tests.Feeds.FeedFixtures;

namespace SCalenderPlus.Application.Tests.Feeds;

/// <summary>
/// Golden files of the feed projection (permissions.md §7, ical-caldav.md §1.3): one per projection rule — busy
/// (cover titles, stripped exceptions, opaque UIDs), read, edit, every label mode, en/de, multi-calendar UIDs, share
/// links and time zones. The same files are parsed by Ical.Net (<see cref="CrossParserTests"/>) and by ical.js
/// (frontend/packages/api-client/src/__tests__/icalGolden.spec.ts).
/// </summary>
public sealed class FeedGoldenTests
{
    /// <summary>"Therapy" with cover title, an imported UID and details busy viewers must never see.</summary>
    private static Event Therapy()
    {
        var ev = Timed(1, "Therapy", "2026-11-03T10:00:00", "2026-11-03T11:00:00", uid: "therapy-42@clinic.example");
        ev.CoverTitle = "Appointment";
        ev.Description = "Room 4, Dr. Smith";
        ev.Location = "Clinic";
        ev.Url = "https://clinic.example/booking/42";
        ev.Categories = ["health"];
        return ev;
    }

    private static Event BoardMeeting()
    {
        var ev = Timed(2, "Board meeting", "2026-11-04T18:00:00", "2026-11-04T20:00:00", uid: "board-1@club.example");
        ev.CoverTitle = "Meeting";
        ev.Description = "Agenda:\n1. Budget; 2. Plans, misc \\ notes";
        ev.Location = "Club house, room 2";
        ev.Url = "https://club.example/board";
        ev.Categories = ["board", "finance"];
        ev.Status = EventStatus.Tentative;
        ev.Sequence = 2;
        return ev;
    }

    /// <summary>Weekly training across the end of DST (25 Oct 2026) with every kind of exception.</summary>
    private static Event Training() =>
        Timed(3, "Training", "2026-10-19T18:00:00", "2026-10-19T20:00:00")
            .Series("FREQ=WEEKLY;COUNT=12", exdates: ["2026-11-30T18:00:00"], rdates: ["2026-12-19T10:00:00"])
            .WithException("2026-10-26T18:00:00", moveTo: ("2026-10-26T19:00:00", "2026-10-26T21:00:00"))
            .WithException("2026-11-02T18:00:00", cancelled: true)
            .WithException("2026-11-09T18:00:00", title: "Training (gym B)", description: "Gym B is closed for repairs")
            .WithException("2026-11-16T18:00:00", transparency: EventTransparency.Transparent)
            .WithException("2026-11-23T18:00:00", coverTitle: "Away");

    private static Event Tournament() => AllDay(4, "Tournament", "2026-11-07", "2026-11-09");

    private static Event GeneralMeeting()
    {
        var ev = Timed(5, "Jahreshauptversammlung mit Wahlen, Berichten und Ehrungen – bitte pünktlich 🎉 (Vereinsheim, großer Saal)", "2026-12-05T15:00:00", "2026-12-05T18:00:00");
        ev.Description = "Bitte Mitgliedsausweis mitbringen. Getränke gibt es an der Theke; Snacks bringt jeder selbst mit.";
        ev.RelatedTo = Training().Uid; // split from the training series, which is in the same feed
        return ev;
    }

    private static Event Hike()
    {
        var ev = Timed(6, "Hike", "2026-11-14T09:00:00", "2026-11-14T15:00:00");
        ev.Location = "Grunewald";
        return ev;
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void Busy_events_show_only_times_and_the_cover_title_or_busy_label(string locale)
    {
        var items = new[] { Therapy().At(EventLevel.FreeBusy), BoardMeeting().At(EventLevel.FreeBusy), Training().At(EventLevel.FreeBusy), Tournament().At(EventLevel.FreeBusy) };

        var ics = GoldenFile.AssertFeed($"busy.{locale}.ics", Document(items, locale: locale));
        foreach (var secret in new[] { "Therapy", "Room 4", "Clinic", "clinic.example", "Board meeting", "Agenda", "Club house", "board-1@", "gym B", "health", "X-SCALENDERPLUS", "LAST-MODIFIED", "SEQUENCE", "STATUS" })
        {
            Assert.DoesNotContain(secret, ics, StringComparison.Ordinal);
        }

        Assert.Contains("SUMMARY:Appointment\r\n", ics, StringComparison.Ordinal);
        Assert.Contains("SUMMARY:Away\r\n", ics, StringComparison.Ordinal); // the occurrence's own cover title
        Assert.Contains($"UID:{Id(1)}@scalenderplus\r\n", ics, StringComparison.Ordinal); // opaque, not the imported UID
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void Read_events_carry_details_and_the_read_only_label(string locale)
    {
        var items = new[] { BoardMeeting().At(EventLevel.Read), Training().At(EventLevel.Read), Tournament().At(EventLevel.Read), GeneralMeeting().At(EventLevel.Read) };

        var ics = GoldenFile.AssertFeed($"read.{locale}.ics", Document(items, locale: locale));
        Assert.Contains("UID:board-1@club.example\r\n", ics, StringComparison.Ordinal); // single-calendar feeds keep the stored UID
        Assert.Contains("SUMMARY:🔒 Board meeting\r\n", ics, StringComparison.Ordinal);
        Assert.DoesNotContain("SUMMARY:Meeting\r\n", ics, StringComparison.Ordinal); // readers get the title, not the cover title
    }

    [Fact]
    public void Editable_events_are_unmarked_by_default_with_the_edit_footer()
    {
        var items = new[] { BoardMeeting().At(EventLevel.Edit), Training().At(EventLevel.Manage), Hike().At(EventLevel.Read) };

        GoldenFile.AssertFeed("edit.en.ics", Document(items));
    }

    [Theory]
    [InlineData(FeedLabelMode.PrefixEmojiBoth, "prefix_emoji_both", "en")]
    [InlineData(FeedLabelMode.SuffixText, "suffix_text", "en")]
    [InlineData(FeedLabelMode.SuffixText, "suffix_text", "de")]
    [InlineData(FeedLabelMode.DescriptionOnly, "description_only", "en")]
    [InlineData(FeedLabelMode.None, "none", "en")]
    public void Every_label_mode_marks_read_and_edit_events(FeedLabelMode mode, string name, string locale)
    {
        var items = new[] { Hike().At(EventLevel.Read), BoardMeeting().At(EventLevel.Edit) };

        GoldenFile.AssertFeed($"labels.{name}.{locale}.ics", Document(items, labelMode: mode, locale: locale));
    }

    [Fact]
    public void Multi_calendar_feeds_use_opaque_uids_everywhere()
    {
        var items = new[] { BoardMeeting().At(EventLevel.Read), Therapy().At(EventLevel.FreeBusy) };

        var ics = GoldenFile.AssertFeed("aggregate.en.ics", Document(items, nativeUids: false, name: "sCalenderPlus"));
        Assert.DoesNotContain("board-1@club.example", ics, StringComparison.Ordinal);
        Assert.Contains($"UID:{Id(2)}@scalenderplus\r\n", ics, StringComparison.Ordinal);
    }

    [Fact]
    public void Share_link_feeds_have_no_labels_links_or_access_properties()
    {
        var items = new[] { Hike().At(EventLevel.Read), Therapy().At(EventLevel.FreeBusy) };

        var ics = GoldenFile.AssertFeed("share-link.en.ics", Document(items, labelMode: FeedLabelMode.None, accessProperties: false));
        Assert.DoesNotContain("X-SCALENDERPLUS", ics, StringComparison.Ordinal);
        Assert.DoesNotContain("app.example.test", ics, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_zone_used_gets_a_vtimezone()
    {
        var items = new[]
        {
            Timed(10, "Stand-up", "2026-11-02T09:00:00", "2026-11-02T09:15:00", "America/New_York").Series("FREQ=WEEKLY;BYDAY=MO,WE,FR").At(EventLevel.Read),
            Timed(11, "Review", "2026-11-03T14:30:00", "2026-11-03T15:30:00", "Asia/Kolkata").At(EventLevel.Read),
            Timed(12, "Summer party", "2027-01-09T18:00:00", "2027-01-09T23:00:00", "Australia/Sydney").At(EventLevel.Read),
            Timed(13, "Server window", "2026-11-05T02:00:00", "2026-11-05T03:00:00", "UTC").At(EventLevel.Read),
            Timed(14, "Monthly club evening", "2024-01-08T19:00:00", "2024-01-08T21:00:00").Series("FREQ=MONTHLY;BYDAY=2MO").At(EventLevel.Read),
            Timed(15, "Conference", "2026-12-01T09:00:00", "2026-12-01T17:00:00", "Africa/Casablanca").At(EventLevel.Read),
            Tournament().At(EventLevel.Read),
        };

        GoldenFile.AssertFeed("timezones.en.ics", Document(items, labelMode: FeedLabelMode.None, timeZone: "America/New_York"));
    }

    [Fact]
    public void Busy_series_count_only_exceptions_that_change_what_busy_viewers_see()
    {
        Assert.Equal(3, FeedRenderer.CountVEvents(Training().At(EventLevel.FreeBusy))); // master + moved + own cover title
        Assert.Equal(5, FeedRenderer.CountVEvents(Training().At(EventLevel.Read))); // master + every exception but the cancelled
        Assert.Equal(1, FeedRenderer.CountVEvents(Hike().At(EventLevel.Read)));
    }
}
