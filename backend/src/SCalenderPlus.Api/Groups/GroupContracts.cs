using System.ComponentModel.DataAnnotations;
using SCalenderPlus.Application.Groups;
using SCalenderPlus.Core.Groups;

namespace SCalenderPlus.Api.Groups;

/// <summary>A group as the signed-in member sees it.</summary>
/// <param name="MyRole">The caller's role: <c>owner</c>, <c>admin</c>, <c>member</c> or <c>viewer</c>.</param>
/// <param name="BillingOwnerId">The owner whose plan governs the group (transferable among owners).</param>
/// <param name="MemberListVisibility"><c>all_members</c> (viewers included) or <c>members_and_above</c>.</param>
/// <param name="Frozen">Over the plan limit: no invites or role changes until resolved.</param>
public sealed record GroupResponse(
    Guid Id,
    string Name,
    string? Description,
    string MyRole,
    Guid BillingOwnerId,
    int MemberCount,
    string MemberListVisibility,
    bool Frozen,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static GroupResponse From(GroupView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var g = view.Group;
        return new GroupResponse(
            g.Id,
            g.Name,
            g.Description,
            GroupRoles.Format(view.MyRole),
            g.OwnerUserId,
            view.MemberCount,
            MemberListVisibilities.Format(g.MemberListVisibility),
            g.FrozenAt is not null,
            g.CreatedAt.ToDateTimeOffset(),
            g.UpdatedAt.ToDateTimeOffset());
    }
}

/// <summary>One page of the signed-in user's groups.</summary>
/// <param name="NextCursor">Pass as <c>cursor</c> for the next page; null on the last page.</param>
public sealed record GroupListResponse(IReadOnlyList<GroupResponse> Items, string? NextCursor);

public sealed class CreateGroupRequest
{
    /// <summary>1–100 characters (trimmed).</summary>
    [Required]
    [StringLength(Group.NameMaxLength)]
    public string Name { get; init; } = string.Empty;

    /// <summary>At most 1000 characters.</summary>
    [StringLength(Group.DescriptionMaxLength)]
    public string? Description { get; init; }
}

/// <summary>
/// JSON Merge Patch of a group (<c>application/merge-patch+json</c>): absent or <c>null</c> members stay
/// unchanged; an empty <c>description</c> removes the description.
/// </summary>
public sealed class UpdateGroupRequest
{
    [StringLength(Group.NameMaxLength, MinimumLength = 1)]
    public string? Name { get; init; }

    [StringLength(Group.DescriptionMaxLength)]
    public string? Description { get; init; }

    /// <summary><c>all_members</c> or <c>members_and_above</c> (hides the member list from viewers).</summary>
    public string? MemberListVisibility { get; init; }
}

/// <summary>A member of a group.</summary>
/// <param name="Email">Only shown to admins and owners; null otherwise.</param>
/// <param name="Role"><c>owner</c>, <c>admin</c>, <c>member</c> or <c>viewer</c>.</param>
/// <param name="IsBillingOwner">This owner's plan governs the group.</param>
/// <param name="Etag">Send as <c>If-Match</c> to change or remove this membership.</param>
public sealed record MemberResponse(
    Guid UserId,
    string DisplayName,
    string? Email,
    string Role,
    bool IsBillingOwner,
    DateTimeOffset JoinedAt,
    string Etag)
{
    public static MemberResponse From(MemberView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var role = GroupRoles.Format(view.Role);
        var joinedAt = view.JoinedAt.ToDateTimeOffset();
        var etag = Hosting.ETags.Of(new { view.UserId, view.DisplayName, view.Email, role, view.IsBillingOwner, joinedAt });
        return new MemberResponse(view.UserId, view.DisplayName, view.Email, role, view.IsBillingOwner, joinedAt, etag);
    }
}

/// <summary>One page of a group's members (ordered by user id).</summary>
public sealed record MemberListResponse(IReadOnlyList<MemberResponse> Items, string? NextCursor);

public sealed class ChangeMemberRoleRequest
{
    /// <summary>
    /// The new role. Owners assign any role; admins manage members and viewers and assign at most
    /// <c>member</c>; everyone may lower their own role.
    /// </summary>
    [Required]
    public string Role { get; init; } = string.Empty;
}

public sealed class TransferBillingRequest
{
    /// <summary>The new billing owner: a member with role owner.</summary>
    [Required]
    public Guid UserId { get; init; }
}
