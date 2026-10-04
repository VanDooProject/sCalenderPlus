using SCalenderPlus.Application.Common;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.Core.Permissions;
using SCalenderPlus.Core.Recurrence;

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

    /// <summary><c>422 recurrence_invalid</c> (malformed) or <c>recurrence_not_supported</c> (valid RFC 5545 outside the supported subset) with the field in <c>errors</c>.</summary>
    public static AppException Recurrence(RecurrenceProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        return new(
            problem.NotSupported ? ErrorCodes.RecurrenceNotSupported : ErrorCodes.RecurrenceInvalid,
            problem.Message,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [Validation.ErrorsMember] = new Dictionary<string, string[]>(StringComparer.Ordinal) { [problem.Field] = [problem.Message] },
            });
    }

    /// <summary><c>404</c>: the event is no series, or has no (live) occurrence with this recurrence id.</summary>
    public static AppException OccurrenceNotFound() => new(ErrorCodes.NotFound, "Occurrence not found.");

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

/// <summary>One refused override entry: the entry and why (<c>level_too_high</c>, <c>duplicate_principal</c>, …).</summary>
public sealed record OverrideProblem(EventOverride Override, string Reason);

/// <summary>Problems of the override use cases (permissions.md §4.4, §4.6).</summary>
public static class OverrideErrors
{
    public const string LevelTooHigh = "level_too_high";
    public const string DuplicatePrincipal = "duplicate_principal";
    public const string GroupNotSelectable = "group_not_selectable";
    public const string UserNotSelectable = "user_not_selectable";
    public const string ExternalSharingReason = "external_sharing";
    public const string RemovalExposesExternalShare = "removal_exposes_external_share";
    public const string NoOverrideRightsInTarget = "no_override_rights_in_target";

    /// <summary>The <c>reason</c> of an engine violation.</summary>
    public static string Reason(OverrideViolationReason reason) => reason switch
    {
        OverrideViolationReason.LevelTooHigh => LevelTooHigh,
        OverrideViolationReason.DuplicatePrincipal => DuplicatePrincipal,
        OverrideViolationReason.GroupNotSelectable => GroupNotSelectable,
        OverrideViolationReason.ExternalSharing => ExternalSharingReason,
        OverrideViolationReason.RemovalExposesExternalShare => RemovalExposesExternalShare,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown violation."),
    };

    /// <summary><c>422 override_invalid</c> with <c>violations</c> and <c>errors.overrides</c>.</summary>
    public static AppException Invalid(IReadOnlyList<OverrideProblem> problems) =>
        new(ErrorCodes.OverrideInvalid, "Some permission entries are invalid.", Members(problems, withErrors: true));

    /// <summary><c>403 external_sharing_not_allowed</c> with the entries that would share outside the calendar's audience (<c>violations</c>).</summary>
    public static AppException ExternalSharing(IReadOnlyList<OverrideProblem> problems) =>
        new(
            ErrorCodes.ExternalSharingNotAllowed,
            "Only calendar managers may share events with people outside the calendar (unless the calendar allows creators to).",
            Members(problems, withErrors: false));

    /// <summary><c>409 override_invalid_in_target</c>: the overrides the mover could not set in the target calendar (<c>violations</c>).</summary>
    public static AppException InvalidInTarget(IReadOnlyList<OverrideProblem> problems) =>
        new(
            ErrorCodes.OverrideInvalidInTarget,
            "Some of the event's permission entries could not be set by you in the target calendar. Remove them first, or ask a manager of the target calendar.",
            Members(problems, withErrors: false));

    private static Dictionary<string, object?> Members(IReadOnlyList<OverrideProblem> problems, bool withErrors)
    {
        var members = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["violations"] = problems.Select(p => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["principal"] = PrincipalMember(p.Override.Principal),
                ["level"] = PermissionLevels.Format(p.Override.Level),
                ["reason"] = p.Reason,
            }).ToList(),
        };
        if (withErrors)
        {
            members[Validation.ErrorsMember] = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["overrides"] = [.. problems.Select(p => $"{p.Override.Principal} → {PermissionLevels.Format(p.Override.Level)}: {Message(p.Reason)}")],
            };
        }

        return members;
    }

    /// <summary>A principal as in the api: <c>{ type, id?, minRole? }</c>.</summary>
    public static Dictionary<string, object?> PrincipalMember(Principal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var member = new Dictionary<string, object?>(StringComparer.Ordinal) { ["type"] = PrincipalTypes.Format(principal.Type) };
        if (principal.Id is { } id)
        {
            member["id"] = id;
        }

        if (principal.MinRole is { } role)
        {
            member["minRole"] = Core.Groups.GroupRoles.Format(role);
        }

        return member;
    }

    private static string Message(string reason) => reason switch
    {
        LevelTooHigh => "overrides grant at most edit",
        DuplicatePrincipal => "the same principal appears twice",
        GroupNotSelectable => "pick one of your groups, the owning group, or a group with access to the calendar",
        _ => "pick someone who shares a group with you or sees the calendar",
    };
}

/// <summary>API names of principal types: <c>user</c>, <c>group</c>, <c>anonymous</c>, <c>everyone</c>.</summary>
public static class PrincipalTypes
{
    public static string Format(PrincipalType type) => type switch
    {
        PrincipalType.User => "user",
        PrincipalType.Group => "group",
        PrincipalType.Anonymous => "anonymous",
        PrincipalType.Everyone => "everyone",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown principal type."),
    };
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

    /// <summary>The event moved to another calendar (before/after <c>{ calendarId }</c>; recorded for both plan subjects when they differ).</summary>
    public const string Moved = "event.moved";

    /// <summary>The event's overrides were replaced (before/after: <c>["principal → level", …]</c>).</summary>
    public const string OverridesChanged = "event.overrides.changed";

    /// <summary>Individual shares (<c>user:</c> entries) were revoked because the user lost calendar level (permissions.md §4.6).</summary>
    public const string OverridesRevoked = "event.overrides.revoked";

    /// <summary>The group an entry named was deleted.</summary>
    public const string OverridesRemovedWithGroup = "event.overrides.removed_with_group";

    /// <summary>One occurrence of a series was changed ("this occurrence"; before/after: the exception).</summary>
    public const string OccurrenceUpdated = "event.occurrence.updated";

    /// <summary>One occurrence of a series was cancelled.</summary>
    public const string OccurrenceCancelled = "event.occurrence.cancelled";

    /// <summary>
    /// The series was split at an occurrence ("this and following"): recorded on the original (before/after its
    /// recurrence, <c>newEventId</c>); the new series gets <see cref="Created"/> with <c>splitFrom</c>.
    /// </summary>
    public const string Split = "event.split";
}
