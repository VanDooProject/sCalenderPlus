using NodaTime;
using SCalenderPlus.Core.Recurrence;

namespace SCalenderPlus.Core.Events;

/// <summary>
/// A modified or cancelled occurrence of a series (<c>event_exceptions</c>, data-model.md §4/§9): keyed by
/// <see cref="RecurrenceId"/> — the occurrence's nominal start as wall clock in the series' zone (all-day: the date
/// at midnight), the iCalendar RECURRENCE-ID — so tzdb updates never orphan it. Null members inherit the series'
/// value; an empty <see cref="Description"/> or <see cref="Location"/> removes it for this occurrence. Moved
/// occurrences carry their own times (same kind and zone as the series) and the derived instants. Exceptions have
/// no ACL of their own: they resolve like their series (permissions.md §4.6, <c>EventAcl.ExceptionOf</c>).
/// </summary>
public sealed class EventExceptionEntry
{
    public Guid Id { get; set; }

    /// <summary>The series master.</summary>
    public Guid EventId { get; set; }

    public LocalDateTime RecurrenceId { get; set; }

    /// <summary>The occurrence is cancelled (exported as EXDATE); the other members are ignored then.</summary>
    public bool Cancelled { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    public string? Location { get; set; }

    public EventStatus? Status { get; set; }

    public EventTransparency? Transparency { get; set; }

    public LocalDateTime? StartLocal { get; set; }

    public LocalDateTime? EndLocal { get; set; }

    public LocalDate? StartDate { get; set; }

    /// <summary>Exclusive.</summary>
    public LocalDate? EndDate { get; set; }

    /// <summary>Start of a moved occurrence (all-day: padded like <see cref="EventTimes"/>); null when not moved.</summary>
    public Instant? StartUtc { get; set; }

    public Instant? EndUtc { get; set; }

    public Instant CreatedAt { get; set; }

    public Instant UpdatedAt { get; set; }

    public bool IsMoved => StartUtc is not null;

    /// <summary>Nothing is overridden: the occurrence is exactly the series' (the row can go).</summary>
    public bool IsEmpty => !Cancelled && !IsMoved && Title is null && Description is null && Location is null && Status is null && Transparency is null;

    /// <summary>The moved times (zone of the series for timed occurrences), or null when not moved.</summary>
    public EventTimes? MovedTimes(string? timeZone) => !IsMoved
        ? null
        : StartDate is { } start
            ? new EventTimes(true, null, null, start, EndDate, null, StartUtc!.Value, EndUtc!.Value, [])
            : new EventTimes(false, StartLocal, EndLocal, null, null, timeZone, StartUtc!.Value, EndUtc!.Value, []);

    /// <summary>Moves the occurrence to <paramref name="times"/>; null (or the original times) undoes the move.</summary>
    public void Move(EventTimes? times)
    {
        StartLocal = times?.StartLocal;
        EndLocal = times?.EndLocal;
        StartDate = times?.StartDate;
        EndDate = times?.EndDate;
        StartUtc = times?.StartUtc;
        EndUtc = times?.EndUtc;
    }
}

/// <summary>
/// One occurrence of a series as viewers see it: the <see cref="Original"/> occurrence of the recurrence set with
/// the <see cref="Exception"/> (if any) applied — its id is <c>{seriesId}:{recurrenceId}</c> in the api.
/// </summary>
public sealed record EventOccurrence(Event Series, Occurrence Original, EventExceptionEntry? Exception)
{
    public LocalDateTime RecurrenceId => Original.RecurrenceId;

    public EventTimes Times => Exception?.MovedTimes(Series.TimeZone) ?? Original.Times;

    public string Title => Exception?.Title ?? Series.Title;

    public string? Description => Effective(Exception?.Description, Series.Description);

    public string? Location => Effective(Exception?.Location, Series.Location);

    public EventStatus Status => Exception?.Status ?? Series.Status;

    public EventTransparency Transparency => Exception?.Transparency ?? Series.Transparency;

    /// <summary>The occurrence differs from the series (moved or with changed fields).</summary>
    public bool IsModified => Exception is { IsEmpty: false };

    private static string? Effective(string? exception, string? series) =>
        exception is null ? series : exception.Length == 0 ? null : exception;
}
