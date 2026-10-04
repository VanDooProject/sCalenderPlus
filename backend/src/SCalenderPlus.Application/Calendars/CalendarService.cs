using Microsoft.EntityFrameworkCore;
using NodaTime;
using SCalenderPlus.Application.Auditing;
using SCalenderPlus.Application.Common;
using SCalenderPlus.Application.Entitlements;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.Application.Events;
using SCalenderPlus.Application.Groups;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Application.Users;
using SCalenderPlus.Core.Calendars;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.Core.Permissions;
using SCalenderPlus.Core.Time;

namespace SCalenderPlus.Application.Calendars;

/// <summary>A calendar as the actor sees it, with their effective level (<c>myLevel</c>).</summary>
public sealed record CalendarView(Calendar Calendar, CalendarLevel MyLevel);

/// <summary>A new calendar: personal (no <see cref="GroupId"/>) or owned by a group.</summary>
/// <param name="RoleDefaults">Group calendars only; default <c>admin → manage, member → contribute, viewer → read</c>.</param>
public sealed record NewCalendar(
    string Name,
    string DefaultTimeZone,
    string? Description = null,
    string? Color = null,
    Guid? GroupId = null,
    RoleDefaultChanges? RoleDefaults = null,
    bool? CreatorsManageOwnEvents = null,
    bool? CreatorsMayShareExternally = null);

/// <summary>Merge-patch of a calendar's settings: <c>null</c> = unchanged; an empty description removes it.</summary>
public sealed record CalendarChanges(
    string? Name = null,
    string? Description = null,
    string? Color = null,
    string? DefaultTimeZone = null,
    bool? CreatorsManageOwnEvents = null,
    bool? CreatorsMayShareExternally = null,
    RoleDefaultChanges? RoleDefaults = null);

/// <summary>Changed group role defaults (§6.2); <c>null</c> = unchanged.</summary>
public sealed record RoleDefaultChanges(CalendarLevel? Admin = null, CalendarLevel? Member = null, CalendarLevel? Viewer = null)
{
    public GroupRoleDefaults ApplyTo(GroupRoleDefaults current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return new GroupRoleDefaults(Admin ?? current.Admin, Member ?? current.Member, Viewer ?? current.Viewer);
    }

    public IEnumerable<CalendarLevel> Levels() =>
        new[] { Admin, Member, Viewer }.Where(l => l is not null).Select(l => l!.Value);
}

