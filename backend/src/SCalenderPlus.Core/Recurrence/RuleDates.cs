using NodaTime;
using SCalenderPlus.Core.Events;

namespace SCalenderPlus.Core.Recurrence;

/// <summary>
/// The dates a <see cref="RecurrenceRule"/> generates from a first date (RFC 5545 §3.3.10), ascending. Every
/// supported part works on whole days (no sub-daily parts), so a rule is a sequence of dates; the series adds the
/// first occurrence's time of day. The first date always counts as the first occurrence (§3.8.5.3), even when it
/// does not match the rule. Each period (year, month, week from <c>WKST</c>, day — every <c>INTERVAL</c>-th) yields
/// its days that pass <c>BYMONTH</c>, <c>BYMONTHDAY</c> and <c>BYDAY</c> (ordinals relative to the month, or to the
/// year for yearly rules without <c>BYMONTH</c>; parts the rule leaves out default to the first date's month, day
/// or weekday as in the RFC), then <c>BYSETPOS</c> picks from them. Invalid dates (30 February) are skipped.
/// </summary>
internal static class RuleDates
{
    /// <summary>
    /// Days examined per enumeration at most: rules that (almost) never match — e.g. 29 February every 7th day —
    /// stop there instead of scanning to year 9998. A normal rule examines ≈ 1–31 days per occurrence.
    /// </summary>
    public const int MaxExaminedDays = 1_000_000;

    /// <summary>
    /// The rule's dates from <paramref name="first"/> on (<paramref name="first"/> itself first). With
    /// <paramref name="scanFrom"/>, periods ending before it are skipped arithmetically (only for rules without
    /// <c>COUNT</c>, whose position in the sequence does not matter); dates before it may still be returned.
    /// <c>COUNT</c> and <c>UNTIL</c> are applied by the caller (UNTIL compares instants). Periods starting after
    /// <paramref name="scanTo"/> are not examined (so rules that rarely match stop at the end of a window).
    /// </summary>
    /// <param name="budget">Shared by the expansions of one request: the enumeration also stops when it is used up.</param>
    public static IEnumerable<LocalDate> Enumerate(RecurrenceRule rule, LocalDate first, LocalDate? scanFrom = null, LocalDate? scanTo = null, ExpansionBudget? budget = null)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var period = scanFrom is { } target && target > first ? FirstPeriod(rule, first, target) : 0;
        if (period == 0)
        {
            yield return first;
        }

