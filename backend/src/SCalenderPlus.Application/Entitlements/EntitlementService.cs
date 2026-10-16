using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NodaTime;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Application.Users;
using SCalenderPlus.Core.Entitlements;

namespace SCalenderPlus.Application.Entitlements;

/// <summary>
/// Plan limits from configuration (<see cref="PlansOptions"/>). Plan resolution until billing lands
/// (<see cref="PlanResolution"/>): <c>Billing__Provider=none</c> → <see cref="Plan.SelfHost"/>; otherwise
/// <see cref="Plan.Pro"/> during the reverse trial of the billing owner's account (for a group's resources: of the
/// older of account and group, <see cref="PlanSubject"/>), <see cref="Plan.Free"/> after it.
/// </summary>
internal sealed class EntitlementService(
    IAppDbContext db,
    IUserDirectory users,
    IClock clock,
    IOptions<BillingOptions> billing,
    IOptions<PlansOptions> plans) : IEntitlementService
{
    public Task<PlanEntitlements> GetAsync(Guid billingOwnerId, CancellationToken cancellationToken = default) =>
        GetAsync(PlanSubject.User(billingOwnerId), cancellationToken);

    public async Task<PlanEntitlements> GetAsync(PlanSubject subject, CancellationToken cancellationToken = default)
    {
        var provider = billing.Value.Provider;
        var options = plans.Value;
        Instant? anchor = null;
        if (PlanResolution.HasTrial(provider, options))
        {
            var ownerCreatedAt = await users.CreatedAtAsync(subject.BillingOwnerId, cancellationToken).ConfigureAwait(false);
            var groupCreatedAt = subject.GroupId is { } groupId
                ? await db.Groups.AsNoTracking().Where(g => g.Id == groupId).Select(g => (Instant?)g.CreatedAt)
                    .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                : null;
            anchor = PlanResolution.TrialAnchor(ownerCreatedAt, groupCreatedAt);
        }

        return PlanResolution.Resolve(provider, options, anchor, clock.GetCurrentInstant());
    }

    public async Task EnsureCanCreateGroupAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var entitlements = await GetAsync(userId, cancellationToken).ConfigureAwait(false);
        if (entitlements.Limits.OwnedGroups is null)
        {
            return;
        }

        var owned = await db.Groups.CountAsync(g => g.OwnerUserId == userId, cancellationToken).ConfigureAwait(false);
        Throw(entitlements, PlanLimits.CheckAdd(PlanLimit.OwnedGroups, entitlements.Limits.OwnedGroups, owned));
    }

    public async Task EnsureCanAddGroupMemberAsync(Guid groupId, Guid billingOwnerId, CancellationToken cancellationToken = default)
    {
        var entitlements = await GetAsync(PlanSubject.Group(groupId, billingOwnerId), cancellationToken).ConfigureAwait(false);
        if (entitlements.Limits.MembersPerGroup is null)
        {
            return;
        }

        var members = await db.GroupMembers.CountAsync(m => m.GroupId == groupId, cancellationToken).ConfigureAwait(false);
        Throw(entitlements, PlanLimits.CheckAdd(PlanLimit.MembersPerGroup, entitlements.Limits.MembersPerGroup, members));
    }

    public async Task EnsureCanCreateCalendarAsync(PlanSubject subject, int ownedCalendars, CancellationToken cancellationToken = default)
    {
        var entitlements = await GetAsync(subject, cancellationToken).ConfigureAwait(false);
        Throw(entitlements, PlanLimits.CheckAdd(PlanLimit.OwnedCalendars, entitlements.Limits.OwnedCalendars, ownedCalendars));
    }

    public async Task EnsureCanChangeEventOverridesAsync(PlanSubject subject, OverrideUsage usage, CancellationToken cancellationToken = default)
    {
        var entitlements = await GetAsync(subject, cancellationToken).ConfigureAwait(false);
        Throw(entitlements, PlanLimits.CheckOverrideChange(entitlements.Limits, usage));
    }

    private static void Throw(PlanEntitlements entitlements, PlanLimitExceeded? exceeded)
    {
        if (exceeded is not null)
        {
            throw EntitlementErrors.LimitReached(entitlements.Plan, exceeded);
        }
    }
}

