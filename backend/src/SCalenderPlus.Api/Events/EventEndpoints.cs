using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SCalenderPlus.Api.Auth;
using SCalenderPlus.Api.Hosting;
using SCalenderPlus.Application.Events;
using SCalenderPlus.Core.Events;

namespace SCalenderPlus.Api.Events;

/// <summary>
/// <c>/api/v1/events</c> (issues #44, #45, #50, #51, docs/architecture/api.md §4): events and series with their
/// effective level (<c>myLevel</c>, from the permission engine). Rules live in <see cref="EventService"/> /
/// <see cref="EventOccurrenceService"/> / <see cref="EventQueryService"/>: level <c>none</c> → 404, too low → 403;
/// callers below <c>read</c> get the busy projection. An event has an <c>ETag</c> (a series': of its master, which
/// carries the exceptions); every change requires <c>If-Match</c>, occurrence edits included.
/// </summary>
internal static class EventEndpoints
{
    public const string Tag = "Events";
    private const string ETagSource = "GET /api/v1/events/{id}";

    public static RouteGroupBuilder MapEventEndpoints(this RouteGroupBuilder v1)
    {
        var events = v1.MapGroup("/events").WithTags(Tag);

        events.MapGet(string.Empty, WindowAsync).WithName("ListEvents")
            .WithSummary("Events in a time window, as I see them (not paged)")
            .WithDescription("Events of every calendar I see (or only calendarIds; unknown or invisible ids are ignored) plus events shared with me, overlapping [from, to) (RFC 3339 instants; at most 13 months), ordered by start; each with myLevel. free_busy events come as the busy projection (title null, times only); transparent events are left out for free_busy, and none-level events never appear. All-day events are dates: with timeZone (IANA) they are placed by their dates in that zone, without it every all-day event whose dates overlap the window in some zone (UTC−12 … UTC+14) is returned. Series: without expand their master (with recurrence and exceptions) when an occurrence overlaps the window (sync clients); with expand=occurrences each occurrence in the window (occurrenceId, recurrenceId, exceptions applied, cancelled ones left out; etag = the series' ETag) for calendar views. At most 5,000 items (truncated: true when more matched; at most 1,000 occurrences per series).");
        events.MapPost(string.Empty, CreateAsync).WithName("CreateEvent")
            .WithSummary("Create an event or a series in a calendar (contribute)")
            .WithDescription("Timed ({ dateTime, timeZone }, zone default: the calendar's) or all-day ({ date }, end exclusive). A local time in a DST gap is shifted forward and reported in warnings (time_shifted_dst_gap); an ambiguous one takes the earlier offset (time_ambiguous_earlier_offset). With recurrence ({ rrule, rdates?, exdates? }) a series whose first occurrence is start/end; unsupported RRULE parts: 422 recurrence_not_supported, malformed: 422 recurrence_invalid. Below contribute: 403; no level: 404. Frozen calendars: 409 calendar_frozen. Duplicate uid: 409 uid_conflict.");
        events.MapGet("/{id:guid}", GetAsync).WithName("GetEvent")
            .WithSummary("An event I see (with ETag)")
            .WithDescription("free_busy callers get the busy projection (times only, title null). 404 for events the caller has no level on, and for transparent events at free_busy (no existence leaks).");
        events.MapPatch("/{id:guid}", UpdateAsync).WithName("UpdateEvent")
            .Accepts<UpdateEventRequest>(MeEndpoints.MergePatchJson, "application/json")
            .WithSummary("Change an event, or all occurrences of a series (edit; JSON Merge Patch, requires If-Match)")
            .WithDescription("Absent or null members stay unchanged; an empty description, location, url or color, or empty categories, remove the value. recurrence: a new rule (or null to stop recurring). Series: when the first occurrence moves, the exceptions (and rdates/exdates unless recurrence is given) shift with it; exceptions that no longer match an occurrence are dropped and listed in droppedExceptions. Below edit: 403. Frozen calendars: 409 calendar_frozen. If-Match: the ETag of GET /events/{id} (or *); stale: 412.");
        events.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteEvent")
            .WithSummary("Delete an event or a whole series (edit; soft delete, requires If-Match)")
            .WithDescription("Below edit: 403; no level: 404. Allowed in frozen calendars (cleanup). If-Match: the ETag of GET /events/{id} (or *); stale: 412.");

        events.MapPatch("/{id:guid}/occurrences/{recurrenceId}", UpdateOccurrenceAsync).WithName("UpdateOccurrence")
            .Accepts<UpdateOccurrenceRequest>(MeEndpoints.MergePatchJson, "application/json")
            .WithSummary("Change one occurrence of a series (edit; JSON Merge Patch, requires If-Match of the series)")
            .WithDescription("This occurrence only: title, description, location, status, transparency and its times (same kind and zone as the series) — stored as an exception keyed by recurrenceId (the occurrence's original start: UTC instant, or date for all-day series). Values equal to the series' follow the series again. 404: not a series, or no such occurrence (cancelled ones included). Below edit on the series: 403. Frozen calendars: 409. If-Match: the ETag of GET /events/{id} (or *); stale: 412. The response is the occurrence; its ETag header the series' new ETag.");
        events.MapDelete("/{id:guid}/occurrences/{recurrenceId}", CancelOccurrenceAsync).WithName("CancelOccurrence")
            .WithSummary("Cancel one occurrence of a series (edit; requires If-Match of the series)")
            .WithDescription("The occurrence disappears (an exception marked cancelled; iCalendar EXDATE). 404: not a series, or no such (live) occurrence. Below edit: 403. Frozen calendars: 409. If-Match: the ETag of GET /events/{id} (or *).");
        events.MapPost("/{id:guid}/split", SplitAsync).WithName("SplitEvent")
            .WithSummary("Change this and the following occurrences: split the series (edit; requires If-Match)")
            .WithDescription("The series ends before the occurrence recurrenceId (UNTIL, or a smaller COUNT); a new series (new id and UID, relatedTo the original's UID) starts there with the original's creator, a copy of its permission overrides (no plan check), its later rdates/exdates and exceptions, and the other members applied as a merge patch (like PATCH /events/{id}; exceptions that no longer match are dropped and listed in droppedExceptions). The first occurrence: 400 (change the series instead). 404: not a series or no such occurrence. Below edit: 403. Frozen calendars: 409. If-Match: the ETag of GET /events/{id} (or *). 201 with the new series.");

        events.MapPost("/{id:guid}/move", MoveAsync).WithName("MoveEvent")
            .WithSummary("Move an event to another calendar (event manage, target contribute; requires If-Match)")
            .WithDescription("Below manage on the event: 403; target unknown or invisible: 404; target below contribute: 403 (calendar levels). The event's overrides travel and are re-validated as if you set them in the target: those you could not set there (no floor on the event in the target, or external sharing without the right to it) are listed in 409 override_invalid_in_target (violations). The target owner's plan counts them (402). UID taken in the target: 409 uid_conflict. Frozen target: 409 calendar_frozen (moving out of a frozen calendar is allowed). Same calendar: 400. If-Match: the ETag of GET /events/{id} (or *); the response carries the event as seen in its new calendar.");
        events.MapOverrideEndpoints();
        return events;
    }

