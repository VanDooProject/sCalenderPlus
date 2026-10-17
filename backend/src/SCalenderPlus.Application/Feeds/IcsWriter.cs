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
/// <para>
/// No value can start a content line of its own: every line drops CR, LF and every other control character (C0
/// but TAB, DEL, C1 including NEL) and the Unicode line and paragraph separators before folding, whatever the
/// caller passes — so stored data that predates validation (or comes from an import) cannot inject properties or
/// components. <c>TZID</c> parameter values are quoted when they contain <c>;</c>, <c>:</c> or <c>,</c>.
/// </para>
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
    public IcsWriter Zoned(string name, string zoneId, LocalDateTime value) => Raw($"{name};TZID={ParamValue(zoneId)}", FormatLocal(value));

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

    /// <summary>
    /// A parameter value (RFC 5545 §3.1: <c>paramtext / quoted-string</c>): quoted when it contains <c>;</c>,
    /// <c>:</c> or <c>,</c>; double quotes (never allowed in a parameter value) and control characters dropped.
    /// </summary>
    public static string ParamValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var clean = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c != '"' && !IsLineBreaking(c))
            {
                clean.Append(c);
            }
        }

        var text = clean.ToString();
        return text.AsSpan().IndexOfAny(";:,") >= 0 ? "\"" + text + "\"" : text;
    }

    /// <summary>
    /// Characters a content line never carries (RFC 5545 §3.1 allows no CTL but HTAB in values): C0 but TAB, DEL,
    /// C1 (NEL among them) and the line/paragraph separators some parsers treat as line breaks.
    /// </summary>
    public static bool IsLineBreaking(char c) => c != '\t' && (char.IsControl(c) || c is '\u2028' or '\u2029');

    /// <summary>
    /// RFC 5545 §3.3.11 TEXT escaping; CR is dropped (CRLF and LF both become <c>\n</c>), as are other control
    /// characters but TAB; the Unicode line and paragraph separators become <c>\n</c> too.
    /// </summary>
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
                case '\n' or '\u2028' or '\u2029':
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

    /// <summary>
    /// Appends one content line, folded at <see cref="MaxOctets"/> octets per physical line. Line-breaking characters
    /// (<see cref="IsLineBreaking"/>) are dropped first: one call is always exactly one logical line.
    /// </summary>
    private IcsWriter Line(string line)
    {
        if (line.Any(IsLineBreaking))
        {
            line = string.Concat(line.Where(c => !IsLineBreaking(c)));
        }

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
