using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using SCalenderPlus.Api.Auth;
using SCalenderPlus.Api.Hosting;
using SCalenderPlus.Application.Calendars;
using SCalenderPlus.Application.Common;
using SCalenderPlus.Core.Calendars;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Api.Calendars;

/// <summary>
/// Anonymous share links of a calendar (api.md "Share links", permissions.md §3): calendar managers create
/// (<c>POST /calendars/{id}/share-links</c>, verified email), list and revoke (<c>DELETE /share-links/{id}</c>) them
/// (<see cref="ShareLinkService"/>). A link's feed is <c>/ical/v1/s/{token}.ics</c>; its URL is returned only at creation.
/// </summary>
internal static class ShareLinkEndpoints
{
    public static RouteGroupBuilder MapShareLinkEndpoints(this RouteGroupBuilder calendars, RouteGroupBuilder v1)
    {
        calendars.MapGet("/{id:guid}/share-links", ListAsync).WithName("ListShareLinks")
            .WithSummary("The calendar's share links (manage; without secrets)")
            .WithDescription("Links that are not revoked, ordered by creation (expired ones too, with expiresAt). Below manage: 403; no level: 404.");
        calendars.MapPost("/{id:guid}/share-links", CreateAsync).WithName("CreateShareLink")
            .RequireVerifiedEmail()
            .WithSummary("Create an anonymous share link (manage; the URL is shown once)")
            .WithDescription("level: free_busy or read — a ceiling for everyone holding the link (event overrides for everyone/anonymous only restrict further). expiresInDays: 1–3650, absent = never. The response carries the secret and the feed URLs (https and webcal); only the hash is stored. Needs a verified email (403 email_not_verified). Frozen calendars: 409 calendar_frozen; at most 20 active links (409 conflict).");
        v1.MapDelete("/share-links/{id:guid}", RevokeAsync).WithTags(CalendarEndpoints.Tag).WithName("RevokeShareLink")
            .WithSummary("Revoke a share link (manage on its calendar; idempotent)")
            .WithDescription("The link's feed answers 404 at once. Allowed in frozen calendars. Unknown links and calendars without a level: 404.");
        return calendars;
    }

    private static async Task<Ok<ShareLinkListResponse>> ListAsync(Guid id, ClaimsPrincipal principal, ShareLinkService links, CancellationToken cancellationToken)
    {
        var items = await links.ListAsync(principal.UserId(), id, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new ShareLinkListResponse([.. items.Select(ShareLinkResponse.From)]));
    }

    private static async Task<Created<IssuedShareLinkResponse>> CreateAsync(
        Guid id,
        CreateShareLinkRequest request,
        ClaimsPrincipal principal,
        ShareLinkService links,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var level = PermissionLevels.TryParse(request.Level, out CalendarLevel parsed) && parsed is CalendarLevel.FreeBusy or CalendarLevel.Read
            ? parsed
            : throw Validation.Failed("level", "Use free_busy or read.");
        var issued = await links.CreateAsync(principal.UserId(), id, level, request.Label, request.ExpiresInDays, cancellationToken).ConfigureAwait(false);
        response.Headers.CacheControl = "no-store";
        return TypedResults.Created(
            $"{ApiV1.BasePath}/calendars/{id}/share-links",
            new IssuedShareLinkResponse(ShareLinkResponse.From(issued.Link), issued.Secret, issued.Url, issued.WebcalUrl));
    }

    private static async Task<NoContent> RevokeAsync(Guid id, ClaimsPrincipal principal, ShareLinkService links, CancellationToken cancellationToken)
    {
        await links.RevokeAsync(principal.UserId(), id, cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }
}

/// <summary>An anonymous share link (never contains the secret).</summary>
/// <param name="Level"><c>free_busy</c> or <c>read</c>: the most any link holder sees.</param>
/// <param name="ExpiresAt">The link stops working then; null = never.</param>
/// <param name="LastUsedAt">Last feed request (updated at most every 15 minutes); null = never used.</param>
public sealed record ShareLinkResponse(
    Guid Id,
    Guid CalendarId,
    string Level,
    string? Label,
    DateTimeOffset? ExpiresAt,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt)
{
    public static ShareLinkResponse From(ShareLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        return new ShareLinkResponse(
            link.Id,
            link.CalendarId,
            PermissionLevels.Format(link.Level),
            link.Label,
            link.ExpiresAt?.ToDateTimeOffset(),
            link.CreatedBy,
            link.CreatedAt.ToDateTimeOffset(),
            link.LastUsedAt?.ToDateTimeOffset());
    }
}

/// <summary>A new share link with its secret — returned only now.</summary>
/// <param name="Token">The secret (43 characters, base64url).</param>
/// <param name="Url">The feed URL (<c>https</c>): <c>/ical/v1/s/{token}.ics</c>.</param>
/// <param name="WebcalUrl">The same URL with the <c>webcal</c> scheme.</param>
public sealed record IssuedShareLinkResponse(ShareLinkResponse ShareLink, string Token, Uri Url, Uri WebcalUrl);

public sealed record ShareLinkListResponse(IReadOnlyList<ShareLinkResponse> Items);

public sealed class CreateShareLinkRequest
{
    /// <summary><c>free_busy</c> or <c>read</c>.</summary>
    [Required]
    public string Level { get; init; } = string.Empty;

    /// <summary>What the managers call it (≤ 100 characters).</summary>
    [StringLength(ShareLink.LabelMaxLength)]
    public string? Label { get; init; }

    /// <summary>1–3650 days; absent = never expires.</summary>
    [Range(1, ShareLink.MaxExpiryDays)]
    public int? ExpiresInDays { get; init; }
}
