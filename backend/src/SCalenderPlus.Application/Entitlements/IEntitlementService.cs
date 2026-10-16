using NodaTime;
using SCalenderPlus.Core.Entitlements;

namespace SCalenderPlus.Application.Entitlements;

/// <summary>The plan of a billing subject and its limits.</summary>
/// <param name="TrialEndsAt">End of the reverse trial while it runs (the plan is then <see cref="Plan.Pro"/>); null otherwise.</param>
public sealed record PlanEntitlements(Plan Plan, PlanLimitValues Limits, Instant? TrialEndsAt = null);

/// <summary>
/// Whose plan a limit is evaluated for (plans.md principle 2): the billing owner, and — for a group's resources
/// (group calendars, members, overrides in group calendars) — the group. A group's reverse trial counts from the
/// earlier of the billing owner's and the group's creation, so transferring billing to a new account does not
/// restart it; personal resources (<see cref="GroupId"/> null) count from the owner's account alone.
/// </summary>
/// <param name="BillingOwnerId">The user whose plan governs (<c>groups.owner_user_id</c> for a group).</param>
/// <param name="GroupId">The group the limit is evaluated on behalf of; null for the owner's personal resources.</param>
public readonly record struct PlanSubject(Guid BillingOwnerId, Guid? GroupId = null)
{
    /// <summary>A user's personal resources.</summary>
    public static PlanSubject User(Guid userId) => new(userId);

    /// <summary>The resources of <paramref name="groupId"/>, billed to <paramref name="billingOwnerId"/>.</summary>
    public static PlanSubject Group(Guid groupId, Guid billingOwnerId) => new(billingOwnerId, groupId);
}

/// <summary>
/// The single server-side enforcement point of plan limits (docs/product/plans.md principle 5). The plan of
/// the <b>resource owner</b> governs: for groups and group-owned calendars that is the group's billing owner
/// (<c>groups.owner_user_id</c>), with the group's reverse trial bounded by the group's age (<see cref="PlanSubject"/>).
/// Every <c>Ensure…</c> method throws <c>402 plan_limit_reached</c> with <c>limit: { key, max, used }</c> when the change would exceed a limit; over-limit usage left by a downgrade
/// stays (only growth is refused).
/// </summary>
/// <remarks>
/// Groups and members are counted here. Calendars (M2-B) and overrides (M2-D) are counted by their use cases in
/// their own transaction (the tables arrive with them) and handed in: call
/// <see cref="EnsureCanCreateCalendarAsync"/> before inserting a calendar and
/// <see cref="EnsureCanChangeEventOverridesAsync"/> before replacing an event's overrides.
/// </remarks>
public interface IEntitlementService
{
    /// <summary>Plan and limits of the user <paramref name="billingOwnerId"/> for their personal resources (organizations follow with Team).</summary>
    Task<PlanEntitlements> GetAsync(Guid billingOwnerId, CancellationToken cancellationToken = default);

    /// <summary>Plan and limits of <paramref name="subject"/>: the billing owner's plan, with a group's trial anchored as <see cref="PlanSubject"/> says.</summary>
    Task<PlanEntitlements> GetAsync(PlanSubject subject, CancellationToken cancellationToken = default);

    /// <summary>Before <paramref name="userId"/> creates a group (they become its billing owner): <see cref="PlanLimit.OwnedGroups"/>.</summary>
    Task EnsureCanCreateGroupAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Before a member joins or is invited to <paramref name="groupId"/>: <see cref="PlanLimit.MembersPerGroup"/> of the billing owner's plan.</summary>
    Task EnsureCanAddGroupMemberAsync(Guid groupId, Guid billingOwnerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Before a calendar is created for (or transferred to) <paramref name="subject"/> (a user, or a group and its
    /// billing owner), whose billing owner owns <paramref name="ownedCalendars"/> calendars now: <see cref="PlanLimit.OwnedCalendars"/>.
    /// </summary>
    Task EnsureCanCreateCalendarAsync(PlanSubject subject, int ownedCalendars, CancellationToken cancellationToken = default);

    /// <summary>Before an event's overrides are replaced; <paramref name="subject"/> is the calendar's plan subject: <see cref="PlanLimit.OverridesPerEvent"/> and <see cref="PlanLimit.EventsWithOverrides"/>.</summary>
    Task EnsureCanChangeEventOverridesAsync(PlanSubject subject, OverrideUsage usage, CancellationToken cancellationToken = default);
}
