using NodaTime;
using SCalenderPlus.Application.Common;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Core.Events;

namespace SCalenderPlus.Application.Events;

/// <summary>
/// The write side of the event choke point (permissions.md §8): the only place besides
/// <see cref="EventQueryService"/> that touches <c>IAppDbContext.Events</c> (architecture test). It adds new rows
/// and appends the sync log (<c>calendar_changes</c>) for every change; changes of loaded events are tracked by
/// the context (the query service loads them for update). Permission checks happen before, in the use cases.
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

    private void Log(Event ev, CalendarChangeKind change) =>
        db.CalendarChanges.Add(new CalendarChange { CalendarId = ev.CalendarId, EventId = ev.Id, Change = change, At = clock.Now() });
}
