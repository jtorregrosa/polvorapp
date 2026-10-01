using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolvorApp.ArquebusierRegistry.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "registry");

            migrationBuilder.CreateTable(
                name: "arquebusiers",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    comparsa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    federation_id = table.Column<int>(type: "integer", nullable: false),
                    national_id = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    birth_date = table.Column<DateOnly>(type: "date", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    gender = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    training_completed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    license_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    license_pending = table.Column<bool>(type: "boolean", nullable: false),
                    license_issued_on = table.Column<DateOnly>(type: "date", nullable: true),
                    license_expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_arquebusiers", x => x.id);
                    table.CheckConstraint("ck_arquebusiers_birth_date", "birth_date >= DATE '1900-01-01'");
                    table.CheckConstraint("ck_arquebusiers_email", "email = lower(email) AND email = btrim(email) AND email <> ''");
                    table.CheckConstraint("ck_arquebusiers_federation_id", "federation_id BETWEEN 1 AND 999999999");
                    table.CheckConstraint("ck_arquebusiers_first_name", "btrim(first_name) <> '' AND first_name = btrim(first_name) AND first_name !~ '[[:cntrl:]]'");
                    table.CheckConstraint("ck_arquebusiers_gender", "gender IN ('MALE', 'FEMALE', 'UNSPECIFIED')");
                    table.CheckConstraint("ck_arquebusiers_last_name", "btrim(last_name) <> '' AND last_name = btrim(last_name) AND last_name !~ '[[:cntrl:]]'");
                    table.CheckConstraint("ck_arquebusiers_license", "(license_type IS NULL AND NOT license_pending AND license_issued_on IS NULL AND license_expires_on IS NULL) OR (license_type IS NOT NULL AND license_pending AND license_issued_on IS NULL AND license_expires_on IS NULL) OR (license_type IS NOT NULL AND NOT license_pending AND license_issued_on IS NOT NULL AND license_expires_on IS NOT NULL AND license_expires_on > license_issued_on)");
                    table.CheckConstraint("ck_arquebusiers_license_type", "license_type IS NULL OR license_type IN ('AE', 'A_PROF')");
                    table.CheckConstraint("ck_arquebusiers_national_id", "national_id ~ '^([0-9]{8}|[XYZ][0-9]{7})[A-Z]$'");
                    table.CheckConstraint("ck_arquebusiers_phone", "phone ~ '^[+]?[0-9 ]+$'");
                    table.CheckConstraint("ck_arquebusiers_status", "status IN ('ACTIVE', 'RESERVE')");
                });

            migrationBuilder.CreateTable(
                name: "owned_weapons",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    arquebusier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    weapon_model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    weapon_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ownership_guide_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_owned_weapons", x => x.id);
                    table.CheckConstraint("ck_owned_weapons_ownership_guide_number", "btrim(ownership_guide_number) <> '' AND ownership_guide_number = btrim(ownership_guide_number) AND ownership_guide_number COLLATE \"C\" = upper(ownership_guide_number COLLATE \"C\")");
                    table.CheckConstraint("ck_owned_weapons_weapon_number", "btrim(weapon_number) <> '' AND weapon_number = btrim(weapon_number)");
                    table.ForeignKey(
                        name: "fk_owned_weapons_arquebusiers_arquebusier_id",
                        column: x => x.arquebusier_id,
                        principalSchema: "registry",
                        principalTable: "arquebusiers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_arquebusiers_comparsa_id",
                schema: "registry",
                table: "arquebusiers",
                column: "comparsa_id");

            migrationBuilder.CreateIndex(
                name: "ix_arquebusiers_federation_id",
                schema: "registry",
                table: "arquebusiers",
                column: "federation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_arquebusiers_national_id",
                schema: "registry",
                table: "arquebusiers",
                column: "national_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_owned_weapons_arquebusier_id",
                schema: "registry",
                table: "owned_weapons",
                column: "arquebusier_id");

            migrationBuilder.CreateIndex(
                name: "ix_owned_weapons_ownership_guide_number",
                schema: "registry",
                table: "owned_weapons",
                column: "ownership_guide_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_owned_weapons_weapon_model_id",
                schema: "registry",
                table: "owned_weapons",
                column: "weapon_model_id");

            // Cross-schema foreign keys (design D3): EF cannot model a key into another module's
            // context, so they are not in the snapshot, and a later migration that changes these
            // columns must handle them by hand. They make the catalog's delete fail while a registry
            // row references the comparsa or model, even in a race with its usage check.
            // NO ACTION, not RESTRICT: it raises foreign_key_violation (23503), which the catalog maps
            // to its inUse problem; RESTRICT would raise restrict_violation (23001).
            // Literal names (a migration must not change when a constant does); they equal
            // ArquebusierRegistryDbContext.ComparsaForeignKey and WeaponModelForeignKey.
            migrationBuilder.Sql(
                "ALTER TABLE registry.arquebusiers ADD CONSTRAINT fk_arquebusiers_catalog_comparsas"
                + " FOREIGN KEY (comparsa_id) REFERENCES catalog.comparsas (id) ON DELETE NO ACTION;");
            migrationBuilder.Sql(
                "ALTER TABLE registry.owned_weapons ADD CONSTRAINT fk_owned_weapons_catalog_weapon_models"
                + " FOREIGN KEY (weapon_model_id) REFERENCES catalog.weapon_models (id) ON DELETE NO ACTION;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "owned_weapons",
                schema: "registry");

            migrationBuilder.DropTable(
                name: "arquebusiers",
                schema: "registry");
        }
    }
}
