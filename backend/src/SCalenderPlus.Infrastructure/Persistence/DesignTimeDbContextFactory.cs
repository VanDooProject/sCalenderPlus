using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SCalenderPlus.Infrastructure.Persistence;

/// <summary>
/// Used by <c>dotnet ef</c> only. Reads <c>ConnectionStrings__Default</c> and falls back to the local
/// development database from deploy/docker-compose.dev.yml (adding a migration needs no database).
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string LocalDevelopment = "Host=localhost;Port=5432;Database=scal;Username=scal;Password=scal";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? LocalDevelopment;
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        builder.UseAppDatabase(connectionString);
        return new AppDbContext(builder.Options);
    }
}
