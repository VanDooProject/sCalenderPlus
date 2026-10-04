using Microsoft.EntityFrameworkCore;
using SCalenderPlus.Core.Calendars;
using SCalenderPlus.Core.Events;
using SCalenderPlus.Core.Groups;

namespace SCalenderPlus.Application.Persistence;

/// <summary>
/// The unit of work of a use case (implemented by the EF Core <c>AppDbContext</c>): changes staged by
/// services such as <c>IJobScheduler.Enqueue</c> or <c>IAuditLog.Record</c> are committed together, in one
/// transaction, by <see cref="SaveChangesAsync"/>. Entity sets are added here as the domain grows.
/// </summary>
public interface IAppDbContext
{
    DbSet<Group> Groups { get; }

    DbSet<GroupMember> GroupMembers { get; }

    DbSet<GroupInvite> GroupInvites { get; }

    DbSet<Calendar> Calendars { get; }

    DbSet<CalendarGrantEntry> CalendarGrants { get; }

    /// <summary>Personal overlays of calendars (<c>user_calendar_prefs</c>): hidden, color.</summary>
    DbSet<CalendarPrefs> CalendarPrefs { get; }

    /// <summary>
    /// Events: read only through <c>Events.EventQueryService</c> (permission-aware) and added only through
    /// <c>Events.EventWriter</c> — an architecture test forbids every other use (tenant isolation, permissions.md §8).
    /// </summary>
    DbSet<Event> Events { get; }

    /// <summary>
    /// Modified and cancelled occurrences of series (<c>event_exceptions</c>): event data like <see cref="Events"/>,
    /// so the same rule applies — only <c>Events.EventQueryService</c> and <c>Events.EventWriter</c> use it.
    /// </summary>
    DbSet<EventExceptionEntry> EventExceptions { get; }

    /// <summary>The sync log (<c>calendar_changes</c>), appended by <c>Events.EventWriter</c>.</summary>
    DbSet<CalendarChange> CalendarChanges { get; }

    /// <summary>Event permission overrides (<c>event_overrides</c>), changed only by the override use cases and the membership lifecycle.</summary>
    DbSet<EventOverrideEntry> EventOverrides { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="operation"/> in one database transaction, for use cases whose collaborators save on
    /// their own (e.g. ASP.NET Core Identity's user manager): every <see cref="SaveChangesAsync"/> inside commits
    /// only when the operation completes; an exception rolls all of them back.
    /// </summary>
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes a transaction-scoped exclusive lock on <paramref name="key"/> (PostgreSQL advisory lock, released at
    /// commit or rollback): serializes check-then-insert sequences such as plan-limit counts. Call it inside
    /// <see cref="InTransactionAsync"/>.
    /// </summary>
    Task LockAsync(Guid key, CancellationToken cancellationToken = default);
}
