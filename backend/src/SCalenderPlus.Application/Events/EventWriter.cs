using NodaTime;
using SCalenderPlus.Application.Common;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Core.Events;

namespace SCalenderPlus.Application.Events;

/// <summary>
/// The write side of the event choke point (permissions.md §8): the only place besides
/// <see cref="EventQueryService"/> that touches <c>IAppDbContext.Events</c> and <c>EventExceptions</c>
/// (architecture test). It adds new rows (events, exceptions of a series), removes exceptions and appends the
/// sync log (<c>calendar_changes</c>) for every change; changes of loaded events are tracked by the context (the
/// query service loads them, with their exceptions, for update). Permission checks happen before, in the use cases.
/// Exception changes are logged as an <c>upsert</c> of their series (sync clients fetch the series resource).
/// Staged only: the caller's <c>SaveChangesAsync</c> commits rows, log and audit together.
/// </summary>
public sealed class EventWriter(IAppDbContext db, IClock clock)
{
    /// <summary>Stages a new event and its <c>upsert</c> log row.</summary>
    public void Add(Event ev)
    {
        ArgumentNullException.ThrowIfNull(ev);
        db.Events.Add(ev);
        Log(ev, CalendarChangeKind.Upsert);
    }

    /// <summary>Logs a change of a (tracked) event: <c>upsert</c>, or <c>delete</c> once it is soft-deleted.</summary>
    public void Changed(Event ev)
    {
        ArgumentNullException.ThrowIfNull(ev);
        Log(ev, ev.IsDeleted ? CalendarChangeKind.Delete : CalendarChangeKind.Upsert);
    }

    /// <summary>
    /// Logs a change of the (tracked) event's permissions — overrides or <c>has_overrides</c> — as <c>acl</c>: sync
    /// clients re-resolve the event; for viewers whose level dropped to <c>none</c> it is a delete.
    /// </summary>
    public void AclChanged(Event ev)
    {
        ArgumentNullException.ThrowIfNull(ev);
        Log(ev, CalendarChangeKind.Acl);
    }

    /// <summary>Logs a move of the (tracked) event from <paramref name="sourceCalendarId"/>: <c>delete</c> there, <c>upsert</c> in its new calendar.</summary>
    public void Moved(Event ev, Guid sourceCalendarId)
    {
        ArgumentNullException.ThrowIfNull(ev);
        Log(sourceCalendarId, ev.Id, CalendarChangeKind.Delete);
        Log(ev, CalendarChangeKind.Upsert);
    }

    /// <summary>Stages a new exception of the (tracked) series <paramref name="series"/>.</summary>
    public void AddException(Event series, EventExceptionEntry exception)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(exception);
        exception.EventId = series.Id;
        series.Exceptions.Add(exception);
        db.EventExceptions.Add(exception);
    }

    /// <summary>Stages the removal of an exception of the (tracked) series <paramref name="series"/>.</summary>
    public void RemoveException(Event series, EventExceptionEntry exception)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(exception);
        series.Exceptions.Remove(exception);
        db.EventExceptions.Remove(exception);
    }

    /// <summary>Moves a (tracked) exception from <paramref name="from"/> to the series <paramref name="to"/> (split).</summary>
    public static void MoveException(EventExceptionEntry exception, Event from, Event to)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        from.Exceptions.Remove(exception);
        to.Exceptions.Add(exception);
        exception.EventId = to.Id;
    }

    private void Log(Event ev, CalendarChangeKind change) => Log(ev.CalendarId, ev.Id, change);

    private void Log(Guid calendarId, Guid eventId, CalendarChangeKind change) =>
        db.CalendarChanges.Add(new CalendarChange { CalendarId = calendarId, EventId = eventId, Change = change, At = clock.Now() });
}