/// <summary>
/// Calendars CRUD (issue #41, docs/architecture/permissions.md §4.5, §6.2): anyone creates personal calendars,
/// group admins and owners create group calendars; the actor's level comes from the permission engine through
/// <see cref="CalendarAccessLoader"/> (level <c>none</c> → 404, too low → 403). Managers change settings and
/// role defaults (never above their own level, never locking themselves out), owners delete. Creating counts
/// against the billing subject's <c>owned_calendars</c> in the same transaction (serialized per subject).
/// Every mutation is audited; ACL-relevant changes bump <c>calendars.acl_version</c>; creating, deleting and
/// changing role defaults also bump the owner's (user or group) <c>acl_version</c>, and lowering role defaults
/// revokes the event shares of members who lose level (like lowering a grant, permissions.md §4.6).
/// </summary>
public sealed class CalendarService(
    IAppDbContext db,
    CalendarAccessLoader access,
    AclVersions aclVersions,
    EventQueryService events,
    EventShareRevocation shares,
    IAuditLog audit,
    IEntitlementService entitlements,
    IClock clock)
{
    public async Task<CalendarView> CreateAsync(Guid actorId, NewCalendar request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var calendar = new Calendar
        {
            Id = Guid.CreateVersion7(),
            Name = ValidName(request.Name),
            Description = ValidDescription(request.Description),
            Color = request.Color is null ? Calendar.DefaultColor : ValidColor(request.Color),
            DefaultTimeZone = ValidTimeZone(request.DefaultTimeZone),
            CreatorsManageOwnEvents = request.CreatorsManageOwnEvents ?? true,
            CreatorsMayShareExternally = request.CreatorsMayShareExternally ?? false,
        };
        calendar.CreatedAt = calendar.UpdatedAt = clock.Now();

        Guid billingOwnerId;
        if (request.GroupId is { } groupId)
        {
            var membership = await db.GroupMembers.AsNoTracking()
                .SingleOrDefaultAsync(m => m.GroupId == groupId && m.UserId == actorId, cancellationToken).ConfigureAwait(false)
                ?? throw GroupErrors.GroupNotFound();
            if (!GroupPolicy.Allows(membership.Role, GroupAction.CreateCalendar))
            {
                throw GroupErrors.InsufficientRole(GroupPolicy.RequiredRole(GroupAction.CreateCalendar), membership.Role);
            }

            calendar.OwnerGroupId = groupId;
            calendar.RoleDefaults = request.RoleDefaults?.ApplyTo(GroupRoleDefaults.Default) ?? GroupRoleDefaults.Default;
            billingOwnerId = await db.Groups.AsNoTracking().Where(g => g.Id == groupId).Select(g => g.OwnerUserId)
                .SingleAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (request.RoleDefaults is not null)
            {
                throw Validation.Failed("groupRoleDefaults", "Role defaults apply to group calendars only.");
            }

            calendar.OwnerUserId = actorId;
            billingOwnerId = actorId;
        }

        var principal = await access.PrincipalAsync(actorId, cancellationToken).ConfigureAwait(false);
        var level = PermissionEngine.ResolveCalendarLevel(principal, calendar.ToAcl([]));
        if (level < CalendarLevel.Manage)
        {
            throw CalendarErrors.SelfLockout(); // e.g. an admin creating with admin → read
        }

        return await db.InTransactionAsync(async ct =>
        {
            // Serializes concurrent creations of the subject, so the count below stays true until the commit.
            await db.LockAsync(billingOwnerId, ct).ConfigureAwait(false);
            var owned = await CountOwnedAsync(billingOwnerId, ct).ConfigureAwait(false);
            await entitlements.EnsureCanCreateCalendarAsync(billingOwnerId, owned, ct).ConfigureAwait(false);

            db.Calendars.Add(calendar);
            await aclVersions.BumpPrincipalsAsync([calendar.Owner], ct).ConfigureAwait(false); // the owner's (group members') calendars changed
            audit.Record(CalendarAuditActions.Created, CalendarAuditActions.ResourceType, calendar.Id.ToString(), null, CalendarAudit.State(calendar), billingOwnerId);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return new CalendarView(calendar, level);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <exception cref="AppException"><c>not_found</c> unless the actor's level is at least <c>free_busy</c>.</exception>
    public async Task<CalendarView> GetAsync(Guid actorId, Guid calendarId, CancellationToken cancellationToken = default)
    {
        var loaded = await access.RequireAsync(actorId, calendarId, CalendarAction.View, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new CalendarView(loaded.Calendar, loaded.Level);
    }

    /// <summary>Every calendar the actor sees (owned, through grants, through group membership) with their level, by creation.</summary>
    public async Task<Page<CalendarView>> ListMineAsync(Guid actorId, PageRequest page, CancellationToken cancellationToken = default)
    {
        var visible = await access.ListVisibleAsync(actorId, page, cancellationToken).ConfigureAwait(false);
        return new Page<CalendarView>([.. visible.Items.Select(v => new CalendarView(v.Calendar, v.Level))], visible.NextCursor);
    }

    /// <param name="precondition">Checks If-Match against the current state (after authorization); throws to refuse.</param>
    /// <param name="revokeEventShares">When role defaults are lowered: also revoke the individual event shares of the members who lose level (permissions.md §4.6).</param>
    public Task<CalendarView> UpdateAsync(
        Guid actorId,
        Guid calendarId,
        CalendarChanges changes,
        Action<CalendarView>? precondition = null,
        bool revokeEventShares = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        return db.InTransactionAsync(ct => UpdateInTransactionAsync(actorId, calendarId, changes, precondition, revokeEventShares, ct), cancellationToken);
    }

    private async Task<CalendarView> UpdateInTransactionAsync(Guid actorId, Guid calendarId, CalendarChanges changes, Action<CalendarView>? precondition, bool revokeEventShares, CancellationToken cancellationToken)
    {
        var loaded = await access.RequireAsync(actorId, calendarId, CalendarAction.UpdateSettings, forUpdate: true, cancellationToken).ConfigureAwait(false);
        var calendar = loaded.Calendar;
        precondition?.Invoke(new CalendarView(calendar, loaded.Level));
        if (calendar.FrozenAt is not null)
        {
            throw CalendarErrors.Frozen();
        }

        var before = CalendarAudit.State(calendar);
        calendar.Name = changes.Name is null ? calendar.Name : ValidName(changes.Name);
        calendar.Description = changes.Description is null ? calendar.Description : ValidDescription(changes.Description);
        calendar.Color = changes.Color is null ? calendar.Color : ValidColor(changes.Color);
        calendar.DefaultTimeZone = changes.DefaultTimeZone is null ? calendar.DefaultTimeZone : ValidTimeZone(changes.DefaultTimeZone);
        calendar.CreatorsManageOwnEvents = changes.CreatorsManageOwnEvents ?? calendar.CreatorsManageOwnEvents;
        calendar.CreatorsMayShareExternally = changes.CreatorsMayShareExternally ?? calendar.CreatorsMayShareExternally;
        if (changes.RoleDefaults is { } roleDefaults)
        {
            if (!calendar.IsGroupOwned)
            {
                throw Validation.Failed("groupRoleDefaults", "Role defaults apply to group calendars only.");
            }

            if (roleDefaults.Levels().Any(l => !AccessPolicy.CanSetRoleDefault(loaded.Level, l)))
            {
                throw Validation.Failed("groupRoleDefaults", $"Role defaults are none … manage and at most your own level ({PermissionLevels.Format(loaded.Level)}).");
            }

            calendar.RoleDefaults = roleDefaults.ApplyTo(calendar.RoleDefaults);
        }

        var after = CalendarAudit.State(calendar);
        var level = PermissionEngine.ResolveCalendarLevel(loaded.Principal, calendar.ToAcl(loaded.Grants));
        if (after == before)
        {
            return new CalendarView(calendar, level);
        }

        if (level < CalendarLevel.Manage)
        {
            throw CalendarErrors.SelfLockout();
        }

        if (before.AclRelevant != after.AclRelevant)
        {
            calendar.AclVersion++;
        }

        calendar.UpdatedAt = clock.Now();
        audit.Record(CalendarAuditActions.Updated, CalendarAuditActions.ResourceType, calendar.Id.ToString(), before, after, await BillingSubjectAsync(calendar, cancellationToken).ConfigureAwait(false));
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        if (before.AclRelevant.GroupRoleDefaults != after.AclRelevant.GroupRoleDefaults)
        {
            // The members' levels changed like through a grant to the owning group (acl_version, lifecycle §4.6);
            // after the save above: revocations bump the calendar row themselves.
            await aclVersions.BumpPrincipalsAsync([calendar.Owner], cancellationToken).ConfigureAwait(false);
            if (revokeEventShares)
            {
                await shares.OnRoleDefaultsChangedAsync(loaded.Acl, calendar.ToAcl(loaded.Grants), calendar.OwnerGroupId!.Value, cancellationToken).ConfigureAwait(false);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        return new CalendarView(calendar, level);
    }

    /// <summary>
    /// Deletes the calendar for real with its grants and events (owners only: the owning user, or role-owners of the
    /// owning group). The <c>acl_version</c> of the owner and of every user and group named by a grant or by an
    /// override of its events is bumped.
    /// </summary>
    public async Task DeleteAsync(Guid actorId, Guid calendarId, Action<CalendarView>? precondition = null, CancellationToken cancellationToken = default)
    {
        await db.InTransactionAsync(async ct =>
        {
            var loaded = await access.RequireAsync(actorId, calendarId, CalendarAction.Delete, forUpdate: true, ct).ConfigureAwait(false);
            precondition?.Invoke(new CalendarView(loaded.Calendar, loaded.Level));

            // Everyone whose calendars or "Shared with me" lose something: grantees, the owner, people its events name.
            var overridePrincipals = await events.OverridePrincipalsAsync(calendarId, ct).ConfigureAwait(false);
            await aclVersions.BumpPrincipalsAsync([.. loaded.Grants.Select(g => g.Principal), loaded.Calendar.Owner, .. overridePrincipals], ct).ConfigureAwait(false);
            audit.Record(
                CalendarAuditActions.Deleted,
                CalendarAuditActions.ResourceType,
                calendarId.ToString(),
                CalendarAudit.State(loaded.Calendar) with { GrantCount = loaded.Grants.Count },
                null,
                await BillingSubjectAsync(loaded.Calendar, ct).ConfigureAwait(false));
            db.CalendarGrants.RemoveRange(loaded.Grants);
            db.Calendars.Remove(loaded.Calendar);
            await SaveAsync(ct).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Calendars whose plan subject is <paramref name="billingOwnerId"/>: their own and those of groups they bill.</summary>
    private Task<int> CountOwnedAsync(Guid billingOwnerId, CancellationToken cancellationToken) =>
        db.Calendars.CountAsync(
            c => c.OwnerUserId == billingOwnerId || db.Groups.Any(g => g.Id == c.OwnerGroupId && g.OwnerUserId == billingOwnerId),
            cancellationToken);

    private Task<Guid> BillingSubjectAsync(Calendar calendar, CancellationToken cancellationToken) =>
        CalendarAudit.BillingSubjectAsync(db, calendar, cancellationToken);

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw CalendarErrors.Changed();
        }
    }

    private static string ValidName(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        return trimmed.Length is >= 1 and <= Calendar.NameMaxLength
            ? trimmed
            : throw Validation.Failed("name", $"The name must have 1 to {Calendar.NameMaxLength} characters.");
    }

    private static string? ValidDescription(string? description)
    {
        var trimmed = description?.Trim();
        return trimmed?.Length > Calendar.DescriptionMaxLength
            ? throw Validation.Failed("description", $"The description must have at most {Calendar.DescriptionMaxLength} characters.")
            : string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string ValidColor(string color) =>
        Calendar.IsValidColor(color)
            ? color.ToLowerInvariant()
            : throw Validation.Failed("color", "Use a hex color like #4f46e5.");

    /// <summary><c>422 time_zone_invalid</c> with <c>errors.defaultTimeZone</c> for unknown IANA ids.</summary>
    private static string ValidTimeZone(string? timeZone) =>
        TimeZoneIds.IsValid(timeZone) && timeZone!.Length <= Calendar.TimeZoneMaxLength
            ? timeZone
            : throw new AppException(
                ErrorCodes.TimeZoneInvalid,
                $"'{timeZone}' is not a known IANA time zone id (e.g. Europe/Berlin).",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [Validation.ErrorsMember] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                    {
                        ["defaultTimeZone"] = ["Use an IANA time zone id such as Europe/Berlin."],
                    },
                });
}

/// <summary>Audit snapshots of calendars and the billing subject they are recorded for.</summary>
internal static class CalendarAudit
{
    public static CalendarState State(Calendar calendar) =>
        new(
            calendar.Name,
            calendar.Description,
            calendar.Color,
            calendar.DefaultTimeZone,
            calendar.Owner.ToString(),
            new AclSettings(
                calendar.CreatorsManageOwnEvents,
                calendar.CreatorsMayShareExternally,
                calendar.IsGroupOwned ? RoleDefaults(calendar.RoleDefaults) : null),
            null);

    public static RoleDefaultsState RoleDefaults(GroupRoleDefaults defaults) =>
        new(PermissionLevels.Format(defaults.Admin), PermissionLevels.Format(defaults.Member), PermissionLevels.Format(defaults.Viewer));

    /// <summary>The plan subject: the owning user, or the owning group's billing owner.</summary>
    public static async Task<Guid> BillingSubjectAsync(IAppDbContext db, Calendar calendar, CancellationToken cancellationToken) =>
        calendar.OwnerGroupId is { } groupId
            ? await db.Groups.AsNoTracking().Where(g => g.Id == groupId).Select(g => g.OwnerUserId).SingleAsync(cancellationToken).ConfigureAwait(false)
            : calendar.OwnerUserId!.Value;

    public sealed record CalendarState(string Name, string? Description, string Color, string DefaultTimeZone, string Owner, AclSettings AclRelevant, int? GrantCount);

    public sealed record AclSettings(bool CreatorsManageOwnEvents, bool CreatorsMayShareExternally, RoleDefaultsState? GroupRoleDefaults);

    public sealed record RoleDefaultsState(string Admin, string Member, string Viewer);
}

/// <summary>
/// Permission-cache invalidation (permissions.md §8): <c>calendars.acl_version</c>, and <c>groups</c>/<c>users.acl_version</c>
/// of the principals a grant (or, from M2-D, an override) names. Executes right away: call it inside
/// <c>IAppDbContext.InTransactionAsync</c> so it commits with the change.
/// </summary>
public sealed class AclVersions(IAppDbContext db, IUserDirectory users)
{
    public Task BumpCalendarAsync(Guid calendarId, CancellationToken cancellationToken = default) =>
        db.Calendars.Where(c => c.Id == calendarId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.AclVersion, c => c.AclVersion + 1), cancellationToken);

    public async Task BumpPrincipalsAsync(IReadOnlyCollection<Principal> principals, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principals);
        var groupIds = principals.Where(p => p.Type == PrincipalType.Group).Select(p => p.Id!.Value).Distinct().ToList();
        if (groupIds.Count > 0)
        {
            await db.Groups.Where(g => groupIds.Contains(g.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.AclVersion, g => g.AclVersion + 1), cancellationToken).ConfigureAwait(false);
        }

        await users.BumpAclVersionAsync([.. principals.Where(p => p.Type == PrincipalType.User).Select(p => p.Id!.Value)], cancellationToken).ConfigureAwait(false);
    }
}
