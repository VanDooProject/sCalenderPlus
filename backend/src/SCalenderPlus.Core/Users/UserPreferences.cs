using NodaTime;
using SCalenderPlus.Core.Time;

namespace SCalenderPlus.Core.Users;

/// <summary>
/// A user's personal settings (docs/architecture/data-model.md §2 <c>users</c>): UI locale, IANA time zone
/// (validated against the bundled tzdb, never the host zone) and first day of the week. Identity concerns
/// (email, password, 2FA) live in Infrastructure; this is the part the domain cares about.
/// </summary>
public sealed record UserPreferences(string Locale, string TimeZone, IsoDayOfWeek WeekStart)
{
    public const string DefaultLocale = "en";
    public const string DefaultTimeZone = "UTC";

    /// <summary>UI languages of the product (en, de).</summary>
    public static IReadOnlyList<string> SupportedLocales { get; } = ["en", "de"];

    public static UserPreferences Default { get; } = new(DefaultLocale, DefaultTimeZone, IsoDayOfWeek.Monday);

    public static bool IsSupportedLocale(string? locale) =>
        locale is not null && SupportedLocales.Contains(locale, StringComparer.Ordinal);

    public static bool IsValidTimeZone(string? timeZone) => TimeZoneIds.IsValid(timeZone);

    /// <summary>API representation of a week start: the lowercase English day name (<c>monday</c>).</summary>
    public static string FormatWeekStart(IsoDayOfWeek day) => day.ToString().ToLowerInvariant();

    public static bool TryParseWeekStart(string? value, out IsoDayOfWeek day)
    {
        day = IsoDayOfWeek.None;
        if (string.IsNullOrEmpty(value) || !value.All(char.IsAsciiLetterLower))
        {
            return false;
        }

        return Enum.TryParse(value, ignoreCase: true, out day) && day != IsoDayOfWeek.None;
    }
}