    private static async Task<Ok<EventWindowResponse>> WindowAsync(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid[]? calendarIds,
        [FromQuery] string? timeZone,
        [FromQuery] string? expand,
        ClaimsPrincipal principal,
        EventService events,
        CancellationToken cancellationToken)
    {
        var window = await events.WindowAsync(principal.UserId(), EventWindows.Parse(from, to, calendarIds, timeZone, expand), cancellationToken).ConfigureAwait(false);
        var seriesETags = new Dictionary<Guid, string>();
        return TypedResults.Ok(new EventWindowResponse(
            [.. window.Items.Select(view => ListItem(view, seriesETags))],
            window.Truncated));
    }

    private static async Task<Created<EventResponse>> CreateAsync(
        CreateEventRequest request,
        ClaimsPrincipal principal,
        EventService events,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var result = await events.CreateAsync(
            principal.UserId(),
            new NewEvent(request.CalendarId!.Value, request.Start!.ToInput(), request.End!.ToInput(), request.Details(), request.Uid, request.Recurrence?.ToInput()),
            cancellationToken).ConfigureAwait(false);
        var body = Respond(result, response);
        return TypedResults.Created($"{ApiV1.BasePath}/events/{body.Id}", body);
    }

    private static async Task<Ok<EventResponse>> GetAsync(Guid id, ClaimsPrincipal principal, EventService events, HttpResponse response, CancellationToken cancellationToken)
    {
        var body = EventResponse.From(await events.GetAsync(principal.UserId(), id, cancellationToken).ConfigureAwait(false));
        response.Headers.ETag = body.HeaderETag();
        return TypedResults.Ok(body);
    }

