using System.Text.RegularExpressions;
using NodaTime;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Core.Calendars;

/// <summary>
/// A calendar (<c>calendars</c>, docs/architecture/data-model.md §3), owned by exactly one user
/// (<see cref="OwnerUserId"/>) or one group (<see cref="OwnerGroupId"/>). Its permission settings
/// (<see cref="RoleDefaults"/>, the creator rules 6 and 7) and its <see cref="CalendarGrantEntry">grants</see>
/// are the input of the permission engine (<see cref="ToAcl"/>). Calendars are deleted for real; grants go
/// with them.
/// </summary>
public sealed partial class Calendar
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 1000;
    public const int TimeZoneMaxLength = 64;
    public const int ColorLength = 7;

    /// <summary>The color of calendars created without one.</summary>
    public const string DefaultColor = "#4f46e5";

    public Guid Id { get; set; }

    /// <summary>The owning user; null for group-owned calendars.</summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>The owning group (its role-owners have level <c>owner</c>); null for user-owned calendars.</summary>
    public Guid? OwnerGroupId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary><c>#rrggbb</c> (lowercase).</summary>
    public string Color { get; set; } = DefaultColor;

    /// <summary>IANA zone of new events and of imported floating times.</summary>
    public string DefaultTimeZone { get; set; } = "UTC";

    /// <summary>Rule 6: creators keep <c>manage</c> on their own events (default true).</summary>
    public bool CreatorsManageOwnEvents { get; set; } = true;

    /// <summary>Rule 7: creators may share their own events outside the calendar's audience (default false).</summary>
    public bool CreatorsMayShareExternally { get; set; }

    /// <summary>Calendar levels of the owning group's roles (§6.2); only meaningful for group-owned calendars.</summary>
    public GroupRoleDefaults RoleDefaults { get; set; } = GroupRoleDefaults.Default;

    /// <summary>Bumped on every ACL-relevant change (grants, role defaults, creator settings, ownership, freeze; overrides and share links later).</summary>
    public long AclVersion { get; set; }

    /// <summary>Over the plan limit (M5): visible, but no edits.</summary>
    public Instant? FrozenAt { get; set; }

    /// <summary>Archived (v1 endpoint <c>POST /calendars/{id}/archive</c>); not used yet.</summary>
    public Instant? ArchivedAt { get; set; }

    public Instant CreatedAt { get; set; }

    public Instant UpdatedAt { get; set; }

    /// <summary>Optimistic concurrency token (PostgreSQL <c>xmin</c>).</summary>
    public uint Version { get; set; }

    public bool IsGroupOwned => OwnerGroupId is not null;

    /// <summary><c>user:{id}</c> or <c>group:{id}</c>.</summary>
    public Principal Owner => OwnerGroupId is { } groupId
        ? Principal.Group(groupId)
        : Principal.User(OwnerUserId ?? throw new InvalidOperationException($"Calendar {Id} has no owner."));

    /// <summary>The engine's view of this calendar with <paramref name="grants"/> (those of other calendars are ignored).</summary>
    public CalendarAcl ToAcl(IEnumerable<CalendarGrantEntry> grants)
    {
        ArgumentNullException.ThrowIfNull(grants);
        return new CalendarAcl(
            Id,
            Owner,
            [.. grants.Where(g => g.CalendarId == Id).Select(g => g.ToGrant())],
            RoleDefaults,
            CreatorsManageOwnEvents,
            CreatorsMayShareExternally);
    }

    /// <summary><c>#rrggbb</c> hex color (either case).</summary>
    public static bool IsValidColor(string? color) => color is { Length: ColorLength } && ColorPattern().IsMatch(color);

    [GeneratedRegex("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ColorPattern();
}

/// <summary>
/// A stored calendar grant (<c>calendar_grants</c>): <c>user:{id}</c> or <c>group:{id}[minRole]</c> → a level
/// <c>free_busy</c> … <c>manage</c>. <see cref="ToGrant"/> is the engine's <see cref="CalendarGrant"/>.
/// </summary>
public sealed class CalendarGrantEntry
{
    public Guid Id { get; set; }

    public Guid CalendarId { get; set; }

    /// <summary><see cref="PrincipalType.User"/> or <see cref="PrincipalType.Group"/>.</summary>
    public PrincipalType PrincipalType { get; set; }

    public Guid PrincipalId { get; set; }

    /// <summary>Lowest matching role of group principals (stored, default viewer); null for users.</summary>
    public GroupRole? MinRole { get; set; }

    public CalendarLevel Level { get; set; }

    /// <summary>Who created the grant (no FK, kept as tombstone).</summary>
    public Guid CreatedBy { get; set; }

    public Instant CreatedAt { get; set; }

    public Instant UpdatedAt { get; set; }

    /// <summary>Optimistic concurrency token (PostgreSQL <c>xmin</c>).</summary>
    public uint Version { get; set; }

    public Principal Principal => Principal.Create(PrincipalType, PrincipalId, MinRole);

    public CalendarGrant ToGrant() => new(Principal, Level);

    /// <summary>A new entry for <paramref name="principal"/> (a user or a group).</summary>
    public static CalendarGrantEntry For(Guid calendarId, Principal principal, CalendarLevel level)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var grant = new CalendarGrant(principal, level); // validates principal type and level
        return new CalendarGrantEntry
        {
            Id = Guid.CreateVersion7(),
            CalendarId = calendarId,
            PrincipalType = grant.Principal.Type,
            PrincipalId = grant.Principal.Id!.Value,
            MinRole = grant.Principal.MinRole,
            Level = grant.Level,
        };
    }
}
