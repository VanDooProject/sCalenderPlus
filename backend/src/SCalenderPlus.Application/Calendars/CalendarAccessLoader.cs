using Microsoft.EntityFrameworkCore;
using SCalenderPlus.Application.Common;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Core.Calendars;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Application.Calendars;

/// <summary>A calendar as the actor sees it: the row, its grants, the engine's ACL and the actor's level.</summary>
/// <param name="Grants">The calendar's grants (tracked when loaded for an update).</param>
public sealed record CalendarAccess(Calendar Calendar, IReadOnlyList<CalendarGrantEntry> Grants, CalendarAcl Acl, PrincipalContext Principal, CalendarLevel Level);

/// <summary>
/// The single place that turns stored rows into the pure permission engine's inputs (docs/architecture/
/// permissions.md §8): <see cref="PrincipalContext"/> from the actor's group memberships, <see cref="CalendarAcl"/>
/// from a calendar and its grants. Use cases ask it for the actor's level (<see cref="RequireAsync"/>) instead of
/// deciding access themselves; listings load in batch (<see cref="ListVisibleAsync"/>: one query for the
/// memberships, then per page one for calendars and one for their grants — no query per calendar). Events resolve
/// with the same principal and calendar ACLs (<c>Events.EventQueryService</c>, <see cref="FindAclAsync"/>,
/// <see cref="VisibleAsync"/>).
/// </summary>
public sealed class CalendarAccessLoader(IAppDbContext db)
{
    private readonly Dictionary<Guid, PrincipalContext> _principals = [];

    /// <summary>The user with their group memberships (cached for the scope, i.e. the request).</summary>
    public async Task<PrincipalContext> PrincipalAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (_principals.TryGetValue(userId, out var cached))
        {
            return cached;
        }

