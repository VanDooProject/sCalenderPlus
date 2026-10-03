using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace SCalenderPlus.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupInvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "group_invites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    role = table.Column<short>(type: "smallint", nullable: false),
                    max_uses = table.Column<int>(type: "integer", nullable: false),
                    uses = table.Column<int>(type: "integer", nullable: false),
                    expires_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group_invites", x => x.id);
                    table.CheckConstraint("ck_group_invites_uses", "uses >= 0 AND uses <= max_uses");
                    table.ForeignKey(
                        name: "fk_group_invites_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_group_invites_group_id",
                table: "group_invites",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_invites_normalized_email",
                table: "group_invites",
                column: "normalized_email",
                filter: "normalized_email IS NOT NULL AND revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_group_invites_token_hash",
                table: "group_invites",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "group_invites");
        }
    }
}
