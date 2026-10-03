using Microsoft.EntityFrameworkCore;

namespace SCalenderPlus.Infrastructure.Persistence;

/// <summary>
/// The system of record (PostgreSQL). Tables use snake_case names; instants map to <c>timestamptz</c>
/// via NodaTime. See docs/architecture/data-model.md.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
