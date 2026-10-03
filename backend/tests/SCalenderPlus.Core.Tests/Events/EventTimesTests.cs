using NodaTime;
using SCalenderPlus.Core.Events;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Core.Tests.Events;

/// <summary>Time model of events (data-model.md §10): wall clock + zone, DST gap/overlap, all-day padding.</summary>
public sealed class EventTimesTests
{
    private static readonly DateTimeZone _berlin = DateTimeZoneProviders.Tzdb["Europe/Berlin"];
    private static readonly DateTimeZone _honolulu = DateTimeZoneProviders.Tzdb["Pacific/Honolulu"]; // UTC−10, no DST
    private static readonly DateTimeZone _fakaofo = DateTimeZoneProviders.Tzdb["Pacific/Fakaofo"]; // UTC+13, no DST

    [Fact]
    public void Timed_events_keep_the_wall_clock_and_derive_utc()
    {
        var times = EventTimes.Timed(new LocalDateTime(2026, 11, 2, 18, 0), new LocalDateTime(2026, 11, 2, 20, 0), _berlin)!;

        Assert.False(times.AllDay);
        Assert.Equal("Europe/Berlin", times.TimeZone);
        Assert.Equal(new LocalDateTime(2026, 11, 2, 18, 0), times.StartLocal);
        Assert.Equal(Instant.FromUtc(2026, 11, 2, 17, 0), times.StartUtc);
        Assert.Equal(Instant.FromUtc(2026, 11, 2, 19, 0), times.EndUtc);
        Assert.Empty(times.Adjustments);
    }

    [Fact]
    public void A_time_in_the_dst_gap_is_shifted_forward_and_reported()
    {
        // 2026-03-29: Berlin jumps from 02:00 to 03:00.
        var times = EventTimes.Timed(new LocalDateTime(2026, 3, 29, 2, 30), new LocalDateTime(2026, 3, 29, 4, 0), _berlin)!;

        Assert.Equal(new LocalDateTime(2026, 3, 29, 3, 30), times.StartLocal);
        Assert.Equal(Instant.FromUtc(2026, 3, 29, 1, 30), times.StartUtc);
        var adjustment = Assert.Single(times.Adjustments);
        Assert.Equal(new TimeAdjustment("start", TimeAdjustmentKind.ShiftedForwardInGap, new LocalDateTime(2026, 3, 29, 2, 30), new LocalDateTime(2026, 3, 29, 3, 30), times.StartUtc), adjustment);
    }

    [Fact]
    public void An_ambiguous_time_takes_the_earlier_offset_and_is_reported()
    {
        // 2026-10-25: Berlin falls back from 03:00 CEST to 02:00 CET; 02:30 happens twice.
        var times = EventTimes.Timed(new LocalDateTime(2026, 10, 25, 1, 0), new LocalDateTime(2026, 10, 25, 2, 30), _berlin)!;

        Assert.Equal(new LocalDateTime(2026, 10, 25, 2, 30), times.EndLocal);
        Assert.Equal(Instant.FromUtc(2026, 10, 25, 0, 30), times.EndUtc); // +02:00, the earlier one
        var adjustment = Assert.Single(times.Adjustments);
        Assert.Equal(TimeAdjustmentKind.AmbiguousEarlierOffset, adjustment.Kind);
        Assert.Equal("end", adjustment.Field);
    }

    [Fact]
    public void End_before_start_is_refused_and_zero_length_allowed()
    {
        var at = new LocalDateTime(2026, 11, 2, 18, 0);

        Assert.Null(EventTimes.Timed(at, at.PlusMinutes(-1), _berlin));
        Assert.NotNull(EventTimes.Timed(at, at, _berlin));
        Assert.Null(EventTimes.AllDayEvent(new LocalDate(2026, 11, 2), new LocalDate(2026, 11, 2)));
    }

    [Fact]
    public void Sub_second_parts_are_dropped()
    {
        var times = EventTimes.Timed(new LocalDateTime(2026, 11, 2, 18, 0, 5).PlusMilliseconds(250), new LocalDateTime(2026, 11, 2, 19, 0), _berlin)!;

        Assert.Equal(new LocalDateTime(2026, 11, 2, 18, 0, 5), times.StartLocal);
    }

    [Fact]
    public void All_day_events_are_floating_dates_with_padded_utc_bounds()
    {
        var times = EventTimes.AllDayEvent(new LocalDate(2026, 11, 2), new LocalDate(2026, 11, 3))!;

        Assert.True(times.AllDay);
        Assert.Null(times.TimeZone);
        Assert.Equal(Instant.FromUtc(2026, 11, 1, 10, 0), times.StartUtc); // 2026-11-02 00:00 at UTC+14
        Assert.Equal(Instant.FromUtc(2026, 11, 3, 14, 0), times.EndUtc);
    }

