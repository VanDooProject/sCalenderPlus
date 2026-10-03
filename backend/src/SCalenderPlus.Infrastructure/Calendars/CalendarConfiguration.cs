using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCalenderPlus.Core.Calendars;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.Core.Permissions;
using SCalenderPlus.Infrastructure.Identity;

namespace SCalenderPlus.Infrastructure.Calendars;

/// <summary><c>calendars</c> and <c>calendar_grants</c> (docs/architecture/data-model.md §3).</summary>
internal sealed class CalendarConfiguration : IEntityTypeConfiguration<Calendar>, IEntityTypeConfiguration<CalendarGrantEntry>
{
    public void Configure(EntityTypeBuilder<Calendar> builder)
    {
        builder.ToTable("calendars", t => t.HasCheckConstraint("ck_calendars_one_owner", "num_nonnulls(owner_user_id, owner_group_id) = 1"));
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(Calendar.NameMaxLength);
        builder.Property(c => c.Description).HasMaxLength(Calendar.DescriptionMaxLength);
        builder.Property(c => c.Color).HasMaxLength(Calendar.ColorLength);
        builder.Property(c => c.DefaultTimeZone).HasMaxLength(Calendar.TimeZoneMaxLength);
        builder.Property(c => c.CreatorsManageOwnEvents).HasDefaultValue(true);
        builder.Property(c => c.RoleDefaults)
            .HasColumnName("group_role_defaults")
            .HasColumnType("jsonb")
            .HasConversion(d => RoleDefaultsJson.Write(d), json => RoleDefaultsJson.Read(json));
        builder.Property(c => c.Version).IsRowVersion();
        builder.Ignore(c => c.IsGroupOwned);
        builder.Ignore(c => c.Owner);

        builder.HasIndex(c => c.OwnerUserId);
        builder.HasIndex(c => c.OwnerGroupId);

        // Owned calendars must be transferred or deleted before the account goes (permissions.md §4.6) …
        builder.HasOne<AppUser>().WithMany().HasForeignKey(c => c.OwnerUserId)
            .HasConstraintName("fk_calendars_users_owner_user_id").OnDelete(DeleteBehavior.Restrict);

        // … and before the owning group is deleted (409 group_has_calendars; issue #43).
        builder.HasOne<Group>().WithMany().HasForeignKey(c => c.OwnerGroupId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<CalendarGrantEntry> builder)
    {
        builder.ToTable("calendar_grants", t =>
        {
            t.HasCheckConstraint("ck_calendar_grants_principal", "(principal_type = 0 AND min_role IS NULL) OR (principal_type = 1 AND min_role IS NOT NULL)");
            t.HasCheckConstraint("ck_calendar_grants_level", "level BETWEEN 1 AND 5");
        });
        builder.Property(g => g.Id).ValueGeneratedNever();
        builder.Property(g => g.PrincipalType).HasConversion<short>();
        builder.Property(g => g.MinRole).HasConversion<short?>();
        builder.Property(g => g.Level).HasConversion<short>();
        builder.Property(g => g.Version).IsRowVersion();
        builder.Ignore(g => g.Principal);

        // One grant per principal; NULLS NOT DISTINCT so two user grants (min_role null) collide too.
        builder.HasIndex(g => new { g.CalendarId, g.PrincipalType, g.PrincipalId, g.MinRole }).IsUnique().AreNullsDistinct(false);

        // "Calendars shared with me / my groups", and cleanup when a group or user goes.
        builder.HasIndex(g => new { g.PrincipalType, g.PrincipalId });

        builder.HasOne<Calendar>().WithMany().HasForeignKey(g => g.CalendarId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// <c>calendars.group_role_defaults</c> as JSON with the API level names:
/// <c>{"admin":"manage","member":"contribute","viewer":"read"}</c>.
/// </summary>
internal static class RoleDefaultsJson
{
    public static string Write(GroupRoleDefaults defaults) =>
        JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["admin"] = PermissionLevels.Format(defaults.Admin),
            ["member"] = PermissionLevels.Format(defaults.Member),
            ["viewer"] = PermissionLevels.Format(defaults.Viewer),
        });

    public static GroupRoleDefaults Read(string json)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        return new GroupRoleDefaults(Level(values, "admin"), Level(values, "member"), Level(values, "viewer"));
    }

    private static CalendarLevel Level(Dictionary<string, string> values, string role) =>
        values.TryGetValue(role, out var name) && PermissionLevels.TryParse(name, out CalendarLevel level)
            ? level
            : throw new InvalidOperationException($"Stored group role defaults lack a valid level for '{role}': {name ?? "missing"}.");
}
