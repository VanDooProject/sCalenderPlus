using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SCalenderPlus.Api.Problems;
using SCalenderPlus.Application.Errors;

namespace SCalenderPlus.Api.RateLimiting;

/// <summary>
/// Rate limiting baseline (docs/architecture/api.md §1, ASP.NET Core rate limiter, in memory per replica):
/// <list type="bullet">
/// <item><see cref="Auth"/> and <see cref="SignUp"/> policies: per client IP (after trusted forwarded headers;
/// IPv6 grouped by /64), fixed windows, on the anonymous auth endpoints.</item>
/// <item><see cref="InviteCreate"/> and <see cref="InviteAccept"/> policies: per signed-in user, fixed windows
/// (invite-email spam, invite-token guessing); <see cref="InvitePreview"/> per client IP (anonymous token guessing).</item>
/// <item><see cref="Feed"/> policy: per feed token (the <c>{token}</c> route value, hashed, <c>u:</c>/<c>s:</c> for
/// personal and share-link feeds), token bucket (60/h, burst 10); and, as part of the global limiter, per client
/// IP over all feed requests (<see cref="RateLimitingOptions.FeedPerClient"/>, 600/h), which also throttles
/// requests for unknown tokens.</item>
/// <item>Global limiter: per signed-in user for cookie sessions (generous sliding window); per API token by
/// plan once tokens exist (<see cref="ApiTokenAuthenticationType"/>); anonymous requests are only limited by
/// the endpoint policies.</item>
/// <item>Rejections are <c>429 rate_limited</c> problems with <c>Retry-After</c> (seconds).</item>
/// </list>
/// </summary>
public static class RateLimitingSetup
{
    public const string Auth = "auth";
    public const string SignUp = "sign-up";
    public const string InviteCreate = "invite-create";
    public const string InviteAccept = "invite-accept";
    public const string InvitePreview = "invite-preview";
    public const string Feed = "feed";

