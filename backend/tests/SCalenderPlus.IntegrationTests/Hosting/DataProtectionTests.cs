using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using SCalenderPlus.IntegrationTests.Infrastructure;
using ApiProgram = SCalenderPlus.Api.Program;
using WorkerProgram = SCalenderPlus.Worker.Program;

namespace SCalenderPlus.IntegrationTests.Hosting;

/// <summary>The key ring lives in PostgreSQL: shared by api and worker, and it survives restarts.</summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class DataProtectionTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Payload_protected_by_one_host_is_readable_by_a_restarted_api_and_the_worker()
    {
        var settings = TestSettings.For(await postgres.CreateDatabaseAsync(), autoMigrate: true);

        string protectedPayload;
        await using (var api = new HostFactory<ApiProgram>(settings))
        {
            protectedPayload = Protector(api.Services).Protect("secret payload");
        }

        await using var restarted = new HostFactory<ApiProgram>(settings);
        await using var worker = new HostFactory<WorkerProgram>(settings);

        Assert.Equal("secret payload", Protector(restarted.Services).Unprotect(protectedPayload));
        Assert.Equal("secret payload", Protector(worker.Services).Unprotect(protectedPayload));
    }

    private static IDataProtector Protector(IServiceProvider services) =>
        services.GetRequiredService<IDataProtectionProvider>().CreateProtector("tests");
}
