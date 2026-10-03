using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using SCalenderPlus.Application;
using SCalenderPlus.Application.Jobs;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Infrastructure;
using SCalenderPlus.Infrastructure.Jobs;
using SCalenderPlus.Infrastructure.Persistence;
using SCalenderPlus.IntegrationTests.Infrastructure;

namespace SCalenderPlus.IntegrationTests.Jobs;

/// <summary>Application + Infrastructure + job processing on a migrated test database, without a host.</summary>
internal sealed class JobTestHost : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private JobTestHost(ServiceProvider services, IClock clock)
    {
        _services = services;
        Clock = clock;
    }

    public IClock Clock { get; }

    public IServiceProvider Services => _services;

    public JobRunner Runner => _services.GetRequiredService<JobRunner>();

    internal JobStore Store => _services.GetRequiredService<JobStore>();

    public static async Task<JobTestHost> StartAsync(
        string connectionString,
        IClock clock,
        IReadOnlyDictionary<string, string?>? jobSettings = null,
        params IJobHandler[] handlers)
    {
        var settings = new Dictionary<string, string?>(TestSettings.For(connectionString))
        {
            ["Jobs:BaseRetryDelay"] = "00:00:10",
            ["Jobs:MaxRetryDelay"] = "00:10:00",
        };
        foreach (var (key, value) in jobSettings ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection()
            .AddLogging()
            .AddApplication(configuration)
            .AddInfrastructure(configuration)
            .AddJobProcessing(configuration)
            .AddSingleton(clock);
        foreach (var handler in handlers)
        {
            services.AddSingleton(handler);
        }

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().MigrateAsync(TestContext.Current.CancellationToken);
        }

        return new JobTestHost(provider, clock);
    }

    /// <summary>Enqueues and commits (the unit of work of a use case).</summary>
    public async Task<Guid> EnqueueAsync<TPayload>(string type, TPayload payload, EnqueueOptions? options = null)
    {
        await using var scope = _services.CreateAsyncScope();
        var id = scope.ServiceProvider.GetRequiredService<IJobScheduler>().Enqueue(type, payload, options);
        await scope.ServiceProvider.GetRequiredService<IAppDbContext>().SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    public async Task<Job?> FindAsync(Guid id)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<Job>().AsNoTracking()
            .SingleOrDefaultAsync(j => j.Id == id, TestContext.Current.CancellationToken);
    }

    public async Task<int> CountAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<Job>().CountAsync(TestContext.Current.CancellationToken);
    }

    public async Task ExecuteSqlAsync(FormattableString sql)
    {
        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlAsync(sql, TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();
}
