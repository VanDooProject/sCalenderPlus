using System.Net;
using SCalenderPlus.IntegrationTests.Infrastructure;
using ApiProgram = SCalenderPlus.Api.Program;
using WorkerProgram = SCalenderPlus.Worker.Program;

namespace SCalenderPlus.IntegrationTests.Health;

/// <summary>Database down: still live (no restart loop), but not ready. No Docker needed.</summary>
public sealed class HealthWithoutDatabaseTests
{
    [Fact]
    public async Task Api_is_live_but_not_ready_when_the_database_is_down()
    {
        await using var factory = new HostFactory<ApiProgram>(TestSettings.For(TestSettings.UnreachableDatabase));
        using var client = factory.CreateClient();

        var live = await HealthClient.GetAsync(client, "/health/live");
        var ready = await HealthClient.GetAsync(client, "/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.Status);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.Status);
        Assert.Equal("Unhealthy", ready.Body.Status);
        Assert.Equal("Unhealthy", ready.Body.Checks["database"]);
        Assert.DoesNotContain("Password", ready.Raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", ready.Raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Worker_is_live_but_not_ready_when_the_database_is_down()
    {
        await using var factory = new HostFactory<WorkerProgram>(TestSettings.For(TestSettings.UnreachableDatabase));
        using var client = factory.CreateClient();

        var live = await HealthClient.GetAsync(client, "/health/live");
        var ready = await HealthClient.GetAsync(client, "/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.Status);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.Status);
        Assert.Equal("Unhealthy", ready.Body.Checks["database"]);
    }
}
