using System.Net;
using SCalenderPlus.IntegrationTests.Infrastructure;
using ApiProgram = SCalenderPlus.Api.Program;
using WorkerProgram = SCalenderPlus.Worker.Program;

namespace SCalenderPlus.IntegrationTests.Health;

[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class HealthEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Api_live_and_ready_are_healthy_with_a_migrated_database()
    {
        var settings = TestSettings.For(await postgres.CreateDatabaseAsync(), autoMigrate: true);
        await using var factory = new HostFactory<ApiProgram>(settings);
        using var client = factory.CreateClient();

        var live = await HealthClient.GetAsync(client, "/health/live");
        var ready = await HealthClient.GetAsync(client, "/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.Status);
        Assert.Equal("Healthy", live.Body.Status);
        Assert.Empty(live.Body.Checks);
        Assert.Equal(HttpStatusCode.OK, ready.Status);
        Assert.Equal("Healthy", ready.Body.Status);
        Assert.Equal("Healthy", ready.Body.Checks["database"]);
    }

    [Fact]
    public async Task Api_is_not_ready_while_migrations_are_pending()
    {
        var settings = TestSettings.For(await postgres.CreateDatabaseAsync(), autoMigrate: false);
        await using var factory = new HostFactory<ApiProgram>(settings);
        using var client = factory.CreateClient();

        var live = await HealthClient.GetAsync(client, "/health/live");
        var ready = await HealthClient.GetAsync(client, "/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.Status);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.Status);
        Assert.Equal("Unhealthy", ready.Body.Checks["database"]);
    }

    [Fact]
    public async Task Worker_is_ready_with_database_and_job_loop_heartbeat()
    {
        // The worker never migrates; the api's `migrate` does. Prepare the schema first.
        var connectionString = await postgres.CreateDatabaseAsync();
        await using (var api = new HostFactory<ApiProgram>(TestSettings.For(connectionString, autoMigrate: true)))
        {
            using var _ = api.CreateClient();
        }

        await using var factory = new HostFactory<WorkerProgram>(TestSettings.For(connectionString));
        using var client = factory.CreateClient();

        var ready = await HealthClient.GetAsync(client, "/health/ready");

        Assert.True(ready.Status == HttpStatusCode.OK, ready.Raw);
        Assert.Equal("Healthy", ready.Body.Checks["database"]);
        Assert.Equal("Healthy", ready.Body.Checks["job-loop"]);
    }
}
