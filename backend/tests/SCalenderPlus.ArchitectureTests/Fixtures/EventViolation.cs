using Microsoft.EntityFrameworkCore;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Core.Events;

// Deliberate violations of the event choke point (EventAccessTests): direct DbSet<Event> use.
namespace SCalenderPlus.ArchitectureTests.Fixtures.EventViolation;

internal sealed class ReadsEventsDirectly(IAppDbContext db, DbContext context)
{
    public int Count() => db.Events.Count();

    public IQueryable<Guid> InQuery() => db.Calendars.Where(c => db.Events.Any(e => e.CalendarId == c.Id)).Select(c => c.Id);

    public int Generic() => context.Set<Event>().Count();
}

internal sealed class HoldsTheSet
{
    public DbSet<Event>? Events { get; set; }
}

internal sealed class UsesCalendarsOnly(IAppDbContext db)
{
    public int Count() => db.Calendars.Count();
}
