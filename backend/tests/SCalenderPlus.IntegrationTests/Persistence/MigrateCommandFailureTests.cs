using SCalenderPlus.IntegrationTests.Infrastructure;

namespace SCalenderPlus.IntegrationTests.Persistence;

/// <summary>A failed migration must fail the deploy (non-zero exit). No Docker needed.</summary>
public sealed class MigrateCommandFailureTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task Migrate_exits_non_zero_without_a_connection_string()
    {
        var result = await BackendProcess.RunAsync(BackendProcess.Api, ["migrate"], new Dictionary<string, string?>(), _timeout);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("ConnectionStrings:Default is required", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Migrate_exits_non_zero_when_the_database_is_unreachable()
    {
        var environment = new Dictionary<string, string?>
        {
            ["ConnectionStrings__Default"] = "Host=127.0.0.1;Port=1;Database=scal;Username=scal;Password=scal;Timeout=3",
        };

        var result = await BackendProcess.RunAsync(BackendProcess.Api, ["migrate"], environment, _timeout);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Database migration failed", result.Output, StringComparison.Ordinal);
    }
}
