using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolvorApp.Notifications.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notifications");

            migrationBuilder.CreateTable(
                name: "notification_deliveries",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    template = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    topic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_deliveries", x => x.id);
                    table.CheckConstraint("ck_notification_deliveries_kind", "kind IN ('LICENSE_DIGEST', 'ORDER_WINDOW', 'ORDER_STATUS', 'MILESTONE_REMINDER')");
                    table.CheckConstraint("ck_notification_deliveries_status", "status IN ('PENDING', 'SENT', 'SKIPPED', 'FAILED')");
                });

            migrationBuilder.CreateTable(
                name: "notification_events",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    edition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comparsa_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_events", x => x.id);
                    table.CheckConstraint("ck_notification_events_type", "type IN ('ORDERS_OPENED', 'ORDERS_CLOSED', 'ORDER_SUBMITTED', 'ORDER_RETURNED', 'ORDER_VALIDATED')");
                });

            migrationBuilder.CreateTable(
                name: "notification_opt_outs",
                schema: "notifications",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_opt_outs", x => new { x.user_id, x.kind });
                    table.CheckConstraint("ck_notification_opt_outs_kind", "kind IN ('LICENSE_DIGEST', 'ORDER_WINDOW', 'ORDER_STATUS', 'MILESTONE_REMINDER')");
                });

            migrationBuilder.CreateTable(
                name: "notification_runs",
                schema: "notifications",
                columns: table => new
                {
                    job = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    period = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ran_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_runs", x => new { x.job, x.period });
                });

            migrationBuilder.CreateIndex(
                name: "ix_notification_deliveries_created_at",
                schema: "notifications",
                table: "notification_deliveries",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_notification_deliveries_due",
                schema: "notifications",
                table: "notification_deliveries",
                column: "next_attempt_at",
                filter: "status = 'PENDING'");

            migrationBuilder.CreateIndex(
                name: "ux_notification_deliveries_user_topic",
                schema: "notifications",
                table: "notification_deliveries",
                columns: new[] { "user_id", "topic" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_events_pending",
                schema: "notifications",
                table: "notification_events",
                column: "occurred_at",
                filter: "processed_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_deliveries",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "notification_events",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "notification_opt_outs",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "notification_runs",
                schema: "notifications");
        }
    }
}
