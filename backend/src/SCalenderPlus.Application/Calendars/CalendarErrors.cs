using SCalenderPlus.Application.Errors;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Application.Calendars;

/// <summary>Problems of the calendar use cases (permissions.md §8 error semantics).</summary>
public static class CalendarErrors
{
    /// <summary>Unknown calendar or level <c>none</c>: indistinguishable (no existence leaks).</summary>
    public static AppException CalendarNotFound() => new(ErrorCodes.NotFound, "Calendar not found.");

    /// <summary>Visible, but the level is too low: <c>403 insufficient_permission</c> with <c>required</c> and <c>actual</c>.</summary>
    public static AppException InsufficientLevel(CalendarLevel required, CalendarLevel actual) =>
        new(ErrorCodes.InsufficientPermission, $"This needs the calendar level {PermissionLevels.Format(required)}.", new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["required"] = PermissionLevels.Format(required),
            ["actual"] = PermissionLevels.Format(actual),
        });

    public static AppException GrantNotFound() => new(ErrorCodes.NotFound, "Grant not found.");

    public static AppException Frozen() =>
        new(ErrorCodes.CalendarFrozen, "The calendar is over its plan limit: it can be viewed and deleted, but not changed.");

    public static AppException SelfLockout() =>
        new(ErrorCodes.PermissionSelfLockout, "This change would take away your own right to manage the calendar. Ask its owner to make it.");

    public static AppException GrantExists() =>
        new(ErrorCodes.Conflict, "This principal already has a grant on the calendar; change that one instead.");

    public static AppException Changed() =>
        new(ErrorCodes.PreconditionFailed, "The calendar was changed meanwhile. Reload it and try again.");

    public static AppException GroupHasCalendars(int count) =>
        new(ErrorCodes.GroupHasCalendars, $"The group still owns {count} calendar(s). Transfer or delete them first.", new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["calendarCount"] = count,
        });
}

/// <summary>
/// Audit actions of calendars (<c>audit_events.action</c>, resource type <see cref="ResourceType"/>, resource id
/// = calendar id; the subject is the calendar's billing subject). Grant changes are recorded on the calendar.
/// </summary>
public static class CalendarAuditActions
{
    public const string ResourceType = "calendar";

    public const string Created = "calendar.created";
    public const string Updated = "calendar.updated";
    public const string Deleted = "calendar.deleted";
    public const string GrantCreated = "calendar.grant.created";
    public const string GrantUpdated = "calendar.grant.updated";
    public const string GrantRemoved = "calendar.grant.removed";

    /// <summary>A group was deleted: its grants on this calendar were removed with it (issue #43).</summary>
    public const string GrantRemovedWithGroup = "calendar.grant.removed_with_group";
}
