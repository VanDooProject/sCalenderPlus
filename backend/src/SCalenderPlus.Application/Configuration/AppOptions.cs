using System.ComponentModel.DataAnnotations;

namespace SCalenderPlus.Application.Configuration;

/// <summary>General application settings (environment variables <c>App__*</c>).</summary>
public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>Public base URL of the app (feed links, emails), e.g. <c>https://app.example.com</c>.</summary>
    [Required]
    [Url]
    public string PublicBaseUrl { get; set; } = string.Empty;
}
