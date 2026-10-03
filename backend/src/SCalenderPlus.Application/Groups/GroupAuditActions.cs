namespace SCalenderPlus.Application.Groups;

/// <summary>
/// Audit actions of groups (<c>audit_events.action</c>, resource type <see cref="ResourceType"/>, resource id =
/// group id; the subject is the group's billing owner). Every mutation of a group, its memberships and invites
/// is recorded with before/after.
/// </summary>
public static class GroupAuditActions
{
    public const string ResourceType = "group";

    public const string Created = "group.created";
    public const string Updated = "group.updated";
    public const string Deleted = "group.deleted";
    public const string BillingOwnerTransferred = "group.billing_owner_transferred";
    public const string MemberRoleChanged = "group.member.role_changed";
    public const string MemberRemoved = "group.member.removed";
    public const string MemberLeft = "group.member.left";
}
