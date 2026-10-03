using SCalenderPlus.Core.Groups;

namespace SCalenderPlus.Core.Permissions;

/// <summary>
/// Kind of principal a grant or override names (docs/architecture/permissions.md §3), stored as
/// <c>smallint</c>. The declaration order is the reverse of the specificity tier (<see cref="Principal.Tier"/>).
/// </summary>
public enum PrincipalType : short
{
    /// <summary><c>user:{id}</c> — tier 4.</summary>
    User = 0,

    /// <summary><c>group:{id}[minRole]</c> — members with role ≥ minRole; tier 3.</summary>
    Group = 1,

    /// <summary>Share-link holders of the calendar; tier 2; restrict-only in overrides.</summary>
    Anonymous = 2,

    /// <summary>Everyone in the calendar's audience (incl. link holders); tier 1; restrict-only in overrides.</summary>
    Everyone = 3,
}

/// <summary>
/// Who a grant or override applies to. Construct with <see cref="User"/>, <see cref="Group"/>,
/// <see cref="Anonymous"/>, <see cref="Everyone"/> or, from stored columns, <see cref="Create"/>; invalid
/// combinations (a group without id, a user with a role, …) cannot be represented. Value equality: two entries
/// with equal principals target the same people (the storage key is <c>(type, id, min_role)</c>).
/// </summary>
public sealed record Principal
{
    private Principal(PrincipalType type, Guid? id, GroupRole? minRole)
    {
        Type = type;
        Id = id;
        MinRole = minRole;
    }

    /// <summary><c>anonymous</c>: share-link holders of the calendar.</summary>
    public static Principal Anonymous { get; } = new(PrincipalType.Anonymous, null, null);

    /// <summary><c>everyone</c>: everybody who reaches the calendar (§3, never strangers).</summary>
    public static Principal Everyone { get; } = new(PrincipalType.Everyone, null, null);

    public PrincipalType Type { get; }

    /// <summary>User or group id; null for <c>anonymous</c>/<c>everyone</c>.</summary>
    public Guid? Id { get; }

    /// <summary>Lowest matching group role; null unless <see cref="Type"/> is <see cref="PrincipalType.Group"/>.</summary>
    public GroupRole? MinRole { get; }

    /// <summary>Specificity: user 4, group 3, anonymous 2, everyone 1 (§3). Higher wins.</summary>
    public int Tier => 4 - (int)Type;

    /// <summary><c>anonymous</c> and <c>everyone</c> entries can only restrict (the engine applies <c>min(override, base)</c>).</summary>
    public bool IsRestrictOnly => Type >= PrincipalType.Anonymous;

    public static Principal User(Guid userId) => new(PrincipalType.User, NotEmpty(userId, nameof(userId)), null);

    public static Principal Group(Guid groupId, GroupRole minRole = GroupRole.Viewer)
    {
        if (!Enum.IsDefined(minRole))
        {
            throw new ArgumentOutOfRangeException(nameof(minRole), minRole, "Unknown group role.");
        }

        return new(PrincipalType.Group, NotEmpty(groupId, nameof(groupId)), minRole);
    }

    /// <summary>From stored columns <c>(principal_type, principal_id, min_role)</c>; throws on inconsistent values.</summary>
    public static Principal Create(PrincipalType type, Guid? id, GroupRole? minRole) => type switch
    {
        PrincipalType.User when id is { } userId && minRole is null => User(userId),
        PrincipalType.Group when id is { } groupId => Group(groupId, minRole ?? GroupRole.Viewer),
        PrincipalType.Anonymous when id is null && minRole is null => Anonymous,
        PrincipalType.Everyone when id is null && minRole is null => Everyone,
        _ => throw new ArgumentException($"Invalid principal: type {type}, id {id?.ToString() ?? "null"}, minRole {minRole?.ToString() ?? "null"}."),
    };

    public override string ToString() => Type switch
    {
        PrincipalType.User => $"user:{Id}",
        PrincipalType.Group => $"group:{Id}[{GroupRoles.Format(MinRole.GetValueOrDefault())}]",
        PrincipalType.Anonymous => "anonymous",
        _ => "everyone",
    };

    private static Guid NotEmpty(Guid id, string name) =>
        id == Guid.Empty ? throw new ArgumentException("A principal id must not be empty.", name) : id;
}
