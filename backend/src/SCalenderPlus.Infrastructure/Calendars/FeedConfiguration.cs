using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCalenderPlus.Core.Calendars;
using SCalenderPlus.Core.Feeds;
using SCalenderPlus.Infrastructure.Identity;

namespace SCalenderPlus.Infrastructure.Calendars;

/// <summary><c>share_links</c> (data-model.md §3) and <c>feed_tokens</c> (§5): secrets are stored as SHA-256 hashes only.</summary>
internal sealed class FeedConfiguration : IEntityTypeConfiguration<ShareLink>, IEntityTypeConfiguration<FeedToken>
{
    public void Configure(EntityTypeBuilder<ShareLink> builder)
    {
        builder.ToTable("share_links", t => t.HasCheckConstraint("ck_share_links_level", "level IN (1, 2)"));
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.Level).HasConversion<short>();
        builder.Property(l => l.Label).HasMaxLength(ShareLink.LabelMaxLength);
        builder.HasIndex(l => l.TokenHash).IsUnique();
        builder.HasIndex(l => l.CalendarId);

        // Links go with their (hard-deleted) calendar.
        builder.HasOne<Calendar>().WithMany().HasForeignKey(l => l.CalendarId).OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<FeedToken> builder)
    {
        builder.ToTable("feed_tokens", t =>
        {
            t.HasCheckConstraint("ck_feed_tokens_scope", "(scope = 0 AND calendar_id IS NOT NULL) OR (scope IN (1, 2) AND calendar_id IS NULL)");
            t.HasCheckConstraint("ck_feed_tokens_include_past_days", $"include_past_days BETWEEN 0 AND {FeedToken.MaxIncludePastDays}");
        });
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Scope).HasConversion<short>();
        builder.Property(t => t.LabelMode).HasConversion<short>();
        builder.Property(t => t.Name).HasMaxLength(FeedToken.NameMaxLength);
        builder.Ignore(t => t.IsRevoked);
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.UserId);

        // Tokens go with the account and with the calendar they subscribe to.
        builder.HasOne<AppUser>().WithMany().HasForeignKey(t => t.UserId)
            .HasConstraintName("fk_feed_tokens_users_user_id").OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Calendar>().WithMany().HasForeignKey(t => t.CalendarId).OnDelete(DeleteBehavior.Cascade);
    }
}
