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
    }
}
