using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace SCalenderPlus.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserCalendarPrefs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_calendar_prefs",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    calendar_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hidden = table.Column<bool>(type: "boolean", nullable: false),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_calendar_prefs", x => new { x.user_id, x.calendar_id });
                    table.ForeignKey(
                        name: "fk_user_calendar_prefs_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_calendar_prefs_calendars_calendar_id",
                        column: x => x.calendar_id,
                        principalTable: "calendars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_calendar_prefs_calendar_id",
                table: "user_calendar_prefs",
                column: "calendar_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_calendar_prefs");
        }
    }
}
