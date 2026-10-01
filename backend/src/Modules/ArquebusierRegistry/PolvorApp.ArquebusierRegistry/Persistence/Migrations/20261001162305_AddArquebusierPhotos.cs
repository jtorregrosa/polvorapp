using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolvorApp.ArquebusierRegistry.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddArquebusierPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "arquebusier_photos",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    arquebusier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    object_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    size_bytes = table.Column<int>(type: "integer", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_arquebusier_photos", x => x.id);
                    table.CheckConstraint("ck_arquebusier_photos_dimensions", "width > 0 AND height > 0 AND size_bytes > 0");
                    table.CheckConstraint("ck_arquebusier_photos_kind", "kind IN ('ID', 'LICENSE_FRONT', 'LICENSE_BACK')");
                    table.CheckConstraint("ck_arquebusier_photos_object_key", "object_key LIKE 'registry/photos/%'");
                    table.ForeignKey(
                        name: "fk_arquebusier_photos_arquebusiers_arquebusier_id",
                        column: x => x.arquebusier_id,
                        principalSchema: "registry",
                        principalTable: "arquebusiers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_arquebusier_photos_arquebusier_id_kind",
                schema: "registry",
                table: "arquebusier_photos",
                columns: new[] { "arquebusier_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_arquebusier_photos_object_key",
                schema: "registry",
                table: "arquebusier_photos",
                column: "object_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "arquebusier_photos",
                schema: "registry");
        }
    }
}
