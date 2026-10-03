using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SCalenderPlus.Application.Persistence;

namespace SCalenderPlus.Infrastructure.Persistence;

/// <summary>
/// The system of record (PostgreSQL). Tables use snake_case names; instants map to <c>timestamptz</c>
/// via NodaTime. See docs/architecture/data-model.md.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IDataProtectionKeyContext, IAppDbContext
{
    /// <summary>ASP.NET Core Data Protection key ring shared by every api and worker replica (<c>data_protection_keys</c>).</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
