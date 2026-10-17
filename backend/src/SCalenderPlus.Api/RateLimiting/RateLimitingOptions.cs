using System.ComponentModel.DataAnnotations;

namespace SCalenderPlus.Api.RateLimiting;

/// <summary>Rate limits (environment variables <c>RateLimiting__*</c>, docs/architecture/api.md §1). Defaults suit production.</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Per client IP: login, second factor, email confirmation, password reset (fixed window).</summary>
    [Required]
    public WindowLimit Auth { get; set; } = new() { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) };

    /// <summary>Per client IP: sign-ups (fixed window).</summary>
    [Required]
    public WindowLimit SignUp { get; set; } = new() { PermitLimit = 5, Window = TimeSpan.FromHours(1) };

    /// <summary>Per signed-in user: group invites created (fixed window); stops invite-email spam.</summary>
    [Required]
    public WindowLimit InviteCreate { get; set; } = new() { PermitLimit = 50, Window = TimeSpan.FromHours(1) };

    /// <summary>Per signed-in user: invite acceptance attempts (fixed window); stops token guessing.</summary>
    [Required]
    public WindowLimit InviteAccept { get; set; } = new() { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) };

    /// <summary>Per client IP: invite previews (fixed window; anonymous, so per address); stops token guessing.</summary>
    [Required]
    public WindowLimit InvitePreview { get; set; } = new() { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) };

    /// <summary>
    /// Per signed-in user of a cookie session, all requests (sliding window): an abuse limit generous enough that
    /// normal use of the web app never reaches it (api.md §1: web sessions are exempt from plan limits).
    /// </summary>
    [Required]
    public WindowLimit Session { get; set; } = new() { PermitLimit = 600, Window = TimeSpan.FromMinutes(1) };

    /// <summary>
    /// Per iCal feed token (token bucket, ical-caldav.md §1.4): 60 requests per hour with bursts of 10 — calendar apps
    /// poll at most every few minutes; more is a misconfigured client or a leaked URL.
    /// </summary>
    [Required]
    public BucketLimit Feed { get; set; } = new() { TokenLimit = 10, TokensPerPeriod = 1, ReplenishmentPeriod = TimeSpan.FromMinutes(1) };

    /// <summary>
    /// Per client IP (IPv6 by /64) over all feed requests, known and unknown tokens alike (fixed window,
    /// ical-caldav.md §1.4): 600 per hour leaves room for a household or office polling dozens of feeds and
    /// throttles token guessing, which the per-token buckets cannot (every guess gets a bucket of its own).
    /// </summary>
    [Required]
    public WindowLimit FeedPerClient { get; set; } = new() { PermitLimit = 600, Window = TimeSpan.FromHours(1) };

    /// <summary>Per API token and minute, by plan (plans.md "API rate limit"); applied once API tokens exist (v1).</summary>
    [Required]
    public PlanLimits TokenPlans { get; set; } = new();
}

public sealed class WindowLimit
{
    [Range(1, 1_000_000)]
    public int PermitLimit { get; set; }

    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan Window { get; set; }
}

/// <summary>A token bucket: bursts of <see cref="TokenLimit"/>, refilled by <see cref="TokensPerPeriod"/> every <see cref="ReplenishmentPeriod"/>.</summary>
public sealed class BucketLimit
{
    [Range(1, 1_000_000)]
    public int TokenLimit { get; set; }

    [Range(1, 1_000_000)]
    public int TokensPerPeriod { get; set; }

    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan ReplenishmentPeriod { get; set; }
}

/// <summary>Requests per minute per API token.</summary>
public sealed class PlanLimits
{
    [Range(1, 1_000_000)]
    public int Free { get; set; } = 60;

    [Range(1, 1_000_000)]
    public int Pro { get; set; } = 600;

    [Range(1, 1_000_000)]
    public int Team { get; set; } = 1200;

    public int For(string? plan) => plan switch
    {
        "pro" => Pro,
        "team" => Team,
        _ => Free,
    };
}
