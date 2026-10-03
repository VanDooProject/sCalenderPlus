using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace SCalenderPlus.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "calendars",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    default_time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    creators_manage_own_events = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    creators_may_share_externally = table.Column<bool>(type: "boolean", nullable: false),
                    group_role_defaults = table.Column<string>(type: "jsonb", nullable: false),
                    acl_version = table.Column<long>(type: "bigint", nullable: false),
                    frozen_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    archived_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendars", x => x.id);
                    table.CheckConstraint("ck_calendars_one_owner", "num_nonnulls(owner_user_id, owner_group_id) = 1");
                    table.ForeignKey(
                        name: "fk_calendars_groups_owner_group_id",
                        column: x => x.owner_group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_calendars_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "calendar_grants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    calendar_id = table.Column<Guid>(type: "uuid", nullable: false),
                    principal_type = table.Column<short>(type: "smallint", nullable: false),
                    principal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    min_role = table.Column<short>(type: "smallint", nullable: true),
                    level = table.Column<short>(type: "smallint", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_grants", x => x.id);
                    table.CheckConstraint("ck_calendar_grants_level", "level BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_calendar_grants_principal", "(principal_type = 0 AND min_role IS NULL) OR (principal_type = 1 AND min_role IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_calendar_grants_calendars_calendar_id",
                        column: x => x.calendar_id,
                        principalTable: "calendars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_calendar_grants_calendar_id_principal_type_principal_id_min",
                table: "calendar_grants",
                columns: new[] { "calendar_id", "principal_type", "principal_id", "min_role" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_calendar_grants_principal_type_principal_id",
                table: "calendar_grants",
                columns: new[] { "principal_type", "principal_id" });

            migrationBuilder.CreateIndex(
                name: "ix_calendars_owner_group_id",
                table: "calendars",
                column: "owner_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_calendars_owner_user_id",
                table: "calendars",
                column: "owner_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "calendar_grants");

            migrationBuilder.DropTable(
                name: "calendars");
        }
    }
}
