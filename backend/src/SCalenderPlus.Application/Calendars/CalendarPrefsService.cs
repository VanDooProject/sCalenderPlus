using Microsoft.EntityFrameworkCore;
using NodaTime;
using SCalenderPlus.Application.Common;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Core.Calendars;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Application.Calendars;

/// <summary>A user's overlay of a calendar; <see cref="Color"/> null = the calendar's own color.</summary>
public sealed record CalendarPrefsView(Guid CalendarId, bool Hidden, string? Color)
{
    public static CalendarPrefsView Default(Guid calendarId) => new(calendarId, false, null);

    public static CalendarPrefsView From(CalendarPrefs prefs)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        return new CalendarPrefsView(prefs.CalendarId, prefs.Hidden, prefs.Color);
    }
}

/// <summary>
/// The personal calendar overlay (<c>PUT /calendars/{id}/prefs</c>, features.md §3 "Personal overlay"): every user
/// who sees a calendar (level ≥ <c>free_busy</c>; <c>none</c> → 404) may hide it or give it their own color. It
/// changes nothing for anyone else, so it is neither audited nor ACL-relevant, and frozen calendars accept it too.
/// </summary>
public sealed class CalendarPrefsService(IAppDbContext db, CalendarAccessLoader access, IClock clock)
{
    /// <summary>The actor's overlays (rows only: calendars without one use the defaults), by calendar id.</summary>
    public async Task<IReadOnlyList<CalendarPrefsView>> ListMineAsync(Guid actorId, CancellationToken cancellationToken = default)
    {
        var rows = await db.CalendarPrefs.AsNoTracking().Where(p => p.UserId == actorId).OrderBy(p => p.CalendarId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. rows.Select(CalendarPrefsView.From)];
    }

    /// <exception cref="Errors.AppException"><c>not_found</c> unless the actor sees the calendar.</exception>
    public async Task<CalendarPrefsView> GetAsync(Guid actorId, Guid calendarId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(actorId, calendarId, CalendarAction.View, cancellationToken: cancellationToken).ConfigureAwait(false);
        var row = await db.CalendarPrefs.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == actorId && p.CalendarId == calendarId, cancellationToken).ConfigureAwait(false);
        return row is null ? CalendarPrefsView.Default(calendarId) : CalendarPrefsView.From(row);
    }

    /// <summary>Replaces the actor's overlay of the calendar (a full <c>PUT</c>: <paramref name="color"/> null = the calendar's).</summary>
    /// <param name="precondition">Checks If-Match against the current overlay (after authorization); throws to refuse.</param>
    public async Task<CalendarPrefsView> PutAsync(
        Guid actorId,
        Guid calendarId,
        bool hidden,
        string? color,
        Action<CalendarPrefsView>? precondition = null,
        CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(actorId, calendarId, CalendarAction.View, cancellationToken: cancellationToken).ConfigureAwait(false);
        var row = await db.CalendarPrefs.SingleOrDefaultAsync(p => p.UserId == actorId && p.CalendarId == calendarId, cancellationToken).ConfigureAwait(false);
        precondition?.Invoke(row is null ? CalendarPrefsView.Default(calendarId) : CalendarPrefsView.From(row));

        var validColor = color is null
            ? null
            : Calendar.IsValidColor(color) ? color.ToLowerInvariant() : throw Validation.Failed("color", "Use a hex color like #4f46e5, or null for the calendar's color.");
        var inserted = row is null;
        if (row is null)
        {
            row = new CalendarPrefs { UserId = actorId, CalendarId = calendarId };
            db.CalendarPrefs.Add(row);
        }

        row.Hidden = hidden;
        row.Color = validColor;
        row.UpdatedAt = clock.Now();
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException) when (inserted)
        {
            // A concurrent first PUT of the same user inserted the row meanwhile: their overlay, so 412 like a stale If-Match.
            throw CalendarErrors.Changed();
        }

        return CalendarPrefsView.From(row);
    }
}
