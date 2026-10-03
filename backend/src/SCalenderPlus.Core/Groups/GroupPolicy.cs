namespace SCalenderPlus.Core.Groups;

/// <summary>What a member may do with the group itself (the minimum role per action is <see cref="GroupPolicy.RequiredRole"/>).</summary>
public enum GroupAction
{
    /// <summary>See the group (name, my role, member count). Non-members don't see it at all (404).</summary>
    View,

    /// <summary>Rename, change description and member list visibility.</summary>
    Update,

    /// <summary>Delete the group with all memberships and invites.</summary>
    Delete,
}

/// <summary>
/// Group administration rules (docs/architecture/permissions.md §6.1) as pure functions over roles, so the
/// use cases (Application) and their tests share one definition. Callers resolve "not a member" to 404 before
/// asking (no existence leaks).
/// </summary>
public static class GroupPolicy
{
    public static GroupRole RequiredRole(GroupAction action) => action switch
    {
        GroupAction.View => GroupRole.Viewer,
        GroupAction.Update => GroupRole.Admin,
        GroupAction.Delete => GroupRole.Owner,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown group action."),
    };

    public static bool Allows(GroupRole actor, GroupAction action) => actor >= RequiredRole(action);

    /// <summary>Members, admins and owners always see the member list; viewers unless the group hides it from them.</summary>
    public static bool CanSeeMemberList(GroupRole actor, MemberListVisibility visibility) =>
        visibility == MemberListVisibility.AllMembers || actor >= GroupRole.Member;
}
