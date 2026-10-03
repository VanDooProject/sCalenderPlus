using SCalenderPlus.Core.Entitlements;

namespace SCalenderPlus.Application.Entitlements;

/// <summary>The plan of a billing subject and its limits.</summary>
public sealed record PlanEntitlements(Plan Plan, PlanLimitValues Limits);

/// <summary>
/// The single server-side enforcement point of plan limits (docs/product/plans.md principle 5). The plan of
/// the <b>resource owner</b> governs: for groups and group-owned calendars that is the group's billing owner
/// (<c>groups.owner_user_id</c>). Every <c>Ensure…</c> method throws <c>402 plan_limit_reached</c> with
/// <c>limit: { key, max, used }</c> when the change would exceed a limit; over-limit usage left by a downgrade
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
    /// <summary>Plan and limits of <paramref name="billingOwnerId"/> (a user; organizations follow with Team).</summary>
    Task<PlanEntitlements> GetAsync(Guid billingOwnerId, CancellationToken cancellationToken = default);

    /// <summary>Before <paramref name="userId"/> creates a group (they become its billing owner): <see cref="PlanLimit.OwnedGroups"/>.</summary>
    Task EnsureCanCreateGroupAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Before a member joins or is invited to <paramref name="groupId"/>: <see cref="PlanLimit.MembersPerGroup"/> of the billing owner's plan.</summary>
    Task EnsureCanAddGroupMemberAsync(Guid groupId, Guid billingOwnerId, CancellationToken cancellationToken = default);

    /// <summary>Before a calendar is created for (or transferred to) <paramref name="billingOwnerId"/>, who owns <paramref name="ownedCalendars"/> calendars now: <see cref="PlanLimit.OwnedCalendars"/>.</summary>
    Task EnsureCanCreateCalendarAsync(Guid billingOwnerId, int ownedCalendars, CancellationToken cancellationToken = default);

    /// <summary>Before an event's overrides are replaced; <paramref name="billingOwnerId"/> is the calendar's plan subject: <see cref="PlanLimit.OverridesPerEvent"/> and <see cref="PlanLimit.EventsWithOverrides"/>.</summary>
    Task EnsureCanChangeEventOverridesAsync(Guid billingOwnerId, OverrideUsage usage, CancellationToken cancellationToken = default);
}
