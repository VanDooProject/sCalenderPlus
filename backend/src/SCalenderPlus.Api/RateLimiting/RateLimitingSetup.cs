using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
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
            .Validate(o => Valid(o.Auth) && Valid(o.SignUp) && Valid(o.Session), "RateLimiting: every PermitLimit must be ≥ 1 and every Window between 1 second and 1 day.")
            .ValidateOnStart();

        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<RateLimitingOptions>>((options, limits) =>
        {
            var settings = limits.Value;
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = OnRejectedAsync;
            options.AddPolicy(Auth, context => FixedWindowPerClient(context, Auth, settings.Auth));
            options.AddPolicy(SignUp, context => FixedWindowPerClient(context, SignUp, settings.SignUp));
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context => GlobalPartition(context.User, settings));
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