/// <summary>
/// Which plan a billing subject has until billing lands (docs/product/plans.md): self-host without a billing
/// provider; otherwise the reverse trial — <see cref="Plan.Pro"/> for <see cref="PlansOptions.TrialDays"/> days from
/// the account's creation — then <see cref="Plan.Free"/>.
/// </summary>
public static class PlanResolution
{
    /// <summary>Whether plans depend on the account's age (a billing provider and a trial of at least one day).</summary>
    public static bool HasTrial(BillingProvider provider, PlansOptions plans)
    {
        ArgumentNullException.ThrowIfNull(plans);
        return provider != BillingProvider.None && plans.TrialDays > 0;
    }

    /// <summary>The trial's end for an account created at <paramref name="createdAt"/>; null without a trial.</summary>
    public static Instant? TrialEndsAt(BillingProvider provider, PlansOptions plans, Instant? createdAt) =>
        HasTrial(provider, plans) && createdAt is { } created ? created + Duration.FromDays(plans.TrialDays) : null;

    /// <summary>
    /// When the trial of a plan subject starts: the billing owner's account creation, or for a group's resources the
    /// earlier of it and the group's creation — a billing transfer to a new account must not restart the group's
    /// trial. Null (no trial) when the owner's account is unknown.
    /// </summary>
    public static Instant? TrialAnchor(Instant? ownerCreatedAt, Instant? groupCreatedAt) =>
        ownerCreatedAt is { } owner && groupCreatedAt is { } group && group < owner ? group : ownerCreatedAt;

    /// <param name="createdAt">Start of the trial: the billing owner's account creation, for a group's resources <see cref="TrialAnchor"/> (null: unknown, e.g. deleted — no trial).</param>
    public static PlanEntitlements Resolve(BillingProvider provider, PlansOptions plans, Instant? createdAt, Instant now)
    {
        ArgumentNullException.ThrowIfNull(plans);
        if (provider == BillingProvider.None)
        {
            return new(Plan.SelfHost, plans.For(Plan.SelfHost).ToValues());
        }

        return TrialEndsAt(provider, plans, createdAt) is { } ends && now < ends
            ? new(Plan.Pro, plans.For(Plan.Pro).ToValues(), ends)
            : new(Plan.Free, plans.For(Plan.Free).ToValues());
    }
}

/// <summary>The problem of a refused change: <c>402 plan_limit_reached</c> with <c>limit: { key, max, used }</c> and <c>plan</c>.</summary>
public static class EntitlementErrors
{
    public static AppException LimitReached(Plan plan, PlanLimitExceeded exceeded)
    {
        ArgumentNullException.ThrowIfNull(exceeded);
        return new(ErrorCodes.PlanLimitReached, Detail(exceeded), new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["limit"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["key"] = exceeded.Key,
                ["max"] = exceeded.Max,
                ["used"] = exceeded.Used,
            },
            ["plan"] = PlanName(plan),
        });
    }

    public static string PlanName(Plan plan) => plan switch
    {
        Plan.Free => "free",
        Plan.Pro => "pro",
        Plan.Team => "team",
        Plan.SelfHost => "selfhost",
        _ => throw new ArgumentOutOfRangeException(nameof(plan), plan, "Unknown plan."),
    };

    private static string Detail(PlanLimitExceeded exceeded) => exceeded.Limit switch
    {
        PlanLimit.OwnedCalendars => $"Your plan allows {exceeded.Max} calendars.",
        PlanLimit.OwnedGroups => $"Your plan allows {exceeded.Max} own groups.",
        PlanLimit.MembersPerGroup => $"This group's plan allows {exceeded.Max} members.",
        PlanLimit.EventsWithOverrides => $"This calendar's plan allows {exceeded.Max} upcoming events with custom permissions.",
        _ => $"This calendar's plan allows {exceeded.Max} permission entries per event.",
    };
}
