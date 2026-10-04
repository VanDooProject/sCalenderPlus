using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Core.Calendars;
using SCalenderPlus.Core.Events;
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

    public DbSet<GroupInvite> GroupInvites => Set<GroupInvite>();

    public DbSet<Calendar> Calendars => Set<Calendar>();

    public DbSet<CalendarGrantEntry> CalendarGrants => Set<CalendarGrantEntry>();

    public DbSet<CalendarPrefs> CalendarPrefs => Set<CalendarPrefs>();

    public DbSet<Event> Events => Set<Event>();

    public DbSet<CalendarChange> CalendarChanges => Set<CalendarChange>();

    public DbSet<EventOverrideEntry> EventOverrides => Set<EventOverrideEntry>();

    public DbSet<EventExceptionEntry> EventExceptions => Set<EventExceptionEntry>();

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

    public async Task LockAsync(Guid key, CancellationToken cancellationToken = default)
    {
        if (Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Advisory locks are transaction-scoped: call LockAsync inside InTransactionAsync.");
        }

        var name = key.ToString();
        await Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({name}, 0))", cancellationToken).ConfigureAwait(false);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.OnModelCreating(builder);
        builder.HasPostgresExtension("btree_gist"); // GiST (calendar_id, occurs_range) of events
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
