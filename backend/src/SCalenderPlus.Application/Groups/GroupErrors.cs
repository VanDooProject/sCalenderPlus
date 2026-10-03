using SCalenderPlus.Application.Common;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.Core.Groups;

namespace SCalenderPlus.Application.Groups;

internal static class GroupErrors
{
    /// <summary>Unknown group or not a member: indistinguishable (no existence leaks).</summary>
    public static AppException GroupNotFound() => new(ErrorCodes.NotFound, "Group not found.");

    public static AppException InsufficientRole(GroupRole required, GroupRole actual) =>
        new(ErrorCodes.InsufficientPermission, $"This needs the group role {GroupRoles.Format(required)}.", new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["required"] = GroupRoles.Format(required),
            ["actual"] = GroupRoles.Format(actual),
        });

    public static AppException MemberNotFound() => new(ErrorCodes.NotFound, "Member not found.");

    /// <summary>The problem of a refused membership change.</summary>
    public static AppException From(MembershipDecision decision, GroupRole actual, string? forbiddenDetail = null) => decision.Verdict switch
    {
        MembershipVerdict.Forbidden => forbiddenDetail is null
            ? InsufficientRole(decision.RequiredRole ?? GroupRole.Owner, actual)
            : new AppException(ErrorCodes.InsufficientPermission, forbiddenDetail),
        MembershipVerdict.LastOwner => new(ErrorCodes.LastOwner, "The group needs at least one owner: make someone else owner first, or delete the group."),
        MembershipVerdict.BillingOwnerTransferRequired => new(ErrorCodes.BillingOwnerTransferRequired, "Transfer billing to another owner first."),
        MembershipVerdict.BillingOwnerMustBeOwner => new(ErrorCodes.BillingOwnerMustBeOwner, "Billing can only be transferred to a member with role owner."),
        MembershipVerdict.GroupFrozen => Frozen(),
        MembershipVerdict.LinkRoleTooHigh => Validation.Failed("role", "Invite links can carry at most the role member; invite admins and owners by email."),
        _ => throw new ArgumentOutOfRangeException(nameof(decision), decision.Verdict, "Not a refusal."),
    };

    public static AppException Frozen() =>
        new(ErrorCodes.GroupFrozen, "The group is over its plan limit: new members, invites and role changes are paused.");

    public static AppException InviteNotFound() => new(ErrorCodes.NotFound, "Invite not found.");

    /// <summary>Unknown, expired, revoked and used-up invites are indistinguishable.</summary>
    public static AppException InviteInvalid() =>
        new(ErrorCodes.TokenInvalid, "This invitation is invalid, has expired or was already used. Ask for a new one.");

    public static AppException Changed() =>
        new(ErrorCodes.PreconditionFailed, "The group was changed meanwhile. Reload it and try again.");
}
