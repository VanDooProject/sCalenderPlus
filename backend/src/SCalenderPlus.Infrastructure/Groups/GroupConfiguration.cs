using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.Infrastructure.Identity;

namespace SCalenderPlus.Infrastructure.Groups;

/// <summary><c>groups</c> and <c>group_members</c> (docs/architecture/data-model.md §2).</summary>
internal sealed class GroupConfiguration : IEntityTypeConfiguration<Group>, IEntityTypeConfiguration<GroupMember>
{
    public void Configure(EntityTypeBuilder<Group> builder)
    {
        builder.ToTable("groups");
        builder.Property(g => g.Id).ValueGeneratedNever();
        builder.Property(g => g.Name).HasMaxLength(Group.NameMaxLength);
        builder.Property(g => g.Description).HasMaxLength(Group.DescriptionMaxLength);
        builder.Property(g => g.MemberListVisibility).HasConversion<short>();
        builder.Property(g => g.Version).IsRowVersion();

        // The billing owner must be transferred before the account can go (permissions.md §4.6).
        builder.HasOne<AppUser>().WithMany().HasForeignKey(g => g.OwnerUserId)
            .HasConstraintName("fk_groups_users_owner_user_id").OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<GroupMember> builder)
    {
        builder.ToTable("group_members");
        builder.HasKey(m => new { m.GroupId, m.UserId });
        builder.Property(m => m.Role).HasConversion<short>();
        builder.Property(m => m.Version).IsRowVersion();
        builder.HasIndex(m => m.UserId);

        builder.HasOne<Group>().WithMany().HasForeignKey(m => m.GroupId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(m => m.UserId)
            .HasConstraintName("fk_group_members_users_user_id").OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary><c>group_invites</c> (docs/architecture/data-model.md §2).</summary>
internal sealed class GroupInviteConfiguration : IEntityTypeConfiguration<GroupInvite>
{
    public void Configure(EntityTypeBuilder<GroupInvite> builder)
    {
        builder.ToTable("group_invites");
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Email).HasMaxLength(GroupInvite.EmailMaxLength);
        builder.Property(i => i.NormalizedEmail).HasMaxLength(GroupInvite.EmailMaxLength);
        builder.Property(i => i.Role).HasConversion<short>();
        builder.Property(i => i.Version).IsRowVersion();
        builder.Ignore(i => i.IsLink);

        builder.HasIndex(i => i.TokenHash).IsUnique();
        builder.HasIndex(i => i.GroupId);

        // Pending email invites of an address, joined when it is verified.
        builder.HasIndex(i => i.NormalizedEmail).HasFilter("normalized_email IS NOT NULL AND revoked_at IS NULL");

        builder.HasOne<Group>().WithMany().HasForeignKey(i => i.GroupId).OnDelete(DeleteBehavior.Cascade);
        builder.ToTable(t => t.HasCheckConstraint("ck_group_invites_uses", "uses >= 0 AND uses <= max_uses"));
    }
}
