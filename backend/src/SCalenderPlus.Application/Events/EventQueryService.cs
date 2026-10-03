using Microsoft.EntityFrameworkCore;
using NodaTime;
using SCalenderPlus.Application.Calendars;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Application.Users;
using SCalenderPlus.Core.Calendars;
using SCalenderPlus.Core.Events;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Application.Events;

/// <summary>
/// An event as the viewer sees it: the row, its calendar (and the engine's ACL of it), the viewer's effective <see cref="Level"/> (never
/// <c>none</c>; below <c>read</c> only the busy projection may be shown), whether it reached them only through an
/// override of a calendar they cannot see (<see cref="SharedWithMe"/>, rule 7) and the creator's display name
/// (null for viewers below <c>read</c>, system events and deleted accounts).
/// </summary>
public sealed record EventView(Event Event, Calendar Calendar, CalendarAcl Acl, EventLevel Level, bool SharedWithMe, string? CreatorName);

/// <summary>A window query: <c>[From, To)</c>, optionally only some calendars, optionally the viewer's zone for all-day events.</summary>
/// <param name="CalendarIds">Only events of these calendars; null = every calendar the viewer sees, plus "Shared with me".</param>
/// <param name="ViewerZone">Places all-day events by their dates in this zone; null = every all-day event that overlaps the window in some zone.</param>
public sealed record EventWindowQuery(Instant From, Instant To, IReadOnlyCollection<Guid>? CalendarIds = null, DateTimeZone? ViewerZone = null);

/// <summary>The events of a window, ordered by start; <see cref="Truncated"/> when more than <see cref="EventQueryService.MaxWindowEvents"/> matched.</summary>
public sealed record EventWindow(IReadOnlyList<EventView> Items, bool Truncated);

