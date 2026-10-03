using System.ComponentModel.DataAnnotations;
using SCalenderPlus.Application.Calendars;
using SCalenderPlus.Application.Common;
using SCalenderPlus.Core.Calendars;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Api.Calendars;

/// <summary>A calendar as the signed-in user sees it.</summary>
/// <param name="Color"><c>#rrggbb</c>.</param>
/// <param name="DefaultTimeZone">IANA time zone id of new events.</param>
/// <param name="MyLevel">The caller's effective level: <c>free_busy</c>, <c>read</c>, <c>contribute</c>, <c>edit</c>, <c>manage</c> or <c>owner</c> (permissions.md §2.2).</param>
/// <param name="GroupRoleDefaults">Group calendars: the calendar level of each role of the owning group; null for personal calendars.</param>
/// <param name="CreatorsManageOwnEvents">Creators keep <c>manage</c> on their own events while they have <c>contribute</c>.</param>
/// <param name="CreatorsMayShareExternally">Creators may share their own events with people outside the calendar.</param>
/// <param name="Frozen">Over the owner's plan limit: visible, but no changes until resolved.</param>
public sealed record CalendarResponse(
    Guid Id,
    string Name,
    string? Description,
    string Color,
    string DefaultTimeZone,
    CalendarOwnerResponse Owner,
    string MyLevel,
    RoleDefaultsResponse? GroupRoleDefaults,
    bool CreatorsManageOwnEvents,
    bool CreatorsMayShareExternally,
    bool Frozen,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static CalendarResponse From(CalendarView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var c = view.Calendar;
        return new CalendarResponse(
            c.Id,
            c.Name,
            c.Description,
            c.Color,
            c.DefaultTimeZone,
            c.OwnerGroupId is { } groupId ? new CalendarOwnerResponse("group", groupId) : new CalendarOwnerResponse("user", c.OwnerUserId!.Value),
            PermissionLevels.Format(view.MyLevel),
            c.IsGroupOwned ? RoleDefaultsResponse.From(c.RoleDefaults) : null,
            c.CreatorsManageOwnEvents,
            c.CreatorsMayShareExternally,
            c.FrozenAt is not null,
            c.CreatedAt.ToDateTimeOffset(),
            c.UpdatedAt.ToDateTimeOffset());
    }
}

/// <param name="Type"><c>user</c> or <c>group</c>.</param>
/// <param name="Id">The owning user or group.</param>
public sealed record CalendarOwnerResponse(string Type, Guid Id);

/// <summary>Calendar levels (<c>none</c> … <c>manage</c>) of the owning group's roles; role owner is always <c>owner</c>.</summary>
public sealed record RoleDefaultsResponse(string Admin, string Member, string Viewer)
{
    public static RoleDefaultsResponse From(GroupRoleDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        return new(PermissionLevels.Format(defaults.Admin), PermissionLevels.Format(defaults.Member), PermissionLevels.Format(defaults.Viewer));
    }
}

/// <summary>One page of the calendars the signed-in user sees.</summary>
/// <param name="NextCursor">Pass as <c>cursor</c> for the next page; null on the last page.</param>
public sealed record CalendarListResponse(IReadOnlyList<CalendarResponse> Items, string? NextCursor);

/// <summary>Changed role defaults: each <c>none</c>, <c>free_busy</c>, <c>read</c>, <c>contribute</c>, <c>edit</c> or <c>manage</c>; absent or null = unchanged.</summary>
public sealed class RoleDefaultsRequest
{
    public string? Admin { get; init; }

    public string? Member { get; init; }

    public string? Viewer { get; init; }

    /// <summary>Parses the levels; <c>400 validation_failed</c> with <c>errors["groupRoleDefaults.{role}"]</c> otherwise.</summary>
    public RoleDefaultChanges Parse() => new(Level(Admin, "admin"), Level(Member, "member"), Level(Viewer, "viewer"));

    private static CalendarLevel? Level(string? value, string role) =>
        value is null ? null
        : PermissionLevels.TryParse(value, out CalendarLevel level) && level <= CalendarLevel.Manage ? level
        : throw Validation.Failed($"groupRoleDefaults.{role}", "Use none, free_busy, read, contribute, edit or manage.");
}

public sealed class CreateCalendarRequest
{
    /// <summary>1–100 characters (trimmed).</summary>
    [Required]
    [StringLength(Calendar.NameMaxLength)]
    public string Name { get; init; } = string.Empty;

    /// <summary>At most 1000 characters.</summary>
    [StringLength(Calendar.DescriptionMaxLength)]
    public string? Description { get; init; }

    /// <summary><c>#rrggbb</c>; default <c>#4f46e5</c>.</summary>
    [StringLength(Calendar.ColorLength)]
    public string? Color { get; init; }

    /// <summary>IANA time zone id (e.g. <c>Europe/Berlin</c>); unknown ids are <c>422 time_zone_invalid</c>.</summary>
    [Required]
    [StringLength(Calendar.TimeZoneMaxLength)]
    public string DefaultTimeZone { get; init; } = string.Empty;

    /// <summary>Create a group calendar (admins and owners of the group); leave out for a personal calendar.</summary>
    public Guid? GroupId { get; init; }

    /// <summary>Group calendars only; default <c>admin → manage, member → contribute, viewer → read</c>.</summary>
    public RoleDefaultsRequest? GroupRoleDefaults { get; init; }

    /// <summary>Default true.</summary>
    public bool? CreatorsManageOwnEvents { get; init; }

    /// <summary>Default false.</summary>
    public bool? CreatorsMayShareExternally { get; init; }
}

/// <summary>
/// JSON Merge Patch of a calendar (<c>application/merge-patch+json</c>): absent or <c>null</c> members stay
/// unchanged; an empty <c>description</c> removes the description.
/// </summary>
public sealed class UpdateCalendarRequest
{
    [StringLength(Calendar.NameMaxLength, MinimumLength = 1)]
    public string? Name { get; init; }

    [StringLength(Calendar.DescriptionMaxLength)]
    public string? Description { get; init; }

    [StringLength(Calendar.ColorLength)]
    public string? Color { get; init; }

    [StringLength(Calendar.TimeZoneMaxLength)]
    public string? DefaultTimeZone { get; init; }

    public bool? CreatorsManageOwnEvents { get; init; }

    public bool? CreatorsMayShareExternally { get; init; }

    /// <summary>Group calendars only; never above the caller's own level.</summary>
    public RoleDefaultsRequest? GroupRoleDefaults { get; init; }
}
