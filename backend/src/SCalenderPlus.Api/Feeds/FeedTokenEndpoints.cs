using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SCalenderPlus.Api.Auth;
using SCalenderPlus.Api.Hosting;
using SCalenderPlus.Application.Feeds;
using SCalenderPlus.Core.Feeds;

namespace SCalenderPlus.Api.Feeds;

/// <summary>
/// <c>/api/v1/feed-tokens</c> (api.md "Feeds", ical-caldav.md §1.1): every signed-in user manages their own iCal
/// feed links (<see cref="FeedTokenService"/>). The secret and its URLs are returned only by create and rotate;
/// lists and reads never contain them. Others' and revoked tokens are 404.
/// </summary>
internal static class FeedTokenEndpoints
{
    public const string Tag = "Feeds";
    private const string ETagSource = "GET /api/v1/feed-tokens/{id}";

    public static RouteGroupBuilder MapFeedTokenEndpoints(this RouteGroupBuilder v1)
    {
        var tokens = v1.MapGroup("/feed-tokens").WithTags(Tag);
        tokens.MapGet(string.Empty, ListAsync).WithName("ListFeedTokens")
            .WithSummary("My iCal feed links (without secrets)")
            .WithDescription("Active links, ordered by creation. The URLs are shown only when created or rotated.");
        tokens.MapPost(string.Empty, CreateAsync).WithName("CreateFeedToken")
            .WithSummary("Create an iCal feed link (the URL is shown once)")
            .WithDescription("scope: calendar (with calendarId of a calendar I see, else 404), aggregate (every calendar I see and have not hidden, plus Shared with me — recommended) or shared_with_me. labelMode default prefix_emoji, includePastDays 0–365 (default 90). The response carries the secret, the https URL and the webcal URL — they cannot be read again (only the hash is stored). At most 50 active links (409 conflict).");
        tokens.MapPost("/rotate-all", RotateAllAsync).WithName("RotateAllFeedTokens")
            .WithSummary("New secrets for all my feed links (each URL shown once)")
            .WithDescription("Every old URL stops working at once (404); settings stay. For a lost device or after a password reset.");
        tokens.MapGet("/{id:guid}", GetAsync).WithName("GetFeedToken")
            .WithSummary("One of my feed links (without the secret, with ETag)");
        tokens.MapPatch("/{id:guid}", UpdateAsync).WithName("UpdateFeedToken")
            .Accepts<UpdateFeedTokenRequest>(MeEndpoints.MergePatchJson, "application/json")
            .WithSummary("Change a feed link's name, label mode or past days (JSON Merge Patch, requires If-Match)")
            .WithDescription("Absent or null members stay unchanged; an empty name removes it. The URL stays the same. If-Match: the etag of the link (or *).");
        tokens.MapDelete("/{id:guid}", RevokeAsync).WithName("RevokeFeedToken")
            .WithSummary("Revoke a feed link")
            .WithDescription("Its URL answers 404 from now on, also where a render of it is cached. Calendar apps keep what they already synced until their next poll.");
        tokens.MapPost("/{id:guid}/rotate", RotateAsync).WithName("RotateFeedToken")
            .WithSummary("A new secret for a feed link (the new URL is shown once)")
            .WithDescription("The old URL stops working at once (404); settings stay. Clears needsNewLink.");
        return tokens;
    }

    private static async Task<Ok<FeedTokenListResponse>> ListAsync(ClaimsPrincipal principal, FeedTokenService tokens, CancellationToken cancellationToken)
    {
        var items = await tokens.ListAsync(principal.UserId(), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new FeedTokenListResponse([.. items.Select(FeedTokenResponse.From)]));
    }

    private static async Task<Created<IssuedFeedTokenResponse>> CreateAsync(
        CreateFeedTokenRequest request,
        ClaimsPrincipal principal,
        FeedTokenService tokens,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var issued = await tokens.CreateAsync(
            principal.UserId(),
            new NewFeedToken(
                FeedTokenNames.ParseScope(request.Scope),
                request.CalendarId,
                request.Name,
                request.LabelMode is null ? FeedLabelMode.PrefixEmoji : FeedTokenNames.ParseLabelMode(request.LabelMode),
                request.IncludePastDays ?? FeedToken.DefaultIncludePastDays),
            cancellationToken).ConfigureAwait(false);
        var body = IssuedFeedTokenResponse.From(issued);
        response.Headers.ETag = body.FeedToken.Etag;
        response.Headers.CacheControl = "no-store";
        return TypedResults.Created($"{ApiV1.BasePath}/feed-tokens/{issued.Token.Id}", body);
    }

    private static async Task<Ok<FeedTokenResponse>> GetAsync(Guid id, ClaimsPrincipal principal, FeedTokenService tokens, HttpResponse response, CancellationToken cancellationToken)
    {
        var token = FeedTokenResponse.From(await tokens.GetAsync(principal.UserId(), id, cancellationToken).ConfigureAwait(false));
        response.Headers.ETag = token.Etag;
        return TypedResults.Ok(token);
    }

    private static async Task<Ok<FeedTokenResponse>> UpdateAsync(
        Guid id,
        [FromBody] UpdateFeedTokenRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        ClaimsPrincipal principal,
        FeedTokenService tokens,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var changes = new FeedTokenChanges(
            request.Name,
            request.LabelMode is null ? null : FeedTokenNames.ParseLabelMode(request.LabelMode),
            request.IncludePastDays);
        var token = FeedTokenResponse.From(await tokens.UpdateAsync(
            principal.UserId(),
            id,
            changes,
            current => ETags.Require(ifMatch, FeedTokenResponse.ETagOf(current), ETagSource),
            cancellationToken).ConfigureAwait(false));
        response.Headers.ETag = token.Etag;
        return TypedResults.Ok(token);
    }

    private static async Task<NoContent> RevokeAsync(Guid id, ClaimsPrincipal principal, FeedTokenService tokens, CancellationToken cancellationToken)
    {
        await tokens.RevokeAsync(principal.UserId(), id, cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<IssuedFeedTokenResponse>> RotateAsync(Guid id, ClaimsPrincipal principal, FeedTokenService tokens, HttpResponse response, CancellationToken cancellationToken)
    {
        var body = IssuedFeedTokenResponse.From(await tokens.RotateAsync(principal.UserId(), id, cancellationToken).ConfigureAwait(false));
        response.Headers.ETag = body.FeedToken.Etag;
        response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(body);
    }

    private static async Task<Ok<IssuedFeedTokenListResponse>> RotateAllAsync(ClaimsPrincipal principal, FeedTokenService tokens, HttpResponse response, CancellationToken cancellationToken)
    {
        var issued = await tokens.RotateAllAsync(principal.UserId(), cancellationToken).ConfigureAwait(false);
        response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(new IssuedFeedTokenListResponse([.. issued.Select(IssuedFeedTokenResponse.From)]));
    }
}