    public static TheoryData<string, bool> ViewerDays => new()
    {
        // The viewer's own day 2026-11-02, a few hours of it, and the neighbouring days.
        { "2026-11-02T00:00/2026-11-03T00:00", true },
        { "2026-11-02T15:00/2026-11-03T00:00", true }, // late afternoon to midnight
        { "2026-11-02T00:00/2026-11-02T01:00", true },
        { "2026-11-01T00:00/2026-11-02T00:00", false },
        { "2026-11-03T00:00/2026-11-04T00:00", false },
    };

    [Theory]
    [MemberData(nameof(ViewerDays))]
    public void All_day_events_are_placed_by_date_in_the_viewers_zone(string window, bool expected)
    {
        var times = EventTimes.AllDayEvent(new LocalDate(2026, 11, 2), new LocalDate(2026, 11, 3))!;

        foreach (var zone in new[] { _honolulu, _fakaofo, _berlin, DateTimeZone.Utc })
        {
            var (from, to) = Window(window, zone);
            Assert.True(expected == times.Overlaps(from, to, zone), $"{window} in {zone.Id}");
            if (expected)
            {
                Assert.True(times.Overlaps(from, to, viewerZone: null), $"padded bounds miss {window} in {zone.Id}");
            }
        }
    }

    [Fact]
    public void Timed_events_overlap_half_open_windows_and_zero_length_events_count_at_the_start()
    {
        var times = EventTimes.Timed(new LocalDateTime(2026, 11, 2, 18, 0), new LocalDateTime(2026, 11, 2, 19, 0), _berlin)!;
        var point = EventTimes.Timed(new LocalDateTime(2026, 11, 2, 18, 0), new LocalDateTime(2026, 11, 2, 18, 0), _berlin)!;
        var start = Instant.FromUtc(2026, 11, 2, 17, 0);

        Assert.True(times.Overlaps(start, start + Duration.FromMinutes(1), null));
        Assert.False(times.Overlaps(start + Duration.FromHours(1), start + Duration.FromHours(2), null)); // ends where the window starts
        Assert.False(times.Overlaps(start - Duration.FromHours(1), start, null)); // starts where the window ends
        Assert.True(point.Overlaps(start, start + Duration.FromHours(1), null));
        Assert.False(point.Overlaps(start - Duration.FromHours(1), start, null));
    }

    [Fact]
    public void Free_busy_viewers_do_not_see_transparent_events()
    {
        Assert.Equal(EventLevel.None, EventVisibility.Effective(EventLevel.FreeBusy, EventTransparency.Transparent));
        Assert.Equal(EventLevel.FreeBusy, EventVisibility.Effective(EventLevel.FreeBusy, EventTransparency.Opaque));
        Assert.Equal(EventLevel.Read, EventVisibility.Effective(EventLevel.Read, EventTransparency.Transparent));
        Assert.True(EventVisibility.IsBusyOnly(EventLevel.FreeBusy));
        Assert.False(EventVisibility.IsBusyOnly(EventLevel.Read));
    }

    [Theory]
    [InlineData("0192f2c4-0000-7000-8000-000000000000@scalenderplus", true)]
    [InlineData("040000008200E00074C5B7101A82E008@outlook.com", true)]
    [InlineData("has space", false)]
    [InlineData("semi;colon", false)]
    [InlineData("comma,", false)]
    [InlineData("quote\"", false)]
    [InlineData("back\\slash", false)]
    [InlineData("ümlaut", false)]
    [InlineData("", false)]
    public void Uids_are_rfc5545_safe(string uid, bool valid) => Assert.Equal(valid, Event.IsValidUid(uid));

    [Fact]
    public void Stored_times_round_trip()
    {
        var ev = new Event { Id = Guid.CreateVersion7(), CalendarId = Guid.CreateVersion7() };
        var times = EventTimes.Timed(new LocalDateTime(2026, 3, 29, 2, 30), new LocalDateTime(2026, 3, 29, 4, 0), _berlin)!;
        ev.SetTimes(times);

        Assert.Equal(times with { Adjustments = ev.Times.Adjustments }, ev.Times);
        Assert.Equal($"{ev.Id}@scalenderplus", Event.NativeUid(ev.Id));
        Assert.Equal(ev.Id, ev.ToAcl().EventId);
    }

    private static (Instant From, Instant To) Window(string window, DateTimeZone zone)
    {
        var parts = window.Split('/');
        Instant At(string local) => zone.AtLeniently(NodaTime.Text.LocalDateTimePattern.CreateWithInvariantCulture("uuuu-MM-dd'T'HH:mm").Parse(local).Value).ToInstant();
        return (At(parts[0]), At(parts[1]));
    }
}
