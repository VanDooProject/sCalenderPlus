using Microsoft.EntityFrameworkCore;
using SCalenderPlus.Infrastructure.Persistence;

namespace SCalenderPlus.IntegrationTests.Persistence;

/// <summary>Every model change has a migration (the snapshot matches the model). No Docker needed.</summary>
public sealed class ModelSnapshotTests
{
    [Fact]
    public void Model_has_no_changes_without_a_migration()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);

        Assert.False(db.Database.HasPendingModelChanges(), "Add a migration: dotnet ef migrations add <Name> -p backend/src/SCalenderPlus.Infrastructure -s backend/src/SCalenderPlus.Infrastructure -o Persistence/Migrations");
    }
}
