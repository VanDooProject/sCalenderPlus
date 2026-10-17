using NodaTime;

namespace SCalenderPlus.Application.Users;

/// <summary>
/// Read access to accounts for use cases outside the account flows (Identity stays in Infrastructure), plus the
/// permission-cache invalidation of <c>users.acl_version</c> (docs/architecture/permissions.md §8).
/// </summary>
public interface IUserDirectory
{
    Task<IReadOnlyDictionary<Guid, UserSummary>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>The account with this email address (any confirmation state), if one exists.</summary>
    Task<UserSummary?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>When the account was created (<c>users.created_at</c>; the reverse trial counts from it); null for unknown ids.</summary>
    Task<Instant?> CreatedAtAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// What a feed request needs of its token's owner (locale, zone, <c>acl_version</c>, deletion state) in one
    /// cheap lookup; null for unknown ids.
    /// </summary>
    Task<FeedOwner?> FeedOwnerAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The lookup form of an email address, as Identity stores it in <c>normalized_email</c>.</summary>
    string NormalizeEmail(string email);

    /// <summary>
    /// Bumps <c>acl_version</c> of the users (membership or role changed): effective immediately for permission
    /// caches and feeds. Executes right away — call it inside <c>IAppDbContext.InTransactionAsync</c> so it commits
    /// with the change.
    /// </summary>
    Task BumpAclVersionAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}

/// <summary>The owner of a feed token as the feed sees them.</summary>
/// <param name="Deleted">The account is being deleted (grace period): its feeds answer 410 Gone.</param>
public sealed record FeedOwner(Guid Id, string Locale, string TimeZone, long AclVersion, bool Deleted);

/// <param name="EmailVerified">The address is confirmed (<c>email_confirmed</c>).</param>
public sealed record UserSummary(Guid Id, string Email, string DisplayName, string Locale, bool EmailVerified);
