using System.Net;
using Microsoft.EntityFrameworkCore;
using SCalenderPlus.Application.Accounts;
using SCalenderPlus.Infrastructure.Auditing;
using SCalenderPlus.Infrastructure.Persistence;

namespace SCalenderPlus.Infrastructure.Identity;

internal sealed class AccountStore(AppDbContext db) : IAccountStore
{
    /// <summary>What replaces the address in other records' snapshots.</summary>
    public const string DeletedEmail = "[deleted]";

    public async Task<AccountRecord?> FindAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new AccountRecord(u.Id, u.Email!, u.DisplayName, u.Locale, u.TimeZone, u.DeletedAt))
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    public async Task PseudonymiseAuditAsync(Guid userId, string email, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        var events = db.Set<AuditEvent>();
        var id = userId.ToString();
        await events.Where(e => e.ResourceType == AccountAuditActions.ResourceType && e.ResourceId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Before, (string?)null).SetProperty(e => e.After, (string?)null), cancellationToken).ConfigureAwait(false);
        await events.Where(e => e.ActorUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Ip, (IPAddress?)null).SetProperty(e => e.UserAgent, (string?)null), cancellationToken).ConfigureAwait(false);

        // Snapshots of other resources that carry the address (an invite to it): only that member is replaced.
        var deleted = $"\"{DeletedEmail}\"";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE audit_events SET before = jsonb_set(before, '{{email}}', {deleted}::jsonb) WHERE lower(before->>'email') = lower({email})",
            cancellationToken).ConfigureAwait(false);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE audit_events SET after = jsonb_set(after, '{{email}}', {deleted}::jsonb) WHERE lower(after->>'email') = lower({email})",
            cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(cancellationToken);
}
