using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolvorApp.Distribution.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialDistribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "distribution");

            migrationBuilder.CreateTable(
                name: "distributions",
                schema: "distribution",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    edition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_distributions", x => x.id);
                    table.CheckConstraint("ck_distributions_location_not_blank", "btrim(location) <> ''");
                    table.CheckConstraint("ck_distributions_type", "type IN ('POWDER', 'WEAPONS')");
                });

            migrationBuilder.CreateTable(
                name: "pickup_proxies",
                schema: "distribution",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    edition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comparsa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    holder_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proxy_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pickup_proxies", x => x.id);
                    table.CheckConstraint("ck_pickup_proxies_not_holder", "holder_entry_id <> proxy_entry_id");
                    table.CheckConstraint("ck_pickup_proxies_type", "type IN ('POWDER', 'WEAPONS')");
                });

            migrationBuilder.CreateTable(
                name: "distribution_slots",
                schema: "distribution",
                columns: table => new
                {
                    distribution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comparsa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    starts_at = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_distribution_slots", x => new { x.distribution_id, x.comparsa_id });
                    table.ForeignKey(
                        name: "fk_distribution_slots_distributions_distribution_id",
                        column: x => x.distribution_id,
                        principalSchema: "distribution",
                        principalTable: "distributions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_distribution_slots_comparsa_id",
                schema: "distribution",
                table: "distribution_slots",
                column: "comparsa_id");

            migrationBuilder.CreateIndex(
                name: "ux_distributions_edition_type",
                schema: "distribution",
                table: "distributions",
                columns: new[] { "edition_id", "type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pickup_proxies_comparsa_id",
                schema: "distribution",
                table: "pickup_proxies",
                column: "comparsa_id");

            migrationBuilder.CreateIndex(
                name: "ix_pickup_proxies_edition_id_comparsa_id",
                schema: "distribution",
                table: "pickup_proxies",
                columns: new[] { "edition_id", "comparsa_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pickup_proxies_proxy_entry_id",
                schema: "distribution",
                table: "pickup_proxies",
                column: "proxy_entry_id");

            migrationBuilder.CreateIndex(
                name: "ux_pickup_proxies_holder_type",
                schema: "distribution",
                table: "pickup_proxies",
                columns: new[] { "holder_entry_id", "type" },
                unique: true);

            // Cross-schema foreign keys, from this later module to earlier ones (design D2). Editions
            // and comparsas are protected (NO ACTION, with the IEditionUsage and ICatalogUsage vetoes);
            // a proxy goes with either of its entries (CASCADE): entries are removed only by an
            // arquebusier's deletion while the orders are open (BR-14), which the registry audits.
            migrationBuilder.Sql(
                "ALTER TABLE distribution.distributions ADD CONSTRAINT fk_distributions_edition"
                + " FOREIGN KEY (edition_id) REFERENCES editions.festival_editions (id) ON DELETE NO ACTION;");
            migrationBuilder.Sql(
                "ALTER TABLE distribution.distribution_slots ADD CONSTRAINT fk_distribution_slots_comparsa"
                + " FOREIGN KEY (comparsa_id) REFERENCES catalog.comparsas (id) ON DELETE NO ACTION;");
            migrationBuilder.Sql(
                "ALTER TABLE distribution.pickup_proxies ADD CONSTRAINT fk_pickup_proxies_edition"
                + " FOREIGN KEY (edition_id) REFERENCES editions.festival_editions (id) ON DELETE NO ACTION;");
            migrationBuilder.Sql(
                "ALTER TABLE distribution.pickup_proxies ADD CONSTRAINT fk_pickup_proxies_comparsa"
                + " FOREIGN KEY (comparsa_id) REFERENCES catalog.comparsas (id) ON DELETE NO ACTION;");
            migrationBuilder.Sql(
                "ALTER TABLE distribution.pickup_proxies ADD CONSTRAINT fk_pickup_proxies_holder_entry"
                + " FOREIGN KEY (holder_entry_id) REFERENCES orders.edition_entries (id) ON DELETE CASCADE;");
            migrationBuilder.Sql(
                "ALTER TABLE distribution.pickup_proxies ADD CONSTRAINT fk_pickup_proxies_proxy_entry"
                + " FOREIGN KEY (proxy_entry_id) REFERENCES orders.edition_entries (id) ON DELETE CASCADE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "distribution_slots",
                schema: "distribution");

            migrationBuilder.DropTable(
                name: "pickup_proxies",
                schema: "distribution");

            migrationBuilder.DropTable(
                name: "distributions",
                schema: "distribution");
        }
    }
}