        var groups = await db.GroupMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .ToDictionaryAsync(m => m.GroupId, m => m.Role, cancellationToken).ConfigureAwait(false);
        var principal = PrincipalContext.ForUser(userId, groups);
        _principals[userId] = principal;
        return principal;
    }

    /// <summary>Forgets cached principals (after a membership change within the same scope).</summary>
    public void Reset() => _principals.Clear();

    /// <summary>The ACLs of <paramref name="calendars"/>, with their grants loaded in one query.</summary>
    public async Task<IReadOnlyDictionary<Guid, CalendarAcl>> AclsAsync(IReadOnlyCollection<Calendar> calendars, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(calendars);
        if (calendars.Count == 0)
        {
            return new Dictionary<Guid, CalendarAcl>();
        }

        var ids = calendars.Select(c => c.Id).ToList();
        var grants = await db.CalendarGrants.AsNoTracking()
            .Where(g => ids.Contains(g.CalendarId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var byCalendar = grants.ToLookup(g => g.CalendarId);
        return calendars.ToDictionary(c => c.Id, c => c.ToAcl(byCalendar[c.Id]));
    }

    /// <summary>
    /// The calendar (untracked) and its ACL without any level check, or null when it does not exist: events resolve
    /// against it, and event overrides (M2-D) may reach people whose calendar level is <c>none</c>.
    /// </summary>
    public async Task<(Calendar Calendar, CalendarAcl Acl)?> FindAclAsync(Guid calendarId, CancellationToken cancellationToken = default)
    {
        var calendar = await db.Calendars.AsNoTracking().SingleOrDefaultAsync(c => c.Id == calendarId, cancellationToken).ConfigureAwait(false);
        if (calendar is null)
        {
            return null;
        }

        var acls = await AclsAsync([calendar], cancellationToken).ConfigureAwait(false);
        return (calendar, acls[calendar.Id]);
    }

    /// <summary>
    /// Every calendar the principal sees (level ≥ <c>free_busy</c>), unpaged, with its ACL and the level — the first
    /// step of event window queries (permissions.md §8). <paramref name="only"/> restricts the candidates to these ids.
    /// Two queries: candidate calendars and their grants (the principal is cached).
    /// </summary>
    public async Task<IReadOnlyList<(Calendar Calendar, CalendarAcl Acl, CalendarLevel Level)>> VisibleAsync(
        PrincipalContext principal,
        IReadOnlyCollection<Guid>? only = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var candidates = Candidates(principal);
        if (only is not null)
        {
            var ids = only.ToList();
            candidates = candidates.Where(c => ids.Contains(c.Id));
        }

        var calendars = await candidates.OrderBy(c => c.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var acls = await AclsAsync(calendars, cancellationToken).ConfigureAwait(false);
        var visible = new List<(Calendar, CalendarAcl, CalendarLevel)>(calendars.Count);
        foreach (var calendar in calendars)
        {
            var acl = acls[calendar.Id];
            var level = PermissionEngine.ResolveCalendarLevel(principal, acl);
            if (level != CalendarLevel.None)
            {
                visible.Add((calendar, acl, level));
            }
        }

        return visible;
    }

    /// <summary>The calendar and the actor's level on it; <c>404</c> for unknown calendars and level <c>none</c>.</summary>
    /// <param name="forUpdate">Track the calendar and its grants for changes.</param>
    public async Task<CalendarAccess> LoadAsync(Guid actorId, Guid calendarId, bool forUpdate = false, CancellationToken cancellationToken = default)
    {
        var calendars = forUpdate ? db.Calendars : db.Calendars.AsNoTracking();
        var calendar = await calendars.SingleOrDefaultAsync(c => c.Id == calendarId, cancellationToken).ConfigureAwait(false)
            ?? throw CalendarErrors.CalendarNotFound();
        var grantRows = forUpdate ? db.CalendarGrants : db.CalendarGrants.AsNoTracking();
        var grants = await grantRows.Where(g => g.CalendarId == calendarId).OrderBy(g => g.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var principal = await PrincipalAsync(actorId, cancellationToken).ConfigureAwait(false);
        var acl = calendar.ToAcl(grants);
        var level = PermissionEngine.ResolveCalendarLevel(principal, acl);
        return level == CalendarLevel.None
            ? throw CalendarErrors.CalendarNotFound()
            : new CalendarAccess(calendar, grants, acl, principal, level);
    }

    /// <summary>
    /// Like <see cref="LoadAsync"/>, and the actor's level must allow <paramref name="action"/>
    /// (<see cref="AccessPolicy.Check(CalendarLevel, CalendarAction)"/>): <c>404</c> for level <c>none</c>,
    /// <c>403 insufficient_permission</c> (with <c>required</c> and <c>actual</c>) when too low.
    /// </summary>
    public async Task<CalendarAccess> RequireAsync(Guid actorId, Guid calendarId, CalendarAction action, bool forUpdate = false, CancellationToken cancellationToken = default)
    {
        var access = await LoadAsync(actorId, calendarId, forUpdate, cancellationToken).ConfigureAwait(false);
        return AccessPolicy.Check(access.Level, action) switch
        {
            AccessCheck.Allowed => access,
            _ => throw CalendarErrors.InsufficientLevel(AccessPolicy.RequiredLevel(action), access.Level),
        };
    }

    /// <summary>
    /// The calendars the actor sees (level ≥ <c>free_busy</c>) with their level, ordered by id (creation order).
    /// Candidates come from SQL (owned by the actor or one of their groups, or granted to them or one of their
    /// groups); the engine decides, so a calendar whose role default or grant <c>minRole</c> gives the actor
    /// nothing is left out. Pages are filled across candidate batches.
    /// </summary>
    public async Task<Page<(Calendar Calendar, CalendarLevel Level)>> ListVisibleAsync(Guid actorId, PageRequest page, CancellationToken cancellationToken = default)
    {
        var principal = await PrincipalAsync(actorId, cancellationToken).ConfigureAwait(false);
        var visible = new List<(Calendar Calendar, CalendarLevel Level)>();
        var after = page.After;
        while (true)
        {
            var candidates = Candidates(principal);
            if (after is { } last)
            {
                candidates = candidates.Where(c => c.Id > last);
            }

            var batch = await candidates.OrderBy(c => c.Id).Take(page.Limit + 1)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var acls = await AclsAsync(batch, cancellationToken).ConfigureAwait(false);
            foreach (var calendar in batch)
            {
                var level = PermissionEngine.ResolveCalendarLevel(principal, acls[calendar.Id]);
                if (level != CalendarLevel.None)
                {
                    visible.Add((calendar, level));
                    if (visible.Count > page.Limit)
                    {
                        return page.ToPage(visible, v => v.Calendar.Id);
                    }
                }
            }

            if (batch.Count <= page.Limit)
            {
                return page.ToPage(visible, v => v.Calendar.Id);
            }

            after = batch[^1].Id;
        }
    }

    /// <summary>Calendars that may be visible to <paramref name="principal"/> (a superset; the engine decides).</summary>
    private IQueryable<Calendar> Candidates(PrincipalContext principal)
    {
        var userId = principal.UserId ?? throw new ArgumentException("Listings are for signed-in users.", nameof(principal));
        var groupIds = principal.Groups.Keys.ToList();
        return db.Calendars.AsNoTracking().Where(c =>
            c.OwnerUserId == userId
            || (c.OwnerGroupId != null && groupIds.Contains(c.OwnerGroupId.Value))
            || db.CalendarGrants.Any(g => g.CalendarId == c.Id
                && ((g.PrincipalType == PrincipalType.User && g.PrincipalId == userId)
                    || (g.PrincipalType == PrincipalType.Group && groupIds.Contains(g.PrincipalId)))));
    }
}
