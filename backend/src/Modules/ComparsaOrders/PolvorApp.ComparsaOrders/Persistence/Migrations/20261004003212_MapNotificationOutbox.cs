using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolvorApp.ComparsaOrders.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MapNotificationOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Snapshot only: the context now maps notifications.notification_events (excluded from its
            // migrations), which the notifications module creates (change add-notifications, design D2).
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: no schema change.
        }
    }
}
