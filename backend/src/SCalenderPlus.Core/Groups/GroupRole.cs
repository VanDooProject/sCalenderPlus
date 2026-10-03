namespace SCalenderPlus.Core.Groups;

/// <summary>
/// Fixed group roles (docs/architecture/permissions.md §6.1), totally ordered
/// <c>viewer &lt; member &lt; admin &lt; owner</c>; stored as <c>smallint</c> (0 … 3), so comparisons follow the order.
/// </summary>
public enum GroupRole : short
{
    Viewer = 0,
    Member = 1,
    Admin = 2,
    Owner = 3,
}

/// <summary>API representation of roles: lowercase names (<c>owner</c>, <c>admin</c>, <c>member</c>, <c>viewer</c>).</summary>
public static class GroupRoles
{
    public static IReadOnlyList<GroupRole> All { get; } = [GroupRole.Viewer, GroupRole.Member, GroupRole.Admin, GroupRole.Owner];

    public static string Format(GroupRole role) => role switch
    {
        GroupRole.Viewer => "viewer",
        GroupRole.Member => "member",
        GroupRole.Admin => "admin",
        GroupRole.Owner => "owner",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown group role."),
    };

    /// <summary>Parses the exact lowercase name; anything else (including numbers) fails.</summary>
    public static bool TryParse(string? value, out GroupRole role)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(value, Format(candidate), StringComparison.Ordinal))
            {
                role = candidate;
                return true;
            }
        }

        role = default;
        return false;
    }
}
