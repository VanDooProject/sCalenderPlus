using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NodaTime;
using SCalenderPlus.Core.Users;

namespace SCalenderPlus.Infrastructure.Identity;

/// <summary>
/// An account (<c>users</c>, docs/architecture/data-model.md §2): ASP.NET Core Identity's user (email,
/// password hash, lockout, 2FA, security stamp) plus the product's profile columns. Identity types stay in
/// Infrastructure and the Api; the domain sees <see cref="Preferences"/> (Core) and the user id.
/// <see cref="IdentityUser{TKey}.UserName"/> always equals the email (login is by email).
/// </summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public const int DisplayNameMaxLength = 100;
    public const int EmailMaxLength = 256;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary><c>en</c> or <c>de</c> (<see cref="UserPreferences.SupportedLocales"/>).</summary>
    public string Locale { get; set; } = UserPreferences.DefaultLocale;

    /// <summary>IANA zone id, validated against tzdb.</summary>
    public string TimeZone { get; set; } = UserPreferences.DefaultTimeZone;

    /// <summary>First day of the week (<c>smallint</c>, 1 = Monday … 7 = Sunday).</summary>
    public IsoDayOfWeek WeekStart { get; set; } = IsoDayOfWeek.Monday;

    /// <summary>Bumped on membership/role changes and overrides naming the user (permission caches, M1-C/M2).</summary>
    public long AclVersion { get; set; }

    public Instant CreatedAt { get; set; }

    public Instant UpdatedAt { get; set; }

    /// <summary>Set when the user requested deletion (14-day grace period, later milestone).</summary>
    public Instant? DeletedAt { get; set; }

    public UserPreferences Preferences => new(Locale, TimeZone, WeekStart);
}

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("users");
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.DisplayName).HasMaxLength(AppUser.DisplayNameMaxLength);
        builder.Property(u => u.Locale).HasMaxLength(10);
        builder.Property(u => u.TimeZone).HasMaxLength(64);
        builder.Property(u => u.WeekStart).HasConversion<short>();

        // Identity's indexes, with conventional names; one account per email (data-model.md §2).
        builder.HasIndex(u => u.NormalizedEmail).IsUnique().HasDatabaseName("ix_users_normalized_email");
        builder.HasIndex(u => u.NormalizedUserName).IsUnique().HasDatabaseName("ix_users_normalized_user_name");
    }
}

internal sealed class IdentityTableConfiguration :
    IEntityTypeConfiguration<IdentityUserClaim<Guid>>,
    IEntityTypeConfiguration<IdentityUserLogin<Guid>>,
    IEntityTypeConfiguration<IdentityUserToken<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> builder) => builder.ToTable("user_claims");

    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> builder) => builder.ToTable("user_logins");

    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> builder) => builder.ToTable("user_tokens");
}
