namespace SCalenderPlus.Core.Permissions;

/// <summary>Outcome of an access check, with the error semantics of §8: <c>none</c> hides the resource (404), a visible but insufficient level is a 403.</summary>
public enum AccessCheck
{
    Allowed,

    /// <summary>The principal's level is <c>none</c>: answer 404, never reveal existence.</summary>
    NotFound,

    /// <summary>Visible but the level is too low: 403 <c>insufficient_permission</c> with the required level.</summary>
    Forbidden,
}

/// <summary>Calendar-level actions (§4.5).</summary>
public enum CalendarAction
{
    /// <summary>See the calendar in lists (and busy blocks).</summary>
    View,

    CreateEvent,

    /// <summary>Name, color, time zone and settings.</summary>
    UpdateSettings,

    /// <summary>Grants, share links and group role defaults.</summary>
    ManageSharing,

    /// <summary>Configure LLM import sources into the calendar.</summary>
    ConfigureImport,

    Delete,

    Transfer,
}

/// <summary>Event-level actions (§2.1).</summary>
public enum EventAction
{
    /// <summary>Times and busy state only.</summary>
    ViewBusy,

    /// <summary>All details.</summary>
    ViewDetails,

    /// <summary>Change fields, times, recurrence, attendees.</summary>
    Edit,

    /// <summary>Delete the event or single occurrences.</summary>
    Delete,

    /// <summary>Create, change or delete the event's overrides (only floor holders, §4.4).</summary>
    ChangeOverrides,

    /// <summary>Move to another calendar (also needs <c>contribute</c> on the target, <see cref="AccessPolicy.CanMoveEvent"/>).</summary>
    Move,
}

/// <summary>Required levels of calendar and event actions (§2, §4.5, §4.6) as pure functions.</summary>
public static class AccessPolicy
{
    public static CalendarLevel RequiredLevel(CalendarAction action) => action switch
    {
        CalendarAction.View => CalendarLevel.FreeBusy,
        CalendarAction.CreateEvent => CalendarLevel.Contribute,
        CalendarAction.UpdateSettings or CalendarAction.ManageSharing or CalendarAction.ConfigureImport => CalendarLevel.Manage,
        CalendarAction.Delete or CalendarAction.Transfer => CalendarLevel.Owner,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown calendar action."),
    };

    public static EventLevel RequiredLevel(EventAction action) => action switch
    {
        EventAction.ViewBusy => EventLevel.FreeBusy,
        EventAction.ViewDetails => EventLevel.Read,
        EventAction.Edit or EventAction.Delete => EventLevel.Edit,
        EventAction.ChangeOverrides or EventAction.Move => EventLevel.Manage,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown event action."),
    };

    public static AccessCheck Check(CalendarLevel level, CalendarAction action) =>
        level == CalendarLevel.None ? AccessCheck.NotFound
        : level >= RequiredLevel(action) ? AccessCheck.Allowed
        : AccessCheck.Forbidden;

    public static AccessCheck Check(EventLevel level, EventAction action) =>
        level == EventLevel.None ? AccessCheck.NotFound
        : level >= RequiredLevel(action) ? AccessCheck.Allowed
        : AccessCheck.Forbidden;

    /// <summary>§4.6: moving needs <c>manage</c> on the event and ≥ <c>contribute</c> on the target calendar.</summary>
    public static bool CanMoveEvent(EventLevel source, CalendarLevel target) =>
        source == EventLevel.Manage & target >= CalendarLevel.Contribute;

    /// <summary>
    /// §4.5: managers grant <c>free_busy</c> … <c>manage</c>, never <c>owner</c> and never above their own level.
    /// Applies to adding, changing and removing a grant (old and new level).
    /// </summary>
    public static bool CanGrant(CalendarLevel actor, CalendarLevel granted) =>
        actor >= CalendarLevel.Manage & granted >= CalendarLevel.FreeBusy & granted <= PermissionLevels.Min(actor, CalendarLevel.Manage);

    /// <summary>§6.2: managers set role defaults <c>none</c> … <c>manage</c>, never above their own level.</summary>
    public static bool CanSetRoleDefault(CalendarLevel actor, CalendarLevel level) =>
        actor >= CalendarLevel.Manage & level >= CalendarLevel.None & level <= PermissionLevels.Min(actor, CalendarLevel.Manage);

    /// <summary>Share links carry <c>free_busy</c> or <c>read</c> and are created by managers.</summary>
    public static bool CanCreateShareLink(CalendarLevel actor, CalendarLevel level) =>
        actor >= CalendarLevel.Manage & (level is CalendarLevel.FreeBusy or CalendarLevel.Read);
}
