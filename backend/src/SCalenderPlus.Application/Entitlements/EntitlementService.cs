using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.Application.Persistence;
using SCalenderPlus.Core.Entitlements;

namespace SCalenderPlus.Application.Entitlements;

/// <summary>
/// Plan limits from configuration (<see cref="PlansOptions"/>). Plan resolution until billing lands:
/// <c>Billing__Provider=none</c> → <see cref="Plan.SelfHost"/>, otherwise <see cref="Plan.Free"/> for everyone.
/// </summary>
internal sealed class EntitlementService(IAppDbContext db, IOptions<BillingOptions> billing, IOptions<PlansOptions> plans) : IEntitlementService
{
    public Task<PlanEntitlements> GetAsync(Guid billingOwnerId, CancellationToken cancellationToken = default)
    {
        var plan = billing.Value.Provider == BillingProvider.None ? Plan.SelfHost : Plan.Free;
        return Task.FromResult(new PlanEntitlements(plan, plans.Value.For(plan).ToValues()));
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
        var entitlements = await GetAsync(billingOwnerId, cancellationToken).ConfigureAwait(false);
        if (entitlements.Limits.MembersPerGroup is null)
        {
            return;
        }

        var members = await db.GroupMembers.CountAsync(m => m.GroupId == groupId, cancellationToken).ConfigureAwait(false);
        Throw(entitlements, PlanLimits.CheckAdd(PlanLimit.MembersPerGroup, entitlements.Limits.MembersPerGroup, members));
    }

    public async Task EnsureCanCreateCalendarAsync(Guid billingOwnerId, int ownedCalendars, CancellationToken cancellationToken = default)
    {
        var entitlements = await GetAsync(billingOwnerId, cancellationToken).ConfigureAwait(false);
        Throw(entitlements, PlanLimits.CheckAdd(PlanLimit.OwnedCalendars, entitlements.Limits.OwnedCalendars, ownedCalendars));
    }

    public async Task EnsureCanChangeEventOverridesAsync(Guid billingOwnerId, OverrideUsage usage, CancellationToken cancellationToken = default)
    {
        var entitlements = await GetAsync(billingOwnerId, cancellationToken).ConfigureAwait(false);
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
