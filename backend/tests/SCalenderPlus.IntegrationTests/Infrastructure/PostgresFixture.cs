using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(SCalenderPlus.IntegrationTests.Infrastructure.PostgresFixture))]

namespace SCalenderPlus.IntegrationTests.Infrastructure;

/// <summary>
/// One PostgreSQL 17 container (Testcontainers) per test run, started lazily on first use; every test gets
/// its own empty database. Tests using it carry <c>[Trait("Category", "Docker")]</c> and are skipped when
/// <c>SCAL_SKIP_DOCKER_TESTS=true</c> (e.g. on machines without a Docker daemon).
/// </summary>
public sealed class PostgresFixture : IAsyncDisposable
{
    public const string Category = "Category";
    public const string Docker = "Docker";
    public const string SkipEnvironmentVariable = "SCAL_SKIP_DOCKER_TESTS";
    public const string Image = "postgres:17-alpine";

    private readonly SemaphoreSlim _startLock = new(1, 1);
    private PostgreSqlContainer? _container;

    public static bool SkipRequested =>
        Environment.GetEnvironmentVariable(SkipEnvironmentVariable) is { } value
        && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));

    /// <summary>Creates a fresh, empty database and returns its connection string.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        Assert.SkipWhen(SkipRequested, $"Docker-based test skipped ({SkipEnvironmentVariable} is set).");

        var server = await StartAsync();
        var name = "test_" + Guid.NewGuid().ToString("N");

        await using (var connection = new NpgsqlConnection(server))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(server) { Database = name }.ConnectionString;
    }

    private async Task<string> StartAsync()
    {
        await _startLock.WaitAsync();
        try
        {
            if (_container is null)
            {
                var container = new PostgreSqlBuilder(Image).Build();
                await container.StartAsync();
                _container = container;
            }

            return _container.GetConnectionString();
        }
        finally
        {
            _startLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }

        _startLock.Dispose();
    }
}
