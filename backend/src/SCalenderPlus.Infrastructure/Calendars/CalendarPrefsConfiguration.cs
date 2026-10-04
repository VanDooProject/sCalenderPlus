using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCalenderPlus.Core.Calendars;
using SCalenderPlus.Infrastructure.Identity;

namespace SCalenderPlus.Infrastructure.Calendars;

/// <summary><c>user_calendar_prefs</c> (docs/architecture/data-model.md §3): one row per user and calendar.</summary>
internal sealed class CalendarPrefsConfiguration : IEntityTypeConfiguration<CalendarPrefs>
{
    public void Configure(EntityTypeBuilder<CalendarPrefs> builder)
    {
        builder.ToTable("user_calendar_prefs");
        builder.HasKey(p => new { p.UserId, p.CalendarId });
        builder.Property(p => p.Color).HasMaxLength(Calendar.ColorLength);

        // The overlay goes with the calendar and with the account.
        builder.HasOne<Calendar>().WithMany().HasForeignKey(p => p.CalendarId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(p => p.CalendarId);
    }
}
