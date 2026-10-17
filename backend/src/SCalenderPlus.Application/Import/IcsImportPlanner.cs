using System.Security.Cryptography;
using System.Text;
using NodaTime;
using NodaTime.Text;
using SCalenderPlus.Core.Events;
using SCalenderPlus.Core.Recurrence;

namespace SCalenderPlus.Application.Import;

/// <summary>
/// Turns the components of an uploaded file into prepared events (ical-caldav.md §2, data-model.md §9/§10): one item
/// per UID — a single event, or a series master with its <c>RRULE</c>, <c>RDATE</c>s/<c>EXDATE</c>s and the
/// <c>RECURRENCE-ID</c> overrides of the same UID as exceptions (cancelled ones as cancelled exceptions). Times:
/// <c>TZID</c>s map to IANA zones (<see cref="IcsTimeZones"/>); floating times and unknown zones are read in the
/// file's <c>X-WR-TIMEZONE</c> or else the calendar's zone (warned); UTC times of single events are stored in that
/// zone too (the instant is the same), UTC series keep <c>UTC</c> so their occurrences stay at the same instant
/// (RFC 5545 semantics of a UTC <c>DTSTART</c>). Values beyond the event limits are truncated (warned), never
/// rejected; what the model cannot represent (unsupported RRULE parts, a second RRULE, unreadable times) skips the
/// event with a reason. Imports never set permission overrides (llm-import.md §9): imported events get the
/// calendar's default access. Pure: no database.
/// </summary>
public static class IcsImportPlanner
{
    /// <summary>Title of events without <c>SUMMARY</c> (the data is not localized).</summary>
    public const string UntitledTitle = "(No title)";

    private static readonly LocalDateTimePattern[] _dateTimePatterns =
    [
        LocalDateTimePattern.CreateWithInvariantCulture("uuuuMMdd'T'HHmmss"),
        LocalDateTimePattern.CreateWithInvariantCulture("uuuuMMdd'T'HHmm"),
    ];

    private static readonly string[] _seedProperties = ["DTSTART", "DTEND", "DURATION", "SUMMARY", "RRULE"];

    private static readonly LocalDatePattern _datePattern = LocalDatePattern.CreateWithInvariantCulture("uuuuMMdd");

    public static IcsImportPlan Plan(IReadOnlyList<IcsComponent> calendars, DateTimeZone calendarZone, Guid calendarId, Guid creatorId, Instant now)
    {
        ArgumentNullException.ThrowIfNull(calendars);
        ArgumentNullException.ThrowIfNull(calendarZone);
        var items = new List<IcsImportItem>();
        var warnings = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var calendar in calendars)
        {
            var context = new Context(calendar, calendarZone, calendarId, creatorId, now);
            if (calendar.Components.Any(c => c.Name is "VTODO" or "VJOURNAL"))
            {
                warnings.Add(IcsImportCodes.UnsupportedComponents);
            }

            // Group by UID in file order: masters (no RECURRENCE-ID) and their overrides.
            var groups = new List<(string Key, List<IcsComponent> Masters, List<IcsComponent> Overrides)>();
            var byKey = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var vevent in calendar.Components.Where(c => c.Name == "VEVENT"))
            {
                var key = Text(vevent, "UID") ?? "\0" + Seed(vevent); // no UID: each event on its own
                if (!byKey.TryGetValue(key, out var index))
                {
                    byKey[key] = index = groups.Count;
                    groups.Add((key, [], []));
                }

                (vevent.First("RECURRENCE-ID") is null ? groups[index].Masters : groups[index].Overrides).Add(vevent);
            }