        var filter = new Filter(rule, first);
        var examined = 0;
        var candidates = new List<LocalDate>(31);
        for (; ; period++)
        {
            if (!PeriodStart(rule, first, period, out var start) || start > scanTo)
            {
                yield break;
            }

            candidates.Clear();
            var days = filter.Collect(start, candidates);
            examined += days;
            if (budget is not null && !budget.Spend(days))
            {
                yield break;
            }

            foreach (var date in filter.SetPositions(candidates))
            {
                if (date > first)
                {
                    yield return date;
                }
            }

            if (examined > MaxExaminedDays)
            {
                yield break;
            }
        }
    }

    /// <summary>The index of the period that contains <paramref name="target"/> (or the last one starting before it).</summary>
    private static long FirstPeriod(RecurrenceRule rule, LocalDate first, LocalDate target)
    {
        long units = rule.Frequency switch
        {
            RecurrenceFrequency.Yearly => target.Year - first.Year,
            RecurrenceFrequency.Monthly => ((target.Year - first.Year) * 12L) + target.Month - first.Month,
            RecurrenceFrequency.Weekly => Period.Between(WeekStart(first, rule.WeekStart), WeekStart(target, rule.WeekStart), PeriodUnits.Days).Days / 7,
            _ => Period.Between(first, target, PeriodUnits.Days).Days,
        };
        return Math.Max(0, units / rule.Interval);
    }

    /// <summary>The first day of period <paramref name="index"/>; false beyond <see cref="EventTimes.MaxDate"/>.</summary>
    private static bool PeriodStart(RecurrenceRule rule, LocalDate first, long index, out LocalDate start)
    {
        var steps = index * rule.Interval;
        start = default;
        switch (rule.Frequency)
        {
            case RecurrenceFrequency.Yearly:
                var year = first.Year + steps;
                if (year > EventTimes.MaxDate.Year)
                {
                    return false;
                }

                start = new LocalDate((int)year, 1, 1);
                return true;
            case RecurrenceFrequency.Monthly:
                var months = ((first.Year * 12L) + first.Month - 1) + steps;
                if (months / 12 > EventTimes.MaxDate.Year)
                {
                    return false;
                }

                start = new LocalDate((int)(months / 12), (int)(months % 12) + 1, 1);
                return true;
            default:
                var days = rule.Frequency == RecurrenceFrequency.Weekly ? steps * 7 : steps;
                var origin = rule.Frequency == RecurrenceFrequency.Weekly ? WeekStart(first, rule.WeekStart) : first;
                if (days > Period.Between(origin, EventTimes.MaxDate, PeriodUnits.Days).Days)
                {
                    return false;
                }

                start = origin.PlusDays((int)days);
                return true;
        }
    }

    private static LocalDate WeekStart(LocalDate date, IsoDayOfWeek weekStart) =>
        date.PlusDays(-(((int)date.DayOfWeek - (int)weekStart + 7) % 7));

    /// <summary>The day filters of a rule, with the RFC defaults taken from the first date.</summary>
    private sealed class Filter
    {
        private readonly RecurrenceRule _rule;
        private readonly bool[] _months = new bool[13];
        private readonly bool _anyMonth;
        private readonly int[] _monthDays;
        private readonly WeekdayEntry[] _days;
        private readonly bool _ordinalsInYear;

        public Filter(RecurrenceRule rule, LocalDate first)
        {
            _rule = rule;
            var noDayParts = rule.ByMonthDay.Count == 0 && rule.ByDay.Count == 0;
            IReadOnlyList<int> months = rule.ByMonth.Count > 0
                ? rule.ByMonth
                : rule.Frequency == RecurrenceFrequency.Yearly && noDayParts ? [first.Month] : [];
            foreach (var month in months)
            {
                _months[month] = true;
            }

            _anyMonth = months.Count == 0;
            _monthDays = noDayParts && rule.Frequency is RecurrenceFrequency.Yearly or RecurrenceFrequency.Monthly
                ? [first.Day]
                : [.. rule.ByMonthDay];
            _days = noDayParts && rule.Frequency == RecurrenceFrequency.Weekly
                ? [new WeekdayEntry(0, first.DayOfWeek)]
                : [.. rule.ByDay];
            _ordinalsInYear = rule.Frequency == RecurrenceFrequency.Yearly && rule.ByMonth.Count == 0;
        }

        /// <summary>Adds the matching days of the period starting at <paramref name="start"/>; returns how many days were examined.</summary>
        public int Collect(LocalDate start, List<LocalDate> into)
        {
            switch (_rule.Frequency)
            {
                case RecurrenceFrequency.Yearly:
                    var examined = 0;
                    for (var month = 1; month <= 12; month++)
                    {
                        if (_anyMonth || _months[month])
                        {
                            examined += CollectMonth(start.Year, month, into);
                        }
                    }

                    return examined + 1;
                case RecurrenceFrequency.Monthly:
                    return _anyMonth || _months[start.Month] ? CollectMonth(start.Year, start.Month, into) : 1;
                case RecurrenceFrequency.Weekly:
                    for (var i = 0; i < 7; i++)
                    {
                        var day = start.PlusDays(i);
                        if (day > EventTimes.MaxDate)
                        {
                            break;
                        }

                        if (Matches(day))
                        {
                            into.Add(day);
                        }
                    }

                    return 7;
                default:
                    if (Matches(start))
                    {
                        into.Add(start);
                    }

                    return 1;
            }
        }

        /// <summary><c>BYSETPOS</c> over the period's (sorted) days; all of them without it.</summary>
        public IEnumerable<LocalDate> SetPositions(List<LocalDate> candidates)
        {
            if (_rule.BySetPos.Count == 0)
            {
                return candidates;
            }

            var picked = new SortedSet<LocalDate>();
            foreach (var position in _rule.BySetPos)
            {
                var index = position > 0 ? position - 1 : candidates.Count + position;
                if (index >= 0 && index < candidates.Count)
                {
                    picked.Add(candidates[index]);
                }
            }

            return picked;
        }

        private int CollectMonth(int year, int month, List<LocalDate> into)
        {
            var length = CalendarSystem.Iso.GetDaysInMonth(year, month);
            for (var day = 1; day <= length; day++)
            {
                var date = new LocalDate(year, month, day);
                if (Matches(date))
                {
                    into.Add(date);
                }
            }

            return length;
        }

        private bool Matches(LocalDate date)
        {
            if (!_anyMonth && !_months[date.Month])
            {
                return false;
            }

            if (_monthDays.Length > 0)
            {
                var length = CalendarSystem.Iso.GetDaysInMonth(date.Year, date.Month);
                var found = false;
                foreach (var day in _monthDays)
                {
                    if (day == date.Day || (day < 0 && length + day + 1 == date.Day))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return false;
                }
            }

            if (_days.Length == 0)
            {
                return true;
            }

            foreach (var entry in _days)
            {
                if (entry.Day == date.DayOfWeek && (entry.Ordinal == 0 || MatchesOrdinal(date, entry.Ordinal)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The date is the <paramref name="ordinal"/>-th of its weekday in its month (or year), counted from the end when negative.</summary>
        private bool MatchesOrdinal(LocalDate date, int ordinal)
        {
            int position, length;
            if (_ordinalsInYear)
            {
                position = date.DayOfYear;
                length = CalendarSystem.Iso.GetDaysInYear(date.Year);
            }
            else
            {
                position = date.Day;
                length = CalendarSystem.Iso.GetDaysInMonth(date.Year, date.Month);
            }

            return ordinal > 0
                ? ((position - 1) / 7) + 1 == ordinal
                : -(((length - position) / 7) + 1) == ordinal;
        }
    }
}
