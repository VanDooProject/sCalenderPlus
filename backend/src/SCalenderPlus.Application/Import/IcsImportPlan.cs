using SCalenderPlus.Core.Events;

namespace SCalenderPlus.Application.Import;

/// <summary>What the upload does with one event of the file.</summary>
public enum IcsImportAction
{
    /// <summary>Imported (preview: will be imported).</summary>
    Create,

    /// <summary>Its UID is already in the calendar (or earlier in the file): left out, so re-uploads add nothing.</summary>
    Duplicate,

    /// <summary>Cannot be imported (see <see cref="IcsImportItem.Reason"/>).</summary>
    Skip,
}

/// <summary>Stable codes of skip reasons, duplicates and warnings (the web app localizes them).</summary>
public static class IcsImportCodes
{
    // Duplicates.
    public const string Exists = "exists";
    public const string DuplicateInFile = "duplicate_in_file";

    // Skip reasons.
    public const string StartMissing = "start_missing";
    public const string TimeInvalid = "time_invalid";
    public const string RecurrenceNotSupported = "recurrence_not_supported";
    public const string RecurrenceInvalid = "recurrence_invalid";
    public const string Cancelled = "cancelled";

    // Warnings (the event is imported anyway).
    public const string TimeZoneUnknown = "time_zone_unknown";
    public const string FloatingTime = "floating_time";
    public const string TitleMissing = "title_missing";
    public const string Truncated = "truncated";
    public const string UrlInvalid = "url_invalid";
    public const string PrivateEvent = "private_event";
    public const string EndBeforeStart = "end_before_start";
    public const string TimeAdjusted = "time_adjusted_dst";
    public const string UidGenerated = "uid_generated";
    public const string DatesCapped = "dates_capped";
    public const string RDatePeriodIgnored = "rdate_period_ignored";
    public const string OccurrenceNotFound = "occurrence_not_found";
    public const string OccurrenceIgnored = "occurrence_ignored";
    public const string OrphanOccurrence = "orphan_occurrence";

    // File-level warnings.
    public const string UnsupportedComponents = "unsupported_components";
}

/// <summary>
/// One event of the file (a single event or a series with its overrides) and what the upload does with it. For
/// <see cref="IcsImportAction.Create"/> it carries the prepared row (<see cref="Event"/>, exceptions attached,
/// without calendar and creator yet).
/// </summary>
public sealed class IcsImportItem
{
    public required string Uid { get; init; }

    public required string Title { get; init; }

    /// <summary>The (first occurrence's) times; null when they could not be read.</summary>
    public EventTimes? Times { get; init; }

    /// <summary>Canonical RRULE of a series.</summary>
    public string? Rrule { get; init; }

    /// <summary>Modified occurrences imported with the series (RECURRENCE-ID overrides).</summary>
    public int ModifiedOccurrences { get; set; }

    /// <summary>Cancelled occurrences (EXDATEs and cancelled overrides).</summary>
    public int CancelledOccurrences { get; set; }

    public IcsImportAction Action { get; set; }

    /// <summary>Why it is a duplicate or skipped (<see cref="IcsImportCodes"/>).</summary>
    public string? Reason { get; set; }

    /// <summary>Warnings of an imported event (<see cref="IcsImportCodes"/>), distinct.</summary>
    public SortedSet<string> Warnings { get; } = new(StringComparer.Ordinal);

    public Event? Event { get; init; }
}

/// <summary>The upload's plan: every event of the file in file order, plus file-level warnings.</summary>
public sealed record IcsImportPlan(IReadOnlyList<IcsImportItem> Items, IReadOnlyList<string> Warnings);
