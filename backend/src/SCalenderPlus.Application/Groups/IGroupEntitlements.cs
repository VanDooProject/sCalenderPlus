namespace SCalenderPlus.Application.Groups;

/// <summary>
/// Plan limits of groups (docs/product/plans.md: owned groups, members per owned group), checked by the use
/// cases before a group is created or a member added (invite acceptance). The entitlement service of M2
/// (<c>IEntitlementService</c>) replaces <see cref="UnlimitedGroupEntitlements"/>; violations throw
/// <c>402 plan_limit_reached</c> with <c>limit</c>.
/// </summary>
public interface IGroupEntitlements
{
    /// <summary>Before <paramref name="userId"/> creates a group (it becomes their owned, billed group).</summary>
    Task EnsureCanCreateGroupAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Before a member joins the group; the group's billing owner's plan applies.</summary>
    Task EnsureCanAddMemberAsync(Guid groupId, Guid billingOwnerId, CancellationToken cancellationToken = default);
}

/// <summary>No limits yet (M1); see <see cref="IGroupEntitlements"/>.</summary>
internal sealed class UnlimitedGroupEntitlements : IGroupEntitlements
{
    public Task EnsureCanCreateGroupAsync(Guid userId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task EnsureCanAddMemberAsync(Guid groupId, Guid billingOwnerId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
