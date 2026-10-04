using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SCalenderPlus.Api.Auth;
using SCalenderPlus.Api.Hosting;
using SCalenderPlus.Application.Calendars;
using SCalenderPlus.Application.Common;

namespace SCalenderPlus.Api.Calendars;

/// <summary>
/// <c>/api/v1/calendars</c> (issue #41, docs/architecture/api.md §4): the calendars the signed-in user sees with
/// their effective level (<c>myLevel</c>, computed by the permission engine). Rules live in
/// <see cref="CalendarService"/> / <c>Core.Permissions.AccessPolicy</c>: level <c>none</c> → 404, too low → 403.
/// A calendar has an <c>ETag</c>; <c>PATCH</c> and <c>DELETE</c> require <c>If-Match</c> (like groups).
/// </summary>
internal static class CalendarEndpoints
{
    public const string Tag = "Calendars";
    private const string ETagSource = "GET /api/v1/calendars/{id}";

    public static RouteGroupBuilder MapCalendarEndpoints(this RouteGroupBuilder v1)
    {
        var calendars = v1.MapGroup("/calendars").WithTags(Tag);

        calendars.MapGet(string.Empty, ListAsync).WithName("ListCalendars")
            .WithSummary("Calendars I see, with my level (cursor-paginated)")
            .WithDescription("Owned by me or one of my groups, or shared with me or my groups; ordered by creation. limit: 1–200 (default 50); cursor: nextCursor of the previous page.");
        calendars.MapPost(string.Empty, CreateAsync).WithName("CreateCalendar")
            .WithSummary("Create a personal calendar, or a group calendar (group admins and owners)")
            .WithDescription("Counts against owned_calendars of the owner's plan (group calendars: the group's billing owner) → 402 plan_limit_reached. Group members below admin: 403; non-members: 404.");
        calendars.MapGet("/{id:guid}", GetAsync).WithName("GetCalendar")
            .WithSummary("A calendar I see (with ETag)")
            .WithDescription("404 for calendars the caller has no level on (no existence leaks).");
        calendars.MapPatch("/{id:guid}", UpdateAsync).WithName("UpdateCalendar")
            .Accepts<UpdateCalendarRequest>(MeEndpoints.MergePatchJson, "application/json")
            .WithSummary("Change name, color, time zone, permission settings or role defaults (manage; JSON Merge Patch, requires If-Match)")
            .WithDescription("Absent or null members stay unchanged; an empty description removes it. Below manage: 403. Role defaults never above the caller's level; a change that would take away the caller's own manage level is 409 permission_self_lockout. Lowering role defaults revokes the individual event shares (user overrides above the new level) of the members who lose level, unless revokeEventShares=false. Frozen calendars: 409 calendar_frozen. If-Match: the ETag of GET /calendars/{id} (or *).");
        calendars.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteCalendar")
            .WithSummary("Delete the calendar with its grants (owners only, requires If-Match)");
        calendars.MapGrantEndpoints();
        calendars.MapPrefsEndpoints(v1);

        return calendars;
    }

    private static async Task<Ok<CalendarListResponse>> ListAsync(
        [FromQuery] int? limit,
        [FromQuery] string? cursor,
        ClaimsPrincipal principal,
        CalendarService calendars,
        CancellationToken cancellationToken)
    {
        var page = await calendars.ListMineAsync(principal.UserId(), PageRequest.Parse(limit, cursor), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new CalendarListResponse([.. page.Items.Select(CalendarResponse.From)], page.NextCursor));
    }

    private static async Task<Created<CalendarResponse>> CreateAsync(
        CreateCalendarRequest request,
        ClaimsPrincipal principal,
        CalendarService calendars,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var view = await calendars.CreateAsync(
            principal.UserId(),
            new NewCalendar(
                request.Name,
                request.DefaultTimeZone,
                request.Description,
                request.Color,
                request.GroupId,
                request.GroupRoleDefaults?.Parse(),
                request.CreatorsManageOwnEvents,
                request.CreatorsMayShareExternally),
            cancellationToken).ConfigureAwait(false);
        var calendar = CalendarResponse.From(view);
        response.Headers.ETag = ETags.Of(calendar);
        return TypedResults.Created($"{ApiV1.BasePath}/calendars/{calendar.Id}", calendar);
    }

    private static async Task<Ok<CalendarResponse>> GetAsync(Guid id, ClaimsPrincipal principal, CalendarService calendars, HttpResponse response, CancellationToken cancellationToken)
    {
        var calendar = CalendarResponse.From(await calendars.GetAsync(principal.UserId(), id, cancellationToken).ConfigureAwait(false));
        response.Headers.ETag = ETags.Of(calendar);
        return TypedResults.Ok(calendar);
    }

    private static async Task<Ok<CalendarResponse>> UpdateAsync(
        Guid id,
        [FromBody] UpdateCalendarRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        [FromQuery] bool? revokeEventShares,
        ClaimsPrincipal principal,
        CalendarService calendars,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var changes = new CalendarChanges(
            request.Name,
            request.Description,
            request.Color,
            request.DefaultTimeZone,
            request.CreatorsManageOwnEvents,
            request.CreatorsMayShareExternally,
            request.GroupRoleDefaults?.Parse());
        var view = await calendars.UpdateAsync(
            principal.UserId(),
            id,
            changes,
            current => ETags.Require(ifMatch, ETags.Of(CalendarResponse.From(current)), ETagSource),
            revokeEventShares ?? true,
            cancellationToken).ConfigureAwait(false);
        var calendar = CalendarResponse.From(view);
        response.Headers.ETag = ETags.Of(calendar);
        return TypedResults.Ok(calendar);
    }

    private static async Task<NoContent> DeleteAsync(
        Guid id,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        ClaimsPrincipal principal,
        CalendarService calendars,
        CancellationToken cancellationToken)
    {
        await calendars.DeleteAsync(
            principal.UserId(),
            id,
            current => ETags.Require(ifMatch, ETags.Of(CalendarResponse.From(current)), ETagSource),
            cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }
}