/// <summary>
/// The permission-aware read side of events — the single choke point of permissions.md §8: every event row is
/// read here and resolved by the pure engine (<see cref="PermissionEngine.ResolveLevel"/>) before anyone sees it;
/// an architecture test forbids <c>IAppDbContext.Events</c> anywhere else except <see cref="EventWriter"/>.
/// Engine inputs come from <see cref="CalendarAccessLoader"/> (principal, calendar ACLs) and
/// <see cref="IEventOverrideSource"/> (overrides, loaded only for events with <c>has_overrides</c>).
/// </summary>
public sealed class EventQueryService(
    IAppDbContext db,
    CalendarAccessLoader calendars,
    IEventOverrideSource overrides,
    IUserDirectory users)
{
    /// <summary>Result cap of a window query (api.md §4: windows are not paged but bounded).</summary>
    public const int MaxWindowEvents = 5000;

    /// <summary>
    /// The event as <paramref name="actorId"/> sees it; <c>404</c> for unknown and deleted events and for level
    /// <c>none</c> (incl. transparent events seen at <c>free_busy</c>).
    /// </summary>
    /// <param name="forUpdate">Track the event for changes (use cases check the required level before changing it).</param>
    public async Task<EventView> GetAsync(Guid actorId, Guid eventId, bool forUpdate = false, CancellationToken cancellationToken = default)
    {
        var events = forUpdate ? db.Events : db.Events.AsNoTracking();
        var ev = await events.SingleOrDefaultAsync(e => e.Id == eventId && e.DeletedAt == null, cancellationToken).ConfigureAwait(false)
            ?? throw EventErrors.NotFound();
        var calendar = await calendars.FindAclAsync(ev.CalendarId, cancellationToken).ConfigureAwait(false)
            ?? throw EventErrors.NotFound();
        var principal = await calendars.PrincipalAsync(actorId, cancellationToken).ConfigureAwait(false);
        var view = await ResolveAsync(principal, ev, calendar.Calendar, calendar.Acl, cancellationToken).ConfigureAwait(false)
            ?? throw EventErrors.NotFound();
        return (await WithCreatorNamesAsync([view], cancellationToken).ConfigureAwait(false))[0];
    }

    /// <summary>The view of an event the actor just created or changed (resolved anew).</summary>
    public async Task<EventView> ViewAsync(Guid actorId, Event ev, Calendar calendar, CalendarAcl acl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ev);
        var principal = await calendars.PrincipalAsync(actorId, cancellationToken).ConfigureAwait(false);
        var view = await ResolveAsync(principal, ev, calendar, acl, cancellationToken).ConfigureAwait(false)
            ?? throw EventErrors.NotFound(); // the actor lost sight of it with this very change
        return (await WithCreatorNamesAsync([view], cancellationToken).ConfigureAwait(false))[0];
    }

    /// <summary>Whether a live event of <paramref name="calendarId"/> has <paramref name="uid"/> (no permission check: UIDs are per calendar).</summary>
    public Task<bool> UidTakenAsync(Guid calendarId, string uid, CancellationToken cancellationToken = default) =>
        db.Events.AnyAsync(e => e.CalendarId == calendarId && e.Uid == uid && e.DeletedAt == null, cancellationToken);

    /// <summary>
    /// Events overlapping the window, as the actor sees them (permissions.md §8 listing): (1) the calendars the
    /// actor sees with their ACLs, (2) one SQL query per window over the GiST index <c>(calendar_id, occurs_range)</c>
    /// for those calendars (<see cref="WindowSql"/>), unioned with the events whose overrides name the actor
    /// ("Shared with me"), (3) overrides only for events with <c>has_overrides</c>, (4) untraced resolution in
    /// memory, dropping <c>none</c> and transparent events seen at <c>free_busy</c>.
    /// </summary>
    public async Task<EventWindow> WindowAsync(Guid actorId, EventWindowQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var principal = await calendars.PrincipalAsync(actorId, cancellationToken).ConfigureAwait(false);
        var visible = await calendars.VisibleAsync(principal, query.CalendarIds, cancellationToken).ConfigureAwait(false);
        var calendarsById = visible.ToDictionary(v => v.Calendar.Id, v => (v.Calendar, v.Acl));

        var rows = visible.Count == 0
            ? []
            : await db.Events.FromSql(WindowSql([.. calendarsById.Keys], query.From, query.To, MaxWindowEvents + 1)).AsNoTracking()
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        var truncated = rows.Count > MaxWindowEvents;
        if (truncated)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        // "Shared with me": events whose overrides name the actor, also in calendars they cannot see (rule 7).
        var named = await overrides.EventsNamingAsync(principal, cancellationToken).ConfigureAwait(false);
        if (named.Count > 0)
        {
            var known = rows.Select(r => r.Id).ToHashSet();
            var ids = named.Where(id => !known.Contains(id)).ToList();
            var (from, to) = (query.From, query.To);
            var shared = await db.Events.AsNoTracking()
                .Where(e => ids.Contains(e.Id) && e.DeletedAt == null && e.StartUtc < to && e.EndUtc >= from)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            rows.AddRange(query.CalendarIds is null ? shared : shared.Where(e => query.CalendarIds.Contains(e.CalendarId)));
            var missing = rows.Select(r => r.CalendarId).Where(id => !calendarsById.ContainsKey(id)).Distinct().ToList();
            if (missing.Count > 0)
            {
                var extra = await db.Calendars.AsNoTracking().Where(c => missing.Contains(c.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
                var extraAcls = await calendars.AclsAsync(extra, cancellationToken).ConfigureAwait(false);
                foreach (var calendar in extra)
                {
                    calendarsById[calendar.Id] = (calendar, extraAcls[calendar.Id]);
                }
            }
        }

        var eventOverrides = await OverridesAsync(rows, cancellationToken).ConfigureAwait(false);
        var calendarLevels = calendarsById.ToDictionary(c => c.Key, c => PermissionEngine.ResolveCalendarLevel(principal, c.Value.Acl));
        var views = new List<EventView>(rows.Count);
        foreach (var ev in rows)
        {
            if (!ev.Times.Overlaps(query.From, query.To, query.ViewerZone) || !calendarsById.TryGetValue(ev.CalendarId, out var calendar))
            {
                continue;
            }

            var view = Resolve(principal, ev, calendar.Calendar, calendar.Acl, calendarLevels[ev.CalendarId], eventOverrides.GetValueOrDefault(ev.Id));
            if (view is not null)
            {
                views.Add(view);
            }
        }

        views.Sort((a, b) => a.Event.StartUtc != b.Event.StartUtc ? a.Event.StartUtc.CompareTo(b.Event.StartUtc) : a.Event.Id.CompareTo(b.Event.Id));
        return new EventWindow(await WithCreatorNamesAsync(views, cancellationToken).ConfigureAwait(false), truncated);
    }

    /// <summary>
    /// The candidate query of a window: live events of <paramref name="calendarIds"/> whose <c>occurs_range</c>
    /// overlaps <c>[from, to)</c>, by start, at most <paramref name="limit"/>. A lateral join per calendar keeps both
    /// columns of the GiST index <c>(calendar_id, occurs_range)</c> usable (GiST cannot search <c>= ANY(array)</c>).
    /// Public for the EXPLAIN test.
    /// </summary>
    public static FormattableString WindowSql(Guid[] calendarIds, Instant from, Instant to, int limit) =>
        $"""
        SELECT e.* FROM unnest({calendarIds}::uuid[]) AS c(id)
        CROSS JOIN LATERAL (
            SELECT * FROM events
            WHERE calendar_id = c.id AND deleted_at IS NULL AND occurs_range && tstzrange({from}, {to}, '[)')
        ) AS e
        ORDER BY e.start_utc, e.id
        LIMIT {limit}
        """;

    private async Task<EventView?> ResolveAsync(PrincipalContext principal, Event ev, Calendar calendar, CalendarAcl acl, CancellationToken cancellationToken)
    {
        var eventOverrides = await OverridesAsync([ev], cancellationToken).ConfigureAwait(false);
        var calendarLevel = PermissionEngine.ResolveCalendarLevel(principal, acl);
        return Resolve(principal, ev, calendar, acl, calendarLevel, eventOverrides.GetValueOrDefault(ev.Id));
    }

    /// <summary>The engine's untraced decision; null for <c>none</c>. <paramref name="calendarLevel"/> only marks "Shared with me".</summary>
    private static EventView? Resolve(
        PrincipalContext principal,
        Event ev,
        Calendar calendar,
        CalendarAcl acl,
        CalendarLevel calendarLevel,
        IReadOnlyList<EventOverride>? eventOverrides)
    {
        var level = EventVisibility.Effective(PermissionEngine.ResolveLevel(principal, acl, ev.ToAcl(eventOverrides)), ev.Transparency);
        if (level == EventLevel.None)
        {
            return null;
        }

        return new EventView(ev, calendar, acl, level, calendarLevel == CalendarLevel.None, null);
    }

    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<EventOverride>>> OverridesAsync(IReadOnlyCollection<Event> events, CancellationToken cancellationToken)
    {
        var withOverrides = events.Where(e => e.HasOverrides).Select(e => e.Id).ToList();
        return withOverrides.Count == 0
            ? new Dictionary<Guid, IReadOnlyList<EventOverride>>()
            : await overrides.ForEventsAsync(withOverrides, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fills <see cref="EventView.CreatorName"/> for views at <c>read</c> or above (one lookup for all creators).</summary>
    private async Task<IReadOnlyList<EventView>> WithCreatorNamesAsync(IReadOnlyList<EventView> views, CancellationToken cancellationToken)
    {
        var creatorIds = views.Where(v => !EventVisibility.IsBusyOnly(v.Level) && v.Event.CreatorUserId is not null)
            .Select(v => v.Event.CreatorUserId!.Value).Distinct().ToList();
        if (creatorIds.Count == 0)
        {
            return views;
        }

        var names = await users.GetAsync(creatorIds, cancellationToken).ConfigureAwait(false);
        return [.. views.Select(v => EventVisibility.IsBusyOnly(v.Level) || v.Event.CreatorUserId is not { } id || !names.TryGetValue(id, out var user)
            ? v
            : v with { CreatorName = user.DisplayName })];
    }
}
