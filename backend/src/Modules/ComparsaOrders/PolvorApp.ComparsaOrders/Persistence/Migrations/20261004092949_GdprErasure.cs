using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolvorApp.ComparsaOrders.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GdprErasure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "erased_at",
                schema: "orders",
                table: "weapon_loans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "erased_at",
                schema: "orders",
                table: "edition_entries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_weapon_loans_lender_national_id",
                schema: "orders",
                table: "weapon_loans",
                column: "lender_national_id",
                filter: "lender_national_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_weapon_loans_erased",
                schema: "orders",
                table: "weapon_loans",
                sql: "erased_at IS NULL OR (lender_owned_weapon_id IS NULL AND lender_first_name IS NULL AND lender_last_name IS NULL AND lender_national_id IS NULL AND weapon_number IS NULL AND ownership_guide_number IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_edition_entries_national_id",
                schema: "orders",
                table: "edition_entries",
                column: "national_id",
                filter: "national_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_edition_entries_erased",
                schema: "orders",
                table: "edition_entries",
                sql: "erased_at IS NULL OR (arquebusier_id IS NULL AND first_name IS NULL AND last_name IS NULL AND national_id IS NULL AND federation_id IS NULL AND owned_weapon_number IS NULL AND owned_weapon_guide_number IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_weapon_loans_lender_national_id",
                schema: "orders",
                table: "weapon_loans");

            migrationBuilder.DropCheckConstraint(
                name: "ck_weapon_loans_erased",
                schema: "orders",
                table: "weapon_loans");

            migrationBuilder.DropIndex(
                name: "ix_edition_entries_national_id",
                schema: "orders",
                table: "edition_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_edition_entries_erased",
                schema: "orders",
                table: "edition_entries");

            migrationBuilder.DropColumn(
                name: "erased_at",
                schema: "orders",
                table: "weapon_loans");

            migrationBuilder.DropColumn(
                name: "erased_at",
                schema: "orders",
                table: "edition_entries");
        }
    }
}
