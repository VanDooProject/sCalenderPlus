using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace SCalenderPlus.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEventOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "event_overrides",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    principal_type = table.Column<short>(type: "smallint", nullable: false),
                    principal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    min_role = table.Column<short>(type: "smallint", nullable: true),
                    level = table.Column<short>(type: "smallint", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_overrides", x => x.id);
                    table.CheckConstraint("ck_event_overrides_level", "level BETWEEN 0 AND 3");
                    table.CheckConstraint("ck_event_overrides_principal", "(principal_type = 0 AND principal_id IS NOT NULL AND min_role IS NULL) OR (principal_type = 1 AND principal_id IS NOT NULL AND min_role IS NOT NULL) OR (principal_type IN (2, 3) AND principal_id IS NULL AND min_role IS NULL)");
                    table.ForeignKey(
                        name: "fk_event_overrides_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_event_overrides_event_id_principal_type_principal_id_min_ro",
                table: "event_overrides",
                columns: new[] { "event_id", "principal_type", "principal_id", "min_role" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_event_overrides_principal_type_principal_id",
                table: "event_overrides",
                columns: new[] { "principal_type", "principal_id" },
                filter: "principal_type IN (0, 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_overrides");
        }
    }
}
