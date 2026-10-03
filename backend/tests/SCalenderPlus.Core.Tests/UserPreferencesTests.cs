using NodaTime;
using SCalenderPlus.Core.Users;

namespace SCalenderPlus.Core.Tests;

public sealed class UserPreferencesTests
{
    [Theory]
    [InlineData("en", true)]
    [InlineData("de", true)]
    [InlineData("DE", false)]
    [InlineData("fr", false)]
    [InlineData(null, false)]
    public void Only_en_and_de_are_supported_locales(string? locale, bool expected) =>
        Assert.Equal(expected, UserPreferences.IsSupportedLocale(locale));

    [Theory]
    [InlineData("monday", IsoDayOfWeek.Monday)]
    [InlineData("saturday", IsoDayOfWeek.Saturday)]
    [InlineData("sunday", IsoDayOfWeek.Sunday)]
    public void Week_start_round_trips_as_lowercase_day_name(string value, IsoDayOfWeek day)
    {
        Assert.True(UserPreferences.TryParseWeekStart(value, out var parsed));
        Assert.Equal(day, parsed);
        Assert.Equal(value, UserPreferences.FormatWeekStart(day));
    }

    [Theory]
    [InlineData("Monday")]
    [InlineData("none")]
    [InlineData("1")]
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_week_starts_are_rejected(string? value) =>
        Assert.False(UserPreferences.TryParseWeekStart(value, out _));

    [Fact]
    public void Default_is_english_utc_monday() =>
        Assert.Equal(new UserPreferences("en", "UTC", IsoDayOfWeek.Monday), UserPreferences.Default);
}
