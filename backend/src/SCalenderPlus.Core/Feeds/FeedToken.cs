using NodaTime;

namespace SCalenderPlus.Core.Feeds;

/// <summary>What a personal feed token subscribes to (ical-caldav.md §1.1), stored as <c>smallint</c>.</summary>
public enum FeedScope : short
{
    /// <summary>One calendar (<see cref="FeedToken.CalendarId"/>), as the token owner sees it.</summary>
    Calendar = 0,

    /// <summary>Every calendar the owner sees and has not hidden, plus "Shared with me" (the recommended default).</summary>
    Aggregate = 1,

    /// <summary>Only events shared with the owner through overrides of calendars they cannot see (rule 7).</summary>
    SharedWithMe = 2,
}

/// <summary>How a feed marks read-only and editable events (ical-caldav.md §1.3 "Label settings"), stored as <c>smallint</c>.</summary>
public enum FeedLabelMode : short
{
    /// <summary>Default: <c>🔒 </c> before read-only titles, editable titles unmarked; description footer.</summary>
    PrefixEmoji = 0,

    /// <summary><c>🔒 </c> before read-only and <c>✏️ </c> before editable titles; description footer.</summary>
    PrefixEmojiBoth = 1,

    /// <summary><c>[read-only]</c> / <c>[editable]</c> after the title (localized); description footer.</summary>
    SuffixText = 2,

    /// <summary>Titles unchanged; description footer only.</summary>
    DescriptionOnly = 3,

    /// <summary>No title marker and no footer.</summary>
    None = 4,
}

/// <summary>
/// A personal iCal subscription token (<c>feed_tokens</c>, data-model.md §5): the secret in the feed URL is the
/// credential, so only its SHA-256 hash is stored (the URL is shown once, at creation and rotation). Feeds are
/// evaluated as <see cref="UserId"/> by the permission engine on every poll. Revoked tokens stay as rows (audit,
/// "last used") but answer 404 like unknown ones.
/// </summary>
public sealed class FeedToken
{
    public const int NameMaxLength = 100;
    public const int DefaultIncludePastDays = 90;
    public const int MaxIncludePastDays = 365;

    public Guid Id { get; set; }

    /// <summary>The owner: the feed shows what this user sees.</summary>
    public Guid UserId { get; set; }

    public FeedScope Scope { get; set; }

    /// <summary>The calendar of a <see cref="FeedScope.Calendar"/> token; null otherwise.</summary>
    public Guid? CalendarId { get; set; }

    /// <summary>Optional label the owner gives the subscription (e.g. "iPhone"); null = none.</summary>
    public string? Name { get; set; }

    /// <summary>SHA-256 of the secret (32 random bytes, base64url).</summary>
    public byte[] TokenHash { get; set; } = [];

    public FeedLabelMode LabelMode { get; set; }

    /// <summary>Past events from this many days before today (in the owner's zone) are included; 0 … <see cref="MaxIncludePastDays"/>.</summary>
    public int IncludePastDays { get; set; } = DefaultIncludePastDays;

    /// <summary>
    /// The secret was replaced without being shown (password reset with <c>rotateFeedTokens</c>): the old URL is dead
    /// and the owner needs a new link (rotate) to subscribe again.
    /// </summary>
    public bool NeedsNewLink { get; set; }

    /// <summary>Last successful feed request (updated at most every few minutes, not on every poll).</summary>
    public Instant? LastUsedAt { get; set; }

    /// <summary>When the secret was last replaced; null until the first rotation.</summary>
    public Instant? RotatedAt { get; set; }

    public Instant? RevokedAt { get; set; }

    public Instant CreatedAt { get; set; }

    public Instant UpdatedAt { get; set; }

    public bool IsRevoked => RevokedAt is not null;
}
