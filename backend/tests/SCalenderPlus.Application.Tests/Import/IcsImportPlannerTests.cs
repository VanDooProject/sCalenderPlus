using NodaTime;
using SCalenderPlus.Application.Import;
using SCalenderPlus.Core.Events;
using static SCalenderPlus.Application.Tests.Import.IcsImportFixtures;
using IcalCalendar = Ical.Net.Calendar;

namespace SCalenderPlus.Application.Tests.Import;

/// <summary>The import plan of the committed exports and of edge cases (ical-caldav.md §2).</summary>
public sealed class IcsImportPlannerTests
{
    private static readonly DateTimeZone _berlin = DateTimeZoneProviders.Tzdb["Europe/Berlin"];

    [Fact]
    public void Google_export_imports_with_recurrence_and_exceptions_intact()
    {
        var plan = Plan(Read("google-export.ics"));

        Assert.Equal(9, plan.Items.Count);
        Assert.Equal(8, plan.Items.Count(i => i.Action == IcsImportAction.Create));
        var cancelled = Assert.Single(plan.Items, i => i.Action == IcsImportAction.Skip);
        Assert.Equal(("cancelled-friendly@google.com", IcsImportCodes.Cancelled), (cancelled.Uid, cancelled.Reason));
        Assert.All(plan.Items.Where(i => i.Event is not null), i => Assert.Equal((CalendarId, CreatorId), (i.Event!.CalendarId, i.Event.CreatorUserId)));

        // Weekly training: RRULE with UNTIL, EXDATEs (one in another TZID line), a moved and a renamed occurrence, a cancelled one.
        var training = Item(plan, "5q3n8f0v2k1m7c9d4h6j8l0p2r@google.com").Event!;
        Assert.Equal("U12 training", training.Title);
        Assert.Equal("Sportpark Nord, Platz 2, 10115 Berlin", training.Location);
        Assert.Equal("Bring water and shin guards.\nParents welcome on the last Monday of the month.", training.Description);
        Assert.Equal(("Europe/Berlin", new LocalDateTime(2026, 9, 7, 18, 0), new LocalDateTime(2026, 9, 7, 19, 30)), (training.TimeZone, training.StartLocal!.Value, training.EndLocal!.Value));
        Assert.Equal("FREQ=WEEKLY;UNTIL=20261221T225959Z;BYDAY=MO", training.Rrule);
        Assert.Equal([new(2026, 10, 5, 18, 0), new(2026, 10, 12, 18, 0), new LocalDateTime(2026, 12, 28, 18, 0)], training.ExDates);
        Assert.Equal(3, training.Exceptions.Count);
        var moved = training.Exceptions.Single(x => x.RecurrenceId == new LocalDateTime(2026, 9, 21, 18, 0));
        Assert.Equal((new LocalDateTime(2026, 9, 21, 19, 0), new LocalDateTime(2026, 9, 21, 20, 30), "Sporthalle Mitte"), (moved.StartLocal!.Value, moved.EndLocal!.Value, moved.Location));
        Assert.Null(moved.Title); // same as the series: inherits
        Assert.Null(moved.Description);
        Assert.True(training.Exceptions.Single(x => x.RecurrenceId == new LocalDateTime(2026, 11, 2, 18, 0)).Cancelled);
        var renamed = training.Exceptions.Single(x => x.RecurrenceId == new LocalDateTime(2026, 11, 16, 18, 0));
        Assert.Equal(("U12 training with parents", false), (renamed.Title, renamed.IsMoved));
        Assert.Null(renamed.Location);

        // The occurrences as viewers will see them: across the DST change at 18:00 local, the moved one at 19:00, without
        // the EXDATEs and the cancelled one, ending on 21 December.
        var occurrences = training.Occurrences(Instant.FromUtc(2026, 9, 1, 0, 0), Instant.FromUtc(2027, 1, 31, 0, 0)).Items;
        Assert.Equal(13, occurrences.Count); // 16 Mondays − 2 EXDATEs − 1 cancelled
        Assert.Equal(new LocalDateTime(2026, 12, 21, 18, 0), occurrences[^1].Times.StartLocal);
        Assert.Contains(occurrences, o => o.Times.StartLocal == new LocalDateTime(2026, 9, 21, 19, 0) && o.Location == "Sporthalle Mitte");
        Assert.DoesNotContain(occurrences, o => o.RecurrenceId.Date == new LocalDate(2026, 11, 2) || o.RecurrenceId.Date == new LocalDate(2026, 10, 5));
        Assert.Equal(Instant.FromUtc(2026, 11, 9, 17, 0), occurrences.Single(o => o.RecurrenceId.Date == new LocalDate(2026, 11, 9)).Times.StartUtc); // CET
        Assert.Equal(Instant.FromUtc(2026, 9, 14, 16, 0), occurrences.Single(o => o.RecurrenceId.Date == new LocalDate(2026, 9, 14)).Times.StartUtc); // CEST
        Assert.Equal((2, 4), (Item(plan, training.Uid).ModifiedOccurrences, Item(plan, training.Uid).CancelledOccurrences));

        // All-day: single (transparent), multi-day, and a yearly series with an EXDATE and a moved occurrence.
        var holiday = Item(plan, "holiday-20261003@google.com").Event!;
        Assert.Equal((true, new LocalDate(2026, 10, 3), new LocalDate(2026, 10, 4), EventTransparency.Transparent), (holiday.AllDay, holiday.StartDate!.Value, holiday.EndDate!.Value, holiday.Transparency));
        var tournament = Item(plan, "7tb2tournament0wknd@google.com").Event!;
        Assert.Equal((new LocalDate(2026, 10, 24), new LocalDate(2026, 10, 27), "https://lions.example.org/tournament", "Tournament weekend 🏆"), (tournament.StartDate!.Value, tournament.EndDate!.Value, tournament.Url, tournament.Title));
        var birthday = Item(plan, "birthday-coach-mia@google.com").Event!;
        Assert.Equal(("FREQ=YEARLY", true), (birthday.Rrule, birthday.AllDay));
        Assert.Equal([new LocalDate(2029, 3, 12).AtMidnight()], birthday.ExDates);
        var party = Assert.Single(birthday.Exceptions);
        Assert.Equal((new LocalDate(2028, 3, 12).AtMidnight(), new LocalDate(2028, 3, 11), "Coach Mia's birthday (party on Saturday)"), (party.RecurrenceId, party.StartDate!.Value, party.Title));
        Assert.Null(birthday.SeriesUntilUtc); // infinite

        // UTC single event: same instant, stored in the file's zone (X-WR-TIMEZONE).
        var call = Item(plan, "0kvideo1call2parents3@google.com").Event!;
        Assert.Equal(("Europe/Berlin", new LocalDateTime(2026, 11, 10, 18, 0), Instant.FromUtc(2026, 11, 10, 17, 0)), (call.TimeZone, call.StartLocal!.Value, call.StartUtc));

        // Another zone keeps its zone; tentative status.
        var meetup = Item(plan, "nyc-meetup-2026@google.com").Event!;
        Assert.Equal(("America/New_York", Instant.FromUtc(2026, 12, 5, 15, 0), EventStatus.Tentative), (meetup.TimeZone, meetup.StartUtc, meetup.Status));

        // COUNT series, private (warned: imports set no overrides), categories.
        var board = Item(plan, "board-meeting-monthly@google.com");
        Assert.Equal("FREQ=MONTHLY;COUNT=6;BYDAY=1TH", board.Event!.Rrule);
        Assert.Equal(["Board", "Club"], board.Event.Categories);
        Assert.Contains(IcsImportCodes.PrivateEvent, board.Warnings);
        Assert.Equal(new LocalDateTime(2027, 3, 4, 21, 30), board.Event.Occurrences(Instant.FromUtc(2026, 9, 1, 0, 0), Instant.FromUtc(2027, 12, 1, 0, 0)).Items[^1].Times.EndLocal);

        var untitled = Item(plan, "3untitled9slot@google.com");
        Assert.Equal(IcsImportPlanner.UntitledTitle, untitled.Title);
        Assert.Contains(IcsImportCodes.TitleMissing, untitled.Warnings);
    }

