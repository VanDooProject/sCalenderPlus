using Microsoft.EntityFrameworkCore;
using SCalenderPlus.Application.Calendars;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Application.Users;
using SCalenderPlus.Core.Events;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Application.Events;

/// <summary>
/// Why a user has their level on an event: the engine's trace (<see cref="PermissionEngine.Resolve"/>) as is, the
/// engine's level and the effective one (a transparent event seen at <c>free_busy</c> is hidden,
/// <see cref="HiddenAsTransparent"/>), and the names of the users and groups the steps mention.
/// </summary>
public sealed record AccessExplanation(
    Event Event,
    Guid UserId,
    string? UserName,
    EventAccess Access,
    EventLevel Level,
    bool HiddenAsTransparent,
    IReadOnlyDictionary<Guid, string> Names);

/// <summary>
/// The access explainer (issue #47, permissions.md §8 "Explainability", <c>GET /events/{id}/access/explain</c>).
/// Everyone who sees an event may explain their own level. Explaining another user's level needs calendar
/// <c>manage</c> (or <c>owner</c>) on the event's calendar — the people who see its grants and overrides anyway —
/// and the user must be someone the caller can see: in the calendar's audience (level ≥ <c>free_busy</c>), named
/// by a <c>user</c> override of the event, or sharing a group with the caller; anyone else (also unknown ids)
/// answers <c>404</c>, so the endpoint reveals neither accounts nor memberships outside the caller's view. The
/// steps only mention what matched the explained user (their grants, their groups' entries, the owner group).
/// </summary>
public sealed class EventAccessExplainer(
    IAppDbContext db,
    CalendarAccessLoader calendars,
    EventQueryService queries,
    IEventOverrideSource overrides,
    IUserDirectory users)
{
    /// <param name="userId">The user to explain; null = the caller.</param>
    public async Task<AccessExplanation> ExplainAsync(Guid actorId, Guid eventId, Guid? userId = null, CancellationToken cancellationToken = default)
    {
        var view = await queries.GetAsync(actorId, eventId, cancellationToken: cancellationToken).ConfigureAwait(false);
        var ev = view.Event;
        var eventOverrides = ev.HasOverrides
            ? (await overrides.ForEventsAsync([ev.Id], cancellationToken).ConfigureAwait(false)).GetValueOrDefault(ev.Id) ?? []
            : [];
        var target = userId ?? actorId;
        if (target != actorId)
        {
            var actor = await calendars.PrincipalAsync(actorId, cancellationToken).ConfigureAwait(false);
            var actorLevel = PermissionEngine.ResolveCalendarLevel(actor, view.Acl);
            if (actorLevel < CalendarLevel.Manage)
            {
                throw CalendarErrors.InsufficientLevel(CalendarLevel.Manage, actorLevel);
            }

            await EnsureVisibleAsync(actor, target, view.Acl, eventOverrides, cancellationToken).ConfigureAwait(false);
        }

        var principal = await calendars.PrincipalAsync(target, cancellationToken).ConfigureAwait(false);
        var access = PermissionEngine.Resolve(principal, view.Acl, ev.ToAcl(eventOverrides));
        var level = EventVisibility.Effective(access.Level, ev.Transparency);
        var names = await NamesAsync(target, access.Steps, cancellationToken).ConfigureAwait(false);
        return new AccessExplanation(ev, target, names.GetValueOrDefault(target), access, level, level != access.Level, names);
    }

    private async Task EnsureVisibleAsync(PrincipalContext actor, Guid target, CalendarAcl acl, IReadOnlyList<EventOverride> eventOverrides, CancellationToken cancellationToken)
    {
        var principal = await calendars.PrincipalAsync(target, cancellationToken).ConfigureAwait(false);
        var visible = PermissionEngine.ResolveCalendarLevel(principal, acl) >= CalendarLevel.FreeBusy
            || eventOverrides.Any(o => o.Principal == Principal.User(target))
            || principal.Groups.Keys.Any(actor.Groups.ContainsKey);
        if (!visible || (await users.GetAsync([target], cancellationToken).ConfigureAwait(false)).Count == 0)
        {
            throw new AppException(ErrorCodes.NotFound, "User not found.");
        }
    }

    private async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(Guid target, IReadOnlyList<ResolutionStep> steps, CancellationToken cancellationToken)
    {
        var principals = steps.Select(s => s.Principal).OfType<Principal>().ToList();
        var userIds = principals.Where(p => p.Type == PrincipalType.User).Select(p => p.Id!.Value).Append(target).Distinct().ToList();
        var groupIds = principals.Where(p => p.Type == PrincipalType.Group).Select(p => p.Id!.Value).Distinct().ToList();
        var people = await users.GetAsync(userIds, cancellationToken).ConfigureAwait(false);
        var names = people.ToDictionary(p => p.Key, p => p.Value.DisplayName);
        if (groupIds.Count > 0)
        {
            var groups = await db.Groups.AsNoTracking().Where(g => groupIds.Contains(g.Id))
                .Select(g => new { g.Id, g.Name }).ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var group in groups)
            {
                names[group.Id] = group.Name;
            }
        }

        return names;
    }
}
