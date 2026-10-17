using System.Globalization;
using NodaTime;
using NodaTime.TimeZones;

namespace SCalenderPlus.Application.Feeds;

/// <summary>
/// <c>VTIMEZONE</c> components (RFC 5545 §3.6.5) generated from the bundled tzdb (NodaTime) for every TZID a feed
/// uses (ical-caldav.md §1.2). Clients that know the IANA id use their own database; the component is the fallback
/// and must agree with ours:
/// <list type="bullet">
/// <item><b>History</b>: every transition from the start of the zone interval containing the earliest emitted time
/// up to the rule period, one observance per transition (<c>DTSTART</c> only: ical.js reads only the first value
/// of an <c>RDATE</c> list and ignores <c>DTSTART</c> next to <c>RDATE</c>, so lists are not interoperable).</item>
/// <item><b>Rules</b>: from the year before <c>now</c>, when the zone has the same two yearly transitions for
/// <see cref="RuleCheckYears"/> years (same month, weekday — n-th or last — wall time and offsets), one
/// observance per transition with <c>RRULE:FREQ=YEARLY;BYMONTH=…;BYDAY=…</c> (valid for infinite series); otherwise
/// the explicit transitions of the next <see cref="ExplicitYears"/> years.</item>
/// <item>A zone without transitions in that span (UTC, fixed offsets) gets a single <c>STANDARD</c> observance.</item>
/// </list>
/// Onsets are local times in the offset before the transition, as RFC 5545 requires.
/// </summary>
public static class VTimeZoneBuilder
{
    /// <summary>Years whose transitions must agree before they are written as a yearly rule.</summary>
    public const int RuleCheckYears = 4;

    /// <summary>Years of explicit transitions for zones whose rule is irregular (e.g. tied to a lunar calendar).</summary>
    public const int ExplicitYears = 12;

    private static readonly string[] _weekdays = ["", "MO", "TU", "WE", "TH", "FR", "SA", "SU"];

    /// <summary>Writes the <c>VTIMEZONE</c> of <paramref name="zone"/> covering times from <paramref name="earliest"/> on.</summary>
    public static void Write(IcsWriter writer, DateTimeZone zone, Instant earliest, Instant now)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(zone);

        var ruleStartYear = now.InUtc().Year - 1;
        var ruleStart = Instant.FromUtc(ruleStartYear, 1, 1, 0, 0);
        var floor = Instant.FromUtc(1970, 1, 1, 0, 0);
        earliest = earliest < floor ? floor : earliest;

        var containing = zone.GetZoneInterval(earliest < ruleStart ? earliest : ruleStart);
        var historyStart = containing.HasStart ? containing.Start : earliest;
        var history = Transitions(zone, historyStart, ruleStart);
        var rule = YearlyRule(zone, ruleStartYear);
        var explicitTransitions = rule is null
            ? Transitions(zone, ruleStart, Instant.FromUtc(ruleStartYear + ExplicitYears, 1, 1, 0, 0))
            : [];

        writer.Begin("VTIMEZONE").Raw("TZID", zone.Id);
        var observances = new List<Observance>();
        observances.AddRange(Single(history.Concat(explicitTransitions)));
        if (rule is not null)
        {
            observances.AddRange(rule);
        }

        if (observances.Count == 0)
        {
            // No transition at all (UTC, fixed offsets, or a zone that stopped changing long before the period).
            var interval = zone.GetZoneInterval(earliest);
            observances.Add(new Observance(
                false,
                new LocalDateTime(1970, 1, 1, 0, 0),
                interval.WallOffset,
                interval.WallOffset,
                interval.Name,
                null));
        }

        foreach (var observance in observances.OrderBy(o => o.Start))
        {
            var component = observance.Daylight ? "DAYLIGHT" : "STANDARD";
            writer.Begin(component).Raw("DTSTART", IcsWriter.FormatLocal(observance.Start));
            if (observance.Rule is { } yearly)
            {
                writer.Raw("RRULE", yearly);
            }

            writer.Raw("TZOFFSETFROM", IcsWriter.FormatOffset(observance.From))
                .Raw("TZOFFSETTO", IcsWriter.FormatOffset(observance.To));
            if (!string.IsNullOrEmpty(observance.Name))
            {
                writer.Text("TZNAME", observance.Name);
            }

            writer.End(component);
        }