    [Fact]
    public void Google_export_parses_with_ical_net_to_the_same_events()
    {
        // Cross-check with an independent parser: the same VEVENTs and UIDs.
        var text = Read("google-export.ics");
        var calendar = IcalCalendar.Load(text)!;
        var plan = Plan(text);

        Assert.Equal(calendar.Events.Select(e => e.Uid).Distinct().Order(StringComparer.Ordinal), plan.Items.Select(i => i.Uid).Order(StringComparer.Ordinal));
        Assert.Equal(calendar.Events.Count(e => e.RecurrenceIdentifier is not null), plan.Items.Sum(i => i.ModifiedOccurrences) + 1); // + the cancelled override
    }

    [Fact]
    public void Outlook_export_maps_windows_zone_names()
    {
        var plan = Plan(Read("outlook-export.ics"));

        Assert.All(plan.Items, i => Assert.Equal(IcsImportAction.Create, i.Action));
        var weekly = plan.Items[0].Event!;
        Assert.Equal(("Europe/Berlin", "FREQ=WEEKLY;COUNT=10;BYDAY=TU", "Wochenrunde"), (weekly.TimeZone, weekly.Rrule, weekly.Title));
        Assert.StartsWith("040000008200E00074C5B7101A82E008", weekly.Uid, StringComparison.Ordinal);
        Assert.Equal(Instant.FromUtc(2026, 10, 15, 12, 0), plan.Items[1].Event!.StartUtc); // display-name TZID, CEST
        Assert.Equal(("America/Los_Angeles", Instant.FromUtc(2026, 10, 20, 16, 0)), (plan.Items[2].Event!.TimeZone, plan.Items[2].Event!.StartUtc));
        var unknown = plan.Items[3];
        Assert.Equal(("Europe/Berlin", new LocalDateTime(2026, 10, 21, 9, 0)), (unknown.Event!.TimeZone, unknown.Event.StartLocal!.Value));
        Assert.Contains(IcsImportCodes.TimeZoneUnknown, unknown.Warnings);
        Assert.Contains(IcsImportCodes.FloatingTime, plan.Items[4].Warnings);
    }

