using Microsoft.EntityFrameworkCore;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.Notifications.Persistence;

/// <summary>Writes deliveries so that a recipient gets each topic once, whoever writes it first (design D3).</summary>
internal static class DeliveryStore
{
    /// <summary>
    /// Inserts <paramref name="delivery"/> unless the user already has a delivery for its topic; true
    /// when it was inserted. <c>ON CONFLICT DO NOTHING</c> on the unique <c>(user_id, topic)</c> index
    /// makes concurrent or repeated runs safe without failing the transaction.
    /// </summary>
    public static async Task<bool> TryAddAsync(this NotificationsDbContext db, NotificationDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        var kind = EnumCodes.ToCode(delivery.Kind);
        var status = EnumCodes.ToCode(delivery.Status);
        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO notifications.notification_deliveries
                (id, user_id, kind, template, topic, data, status, attempts, next_attempt_at, created_at)
            VALUES ({delivery.Id}, {delivery.UserId}, {kind}, {delivery.Template}, {delivery.Topic}, {delivery.Data}::jsonb,
                {status}, {delivery.Attempts}, {delivery.NextAttemptAt}, {delivery.CreatedAt})
            ON CONFLICT (user_id, topic) DO NOTHING
            """,
            cancellationToken);
        return inserted == 1;
    }
}
