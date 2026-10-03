using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolvorApp.FederationCatalog.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFederationLogo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "federation_settings",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    logo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    logo_object_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    logo_width = table.Column<int>(type: "integer", nullable: true),
                    logo_height = table.Column<int>(type: "integer", nullable: true),
                    logo_size_bytes = table.Column<int>(type: "integer", nullable: true),
                    logo_uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_federation_settings", x => x.id);
                    table.CheckConstraint("ck_federation_settings_logo_complete", "(logo_id IS NULL AND logo_object_key IS NULL AND logo_width IS NULL AND logo_height IS NULL AND logo_size_bytes IS NULL AND logo_uploaded_at IS NULL) OR (logo_id IS NOT NULL AND logo_object_key IS NOT NULL AND logo_width IS NOT NULL AND logo_height IS NOT NULL AND logo_size_bytes IS NOT NULL AND logo_uploaded_at IS NOT NULL)");
                    table.CheckConstraint("ck_federation_settings_logo_key", "logo_object_key IS NULL OR logo_object_key = 'catalog/logos/' || replace(logo_id::text, '-', '') || '.png'");
                    table.CheckConstraint("ck_federation_settings_logo_size", "logo_id IS NULL OR (logo_width > 0 AND logo_height > 0 AND logo_size_bytes > 0)");
                    table.CheckConstraint("ck_federation_settings_single", "id = 1");
                });

            // The single settings row (design D11), without a logo: an Admin uploads it at run time.
            migrationBuilder.Sql("INSERT INTO catalog.federation_settings (id, updated_at) VALUES (1, now());");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "federation_settings",
                schema: "catalog");
        }
    }
}
