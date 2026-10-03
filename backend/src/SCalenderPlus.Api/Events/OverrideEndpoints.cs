using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SCalenderPlus.Api.Auth;
using SCalenderPlus.Api.Hosting;
using SCalenderPlus.Application.Events;

namespace SCalenderPlus.Api.Events;

/// <summary>
/// <c>/api/v1/events/{id}/overrides</c> (issue #46, permissions.md §4.4): the event's own permissions, read and
/// replaced as a whole set by those who may change them — event level <c>manage</c> (calendar managers and owners,
/// the creator with the creator floor). Rules live in <see cref="EventOverrideService"/> and
/// <c>Core.Permissions.OverridePolicy</c>.
/// </summary>
internal static class OverrideEndpoints
{
    private const string ETagSource = "GET /api/v1/events/{id}/overrides";

    public static RouteGroupBuilder MapOverrideEndpoints(this RouteGroupBuilder events)
    {
        events.MapGet("/{id:guid}/overrides", GetAsync).WithName("GetEventOverrides")
            .WithSummary("The event's permission overrides (event level manage, with ETag)")
            .WithDescription("Only those who may change them see them: calendar managers and owners, and the creator while the creator floor applies. Others who see the event: 403 insufficient_permission; no level: 404.");
        events.MapPut("/{id:guid}/overrides", ReplaceAsync).WithName("ReplaceEventOverrides")
            .WithSummary("Replace the event's permission overrides (event level manage; requires If-Match)")
            .WithDescription("Atomic replace of the whole set (an empty list removes all). Levels none … edit; manage, the same principal twice, or a group/user you may not select: 422 override_invalid (violations). Sharing with people outside the calendar's audience needs calendar manage (or the calendar setting creatorsMayShareExternally): else 403 external_sharing_not_allowed (violations). Unchanged and lowered entries are not re-checked. Plan limits: 402 plan_limit_reached (removals always pass). Frozen calendars allow removals only (409 calendar_frozen). If-Match: the ETag of GET /events/{id}/overrides (or *).");
        return events;
    }

    private static async Task<Ok<EventOverridesResponse>> GetAsync(Guid id, ClaimsPrincipal principal, EventOverrideService overrides, HttpResponse response, CancellationToken cancellationToken)
    {
        var body = EventOverridesResponse.From(await overrides.GetAsync(principal.UserId(), id, cancellationToken).ConfigureAwait(false));
        response.Headers.ETag = body.Etag;
        return TypedResults.Ok(body);
    }

    private static async Task<Ok<EventOverridesResponse>> ReplaceAsync(
        Guid id,
        ReplaceOverridesRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        ClaimsPrincipal principal,
        EventOverrideService overrides,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var proposed = request.Parse();
        var view = await overrides.ReplaceAsync(
            principal.UserId(),
            id,
            proposed,
            current => ETags.Require(ifMatch, EventOverridesResponse.From(current).Etag, ETagSource),
            cancellationToken).ConfigureAwait(false);
        var body = EventOverridesResponse.From(view);
        response.Headers.ETag = body.Etag;
        return TypedResults.Ok(body);
    }
}