    [Fact]
    public void Floating_times_use_the_calendar_zone_without_x_wr_timezone()
    {
        var item = Single(Plan(Calendar("BEGIN:VEVENT", "UID:a@x", "SUMMARY:A", "DTSTART:20261022T090000", "DTEND:20261022T100000", "END:VEVENT"), "America/Chicago"));

        Assert.Equal(("America/Chicago", Instant.FromUtc(2026, 10, 22, 14, 0)), (item.Event!.TimeZone, item.Event.StartUtc));
    }

    [Fact]
    public void Utc_series_keep_utc()
    {
        var item = Single(Plan(Calendar("BEGIN:VEVENT", "UID:a@x", "SUMMARY:A", "DTSTART:20261022T090000Z", "DURATION:PT45M", "RRULE:FREQ=DAILY;COUNT=3", "EXDATE:20261023T090000Z", "END:VEVENT")));

        Assert.Equal(("UTC", new LocalDateTime(2026, 10, 22, 9, 45)), (item.Event!.TimeZone, item.Event.EndLocal!.Value));
        Assert.Equal([new LocalDateTime(2026, 10, 23, 9, 0)], item.Event.ExDates);
    }

    [Fact]
    public void Defaults_end_and_reads_duration()
    {
        var plan = Plan(Calendar(
            "BEGIN:VEVENT", "UID:a@x", "SUMMARY:A", "DTSTART;VALUE=DATE:20261022", "END:VEVENT",
            "BEGIN:VEVENT", "UID:b@x", "SUMMARY:B", "DTSTART;VALUE=DATE:20261022", "DURATION:P1W", "END:VEVENT",
            "BEGIN:VEVENT", "UID:c@x", "SUMMARY:C", "DTSTART;TZID=Europe/Berlin:20261022T090000", "END:VEVENT",
            "BEGIN:VEVENT", "UID:d@x", "SUMMARY:D", "DTSTART;TZID=Europe/Berlin:20261022T090000", "DURATION:P1DT2H", "END:VEVENT"));

        Assert.Equal(new LocalDate(2026, 10, 23), plan.Items[0].Event!.EndDate);
        Assert.Equal(new LocalDate(2026, 10, 29), plan.Items[1].Event!.EndDate);
        Assert.Equal(plan.Items[2].Event!.StartUtc, plan.Items[2].Event!.EndUtc);
        Assert.Equal(new LocalDateTime(2026, 10, 23, 11, 0), plan.Items[3].Event!.EndLocal);
    }

