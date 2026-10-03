using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolvorApp.ComparsaOrders.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "orders");

            migrationBuilder.CreateTable(
                name: "comparsa_orders",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    edition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    edition_year = table.Column<int>(type: "integer", nullable: false),
                    comparsa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    prepared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    prepared_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    submitted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attested = table.Column<bool>(type: "boolean", nullable: false),
                    submitted_by_admin = table.Column<bool>(type: "boolean", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    return_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comparsa_orders", x => x.id);
                    table.UniqueConstraint("ak_comparsa_orders_id_edition_id", x => new { x.id, x.edition_id });
                    table.CheckConstraint("ck_comparsa_orders_return_reason", "(status = 'RETURNED') = (return_reason IS NOT NULL) AND (return_reason IS NULL OR btrim(return_reason) <> '')");
                    table.CheckConstraint("ck_comparsa_orders_status", "status IN ('DRAFT', 'SUBMITTED', 'RETURNED', 'VALIDATED')");
                    table.CheckConstraint("ck_comparsa_orders_submission", "NOT (attested AND submitted_by_admin)");
                });

            migrationBuilder.CreateTable(
                name: "edition_entries",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    edition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    arquebusier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    powder_kg = table.Column<short>(type: "smallint", nullable: false),
                    caps_boxes = table.Column<short>(type: "smallint", nullable: false),
                    caps_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    weapon_source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    owned_weapon_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rental_weapon_model_id = table.Column<Guid>(type: "uuid", nullable: true),
                    flask = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    national_id = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    federation_id = table.Column<int>(type: "integer", nullable: true),
                    owned_weapon_model_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owned_weapon_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    owned_weapon_guide_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    copied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_edition_entries", x => x.id);
                    table.CheckConstraint("ck_edition_entries_caps_boxes", "caps_boxes BETWEEN 0 AND 99");
                    table.CheckConstraint("ck_edition_entries_caps_type", "(caps_boxes = 0) = (caps_type IS NULL)");
                    table.CheckConstraint("ck_edition_entries_caps_type_code", "caps_type IS NULL OR caps_type IN ('NORMAL', 'SMALL')");
                    table.CheckConstraint("ck_edition_entries_copy", "arquebusier_id IS NULL OR (first_name IS NOT NULL AND last_name IS NOT NULL AND national_id IS NOT NULL AND federation_id IS NOT NULL)");
                    table.CheckConstraint("ck_edition_entries_flask", "flask IN ('OWNED', 'RENTAL_1KG', 'RENTAL_2KG', 'NONE')");
                    table.CheckConstraint("ck_edition_entries_owned", "weapon_source = 'OWNED' OR (owned_weapon_id IS NULL AND owned_weapon_model_id IS NULL AND owned_weapon_number IS NULL AND owned_weapon_guide_number IS NULL)");
                    table.CheckConstraint("ck_edition_entries_powder_kg", "powder_kg BETWEEN 0 AND 2");
                    table.CheckConstraint("ck_edition_entries_rental", "(weapon_source = 'RENTAL') = (rental_weapon_model_id IS NOT NULL)");
                    table.CheckConstraint("ck_edition_entries_reserve", "status <> 'RESERVE' OR (powder_kg = 0 AND caps_boxes = 0 AND weapon_source = 'NONE' AND flask = 'NONE')");
                    table.CheckConstraint("ck_edition_entries_status", "status IN ('ACTIVE', 'RESERVE')");
                    table.CheckConstraint("ck_edition_entries_weapon_source", "weapon_source IN ('OWNED', 'RENTAL', 'LOAN', 'NONE')");
                    table.ForeignKey(
                        name: "fk_edition_entries_comparsa_orders_order_id_edition_id",
                        columns: x => new { x.order_id, x.edition_id },
                        principalSchema: "orders",
                        principalTable: "comparsa_orders",
                        principalColumns: new[] { "id", "edition_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "weapon_loans",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lender_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    lender_owned_weapon_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lender_first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    lender_last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    lender_national_id = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    lender_comparsa_id = table.Column<Guid>(type: "uuid", nullable: true),
                    weapon_model_id = table.Column<Guid>(type: "uuid", nullable: true),
                    weapon_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ownership_guide_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    copied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_weapon_loans", x => x.id);
                    table.CheckConstraint("ck_weapon_loans_external", "lender_kind <> 'EXTERNAL' OR (lender_owned_weapon_id IS NULL AND lender_comparsa_id IS NULL)");
                    table.CheckConstraint("ck_weapon_loans_lender_kind", "lender_kind IN ('ARQUEBUSIER', 'EXTERNAL')");
                    table.CheckConstraint("ck_weapon_loans_registered", "lender_kind <> 'ARQUEBUSIER' OR lender_comparsa_id IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_weapon_loans_edition_entries_entry_id",
                        column: x => x.entry_id,
                        principalSchema: "orders",
                        principalTable: "edition_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_comparsa_orders_comparsa_id",
                schema: "orders",
                table: "comparsa_orders",
                column: "comparsa_id");

            migrationBuilder.CreateIndex(
                name: "ix_comparsa_orders_edition_year",
                schema: "orders",
                table: "comparsa_orders",
                column: "edition_year");

            migrationBuilder.CreateIndex(
                name: "ux_comparsa_orders_edition_comparsa",
                schema: "orders",
                table: "comparsa_orders",
                columns: new[] { "edition_id", "comparsa_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_edition_entries_arquebusier_id",
                schema: "orders",
                table: "edition_entries",
                column: "arquebusier_id",
                filter: "arquebusier_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_edition_entries_order_id_edition_id",
                schema: "orders",
                table: "edition_entries",
                columns: new[] { "order_id", "edition_id" });

            migrationBuilder.CreateIndex(
                name: "ix_edition_entries_owned_weapon_id",
                schema: "orders",
                table: "edition_entries",
                column: "owned_weapon_id",
                filter: "owned_weapon_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_edition_entries_owned_weapon_model_id",
                schema: "orders",
                table: "edition_entries",
                column: "owned_weapon_model_id",
                filter: "owned_weapon_model_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_edition_entries_rental_weapon_model_id",
                schema: "orders",
                table: "edition_entries",
                column: "rental_weapon_model_id",
                filter: "rental_weapon_model_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_edition_entries_edition_arquebusier",
                schema: "orders",
                table: "edition_entries",
                columns: new[] { "edition_id", "arquebusier_id" },
                unique: true,
                filter: "arquebusier_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_weapon_loans_entry_id",
                schema: "orders",
                table: "weapon_loans",
                column: "entry_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_weapon_loans_lender_comparsa_id",
                schema: "orders",
                table: "weapon_loans",
                column: "lender_comparsa_id",
                filter: "lender_comparsa_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_weapon_loans_lender_owned_weapon_id",
                schema: "orders",
                table: "weapon_loans",
                column: "lender_owned_weapon_id",
                filter: "lender_owned_weapon_id IS NOT NULL")
                .Annotation("Npgsql:IndexInclude", new[] { "entry_id" });

            migrationBuilder.CreateIndex(
                name: "ix_weapon_loans_weapon_model_id",
                schema: "orders",
                table: "weapon_loans",
                column: "weapon_model_id",
                filter: "weapon_model_id IS NOT NULL");

            // Cross-schema foreign keys, from this later module to earlier ones (design D2, D3). Those to
            // the registry use SET NULL: entries outlive the arquebusier and the weapon with their copy.
            migrationBuilder.Sql(
                "ALTER TABLE orders.comparsa_orders ADD CONSTRAINT fk_comparsa_orders_edition"
                + " FOREIGN KEY (edition_id) REFERENCES editions.festival_editions (id) ON DELETE NO ACTION;");
            migrationBuilder.Sql(
                "ALTER TABLE orders.comparsa_orders ADD CONSTRAINT fk_comparsa_orders_comparsa"
                + " FOREIGN KEY (comparsa_id) REFERENCES catalog.comparsas (id) ON DELETE NO ACTION;");
            migrationBuilder.Sql(
                "ALTER TABLE orders.edition_entries ADD CONSTRAINT fk_edition_entries_rental_weapon_model"
                + " FOREIGN KEY (rental_weapon_model_id) REFERENCES catalog.weapon_models (id) ON DELETE NO ACTION;");
            migrationBuilder.Sql(
                "ALTER TABLE orders.edition_entries ADD CONSTRAINT fk_edition_entries_owned_weapon_model"
                + " FOREIGN KEY (owned_weapon_model_id) REFERENCES catalog.weapon_models (id) ON DELETE NO ACTION;");
            migrationBuilder.Sql(
                "ALTER TABLE orders.edition_entries ADD CONSTRAINT fk_edition_entries_arquebusier"
                + " FOREIGN KEY (arquebusier_id) REFERENCES registry.arquebusiers (id) ON DELETE SET NULL;");
            migrationBuilder.Sql(
                "ALTER TABLE orders.edition_entries ADD CONSTRAINT fk_edition_entries_owned_weapon"
                + " FOREIGN KEY (owned_weapon_id) REFERENCES registry.owned_weapons (id) ON DELETE SET NULL;");
            migrationBuilder.Sql(
                "ALTER TABLE orders.weapon_loans ADD CONSTRAINT fk_weapon_loans_weapon_model"
                + " FOREIGN KEY (weapon_model_id) REFERENCES catalog.weapon_models (id) ON DELETE NO ACTION;");
            migrationBuilder.Sql(
                "ALTER TABLE orders.weapon_loans ADD CONSTRAINT fk_weapon_loans_lender_comparsa"
                + " FOREIGN KEY (lender_comparsa_id) REFERENCES catalog.comparsas (id) ON DELETE NO ACTION;");
            migrationBuilder.Sql(
                "ALTER TABLE orders.weapon_loans ADD CONSTRAINT fk_weapon_loans_lender_owned_weapon"
                + " FOREIGN KEY (lender_owned_weapon_id) REFERENCES registry.owned_weapons (id) ON DELETE SET NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "weapon_loans",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "edition_entries",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "comparsa_orders",
                schema: "orders");
        }
    }
}
