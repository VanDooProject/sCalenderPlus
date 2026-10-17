using System.Runtime.CompilerServices;
using System.Text;
using NodaTime;
using SCalenderPlus.Application.Feeds;
using SCalenderPlus.Core.Events;
using SCalenderPlus.Core.Feeds;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Application.Tests.Feeds;

/// <summary>
/// Fixed events for the feed golden files: deterministic ids, instants and texts (the files are compared byte for
/// byte and parsed by Ical.Net here and by ical.js in the frontend tests).
/// </summary>
internal static class FeedFixtures
{
    public static readonly Instant Now = Instant.FromUtc(2026, 10, 17, 12, 0);
    public static readonly Instant Created = Instant.FromUtc(2026, 9, 1, 8, 0);
    public static readonly Instant Updated = Instant.FromUtc(2026, 10, 1, 9, 30);
    public static readonly Uri AppBase = new("https://app.example.test/");

    private static readonly DateTimeZone _berlin = DateTimeZoneProviders.Tzdb["Europe/Berlin"];

    public static Guid Id(int n) => Guid.Parse($"01990000-0000-7000-8000-{n:000000000000}");

    public static FeedDocument Document(
        IReadOnlyList<FeedItem> items,
        FeedLabelMode labelMode = FeedLabelMode.PrefixEmoji,
        string locale = "en",
        bool nativeUids = true,
        bool accessProperties = true,
        string name = "FC Lions – Club",
        string timeZone = "Europe/Berlin") =>
        new(name, timeZone, FeedTexts.For(locale), labelMode, nativeUids, accessProperties, "sCalenderPlus", AppBase, Now, items);

    /// <summary>A timed event (wall clock in <paramref name="zone"/>, default Berlin).</summary>
    public static Event Timed(int id, string title, string start, string end, string zone = "Europe/Berlin", string? uid = null)
    {
        var times = EventTimes.Timed(Local(start), Local(end), DateTimeZoneProviders.Tzdb[zone])!;
        var ev = new Event
        {
            Id = Id(id),
            CalendarId = Id(900),
            Uid = uid ?? Event.NativeUid(Id(id)),
            Title = title,
            CreatedAt = Created,
            UpdatedAt = Updated,
        };
        ev.SetTimes(times);
        return ev;
    }

    public static Event AllDay(int id, string title, string start, string endExclusive)
    {
        var ev = new Event
        {
            Id = Id(id),
            CalendarId = Id(900),
            Uid = Event.NativeUid(Id(id)),
            Title = title,
            CreatedAt = Created,
            UpdatedAt = Updated,
        };
        ev.SetTimes(EventTimes.AllDayEvent(LocalDate.FromDateTime(DateTime.Parse(start, System.Globalization.CultureInfo.InvariantCulture)), LocalDate.FromDateTime(DateTime.Parse(endExclusive, System.Globalization.CultureInfo.InvariantCulture)))!);
        return ev;
    }

    /// <summary>Makes <paramref name="ev"/> a series with <paramref name="rrule"/> (canonical text).</summary>
    public static Event Series(this Event ev, string rrule, string[]? exdates = null, string[]? rdates = null)
    {
        ev.Rrule = rrule;
        ev.ExDates = [.. (exdates ?? []).Select(Local)];
        ev.RDates = [.. (rdates ?? []).Select(Local)];
        return ev;
    }

    /// <summary>An exception of a Berlin series; <paramref name="moveTo"/> (start, end) moves it.</summary>
    public static Event WithException(
        this Event series,
        string recurrenceId,
        bool cancelled = false,
        string? title = null,
        string? coverTitle = null,
        string? description = null,
        EventTransparency? transparency = null,
        (string Start, string End)? moveTo = null)
    {
        var exception = new EventExceptionEntry
        {
            Id = Guid.NewGuid(),
            EventId = series.Id,
            RecurrenceId = Local(recurrenceId),
            Cancelled = cancelled,
            Title = title,
            CoverTitle = coverTitle,
            Description = description,
            Transparency = transparency,
            CreatedAt = Updated,
            UpdatedAt = Updated,
        };
        if (moveTo is { } move)
        {
            exception.Move(EventTimes.Timed(Local(move.Start), Local(move.End), DateTimeZoneProviders.Tzdb[series.TimeZone ?? "Europe/Berlin"]));
        }

        series.Exceptions.Add(exception);
        return series;
    }

    public static FeedItem At(this Event ev, EventLevel level) => new(ev, level);