    [Fact]
    public void Same_uid_twice_imports_the_first_only()
    {
        var plan = Plan(Calendar(
            "BEGIN:VEVENT", "UID:a@x", "SUMMARY:First", "DTSTART;VALUE=DATE:20261022", "END:VEVENT",
            "BEGIN:VEVENT", "UID:a@x", "SUMMARY:Second", "DTSTART;VALUE=DATE:20261023", "END:VEVENT"));

        Assert.Equal((IcsImportAction.Create, "First"), (plan.Items[0].Action, plan.Items[0].Title));
        Assert.Equal((IcsImportAction.Duplicate, IcsImportCodes.DuplicateInFile), (plan.Items[1].Action, plan.Items[1].Reason));
    }

    [Fact]
    public void Missing_and_unusable_uids_get_stable_generated_ones()
    {
        string[] body = ["BEGIN:VEVENT", "SUMMARY:No uid", "DTSTART;VALUE=DATE:20261022", "END:VEVENT", "BEGIN:VEVENT", "UID:with space, comma", "SUMMARY:Bad uid", "DTSTART;VALUE=DATE:20261022", "END:VEVENT"];
        var first = Plan(Calendar(body));
        var again = Plan(Calendar(body));

        Assert.All(first.Items, i => Assert.Matches("^ics-[0-9a-f]{32}@import.scalenderplus$", i.Uid));
        Assert.All(first.Items, i => Assert.True(Event.IsValidUid(i.Uid)));
        Assert.Equal(first.Items.Select(i => i.Uid), again.Items.Select(i => i.Uid));
        Assert.NotEqual(first.Items[0].Uid, first.Items[1].Uid);
        Assert.All(first.Items, i => Assert.Contains(IcsImportCodes.UidGenerated, i.Warnings));
    }

    [Theory]
    [InlineData("RRULE:FREQ=HOURLY", IcsImportCodes.RecurrenceNotSupported)]
    [InlineData("RRULE:FREQ=WEEKLY;BYHOUR=9", IcsImportCodes.RecurrenceNotSupported)]
    [InlineData("RRULE:FREQ=WEEKLY;UNTIL=20200101T000000Z", IcsImportCodes.RecurrenceInvalid)]
    [InlineData("RRULE:FREQ=SOMETIMES", IcsImportCodes.RecurrenceInvalid)]
    public void Unsupported_rules_skip_the_event(string rule, string reason)
    {
        var item = Single(Plan(Calendar("BEGIN:VEVENT", "UID:a@x", "SUMMARY:A", "DTSTART;TZID=Europe/Berlin:20261022T090000", rule, "END:VEVENT")));

        Assert.Equal((IcsImportAction.Skip, reason), (item.Action, item.Reason));
        Assert.Null(item.Event);
    }

    [Theory]
    [InlineData("DTSTART:yesterday")]
    [InlineData("DTSTART;VALUE=DATE:99991231")]
    public void Unreadable_times_skip_the_event(string start) =>
        Assert.Equal(IcsImportCodes.TimeInvalid, Single(Plan(Calendar("BEGIN:VEVENT", "UID:a@x", start, "END:VEVENT"))).Reason);

    [Fact]
    public void Missing_start_skips_the_event() =>
        Assert.Equal(IcsImportCodes.StartMissing, Single(Plan(Calendar("BEGIN:VEVENT", "UID:a@x", "SUMMARY:A", "END:VEVENT"))).Reason);

    [Fact]
    public void Overrides_without_their_series_become_single_events()
    {
        var plan = Plan(Calendar(
            "BEGIN:VEVENT", "UID:s@x", "SUMMARY:Moved", "RECURRENCE-ID;TZID=Europe/Berlin:20261020T090000", "DTSTART;TZID=Europe/Berlin:20261021T090000", "DTEND;TZID=Europe/Berlin:20261021T100000", "END:VEVENT",
            "BEGIN:VEVENT", "UID:s@x", "SUMMARY:Other", "RECURRENCE-ID;TZID=Europe/Berlin:20261027T090000", "DTSTART;TZID=Europe/Berlin:20261027T090000", "END:VEVENT"));

        Assert.Equal(2, plan.Items.Count(i => i.Action == IcsImportAction.Create));
        Assert.NotEqual(plan.Items[0].Uid, plan.Items[1].Uid);
        Assert.All(plan.Items, i => Assert.Contains(IcsImportCodes.OrphanOccurrence, i.Warnings));
        Assert.Null(plan.Items[0].Event!.Rrule);
    }