    private static async Task<Ok<EventResponse>> UpdateAsync(
        Guid id,
        [FromBody] UpdateEventRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        ClaimsPrincipal principal,
        EventService events,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var result = await events.UpdateAsync(
            principal.UserId(),
            id,
            new EventChanges(request.Details(), request.Start?.ToInput(), request.End?.ToInput(), request.RecurrenceGiven, request.Recurrence?.ToInput()),
            current => ETags.Require(ifMatch, EventResponse.From(current).HeaderETag(), ETagSource),
            cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(Respond(result, response));
    }

    private static async Task<NoContent> DeleteAsync(
        Guid id,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        ClaimsPrincipal principal,
        EventService events,
        CancellationToken cancellationToken)
    {
        await events.DeleteAsync(
            principal.UserId(),
            id,
            current => ETags.Require(ifMatch, EventResponse.From(current).HeaderETag(), ETagSource),
            cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<EventResponse>> UpdateOccurrenceAsync(
        Guid id,
        string recurrenceId,
        [FromBody] UpdateOccurrenceRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        ClaimsPrincipal principal,
        EventOccurrenceService occurrences,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var result = await occurrences.UpdateAsync(
            principal.UserId(),
            id,
            recurrenceId,
            request.ToChanges(),
            current => ETags.Require(ifMatch, EventResponse.SeriesETag(current), ETagSource),
            cancellationToken).ConfigureAwait(false);
        var body = EventResponse.From(result.View);
        response.Headers.ETag = EventResponse.SeriesETag(result.View);
        return TypedResults.Ok(result.Adjustments.Count == 0 ? body : body with { Warnings = [.. result.Adjustments.Select(EventWarningResponse.From)] });
    }

    private static async Task<NoContent> CancelOccurrenceAsync(
        Guid id,
        string recurrenceId,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        ClaimsPrincipal principal,
        EventOccurrenceService occurrences,
        CancellationToken cancellationToken)
    {
        await occurrences.CancelAsync(
            principal.UserId(),
            id,
            recurrenceId,
            current => ETags.Require(ifMatch, EventResponse.SeriesETag(current), ETagSource),
            cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<Created<EventResponse>> SplitAsync(
        Guid id,
        SplitEventRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        ClaimsPrincipal principal,
        EventOccurrenceService occurrences,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var result = await occurrences.SplitAsync(
            principal.UserId(),
            id,
            request.RecurrenceId!,
            request.ToChanges(),
            current => ETags.Require(ifMatch, EventResponse.SeriesETag(current), ETagSource),
            cancellationToken).ConfigureAwait(false);
        var body = Respond(result, response);
        return TypedResults.Created($"{ApiV1.BasePath}/events/{body.Id}", body);
    }

    private static async Task<Ok<EventResponse>> MoveAsync(
        Guid id,
        MoveEventRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        ClaimsPrincipal principal,
        EventMoveService moves,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var view = await moves.MoveAsync(
            principal.UserId(),
            id,
            request.TargetCalendarId!.Value,
            current => ETags.Require(ifMatch, EventResponse.From(current).HeaderETag(), ETagSource),
            cancellationToken).ConfigureAwait(false);
        var body = EventResponse.From(view);
        response.Headers.ETag = body.HeaderETag();
        return TypedResults.Ok(body);
    }

    /// <summary>
    /// A window item: with its <c>etag</c> unless it is the busy projection (nothing to change there); occurrences
    /// carry their series' ETag (computed once per series).
    /// </summary>
    private static EventResponse ListItem(EventView view, Dictionary<Guid, string> seriesETags)
    {
        var item = EventResponse.From(view);
        if (EventVisibility.IsBusyOnly(view.Level))
        {
            return item;
        }

        if (view.Occurrence is null)
        {
            return item.WithEtag();
        }

        if (!seriesETags.TryGetValue(view.Event.Id, out var etag))
        {
            seriesETags[view.Event.Id] = etag = EventResponse.SeriesETag(view);
        }

        return item with { Etag = etag };
    }

    /// <summary>The body (with warnings and dropped exceptions, if any) and the <c>ETag</c> header of a created or changed event.</summary>
    private static EventResponse Respond(EventResult result, HttpResponse response)
    {
        var body = EventResponse.From(result.View);
        response.Headers.ETag = body.HeaderETag();
        return body with
        {
            Warnings = result.Adjustments.Count == 0 ? null : [.. result.Adjustments.Select(EventWarningResponse.From)],
            DroppedExceptions = result.DroppedExceptions is { Count: > 0 } dropped ? dropped : null,
        };
    }
}
