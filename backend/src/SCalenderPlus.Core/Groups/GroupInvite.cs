using NodaTime;

namespace SCalenderPlus.Core.Groups;

/// <summary>
/// An invitation into a group (<c>group_invites</c>, docs/architecture/data-model.md §2): either bound to an
/// email address (single use; only an account whose <b>verified</b> email matches can use it) or a shareable
/// link (<see cref="Email"/> null; role at most <c>member</c>, several uses). Only the SHA-256 hash of the
/// token is stored; the token itself is shown once (link) or only sent by email.
/// </summary>
public sealed class GroupInvite
{
    public const int EmailMaxLength = 256;
    public const int MaxLinkUses = 1000;
    public const int DefaultLinkUses = 50;
    public const int MaxExpiryDays = 30;
    public const int DefaultEmailExpiryDays = 14;
    public const int DefaultLinkExpiryDays = 7;

    public Guid Id { get; set; }

    public Guid GroupId { get; set; }

    /// <summary>Invited address as entered; null for invite links.</summary>
    public string? Email { get; set; }

    /// <summary>Upper-cased like Identity's normalized email, for matching on accept and on verification.</summary>
    public string? NormalizedEmail { get; set; }

    public byte[] TokenHash { get; set; } = [];

    public GroupRole Role { get; set; }

    public int MaxUses { get; set; }

    public int Uses { get; set; }

    public Instant ExpiresAt { get; set; }

    public Guid CreatedBy { get; set; }

    public Instant CreatedAt { get; set; }

    public Instant? RevokedAt { get; set; }

    /// <summary>Optimistic concurrency token (PostgreSQL <c>xmin</c>).</summary>
    public uint Version { get; set; }

    public bool IsLink => Email is null;

    /// <summary>Usable: not revoked, not expired, uses left.</summary>
    public bool IsPending(Instant now) => RevokedAt is null && Uses < MaxUses && ExpiresAt > now;
}
