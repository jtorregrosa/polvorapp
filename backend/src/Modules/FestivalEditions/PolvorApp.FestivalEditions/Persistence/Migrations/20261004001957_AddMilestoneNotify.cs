using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolvorApp.FestivalEditions.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMilestoneNotify : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "notify",
                schema: "editions",
                table: "calendar_milestones",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "notify",
                schema: "editions",
                table: "calendar_milestones");
        }
    }
}
