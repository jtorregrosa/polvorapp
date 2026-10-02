using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolvorApp.ArquebusierRegistry.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRegistryLock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "registry_settings",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false),
                    locked = table.Column<bool>(type: "boolean", nullable: false),
                    locked_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_registry_settings", x => x.id);
                    table.CheckConstraint("ck_registry_settings_singleton", "id = 1");
                });

            // The single row, unlocked (add-festival-editions, design D8). Inserted here, not through
            // HasData, so no later migration can reset the lock with an UpdateData.
            migrationBuilder.InsertData(
                schema: "registry",
                table: "registry_settings",
                columns: new[] { "id", "locked", "locked_changed_at" },
                values: new object[] { (short)1, false, null! });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "registry_settings",
                schema: "registry");
        }
    }
}
