using NodaTime;

namespace SCalenderPlus.Core.Calendars;

/// <summary>
/// A user's personal overlay of a calendar (<c>user_calendar_prefs</c>, data-model.md §3): hidden in their views
/// and/or shown in another color. Never affects anyone else, and is no permission: a calendar the user can no
/// longer see is not shown whatever the overlay says. A missing row means the defaults (shown, calendar color).
/// </summary>
public sealed class CalendarPrefs
{
    public Guid UserId { get; set; }

    public Guid CalendarId { get; set; }

    /// <summary>Left out of the user's calendar views (still listed, to show it again).</summary>
    public bool Hidden { get; set; }

    /// <summary><c>#rrggbb</c> (lowercase) shown instead of the calendar's color; null = the calendar's.</summary>
    public string? Color { get; set; }

    public Instant UpdatedAt { get; set; }
}
