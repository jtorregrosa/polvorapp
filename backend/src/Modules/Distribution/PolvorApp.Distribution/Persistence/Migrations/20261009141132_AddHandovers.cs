using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolvorApp.Distribution.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHandovers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "handovers",
                schema: "distribution",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    distribution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    holder_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    distribution_number = table.Column<int>(type: "integer", nullable: false),
                    collected_by = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    collector_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    powder_kg = table.Column<short>(type: "smallint", nullable: false),
                    rental_flask_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    traceability1 = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    traceability2 = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    collected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_handovers", x => x.id);
                    table.CheckConstraint("ck_handovers_collected_by", "collected_by IN ('HOLDER', 'PROXY')");
                    table.CheckConstraint("ck_handovers_collector", "collected_by = 'PROXY' OR collector_entry_id IS NULL");
                    table.CheckConstraint("ck_handovers_distribution_number", "distribution_number > 0");
                    table.CheckConstraint("ck_handovers_not_holder", "collector_entry_id <> holder_entry_id");
                    table.CheckConstraint("ck_handovers_powder_kg", "powder_kg BETWEEN 1 AND 2");
                    table.CheckConstraint("ck_handovers_texts_not_blank", "(rental_flask_number IS NULL OR btrim(rental_flask_number) <> '') AND (traceability1 IS NULL OR btrim(traceability1) <> '') AND (traceability2 IS NULL OR btrim(traceability2) <> '')");
                    table.ForeignKey(
                        name: "fk_handovers_distribution",
                        column: x => x.distribution_id,
                        principalSchema: "distribution",
                        principalTable: "distributions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_handovers_collector_entry_id",
                schema: "distribution",
                table: "handovers",
                column: "collector_entry_id",
                filter: "collector_entry_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_handovers_holder_entry_id",
                schema: "distribution",
                table: "handovers",
                column: "holder_entry_id");

            migrationBuilder.CreateIndex(
                name: "ux_handovers_distribution_holder",
                schema: "distribution",
                table: "handovers",
                columns: new[] { "distribution_id", "holder_entry_id" },
                unique: true);

            // A flask number once per day, ignoring case (design D1): an expression index, which the
            // EF model cannot describe, so it lives here only.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_handovers_distribution_flask ON distribution.handovers"
                + " (distribution_id, upper(rental_flask_number)) WHERE rental_flask_number IS NOT NULL;");

            // Cross-schema foreign keys (Modules README): a handover goes with its holder's entry
            // (CASCADE, BR-14) and keeps its role when the proxy's entry goes (SET NULL).
            migrationBuilder.Sql(
                "ALTER TABLE distribution.handovers ADD CONSTRAINT fk_handovers_holder_entry"
                + " FOREIGN KEY (holder_entry_id) REFERENCES orders.edition_entries (id) ON DELETE CASCADE;");
            migrationBuilder.Sql(
                "ALTER TABLE distribution.handovers ADD CONSTRAINT fk_handovers_collector_entry"
                + " FOREIGN KEY (collector_entry_id) REFERENCES orders.edition_entries (id) ON DELETE SET NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "handovers",
                schema: "distribution");
        }
    }
}
