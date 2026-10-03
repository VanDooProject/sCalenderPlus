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

    /// <summary>
    /// Per signed-in user of a cookie session, all requests (sliding window): an abuse limit generous enough that
    /// normal use of the web app never reaches it (api.md §1: web sessions are exempt from plan limits).
    /// </summary>
    [Required]
    public WindowLimit Session { get; set; } = new() { PermitLimit = 600, Window = TimeSpan.FromMinutes(1) };

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
