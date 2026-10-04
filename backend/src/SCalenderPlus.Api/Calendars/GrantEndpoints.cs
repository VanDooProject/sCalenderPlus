using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SCalenderPlus.Api.Auth;
using SCalenderPlus.Api.Hosting;
using SCalenderPlus.Application.Calendars;
using SCalenderPlus.Application.Common;

namespace SCalenderPlus.Api.Calendars;

/// <summary>
/// <c>/api/v1/calendars/{id}/grants</c> (issue #42, docs/architecture/api.md §4): calendar managers share the
/// calendar with users and groups. Rules live in <see cref="CalendarGrantService"/> /
/// <c>Core.Permissions.AccessPolicy.CanGrant</c>. Changing or removing a grant requires <c>If-Match</c> with the
/// grant's <c>etag</c> from the list (or <c>*</c>).
/// </summary>
internal static class GrantEndpoints
{
    private const string ETagSource = "GET /api/v1/calendars/{id}/grants";

    public static RouteGroupBuilder MapGrantEndpoints(this RouteGroupBuilder calendars)
    {
        calendars.MapGet("/{id:guid}/grants", ListAsync).WithName("ListCalendarGrants")
            .WithSummary("The calendar's grants (manage; cursor-paginated)")
            .WithDescription("Ordered by creation. Below manage: 403; no level: 404.");
        calendars.MapPost("/{id:guid}/grants", CreateAsync).WithName("CreateCalendarGrant")
            .WithSummary("Share the calendar with a user or a group (manage)")
            .WithDescription("Levels free_busy … manage, never owner or above the caller's level (400 validation_failed). Principals: users who share a group with the caller or already see the calendar; the caller's groups or groups that already have a grant (else 400). One grant per principal (409 conflict). Frozen calendars: 409 calendar_frozen.");
        calendars.MapPatch("/{id:guid}/grants/{grantId:guid}", UpdateAsync).WithName("UpdateCalendarGrant")
            .Accepts<UpdateGrantRequest>(MeEndpoints.MergePatchJson, "application/json")
            .WithSummary("Change a grant's level (manage; requires If-Match)")
            .WithDescription("Old and new level at most the caller's own. A change that would take away the caller's own manage level is 409 permission_self_lockout. Lowering revokes the individual event shares (user overrides above the new level) of those who lose level, unless revokeEventShares=false. Frozen calendars: raising is 409 calendar_frozen, lowering is allowed. If-Match: the grant's etag (or *).");
        calendars.MapDelete("/{id:guid}/grants/{grantId:guid}", DeleteAsync).WithName("DeleteCalendarGrant")
            .WithSummary("Remove a grant (manage; requires If-Match)")
            .WithDescription("A removal that would take away the caller's own manage level is 409 permission_self_lockout. Revokes the individual event shares (user overrides above what the calendar still gives them) of those who lose level, unless revokeEventShares=false. Allowed in frozen calendars.");

        return calendars;
    }

    private static async Task<Ok<GrantListResponse>> ListAsync(
        Guid id,
        [FromQuery] int? limit,
        [FromQuery] string? cursor,
        ClaimsPrincipal principal,
        CalendarGrantService grants,
        CancellationToken cancellationToken)
    {
        var page = await grants.ListAsync(principal.UserId(), id, PageRequest.Parse(limit, cursor), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new GrantListResponse([.. page.Items.Select(GrantResponse.From)], page.NextCursor));
    }

    private static async Task<Created<GrantResponse>> CreateAsync(
        Guid id,
        CreateGrantRequest request,
        ClaimsPrincipal principal,
        CalendarGrantService grants,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var view = await grants.CreateAsync(principal.UserId(), id, request.Principal.Parse(), GrantLevels.Parse(request.Level), cancellationToken).ConfigureAwait(false);
        var grant = GrantResponse.From(view);
        response.Headers.ETag = grant.Etag;
        return TypedResults.Created($"{ApiV1.BasePath}/calendars/{id}/grants/{grant.Id}", grant);
    }

    private static async Task<Ok<GrantResponse>> UpdateAsync(
        Guid id,
        Guid grantId,
        [FromBody] UpdateGrantRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        [FromQuery] bool? revokeEventShares,
        ClaimsPrincipal principal,
        CalendarGrantService grants,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var level = GrantLevels.Parse(request.Level);
        var view = await grants.UpdateAsync(
            principal.UserId(),
            id,
            grantId,
            level,
            current => ETags.Require(ifMatch, GrantResponse.ETagOf(current), ETagSource),
            revokeEventShares ?? true,
            cancellationToken).ConfigureAwait(false);
        var grant = GrantResponse.From(view);
        response.Headers.ETag = grant.Etag;
        return TypedResults.Ok(grant);
    }

    private static async Task<NoContent> DeleteAsync(
        Guid id,
        Guid grantId,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        [FromQuery] bool? revokeEventShares,
        ClaimsPrincipal principal,
        CalendarGrantService grants,
        CancellationToken cancellationToken)
    {
        await grants.DeleteAsync(
            principal.UserId(),
            id,
            grantId,
            current => ETags.Require(ifMatch, GrantResponse.ETagOf(current), ETagSource),
            revokeEventShares ?? true,
            cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }
}
