using System.Text;

namespace SCalenderPlus.Application.Import;

/// <summary>A content line (RFC 5545 §3.1): name, parameters (first value of each, upper-case names) and the raw value (TEXT not yet unescaped).</summary>
public sealed record IcsProperty(string Name, IReadOnlyDictionary<string, string> Parameters, string Value)
{
    public string? Param(string name) => Parameters.GetValueOrDefault(name);
}

/// <summary>A component (<c>BEGIN:NAME</c> … <c>END:NAME</c>) with its properties and sub-components, in file order.</summary>
public sealed class IcsComponent(string name)
{
    public string Name { get; } = name;

    public List<IcsProperty> Properties { get; } = [];

    public List<IcsComponent> Components { get; } = [];

    public IcsProperty? First(string name) => Properties.Find(p => p.Name == name);

    public IEnumerable<IcsProperty> All(string name) => Properties.Where(p => p.Name == name);
}

/// <summary>Why a file could not be read at all.</summary>
public enum IcsReadError
{
    /// <summary>Not an iCalendar object (no <c>BEGIN:VCALENDAR</c>, unbalanced components, broken content lines).</summary>
    Invalid,

    /// <summary>More <c>VEVENT</c>s than allowed (the reader stops counting there).</summary>
    TooManyEvents,
}

/// <summary>
/// A small, lenient-where-it-is-safe reader of iCalendar objects (RFC 5545 §3.1–3.6) for the one-time upload
/// (ical-caldav.md §2) — the counterpart of the feeds' <c>IcsWriter</c>, and like it in-house (data-model.md §9):
/// the import needs the raw property values (RRULE text for <c>Core/Recurrence</c>, TZIDs to map, RECURRENCE-IDs)
/// rather than an object model with its own time types, and it must stay bounded on hostile input. It unfolds lines
/// (CRLF or bare LF followed by space/tab), splits names, parameters (quoted values, comma lists — the first value
/// is kept) and values, and builds the component tree. Components must balance; unknown ones are kept (and ignored
/// by the importer). At most <paramref name="maxEvents"/> <c>VEVENT</c>s and nesting depth 10.
/// </summary>
public static class IcsReader
{
    private const int MaxDepth = 10;

    /// <summary>The top-level <c>VCALENDAR</c> components, or null with <paramref name="error"/>.</summary>
    public static IReadOnlyList<IcsComponent>? Read(string text, int maxEvents, out IcsReadError? error, out string? detail)
    {
        ArgumentNullException.ThrowIfNull(text);
        error = null;
        detail = null;
        var roots = new List<IcsComponent>();
        var stack = new Stack<IcsComponent>();
        var events = 0;
        var lineNumber = 0;
        foreach (var (line, number) in Unfold(text))
        {
            lineNumber = number;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var property = ParseLine(line);
            if (property is null)
            {
                return Fail(IcsReadError.Invalid, $"Line {number} is not an iCalendar content line.", out error, out detail);
            }

            if (property.Name == "BEGIN")
            {
                var name = property.Value.Trim().ToUpperInvariant();
                if (stack.Count == 0 && name != "VCALENDAR")
                {
                    return Fail(IcsReadError.Invalid, "The file does not start with BEGIN:VCALENDAR.", out error, out detail);
                }

                if (stack.Count >= MaxDepth)
                {
                    return Fail(IcsReadError.Invalid, $"Components are nested too deeply (line {number}).", out error, out detail);
                }

                if (name == "VEVENT" && ++events > maxEvents)
                {
                    return Fail(IcsReadError.TooManyEvents, $"The file has more than {maxEvents} events.", out error, out detail);
                }

                var component = new IcsComponent(name);
                if (stack.TryPeek(out var parent))
                {
                    parent.Components.Add(component);
                }
                else
                {
                    roots.Add(component);
                }

                stack.Push(component);
            }
            else if (property.Name == "END")
            {
                var name = property.Value.Trim().ToUpperInvariant();
                if (!stack.TryPop(out var open) || open.Name != name)
                {
                    return Fail(IcsReadError.Invalid, $"END:{name} on line {number} does not close an open component.", out error, out detail);
                }
            }
            else if (stack.TryPeek(out var current))
            {
                current.Properties.Add(property);
            }
            else
            {
                return Fail(IcsReadError.Invalid, $"Line {number} is outside of BEGIN:VCALENDAR … END:VCALENDAR.", out error, out detail);
            }
        }

        if (stack.Count > 0)
        {
            return Fail(IcsReadError.Invalid, $"The file ends inside {stack.Peek().Name} (line {lineNumber}): it may be truncated.", out error, out detail);
        }

        return roots.Count == 0
            ? Fail(IcsReadError.Invalid, "The file contains no BEGIN:VCALENDAR.", out error, out detail)
            : roots;
    }

