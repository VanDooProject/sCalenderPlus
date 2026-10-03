using SCalenderPlus.Application.Common;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Application.Events;

/// <summary>Problems of the event use cases (permissions.md §8 error semantics).</summary>
public static class EventErrors
{
    /// <summary>Unknown, deleted, or level <c>none</c> (incl. transparent events for <c>free_busy</c> viewers): indistinguishable.</summary>
    public static AppException NotFound() => new(ErrorCodes.NotFound, "Event not found.");

    /// <summary>Visible, but the level is too low: <c>403 insufficient_permission</c> with the event levels <c>required</c> and <c>actual</c>.</summary>
    public static AppException InsufficientLevel(EventLevel required, EventLevel actual) =>
        new(ErrorCodes.InsufficientPermission, $"This needs the event level {PermissionLevels.Format(required)}.", new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["required"] = PermissionLevels.Format(required),
            ["actual"] = PermissionLevels.Format(actual),
        });

    public static AppException UidConflict() =>
        new(ErrorCodes.UidConflict, "The calendar already has an event with this UID.");

    public static AppException Changed() =>
        new(ErrorCodes.PreconditionFailed, "The event was changed meanwhile. Reload it and try again.");

    /// <summary><c>422 recurrence_not_supported</c> with <c>errors.recurrence</c>: recurring events come with M2-E.</summary>
    public static AppException RecurrenceNotSupported() =>
        new(ErrorCodes.RecurrenceNotSupported, "Recurring events are not supported yet; create single events.", new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [Validation.ErrorsMember] = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["recurrence"] = ["Leave out recurrence (or send null)."],
            },
        });

    /// <summary><c>422 time_zone_invalid</c> with the offending field in <c>errors</c>.</summary>
    public static AppException TimeZoneInvalid(string field, string? timeZone) =>
        new(ErrorCodes.TimeZoneInvalid, $"'{timeZone}' is not a known IANA time zone id (e.g. Europe/Berlin).", new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [Validation.ErrorsMember] = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = ["Use an IANA time zone id such as Europe/Berlin."],
            },
        });
}

/// <summary>
/// Audit actions of events (<c>audit_events.action</c>, resource type <see cref="ResourceType"/>, resource id =
/// event id; the subject is the calendar's billing subject).
/// </summary>
public static class EventAuditActions
{
    public const string ResourceType = "event";

    public const string Created = "event.created";
    public const string Updated = "event.updated";
    public const string Deleted = "event.deleted";
}
