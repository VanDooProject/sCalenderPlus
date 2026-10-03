namespace SCalenderPlus.Application.Users;

/// <summary>
/// Read access to accounts for use cases outside the account flows (Identity stays in Infrastructure), plus the
/// permission-cache invalidation of <c>users.acl_version</c> (docs/architecture/permissions.md §8).
/// </summary>
public interface IUserDirectory
{
    Task<IReadOnlyDictionary<Guid, UserSummary>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bumps <c>acl_version</c> of the users (membership or role changed): effective immediately for permission
    /// caches and feeds. Executes right away — call it inside <c>IAppDbContext.InTransactionAsync</c> so it commits
    /// with the change.
    /// </summary>
    Task BumpAclVersionAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}

/// <param name="EmailVerified">The address is confirmed (<c>email_confirmed</c>).</param>
public sealed record UserSummary(Guid Id, string Email, string DisplayName, string Locale, bool EmailVerified);
