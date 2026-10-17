using NodaTime;
using SCalenderPlus.Application.Auditing;
using SCalenderPlus.Application.Calendars;
using SCalenderPlus.Application.Common;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.Application.Events;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Application.Import;

/// <summary>The outcome of an upload (preview or commit): the plan with every item's action, and the totals.</summary>
public sealed record IcsImportResult(Guid CalendarId, bool DryRun, IcsImportPlan Plan)
{
    public int Total => Plan.Items.Count;

    public int Created => Count(IcsImportAction.Create);

    public int Duplicates => Count(IcsImportAction.Duplicate);

    public int Skipped => Count(IcsImportAction.Skip);

    public int Series => Plan.Items.Count(i => i.Action == IcsImportAction.Create && i.Rrule is not null);

    public int ModifiedOccurrences => Plan.Items.Where(i => i.Action == IcsImportAction.Create).Sum(i => i.ModifiedOccurrences);

    public int CancelledOccurrences => Plan.Items.Where(i => i.Action == IcsImportAction.Create).Sum(i => i.CancelledOccurrences);

    /// <summary>Warnings of the imported items and the file, with how many items each concerns.</summary>
    public IReadOnlyList<(string Code, int Count)> Warnings =>
    [
        .. Plan.Items.Where(i => i.Action == IcsImportAction.Create)
            .SelectMany(i => i.Warnings)
            .Concat(Plan.Warnings)
            .GroupBy(code => code, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (g.Key, g.Count())),
    ];

    private int Count(IcsImportAction action) => Plan.Items.Count(i => i.Action == action);
}

/// <summary>
/// One-time ICS upload into a calendar (ical-caldav.md §2, api.md "ICS upload"): <c>Lc ≥ contribute</c> (creating
/// events; frozen calendars refuse), at most <see cref="MaxBytes"/> and <see cref="MaxEvents"/> VEVENTs. A dry run
/// previews what would happen; the commit re-reads the same file and creates the events in one transaction under the
/// calendar's advisory lock (the UID check of event creation), so concurrent uploads of one file cannot both add it.
/// Dedupe is by UID: an event whose UID a live event of the calendar already has is a duplicate and left out — a
/// re-upload of the same file creates nothing (existing events are not updated: one-time import, not a sync). The
/// uploader becomes the creator of every event (creator floor as for events they create); no overrides are set and
/// no plan limit applies (plans count calendars, groups, members and overrides, not events). Every event gets its
/// <c>calendar_changes</c> row (<see cref="EventWriter"/>); the audit trail has one <c>calendar.ics_imported</c> record
/// with the totals instead of one per event.
/// </summary>
public sealed class IcsImportService(
    IAppDbContext db,
    CalendarAccessLoader calendars,
    EventQueryService queries,
    EventWriter writer,
    IAuditLog audit,
    IClock clock)
{
    /// <summary>Largest file accepted (ical-caldav.md §2): 5 MB.</summary>
    public const int MaxBytes = 5 * 1024 * 1024;

    /// <summary>Most VEVENTs per file (overrides of series included).</summary>
    public const int MaxEvents = 5000;

    public async Task<IcsImportResult> UploadAsync(Guid actorId, Guid calendarId, string content, bool dryRun, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var access = await calendars.RequireAsync(actorId, calendarId, CalendarAction.CreateEvent, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (access.Calendar.FrozenAt is not null)
        {
            throw CalendarErrors.Frozen();
        }

        var roots = IcsReader.Read(content, MaxEvents, out var error, out var detail);
        if (roots is null)
        {
            throw error == IcsReadError.TooManyEvents
                ? new AppException(ErrorCodes.IcsTooManyEvents, detail!, new Dictionary<string, object?>(StringComparer.Ordinal) { ["limit"] = new { max = MaxEvents } })
                : new AppException(ErrorCodes.IcsInvalid, detail!);
        }

        var zone = DateTimeZoneProviders.Tzdb[access.Calendar.DefaultTimeZone];
        var plan = IcsImportPlanner.Plan(roots, zone, calendarId, actorId, clock.Now());
        var result = new IcsImportResult(calendarId, dryRun, plan);
        if (dryRun)
        {
            await MarkExistingAsync(plan, calendarId, cancellationToken).ConfigureAwait(false);
            return result;
        }

        await db.InTransactionAsync(async ct =>
        {
            await db.LockAsync(calendarId, ct).ConfigureAwait(false); // the UID check of event creation (EventService)
            await MarkExistingAsync(plan, calendarId, ct).ConfigureAwait(false);
            foreach (var item in plan.Items.Where(i => i.Action == IcsImportAction.Create))
            {
                writer.Add(item.Event!);
            }

            audit.Record(
                CalendarAuditActions.IcsImported,
                CalendarAuditActions.ResourceType,
                calendarId.ToString(),
                null,
                new
                {
                    result.Total,
                    result.Created,
                    result.Duplicates,
                    result.Skipped,
                    result.Series,
                    result.ModifiedOccurrences,
                    result.CancelledOccurrences,
                },
                await CalendarAudit.BillingSubjectAsync(db, access.Calendar, ct).ConfigureAwait(false));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task MarkExistingAsync(IcsImportPlan plan, Guid calendarId, CancellationToken cancellationToken)
    {
        var candidates = plan.Items.Where(i => i.Action == IcsImportAction.Create).ToList();
        var existing = await queries.ExistingUidsAsync(calendarId, [.. candidates.Select(i => i.Uid)], cancellationToken).ConfigureAwait(false);
        foreach (var item in candidates.Where(i => existing.Contains(i.Uid)))
        {
            item.Action = IcsImportAction.Duplicate;
            item.Reason = IcsImportCodes.Exists;
        }
    }
}
