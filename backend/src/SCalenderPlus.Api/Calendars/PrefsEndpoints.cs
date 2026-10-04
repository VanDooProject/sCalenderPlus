using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SCalenderPlus.Api.Auth;
using SCalenderPlus.Api.Hosting;
using SCalenderPlus.Application.Calendars;
using SCalenderPlus.Core.Calendars;

namespace SCalenderPlus.Api.Calendars;

/// <summary>
/// The personal calendar overlay (issue #56, api.md "My calendar prefs"): <c>GET /me/calendar-prefs</c> lists the
/// caller's overlays, <c>GET/PUT /calendars/{id}/prefs</c> read and replace one (<see cref="CalendarPrefsService"/>).
/// <c>PUT</c> requires <c>If-Match</c> with the overlay's ETag (also for a calendar without one yet: the ETag of
/// the defaults that <c>GET</c> returns) or <c>*</c>.
/// </summary>
internal static class PrefsEndpoints
{
    private const string ETagSource = "GET /api/v1/calendars/{id}/prefs";

    public static RouteGroupBuilder MapPrefsEndpoints(this RouteGroupBuilder calendars, RouteGroupBuilder v1)
    {
        v1.MapGet("/me/calendar-prefs", ListAsync).WithTags(CalendarEndpoints.Tag).WithName("ListMyCalendarPrefs")
            .WithSummary("My calendar overlays (hidden, color)")
            .WithDescription("Only calendars with an overlay are listed; every other calendar is shown in its own color. Overlays of calendars the caller no longer sees may be listed: show only those of GET /calendars.");
        calendars.MapGet("/{id:guid}/prefs", GetAsync).WithName("GetCalendarPrefs")
            .WithSummary("My overlay of a calendar I see (with ETag)")
            .WithDescription("The defaults (shown, calendar color) when none is stored. No level: 404.");
        calendars.MapPut("/{id:guid}/prefs", PutAsync).WithName("PutCalendarPrefs")
            .WithSummary("Replace my overlay of a calendar I see (requires If-Match)")
            .WithDescription("Personal only (nobody else sees it); any level ≥ free_busy, frozen calendars included. color null = the calendar's color. If-Match: the ETag of GET /calendars/{id}/prefs, the etag of GET /me/calendar-prefs, or *.");
        return calendars;
    }

    private static async Task<Ok<CalendarPrefsListResponse>> ListAsync(ClaimsPrincipal principal, CalendarPrefsService prefs, CancellationToken cancellationToken)
    {
        var items = await prefs.ListMineAsync(principal.UserId(), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new CalendarPrefsListResponse([.. items.Select(CalendarPrefsResponse.From)]));
    }

    private static async Task<Ok<CalendarPrefsResponse>> GetAsync(Guid id, ClaimsPrincipal principal, CalendarPrefsService prefs, HttpResponse response, CancellationToken cancellationToken)
    {
        var view = CalendarPrefsResponse.From(await prefs.GetAsync(principal.UserId(), id, cancellationToken).ConfigureAwait(false));
        response.Headers.ETag = view.Etag;
        return TypedResults.Ok(view);
    }

    private static async Task<Ok<CalendarPrefsResponse>> PutAsync(
        Guid id,
        PutCalendarPrefsRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        ClaimsPrincipal principal,
        CalendarPrefsService prefs,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var view = CalendarPrefsResponse.From(await prefs.PutAsync(
            principal.UserId(),
            id,
            request.Hidden!.Value,
            request.Color,
            current => ETags.Require(ifMatch, CalendarPrefsResponse.ETagOf(current), ETagSource),
            cancellationToken).ConfigureAwait(false));
        response.Headers.ETag = view.Etag;
        return TypedResults.Ok(view);
    }
}

/// <summary>My overlay of a calendar.</summary>
/// <param name="Hidden">Left out of my calendar views.</param>
/// <param name="Color"><c>#rrggbb</c> shown instead of the calendar's color; null = the calendar's.</param>
/// <param name="Etag">The <c>If-Match</c> of <c>PUT /calendars/{id}/prefs</c>.</param>
public sealed record CalendarPrefsResponse(Guid CalendarId, bool Hidden, string? Color, string Etag)
{
    public static CalendarPrefsResponse From(CalendarPrefsView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new CalendarPrefsResponse(view.CalendarId, view.Hidden, view.Color, ETagOf(view));
    }

    public static string ETagOf(CalendarPrefsView view) => ETags.Of(view);
}

public sealed record CalendarPrefsListResponse(IReadOnlyList<CalendarPrefsResponse> Items);

/// <summary>The full overlay (<c>PUT</c>): <c>hidden</c> is required; absent <c>color</c> or null = the calendar's color.</summary>
public sealed class PutCalendarPrefsRequest
{
    /// <summary>Required: true leaves the calendar out of my views.</summary>
    [Required]
    public bool? Hidden { get; init; }

    /// <summary><c>#rrggbb</c>, or null for the calendar's color.</summary>
    [StringLength(Calendar.ColorLength)]
    public string? Color { get; init; }
}
