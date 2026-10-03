using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NodaTime;
using SCalenderPlus.Core.Calendars;
using SCalenderPlus.Core.Events;

namespace SCalenderPlus.Infrastructure.Calendars;

/// <summary><c>events</c> and the sync log <c>calendar_changes</c> (docs/architecture/data-model.md §4).</summary>
internal sealed class EventConfiguration : IEntityTypeConfiguration<Event>, IEntityTypeConfiguration<CalendarChange>
{
    /// <summary>The GiST index of window queries (asserted by the EXPLAIN test).</summary>
    public const string WindowIndex = "ix_events_calendar_id_occurs_range";

    /// <summary>
    /// <c>occurs_range</c> (generated): <c>[start_utc, end_utc)</c> of single events (a zero-length event is the
    /// point <c>[start_utc, start_utc]</c>, so it still overlaps windows); series masters (M2-E) reach to
    /// <c>series_until_utc</c> or infinity.
    /// </summary>
    public const string OccursRangeSql =
        "tstzrange(start_utc, CASE WHEN rrule IS NULL THEN end_utc ELSE coalesce(series_until_utc, 'infinity'::timestamptz) END, " +
        "CASE WHEN rrule IS NULL AND end_utc = start_utc THEN '[]' ELSE '[)' END)";

    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("events", t =>
        {
            t.HasCheckConstraint(
                "ck_events_times",
                "(all_day AND start_date IS NOT NULL AND end_date > start_date AND start_local IS NULL AND end_local IS NULL AND time_zone IS NULL) OR " +
                "(NOT all_day AND start_local IS NOT NULL AND end_local IS NOT NULL AND time_zone IS NOT NULL AND start_date IS NULL AND end_date IS NULL)");
            t.HasCheckConstraint("ck_events_utc_order", "end_utc >= start_utc");
        });
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Uid).HasMaxLength(Event.UidMaxLength);
        builder.Property(e => e.Title).HasMaxLength(Event.TitleMaxLength);
        builder.Property(e => e.Description).HasMaxLength(Event.DescriptionMaxLength);
        builder.Property(e => e.Location).HasMaxLength(Event.LocationMaxLength);
        builder.Property(e => e.Url).HasMaxLength(Event.UrlMaxLength);
        builder.Property(e => e.Color).HasMaxLength(Event.ColorLength);
        builder.Property(e => e.TimeZone).HasMaxLength(Event.TimeZoneMaxLength);
        builder.Property(e => e.Status).HasConversion<short>();
        builder.Property(e => e.Transparency).HasConversion<short>();
        builder.Property(e => e.Categories).HasColumnType("text[]");
        builder.Property(e => e.Version).IsRowVersion();
        builder.Property<Interval>("OccursRange")
            .HasColumnName("occurs_range")
            .HasComputedColumnSql(OccursRangeSql, stored: true);
        builder.Ignore(e => e.IsDeleted);
        builder.Ignore(e => e.Times);

        // Window queries per calendar (btree_gist for the uuid column); live events only.
        builder.HasIndex(nameof(Event.CalendarId), "OccursRange")
            .HasDatabaseName(WindowIndex)
            .HasMethod("gist")
            .HasFilter("deleted_at IS NULL");

        // UIDs are unique among a calendar's live events (a deleted event's UID may be used again, e.g. by CalDAV).
        builder.HasIndex(e => new { e.CalendarId, e.Uid }).IsUnique().HasFilter("deleted_at IS NULL");

        // Events go with their calendar (calendars are hard-deleted, permissions.md §4.6).
        builder.HasOne<Calendar>().WithMany().HasForeignKey(e => e.CalendarId).OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<CalendarChange> builder)
    {
        builder.ToTable("calendar_changes");
        builder.HasKey(c => c.Seq);
        builder.Property(c => c.Seq).UseIdentityByDefaultColumn();
        builder.Property(c => c.Change).HasConversion<short>();
        builder.HasIndex(c => new { c.CalendarId, c.Seq });

        // The log of a deleted calendar goes with it; event ids stay without FK (events are soft-deleted, then purged).
        builder.HasOne<Calendar>().WithMany().HasForeignKey(c => c.CalendarId).OnDelete(DeleteBehavior.Cascade);
    }
}
