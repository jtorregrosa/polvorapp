using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolvorApp.FederationCatalog.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddComparsaLogos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "logo_height",
                schema: "catalog",
                table: "comparsas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "logo_id",
                schema: "catalog",
                table: "comparsas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "logo_object_key",
                schema: "catalog",
                table: "comparsas",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "logo_size_bytes",
                schema: "catalog",
                table: "comparsas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "logo_uploaded_at",
                schema: "catalog",
                table: "comparsas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "logo_width",
                schema: "catalog",
                table: "comparsas",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_comparsas_logo_object_key",
                schema: "catalog",
                table: "comparsas",
                column: "logo_object_key",
                unique: true,
                filter: "logo_object_key IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_comparsas_logo_complete",
                schema: "catalog",
                table: "comparsas",
                sql: "(logo_id IS NULL AND logo_object_key IS NULL AND logo_width IS NULL AND logo_height IS NULL AND logo_size_bytes IS NULL AND logo_uploaded_at IS NULL) OR (logo_id IS NOT NULL AND logo_object_key IS NOT NULL AND logo_width IS NOT NULL AND logo_height IS NOT NULL AND logo_size_bytes IS NOT NULL AND logo_uploaded_at IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_comparsas_logo_key",
                schema: "catalog",
                table: "comparsas",
                sql: "logo_object_key IS NULL OR logo_object_key = 'catalog/logos/' || replace(logo_id::text, '-', '') || '.png'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_comparsas_logo_size",
                schema: "catalog",
                table: "comparsas",
                sql: "logo_id IS NULL OR (logo_width > 0 AND logo_height > 0 AND logo_size_bytes > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_comparsas_logo_object_key",
                schema: "catalog",
                table: "comparsas");

            migrationBuilder.DropCheckConstraint(
                name: "ck_comparsas_logo_complete",
                schema: "catalog",
                table: "comparsas");

            migrationBuilder.DropCheckConstraint(
                name: "ck_comparsas_logo_key",
                schema: "catalog",
                table: "comparsas");

            migrationBuilder.DropCheckConstraint(
                name: "ck_comparsas_logo_size",
                schema: "catalog",
                table: "comparsas");

            migrationBuilder.DropColumn(
                name: "logo_height",
                schema: "catalog",
                table: "comparsas");

            migrationBuilder.DropColumn(
                name: "logo_id",
                schema: "catalog",
                table: "comparsas");

            migrationBuilder.DropColumn(
                name: "logo_object_key",
                schema: "catalog",
                table: "comparsas");

            migrationBuilder.DropColumn(
                name: "logo_size_bytes",
                schema: "catalog",
                table: "comparsas");

            migrationBuilder.DropColumn(
                name: "logo_uploaded_at",
                schema: "catalog",
                table: "comparsas");

            migrationBuilder.DropColumn(
                name: "logo_width",
                schema: "catalog",
                table: "comparsas");
        }
    }
}
