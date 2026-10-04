using Microsoft.EntityFrameworkCore;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Application.Events;

/// <summary>
/// Where the event query service gets event overrides from (permissions.md §4.2): the <c>event_overrides</c>
/// table (<see cref="EventOverrideSource"/>), loaded in batch for the events of a listing.
/// </summary>
public interface IEventOverrideSource
{
    /// <summary>
    /// The overrides of <paramref name="eventIds"/> (only events with <c>has_overrides</c> are asked), by event id;
    /// events without overrides may be missing from the result.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<EventOverride>>> ForEventsAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// The overrides of <c>event_overrides</c>: one query per batch of events, each event's entries in a stable order
/// (users, groups, anonymous, everyone; then by id and role) so explainer traces are deterministic.
/// </summary>
public sealed class EventOverrideSource(IAppDbContext db) : IEventOverrideSource
{
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<EventOverride>>> ForEventsAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventIds);
        if (eventIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<EventOverride>>();
        }

        var ids = eventIds.ToList();
        var rows = await db.EventOverrides.AsNoTracking()
            .Where(o => ids.Contains(o.EventId))
            .OrderBy(o => o.EventId).ThenBy(o => o.PrincipalType).ThenBy(o => o.PrincipalId).ThenBy(o => o.MinRole)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.GroupBy(o => o.EventId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<EventOverride>)[.. g.Select(o => o.ToOverride())]);
    }
}
