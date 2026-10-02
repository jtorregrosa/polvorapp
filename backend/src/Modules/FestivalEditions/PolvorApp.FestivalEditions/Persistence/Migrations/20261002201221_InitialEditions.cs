using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolvorApp.FestivalEditions.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialEditions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "editions");

            migrationBuilder.CreateTable(
                name: "festival_editions",
                schema: "editions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    festival_starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    festival_ends_on = table.Column<DateOnly>(type: "date", nullable: false),
                    orders_open_on = table.Column<DateOnly>(type: "date", nullable: true),
                    orders_close_on = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    orders_open = table.Column<bool>(type: "boolean", nullable: false),
                    powder_per_kg = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    caps_box = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    weapon_rental = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    flask_rental = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_festival_editions", x => x.id);
                    table.CheckConstraint("ck_festival_editions_caps_box", "caps_box IS NULL OR caps_box >= 0");
                    table.CheckConstraint("ck_festival_editions_festival_dates", "festival_ends_on >= festival_starts_on");
                    table.CheckConstraint("ck_festival_editions_flask_rental", "flask_rental IS NULL OR flask_rental >= 0");
                    table.CheckConstraint("ck_festival_editions_orders_open_in_progress", "NOT orders_open OR status = 'IN_PROGRESS'");
                    table.CheckConstraint("ck_festival_editions_orders_window", "orders_open_on IS NULL OR orders_close_on IS NULL OR orders_close_on >= orders_open_on");
                    table.CheckConstraint("ck_festival_editions_powder_per_kg", "powder_per_kg IS NULL OR powder_per_kg >= 0");
                    table.CheckConstraint("ck_festival_editions_status", "status IN ('DRAFT', 'IN_PROGRESS', 'CLOSED')");
                    table.CheckConstraint("ck_festival_editions_weapon_rental", "weapon_rental IS NULL OR weapon_rental >= 0");
                    table.CheckConstraint("ck_festival_editions_year", "year BETWEEN 2000 AND 2100");
                });

            migrationBuilder.CreateTable(
                name: "calendar_milestones",
                schema: "editions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    edition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_milestones", x => x.id);
                    table.CheckConstraint("ck_calendar_milestones_title_not_blank", "btrim(title) <> ''");
                    table.ForeignKey(
                        name: "fk_calendar_milestones_festival_editions_edition_id",
                        column: x => x.edition_id,
                        principalSchema: "editions",
                        principalTable: "festival_editions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "edition_weapon_models",
                schema: "editions",
                columns: table => new
                {
                    edition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    weapon_model_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_edition_weapon_models", x => new { x.edition_id, x.weapon_model_id });
                    table.ForeignKey(
                        name: "fk_edition_weapon_models_festival_editions_edition_id",
                        column: x => x.edition_id,
                        principalSchema: "editions",
                        principalTable: "festival_editions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_calendar_milestones_edition_id_date",
                schema: "editions",
                table: "calendar_milestones",
                columns: new[] { "edition_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_edition_weapon_models_weapon_model_id",
                schema: "editions",
                table: "edition_weapon_models",
                column: "weapon_model_id");

            migrationBuilder.CreateIndex(
                name: "ix_festival_editions_year",
                schema: "editions",
                table: "festival_editions",
                column: "year",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_festival_editions_in_progress",
                schema: "editions",
                table: "festival_editions",
                column: "status",
                unique: true,
                filter: "status = 'IN_PROGRESS'");

            // Cross-schema foreign key to the catalog (an earlier module), which EF does not model:
            // it makes the catalog's delete fail while an edition offers the model, even in a race
            // with its usage check. NO ACTION raises foreign_key_violation (23503), which the
            // catalog maps to its inUse problem. Literal name: it equals
            // FestivalEditionsDbContext.WeaponModelForeignKey.
            migrationBuilder.Sql(
                "ALTER TABLE editions.edition_weapon_models ADD CONSTRAINT fk_edition_weapon_models_catalog_weapon_models"
                + " FOREIGN KEY (weapon_model_id) REFERENCES catalog.weapon_models (id) ON DELETE NO ACTION;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "calendar_milestones",
                schema: "editions");

            migrationBuilder.DropTable(
                name: "edition_weapon_models",
                schema: "editions");

            migrationBuilder.DropTable(
                name: "festival_editions",
                schema: "editions");
        }
    }
}
