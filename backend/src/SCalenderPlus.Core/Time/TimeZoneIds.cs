using NodaTime;

namespace SCalenderPlus.Core.Time;

/// <summary>
/// IANA time zone identifiers, validated against the bundled tzdb (never the host time zone).
/// </summary>
public static class TimeZoneIds
{
    /// <summary>Returns <c>true</c> when <paramref name="id"/> is a known IANA (tzdb) zone id.</summary>
    public static bool IsValid(string? id) =>
        !string.IsNullOrWhiteSpace(id) && DateTimeZoneProviders.Tzdb.GetZoneOrNull(id) is not null;
}
