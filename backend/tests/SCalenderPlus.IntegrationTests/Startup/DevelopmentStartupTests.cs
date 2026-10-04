using System.Net;
using SCalenderPlus.IntegrationTests.Health;
using SCalenderPlus.IntegrationTests.Infrastructure;
using ApiProgram = SCalenderPlus.Api.Program;
using WorkerProgram = SCalenderPlus.Worker.Program;

namespace SCalenderPlus.IntegrationTests.Startup;

/// <summary>
/// In the Development environment the host validates the whole container when it is built (<c>ValidateOnBuild</c>,
/// <c>ValidateScopes</c>): every registered service must be constructible. The api once registered the worker's job
/// handlers without the senders they need, so <c>dotnet run</c> of the api failed locally while the "Testing" and
/// "Production" hosts (no build-time validation) started fine.
/// </summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class DevelopmentStartupTests(PostgresFixture postgres)
{
    private const string Development = "Development";

    [Fact]
    public async Task Api_starts_in_development_with_service_provider_validation()
    {
        var settings = TestSettings.For(await postgres.CreateDatabaseAsync(), autoMigrate: true);
        await using var factory = new HostFactory<ApiProgram>(settings, environment: Development);
        using var client = factory.CreateClient();

        var ready = await HealthClient.GetAsync(client, "/health/ready");

        Assert.True(ready.Status == HttpStatusCode.OK, ready.Raw);
    }

    [Fact]
    public async Task Worker_starts_in_development_with_service_provider_validation()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using (var api = new HostFactory<ApiProgram>(TestSettings.For(connectionString, autoMigrate: true)))
        {
            using var _ = api.CreateClient();
        }

        await using var factory = new HostFactory<WorkerProgram>(TestSettings.For(connectionString), environment: Development);
        using var client = factory.CreateClient();

        var live = await HealthClient.GetAsync(client, "/health/live");

        Assert.Equal(HttpStatusCode.OK, live.Status);
    }
}
