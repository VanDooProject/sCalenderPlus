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

    /// <summary>
    /// Events with a <c>user</c>/<c>group</c> override that matches <paramref name="principal"/> (index
    /// <c>event_overrides(principal_type, principal_id)</c>): the candidates of "Shared with me" — events of
    /// calendars the principal may not see at all (rule 7). The engine still decides their level.
    /// </summary>
    Task<IReadOnlyList<Guid>> EventsNamingAsync(PrincipalContext principal, CancellationToken cancellationToken = default);
}

/// <summary>The overrides of <c>event_overrides</c>: one query per batch of events.</summary>
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
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.GroupBy(o => o.EventId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<EventOverride>)[.. g.Select(o => o.ToOverride())]);
    }

    public async Task<IReadOnlyList<Guid>> EventsNamingAsync(PrincipalContext principal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (principal.UserId is not { } userId)
        {
            return []; // link holders are never named (user/group entries do not match them)
        }

        // Entries that could give something (level > none) naming the user or one of their groups; the group
        // entries' minimum roles are checked in memory (the engine decides the level anyway).
        var groupIds = principal.Groups.Keys.ToList();
        var rows = await db.EventOverrides.AsNoTracking()
            .Where(o => o.Level > EventLevel.None
                && ((o.PrincipalType == PrincipalType.User && o.PrincipalId == userId)
                    || (o.PrincipalType == PrincipalType.Group && groupIds.Contains(o.PrincipalId!.Value))))
            .Select(o => new { o.EventId, o.PrincipalType, o.PrincipalId, o.MinRole })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. rows
            .Where(o => o.PrincipalType == PrincipalType.User || principal.RoleIn(o.PrincipalId!.Value) >= o.MinRole)
            .Select(o => o.EventId)
            .Distinct()];
    }
}
