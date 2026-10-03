using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SCalenderPlus.Application.Configuration;

namespace SCalenderPlus.Application.Tests;

public sealed class AppOptionsTests
{
    [Fact]
    public void Valid_configuration_binds()
    {
        using var provider = BuildProvider(new() { ["App:PublicBaseUrl"] = "https://app.example.com" });

        provider.GetRequiredService<IStartupValidator>().Validate();

        Assert.Equal("https://app.example.com", provider.GetRequiredService<IOptions<AppOptions>>().Value.PublicBaseUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    public void Missing_or_invalid_public_base_url_fails_startup_validation(string? value)
    {
        using var provider = BuildProvider(new() { ["App:PublicBaseUrl"] = value });

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Equal(typeof(AppOptions), ex.OptionsType);
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddApplication(configuration).BuildServiceProvider();
    }
}
