using System.ComponentModel.DataAnnotations;

namespace SCalenderPlus.Application.Configuration;

/// <summary>
/// White-label branding of server-rendered output (environment variables <c>Brand__*</c>): the product name in
/// emails (subjects, bodies, footers), the sender name and the authenticator-app issuer of 2FA. Per deployment,
/// no rebuild; the web app is branded separately through <c>/config.json</c>.
/// </summary>
public sealed class BrandOptions
{
    public const string SectionName = "Brand";
    public const string DefaultProductName = "sCalenderPlus";
    public const int ProductNameMaxLength = 100;

    /// <summary>The product name users see (<c>Brand__ProductName</c>); default <see cref="DefaultProductName"/>.</summary>
    [Required(ErrorMessage = "Brand:ProductName must not be empty.")]
    [StringLength(ProductNameMaxLength, ErrorMessage = "Brand:ProductName has at most 100 characters.")]
    public string ProductName { get; set; } = DefaultProductName;

    /// <summary>Optional support address (<c>Brand__SupportEmail</c>), shown in email footers; empty = none.</summary>
    [EmailAddress(ErrorMessage = "Brand:SupportEmail must be an email address.")]
    public string? SupportEmail { get; set; }

    /// <summary>Trims the values; an empty support address means none.</summary>
    internal void Normalize()
    {
        ProductName = ProductName?.Trim() ?? string.Empty;
        SupportEmail = string.IsNullOrWhiteSpace(SupportEmail) ? null : SupportEmail.Trim();
    }
}
