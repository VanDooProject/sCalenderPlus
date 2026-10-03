using SCalenderPlus.Core.Groups;

namespace SCalenderPlus.Core.Permissions;

/// <summary>
/// Who is asking (docs/architecture/permissions.md §3): either a signed-in user with their group memberships,
/// or an anonymous holder of a share link. A share link belongs to exactly one calendar and carries its level
/// (<c>free_busy</c> or <c>read</c>) as a ceiling; on every other calendar the holder has level <c>none</c>.
/// Signed-in users never act through share links (a share-link feed is anonymous whoever fetches it).
/// </summary>
public sealed class PrincipalContext
{
    private static readonly IReadOnlyDictionary<Guid, GroupRole> _noGroups = new Dictionary<Guid, GroupRole>();

    private PrincipalContext(Guid? userId, IReadOnlyDictionary<Guid, GroupRole> groups, Guid? linkCalendarId, CalendarLevel linkLevel)
    {
        UserId = userId;
        Groups = groups;
        LinkCalendarId = linkCalendarId;
        LinkLevel = linkLevel;
    }

    /// <summary>The signed-in user; null for share-link holders.</summary>
    public Guid? UserId { get; }

    /// <summary>Group memberships (group id → role); empty for share-link holders. No nested groups (MVP).</summary>
    public IReadOnlyDictionary<Guid, GroupRole> Groups { get; }

    /// <summary>The calendar the share link belongs to; null for users.</summary>
    public Guid? LinkCalendarId { get; }

    /// <summary>The share link's level (ceiling); <see cref="CalendarLevel.None"/> for users.</summary>
    public CalendarLevel LinkLevel { get; }

    public bool IsAnonymous => UserId is null;

    public static PrincipalContext ForUser(Guid userId, IReadOnlyDictionary<Guid, GroupRole>? groups = null)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("The user id must not be empty.", nameof(userId));
        }

        return new(userId, groups ?? _noGroups, null, CalendarLevel.None);
    }

    /// <summary>An anonymous holder of a share link of <paramref name="calendarId"/> with <paramref name="level"/> (<c>free_busy</c> or <c>read</c>).</summary>
    public static PrincipalContext ForShareLink(Guid calendarId, CalendarLevel level)
    {
        if (level is not (CalendarLevel.FreeBusy or CalendarLevel.Read))
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Share links carry free_busy or read.");
        }

        return new(null, _noGroups, calendarId, level);
    }

    /// <summary>The user's role in <paramref name="groupId"/>, or null when not a member.</summary>
    public GroupRole? RoleIn(Guid groupId) => Groups.TryGetValue(groupId, out var role) ? role : null;
}
