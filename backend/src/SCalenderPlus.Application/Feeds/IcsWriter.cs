using System.Globalization;
using System.Text;
using NodaTime;
using NodaTime.Text;

namespace SCalenderPlus.Application.Feeds;

/// <summary>
/// RFC 5545 content lines (§3.1): <c>NAME;PARAM=value:value</c> terminated by CRLF and folded after 75 octets
/// (continuation lines start with a space; multi-byte UTF-8 sequences and surrogate pairs are never split), TEXT
/// values escaped (§3.3.11: backslash, semicolon, comma, newline; other control characters dropped). Deterministic:
/// the same calls produce the same bytes (golden files).
/// </summary>
public sealed class IcsWriter
{
    private const int MaxOctets = 75;

    private static readonly LocalDateTimePattern _localPattern = LocalDateTimePattern.CreateWithInvariantCulture("uuuuMMdd'T'HHmmss");
    private static readonly LocalDatePattern _datePattern = LocalDatePattern.CreateWithInvariantCulture("uuuuMMdd");
    private static readonly InstantPattern _utcPattern = InstantPattern.CreateWithInvariantCulture("uuuuMMdd'T'HHmmss'Z'");

    private readonly StringBuilder _output = new();

    /// <summary>A property whose value is written as is (already in its value type's syntax).</summary>
    public IcsWriter Raw(string name, string value) => Line(name + ":" + value);

    /// <summary>A TEXT property (escaped).</summary>
    public IcsWriter Text(string name, string value) => Raw(name, EscapeText(value));

    /// <summary>A DATE-TIME in UTC (<c>20261102T170000Z</c>).</summary>
    public IcsWriter Utc(string name, Instant value) => Raw(name, FormatUtc(value));

    /// <summary>A local DATE-TIME with <c>TZID</c> (<c>DTSTART;TZID=Europe/Berlin:20261102T180000</c>).</summary>
    public IcsWriter Zoned(string name, string zoneId, LocalDateTime value) => Raw($"{name};TZID={zoneId}", FormatLocal(value));

    /// <summary>A DATE (<c>DTSTART;VALUE=DATE:20261102</c>).</summary>
    public IcsWriter Date(string name, LocalDate value) => Raw(name + ";VALUE=DATE", FormatDate(value));

    public IcsWriter Begin(string component) => Line("BEGIN:" + component);

    public IcsWriter End(string component) => Line("END:" + component);

    public override string ToString() => _output.ToString();

    public static string FormatLocal(LocalDateTime value) => _localPattern.Format(value);

    public static string FormatDate(LocalDate value) => _datePattern.Format(value);

    public static string FormatUtc(Instant value) => _utcPattern.Format(value);

    /// <summary>A UTC offset as <c>+hhmm</c> (with seconds when it has any).</summary>
    public static string FormatOffset(Offset offset)
    {
        var total = offset.Seconds;
        var sign = total < 0 ? '-' : '+';
        total = Math.Abs(total);
        var text = string.Create(CultureInfo.InvariantCulture, $"{sign}{total / 3600:00}{total / 60 % 60:00}");
        return total % 60 == 0 ? text : text + (total % 60).ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>RFC 5545 §3.3.11 TEXT escaping; CR is dropped (CRLF and LF both become <c>\n</c>), as are other control characters but TAB.</summary>
    public static string EscapeText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var escaped = new StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\':
                    escaped.Append(@"\\");
                    break;
                case ';':
                    escaped.Append(@"\;");
                    break;
                case ',':
                    escaped.Append(@"\,");
                    break;
                case '\n':
                    escaped.Append(@"\n");
                    break;
                case '\t':
                    escaped.Append(c);
                    break;
                default:
                    if (!char.IsControl(c))
                    {
                        escaped.Append(c);
                    }

                    break;
            }
        }

        return escaped.ToString();
    }

    /// <summary>Appends one content line, folded at <see cref="MaxOctets"/> octets per physical line.</summary>
    private IcsWriter Line(string line)
    {
        var octets = 0;
        for (var i = 0; i < line.Length; i++)
        {
            var length = char.IsHighSurrogate(line[i]) && i + 1 < line.Length && char.IsLowSurrogate(line[i + 1]) ? 2 : 1;
            var size = Encoding.UTF8.GetByteCount(line.AsSpan(i, length));
            if (octets + size > MaxOctets)
            {
                _output.Append("\r\n ");
                octets = 1; // the leading space counts
            }

            _output.Append(line, i, length);
            octets += size;
            i += length - 1;
        }

        _output.Append("\r\n");
        return this;
    }
}
