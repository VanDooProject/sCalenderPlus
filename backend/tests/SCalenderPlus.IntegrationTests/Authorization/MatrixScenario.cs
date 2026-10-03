using SCalenderPlus.IntegrationTests.Infrastructure;
using ApiProgram = SCalenderPlus.Api.Program;

namespace SCalenderPlus.IntegrationTests.Authorization;

/// <summary>
/// The seeded world the matrix runs in: one migrated database and api host per test class, plus named
/// resources (ids) created by <see cref="SeedAsync"/> that cases use as route values. Seeding grows with the
/// features (M1-B: users and sessions per actor; M1-C: groups "lions"/"other-tenant" with members per role).
/// </summary>
public sealed class MatrixScenario(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly Dictionary<string, string> _resources = new(StringComparer.Ordinal);
    private HostFactory<ApiProgram>? _api;

    public HostFactory<ApiProgram> Api => _api ?? throw new InvalidOperationException("Scenario not initialized.");

    /// <summary>Id of a seeded resource, e.g. <c>Get("group:lions")</c>.</summary>
    public string Get(string name) =>
        _resources.TryGetValue(name, out var id) ? id : throw new KeyNotFoundException($"Resource '{name}' was not seeded.");

    internal void Set(string name, string id) => _resources[name] = id;

    public HttpClient CreateAnonymousClient() => Api.CreateClient();

    public async ValueTask InitializeAsync()
    {
        _api = new HostFactory<ApiProgram>(TestSettings.For(await postgres.CreateDatabaseAsync(), autoMigrate: true));
        using var _ = _api.CreateClient(); // start the host: migrations run before it serves
        await SeedAsync();
    }

    private static Task SeedAsync() => Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_api is not null)
        {
            await _api.DisposeAsync();
        }
    }
}