    [Fact]
    public void Overrides_of_occurrences_that_do_not_exist_are_reported()
    {
        var item = Single(Plan(Calendar(
            "BEGIN:VEVENT", "UID:s@x", "SUMMARY:S", "DTSTART;TZID=Europe/Berlin:20261019T090000", "RRULE:FREQ=WEEKLY;COUNT=3", "END:VEVENT",
            "BEGIN:VEVENT", "UID:s@x", "SUMMARY:Off", "RECURRENCE-ID;TZID=Europe/Berlin:20261020T090000", "DTSTART;TZID=Europe/Berlin:20261020T090000", "END:VEVENT",
            "BEGIN:VEVENT", "UID:s@x", "SUMMARY:Range", "RECURRENCE-ID;RANGE=THISANDFUTURE;TZID=Europe/Berlin:20261026T090000", "DTSTART;TZID=Europe/Berlin:20261026T090000", "END:VEVENT",
            "BEGIN:VEVENT", "UID:s@x", "SUMMARY:Utc id", "RECURRENCE-ID:20261102T080000Z", "DTSTART;TZID=Europe/Berlin:20261102T100000", "END:VEVENT")));

        Assert.Contains(IcsImportCodes.OccurrenceNotFound, item.Warnings);
        Assert.Contains(IcsImportCodes.OccurrenceIgnored, item.Warnings);
        var exception = Assert.Single(item.Event!.Exceptions); // the UTC recurrence id matched 09:00 Berlin
        Assert.Equal((new LocalDateTime(2026, 11, 2, 9, 0), new LocalDateTime(2026, 11, 2, 10, 0), "Utc id"), (exception.RecurrenceId, exception.StartLocal!.Value, exception.Title));
    }

    [Fact]
    public void Long_values_are_truncated_and_invalid_urls_dropped()
    {
        var item = Single(Plan(Calendar("BEGIN:VEVENT", "UID:a@x", "SUMMARY:" + new string('t', 600), "DESCRIPTION:" + new string('d', 21_000), "URL:javascript:alert(1)", "DTSTART;VALUE=DATE:20261022", "END:VEVENT")));

        Assert.Equal((Event.TitleMaxLength, Event.DescriptionMaxLength), (item.Event!.Title.Length, item.Event.Description!.Length));
        Assert.Null(item.Event.Url);
        Assert.Contains(IcsImportCodes.Truncated, item.Warnings);
        Assert.Contains(IcsImportCodes.UrlInvalid, item.Warnings);
    }

    [Fact]
    public void End_before_start_becomes_zero_length()
    {
        var item = Single(Plan(Calendar("BEGIN:VEVENT", "UID:a@x", "SUMMARY:A", "DTSTART;TZID=Europe/Berlin:20261022T100000", "DTEND;TZID=Europe/Berlin:20261022T090000", "END:VEVENT")));

        Assert.Equal(item.Event!.StartUtc, item.Event.EndUtc);
        Assert.Contains(IcsImportCodes.EndBeforeStart, item.Warnings);
    }

    [Fact]
    public void Dst_gap_is_shifted_and_warned()
    {
        var item = Single(Plan(Calendar("BEGIN:VEVENT", "UID:a@x", "SUMMARY:A", "DTSTART;TZID=Europe/Berlin:20270328T023000", "DTEND;TZID=Europe/Berlin:20270328T040000", "END:VEVENT")));

        Assert.Equal(new LocalDateTime(2027, 3, 28, 3, 30), item.Event!.StartLocal);
        Assert.Contains(IcsImportCodes.TimeAdjusted, item.Warnings);
        Assert.Equal(_berlin.Id, item.Event.TimeZone);
    }

    [Fact]
    public void Planning_stops_at_its_time_budget()
    {
        var roots = IcsReader.Read(Calendar("BEGIN:VEVENT", "UID:a@x", "SUMMARY:A", "DTSTART;VALUE=DATE:20261022", "END:VEVENT"), 10, out _, out _)!;

        var error = Assert.Throws<Errors.AppException>(() => IcsImportPlanner.Plan(roots, _berlin, CalendarId, CreatorId, Now, TimeSpan.FromTicks(-1)));

        Assert.Equal(Errors.ErrorCodes.IcsTooComplex, error.Code);
    }
}
