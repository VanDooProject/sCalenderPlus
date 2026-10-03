using NodaTime;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Core.Events;

/// <summary>
/// A stored event permission override (<c>event_overrides</c>, data-model.md §4): <c>user:{id}</c>,
/// <c>group:{id}[minRole]</c>, <c>anonymous</c> or <c>everyone</c> → a level <c>none</c> … <c>edit</c>.
/// <see cref="ToOverride"/> is the engine's <see cref="EventOverride"/>. One entry per principal and event.
/// </summary>
public sealed class EventOverrideEntry
{
    public Guid Id { get; set; }

    public Guid EventId { get; set; }

    public PrincipalType PrincipalType { get; set; }

    /// <summary>User or group id; null for <c>anonymous</c>/<c>everyone</c>.</summary>
    public Guid? PrincipalId { get; set; }

    /// <summary>Lowest matching role of group principals; null otherwise.</summary>
    public GroupRole? MinRole { get; set; }

    /// <summary><c>none</c> … <c>edit</c> (overrides never carry <c>manage</c>, rule 9).</summary>
    public EventLevel Level { get; set; }

    /// <summary>Who set this entry at its current level (no FK, kept as tombstone).</summary>
    public Guid CreatedBy { get; set; }

    public Instant CreatedAt { get; set; }

    public Principal Principal => Principal.Create(PrincipalType, PrincipalId, MinRole);

    public EventOverride ToOverride() => new(Principal, Level);

    /// <summary>A new entry of <paramref name="eventId"/> for <paramref name="entry"/>.</summary>
    public static EventOverrideEntry For(Guid eventId, EventOverride entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Level > PermissionLevels.MaxOverrideLevel)
        {
            throw new ArgumentOutOfRangeException(nameof(entry), entry.Level, "Overrides carry none … edit.");
        }

        return new EventOverrideEntry
        {
            Id = Guid.CreateVersion7(),
            EventId = eventId,
            PrincipalType = entry.Principal.Type,
            PrincipalId = entry.Principal.Id,
            MinRole = entry.Principal.MinRole,
            Level = entry.Level,
        };
    }
}
