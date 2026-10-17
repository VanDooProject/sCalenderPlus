using NodaTime;

namespace SCalenderPlus.Application.Accounts;

/// <summary>What account deletion needs of an account (Identity stays in Infrastructure).</summary>
/// <param name="DeletedAt">When deletion was requested (<c>users.deleted_at</c>); null = not scheduled.</param>
public sealed record AccountRecord(Guid Id, string Email, string DisplayName, string Locale, string TimeZone, Instant? DeletedAt);

/// <summary>
/// The account row itself and the audit trail, for the purge of a deleted account (permissions.md §4.6, GDPR
/// Art. 17). Both statements execute at once — call them inside <c>IAppDbContext.InTransactionAsync</c>.
/// </summary>
public interface IAccountStore
{
    Task<AccountRecord?> FindAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pseudonymises the audit trail of the account: the snapshots of its own records (resource <c>user</c>: email,
    /// display name, profile) are removed, IP address and user agent are cleared on every record it acted in, and
    /// its email address is replaced in other records' snapshots (e.g. an invite to that address). The user id
    /// stays as the pseudonym: once the account row is gone nothing maps it to a person.
    /// </summary>
    Task PseudonymiseAuditAsync(Guid userId, string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the account row; Identity data (logins, tokens, claims), feed links, calendar prefs and memberships go
    /// with it (FK cascade). Owned calendars and billed groups must be gone or handed over before (FK restrict).
    /// </summary>
    Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default);
}
