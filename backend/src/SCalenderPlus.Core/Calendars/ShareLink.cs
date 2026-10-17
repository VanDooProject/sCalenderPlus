using NodaTime;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Core.Calendars;

/// <summary>
/// An anonymous share link of a calendar (<c>share_links</c>, data-model.md §3): whoever holds the secret sees the
/// calendar as the <c>anonymous</c> principal with <see cref="Level"/> (<c>free_busy</c> or <c>read</c>) as a ceiling
/// (permissions.md §3, <see cref="PrincipalContext.ForShareLink"/>) — through the feed <c>/ical/v1/s/{token}.ics</c>.
/// Only the SHA-256 of the secret is stored; revoked and expired links answer 404 like unknown ones. No passwords:
/// iCal clients cannot send them.
/// </summary>
public sealed class ShareLink
{
    public const int LabelMaxLength = 100;
    public const int MaxExpiryDays = 3650;

    public Guid Id { get; set; }

    public Guid CalendarId { get; set; }

    public byte[] TokenHash { get; set; } = [];

    /// <summary><see cref="CalendarLevel.FreeBusy"/> or <see cref="CalendarLevel.Read"/> (CHECK).</summary>
    public CalendarLevel Level { get; set; }

    /// <summary>What the managers call it (e.g. "Club website"); null = none.</summary>
    public string? Label { get; set; }

    /// <summary>The link stops working at this instant; null = never.</summary>
    public Instant? ExpiresAt { get; set; }

    public Instant? RevokedAt { get; set; }

    /// <summary>Who created it (no FK, kept as tombstone).</summary>
    public Guid CreatedBy { get; set; }

    public Instant CreatedAt { get; set; }

    /// <summary>Last feed request through the link (updated at most every few minutes).</summary>
    public Instant? LastUsedAt { get; set; }

    /// <summary>Usable at <paramref name="now"/>: not revoked, not expired.</summary>
    public bool IsActive(Instant now) => RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);
}
