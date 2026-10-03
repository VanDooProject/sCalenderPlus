using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SCalenderPlus.Infrastructure;
using SCalenderPlus.Infrastructure.Persistence;
using SCalenderPlus.IntegrationTests.Infrastructure;

namespace SCalenderPlus.IntegrationTests.Persistence;

[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class MigrationTests(PostgresFixture postgres)
{
    private static readonly TimeSpan _processTimeout = TimeSpan.FromSeconds(90);

    [Fact]
    public async Task Migrations_apply_to_an_empty_database()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var services = BuildServices(connectionString);

        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().MigrateAsync(TestContext.Current.CancellationToken);
        }

        await using var verify = services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var applied = await db.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(db.Database.GetMigrations());
        Assert.Equal(db.Database.GetMigrations(), applied);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Migrate_command_exits_zero_twice_in_a_row()
    {
        var environment = Environment(await postgres.CreateDatabaseAsync());

        var first = await BackendProcess.RunAsync(BackendProcess.Api, [MigrateCommandName], environment, _processTimeout);
        var second = await BackendProcess.RunAsync(BackendProcess.Api, [MigrateCommandName], environment, _processTimeout);

        Assert.True(first.ExitCode == 0, first.Output);
        Assert.Contains("Applying", first.Output, StringComparison.Ordinal);
        Assert.True(second.ExitCode == 0, second.Output);
        Assert.Contains("up to date", second.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Concurrent_migrate_commands_serialize_on_the_advisory_lock()
    {
        var environment = Environment(await postgres.CreateDatabaseAsync());

        var results = await Task.WhenAll(
            BackendProcess.RunAsync(BackendProcess.Api, [MigrateCommandName], environment, _processTimeout),
            BackendProcess.RunAsync(BackendProcess.Api, [MigrateCommandName], environment, _processTimeout));

        Assert.All(results, r => Assert.True(r.ExitCode == 0, r.Output));
    }

    private const string MigrateCommandName = "migrate";

    private static Dictionary<string, string?> Environment(string connectionString) =>
        new() { ["ConnectionStrings__Default"] = connectionString };

    private static ServiceProvider BuildServices(string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connectionString })
            .Build();
        return new ServiceCollection().AddLogging().AddPersistence(configuration).BuildServiceProvider();
    }
}
