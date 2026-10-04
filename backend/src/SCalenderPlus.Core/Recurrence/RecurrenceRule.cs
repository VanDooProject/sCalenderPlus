using System.Globalization;
using System.Text;
using NodaTime;
using NodaTime.Text;

namespace SCalenderPlus.Core.Recurrence;

/// <summary><c>FREQ</c> of a supported rule (sub-daily frequencies are not supported).</summary>
public enum RecurrenceFrequency
{
    Daily,
    Weekly,
    Monthly,
    Yearly,
}

/// <summary>A <c>BYDAY</c> entry: a weekday, optionally with an ordinal (<c>2MO</c> = second Monday, <c>-1FR</c> = last Friday; 0 = every).</summary>
public readonly record struct WeekdayEntry(int Ordinal, IsoDayOfWeek Day)
{
    public override string ToString() => (Ordinal == 0 ? string.Empty : Ordinal.ToString(CultureInfo.InvariantCulture)) + RecurrenceRule.DayCode(Day);
}

/// <summary>Why a rule (or a recurrence member) was refused: <see cref="NotSupported"/> = valid RFC 5545 we do not implement.</summary>
/// <param name="Field">The API member (<c>recurrence.rrule</c>, <c>recurrence.rdates</c>, …).</param>
public sealed record RecurrenceProblem(bool NotSupported, string Field, string Message);

/// <summary>
/// An RFC 5545 <c>RRULE</c> of the supported subset (data-model.md §9): <c>FREQ</c> <c>DAILY</c> | <c>WEEKLY</c> |
/// <c>MONTHLY</c> | <c>YEARLY</c>, <c>INTERVAL</c>, <c>COUNT</c> or <c>UNTIL</c>, <c>BYMONTH</c>, <c>BYMONTHDAY</c>,
/// <c>BYDAY</c> (with ordinals for monthly/yearly rules), <c>BYSETPOS</c> and <c>WKST</c>. Sub-daily frequencies,
/// <c>BYHOUR</c>/<c>BYMINUTE</c>/<c>BYSECOND</c>, <c>BYYEARDAY</c>, <c>BYWEEKNO</c>, <c>RSCALE</c>/<c>SKIP</c> and
/// extension parts are refused as not supported. Build with <see cref="Parse"/> and bind <c>UNTIL</c> to a series
/// with <see cref="ForSeries"/>; <see cref="ToString"/> is the canonical text (stored, exported to iCalendar).
/// </summary>
public sealed record RecurrenceRule
{
    /// <summary>Largest <c>COUNT</c>: a series with more occurrences is open-ended (leave out <c>COUNT</c>).</summary>
    public const int MaxCount = 5000;

    /// <summary>Largest <c>INTERVAL</c>.</summary>
    public const int MaxInterval = 1000;

    /// <summary>Longest accepted rule text.</summary>
    public const int MaxLength = 500;

    private static readonly string[] _dayCodes = ["MO", "TU", "WE", "TH", "FR", "SA", "SU"];

    private static readonly LocalDatePattern _datePattern = LocalDatePattern.CreateWithInvariantCulture("uuuuMMdd");
    private static readonly LocalDateTimePattern _localPattern = LocalDateTimePattern.CreateWithInvariantCulture("uuuuMMdd'T'HHmmss");
    private static readonly InstantPattern _utcPattern = InstantPattern.CreateWithInvariantCulture("uuuuMMdd'T'HHmmss'Z'");

    public RecurrenceFrequency Frequency { get; init; }

    public int Interval { get; init; } = 1;

    public int? Count { get; init; }

    /// <summary><c>UNTIL</c> of a timed series (inclusive, compared with occurrence starts).</summary>
    public Instant? UntilUtc { get; init; }

    /// <summary><c>UNTIL</c> of an all-day series (inclusive), or as parsed (date form) before <see cref="ForSeries"/>.</summary>
    public LocalDate? UntilDate { get; init; }

    /// <summary><c>UNTIL</c> as a floating local date-time, only before <see cref="ForSeries"/> binds it to the series' zone.</summary>
    public LocalDateTime? UntilLocal { get; init; }

    public IReadOnlyList<int> ByMonth { get; init; } = [];

    public IReadOnlyList<int> ByMonthDay { get; init; } = [];

    public IReadOnlyList<WeekdayEntry> ByDay { get; init; } = [];

    public IReadOnlyList<int> BySetPos { get; init; } = [];

    public IsoDayOfWeek WeekStart { get; init; } = IsoDayOfWeek.Monday;

