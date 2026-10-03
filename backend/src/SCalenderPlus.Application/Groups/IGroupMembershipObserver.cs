using SCalenderPlus.Core.Groups;

namespace SCalenderPlus.Application.Groups;

/// <summary>
/// Extension point for consequences of membership changes, called by the group use cases inside their
/// transaction (before the commit; throwing rolls the change back). <c>Events.EventShareRevocation</c> implements
/// "membership removal revokes event shares" here (permissions.md §4.6: on removal, leaving, demotion or group
/// deletion, <c>user:</c> overrides sharing events of the group's calendars with the user above their new level are
/// deleted unless <see cref="MembershipChange.RevokeEventShares"/> is false).
/// </summary>
public interface IGroupMembershipObserver
{
    Task OnMembershipChangedAsync(MembershipChange change, CancellationToken cancellationToken = default);
}

/// <param name="OldRole">Null when the user just joined.</param>
/// <param name="NewRole">Null when the membership ended (removed, left, group deleted).</param>
/// <param name="RevokeEventShares">
/// For removals and demotions: also delete the user's individual event shares on the group's calendars
/// (default true; the remover can opt out).
/// </param>
public sealed record MembershipChange(
    Guid GroupId,
    Guid UserId,
    GroupRole? OldRole,
    GroupRole? NewRole,
    MembershipChangeKind Kind,
    bool RevokeEventShares = true);

public enum MembershipChangeKind
{
    Joined,
    RoleChanged,
    Removed,
    Left,
    GroupDeleted,
}
