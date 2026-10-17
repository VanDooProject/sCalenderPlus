using System.ComponentModel.DataAnnotations;

namespace SCalenderPlus.Application.Feeds;

/// <summary>iCal feeds (environment variables <c>Feeds__*</c>, ical-caldav.md §1). Defaults suit production.</summary>
public sealed class FeedOptions
{
    public const string SectionName = "Feeds";

    /// <summary>Size limit of the in-process cache of rendered feeds (per api replica).</summary>
    [Range(1, 4096)]
    public int CacheSizeMegabytes { get; set; } = 64;

    /// <summary>How long a rendered feed stays cached (absolute); polls in between with the same ETag reuse it.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Hard cap of VEVENTs per feed (oldest past events dropped first).</summary>
    [Range(1, 100_000)]
    public int MaxEvents { get; set; } = 5000;

    /// <summary>Active (not revoked) feed tokens per user.</summary>
    [Range(1, 1000)]
    public int MaxTokensPerUser { get; set; } = 50;

    /// <summary>Active (not revoked) share links per calendar.</summary>
    [Range(1, 1000)]
    public int MaxShareLinksPerCalendar { get; set; } = 20;
}
