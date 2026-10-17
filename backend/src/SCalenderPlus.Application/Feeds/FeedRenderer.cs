using System.Globalization;
using NodaTime;
using SCalenderPlus.Core.Events;
using SCalenderPlus.Core.Feeds;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Application.Feeds;

/// <summary>An event of a feed with the viewer's effective level (never <c>none</c>; transparent events never at <c>free_busy</c>).</summary>
/// <param name="Event">A single event or a series master with its exceptions loaded.</param>
public sealed record FeedItem(Event Event, EventLevel Level);

/// <summary>What a feed renders: the calendar properties, the viewer's settings and the resolved events.</summary>
/// <param name="Name"><c>X-WR-CALNAME</c>.</param>
/// <param name="TimeZone"><c>X-WR-TIMEZONE</c>: the viewer's zone (a display hint; every time carries its own TZID).</param>
/// <param name="NativeUids">Single-calendar feeds: the stored UID for <c>read</c> and above; false (multi-calendar feeds) = <c>{id}@scalenderplus</c> everywhere.</param>
/// <param name="AccessProperties">The description footer with app links and the <c>X-SCALENDERPLUS-*</c> properties (personal feeds; not share links, whose holders have no account).</param>
/// <param name="ProductName">The brand in user-visible texts (<c>Brand__ProductName</c>).</param>
/// <param name="AppBaseUrl">Base of the app links in the footer (<c>App__PublicBaseUrl</c>).</param>
/// <param name="Now">Picks the rule period of the generated <c>VTIMEZONE</c>s (the year before it on).</param>
public sealed record FeedDocument(
    string Name,
    string TimeZone,
    FeedTexts Texts,
    FeedLabelMode LabelMode,
    bool NativeUids,
    bool AccessProperties,
    string ProductName,
    Uri AppBaseUrl,
    Instant Now,
    IReadOnlyList<FeedItem> Items);

/// <summary>
/// The iCalendar projection of feeds (permissions.md §7, ical-caldav.md §1.2–1.3) — pure and deterministic, so
/// every rule has a golden file. Series are written as <c>RRULE</c>/<c>RDATE</c>/<c>EXDATE</c> plus one
/// <c>RECURRENCE-ID</c> VEVENT per modified occurrence (never expanded); cancelled occurrences are <c>EXDATE</c>s.
/// <list type="bullet">
/// <item><c>free_busy</c>: only times, recurrence, <c>TRANSP</c>, <c>CLASS:CONFIDENTIAL</c>, <c>SUMMARY</c> = the
/// cover title or the localized "Busy", <c>DTSTAMP</c> = creation (edits of hidden fields leave no trace) and the
/// opaque UID <c>{id}@scalenderplus</c>. Exceptions are stripped the same way: only those that change what busy
/// viewers see (moved, another cover title) get a VEVENT; transparent occurrences become <c>EXDATE</c>s.</item>
/// <item><c>read</c> / <c>edit</c> / <c>manage</c>: every field plus the access label of the token's
/// <see cref="FeedLabelMode"/>, a description footer and <c>X-SCALENDERPLUS-ACCESS</c>/<c>-EVENT-ID</c>.</item>
/// <item>Never <c>ATTENDEE</c>/<c>ORGANIZER</c> (no iTIP replies to a feed) and no <c>VALARM</c>.</item>
/// </list>
/// </summary>
public static class FeedRenderer
{
    /// <summary>
    /// Technical identifier of the generator: stays unbranded in every edition and deployment like the UID domain
    /// (editions.md §3.3) — clients may log or compare it; users never see it.
    /// </summary>
    public const string ProductId = "-//sCalenderPlus//EN";

    /// <summary>Bump when the output changes for the same data: part of the feed ETag (cached renders and client caches refresh).</summary>
    public const int FormatVersion = 1;

