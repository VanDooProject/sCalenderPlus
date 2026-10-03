using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using SCalenderPlus.Application.Jobs;
using SCalenderPlus.Infrastructure.Jobs;
using SCalenderPlus.IntegrationTests.Infrastructure;

namespace SCalenderPlus.IntegrationTests.Jobs;

[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class JobQueueTests(PostgresFixture postgres)
{
    private const string Worker = "worker-a";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Payload(string Value);

    [Fact]
    public async Task Enqueued_job_is_only_visible_after_the_unit_of_work_commits()
    {
        await using var host = await StartAsync(new MutableClock());

        await using (var scope = host.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IJobScheduler>().Enqueue("test.ok", new Payload("x"));

            // No SaveChangesAsync: the use case failed before committing.
        }

        Assert.Equal(0, await host.CountAsync());
        Assert.False(await host.Runner.RunNextAsync(Worker, null, Ct));
    }

    [Fact]
    public async Task Successful_job_runs_once_with_its_payload_and_is_removed()
    {
        var seen = new ConcurrentBag<(string Value, int Attempt)>();
        await using var host = await StartAsync(new MutableClock(), Handler("test.ok", (ctx, _) =>
        {
            seen.Add((ctx.GetPayload<Payload>().Value, ctx.Attempt));
            return Task.CompletedTask;
        }));
        var id = await host.EnqueueAsync("test.ok", new Payload("hello"));

        Assert.True(await host.Runner.RunNextAsync(Worker, null, Ct));
        Assert.False(await host.Runner.RunNextAsync(Worker, null, Ct));

        Assert.Equal([("hello", 1)], seen);
        Assert.Null(await host.FindAsync(id));
    }

    [Fact]
    public async Task Failed_job_is_retried_after_an_exponential_backoff()
    {
        var clock = new MutableClock();
        var attempts = new ConcurrentBag<int>();
        await using var host = await StartAsync(clock, Handler("test.flaky", (ctx, _) =>
        {
            attempts.Add(ctx.Attempt);
            return ctx.Attempt < 3 ? throw new InvalidOperationException("smtp down") : Task.CompletedTask;
        }));
        var id = await host.EnqueueAsync("test.flaky", new Payload("x"));

        Assert.True(await host.Runner.RunNextAsync(Worker, null, Ct));
        var afterFirst = (await host.FindAsync(id))!;
        Assert.Equal(1, afterFirst.Attempts);
        Assert.Null(afterFirst.LockedBy);
        Assert.Contains("smtp down", afterFirst.LastError, StringComparison.Ordinal);
        AssertDelay(afterFirst.RunAt - clock.GetCurrentInstant(), Duration.FromSeconds(10)); // base delay

        Assert.False(await host.Runner.RunNextAsync(Worker, null, Ct)); // not due yet
        clock.Advance(Duration.FromSeconds(13));
        Assert.True(await host.Runner.RunNextAsync(Worker, null, Ct));
        var afterSecond = (await host.FindAsync(id))!;
        AssertDelay(afterSecond.RunAt - clock.GetCurrentInstant(), Duration.FromSeconds(20)); // doubled

        clock.Advance(Duration.FromSeconds(25));
        Assert.True(await host.Runner.RunNextAsync(Worker, null, Ct));
        Assert.Null(await host.FindAsync(id));
        Assert.Equal([1, 2, 3], attempts.Order());
    }

    [Fact]
    public async Task Job_is_dead_lettered_when_attempts_are_exhausted()
    {
        var clock = new MutableClock();
        await using var host = await StartAsync(clock, Handler("test.fail", (_, _) => throw new InvalidOperationException("boom")));
        var id = await host.EnqueueAsync("test.fail", new Payload("x"), new EnqueueOptions(MaxAttempts: 2));

        Assert.True(await host.Runner.RunNextAsync(Worker, null, Ct));
        clock.Advance(Duration.FromMinutes(1));
        Assert.True(await host.Runner.RunNextAsync(Worker, null, Ct));
        clock.Advance(Duration.FromHours(2));
        Assert.False(await host.Runner.RunNextAsync(Worker, null, Ct));

        var dead = (await host.FindAsync(id))!;
        Assert.Equal(2, dead.Attempts);
        Assert.NotNull(dead.DeadAt);
        Assert.Contains("boom", dead.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Permanent_failure_and_unknown_type_are_dead_lettered_without_retry()
    {
        await using var host = await StartAsync(new MutableClock(), Handler("test.permanent", (_, _) => throw new PermanentJobFailureException("invalid recipient")));
        var permanent = await host.EnqueueAsync("test.permanent", new Payload("x"));
        var unknown = await host.EnqueueAsync("test.unknown", new Payload("x"));

        Assert.True(await host.Runner.RunNextAsync(Worker, null, Ct));
        Assert.True(await host.Runner.RunNextAsync(Worker, null, Ct));

        Assert.NotNull((await host.FindAsync(permanent))!.DeadAt);
        Assert.Equal(1, (await host.FindAsync(permanent))!.Attempts);
        Assert.Contains("No handler registered for job type 'test.unknown'", (await host.FindAsync(unknown))!.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Job_of_a_crashed_worker_is_retried_by_another_worker_after_the_lease_expires()
    {
        var clock = new MutableClock();
        var ran = new ConcurrentBag<int>();
        await using var host = await StartAsync(clock, Handler("test.ok", (ctx, _) =>
        {
            ran.Add(ctx.Attempt);
            return Task.CompletedTask;
        }));
        var id = await host.EnqueueAsync("test.ok", new Payload("x"));

        // Worker A claims the job and dies without recording anything.
        var claimed = await host.Store.ClaimAsync("crashed-worker", clock.GetCurrentInstant(), Duration.FromMinutes(5), Ct);
        Assert.Equal(id, claimed!.Id);

        Assert.False(await host.Runner.RunNextAsync(Worker, null, Ct)); // invisible while leased
        clock.Advance(Duration.FromMinutes(5));
        Assert.True(await host.Runner.RunNextAsync(Worker, null, Ct));

        Assert.Equal([2], ran);
        Assert.Null(await host.FindAsync(id));
        Assert.False(await host.Store.CompleteAsync(claimed, "crashed-worker", Ct)); // the stale lease holder can't touch it
    }

    [Fact]
    public async Task Lease_is_renewed_while_a_long_job_runs()
    {
        using var release = new SemaphoreSlim(0);
        var started = new TaskCompletionSource();
        var settings = new Dictionary<string, string?> { ["Jobs:LeaseDuration"] = "00:00:01", ["Jobs:LeaseRenewalInterval"] = "00:00:00.200" };
        await using var host = await StartAsync(SystemClock.Instance, settings, Handler("test.slow", async (_, ct) =>
        {
            started.SetResult();
            await release.WaitAsync(ct);
        }));
        var id = await host.EnqueueAsync("test.slow", new Payload("x"));
        var beats = 0;

        var running = host.Runner.RunNextAsync(Worker, () => Interlocked.Increment(ref beats), Ct);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        for (var i = 0; i < 5; i++)
        {
            await Task.Delay(500, Ct); // 2.5 s in total: well past the 1 s lease
            Assert.False(await host.Runner.RunNextAsync("worker-b", null, Ct));
        }

        release.Release();
        Assert.True(await running);
        Assert.Null(await host.FindAsync(id));
        Assert.True(beats >= 5, $"heartbeat called {beats} times");
    }

    [Fact]
    public async Task Handler_is_cancelled_when_its_lease_is_lost()
    {
        var cancelled = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var settings = new Dictionary<string, string?> { ["Jobs:LeaseRenewalInterval"] = "00:00:00.200" };
        await using var host = await StartAsync(SystemClock.Instance, settings, Handler("test.slow", async (_, ct) =>
        {
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                cancelled.SetResult();
                throw;
            }
        }));
        var id = await host.EnqueueAsync("test.slow", new Payload("x"));

        var running = host.Runner.RunNextAsync(Worker, null, Ct);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await host.ExecuteSqlAsync($"UPDATE jobs SET locked_by = 'worker-b' WHERE id = {id}"); // another worker took over

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.True(await running);
        Assert.Equal("worker-b", (await host.FindAsync(id))!.LockedBy); // untouched by the loser
    }

    [Fact]
    public async Task Enqueue_unique_skips_a_pending_job_with_the_same_key()
    {
        await using var host = await StartAsync(new MutableClock());
        await using var scope = host.Services.CreateAsyncScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IJobScheduler>();

        Assert.True(await scheduler.EnqueueUniqueAsync("test.cron", new Payload("x"), "digest:2026-10-03", cancellationToken: Ct));
        Assert.False(await scheduler.EnqueueUniqueAsync("test.cron", new Payload("x"), "digest:2026-10-03", cancellationToken: Ct));
        Assert.True(await scheduler.EnqueueUniqueAsync("test.cron", new Payload("x"), "digest:2026-10-04", cancellationToken: Ct));
        Assert.Equal(2, await host.CountAsync());
    }

    [Theory]
    [InlineData(1, 0.0, 8)] // base 10 s, -20 %
    [InlineData(1, 0.5, 10)]
    [InlineData(3, 0.5, 40)]
    [InlineData(10, 0.5, 600)] // capped at 10 min
    [InlineData(40, 0.99, 600)] // large attempts don't overflow
    public void Retry_delay_doubles_per_attempt_with_jitter_and_cap(int failedAttempt, double random, int expectedSeconds)
    {
        var delay = JobRunner.RetryDelay(failedAttempt, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10), random);

        Assert.Equal(expectedSeconds, delay.TotalSeconds, precision: 3);
    }

    private static void AssertDelay(Duration actual, Duration nominal)
    {
        Assert.InRange(actual, nominal * 0.8 - Duration.FromMilliseconds(1), nominal * 1.2 + Duration.FromMilliseconds(1));
    }

    private static DelegateJobHandler Handler(string type, Func<JobContext, CancellationToken, Task> handle) => new(type, handle);

    private Task<JobTestHost> StartAsync(IClock clock, params IJobHandler[] handlers) => StartAsync(clock, null, handlers);

    private async Task<JobTestHost> StartAsync(IClock clock, IReadOnlyDictionary<string, string?>? settings, params IJobHandler[] handlers) =>
        await JobTestHost.StartAsync(await postgres.CreateDatabaseAsync(), clock, settings, handlers);
}
