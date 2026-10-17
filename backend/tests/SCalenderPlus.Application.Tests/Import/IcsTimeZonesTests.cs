using SCalenderPlus.Application.Import;

namespace SCalenderPlus.Application.Tests.Import;

public sealed class IcsTimeZonesTests
{
    [Theory]
    [InlineData("Europe/Berlin", "Europe/Berlin")]
    [InlineData("europe/berlin", "Europe/Berlin")]
    [InlineData("UTC", "UTC")]
    [InlineData("W. Europe Standard Time", "Europe/Berlin")]
    [InlineData("Pacific Standard Time", "America/Los_Angeles")]
    [InlineData("Eastern Standard Time", "America/New_York")]
    [InlineData("GMT Standard Time", "Europe/London")]
    [InlineData("Romance Standard Time", "Europe/Paris")]
    [InlineData("\"W. Europe Standard Time\"", "Europe/Berlin")]
    [InlineData("/mozilla.org/20050126_1/Europe/Berlin", "Europe/Berlin")]
    [InlineData("/freeassociation.sourceforge.net/Tzfile/America/Argentina/Buenos_Aires", "America/Argentina/Buenos_Aires")]
    [InlineData("(UTC+01:00) Amsterdam, Berlin, Bern, Rome, Stockholm, Vienna", "Europe/Amsterdam")]
    [InlineData("(UTC-05:00) Eastern Time (US & Canada)", "America/New_York")]
    [InlineData("(UTC+09:00) Osaka, Sapporo, Tokyo", "Asia/Tokyo")]
    public void Maps_exporter_ids_to_iana(string tzid, string expected) =>
        Assert.Equal(expected, IcsTimeZones.Resolve(tzid)?.Id);

    [Theory]
    [InlineData("Mars Standard Time")]
    [InlineData("Customized Time Zone")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_ids_are_null(string? tzid) => Assert.Null(IcsTimeZones.Resolve(tzid));

    [Fact]
    public void Uses_the_location_hint_of_the_vtimezone()
    {
        var definition = new IcsComponent("VTIMEZONE");
        definition.Properties.Add(new IcsProperty("TZID", new Dictionary<string, string>(), "Customized Time Zone"));
        definition.Properties.Add(new IcsProperty("X-LIC-LOCATION", new Dictionary<string, string>(), "Europe/Vienna"));

        Assert.Equal("Europe/Vienna", IcsTimeZones.Resolve("Customized Time Zone", definition)?.Id);
    }
}
