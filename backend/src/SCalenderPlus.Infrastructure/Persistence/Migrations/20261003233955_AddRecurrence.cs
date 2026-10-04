using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace SCalenderPlus.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurrence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<LocalDateTime[]>(
                name: "exdates",
                table: "events",
                type: "timestamp without time zone[]",
                nullable: false,
                defaultValue: new LocalDateTime[0]);

            migrationBuilder.AddColumn<LocalDateTime[]>(
                name: "rdates",
                table: "events",
                type: "timestamp without time zone[]",
                nullable: false,
                defaultValue: new LocalDateTime[0]);

            migrationBuilder.AddColumn<string>(
                name: "related_to",
                table: "events",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "series_start_utc",
                table: "events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AlterColumn<Interval>(
                name: "occurs_range",
                table: "events",
                type: "tstzrange",
                nullable: false,
                computedColumnSql: "tstzrange(CASE WHEN rrule IS NULL THEN start_utc ELSE least(start_utc, series_start_utc) END, CASE WHEN rrule IS NULL THEN end_utc ELSE coalesce(series_until_utc, 'infinity'::timestamptz) END, CASE WHEN rrule IS NULL AND end_utc = start_utc THEN '[]' ELSE '[)' END)",
                stored: true,
                oldClrType: typeof(Interval),
                oldType: "tstzrange",
                oldComputedColumnSql: "tstzrange(start_utc, CASE WHEN rrule IS NULL THEN end_utc ELSE coalesce(series_until_utc, 'infinity'::timestamptz) END, CASE WHEN rrule IS NULL AND end_utc = start_utc THEN '[]' ELSE '[)' END)",
                oldStored: true);

            migrationBuilder.CreateTable(
                name: "event_exceptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recurrence_id = table.Column<LocalDateTime>(type: "timestamp without time zone", nullable: false),
                    cancelled = table.Column<bool>(type: "boolean", nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    description = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    location = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<short>(type: "smallint", nullable: true),
                    transparency = table.Column<short>(type: "smallint", nullable: true),
                    start_local = table.Column<LocalDateTime>(type: "timestamp without time zone", nullable: true),
                    end_local = table.Column<LocalDateTime>(type: "timestamp without time zone", nullable: true),
                    start_date = table.Column<LocalDate>(type: "date", nullable: true),
                    end_date = table.Column<LocalDate>(type: "date", nullable: true),
                    start_utc = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    end_utc = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_exceptions", x => x.id);
                    table.CheckConstraint("ck_event_exceptions_times", "(start_utc IS NULL AND end_utc IS NULL AND start_local IS NULL AND end_local IS NULL AND start_date IS NULL AND end_date IS NULL) OR (start_utc IS NOT NULL AND end_utc >= start_utc AND ((start_local IS NOT NULL AND end_local IS NOT NULL AND start_date IS NULL AND end_date IS NULL) OR (start_date IS NOT NULL AND end_date > start_date AND start_local IS NULL AND end_local IS NULL)))");
                    table.ForeignKey(
                        name: "fk_event_exceptions_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_event_exceptions_event_id",
                table: "event_exceptions",
                column: "event_id");
            // One exception per occurrence; deferrable because re-keying shifts several keys in one transaction
            // (EventConfiguration.ExceptionKeyConstraint: EF Core cannot model deferrable constraints).
            migrationBuilder.Sql(
                "ALTER TABLE event_exceptions ADD CONSTRAINT uq_event_exceptions_event_id_recurrence_id " +
                "UNIQUE (event_id, recurrence_id) DEFERRABLE INITIALLY DEFERRED;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_exceptions");

            migrationBuilder.DropColumn(
                name: "exdates",
                table: "events");

            migrationBuilder.DropColumn(
                name: "rdates",
                table: "events");

            migrationBuilder.DropColumn(
                name: "related_to",
                table: "events");

            migrationBuilder.DropColumn(
                name: "series_start_utc",
                table: "events");

            migrationBuilder.AlterColumn<Interval>(
                name: "occurs_range",
                table: "events",
                type: "tstzrange",
                nullable: false,
                computedColumnSql: "tstzrange(start_utc, CASE WHEN rrule IS NULL THEN end_utc ELSE coalesce(series_until_utc, 'infinity'::timestamptz) END, CASE WHEN rrule IS NULL AND end_utc = start_utc THEN '[]' ELSE '[)' END)",
                stored: true,
                oldClrType: typeof(Interval),
                oldType: "tstzrange",
                oldComputedColumnSql: "tstzrange(CASE WHEN rrule IS NULL THEN start_utc ELSE least(start_utc, series_start_utc) END, CASE WHEN rrule IS NULL THEN end_utc ELSE coalesce(series_until_utc, 'infinity'::timestamptz) END, CASE WHEN rrule IS NULL AND end_utc = start_utc THEN '[]' ELSE '[)' END)",
                oldStored: true);
        }
    }
}
