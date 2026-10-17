using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using SCalenderPlus.Api.Auth;
using SCalenderPlus.Api.Events;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.Application.Import;

namespace SCalenderPlus.Api.Calendars;

/// <summary>
/// One-time ICS upload (api.md "ICS upload", ical-caldav.md §2): <c>POST /calendars/{id}/ics-upload?dryRun=</c>
/// with the file as a <c>text/calendar</c> body (≤ 5 MB). <c>dryRun=true</c> (the default) previews, <c>false</c>
/// imports; both answer the same report (<see cref="IcsImportService"/>). Stateless: the commit sends the file again.
/// </summary>
internal static class IcsUploadEndpoints
{
    public const string ContentType = "text/calendar";

    public static RouteGroupBuilder MapIcsUploadEndpoints(this RouteGroupBuilder calendars)
    {
        calendars.MapPost("/{id:guid}/ics-upload", UploadAsync).WithName("UploadIcs")
            .RequireVerifiedEmail()
            .Accepts<string>(ContentType)
            .WithSummary("Import an .ics file into the calendar (contribute; preview with dryRun=true, the default)")
            .WithDescription("Body: the iCalendar file (Content-Type text/calendar, UTF-8, at most 5 MB and 5,000 VEVENTs: 413 payload_too_large / 422 ics_too_many_events; not iCalendar: 422 ics_invalid). Series keep their RRULE, EXDATEs and RECURRENCE-ID overrides (as exceptions; cancelled ones as cancelled occurrences); TZIDs map to IANA zones (Windows names of Outlook exports too), floating times use X-WR-TIMEZONE or the calendar's zone. Events whose UID the calendar already has are duplicates and left out, so uploading the same file again creates nothing. The uploader becomes the creator; no permission overrides are set. Needs calendar level contribute (below: 403; none: 404), a verified email (403 email_not_verified) and a calendar that is not frozen (409 calendar_frozen).");
        return calendars;
    }

    private static async Task<Ok<IcsUploadResponse>> UploadAsync(
        Guid id,
        [FromQuery] bool? dryRun,
        ClaimsPrincipal principal,
        HttpRequest request,
        IcsImportService imports,
        CancellationToken cancellationToken)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var type) || !type.MediaType.Equals(ContentType, StringComparison.OrdinalIgnoreCase))
        {
            throw new AppException(ErrorCodes.UnsupportedMediaType, "Send the file with Content-Type: text/calendar.");
        }

        var content = await ReadAsync(request, cancellationToken).ConfigureAwait(false);
        var result = await imports.UploadAsync(principal.UserId(), id, content, dryRun ?? true, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(IcsUploadResponse.From(result));
    }

    /// <summary>The body as UTF-8 text (a byte order mark is dropped); more than <see cref="IcsImportService.MaxBytes"/> → 413.</summary>
    private static async Task<string> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var tooLarge = new AppException(ErrorCodes.PayloadTooLarge, $"The file is larger than {IcsImportService.MaxBytes / (1024 * 1024)} MB.");
        if (request.ContentLength > IcsImportService.MaxBytes)
        {
            throw tooLarge;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > IcsImportService.MaxBytes)
            {
                throw tooLarge;
            }

            buffer.Write(chunk, 0, read);
        }

        var bytes = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
        if (bytes.StartsWith(Encoding.UTF8.Preamble))
        {
            bytes = bytes[Encoding.UTF8.Preamble.Length..];
        }

        return Encoding.UTF8.GetString(bytes);
    }
}

/// <summary>
/// What an upload did (or, for a dry run, would do). <see cref="Counts"/> are totals; <see cref="Items"/> lists every
/// event of the file in file order (series once, with their modified and cancelled occurrences counted).
/// </summary>
public sealed record IcsUploadResponse(Guid CalendarId, bool DryRun, IcsUploadCounts Counts, IReadOnlyList<IcsUploadWarning> Warnings, IReadOnlyList<IcsUploadItem> Items)
{
    public static IcsUploadResponse From(IcsImportResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new IcsUploadResponse(
            result.CalendarId,
            result.DryRun,
            new IcsUploadCounts(result.Total, result.Created, result.Duplicates, result.Skipped, result.Series, result.ModifiedOccurrences, result.CancelledOccurrences),
            [.. result.Warnings.Select(w => new IcsUploadWarning(w.Code, w.Count))],
            [.. result.Plan.Items.Select(IcsUploadItem.From)]);
    }
}

/// <param name="Total">Events in the file (a series with its overrides counts once).</param>
/// <param name="Created">Imported — for a dry run: would be imported.</param>
/// <param name="Duplicates">Left out because the calendar (or the file, earlier) already has their UID.</param>
/// <param name="Skipped">Not importable (see the items' <c>reason</c>).</param>
/// <param name="Series">Recurring events among <paramref name="Created"/>.</param>
/// <param name="ModifiedOccurrences">Overridden occurrences of those series (RECURRENCE-ID), imported as exceptions.</param>
/// <param name="CancelledOccurrences">Cancelled occurrences of those series (EXDATE, cancelled overrides).</param>
public sealed record IcsUploadCounts(int Total, int Created, int Duplicates, int Skipped, int Series, int ModifiedOccurrences, int CancelledOccurrences);

/// <summary>A warning code and how many imported events (or the file: 1) it concerns.</summary>
/// <param name="Code">
/// <c>time_zone_unknown</c>, <c>floating_time</c>, <c>title_missing</c>, <c>truncated</c>, <c>url_invalid</c>,
/// <c>private_event</c> (CLASS:PRIVATE — imported events get the calendar's default access), <c>end_before_start</c>,
/// <c>time_adjusted_dst</c>, <c>uid_generated</c>, <c>dates_capped</c>, <c>rdate_period_ignored</c>,
/// <c>occurrence_not_found</c>, <c>occurrence_ignored</c>, <c>orphan_occurrence</c>, <c>unsupported_components</c>.
/// </param>
public sealed record IcsUploadWarning(string Code, int Count);

/// <summary>One event of the file.</summary>
/// <param name="Uid">The UID it is stored with (the file's, or a generated one when it had none or an unusable one).</param>
/// <param name="Rrule">Canonical RRULE of a series.</param>
/// <param name="Action"><c>create</c>, <c>duplicate</c> or <c>skip</c>.</param>
/// <param name="Reason">
/// Duplicates: <c>exists</c> (the calendar has the UID), <c>duplicate_in_file</c>. Skipped: <c>start_missing</c>,
/// <c>time_invalid</c>, <c>recurrence_not_supported</c>, <c>recurrence_invalid</c>, <c>cancelled</c>.
/// </param>
/// <param name="Warnings">Warning codes of an imported event (see <see cref="IcsUploadWarning"/>).</param>
public sealed record IcsUploadItem(
    string Uid,
    string Title,
    EventTimeResponse? Start,
    EventTimeResponse? End,
    bool AllDay,
    string? Rrule,
    int ModifiedOccurrences,
    int CancelledOccurrences,
    string Action,
    string? Reason,
    IReadOnlyList<string> Warnings)
{
    public static IcsUploadItem From(IcsImportItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var times = item.Times is { } t ? EventTimeResponse.From(t) : ((EventTimeResponse Start, EventTimeResponse End)?)null;
        return new IcsUploadItem(
            item.Uid,
            item.Title,
            times?.Start,
            times?.End,
            item.Times?.AllDay ?? false,
            item.Rrule,
            item.ModifiedOccurrences,
            item.CancelledOccurrences,
            item.Action.ToString().ToLowerInvariant(),
            item.Reason,
            item.Action == IcsImportAction.Create ? [.. item.Warnings] : []);
    }
}
