using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolvorApp.FederationCatalog.Persistence.Migrations
{
    /// <summary>
    /// Drops the pistol exception of BR-07 (allow-rentable-pistols): any kind may be rentable. No row
    /// changes. <c>Down</c> refuses to run while a rentable pistol exists.
    /// </summary>
    public partial class AllowRentablePistols : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_weapon_models_pistol_not_rentable",
                schema: "catalog",
                table: "weapon_models");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM catalog.weapon_models WHERE kind = 'PISTOL' AND rentable) THEN
                        RAISE EXCEPTION 'Rentable pistols exist: clear their rentable flag and remove them from edition rental sets before rolling back.';
                    END IF;
                END $$;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_weapon_models_pistol_not_rentable",
                schema: "catalog",
                table: "weapon_models",
                sql: "NOT (kind = 'PISTOL' AND rentable)");
        }
    }
}
