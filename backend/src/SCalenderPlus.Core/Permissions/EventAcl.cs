namespace SCalenderPlus.Core.Permissions;

/// <summary>An event-level rule <c>principal → level</c> that replaces the calendar-derived level for the principals it matches (§4.2).</summary>
/// <remarks>Stored overrides never carry <c>manage</c> (§4.4); the engine caps them at <c>edit</c> anyway.</remarks>
public sealed record EventOverride
{
    public EventOverride(Principal principal, EventLevel level)
    {
        ArgumentNullException.ThrowIfNull(principal);
        Principal = principal;
        Level = level;
    }

    public Principal Principal { get; }

    public EventLevel Level { get; }
}

/// <summary>
/// Everything the engine needs to know about an event: its calendar, its creator (null for system/import
/// events and tombstoned creators — no floor) and its overrides. A recurrence exception has no ACL of its own:
/// build it with <see cref="ExceptionOf"/> and it resolves exactly like its series (§4.2, §4.6).
/// </summary>
public sealed class EventAcl
{
    public EventAcl(Guid eventId, Guid calendarId, Guid? creatorUserId, IReadOnlyList<EventOverride>? overrides = null)
    {
        EventId = eventId;
        CalendarId = calendarId;
        CreatorUserId = creatorUserId;
        Overrides = overrides ?? [];
    }

    private EventAcl(Guid exceptionId, EventAcl series)
        : this(exceptionId, series.CalendarId, series.CreatorUserId, series.Overrides) => Series = series;

    public Guid EventId { get; }

    public Guid CalendarId { get; }

    public Guid? CreatorUserId { get; }

    public IReadOnlyList<EventOverride> Overrides { get; }

    /// <summary>The series master when this is a recurrence exception (modified/cancelled occurrence); null otherwise.</summary>
    public EventAcl? Series { get; }

    /// <summary>The ACL of an exception (<paramref name="exceptionId"/>) of <paramref name="series"/>: it inherits the series ACL.</summary>
    public static EventAcl ExceptionOf(EventAcl series, Guid exceptionId)
    {
        ArgumentNullException.ThrowIfNull(series);
        if (series.Series is not null)
        {
            throw new ArgumentException("The series of an exception must be a series master, not an exception.", nameof(series));
        }

        return new(exceptionId, series);
    }
}
