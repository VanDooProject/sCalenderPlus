namespace SCalenderPlus.Core.Permissions;

/// <summary>
/// Level of a principal on a calendar (docs/architecture/permissions.md §2.2), totally ordered and stored as
/// <c>smallint</c> 0 … 6. The numbers differ from <see cref="EventLevel"/> for the same name: never compare
/// across the two enums, map with <see cref="PermissionLevels.ImpliedEventLevel"/>.
/// </summary>
public enum CalendarLevel : short
{
    None = 0,
    FreeBusy = 1,
    Read = 2,

    /// <summary>Read + create events (the creator floor gives <c>manage</c> on one's own events).</summary>
    Contribute = 3,
    Edit = 4,
    Manage = 5,

    /// <summary>Exactly one principal (a user, or every role-owner of the owning group); never grantable.</summary>
    Owner = 6,
}

/// <summary>
/// Level of a principal on an event (docs/architecture/permissions.md §2.1), totally ordered and stored as
/// <c>smallint</c> 0 … 4. <see cref="Manage"/> is only reachable through a floor; overrides grant at most
/// <see cref="Edit"/>.
/// </summary>
public enum EventLevel : short
{
    /// <summary>The event does not exist for the principal (404, not listed, not in feeds).</summary>
    None = 0,

    /// <summary>Times, all-day flag and transparency only; title "Busy".</summary>
    FreeBusy = 1,
    Read = 2,
    Edit = 3,

    /// <summary>Edit + change the event's overrides and move it.</summary>
    Manage = 4,
}

/// <summary>Level conversions and their API names (lowercase snake case, e.g. <c>free_busy</c>).</summary>
public static class PermissionLevels
{
    public static IReadOnlyList<CalendarLevel> CalendarLevels { get; } =
    [
        CalendarLevel.None, CalendarLevel.FreeBusy, CalendarLevel.Read, CalendarLevel.Contribute,
        CalendarLevel.Edit, CalendarLevel.Manage, CalendarLevel.Owner,
    ];

    public static IReadOnlyList<EventLevel> EventLevels { get; } =
        [EventLevel.None, EventLevel.FreeBusy, EventLevel.Read, EventLevel.Edit, EventLevel.Manage];

    /// <summary>The highest level an override may carry (rule 9: <c>manage</c> only through floors).</summary>
    public const EventLevel MaxOverrideLevel = EventLevel.Edit;

    /// <summary>The default event level a calendar level implies (the table of §2.2).</summary>
    public static EventLevel ImpliedEventLevel(CalendarLevel level) => level switch
    {
        CalendarLevel.None => EventLevel.None,
        CalendarLevel.FreeBusy => EventLevel.FreeBusy,
        CalendarLevel.Read or CalendarLevel.Contribute => EventLevel.Read,
        CalendarLevel.Edit => EventLevel.Edit,
        CalendarLevel.Manage or CalendarLevel.Owner => EventLevel.Manage,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown calendar level."),
    };

    public static string Format(CalendarLevel level) => level switch
    {
        CalendarLevel.None => "none",
        CalendarLevel.FreeBusy => "free_busy",
        CalendarLevel.Read => "read",
        CalendarLevel.Contribute => "contribute",
        CalendarLevel.Edit => "edit",
        CalendarLevel.Manage => "manage",
        CalendarLevel.Owner => "owner",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown calendar level."),
    };

    public static string Format(EventLevel level) => level switch
    {
        EventLevel.None => "none",
        EventLevel.FreeBusy => "free_busy",
        EventLevel.Read => "read",
        EventLevel.Edit => "edit",
        EventLevel.Manage => "manage",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown event level."),
    };

    /// <summary>Parses the exact lowercase name; anything else (including numbers) fails.</summary>
    public static bool TryParse(string? value, out CalendarLevel level)
    {
        foreach (var candidate in CalendarLevels)
        {
            if (string.Equals(value, Format(candidate), StringComparison.Ordinal))
            {
                level = candidate;
                return true;
            }
        }

        level = default;
        return false;
    }

    /// <summary>Parses the exact lowercase name; anything else (including numbers) fails.</summary>
    public static bool TryParse(string? value, out EventLevel level)
    {
        foreach (var candidate in EventLevels)
        {
            if (string.Equals(value, Format(candidate), StringComparison.Ordinal))
            {
                level = candidate;
                return true;
            }
        }

        level = default;
        return false;
    }

    public static EventLevel Min(EventLevel a, EventLevel b) => (EventLevel)Math.Min((short)a, (short)b);

    public static EventLevel Max(EventLevel a, EventLevel b) => (EventLevel)Math.Max((short)a, (short)b);

    public static CalendarLevel Min(CalendarLevel a, CalendarLevel b) => (CalendarLevel)Math.Min((short)a, (short)b);

    public static CalendarLevel Max(CalendarLevel a, CalendarLevel b) => (CalendarLevel)Math.Max((short)a, (short)b);
}
