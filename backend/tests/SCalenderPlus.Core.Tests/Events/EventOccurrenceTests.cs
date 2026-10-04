using NodaTime;
using SCalenderPlus.Core.Events;

namespace SCalenderPlus.Core.Tests.Events;

/// <summary>Occurrences of a series master with exceptions applied (data-model.md §9), and the series bounds.</summary>
public sealed class EventOccurrenceTests
{
    private static readonly DateTimeZone _berlin = DateTimeZoneProviders.Tzdb["Europe/Berlin"];

    [Fact]
    public void Exceptions_change_move_and_cancel_occurrences()
    {
        var series = Series("FREQ=WEEKLY;COUNT=4");
        series.Exceptions.Add(new EventExceptionEntry { RecurrenceId = new(2026, 11, 9, 18, 0), Title = "Away", Description = string.Empty, Status = EventStatus.Tentative, Transparency = EventTransparency.Transparent });
        var moved = new EventExceptionEntry { RecurrenceId = new(2026, 11, 16, 18, 0) };
        moved.Move(EventTimes.Timed(new(2026, 12, 20, 10, 0), new(2026, 12, 20, 11, 0), _berlin));
        series.Exceptions.Add(moved);
        series.Exceptions.Add(new EventExceptionEntry { RecurrenceId = new(2026, 11, 23, 18, 0), Cancelled = true });

        var november = series.Occurrences(Instant.FromUtc(2026, 11, 1, 0, 0), Instant.FromUtc(2026, 12, 1, 0, 0));

        Assert.False(november.Truncated);
        Assert.Equal([new LocalDateTime(2026, 11, 2, 18, 0), new LocalDateTime(2026, 11, 9, 18, 0)], november.Items.Select(o => o.RecurrenceId));
        var plain = november.Items[0];
        Assert.Equal(("Training", "Bring shoes", EventStatus.Confirmed, EventTransparency.Opaque, false), (plain.Title, plain.Description, plain.Status, plain.Transparency, plain.IsModified));
        var changed = november.Items[1];
        Assert.Equal(("Away", null, EventStatus.Tentative, EventTransparency.Transparent, true), (changed.Title, changed.Description, changed.Status, changed.Transparency, changed.IsModified));
        Assert.Equal("Pitch", changed.Location);

        // The moved occurrence appears where it moved to (beyond the rule's end).
        var december = series.Occurrences(Instant.FromUtc(2026, 12, 15, 0, 0), Instant.FromUtc(2027, 1, 1, 0, 0));
        var later = Assert.Single(december.Items);
        Assert.Equal(new LocalDateTime(2026, 11, 16, 18, 0), later.RecurrenceId);
        Assert.Equal(new LocalDateTime(2026, 12, 20, 10, 0), later.Times.StartLocal);

        series.RefreshSeriesBounds();
        Assert.Equal(Instant.FromUtc(2026, 12, 20, 10, 0), series.SeriesUntilUtc);
        Assert.Null(series.SeriesStartUtc);

        Assert.Null(series.FindOccurrence(new(2026, 11, 23, 18, 0)));
        Assert.True(series.FindOccurrence(new(2026, 11, 23, 18, 0), includeCancelled: true)!.Exception!.Cancelled);
        Assert.Null(series.FindOccurrence(new(2026, 11, 24, 18, 0)));
        Assert.Null(series.FindOccurrence(new(2026, 11, 30, 18, 0))); // beyond COUNT
        Assert.True(series.FindOccurrence(new(2026, 11, 16, 18, 0))!.IsModified);
    }

    [Fact]
    public void An_occurrence_moved_before_the_first_widens_the_series_start()
    {
        var series = Series("FREQ=DAILY");
        var moved = new EventExceptionEntry { RecurrenceId = new(2026, 11, 3, 18, 0) };
        moved.Move(EventTimes.Timed(new(2026, 10, 30, 9, 0), new(2026, 10, 30, 10, 0), _berlin));
        series.Exceptions.Add(moved);

        series.RefreshSeriesBounds();

        Assert.Null(series.SeriesUntilUtc); // infinite
        Assert.Equal(Instant.FromUtc(2026, 10, 30, 8, 0), series.SeriesStartUtc);
        Assert.Null(series.OccursUntil);
    }

    [Fact]
    public void Exceptions_report_whether_they_override_anything()
    {
        var empty = new EventExceptionEntry();
        Assert.True(empty.IsEmpty);
        Assert.Null(empty.MovedTimes("Europe/Berlin"));
        Assert.False(new EventExceptionEntry { Cancelled = true }.IsEmpty);
        Assert.False(new EventExceptionEntry { Location = string.Empty }.IsEmpty);

        var allDay = new EventExceptionEntry();
        allDay.Move(EventTimes.AllDayEvent(new LocalDate(2026, 11, 3), new LocalDate(2026, 11, 4)));
        Assert.True(allDay.IsMoved);
        Assert.Equal(new LocalDate(2026, 11, 3), allDay.MovedTimes(null)!.StartDate);
        allDay.Move(null);
        Assert.True(allDay.IsEmpty);
    }

    [Fact]
    public void Single_events_have_no_occurrences()
    {
        var single = Series("FREQ=DAILY");
        single.Rrule = null;
        single.RefreshSeriesBounds();

        Assert.Null(single.Recurrence());
        Assert.Null(single.SeriesUntilUtc);
        Assert.Null(single.FindOccurrence(new(2026, 11, 2, 18, 0)));
        Assert.Throws<InvalidOperationException>(() => single.Occurrences(Instant.MinValue, Instant.MaxValue));
        Assert.Equal(single.EndUtc, single.OccursUntil);
    }

    [Fact]
    public void Series_are_capped_per_window()
    {
        var series = Series("FREQ=DAILY");

        var window = series.Occurrences(Instant.FromUtc(2026, 11, 1, 0, 0), Instant.FromUtc(2027, 11, 1, 0, 0), max: 10);

        Assert.True(window.Truncated);
        Assert.Equal(10, window.Items.Count);
    }

    private static Event Series(string rrule)
    {
        var ev = new Event { Id = Guid.CreateVersion7(), Title = "Training", Description = "Bring shoes", Location = "Pitch", Rrule = rrule };
        ev.SetTimes(EventTimes.Timed(new(2026, 11, 2, 18, 0), new(2026, 11, 2, 19, 0), _berlin)!);
        return ev;
    }
}
