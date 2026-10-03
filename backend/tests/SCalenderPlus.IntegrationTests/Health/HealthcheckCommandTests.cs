using System.Text.Json;
using SCalenderPlus.Infrastructure.Hosting;
using SCalenderPlus.IntegrationTests.Infrastructure;

namespace SCalenderPlus.IntegrationTests.Health;

/// <summary>The `healthcheck` CLI used as container HEALTHCHECK in chiseled images.</summary>
public sealed class HealthcheckCommandTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

    [Theory]
    [InlineData(new string[0], null, 8080, "http://127.0.0.1:8080/health/ready")]
    [InlineData(new[] { "live" }, null, 8081, "http://127.0.0.1:8081/health/live")]
    [InlineData(new string[0], "9000", 8080, "http://127.0.0.1:9000/health/ready")]
    [InlineData(new string[0], "9000;9001", 8080, "http://127.0.0.1:9000/health/ready")]
    [InlineData(new[] { "ready", "--url", "http://localhost:5080" }, "9000", 8080, "http://localhost:5080/health/ready")]
    public void Resolves_the_probe_url(string[] args, string? httpPorts, int defaultPort, string expected) =>
        Assert.Equal(new Uri(expected), HealthcheckCommand.ResolveTarget(args, defaultPort, httpPorts));

    [Fact]
    public void Rejects_unknown_arguments() =>
        Assert.Throws<ArgumentException>(() => HealthcheckCommand.ResolveTarget(["bogus"], 8080, null));

    [Fact]
    public async Task Exits_1_when_the_database_is_down_and_0_for_liveness()
    {
        await using var api = await RunningBackend.StartAsync(BackendProcess.Api, Environment(TestSettings.UnreachableDatabase));

        var ready = await Healthcheck(BackendProcess.Api, api.BaseUrl);
        var live = await Healthcheck(BackendProcess.Api, api.BaseUrl, "live");

        Assert.True(ready.ExitCode == 1, ready.Output);
        Assert.True(live.ExitCode == 0, live.Output);
    }

    [Fact]
    public async Task Exits_1_when_nothing_is_listening()
    {
        var result = await Healthcheck(BackendProcess.Worker, new Uri("http://127.0.0.1:1"));

        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public async Task Production_logs_are_single_line_json()
    {
        await using var api = await RunningBackend.StartAsync(BackendProcess.Api, Environment(TestSettings.UnreachableDatabase));

        var lines = api.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.NotEmpty(lines);
        Assert.All(lines, line =>
        {
            using var json = JsonDocument.Parse(line);
            Assert.True(json.RootElement.TryGetProperty("Timestamp", out _), line);
            Assert.True(json.RootElement.TryGetProperty("LogLevel", out _), line);
            Assert.True(json.RootElement.TryGetProperty("Category", out _), line);
        });
    }

    internal static Task<ProcessResult> Healthcheck(string project, Uri baseUrl, params string[] extra) =>
        BackendProcess.RunAsync(project, [HealthcheckCommand.Name, .. extra, "--url", baseUrl.ToString()], new Dictionary<string, string?>(), _timeout);

    internal static Dictionary<string, string?> Environment(string connectionString) => new()
    {
        ["App__PublicBaseUrl"] = "https://app.example.test",
        ["ConnectionStrings__Default"] = connectionString,
    };
}

[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class HealthcheckCommandWithDatabaseTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Exits_0_when_the_api_is_ready()
    {
        var environment = HealthcheckCommandTests.Environment(await postgres.CreateDatabaseAsync());
        environment["Database__AutoMigrate"] = "true";
        await using var api = await RunningBackend.StartAsync(BackendProcess.Api, environment);

        var result = await HealthcheckCommandTests.Healthcheck(BackendProcess.Api, api.BaseUrl);

        Assert.True(result.ExitCode == 0, result.Output + "\n--- api ---\n" + api.Output);
    }
}
