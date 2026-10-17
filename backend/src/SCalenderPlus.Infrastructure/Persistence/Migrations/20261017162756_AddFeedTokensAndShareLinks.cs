using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace SCalenderPlus.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedTokensAndShareLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "feed_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<short>(type: "smallint", nullable: false),
                    calendar_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    label_mode = table.Column<short>(type: "smallint", nullable: false),
                    include_past_days = table.Column<int>(type: "integer", nullable: false),
                    needs_new_link = table.Column<bool>(type: "boolean", nullable: false),
                    last_used_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    rotated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feed_tokens", x => x.id);
                    table.CheckConstraint("ck_feed_tokens_include_past_days", "include_past_days BETWEEN 0 AND 365");
                    table.CheckConstraint("ck_feed_tokens_scope", "(scope = 0 AND calendar_id IS NOT NULL) OR (scope IN (1, 2) AND calendar_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_feed_tokens_calendars_calendar_id",
                        column: x => x.calendar_id,
                        principalTable: "calendars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_feed_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "share_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    calendar_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    level = table.Column<short>(type: "smallint", nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    expires_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_share_links", x => x.id);
                    table.CheckConstraint("ck_share_links_level", "level IN (1, 2)");
                    table.ForeignKey(
                        name: "fk_share_links_calendars_calendar_id",
                        column: x => x.calendar_id,
                        principalTable: "calendars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_calendar_changes_event_id_seq",
                table: "calendar_changes",
                columns: new[] { "event_id", "seq" });

            migrationBuilder.CreateIndex(
                name: "ix_feed_tokens_calendar_id",
                table: "feed_tokens",
                column: "calendar_id");

            migrationBuilder.CreateIndex(
                name: "ix_feed_tokens_token_hash",
                table: "feed_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_feed_tokens_user_id",
                table: "feed_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_share_links_calendar_id",
                table: "share_links",
                column: "calendar_id");

            migrationBuilder.CreateIndex(
                name: "ix_share_links_token_hash",
                table: "share_links",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feed_tokens");

            migrationBuilder.DropTable(
                name: "share_links");

            migrationBuilder.DropIndex(
                name: "ix_calendar_changes_event_id_seq",
                table: "calendar_changes");
        }
    }
}
