using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.Infrastructure.Identity;

namespace SCalenderPlus.Infrastructure.Persistence;

/// <summary>
/// The system of record (PostgreSQL). Tables use snake_case names; instants map to <c>timestamptz</c>
/// via NodaTime. Includes the ASP.NET Core Identity user tables (<c>users</c>, <c>user_claims</c>,
/// <c>user_logins</c>, <c>user_tokens</c>; no Identity roles: roles are per group). See docs/architecture/data-model.md.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityUserContext<AppUser, Guid>(options), IDataProtectionKeyContext, IAppDbContext
{
    /// <summary>ASP.NET Core Data Protection key ring shared by every api and worker replica (<c>data_protection_keys</c>).</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<Group> Groups => Set<Group>();

    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();

    public async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (Database.CurrentTransaction is not null)
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }

        var transaction = await Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var result = await operation(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