            foreach (var (_, masters, overrides) in groups)
            {
                if (masters.Count == 0)
                {
                    items.AddRange(overrides.Select(o => Guarded(o, context, () => Orphan(o, context))));
                    continue;
                }

                var item = Guarded(masters[0], context, () =>
                {
                    var built = Build(masters[0], context, series: null);
                    if (built.Event is { } ev)
                    {
                        ApplyOverrides(ev, built, overrides, context);
                    }

                    return built;
                });
                items.Add(item);
                items.AddRange(masters.Skip(1).Select(m => Guarded(m, context, () => Duplicate(m, context))));
            }
        }

        // The same UID in two VCALENDARs of one file (or a generated UID colliding): only the first is imported.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items.Where(i => i.Action == IcsImportAction.Create))
        {
            if (!seen.Add(item.Uid))
            {
                item.Action = IcsImportAction.Duplicate;
                item.Reason = IcsImportCodes.DuplicateInFile;
            }
        }

        return new IcsImportPlan(items, [.. warnings]);
    }

    /// <summary>
    /// Runs <paramref name="build"/>; values at the edges of the supported ranges that still overflow somewhere in the
    /// date arithmetic (hostile or broken files) skip the event instead of failing the upload.
    /// </summary>
    private static IcsImportItem Guarded(IcsComponent vevent, Context context, Func<IcsImportItem> build)
    {
        try
        {
            return build();
        }
        catch (Exception ex) when (ex is OverflowException or ArgumentException or InvalidOperationException)
        {
            var ignored = new SortedSet<string>(StringComparer.Ordinal);
            return new IcsImportItem { Uid = Uid(vevent, null, ignored), Title = Title(vevent, ignored), Action = IcsImportAction.Skip, Reason = IcsImportCodes.TimeInvalid };
        }
    }

    /// <summary>A single event or series master (or, with <paramref name="series"/>, an orphaned override imported on its own).</summary>
    private static IcsImportItem Build(IcsComponent vevent, Context context, string? series)
    {
        var warnings = new SortedSet<string>(StringComparer.Ordinal);
        var uid = Uid(vevent, series, warnings);
        var title = Title(vevent, warnings);
        IcsImportItem Skip(string reason, EventTimes? times = null)
        {
            var item = new IcsImportItem { Uid = uid, Title = title, Times = times, Action = IcsImportAction.Skip, Reason = reason };
            return item;
        }

        if (string.Equals(Text(vevent, "STATUS"), "CANCELLED", StringComparison.OrdinalIgnoreCase))
        {
            return Skip(IcsImportCodes.Cancelled);
        }

        var startProperty = vevent.First("DTSTART");
        if (startProperty is null)
        {
            return Skip(IcsImportCodes.StartMissing);
        }

        var rrules = series is null ? vevent.All("RRULE").ToList() : [];
        var times = Times(vevent, context, zone: null, isSeries: rrules.Count > 0, warnings);
        if (times is null)
        {
            return Skip(IcsImportCodes.TimeInvalid);
        }

        var ev = new Event
        {
            Id = Guid.CreateVersion7(),
            CalendarId = context.CalendarId,
            CreatorUserId = context.CreatorId,
            Uid = uid,
            Title = title,
            CreatedAt = context.Now,
            UpdatedAt = context.Now,
        };
        ev.SetTimes(times);
        ApplyDetails(ev, vevent, warnings);

        if (rrules.Count > 1)
        {
            return Skip(IcsImportCodes.RecurrenceNotSupported, times);
        }

        if (rrules.Count == 1)
        {
            var rule = RecurrenceRule.Parse(rrules[0].Value, out var problem);
            var zone = ev.AllDay ? null : DateTimeZoneProviders.Tzdb[ev.TimeZone!];
            var firstDate = ev.AllDay ? ev.StartDate!.Value : ev.StartLocal!.Value.Date;
            var bound = rule?.ForSeries(ev.AllDay, zone, ev.StartUtc, firstDate, out problem);
            if (bound is null)
            {
                return Skip(problem!.NotSupported ? IcsImportCodes.RecurrenceNotSupported : IcsImportCodes.RecurrenceInvalid, times);
            }

            ev.Rrule = bound.ToString();
            ev.ExDates = Dates(vevent, "EXDATE", ev, context, RecurrenceSet.MaxExDates, warnings);
            var first = FirstKey(ev);
            ev.RDates = [.. Dates(vevent, "RDATE", ev, context, RecurrenceSet.MaxRDates, warnings).Where(r => r >= first)];
            ev.RefreshSeriesBounds();
        }
        else if (vevent.First("RDATE") is not null)
        {
            warnings.Add(IcsImportCodes.OccurrenceIgnored); // RDATE without RRULE: only the first occurrence
        }

        var created = new IcsImportItem
        {
            Uid = uid,
            Title = title,
            Times = times,
            Rrule = ev.Rrule,
            Action = IcsImportAction.Create,
            Event = ev,
            CancelledOccurrences = ev.ExDates.Count,
        };
        created.Warnings.UnionWith(warnings);
        return created;
    }

    private static IcsImportItem Duplicate(IcsComponent vevent, Context context)
    {
        var ignored = new SortedSet<string>(StringComparer.Ordinal);
        return new IcsImportItem
        {
            Uid = Uid(vevent, null, ignored),
            Title = Title(vevent, ignored),
            Times = vevent.First("DTSTART") is null ? null : Times(vevent, context, null, false, ignored),
            Action = IcsImportAction.Duplicate,
            Reason = IcsImportCodes.DuplicateInFile,
        };
    }

    /// <summary>An override whose series is not in the file: imported as a single event with a UID of its own.</summary>
    private static IcsImportItem Orphan(IcsComponent vevent, Context context)
    {
        var seed = (Text(vevent, "UID") ?? Seed(vevent)) + "|" + vevent.First("RECURRENCE-ID")!.Value;
        var item = Build(vevent, context, series: seed);
        item.Warnings.Add(IcsImportCodes.OrphanOccurrence);
        return item;
    }

    /// <summary>RECURRENCE-ID overrides of <paramref name="series"/> as exceptions; a single event ignores them (warned).</summary>
    private static void ApplyOverrides(Event series, IcsImportItem item, List<IcsComponent> overrides, Context context)
    {
        if (overrides.Count == 0)
        {
            return;
        }

        if (series.Recurrence() is not { } set)
        {
            item.Warnings.Add(IcsImportCodes.OccurrenceIgnored);
            return;
        }

        foreach (var vevent in overrides)
        {
            var property = vevent.First("RECURRENCE-ID")!;
            if (string.Equals(property.Param("RANGE"), "THISANDFUTURE", StringComparison.OrdinalIgnoreCase)
                || ReadTime(property) is not { } recurrenceId)
            {
                item.Warnings.Add(IcsImportCodes.OccurrenceIgnored);
                continue;
            }

            var key = Key(recurrenceId, series, context, item.Warnings);
            var cancelled = string.Equals(Text(vevent, "STATUS"), "CANCELLED", StringComparison.OrdinalIgnoreCase);
            if (series.Exceptions.Any(x => x.RecurrenceId == key))
            {
                item.Warnings.Add(IcsImportCodes.OccurrenceIgnored); // the same occurrence twice
                continue;
            }

            if (set.Find(key) is not { } original)
            {
                if (!(cancelled && series.ExDates.Contains(key)))
                {
                    item.Warnings.Add(IcsImportCodes.OccurrenceNotFound);
                }

                continue;
            }

            var exception = new EventExceptionEntry
            {
                Id = Guid.CreateVersion7(),
                EventId = series.Id,
                RecurrenceId = key,
                CreatedAt = context.Now,
                UpdatedAt = context.Now,
            };
            if (cancelled)
            {
                exception.Cancelled = true;
                item.CancelledOccurrences++;
            }
            else
            {
                // An override is a complete VEVENT: a missing DESCRIPTION/LOCATION means "none for this occurrence".
                var ignored = new SortedSet<string>(StringComparer.Ordinal);
                exception.Title = Text(vevent, "SUMMARY") is null ? series.Title : Title(vevent, item.Warnings);
                exception.Description = Limited(Text(vevent, "DESCRIPTION", multiline: true), Event.DescriptionMaxLength, item.Warnings) ?? string.Empty;
                exception.Location = Limited(Text(vevent, "LOCATION"), Event.LocationMaxLength, item.Warnings) ?? string.Empty;
                exception.Status = Status(vevent) ?? EventStatus.Confirmed;
                exception.Transparency = Transparency(vevent);
                if (vevent.First("DTSTART") is { } start && ReadTime(start) is { } startTime)
                {
                    if (startTime.IsDate != series.AllDay)
                    {
                        item.Warnings.Add(IcsImportCodes.OccurrenceIgnored); // timed ↔ all-day: keep the original times
                    }
                    else if (Times(vevent, context, series.AllDay ? null : DateTimeZoneProviders.Tzdb[series.TimeZone!], false, ignored) is { } times
                        && !SameTimes(times, original.Times))
                    {
                        exception.Move(times);
                    }
                }

                exception.InheritEqualValues(series);
                if (exception.IsEmpty)
                {
                    continue;
                }

                item.ModifiedOccurrences++;
            }

            series.Exceptions.Add(exception);
        }

        series.RefreshSeriesBounds();
    }

    private static bool SameTimes(EventTimes a, EventTimes b) =>
        a.AllDay ? a.StartDate == b.StartDate && a.EndDate == b.EndDate : a.StartUtc == b.StartUtc && a.EndUtc == b.EndUtc;

    /// <summary>
    /// The event's times: all-day from <c>VALUE=DATE</c> (end exclusive; default one day), timed in the zone of
    /// <c>DTSTART</c> (or <paramref name="zone"/>, for overrides: the series'); end from <c>DTEND</c> or
    /// <c>DURATION</c> (default: zero length). Null when unreadable or out of range.
    /// </summary>
    private static EventTimes? Times(IcsComponent vevent, Context context, DateTimeZone? zone, bool isSeries, SortedSet<string> warnings)
    {
        if (ReadTime(vevent.First("DTSTART")) is not { } start)
        {
            return null;
        }

        var endProperty = vevent.First("DTEND");
        var end = ReadTime(endProperty);
        var duration = vevent.First("DURATION") is { } d ? ParseDuration(d.Value) : null;
        if ((endProperty is not null && end is null) || (vevent.First("DURATION") is not null && endProperty is null && duration is null))
        {
            return null;
        }

        if (start.IsDate)
        {
            if (!EventTimes.IsSupported(start.Date) || (end is { } endValue && !EventTimes.IsSupported(endValue.Date)))
            {
                return null;
            }

            var endDate = end is { } e ? e.Date : duration is { } p ? start.Date.PlusDays(Math.Max(1, WholeDays(p))) : start.Date.PlusDays(1);
            if (endDate <= start.Date)
            {
                if (end is not null || duration is not null)
                {
                    warnings.Add(IcsImportCodes.EndBeforeStart);
                }

                endDate = start.Date.PlusDays(1);
            }

            return EventTimes.IsSupported(start.Date) && EventTimes.IsSupported(endDate) ? EventTimes.AllDayEvent(start.Date, endDate) : null;
        }

        DateTimeZone eventZone;
        LocalDateTime startLocal;
        if (zone is not null)
        {
            eventZone = zone;
            startLocal = Local(start, zone, context, warnings);
        }
        else
        {
            (eventZone, startLocal) = Place(start, isSeries, context, warnings);
        }

        var endLocal = end is { } endTime
            ? endTime.IsDate ? endTime.Date.AtMidnight() : Local(endTime, eventZone, context, warnings)
            : duration is { } period ? startLocal.Plus(period) : startLocal;
        if (!EventTimes.IsSupported(startLocal.Date) || !EventTimes.IsSupported(endLocal.Date))
        {
            return null;
        }

        var times = EventTimes.Timed(startLocal, endLocal, eventZone);
        if (times is null)
        {
            warnings.Add(IcsImportCodes.EndBeforeStart);
            times = EventTimes.Timed(startLocal, startLocal, eventZone)!;
        }

        if (times.Adjustments.Count > 0)
        {
            warnings.Add(IcsImportCodes.TimeAdjusted);
        }

        return times;
    }

    /// <summary>The zone and wall clock of a <c>DTSTART</c>.</summary>
    private static (DateTimeZone Zone, LocalDateTime Local) Place(IcsTime time, bool isSeries, Context context, SortedSet<string> warnings)
    {
        if (time.Utc)
        {
            var zone = isSeries ? DateTimeZone.Utc : context.Fallback;
            return (zone, time.Local.InUtc().ToInstant().InZone(zone).LocalDateTime);
        }

        if (time.TzId is not null)
        {
            if (context.Zone(time.TzId) is { } zone)
            {
                return (zone, time.Local);
            }

            warnings.Add(IcsImportCodes.TimeZoneUnknown);
            return (context.Fallback, time.Local);
        }

        warnings.Add(IcsImportCodes.FloatingTime);
        return (context.Fallback, time.Local);
    }

    /// <summary>The wall clock in <paramref name="zone"/> of a date-time (converted through its instant when it has another zone).</summary>
    private static LocalDateTime Local(IcsTime time, DateTimeZone zone, Context context, SortedSet<string> warnings)
    {
        if (time.Utc)
        {
            return time.Local.InUtc().ToInstant().InZone(zone).LocalDateTime;
        }

        if (time.TzId is not null)
        {
            var own = context.Zone(time.TzId);
            if (own is null)
            {
                warnings.Add(IcsImportCodes.TimeZoneUnknown);
            }
            else if (own.Id != zone.Id)
            {
                return own.AtLeniently(time.Local).ToInstant().InZone(zone).LocalDateTime;
            }
        }

        return time.Local;
    }

    /// <summary>A recurrence id, EXDATE or RDATE as a key of <paramref name="series"/>: wall clock in its zone (all-day: the date at midnight).</summary>
    private static LocalDateTime Key(IcsTime time, Event series, Context context, SortedSet<string> warnings)
    {
        if (series.AllDay)
        {
            return (time.IsDate ? time.Date : time.Local.Date).AtMidnight();
        }

        return time.IsDate
            ? time.Date.At(series.StartLocal!.Value.TimeOfDay)
            : Local(time, DateTimeZoneProviders.Tzdb[series.TimeZone!], context, warnings);
    }

    private static LocalDateTime FirstKey(Event series) => series.AllDay ? series.StartDate!.Value.AtMidnight() : series.StartLocal!.Value;

    private static List<LocalDateTime> Dates(IcsComponent vevent, string name, Event series, Context context, int max, SortedSet<string> warnings)
    {
        var values = new SortedSet<LocalDateTime>();
        foreach (var property in vevent.All(name))
        {
            if (string.Equals(property.Param("VALUE"), "PERIOD", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add(IcsImportCodes.RDatePeriodIgnored);
                continue;
            }

            foreach (var value in IcsReader.SplitList(property.Value))
            {
                if (ReadTime(property with { Value = value.Trim() }) is { } time && EventTimes.IsSupported(time.Date))
                {
                    values.Add(Key(time, series, context, warnings));
                }
            }
        }

        if (values.Count > max)
        {
            warnings.Add(IcsImportCodes.DatesCapped);
        }

        return [.. values.Take(max)];
    }

    private static void ApplyDetails(Event ev, IcsComponent vevent, SortedSet<string> warnings)
    {
        ev.Description = Limited(Text(vevent, "DESCRIPTION", multiline: true), Event.DescriptionMaxLength, warnings);
        ev.Location = Limited(Text(vevent, "LOCATION"), Event.LocationMaxLength, warnings);
        ev.Status = Status(vevent) ?? EventStatus.Confirmed;
        ev.Transparency = Transparency(vevent);
        if (Text(vevent, "URL") is { } url)
        {
            if (url.Length <= Event.UrlMaxLength && Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            {
                ev.Url = url;
            }
            else
            {
                warnings.Add(IcsImportCodes.UrlInvalid);
            }
        }

        var categories = vevent.All("CATEGORIES")
            .SelectMany(p => IcsReader.SplitList(p.Value))
            .Select(c => c.Replace(',', ' ').Trim())
            .Where(c => c.Length > 0)
            .Select(c => c.Length > Event.CategoryMaxLength ? c[..Event.CategoryMaxLength].TrimEnd() : c)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (categories.Count > Event.MaxCategories)
        {
            warnings.Add(IcsImportCodes.Truncated);
        }

        ev.Categories = [.. categories.Take(Event.MaxCategories)];
        if (Text(vevent, "CLASS")?.ToUpperInvariant() is "PRIVATE" or "CONFIDENTIAL")
        {
            warnings.Add(IcsImportCodes.PrivateEvent); // imports set no overrides: the calendar's audience will see it
        }
    }

    private static EventStatus? Status(IcsComponent vevent) => Text(vevent, "STATUS")?.ToUpperInvariant() switch
    {
        "TENTATIVE" => EventStatus.Tentative,
        "CONFIRMED" => EventStatus.Confirmed,
        _ => null,
    };

    private static EventTransparency Transparency(IcsComponent vevent) =>
        string.Equals(Text(vevent, "TRANSP"), "TRANSPARENT", StringComparison.OrdinalIgnoreCase) ? EventTransparency.Transparent : EventTransparency.Opaque;

    private static string Title(IcsComponent vevent, SortedSet<string> warnings)
    {
        var title = Text(vevent, "SUMMARY");
        if (title is null)
        {
            warnings.Add(IcsImportCodes.TitleMissing);
            return UntitledTitle;
        }

        return Limited(title, Event.TitleMaxLength, warnings)!;
    }

    /// <summary>The UID to store: the file's when valid (Event.IsValidUid), else a stable one derived from it (or from the event's content when it has none), so re-uploads match.</summary>
    private static string Uid(IcsComponent vevent, string? orphanSeed, SortedSet<string> warnings)
    {
        var uid = Text(vevent, "UID");
        if (orphanSeed is null && uid is not null && Event.IsValidUid(uid))
        {
            return uid;
        }

        warnings.Add(IcsImportCodes.UidGenerated);
        var seed = orphanSeed ?? uid ?? Seed(vevent);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)))[..32];
        return $"ics-{hash}@import.{Event.UidDomain}";
    }

    /// <summary>Content of an event without UID (start, end, title, rule): the same event in a re-upload gets the same generated UID.</summary>
    private static string Seed(IcsComponent vevent) =>
        string.Join('|', _seedProperties.Select(n => vevent.First(n)?.Value ?? string.Empty));

    /// <summary>A TEXT value, unescaped and trimmed (single-line ones with line breaks as spaces); null when absent or empty.</summary>
    private static string? Text(IcsComponent component, string name, bool multiline = false)
    {
        if (component.First(name) is not { } property)
        {
            return null;
        }

        var text = IcsReader.UnescapeText(property.Value).Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!multiline)
        {
            text = text.Replace('\n', ' ').Replace('\r', ' ');
        }

        text = new string([.. text.Where(c => !char.IsControl(c) || c == '\n' || c == '\t')]).Trim();
        return text.Length == 0 ? null : text;
    }

    private static string? Limited(string? value, int max, SortedSet<string> warnings)
    {
        if (value is null || value.Length <= max)
        {
            return value;
        }

        warnings.Add(IcsImportCodes.Truncated);
        return value[..max].TrimEnd();
    }

    /// <summary>A DATE or DATE-TIME property value (<c>VALUE=DATE</c>, 8 digits; <c>…T…Z</c> = UTC; <c>TZID</c>).</summary>
    private static IcsTime? ReadTime(IcsProperty? property)
    {
        if (property is null)
        {
            return null;
        }

        var value = property.Value.Trim();
        if (string.Equals(property.Param("VALUE"), "DATE", StringComparison.OrdinalIgnoreCase) || (value.Length == 8 && value.All(char.IsAsciiDigit)))
        {
            var date = _datePattern.Parse(value.Length > 8 ? value[..8] : value);
            return date.Success ? new IcsTime(true, date.Value.AtMidnight(), false, null) : null;
        }

        var utc = value.EndsWith('Z') || value.EndsWith('z');
        var text = utc ? value[..^1] : value;
        foreach (var pattern in _dateTimePatterns)
        {
            var parsed = pattern.Parse(text);
            if (parsed.Success)
            {
                return new IcsTime(false, parsed.Value, utc, utc ? null : property.Param("TZID"));
            }
        }

        return null;
    }

    /// <summary>Days an all-day event with this DURATION covers (a partial day counts as one).</summary>
    private static int WholeDays(Period period)
    {
        var normalized = period.Normalize();
        var days = normalized.Days;
        return (int)Math.Clamp(days + (normalized.Hours > 0 || normalized.Minutes > 0 || normalized.Seconds > 0 ? 1 : 0), 0, 36_500);
    }

    /// <summary>RFC 5545 DURATION (<c>P1D</c>, <c>PT1H30M</c>, <c>P1W</c>; negative ones are refused).</summary>
    private static Period? ParseDuration(string value)
    {
        var text = value.Trim().ToUpperInvariant();
        if (text.StartsWith('+'))
        {
            text = text[1..];
        }

        if (text.StartsWith('-'))
        {
            return null;
        }

        var parsed = PeriodPattern.NormalizingIso.Parse(text);
        return parsed.Success && parsed.Value.Years == 0 && parsed.Value.Months == 0 ? parsed.Value : null;
    }

    private sealed record IcsTime(bool IsDate, LocalDateTime Local, bool Utc, string? TzId)
    {
        public LocalDate Date => Local.Date;
    }

    /// <summary>Per-VCALENDAR context: its VTIMEZONE definitions, the fallback zone and a cache of resolved TZIDs.</summary>
    private sealed class Context
    {
        private readonly Dictionary<string, DateTimeZone?> _zones = new(StringComparer.Ordinal);
        private readonly Dictionary<string, IcsComponent> _definitions;

        public Context(IcsComponent calendar, DateTimeZone calendarZone, Guid calendarId, Guid creatorId, Instant now)
        {
            _definitions = calendar.Components
                .Where(c => c.Name == "VTIMEZONE" && c.First("TZID") is not null)
                .GroupBy(c => c.First("TZID")!.Value.Trim(), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            Fallback = IcsTimeZones.Resolve(calendar.First("X-WR-TIMEZONE")?.Value) ?? calendarZone;
            CalendarId = calendarId;
            CreatorId = creatorId;
            Now = now;
        }

        /// <summary>Zone for floating times and unknown TZIDs: the file's <c>X-WR-TIMEZONE</c>, else the calendar's.</summary>
        public DateTimeZone Fallback { get; }

        public Guid CalendarId { get; }

        public Guid CreatorId { get; }

        public Instant Now { get; }

        public DateTimeZone? Zone(string tzid)
        {
            if (!_zones.TryGetValue(tzid, out var zone))
            {
                _zones[tzid] = zone = IcsTimeZones.Resolve(tzid, _definitions.GetValueOrDefault(tzid.Trim()));
            }

            return zone;
        }
    }
}
