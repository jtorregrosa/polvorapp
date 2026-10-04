using Microsoft.EntityFrameworkCore;
using PolvorApp.Notifications.Contracts;

namespace PolvorApp.Notifications;

/// <summary>Adds events to the caller's unit of work (design D2); the caller's <c>SaveChanges</c> stores them.</summary>
internal sealed class NotificationOutbox(TimeProvider time) : INotificationOutbox
{
    public void Record(DbContext context, NotificationEvent notificationEvent)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(notificationEvent);
        var now = time.GetUtcNow();
        context.Set<NotificationEventEntry>().Add(new NotificationEventEntry
        {
            Id = Guid.CreateVersion7(now),
            Type = notificationEvent.Type,
            EditionId = notificationEvent.EditionId,
            ComparsaId = notificationEvent.ComparsaId,
            OrderId = notificationEvent.OrderId,
            OccurredAt = now,
        });
    }
}
