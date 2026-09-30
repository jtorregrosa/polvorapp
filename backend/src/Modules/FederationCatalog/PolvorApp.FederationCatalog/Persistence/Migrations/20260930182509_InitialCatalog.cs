using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolvorApp.FederationCatalog.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.CreateTable(
                name: "comparsas",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    side = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    name_key = table.Column<string>(type: "text", nullable: false, computedColumnSql: "lower(name)", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comparsas", x => x.id);
                    table.CheckConstraint("ck_comparsas_name_not_blank", "btrim(name) <> ''");
                    table.CheckConstraint("ck_comparsas_side", "side IN ('MOORISH', 'CHRISTIAN')");
                });

            migrationBuilder.CreateTable(
                name: "weapon_models",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    side = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    handedness = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    size = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    rentable = table.Column<bool>(type: "boolean", nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    label_key = table.Column<string>(type: "text", nullable: false, computedColumnSql: "lower(label)", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_weapon_models", x => x.id);
                    table.CheckConstraint("ck_weapon_models_attributes", "kind = 'PISTOL' OR (side IS NOT NULL AND handedness IS NOT NULL AND size IS NOT NULL)");
                    table.CheckConstraint("ck_weapon_models_handedness", "handedness IS NULL OR handedness IN ('RIGHT', 'LEFT')");
                    table.CheckConstraint("ck_weapon_models_kind", "kind IN ('TRABUCO', 'ARCABUZ', 'PISTOL')");
                    table.CheckConstraint("ck_weapon_models_label_not_blank", "btrim(label) <> ''");
                    table.CheckConstraint("ck_weapon_models_pistol_not_rentable", "NOT (kind = 'PISTOL' AND rentable)");
                    table.CheckConstraint("ck_weapon_models_side", "side IS NULL OR side IN ('MOORISH', 'CHRISTIAN')");
                    table.CheckConstraint("ck_weapon_models_size", "size IS NULL OR size IN ('NORMAL', 'SMALL')");
                });

            migrationBuilder.CreateTable(
                name: "firing_chief_assignments",
                schema: "catalog",
                columns: table => new
                {
                    comparsa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_firing_chief_assignments", x => new { x.comparsa_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_firing_chief_assignments_comparsas_comparsa_id",
                        column: x => x.comparsa_id,
                        principalSchema: "catalog",
                        principalTable: "comparsas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_comparsas_name_key",
                schema: "catalog",
                table: "comparsas",
                column: "name_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_firing_chief_assignments_user_id",
                schema: "catalog",
                table: "firing_chief_assignments",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_weapon_models_combination",
                schema: "catalog",
                table: "weapon_models",
                columns: new[] { "kind", "side", "handedness", "size" },
                unique: true,
                filter: "kind <> 'PISTOL'");

            migrationBuilder.CreateIndex(
                name: "ix_weapon_models_label_key",
                schema: "catalog",
                table: "weapon_models",
                column: "label_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "firing_chief_assignments",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "weapon_models",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "comparsas",
                schema: "catalog");
        }
    }
}