    public static string Render(FeedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var writer = new IcsWriter();
        writer.Begin("VCALENDAR")
            .Raw("VERSION", "2.0")
            .Raw("PRODID", ProductId)
            .Raw("CALSCALE", "GREGORIAN")
            .Raw("METHOD", "PUBLISH")
            .Text("X-WR-CALNAME", document.Name)
            .Text("X-WR-TIMEZONE", document.TimeZone)
            .Raw("REFRESH-INTERVAL;VALUE=DURATION", "PT1H")
            .Raw("X-PUBLISHED-TTL", "PT1H");

        var zones = document.Items
            .Where(i => !i.Event.AllDay && i.Event.TimeZone is not null)
            .GroupBy(i => i.Event.TimeZone!, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal);
        foreach (var zoneItems in zones)
        {
            if (DateTimeZoneProviders.Tzdb.GetZoneOrNull(zoneItems.Key) is { } zone)
            {
                var earliest = zoneItems.Min(i => i.Event.SeriesStartUtc is { } s && s < i.Event.StartUtc ? s : i.Event.StartUtc);
                VTimeZoneBuilder.Write(writer, zone, earliest, document.Now);
            }
        }

        foreach (var item in document.Items.OrderBy(i => i.Event.StartUtc).ThenBy(i => i.Event.Id))
        {
            if (EventVisibility.IsBusyOnly(item.Level))
            {
                WriteBusy(writer, document, item.Event);
            }
            else
            {
                WriteFull(writer, document, item.Event, item.Level);
            }
        }

        writer.End("VCALENDAR");
        return writer.ToString();
    }

    /// <summary>VEVENTs this item produces (master plus written exceptions), for the per-feed cap.</summary>
    public static int CountVEvents(FeedItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.Event.IsSeries)
        {
            return 1;
        }

