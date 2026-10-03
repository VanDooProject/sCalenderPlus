using NodaTime;

namespace SCalenderPlus.Core.Groups;

/// <summary>
/// A group (<c>groups</c>, docs/architecture/data-model.md §2): people with roles who can own calendars
/// together. <see cref="OwnerUserId"/> is the <b>billing owner</b> — always one of the role-owners, whose plan
/// governs the group (plans.md). Groups are deleted for real (no soft delete); memberships and invites go with
/// them.
/// </summary>
public sealed class Group
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 1000;

    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Team plan (v1): the organization owning the group.</summary>
    public Guid? OrganizationId { get; set; }

    /// <summary>The billing owner: a member with role <see cref="GroupRole.Owner"/>.</summary>
    public Guid OwnerUserId { get; set; }

    /// <summary>Bumped on grants/overrides naming the group (permission caches, M2).</summary>
    public long AclVersion { get; set; }

    /// <summary>Over the plan limit (M5): no invites or role changes while set.</summary>
    public Instant? FrozenAt { get; set; }

    public MemberListVisibility MemberListVisibility { get; set; }

    public Instant CreatedAt { get; set; }

    public Instant UpdatedAt { get; set; }

    /// <summary>Optimistic concurrency token (PostgreSQL <c>xmin</c>).</summary>
    public uint Version { get; set; }
}

/// <summary>A membership (<c>group_members</c>, PK <c>(group_id, user_id)</c>).</summary>
public sealed class GroupMember
{
    public Guid GroupId { get; set; }

    public Guid UserId { get; set; }

    public GroupRole Role { get; set; }

    public Instant JoinedAt { get; set; }

    public Instant UpdatedAt { get; set; }

    /// <summary>Optimistic concurrency token (PostgreSQL <c>xmin</c>).</summary>
    public uint Version { get; set; }
}
