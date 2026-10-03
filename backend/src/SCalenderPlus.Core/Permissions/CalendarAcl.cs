using SCalenderPlus.Core.Groups;

namespace SCalenderPlus.Core.Permissions;

/// <summary>A calendar-level rule <c>principal → level</c> (additive). Only users and groups can be granted (share links are separate, <c>everyone</c> is not grantable); levels <c>free_busy</c> … <c>manage</c>.</summary>
public sealed record CalendarGrant
{
    public CalendarGrant(Principal principal, CalendarLevel level)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (principal.IsRestrictOnly)
        {
            throw new ArgumentException("Calendar grants name a user or a group.", nameof(principal));
        }

        if (level is < CalendarLevel.FreeBusy or > CalendarLevel.Manage)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Grants carry free_busy … manage.");
        }

        Principal = principal;
        Level = level;
    }

    public Principal Principal { get; }

    public CalendarLevel Level { get; }
}

/// <summary>
/// Calendar levels of the owning group's roles (§6.2), for group-owned calendars. Role <c>owner</c> is always
/// <see cref="CalendarLevel.Owner"/>; the others may be anything from <c>none</c> to <c>manage</c>.
/// </summary>
public sealed record GroupRoleDefaults
{
    public GroupRoleDefaults(CalendarLevel admin, CalendarLevel member, CalendarLevel viewer)
    {
        Admin = Valid(admin, nameof(admin));
        Member = Valid(member, nameof(member));
        Viewer = Valid(viewer, nameof(viewer));
    }

    /// <summary><c>admin → manage, member → contribute, viewer → read</c>.</summary>
    public static GroupRoleDefaults Default { get; } = new(CalendarLevel.Manage, CalendarLevel.Contribute, CalendarLevel.Read);

    public CalendarLevel Admin { get; }

    public CalendarLevel Member { get; }

    public CalendarLevel Viewer { get; }

    public CalendarLevel For(GroupRole role) => role switch
    {
        GroupRole.Owner => CalendarLevel.Owner,
        GroupRole.Admin => Admin,
        GroupRole.Member => Member,
        GroupRole.Viewer => Viewer,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown group role."),
    };

    private static CalendarLevel Valid(CalendarLevel level, string name) =>
        level is < CalendarLevel.None or > CalendarLevel.Manage
            ? throw new ArgumentOutOfRangeException(name, level, "Role defaults are none … manage.")
            : level;
}

/// <summary>
/// Everything the engine needs to know about a calendar: owner (a user or a group), grants, the owning
/// group's role defaults and the permission settings (rules 6 and 7). Plain data, loaded in batch.
/// </summary>
public sealed class CalendarAcl
{
    public CalendarAcl(
        Guid calendarId,
        Principal owner,
        IReadOnlyList<CalendarGrant>? grants = null,
        GroupRoleDefaults? roleDefaults = null,
        bool creatorsManageOwnEvents = true,
        bool creatorsMayShareExternally = false)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (owner.IsRestrictOnly)
        {
            throw new ArgumentException("A calendar is owned by a user or a group.", nameof(owner));
        }

        CalendarId = calendarId;
        Owner = Principal.Create(owner.Type, owner.Id, null); // owner groups have no role restriction
        Grants = grants ?? [];
        RoleDefaults = roleDefaults ?? GroupRoleDefaults.Default;
        CreatorsManageOwnEvents = creatorsManageOwnEvents;
        CreatorsMayShareExternally = creatorsMayShareExternally;
    }

    public Guid CalendarId { get; }

    /// <summary><c>user:{id}</c> or <c>group:{id}</c>.</summary>
    public Principal Owner { get; }

    public IReadOnlyList<CalendarGrant> Grants { get; }

    /// <summary>Only used for group-owned calendars.</summary>
    public GroupRoleDefaults RoleDefaults { get; }

    /// <summary>Rule 6: creators keep <c>manage</c> on their own events while they have <c>contribute</c> (default true).</summary>
    public bool CreatorsManageOwnEvents { get; }

    /// <summary>Rule 7: creators may share their own events with principals outside the calendar's audience (default false).</summary>
    public bool CreatorsMayShareExternally { get; }

    public bool IsGroupOwned => Owner.Type == PrincipalType.Group;
}
