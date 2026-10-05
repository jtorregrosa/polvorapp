using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolvorApp.FederationCatalog.Persistence.Migrations
{
    /// <summary>
    /// The Federation settings (add-federation-settings, design D1). The single row starts with the
    /// values used until now (the names printed in documents, "Unión de Comparsas", "PolvorApp", 7 and
    /// 7 days), so nothing changes until an Admin edits a setting. <c>xmin</c> is a system column:
    /// Npgsql emits no SQL for it.
    /// </summary>
    public partial class AddFederationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "close_reminder_lead_days",
                schema: "catalog",
                table: "federation_settings",
                type: "integer",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<string>(
                name: "contact_email",
                schema: "catalog",
                table: "federation_settings",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "milestone_lead_days",
                schema: "catalog",
                table: "federation_settings",
                type: "integer",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<string>(
                name: "official_name_ca",
                schema: "catalog",
                table: "federation_settings",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "Unió de Comparses de Moros i Cristians «Ber-Largas»");

            migrationBuilder.AddColumn<string>(
                name: "official_name_es",
                schema: "catalog",
                table: "federation_settings",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "Unión de Comparsas de Moros y Cristianos «Ber-Largas»");

            migrationBuilder.AddColumn<string>(
                name: "reply_to",
                schema: "catalog",
                table: "federation_settings",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sender_name",
                schema: "catalog",
                table: "federation_settings",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "PolvorApp");

            migrationBuilder.AddColumn<string>(
                name: "short_name",
                schema: "catalog",
                table: "federation_settings",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Unión de Comparsas");

            migrationBuilder.AddColumn<string>(
                name: "website",
                schema: "catalog",
                table: "federation_settings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "catalog",
                table: "federation_settings",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddCheckConstraint(
                name: "ck_federation_settings_close_reminder_lead_days",
                schema: "catalog",
                table: "federation_settings",
                sql: "close_reminder_lead_days BETWEEN 2 AND 14");

            migrationBuilder.AddCheckConstraint(
                name: "ck_federation_settings_milestone_lead_days",
                schema: "catalog",
                table: "federation_settings",
                sql: "milestone_lead_days BETWEEN 1 AND 14");

            migrationBuilder.AddCheckConstraint(
                name: "ck_federation_settings_names_not_blank",
                schema: "catalog",
                table: "federation_settings",
                sql: "btrim(official_name_es) <> '' AND btrim(official_name_ca) <> '' AND btrim(short_name) <> '' AND btrim(sender_name) <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_federation_settings_close_reminder_lead_days",
                schema: "catalog",
                table: "federation_settings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_federation_settings_milestone_lead_days",
                schema: "catalog",
                table: "federation_settings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_federation_settings_names_not_blank",
                schema: "catalog",
                table: "federation_settings");

            migrationBuilder.DropColumn(
                name: "close_reminder_lead_days",
                schema: "catalog",
                table: "federation_settings");

            migrationBuilder.DropColumn(
                name: "contact_email",
                schema: "catalog",
                table: "federation_settings");

            migrationBuilder.DropColumn(
                name: "milestone_lead_days",
                schema: "catalog",
                table: "federation_settings");

            migrationBuilder.DropColumn(
                name: "official_name_ca",
                schema: "catalog",
                table: "federation_settings");

            migrationBuilder.DropColumn(
                name: "official_name_es",
                schema: "catalog",
                table: "federation_settings");

            migrationBuilder.DropColumn(
                name: "reply_to",
                schema: "catalog",
                table: "federation_settings");

            migrationBuilder.DropColumn(
                name: "sender_name",
                schema: "catalog",
                table: "federation_settings");

            migrationBuilder.DropColumn(
                name: "short_name",
                schema: "catalog",
                table: "federation_settings");

            migrationBuilder.DropColumn(
                name: "website",
                schema: "catalog",
                table: "federation_settings");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "catalog",
                table: "federation_settings");
        }
    }
}
