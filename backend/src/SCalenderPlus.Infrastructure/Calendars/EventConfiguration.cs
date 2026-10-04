using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NodaTime;
using SCalenderPlus.Core.Calendars;
using SCalenderPlus.Core.Events;

namespace SCalenderPlus.Infrastructure.Calendars;

/// <summary><c>events</c>, <c>event_exceptions</c>, <c>event_overrides</c> and the sync log <c>calendar_changes</c> (docs/architecture/data-model.md §4).</summary>
internal sealed class EventConfiguration :
    IEntityTypeConfiguration<Event>,
    IEntityTypeConfiguration<EventExceptionEntry>,
    IEntityTypeConfiguration<CalendarChange>,
    IEntityTypeConfiguration<EventOverrideEntry>
{
    /// <summary>
    /// One exception per occurrence of a series. Deferrable (checked at commit) because re-keying a series' exceptions
    /// shifts several keys at once (an intermediate state may repeat one); created by SQL in the migration
    /// <c>AddRecurrence</c>, since EF Core cannot model deferrable constraints.
    /// </summary>
    public const string ExceptionKeyConstraint = "uq_event_exceptions_event_id_recurrence_id";

    /// <summary>The GiST index of window queries (asserted by the EXPLAIN test).</summary>
    public const string WindowIndex = "ix_events_calendar_id_occurs_range";

    /// <summary>
    /// <c>occurs_range</c> (generated): <c>[start_utc, end_utc)</c> of single events (a zero-length event is the
    /// point <c>[start_utc, start_utc]</c>, so it still overlaps windows); series masters reach from their first
    /// occurrence (or an earlier moved exception, <c>series_start_utc</c>) to <c>series_until_utc</c> or infinity.
    /// </summary>
    public const string OccursRangeSql =
        "tstzrange(CASE WHEN rrule IS NULL THEN start_utc ELSE least(start_utc, series_start_utc) END, " +
        "CASE WHEN rrule IS NULL THEN end_utc ELSE coalesce(series_until_utc, 'infinity'::timestamptz) END, " +
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
        builder.Property(e => e.RDates).HasColumnName("rdates").HasColumnType("timestamp without time zone[]");
        builder.Property(e => e.ExDates).HasColumnName("exdates").HasColumnType("timestamp without time zone[]");
        builder.Property(e => e.RelatedTo).HasMaxLength(Event.UidMaxLength);
        builder.Property(e => e.Version).IsRowVersion();
        builder.Property<Interval>("OccursRange")
            .HasColumnName("occurs_range")
            .HasComputedColumnSql(OccursRangeSql, stored: true);
        builder.Ignore(e => e.IsDeleted);
        builder.Ignore(e => e.OccursUntil);
        builder.Ignore(e => e.Times);
        builder.Ignore(e => e.IsSeries);

        // Window queries per calendar (btree_gist for the uuid column); live events only.
        builder.HasIndex(nameof(Event.CalendarId), "OccursRange")
            .HasDatabaseName(WindowIndex)
            .HasMethod("gist")
            .HasFilter("deleted_at IS NULL");

        // UIDs are unique among a calendar's live events (a deleted event's UID may be used again, e.g. by CalDAV).
        builder.HasIndex(e => new { e.CalendarId, e.Uid }).IsUnique().HasFilter("deleted_at IS NULL");

        // Events go with their calendar (calendars are hard-deleted, permissions.md §4.6).
        builder.HasOne<Calendar>().WithMany().HasForeignKey(e => e.CalendarId).OnDelete(DeleteBehavior.Cascade);

        // Exceptions go with their series (soft-deleted masters keep them for a restore).
        builder.HasMany(e => e.Exceptions).WithOne().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<EventExceptionEntry> builder)
    {
        builder.ToTable("event_exceptions", t =>
        {
            t.HasCheckConstraint(
                "ck_event_exceptions_times",
                "(start_utc IS NULL AND end_utc IS NULL AND start_local IS NULL AND end_local IS NULL AND start_date IS NULL AND end_date IS NULL) OR " +
                "(start_utc IS NOT NULL AND end_utc >= start_utc AND ((start_local IS NOT NULL AND end_local IS NOT NULL AND start_date IS NULL AND end_date IS NULL) OR " +
                "(start_date IS NOT NULL AND end_date > start_date AND start_local IS NULL AND end_local IS NULL)))");
        });
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Title).HasMaxLength(Event.TitleMaxLength);
        builder.Property(x => x.Description).HasMaxLength(Event.DescriptionMaxLength);
        builder.Property(x => x.Location).HasMaxLength(Event.LocationMaxLength);
        builder.Property(x => x.Status).HasConversion<short?>();
        builder.Property(x => x.Transparency).HasConversion<short?>();
        builder.Ignore(x => x.IsMoved);
        builder.Ignore(x => x.IsEmpty);

        // The unique key (event_id, recurrence_id) is the deferrable constraint ExceptionKeyConstraint (migration SQL).
        builder.HasIndex(x => x.EventId);
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

    public void Configure(EntityTypeBuilder<EventOverrideEntry> builder)
    {
        builder.ToTable("event_overrides", t =>
        {
            t.HasCheckConstraint(
                "ck_event_overrides_principal",
                "(principal_type = 0 AND principal_id IS NOT NULL AND min_role IS NULL) OR " +
                "(principal_type = 1 AND principal_id IS NOT NULL AND min_role IS NOT NULL) OR " +
                "(principal_type IN (2, 3) AND principal_id IS NULL AND min_role IS NULL)");
            t.HasCheckConstraint("ck_event_overrides_level", "level BETWEEN 0 AND 3"); // manage only through floors (rule 9)
        });
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.PrincipalType).HasConversion<short>();
        builder.Property(o => o.MinRole).HasConversion<short?>();
        builder.Property(o => o.Level).HasConversion<short>();
        builder.Ignore(o => o.Principal);

        // One entry per principal and event; NULLS NOT DISTINCT so two `everyone` entries collide too.
        builder.HasIndex(o => new { o.EventId, o.PrincipalType, o.PrincipalId, o.MinRole }).IsUnique().AreNullsDistinct(false);

        // "Shared with me" (events naming a user or one of their groups) and cleanup when a member or group goes.
        builder.HasIndex(o => new { o.PrincipalType, o.PrincipalId }).HasFilter("principal_type IN (0, 1)");

        // Overrides go with their event (events are soft-deleted, then purged; calendars hard-delete their events).
        builder.HasOne<Event>().WithMany().HasForeignKey(o => o.EventId).OnDelete(DeleteBehavior.Cascade);
    }
}
