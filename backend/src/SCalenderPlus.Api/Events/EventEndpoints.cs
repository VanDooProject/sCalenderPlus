using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SCalenderPlus.Api.Auth;
using SCalenderPlus.Api.Hosting;
using SCalenderPlus.Application.Events;
using SCalenderPlus.Core.Events;

namespace SCalenderPlus.Api.Events;

/// <summary>
/// <c>/api/v1/events</c> (issues #44, #45, docs/architecture/api.md §4): single events with their effective level
/// (<c>myLevel</c>, from the permission engine). Rules live in <see cref="EventService"/> /
/// <see cref="EventQueryService"/>: level <c>none</c> → 404, too low → 403; callers below <c>read</c> get the busy
/// projection. An event has an <c>ETag</c>; <c>PATCH</c> and <c>DELETE</c> require <c>If-Match</c>.
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
            .WithDescription("Events of every calendar I see (or only calendarIds; unknown or invisible ids are ignored) plus events shared with me, overlapping [from, to) (RFC 3339 instants; at most 13 months), ordered by start; each with myLevel. free_busy events come as the busy projection (title null, times only); transparent events are left out for free_busy, and none-level events never appear. All-day events are dates: with timeZone (IANA) they are placed by their dates in that zone, without it every all-day event whose dates overlap the window in some zone (UTC−12 … UTC+14) is returned. At most 5,000 events (truncated: true when more matched).");
        events.MapPost(string.Empty, CreateAsync).WithName("CreateEvent")
            .WithSummary("Create a single event in a calendar (contribute)")
            .WithDescription("Timed ({ dateTime, timeZone }, zone default: the calendar's) or all-day ({ date }, end exclusive). A local time in a DST gap is shifted forward and reported in warnings (time_shifted_dst_gap); an ambiguous one takes the earlier offset (time_ambiguous_earlier_offset). Below contribute: 403; no level: 404. Frozen calendars: 409 calendar_frozen. Recurrence: 422 recurrence_not_supported. Duplicate uid: 409 uid_conflict.");
        events.MapGet("/{id:guid}", GetAsync).WithName("GetEvent")
            .WithSummary("An event I see (with ETag)")
            .WithDescription("free_busy callers get the busy projection (times only, title null). 404 for events the caller has no level on, and for transparent events at free_busy (no existence leaks).");
        events.MapPatch("/{id:guid}", UpdateAsync).WithName("UpdateEvent")
            .Accepts<UpdateEventRequest>(MeEndpoints.MergePatchJson, "application/json")
            .WithSummary("Change an event (edit; JSON Merge Patch, requires If-Match)")
            .WithDescription("Absent or null members stay unchanged; an empty description, location, url or color, or empty categories, remove the value. Below edit: 403. Frozen calendars: 409 calendar_frozen. If-Match: the ETag of GET /events/{id} (or *); stale: 412.");
        events.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteEvent")
            .WithSummary("Delete an event (edit; soft delete, requires If-Match)");

        return events;
    }

    private static async Task<Ok<EventWindowResponse>> WindowAsync(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid[]? calendarIds,
        [FromQuery] string? timeZone,
        ClaimsPrincipal principal,
        EventService events,
        CancellationToken cancellationToken)
    {
        var window = await events.WindowAsync(principal.UserId(), EventWindows.Parse(from, to, calendarIds, timeZone), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new EventWindowResponse(
            [.. window.Items.Select(ListItem)],
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
            new NewEvent(request.CalendarId!.Value, request.Start!.ToInput(), request.End!.ToInput(), request.Details(), request.Uid, request.Recurrence is not null),
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
            new EventChanges(request.Details(), request.Start?.ToInput(), request.End?.ToInput(), request.Recurrence is not null),
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

    /// <summary>A window item: with its <c>etag</c> unless it is the busy projection (nothing to change there).</summary>
    private static EventResponse ListItem(EventView view)
    {
        var item = EventResponse.From(view);
        return EventVisibility.IsBusyOnly(view.Level) ? item : item.WithEtag();
    }

    /// <summary>The body (with warnings, if any) and the <c>ETag</c> header of a created or changed event.</summary>
    private static EventResponse Respond(EventResult result, HttpResponse response)
    {
        var body = EventResponse.From(result.View);
        response.Headers.ETag = body.HeaderETag();
        return result.Adjustments.Count == 0 ? body : body with { Warnings = [.. result.Adjustments.Select(EventWarningResponse.From)] };
    }
}
