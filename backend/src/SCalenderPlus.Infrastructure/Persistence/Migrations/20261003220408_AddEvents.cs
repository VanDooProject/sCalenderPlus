using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SCalenderPlus.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,");

            migrationBuilder.CreateTable(
                name: "calendar_changes",
                columns: table => new
                {
                    seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    calendar_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    change = table.Column<short>(type: "smallint", nullable: false),
                    at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_changes", x => x.seq);
                    table.ForeignKey(
                        name: "fk_calendar_changes_calendars_calendar_id",
                        column: x => x.calendar_id,
                        principalTable: "calendars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    calendar_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uid = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    creator_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    description = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    location = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    transparency = table.Column<short>(type: "smallint", nullable: false),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    categories = table.Column<string[]>(type: "text[]", nullable: false),
                    all_day = table.Column<bool>(type: "boolean", nullable: false),
                    start_local = table.Column<LocalDateTime>(type: "timestamp without time zone", nullable: true),
                    end_local = table.Column<LocalDateTime>(type: "timestamp without time zone", nullable: true),
                    start_date = table.Column<LocalDate>(type: "date", nullable: true),
                    end_date = table.Column<LocalDate>(type: "date", nullable: true),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    start_utc = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    end_utc = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    rrule = table.Column<string>(type: "text", nullable: true),
                    series_until_utc = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    has_overrides = table.Column<bool>(type: "boolean", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    occurs_range = table.Column<Interval>(type: "tstzrange", nullable: false, computedColumnSql: "tstzrange(start_utc, CASE WHEN rrule IS NULL THEN end_utc ELSE coalesce(series_until_utc, 'infinity'::timestamptz) END, CASE WHEN rrule IS NULL AND end_utc = start_utc THEN '[]' ELSE '[)' END)", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_events", x => x.id);
                    table.CheckConstraint("ck_events_times", "(all_day AND start_date IS NOT NULL AND end_date > start_date AND start_local IS NULL AND end_local IS NULL AND time_zone IS NULL) OR (NOT all_day AND start_local IS NOT NULL AND end_local IS NOT NULL AND time_zone IS NOT NULL AND start_date IS NULL AND end_date IS NULL)");
                    table.CheckConstraint("ck_events_utc_order", "end_utc >= start_utc");
                    table.ForeignKey(
                        name: "fk_events_calendars_calendar_id",
                        column: x => x.calendar_id,
                        principalTable: "calendars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_calendar_changes_calendar_id_seq",
                table: "calendar_changes",
                columns: new[] { "calendar_id", "seq" });

            migrationBuilder.CreateIndex(
                name: "ix_events_calendar_id_occurs_range",
                table: "events",
                columns: new[] { "calendar_id", "occurs_range" },
                filter: "deleted_at IS NULL")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_events_calendar_id_uid",
                table: "events",
                columns: new[] { "calendar_id", "uid" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "calendar_changes");

            migrationBuilder.DropTable(
                name: "events");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:btree_gist", ",,");
        }
    }
}
