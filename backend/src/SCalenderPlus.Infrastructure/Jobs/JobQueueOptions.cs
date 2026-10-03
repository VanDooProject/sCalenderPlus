using System.ComponentModel.DataAnnotations;

namespace SCalenderPlus.Infrastructure.Jobs;

/// <summary>Worker job processing settings (<c>Jobs__*</c>). Defaults suit production.</summary>
public sealed class JobQueueOptions
{
    public const string SectionName = "Jobs";

    /// <summary>Jobs processed in parallel per worker instance.</summary>
    [Range(1, 64)]
    public int Concurrency { get; set; } = 4;

    /// <summary>Wait between claim attempts while the queue is empty.</summary>
    [Range(typeof(TimeSpan), "00:00:00.010", "00:01:00")]
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Visibility timeout: a claimed job is invisible to other workers this long; renewed while the handler
    /// runs, so only a crashed or stuck worker lets it expire (then another worker retries it).
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How often a running job's lease is extended. Must be well below <see cref="LeaseDuration"/>.</summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "00:10:00")]
    public TimeSpan LeaseRenewalInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Delay before the second attempt; doubles per attempt (with ±20 % jitter) up to <see cref="MaxRetryDelay"/>.</summary>
    [Range(typeof(TimeSpan), "00:00:00", "01:00:00")]
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    [Range(typeof(TimeSpan), "00:00:00", "1.00:00:00")]
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromHours(1);
}