    public bool HasUntil => UntilUtc is not null || UntilDate is not null || UntilLocal is not null;

    /// <summary>The rule ends (<c>COUNT</c> or <c>UNTIL</c>); otherwise the series is infinite.</summary>
    public bool IsFinite => Count is not null || HasUntil;

    /// <summary>
    /// Parses an RRULE value (an optional <c>RRULE:</c> prefix and letter case are ignored). Returns null with
    /// <paramref name="problem"/> set when the text is malformed or uses parts outside the supported subset.
    /// </summary>
    public static RecurrenceRule? Parse(string? text, out RecurrenceProblem? problem)
    {
        problem = null;
        var value = text?.Trim() ?? string.Empty;
        if (value.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase))
        {
            value = value[6..];
        }

        if (value.Length == 0 || value.Length > MaxLength)
        {
            problem = Invalid($"Give an RRULE of at most {MaxLength} characters, e.g. FREQ=WEEKLY;BYDAY=MO.");
            return null;
        }

        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in value.Split(';'))
        {
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0 || eq == part.Length - 1)
            {
                problem = Invalid($"'{part}' is not a NAME=VALUE rule part.");
                return null;
            }

            var name = part[..eq].Trim().ToUpperInvariant();
            if (!parts.TryAdd(name, part[(eq + 1)..].Trim().ToUpperInvariant()))
            {
                problem = Invalid($"{name} appears twice.");
                return null;
            }
        }

        foreach (var name in parts.Keys)
        {
            switch (name)
            {
                case "FREQ" or "INTERVAL" or "COUNT" or "UNTIL" or "BYMONTH" or "BYMONTHDAY" or "BYDAY" or "BYSETPOS" or "WKST":
                    break;
                case "BYSECOND" or "BYMINUTE" or "BYHOUR" or "BYYEARDAY" or "BYWEEKNO" or "RSCALE" or "SKIP":
                    problem = NotSupported($"{name} is not supported (supported: FREQ, INTERVAL, COUNT, UNTIL, BYMONTH, BYMONTHDAY, BYDAY, BYSETPOS, WKST).");
                    return null;
                default:
                    problem = name.StartsWith("X-", StringComparison.Ordinal)
                        ? NotSupported($"The extension part {name} is not supported.")
                        : Invalid($"{name} is not an RRULE part.");
                    return null;
            }
        }

        var rule = new RecurrenceRule();
        if (!parts.TryGetValue("FREQ", out var freq))
        {
            problem = Invalid("FREQ is required, e.g. FREQ=WEEKLY.");
            return null;
        }

        switch (freq)
        {
            case "DAILY":
                rule = rule with { Frequency = RecurrenceFrequency.Daily };
                break;
            case "WEEKLY":
                rule = rule with { Frequency = RecurrenceFrequency.Weekly };
                break;
            case "MONTHLY":
                rule = rule with { Frequency = RecurrenceFrequency.Monthly };
                break;
            case "YEARLY":
                rule = rule with { Frequency = RecurrenceFrequency.Yearly };
                break;
            case "SECONDLY" or "MINUTELY" or "HOURLY":
                problem = NotSupported($"FREQ={freq} is not supported: events repeat at most daily.");
                return null;
            default:
                problem = Invalid($"FREQ={freq} is unknown; use DAILY, WEEKLY, MONTHLY or YEARLY.");
                return null;
        }

        if (parts.TryGetValue("INTERVAL", out var interval))
        {
            if (!TryInt(interval, 1, MaxInterval, out var i))
            {
                problem = Invalid($"INTERVAL must be a whole number from 1 to {MaxInterval}.");
                return null;
            }

            rule = rule with { Interval = i };
        }

        if (parts.ContainsKey("COUNT") && parts.ContainsKey("UNTIL"))
        {
            problem = Invalid("Use COUNT or UNTIL, not both.");
            return null;
        }

        if (parts.TryGetValue("COUNT", out var count))
        {
            if (!TryInt(count, 1, MaxCount, out var c))
            {
                problem = Invalid($"COUNT must be a whole number from 1 to {MaxCount} (leave it out for an open-ended series).");
                return null;
            }

            rule = rule with { Count = c };
        }

        if (parts.TryGetValue("UNTIL", out var until))
        {
            if (_utcPattern.Parse(until) is { Success: true } utc)
            {
                rule = rule with { UntilUtc = utc.Value };
            }
            else if (_localPattern.Parse(until) is { Success: true } local)
            {
                rule = rule with { UntilLocal = local.Value };
            }
            else if (_datePattern.Parse(until) is { Success: true } date)
            {
                rule = rule with { UntilDate = date.Value };
            }
            else
            {
                problem = Invalid("UNTIL must be a date (20261231) or a UTC date-time (20261231T235959Z).");
                return null;
            }
        }

        if (parts.TryGetValue("BYMONTH", out var byMonth))
        {
            if (!TryInts(byMonth, 1, 12, allowNegative: false, out var months))
            {
                problem = Invalid("BYMONTH takes months 1 to 12, e.g. BYMONTH=1,7.");
                return null;
            }

            rule = rule with { ByMonth = months };
        }

        if (parts.TryGetValue("BYMONTHDAY", out var byMonthDay))
        {
            if (!TryInts(byMonthDay, 1, 31, allowNegative: true, out var days))
            {
                problem = Invalid("BYMONTHDAY takes days 1 to 31 or -1 to -31 (from the month's end).");
                return null;
            }

            if (rule.Frequency == RecurrenceFrequency.Weekly)
            {
                problem = Invalid("BYMONTHDAY cannot be used with FREQ=WEEKLY.");
                return null;
            }

            rule = rule with { ByMonthDay = days };
        }

        if (parts.TryGetValue("BYDAY", out var byDay))
        {
            var entries = new SortedSet<WeekdayEntry>(Comparer<WeekdayEntry>.Create((a, b) => a.Day != b.Day ? a.Day.CompareTo(b.Day) : a.Ordinal.CompareTo(b.Ordinal)));
            foreach (var item in byDay.Split(','))
            {
                if (ParseWeekday(item.Trim()) is not { } entry)
                {
                    problem = Invalid($"'{item}' is not a BYDAY value; use MO, TU, WE, TH, FR, SA, SU with an optional ordinal (2MO, -1FR).");
                    return null;
                }

                var maxOrdinal = rule.Frequency == RecurrenceFrequency.Monthly || parts.ContainsKey("BYMONTH") ? 5 : 53;
                if (entry.Ordinal != 0 && rule.Frequency is RecurrenceFrequency.Daily or RecurrenceFrequency.Weekly)
                {
                    problem = Invalid($"BYDAY ordinals ({entry}) only work with FREQ=MONTHLY or FREQ=YEARLY.");
                    return null;
                }

                if (Math.Abs(entry.Ordinal) > maxOrdinal)
                {
                    problem = Invalid($"BYDAY ordinal {entry} is out of range (at most ±{maxOrdinal} here).");
                    return null;
                }

                entries.Add(entry);
            }

            rule = rule with { ByDay = [.. entries] };
        }

        if (parts.TryGetValue("BYSETPOS", out var bySetPos))
        {
            if (!TryInts(bySetPos, 1, 366, allowNegative: true, out var positions))
            {
                problem = Invalid("BYSETPOS takes positions 1 to 366 or -1 to -366.");
                return null;
            }

            if (rule.ByDay.Count == 0 && rule.ByMonthDay.Count == 0 && rule.ByMonth.Count == 0)
            {
                problem = Invalid("BYSETPOS needs BYDAY, BYMONTHDAY or BYMONTH to select from.");
                return null;
            }

            rule = rule with { BySetPos = positions };
        }

        if (parts.TryGetValue("WKST", out var wkst))
        {
            var index = Array.IndexOf(_dayCodes, wkst);
            if (index < 0)
            {
                problem = Invalid("WKST must be one of MO, TU, WE, TH, FR, SA, SU.");
                return null;
            }

            rule = rule with { WeekStart = (IsoDayOfWeek)(index + 1) };
        }

        return rule;
    }

    /// <summary>
    /// Binds <c>UNTIL</c> to a series (RFC 5545 §3.3.10): all-day series take a date (a date-time <c>UNTIL</c> is cut
    /// to its date); timed series take a UTC instant — a date means "through the end of that day" and a floating
    /// local date-time is read in <paramref name="zone"/>. Null with <paramref name="problem"/> when <c>UNTIL</c> is
    /// before the first occurrence (<paramref name="firstStartUtc"/> / <paramref name="firstDate"/>).
    /// </summary>
    public RecurrenceRule? ForSeries(bool allDay, DateTimeZone? zone, Instant firstStartUtc, LocalDate firstDate, out RecurrenceProblem? problem)
    {
        problem = null;
        if (!HasUntil)
        {
            return this;
        }

        RecurrenceRule bound;
        if (allDay)
        {
            var date = UntilDate ?? UntilLocal?.Date ?? UntilUtc!.Value.InUtc().Date;
            bound = this with { UntilDate = date, UntilLocal = null, UntilUtc = null };
            if (date < firstDate)
            {
                problem = Invalid("UNTIL is before the first occurrence.");
                return null;
            }

            return bound;
        }

        ArgumentNullException.ThrowIfNull(zone);
        var utc = UntilUtc
            ?? (UntilLocal is { } local
                ? zone.ResolveLocal(local, NodaTime.TimeZones.Resolvers.LenientResolver).ToInstant()
                : zone.ResolveLocal(UntilDate!.Value.PlusDays(1).AtMidnight(), NodaTime.TimeZones.Resolvers.LenientResolver).ToInstant() - Duration.FromSeconds(1));
        if (utc < firstStartUtc)
        {
            problem = Invalid("UNTIL is before the first occurrence.");
            return null;
        }

        return this with { UntilUtc = utc, UntilDate = null, UntilLocal = null };
    }

    /// <summary>The canonical text: <c>FREQ</c>, <c>INTERVAL</c> (unless 1), <c>COUNT</c>/<c>UNTIL</c>, <c>BYMONTH</c>, <c>BYMONTHDAY</c>, <c>BYDAY</c>, <c>BYSETPOS</c>, <c>WKST</c> (unless MO).</summary>
    public override string ToString()
    {
        var text = new StringBuilder("FREQ=").Append(Frequency.ToString().ToUpperInvariant());
        if (Interval != 1)
        {
            text.Append(";INTERVAL=").Append(Interval.ToString(CultureInfo.InvariantCulture));
        }

        if (Count is { } count)
        {
            text.Append(";COUNT=").Append(count.ToString(CultureInfo.InvariantCulture));
        }
        else if (UntilUtc is { } utc)
        {
            text.Append(";UNTIL=").Append(_utcPattern.Format(utc));
        }
        else if (UntilLocal is { } local)
        {
            text.Append(";UNTIL=").Append(_localPattern.Format(local));
        }
        else if (UntilDate is { } date)
        {
            text.Append(";UNTIL=").Append(_datePattern.Format(date));
        }

        Append(text, "BYMONTH", ByMonth.Select(m => m.ToString(CultureInfo.InvariantCulture)));
        Append(text, "BYMONTHDAY", ByMonthDay.Select(d => d.ToString(CultureInfo.InvariantCulture)));
        Append(text, "BYDAY", ByDay.Select(d => d.ToString()));
        Append(text, "BYSETPOS", BySetPos.Select(p => p.ToString(CultureInfo.InvariantCulture)));
        if (WeekStart != IsoDayOfWeek.Monday)
        {
            text.Append(";WKST=").Append(DayCode(WeekStart));
        }

        return text.ToString();
    }

    /// <summary>The two-letter iCalendar code of a weekday (<c>MO</c> … <c>SU</c>).</summary>
    public static string DayCode(IsoDayOfWeek day) => _dayCodes[(int)day - 1];

    private static void Append(StringBuilder text, string name, IEnumerable<string> values)
    {
        var joined = string.Join(',', values);
        if (joined.Length > 0)
        {
            text.Append(';').Append(name).Append('=').Append(joined);
        }
    }

    private static WeekdayEntry? ParseWeekday(string value)
    {
        if (value.Length < 2)
        {
            return null;
        }

        var index = Array.IndexOf(_dayCodes, value[^2..]);
        var ordinalText = value[..^2];
        if (index < 0)
        {
            return null;
        }

        if (ordinalText.Length == 0)
        {
            return new WeekdayEntry(0, (IsoDayOfWeek)(index + 1));
        }

        return int.TryParse(ordinalText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var ordinal) && ordinal != 0
            ? new WeekdayEntry(ordinal, (IsoDayOfWeek)(index + 1))
            : null;
    }

    private static bool TryInt(string value, int min, int max, out int result) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result) && result >= min && result <= max;

    private static bool TryInts(string value, int min, int max, bool allowNegative, out IReadOnlyList<int> result)
    {
        var values = new SortedSet<int>();
        result = [];
        foreach (var item in value.Split(','))
        {
            if (!int.TryParse(item.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)
                || n == 0
                || Math.Abs(n) < min
                || Math.Abs(n) > max
                || (n < 0 && !allowNegative))
            {
                return false;
            }

            values.Add(n);
        }

        result = [.. values];
        return true;
    }

    private static RecurrenceProblem Invalid(string message) => new(false, "recurrence.rrule", message);

    private static RecurrenceProblem NotSupported(string message) => new(true, "recurrence.rrule", message);
}