    /// <summary>
    /// <see cref="ClaimsIdentity.AuthenticationType"/> the API token handler (v1) will give its identities; it
    /// must also add a <see cref="PlanClaim"/> and a <see cref="TokenIdClaim"/>.
    /// </summary>
    public const string ApiTokenAuthenticationType = "api-token";
    public const string PlanClaim = "scal:plan";
    public const string TokenIdClaim = "scal:token_id";

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                o => Valid(o.Auth) && Valid(o.SignUp) && Valid(o.Session) && Valid(o.InviteCreate) && Valid(o.InviteAccept) && Valid(o.InvitePreview) && Valid(o.FeedPerClient)
                    && o.Feed is { TokenLimit: >= 1, TokensPerPeriod: >= 1 } && o.Feed.ReplenishmentPeriod >= TimeSpan.FromSeconds(1) && o.Feed.ReplenishmentPeriod <= TimeSpan.FromDays(1),
                "RateLimiting: every PermitLimit/TokenLimit must be ≥ 1 and every Window/ReplenishmentPeriod between 1 second and 1 day.")
            .ValidateOnStart();

        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<RateLimitingOptions>>((options, limits) =>
        {
            var settings = limits.Value;
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = OnRejectedAsync;
            options.AddPolicy(Auth, context => FixedWindowPerClient(context, Auth, settings.Auth));
            options.AddPolicy(SignUp, context => FixedWindowPerClient(context, SignUp, settings.SignUp));
            options.AddPolicy(InviteCreate, context => FixedWindowPerUser(context, InviteCreate, settings.InviteCreate));
            options.AddPolicy(InviteAccept, context => FixedWindowPerUser(context, InviteAccept, settings.InviteAccept));
            options.AddPolicy(InvitePreview, context => FixedWindowPerClient(context, InvitePreview, settings.InvitePreview));
            options.AddPolicy(Feed, context => BucketPerFeed(context, settings.Feed));
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(context => GlobalPartition(context.User, settings)),
                PartitionedRateLimiter.Create<HttpContext, string>(context => FeedClientPartition(context, settings.FeedPerClient)));
        });
        return services;
    }

    /// <summary>The global partition of a request's principal: cookie session, API token (by plan) or none.</summary>
    public static RateLimitPartition<string> GlobalPartition(ClaimsPrincipal principal, RateLimitingOptions settings)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(settings);

        var identity = principal.Identity;
        if (identity?.IsAuthenticated != true)
        {
            return RateLimitPartition.GetNoLimiter("anonymous");
        }

        if (identity.AuthenticationType == ApiTokenAuthenticationType)
        {
            var tokenId = principal.FindFirstValue(TokenIdClaim) ?? principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
            var permits = settings.TokenPlans.For(principal.FindFirstValue(PlanClaim));
            return RateLimitPartition.GetFixedWindowLimiter("token:" + tokenId, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
        }

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter("session:" + userId, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = settings.Session.PermitLimit,
            Window = settings.Session.Window,
            SegmentsPerWindow = 6,
            QueueLimit = 0,
        });
    }

    /// <summary>Partition key of a client address: IPv4 as is, IPv6 by its /64 network (one subscriber's prefix).</summary>
    public static string ClientKey(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }

    private static RateLimitPartition<string> FixedWindowPerClient(HttpContext context, string policy, WindowLimit limit) =>
        RateLimitPartition.GetFixedWindowLimiter(policy + ":" + ClientKey(context.Connection.RemoteIpAddress), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limit.PermitLimit,
            Window = limit.Window,
            QueueLimit = 0,
        });

    /// <summary>Per signed-in user (falls back to the client address; the endpoints require a session anyway).</summary>
    private static RateLimitPartition<string> FixedWindowPerUser(HttpContext context, string policy, WindowLimit limit) =>
        RateLimitPartition.GetFixedWindowLimiter(
            policy + ":" + (context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? ClientKey(context.Connection.RemoteIpAddress)),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit.PermitLimit,
                Window = limit.Window,
                QueueLimit = 0,
            });

    /// <summary>
    /// The partition key of a feed request: <c>u:</c> (personal) or <c>s:</c> (share link) and the SHA-256 of the
    /// <c>{token}</c> route value — the secret itself must not sit in memory as a dictionary key in clear, and the
    /// raw path would give every spelling of the same URL (case of the literal segments, a trailing slash,
    /// percent-encoding) a bucket of its own. Unknown tokens get buckets too; the per-client limit covers guessing.
    /// </summary>
    public static string FeedKey(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var token = context.Request.RouteValues.TryGetValue("token", out var value) ? value?.ToString() ?? string.Empty : string.Empty;
        var kind = context.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == Feeds.FeedEndpoints.ShareLinkFeedName ? "s:" : "u:";
        return kind + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    /// <summary>The global limiter's per-client limit of feed requests (endpoints with the <see cref="Feed"/> policy); no limit elsewhere.</summary>
    private static RateLimitPartition<string> FeedClientPartition(HttpContext context, WindowLimit limit) =>
        context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName != Feed
            ? RateLimitPartition.GetNoLimiter(string.Empty)
            : RateLimitPartition.GetFixedWindowLimiter("feed-client:" + ClientKey(context.Connection.RemoteIpAddress), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit.PermitLimit,
                Window = limit.Window,
                QueueLimit = 0,
            });

    private static RateLimitPartition<string> BucketPerFeed(HttpContext context, BucketLimit limit) =>
        RateLimitPartition.GetTokenBucketLimiter(
            Feed + ":" + FeedKey(context),
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = limit.TokenLimit,
                TokensPerPeriod = limit.TokensPerPeriod,
                ReplenishmentPeriod = limit.ReplenishmentPeriod,
                QueueLimit = 0,
                AutoReplenishment = true,
            });

    private static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var value) ? value : TimeSpan.FromMinutes(1);
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        await ApiProblems.Create(ErrorCodes.RateLimited, $"Too many requests. Try again in {seconds} seconds.")
            .ExecuteAsync(context.HttpContext).ConfigureAwait(false);
    }

    private static bool Valid(WindowLimit? limit) =>
        limit is not null && limit.PermitLimit >= 1 && limit.Window >= TimeSpan.FromSeconds(1) && limit.Window <= TimeSpan.FromDays(1);
}