        writer.End("VTIMEZONE");
    }

    /// <summary>The transitions of <paramref name="zone"/> in <c>[from, to)</c>.</summary>
    private static List<Transition> Transitions(DateTimeZone zone, Instant from, Instant to)
    {
        var transitions = new List<Transition>();
        if (from >= to)
        {
            return transitions;
        }

        ZoneInterval? previous = null;
        foreach (var interval in zone.GetZoneIntervals(new Interval(from, to)))
        {
            if (interval.HasStart && interval.Start >= from)
            {
                var before = previous ?? zone.GetZoneInterval(interval.Start - Duration.Epsilon);
                if (before.WallOffset != interval.WallOffset || before.Name != interval.Name)
                {
                    transitions.Add(new Transition(interval.Start, before.WallOffset, interval));
                }
            }

            previous = interval;
        }

        return transitions;
    }

    /// <summary>History and irregular transitions: one observance each, at its onset.</summary>
    private static IEnumerable<Observance> Single(IEnumerable<Transition> transitions) =>
        transitions.Select(t => new Observance(t.Daylight, t.Onset, t.From, t.Interval.WallOffset, t.Interval.Name, null));

    /// <summary>
    /// Yearly rules from <paramref name="firstYear"/> on when the zone has the same two transitions in each of
    /// <see cref="RuleCheckYears"/> years; null otherwise (also for zones without daylight saving time).
    /// </summary>
    private static List<Observance>? YearlyRule(DateTimeZone zone, int firstYear)
    {
        var years = Enumerable.Range(firstYear, RuleCheckYears)
            .Select(y => Transitions(zone, Instant.FromUtc(y, 1, 1, 0, 0), Instant.FromUtc(y + 1, 1, 1, 0, 0)))
            .ToList();
        if (years.Any(y => y.Count != 2))
        {
            return null;
        }

        var rules = new List<Observance>(2);
        for (var k = 0; k < 2; k++)
        {
            var samples = years.Select(y => y[k]).ToList();
            var first = samples[0];
            var shapes = samples.Select(t => (t.Onset.Month, t.Onset.DayOfWeek, t.Onset.TimeOfDay, t.From, t.Interval.WallOffset, t.Interval.Name, t.Daylight)).Distinct().Count();
            if (shapes != 1)
            {
                return null;
            }

            string byDay;
            if (samples.All(t => IsLastOfMonth(t.Onset.Date)))
            {
                byDay = "-1" + _weekdays[(int)first.Onset.DayOfWeek];
            }
            else if (samples.Select(t => (t.Onset.Day - 1) / 7 + 1).Distinct().ToList() is [var nth] && nth <= 4)
            {
                byDay = nth.ToString(CultureInfo.InvariantCulture) + _weekdays[(int)first.Onset.DayOfWeek];
            }
            else
            {
                return null;
            }

            var rule = string.Create(CultureInfo.InvariantCulture, $"FREQ=YEARLY;BYMONTH={first.Onset.Month};BYDAY={byDay}");
            rules.Add(new Observance(first.Daylight, first.Onset, first.From, first.Interval.WallOffset, first.Interval.Name, rule));
        }

        return rules;
    }

    private static bool IsLastOfMonth(LocalDate date) => date.Day + 7 > CalendarSystem.Iso.GetDaysInMonth(date.Year, date.Month);

    /// <summary>A change of offset at <see cref="At"/>, from <see cref="From"/> into <see cref="Interval"/>.</summary>
    private sealed record Transition(Instant At, Offset From, ZoneInterval Interval)
    {
        /// <summary>The wall clock at which the change happens, in the offset before it.</summary>
        public LocalDateTime Onset => At.WithOffset(From).LocalDateTime;

        public bool Daylight => Interval.Savings != Offset.Zero;
    }

    private sealed record Observance(bool Daylight, LocalDateTime Start, Offset From, Offset To, string Name, string? Rule);
}
