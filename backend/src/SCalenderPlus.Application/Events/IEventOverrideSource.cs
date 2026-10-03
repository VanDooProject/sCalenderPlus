using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Application.Events;

/// <summary>
/// Where the event query service gets event overrides from (permissions.md §4.2): the plug-in point of the
/// <c>event_overrides</c> table and its API (M2-D). Until then no override exists (<see cref="NoEventOverrides"/>),
/// every event resolves to <c>impliedEventLevel(Lc)</c> unless a floor applies, and "Shared with me" is empty.
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

/// <summary>No overrides yet (M2-C): replaced by the <c>event_overrides</c>-backed source in M2-D.</summary>
public sealed class NoEventOverrides : IEventOverrideSource
{
    private static readonly IReadOnlyDictionary<Guid, IReadOnlyList<EventOverride>> _none = new Dictionary<Guid, IReadOnlyList<EventOverride>>();

    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<EventOverride>>> ForEventsAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken = default) =>
        Task.FromResult(_none);

    public Task<IReadOnlyList<Guid>> EventsNamingAsync(PrincipalContext principal, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Guid>>([]);
}
