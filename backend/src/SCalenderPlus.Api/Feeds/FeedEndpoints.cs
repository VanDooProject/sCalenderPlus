using System.Globalization;
using NodaTime;
using SCalenderPlus.Api.RateLimiting;
using SCalenderPlus.Application.Feeds;

namespace SCalenderPlus.Api.Feeds;

/// <summary>
/// The iCal subscription feeds (ical-caldav.md §1), outside the versioned JSON api: <c>GET /ical/v1/{token}.ics</c>
/// (personal: evaluated as the token's owner) and <c>GET /ical/v1/s/{token}.ics</c> (anonymous share link, the
/// link's level as ceiling). The secret in the path is the credential: anonymous endpoints, rate limited per token
/// (<see cref="RateLimitingSetup.Feed"/>), redacted from logs and traces. Answers: <c>200 text/calendar</c> with a
/// weak <c>ETag</c>, <c>Last-Modified</c> and <c>Cache-Control: private, max-age=300</c>; <c>304</c> for a matching
/// <c>If-None-Match</c>/<c>If-Modified-Since</c>; <c>404</c> for unknown, revoked or expired tokens and <c>410</c>
/// when the owner's account is being deleted — as short <c>text/plain</c> bodies, not problem details (calendar apps
/// cannot use them, and a problem's <c>instance</c> would echo the secret).
/// </summary>
internal static class FeedEndpoints
{
    public const string BasePath = "/ical/v1";
    private const string CalendarContentType = "text/calendar; charset=utf-8";

    public static IEndpointRouteBuilder MapFeedEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var feeds = endpoints.MapGroup(BasePath).WithTags(FeedTokenEndpoints.Tag).AllowAnonymous().RequireRateLimiting(RateLimitingSetup.Feed);
        feeds.MapGet("/{token}.ics", UserFeedAsync).WithName("GetFeed")
            .WithSummary("Personal iCal feed (the token is the credential)")
            .WithDescription("What the token's owner sees, per event through the permission engine: busy-only events as cover title or Busy without details, read-only and editable events labelled per the token's label mode. Series as RRULE plus exceptions, VTIMEZONE for every TZID. ETag/If-None-Match → 304; 404 for unknown and revoked tokens, 410 while the owner's account is being deleted. 60 requests per hour per token (burst 10), then 429 with Retry-After.")
            .Produces<string>(StatusCodes.Status200OK, "text/calendar")
            .Produces(StatusCodes.Status304NotModified)
            .Produces<string>(StatusCodes.Status404NotFound, "text/plain")
            .Produces<string>(StatusCodes.Status410Gone, "text/plain");
        feeds.MapGet("/s/{token}.ics", ShareLinkFeedAsync).WithName("GetShareLinkFeed")
            .WithSummary("Anonymous share-link iCal feed (the token is the credential)")
            .WithDescription("The calendar as the anonymous principal sees it, the link's level (free_busy or read) as ceiling: no event is ever more than the link allows. No access labels or app links. 404 for unknown, revoked and expired links. Same caching and rate limit as personal feeds.")
            .Produces<string>(StatusCodes.Status200OK, "text/calendar")
            .Produces(StatusCodes.Status304NotModified)
            .Produces<string>(StatusCodes.Status404NotFound, "text/plain");
        return endpoints;
    }

    private static async Task<IResult> UserFeedAsync(string token, HttpRequest request, FeedService feeds, CancellationToken cancellationToken) =>
        new FeedResult(await feeds.UserFeedAsync(token, Conditions(request), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ShareLinkFeedAsync(string token, HttpRequest request, FeedService feeds, CancellationToken cancellationToken) =>
        new FeedResult(await feeds.ShareLinkFeedAsync(token, Conditions(request), cancellationToken).ConfigureAwait(false));

    private static FeedConditions Conditions(HttpRequest request)
    {
        var ifNoneMatch = request.Headers.IfNoneMatch
            .SelectMany(v => (v ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .ToList();
        var since = request.GetTypedHeaders().IfModifiedSince;
        return new FeedConditions(ifNoneMatch, since is { } value ? Instant.FromDateTimeOffset(value) : null);
    }

    private sealed class FeedResult(FeedResponse response) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var http = httpContext.Response;
            switch (response.Status)
            {
                case FeedStatus.NotFound:
                    await PlainAsync(http, StatusCodes.Status404NotFound, "Feed not found.").ConfigureAwait(false);
                    return;
                case FeedStatus.Gone:
                    await PlainAsync(http, StatusCodes.Status410Gone, "This feed no longer exists.").ConfigureAwait(false);
                    return;
            }

            http.Headers.ETag = response.ETag;
            http.Headers.CacheControl = "private, max-age=300";
            if (response.LastModified is { } lastModified)
            {
                http.Headers.LastModified = lastModified.ToDateTimeOffset().ToString("R", CultureInfo.InvariantCulture);
            }

            if (response.Status == FeedStatus.NotModified)
            {
                http.StatusCode = StatusCodes.Status304NotModified;
                return;
            }

            var body = response.Feed!.Body;
            http.StatusCode = StatusCodes.Status200OK;
            http.ContentType = CalendarContentType;
            http.ContentLength = body.Length;
            await http.Body.WriteAsync(body, httpContext.RequestAborted).ConfigureAwait(false);
        }

        private static async Task PlainAsync(HttpResponse http, int status, string text)
        {
            http.StatusCode = status;
            http.Headers.CacheControl = "no-store";
            http.ContentType = "text/plain; charset=utf-8";
            await http.WriteAsync(text, http.HttpContext.RequestAborted).ConfigureAwait(false);
        }
    }
}