        return 1 + (EventVisibility.IsBusyOnly(item.Level)
            ? BusyExceptions(item.Event).Count(x => x.Kind == BusyExceptionKind.Event)
            : item.Event.Exceptions.Count(x => !x.Cancelled));
    }

    private static void WriteFull(IcsWriter writer, FeedDocument document, Event ev, EventLevel level)
    {
        var uid = document.NativeUids ? ev.Uid : Event.NativeUid(ev.Id);
        writer.Begin("VEVENT")
            .Raw("UID", uid)
            .Utc("DTSTAMP", ev.UpdatedAt)
            .Utc("CREATED", ev.CreatedAt)
            .Utc("LAST-MODIFIED", ev.UpdatedAt)
            .Raw("SEQUENCE", ev.Sequence.ToString(CultureInfo.InvariantCulture));
        WriteTimes(writer, ev.Times);
        if (ev.IsSeries)
        {
            writer.Raw("RRULE", ev.Rrule!);
            WriteDates(writer, "RDATE", ev, ev.RDates);
            WriteDates(writer, "EXDATE", ev, ev.ExDates.Concat(ev.Exceptions.Where(x => x.Cancelled).Select(x => x.RecurrenceId)));
        }

        WriteDetails(writer, document, ev, level, ev.Title, ev.Description, ev.Location, ev.Status, ev.Transparency);
        writer.End("VEVENT");

        if (!ev.IsSeries)
        {
            return;
        }

        foreach (var exception in ev.Exceptions.Where(x => !x.Cancelled).OrderBy(x => x.RecurrenceId))
        {
            if (ev.FindOccurrence(exception.RecurrenceId) is not { } occurrence)
            {
                continue; // no longer an occurrence of the set (dropped on the next change of the series)
            }

            writer.Begin("VEVENT")
                .Raw("UID", uid)
                .Utc("DTSTAMP", exception.UpdatedAt)
                .Utc("CREATED", exception.CreatedAt)
                .Utc("LAST-MODIFIED", exception.UpdatedAt)
                .Raw("SEQUENCE", ev.Sequence.ToString(CultureInfo.InvariantCulture));
            WriteRecurrenceId(writer, ev, exception.RecurrenceId);
            WriteTimes(writer, occurrence.Times);
            WriteDetails(writer, document, ev, level, occurrence.Title, occurrence.Description, occurrence.Location, occurrence.Status, occurrence.Transparency);
            writer.End("VEVENT");
        }
    }

    private static void WriteDetails(
        IcsWriter writer,
        FeedDocument document,
        Event ev,
        EventLevel level,
        string title,
        string? description,
        string? location,
        EventStatus status,
        EventTransparency transparency)
    {
        writer.Text("SUMMARY", Summary(document, title, level));
        if (Description(document, ev.Id, level, description) is { Length: > 0 } text)
        {
            writer.Text("DESCRIPTION", text);
        }

        if (!string.IsNullOrEmpty(location))
        {
            writer.Text("LOCATION", location);
        }

        if (!string.IsNullOrEmpty(ev.Url))
        {
            writer.Raw("URL", ev.Url);
        }

        writer.Raw("STATUS", status switch
        {
            EventStatus.Tentative => "TENTATIVE",
            EventStatus.Cancelled => "CANCELLED",
            _ => "CONFIRMED",
        });
        writer.Raw("TRANSP", transparency == EventTransparency.Transparent ? "TRANSPARENT" : "OPAQUE");
        if (ev.Categories.Count > 0)
        {
            writer.Raw("CATEGORIES", string.Join(',', ev.Categories.Select(IcsWriter.EscapeText)));
        }

        if (!string.IsNullOrEmpty(ev.RelatedTo))
        {
            writer.Raw("RELATED-TO", ev.RelatedTo);
        }

        if (document.AccessProperties)
        {
            writer.Raw("X-SCALENDERPLUS-ACCESS", PermissionLevels.Format(level))
                .Raw("X-SCALENDERPLUS-EVENT-ID", ev.Id.ToString());
        }
    }

    private static void WriteBusy(IcsWriter writer, FeedDocument document, Event ev)
    {
        var uid = Event.NativeUid(ev.Id);
        var exceptions = ev.IsSeries ? BusyExceptions(ev) : [];
        writer.Begin("VEVENT")
            .Raw("UID", uid)
            .Utc("DTSTAMP", ev.CreatedAt);
        WriteTimes(writer, ev.Times);
        if (ev.IsSeries)
        {
            writer.Raw("RRULE", ev.Rrule!);
            WriteDates(writer, "RDATE", ev, ev.RDates);
            WriteDates(writer, "EXDATE", ev, ev.ExDates.Concat(exceptions.Where(x => x.Kind == BusyExceptionKind.Excluded).Select(x => x.RecurrenceId)));
        }

        WriteBusyDetails(writer, document, ev.CoverTitle);
        writer.End("VEVENT");

        foreach (var exception in exceptions.Where(x => x.Kind == BusyExceptionKind.Event))
        {
            writer.Begin("VEVENT")
                .Raw("UID", uid)
                .Utc("DTSTAMP", ev.CreatedAt);
            WriteRecurrenceId(writer, ev, exception.RecurrenceId);
            WriteTimes(writer, exception.Occurrence!.Times);
            WriteBusyDetails(writer, document, exception.Occurrence.CoverTitle);
            writer.End("VEVENT");
        }
    }

    private static void WriteBusyDetails(IcsWriter writer, FeedDocument document, string? coverTitle) =>
        writer.Text("SUMMARY", string.IsNullOrEmpty(coverTitle) ? document.Texts.Busy : coverTitle)
            .Raw("CLASS", "CONFIDENTIAL")
            .Raw("TRANSP", "OPAQUE");

    private enum BusyExceptionKind
    {
        /// <summary>Hidden from busy viewers: cancelled, or transparent ("free" time is never shown at free_busy).</summary>
        Excluded,

        /// <summary>Changes what busy viewers see (moved, or another cover title): its own VEVENT.</summary>
        Event,
    }

    private sealed record BusyException(LocalDateTime RecurrenceId, BusyExceptionKind Kind, EventOccurrence? Occurrence);

    /// <summary>The exceptions of a series as busy viewers see them; exceptions that only change hidden fields are left out.</summary>
    private static List<BusyException> BusyExceptions(Event ev)
    {
        var result = new List<BusyException>();
        foreach (var exception in ev.Exceptions.OrderBy(x => x.RecurrenceId))
        {
            if (exception.Cancelled)
            {
                result.Add(new(exception.RecurrenceId, BusyExceptionKind.Excluded, null));
                continue;
            }

            if (ev.FindOccurrence(exception.RecurrenceId) is not { } occurrence)
            {
                continue;
            }

            if (occurrence.Transparency == EventTransparency.Transparent)
            {
                result.Add(new(exception.RecurrenceId, BusyExceptionKind.Excluded, null));
            }
            else if (exception.IsMoved || occurrence.CoverTitle != ev.CoverTitle)
            {
                result.Add(new(exception.RecurrenceId, BusyExceptionKind.Event, occurrence));
            }
        }

        return result;
    }

    private static string Summary(FeedDocument document, string title, EventLevel level)
    {
        var editable = level >= EventLevel.Edit;
        return document.LabelMode switch
        {
            FeedLabelMode.PrefixEmoji => editable ? title : $"{FeedTexts.ReadOnlyMarker} {title}",
            FeedLabelMode.PrefixEmojiBoth => $"{(editable ? FeedTexts.EditableMarker : FeedTexts.ReadOnlyMarker)} {title}",
            FeedLabelMode.SuffixText => $"{title} {(editable ? document.Texts.EditableSuffix : document.Texts.ReadOnlySuffix)}",
            _ => title,
        };
    }

    private static string? Description(FeedDocument document, Guid eventId, EventLevel level, string? description)
    {
        if (document.LabelMode == FeedLabelMode.None || !document.AccessProperties)
        {
            return description;
        }

        var link = new Uri(document.AppBaseUrl, level >= EventLevel.Edit ? $"e/{eventId}/edit" : $"e/{eventId}");
        var footer = string.Format(
            CultureInfo.InvariantCulture,
            level >= EventLevel.Edit ? document.Texts.EditableFooter : document.Texts.ReadOnlyFooter,
            document.ProductName,
            link);
        return string.IsNullOrEmpty(description) ? "—\n" + footer : description + "\n\n—\n" + footer;
    }

    private static void WriteTimes(IcsWriter writer, EventTimes times)
    {
        if (times.AllDay)
        {
            writer.Date("DTSTART", times.StartDate!.Value).Date("DTEND", times.EndDate!.Value);
        }
        else
        {
            writer.Zoned("DTSTART", times.TimeZone!, times.StartLocal!.Value).Zoned("DTEND", times.TimeZone!, times.EndLocal!.Value);
        }
    }

    private static void WriteRecurrenceId(IcsWriter writer, Event series, LocalDateTime recurrenceId)
    {
        if (series.AllDay)
        {
            writer.Date("RECURRENCE-ID", recurrenceId.Date);
        }
        else
        {
            writer.Zoned("RECURRENCE-ID", series.TimeZone!, recurrenceId);
        }
    }

    private static void WriteDates(IcsWriter writer, string name, Event series, IEnumerable<LocalDateTime> values)
    {
        var dates = values.Distinct().Order().ToList();
        if (dates.Count == 0)
        {
            return;
        }

        if (series.AllDay)
        {
            writer.Raw(name + ";VALUE=DATE", string.Join(',', dates.Select(d => IcsWriter.FormatDate(d.Date))));
        }
        else
        {
            writer.Raw($"{name};TZID={series.TimeZone}", string.Join(',', dates.Select(IcsWriter.FormatLocal)));
        }
    }
}