    /// <summary>Unescapes a TEXT value (RFC 5545 §3.3.11): <c>\\</c>, <c>\;</c>, <c>\,</c>, <c>\n</c>/<c>\N</c>.</summary>
    public static string UnescapeText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!value.Contains('\\', StringComparison.Ordinal))
        {
            return value;
        }

        var result = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\\' && i + 1 < value.Length)
            {
                var next = value[++i];
                result.Append(next is 'n' or 'N' ? '\n' : next);
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    /// <summary>Splits a list value on unescaped commas (CATEGORIES, EXDATE, RDATE), unescaping TEXT.</summary>
    public static IReadOnlyList<string> SplitList(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var items = new List<string>();
        var current = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\\' && i + 1 < value.Length)
            {
                current.Append(c).Append(value[++i]);
            }
            else if (c == ',')
            {
                items.Add(UnescapeText(current.ToString()));
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        items.Add(UnescapeText(current.ToString()));
        return items;
    }

    private static IReadOnlyList<IcsComponent>? Fail(IcsReadError kind, string message, out IcsReadError? error, out string? detail)
    {
        error = kind;
        detail = message;
        return null;
    }

    /// <summary>Logical lines with the number of their first physical line; continuation lines are appended without their leading space/tab.</summary>
    private static IEnumerable<(string Line, int Number)> Unfold(string text)
    {
        var current = new StringBuilder();
        var start = 0;
        var number = 0;
        var position = 0;
        while (position <= text.Length)
        {
            var end = text.IndexOf('\n', position);
            var physical = end < 0 ? text[position..] : text[position..end];
            if (physical.EndsWith('\r'))
            {
                physical = physical[..^1];
            }

            number++;
            if (physical.Length > 0 && (physical[0] == ' ' || physical[0] == '\t') && current.Length > 0)
            {
                current.Append(physical, 1, physical.Length - 1);
            }
            else
            {
                if (current.Length > 0)
                {
                    yield return (current.ToString(), start);
                }

                current.Clear().Append(physical);
                start = number;
            }

            if (end < 0)
            {
                break;
            }

            position = end + 1;
        }

        if (current.Length > 0)
        {
            yield return (current.ToString(), start);
        }
    }

    /// <summary><c>NAME *(";" param) ":" value</c>; parameter values may be quoted (and then contain <c>;:,</c>).</summary>
    private static IcsProperty? ParseLine(string line)
    {
        var i = 0;
        while (i < line.Length && line[i] is not (';' or ':'))
        {
            i++;
        }

        if (i == 0 || i == line.Length)
        {
            return null;
        }

        var name = line[..i].Trim().ToUpperInvariant();
        if (name.Length == 0 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
        {
            return null;
        }

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        while (i < line.Length && line[i] == ';')
        {
            i++;
            var nameStart = i;
            while (i < line.Length && line[i] is not ('=' or ';' or ':'))
            {
                i++;
            }

            if (i == line.Length || line[i] != '=')
            {
                return null;
            }

            var paramName = line[nameStart..i].Trim().ToUpperInvariant();
            i++;
            string? first = null;
            while (true)
            {
                string value;
                if (i < line.Length && line[i] == '"')
                {
                    var close = line.IndexOf('"', i + 1);
                    if (close < 0)
                    {
                        return null;
                    }

                    value = line[(i + 1)..close];
                    i = close + 1;
                }
                else
                {
                    var valueStart = i;
                    while (i < line.Length && line[i] is not (',' or ';' or ':'))
                    {
                        i++;
                    }

                    value = line[valueStart..i];
                }

                first ??= value;
                if (i < line.Length && line[i] == ',')
                {
                    i++;
                    continue;
                }

                break;
            }

            parameters.TryAdd(paramName, first);
        }

        return i < line.Length && line[i] == ':'
            ? new IcsProperty(name, parameters, line[(i + 1)..])
            : null;
    }
}
