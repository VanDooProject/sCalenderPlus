using SCalenderPlus.Core.Time;

namespace SCalenderPlus.Core.Tests;

public sealed class TimeZoneIdsTests
{
    [Theory]
    [InlineData("Europe/Berlin")]
    [InlineData("America/New_York")]
    [InlineData("Pacific/Kiritimati")]
    [InlineData("UTC")]
    public void Known_iana_zones_are_valid(string id) => Assert.True(TimeZoneIds.IsValid(id));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("CEST")]
    public void Unknown_or_empty_zones_are_invalid(string? id) => Assert.False(TimeZoneIds.IsValid(id));
}
