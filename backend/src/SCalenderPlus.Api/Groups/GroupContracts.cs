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
