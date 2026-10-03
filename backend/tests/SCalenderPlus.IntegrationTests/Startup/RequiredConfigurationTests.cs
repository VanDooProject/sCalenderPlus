using SCalenderPlus.IntegrationTests.Infrastructure;

namespace SCalenderPlus.IntegrationTests.Startup;

/// <summary>A misconfigured container must fail fast (non-zero exit) instead of starting half-working.</summary>
public sealed class RequiredConfigurationTests
{
    [Theory]
    [InlineData(BackendProcess.Api)]
    [InlineData(BackendProcess.Worker)]
    public async Task Process_exits_non_zero_when_required_configuration_is_missing(string project)
    {
        var result = await BackendProcess.RunAsync(project, [], new Dictionary<string, string?>(), TimeSpan.FromSeconds(60));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Invalid configuration", result.Output, StringComparison.Ordinal);
        Assert.Contains("PublicBaseUrl", result.Output, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings:Default is required", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Worker_requires_smtp_settings()
    {
        var environment = new Dictionary<string, string?>
        {
            ["App__PublicBaseUrl"] = "https://app.example.test",
            ["ConnectionStrings__Default"] = TestSettings.UnreachableDatabase,
        };

        var result = await BackendProcess.RunAsync(BackendProcess.Worker, [], environment, TimeSpan.FromSeconds(60));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Smtp:Host is required", result.Output, StringComparison.Ordinal);
        Assert.Contains("Smtp:From is required", result.Output, StringComparison.Ordinal);
    }
}
