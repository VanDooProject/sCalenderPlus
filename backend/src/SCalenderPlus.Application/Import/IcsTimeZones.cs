using NodaTime;
using NodaTime.TimeZones;

namespace SCalenderPlus.Application.Import;

/// <summary>
/// Maps the <c>TZID</c>s of uploaded files to IANA zones of the bundled tzdb (data-model.md §10: events store IANA
/// ids only). Exporters differ: Google and Apple write IANA ids, Outlook and Exchange write Windows zone names
/// (<c>W. Europe Standard Time</c>, mapped through CLDR's windowsZones in NodaTime) or display names
/// (<c>(UTC+01:00) Amsterdam, Berlin, Bern, Rome, Stockholm, Vienna</c>, matched by their cities), Thunderbird and
/// older libical prefix ids with a path (<c>/mozilla.org/20050126_1/Europe/Berlin</c>), and some files carry the
/// IANA id only in the <c>VTIMEZONE</c> (<c>X-LIC-LOCATION</c>). The VTIMEZONE's own rules are never used: the
/// tzdb's are authoritative (and current). Unknown ids return null — the importer then falls back to the file's
/// <c>X-WR-TIMEZONE</c> or the calendar's zone and warns.
/// </summary>
public static class IcsTimeZones
{
    private static readonly IDateTimeZoneProvider _tzdb = DateTimeZoneProviders.Tzdb;

    private static readonly Lazy<Dictionary<string, string>> _byLowerId = new(() =>
        _tzdb.Ids.ToDictionary(id => id.ToLowerInvariant(), id => id, StringComparer.Ordinal));

    /// <summary>tzdb ids by their last segment (<c>berlin</c> → <c>Europe/Berlin</c>), canonical ids first.</summary>
    private static readonly Lazy<Dictionary<string, string>> _byCity = new(() =>
    {
        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        var canonical = TzdbDateTimeZoneSource.Default.CanonicalIdMap;
        foreach (var id in _tzdb.Ids.OrderBy(id => canonical.TryGetValue(id, out var c) && c == id ? 0 : 1).ThenBy(id => id, StringComparer.Ordinal))
        {
            if (id.Contains('/', StringComparison.Ordinal) && !id.StartsWith("Etc/", StringComparison.Ordinal))
            {
                index.TryAdd(id[(id.LastIndexOf('/') + 1)..].ToLowerInvariant(), id);
            }
        }

        return index;
    });

    private static readonly Lazy<Dictionary<string, string>> _windows = new(() =>
        new Dictionary<string, string>(TzdbDateTimeZoneSource.Default.WindowsMapping.PrimaryMapping, StringComparer.OrdinalIgnoreCase));

    /// <summary>Windows display names without a city (US zones).</summary>
    private static readonly Dictionary<string, string> _regions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Eastern Time (US & Canada)"] = "America/New_York",
        ["Central Time (US & Canada)"] = "America/Chicago",
        ["Mountain Time (US & Canada)"] = "America/Denver",
        ["Pacific Time (US & Canada)"] = "America/Los_Angeles",
        ["Coordinated Universal Time"] = "UTC",
    };

    /// <summary>The IANA zone for <paramref name="tzid"/>, using hints of its <c>VTIMEZONE</c> (<paramref name="definition"/>) when given; null when unknown.</summary>
    public static DateTimeZone? Resolve(string? tzid, IcsComponent? definition = null)
    {
        var id = tzid?.Trim().Trim('"').Trim();
        if (string.IsNullOrEmpty(id) || id.Length > 200)
        {
            return null;
        }

        return Direct(id)
            ?? Windows(id)
            ?? Path(id)
            ?? Hint(definition)
            ?? DisplayName(id);
    }

    private static DateTimeZone? Direct(string id) =>
        _tzdb.GetZoneOrNull(id) ?? (_byLowerId.Value.TryGetValue(id.ToLowerInvariant(), out var exact) ? _tzdb[exact] : null);

    private static DateTimeZone? Windows(string id) =>
        _windows.Value.TryGetValue(id, out var iana) ? _tzdb.GetZoneOrNull(iana) : null;

    /// <summary><c>/mozilla.org/20050126_1/Europe/Berlin</c>, <c>/freeassociation.sourceforge.net/Tzfile/America/Argentina/Buenos_Aires</c>.</summary>
    private static DateTimeZone? Path(string id)
    {
        var segments = id.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var take = Math.Min(3, segments.Length); take >= 2; take--)
        {
            if (Direct(string.Join('/', segments[^take..])) is { } zone)
            {
                return zone;
            }
        }

        return null;
    }

    /// <summary><c>X-LIC-LOCATION</c> (libical, Google), <c>TZID-ALIAS-OF</c> (RFC 7808-style aliases) of the definition.</summary>
    private static DateTimeZone? Hint(IcsComponent? definition)
    {
        foreach (var name in new[] { "X-LIC-LOCATION", "TZID-ALIAS-OF", "X-MS-OLSON" })
        {
            if (definition?.First(name)?.Value.Trim() is { Length: > 0 } hint && (Direct(hint) ?? Windows(hint)) is { } zone)
            {
                return zone;
            }
        }

        return null;
    }

    /// <summary>Windows display names: <c>(UTC+01:00) Amsterdam, Berlin, Bern, Rome, Stockholm, Vienna</c> → the first city tzdb knows.</summary>
    private static DateTimeZone? DisplayName(string id)
    {
        var text = id.StartsWith('(') && id.IndexOf(')', StringComparison.Ordinal) is > 0 and var close ? id[(close + 1)..].Trim() : id;
        if (_regions.TryGetValue(text, out var region))
        {
            return _tzdb.GetZoneOrNull(region);
        }

        foreach (var city in text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (_byCity.Value.TryGetValue(city.Replace(' ', '_').ToLowerInvariant(), out var match))
            {
                return _tzdb[match];
            }
        }

        return null;
    }
}
