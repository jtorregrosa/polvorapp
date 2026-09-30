using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolvorApp.AuditPrivacy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAuditTrail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "audit_entries",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    comparsa_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trace_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    data = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_entries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_actor_user_id_occurred_at",
                schema: "audit",
                table: "audit_entries",
                columns: new[] { "actor_user_id", "occurred_at" },
                filter: "actor_user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_comparsa_id_occurred_at",
                schema: "audit",
                table: "audit_entries",
                columns: new[] { "comparsa_id", "occurred_at" },
                filter: "comparsa_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_entity_type_entity_id",
                schema: "audit",
                table: "audit_entries",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_occurred_at",
                schema: "audit",
                table: "audit_entries",
                column: "occurred_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_entries",
                schema: "audit");
        }
    }
}
