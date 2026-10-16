using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SCalenderPlus.Application.Configuration;

namespace SCalenderPlus.Application.Tests;

/// <summary>White-label branding (<c>Brand__*</c>): defaults, trimming and start-up validation.</summary>
public sealed class BrandOptionsTests
{
    [Fact]
    public void Defaults_to_the_product_name_without_a_support_address()
    {
        using var provider = BuildProvider([]);
        provider.GetRequiredService<IStartupValidator>().Validate();

        var brand = provider.GetRequiredService<IOptions<BrandOptions>>().Value;
        Assert.Equal("sCalenderPlus", brand.ProductName);
        Assert.Null(brand.SupportEmail);
    }

    [Fact]
    public void Configured_values_are_trimmed_and_an_empty_support_address_means_none()
    {
        using var provider = BuildProvider(new() { ["Brand:ProductName"] = "  Lions Calendar ", ["Brand:SupportEmail"] = " " });
        provider.GetRequiredService<IStartupValidator>().Validate();

        var brand = provider.GetRequiredService<IOptions<BrandOptions>>().Value;
        Assert.Equal("Lions Calendar", brand.ProductName);
        Assert.Null(brand.SupportEmail);
    }

    [Theory]
    [InlineData("Brand:ProductName", "")]
    [InlineData("Brand:ProductName", "   ")]
    [InlineData("Brand:SupportEmail", "not an address")]
    public void Empty_names_and_invalid_support_addresses_fail_startup_validation(string key, string value)
    {
        using var provider = BuildProvider(new() { [key] = value });

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Equal(typeof(BrandOptions), ex.OptionsType);
        Assert.Contains(key, ex.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        settings["App:PublicBaseUrl"] = "https://app.example.com";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddApplication(configuration).BuildServiceProvider();
    }
}