    public static LocalDateTime Local(string value) =>
        NodaTime.Text.LocalDateTimePattern.ExtendedIso.Parse(value).GetValueOrThrow();

    /// <summary>The Berlin zone (most fixtures).</summary>
    public static DateTimeZone Berlin => _berlin;
}

/// <summary>
/// Golden files (no Verify in this repo): the rendered text must equal the committed <c>Feeds/Golden/{name}</c>
/// byte for byte. With <c>SCAL_UPDATE_GOLDEN=1</c> the files are (re)written and the test fails, so a changed
/// file is always reviewed in the diff; a missing file is written and fails the same way.
/// </summary>
internal static class GoldenFile
{
    public static string Directory([CallerFilePath] string source = "") => Path.Combine(Path.GetDirectoryName(source)!, "Golden");

    public static void Assert(string name, string actual)
    {
        if (Check(name, actual) is { } failure)
        {
            Xunit.Assert.Fail(failure);
        }
    }

    /// <summary>Compares (or, when updating or missing, writes) one golden file; the failure message, or null.</summary>
    private static string? Check(string name, string actual)
    {
        var path = Path.Combine(Directory(), name);
        var update = Environment.GetEnvironmentVariable("SCAL_UPDATE_GOLDEN") == "1";
        if (update || !File.Exists(path))
        {
            File.WriteAllText(path, actual, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return $"Golden file {name} was {(update ? "updated" : "missing and was written")}: review it and run the test again without SCAL_UPDATE_GOLDEN.";
        }

        var expected = File.ReadAllText(path, Encoding.UTF8);
        return expected == actual
            ? null
            : $"Feed differs from the golden file {name} (rerun with SCAL_UPDATE_GOLDEN=1 to accept, then review the diff).\n--- expected\n{expected}\n--- actual\n{actual}";
    }

    private static readonly System.Text.Json.JsonSerializerOptions _indented = new() { WriteIndented = true };

    public static IEnumerable<string> All() => System.IO.Directory.EnumerateFiles(Directory(), "*.ics").Order(StringComparer.Ordinal);

    /// <summary>
    /// Renders <paramref name="document"/>, compares it with the golden file <paramref name="name"/> and its sidecar
    /// <c>{name}.instants.json</c>: the UTC instants (by the bundled tzdb) of every timed VEVENT and the first
    /// occurrences of every timed series, which the ical.js test compares with what it computes from the file's own
    /// VTIMEZONEs.
    /// </summary>
    public static string AssertFeed(string name, FeedDocument document)
    {
        var ics = FeedRenderer.Render(document);
        var failures = new[] { Check(name, ics), Check(Path.ChangeExtension(name, ".instants.json"), Instants(document)) }.OfType<string>().ToList();
        if (failures.Count > 0)
        {
            Xunit.Assert.Fail(string.Join("\n", failures));
        }

        return ics;
    }

    private static string Instants(FeedDocument document)
    {
        var events = new List<object>();
        var occurrences = new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var item in document.Items.Where(i => !i.Event.AllDay).OrderBy(i => i.Event.StartUtc).ThenBy(i => i.Event.Id))
        {
            var ev = item.Event;
            var uid = document.NativeUids && !EventVisibility.IsBusyOnly(item.Level) ? ev.Uid : Event.NativeUid(ev.Id);
            var zone = DateTimeZoneProviders.Tzdb[ev.TimeZone!];
            events.Add(new { uid, recurrenceId = (string?)null, start = Utc(ev.StartUtc), end = Utc(ev.EndUtc) });
            if (!ev.IsSeries)
            {
                continue;
            }

            foreach (var exception in ev.Exceptions.Where(x => !x.Cancelled).OrderBy(x => x.RecurrenceId))
            {
                if (ev.FindOccurrence(exception.RecurrenceId) is { } occurrence)
                {
                    events.Add(new { uid, recurrenceId = Utc(zone.AtLeniently(exception.RecurrenceId).ToInstant()), start = Utc(occurrence.Times.StartUtc), end = Utc(occurrence.Times.EndUtc) });
                }
            }

            var set = ev.Recurrence()!;
            occurrences[uid] = [.. set.Between(ev.StartUtc, ev.StartUtc + Duration.FromDays(800), 12).Items.Select(o => Utc(o.Times.StartUtc))];
        }

        var json = System.Text.Json.JsonSerializer.Serialize(new { events, occurrences }, _indented);
        return json.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static string Utc(Instant instant) => NodaTime.Text.InstantPattern.ExtendedIso.Format(instant);
}
