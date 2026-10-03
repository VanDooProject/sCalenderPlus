using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using SCalenderPlus.Application.Jobs;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.IntegrationTests.Infrastructure;
using WorkerProgram = SCalenderPlus.Worker.Program;

namespace SCalenderPlus.IntegrationTests.Jobs;

/// <summary>Issue #27: two worker instances on one queue never process the same job.</summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class TwoWorkersTests(PostgresFixture postgres)
{
    private const int JobCount = 200;

    [Fact]
    public async Task Two_worker_hosts_process_every_job_exactly_once()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var producer = await JobTestHost.StartAsync(connectionString, SystemClock.Instance);
        var processed = new ConcurrentBag<(Guid JobId, string Worker, int Attempt)>();
        await using var workerA = StartWorker(connectionString, "a", processed);
        await using var workerB = StartWorker(connectionString, "b", processed);

        // One commit with all jobs, so both (already polling) workers contend for the same rows.
        await using (var scope = producer.Services.CreateAsyncScope())
        {
            var scheduler = scope.ServiceProvider.GetRequiredService<IJobScheduler>();
            for (var i = 0; i < JobCount; i++)
            {
                scheduler.Enqueue("test.record", new { index = i });
            }

            await scope.ServiceProvider.GetRequiredService<IAppDbContext>().SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (await producer.CountAsync() > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        var duplicates = processed.GroupBy(p => p.JobId).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(duplicates);
        Assert.Equal(JobCount, processed.Count);
        Assert.All(processed, p => Assert.Equal(1, p.Attempt));
        Assert.Equal(0, await producer.CountAsync());
        Assert.Equal(["a", "b"], processed.Select(p => p.Worker).Distinct().Order(StringComparer.Ordinal));
    }

    private static HostFactory<WorkerProgram> StartWorker(string connectionString, string name, ConcurrentBag<(Guid, string, int)> processed)
    {
        var settings = TestSettings.For(connectionString);
        settings["Jobs:Concurrency"] = "4";
        settings["Jobs:PollInterval"] = "00:00:00.020";
        var factory = new HostFactory<WorkerProgram>(settings, services => services.AddSingleton<IJobHandler>(
            new DelegateJobHandler("test.record", async (ctx, ct) =>
            {
                processed.Add((ctx.JobId, name, ctx.Attempt));
                await Task.Delay(10, ct); // hold the job a little so both workers contend
            })));
        _ = factory.Services; // start the host (and its job loop)
        return factory;
    }
}
