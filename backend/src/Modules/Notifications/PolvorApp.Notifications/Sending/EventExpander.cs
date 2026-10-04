using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.Notifications.Contracts;
using PolvorApp.Notifications.Emails;
using PolvorApp.Notifications.Persistence;
using PolvorApp.Notifications.Rules;

namespace PolvorApp.Notifications.Sending;

/// <summary>
/// Turns the events other modules recorded into one delivery per recipient (design D5, D6), one event per
/// transaction, oldest first. An event is locked <c>FOR UPDATE SKIP LOCKED</c>, so two expanders never
/// take the same one, and deliveries use <see cref="DeliveryStore.TryAddAsync"/>, so an event expanded
/// twice still gives one delivery each. An event that a later one about the same thing supersedes (the
/// orders toggled again, the order moved again) creates nothing: only the latest is told. An event that
/// cannot be planned is a bug: it is logged and set aside, so it never blocks the others.
/// </summary>
internal sealed partial class EventExpander(
    NotificationsDbContext db, IUserDirectory users, ICatalogDirectory catalog, TimeProvider time, ILogger<EventExpander> logger)
{
    public const int BatchSize = 50;

    /// <summary>Expands up to <see cref="BatchSize"/> waiting events; returns how many it took.</summary>
    public async Task<int> ExpandAsync(CancellationToken cancellationToken)
    {
        var recipients = await Recipients.LoadAsync(users, catalog, db, cancellationToken);
        var expanded = 0;
        while (expanded < BatchSize && await ExpandOneAsync(recipients, cancellationToken))
        {
            expanded++;
        }

        return expanded;
    }

    private async Task<bool> ExpandOneAsync(Recipients recipients, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.BeginWriteAsync(cancellationToken);
        var ids = await db.Database.SqlQuery<Guid>(
                $"SELECT id AS \"Value\" FROM notifications.notification_events WHERE processed_at IS NULL ORDER BY occurred_at, id LIMIT 1 FOR UPDATE SKIP LOCKED")
            .ToListAsync(cancellationToken);
        if (ids is not [var id])
        {
            return false;
        }

        var notificationEvent = await db.Events.SingleAsync(e => e.Id == id, cancellationToken);
        var now = time.GetUtcNow();
        if (await IsSupersededAsync(notificationEvent, cancellationToken))
        {
            LogSuperseded(logger, notificationEvent.Id, notificationEvent.Type);
        }
        else if (TryPlan(notificationEvent, recipients) is { } plan)
        {
            var data = plan.Data.Serialize();
            foreach (var user in plan.Audience)
            {
                await db.TryAddAsync(
                    new NotificationDelivery
                    {
                        Id = Guid.CreateVersion7(now),
                        UserId = user.Id,
                        Kind = plan.Kind,
                        Template = plan.Template,
                        Topic = Topics.Event(notificationEvent.Id),
                        Data = data,
                        NextAttemptAt = now,
                        CreatedAt = now,
                    },
                    cancellationToken);
            }
        }

        notificationEvent.ProcessedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <summary>A later event about the same orders window or the same order makes this one stale.</summary>
    private Task<bool> IsSupersededAsync(NotificationEventEntry e, CancellationToken cancellationToken) => e.Type switch
    {
        NotificationEventType.OrdersOpened or NotificationEventType.OrdersClosed => db.Events.AnyAsync(
            o => o.EditionId == e.EditionId && o.OccurredAt > e.OccurredAt
                && (o.Type == NotificationEventType.OrdersOpened || o.Type == NotificationEventType.OrdersClosed),
            cancellationToken),
        _ when e.OrderId is { } orderId => db.Events.AnyAsync(o => o.OrderId == orderId && o.OccurredAt > e.OccurredAt, cancellationToken),
        _ => Task.FromResult(false),
    };

    private (NotificationKind Kind, string Template, DeliveryData Data, List<UserSummary> Audience)? TryPlan(NotificationEventEntry e, Recipients recipients)
    {
        try
        {
            return Plan(e, recipients);
        }
        catch (InvalidOperationException)
        {
            LogUnplannable(logger, e.Id, e.Type);
            return null;
        }
    }

    /// <summary>The kind, template, data and candidates of an event (spec: Orders opened and closed, Order status emails).</summary>
    private static (NotificationKind Kind, string Template, DeliveryData Data, List<UserSummary> Audience) Plan(NotificationEventEntry e, Recipients recipients)
    {
        (NotificationKind, string, DeliveryData, IEnumerable<UserSummary>) plan = e.Type switch
        {
            NotificationEventType.OrdersOpened => (NotificationKind.OrderWindow, NotificationTemplates.OrdersOpened,
                new DeliveryData { EditionId = e.EditionId, OrdersOpen = true }, recipients.FiringChiefsWithComparsas(NotificationKind.OrderWindow)),
            NotificationEventType.OrdersClosed => (NotificationKind.OrderWindow, NotificationTemplates.OrdersClosed,
                new DeliveryData { EditionId = e.EditionId, OrdersOpen = false }, recipients.FiringChiefsWithComparsas(NotificationKind.OrderWindow)),
            NotificationEventType.OrderSubmitted => (NotificationKind.OrderStatus, NotificationTemplates.OrderSubmitted,
                Order(e, OrderStatus.Submitted), recipients.WithRole(UserRole.Admin, NotificationKind.OrderStatus)),
            NotificationEventType.OrderReturned => (NotificationKind.OrderStatus, NotificationTemplates.OrderReturned,
                Order(e, OrderStatus.Returned), recipients.FiringChiefsOf(ComparsaOf(e), NotificationKind.OrderStatus)),
            NotificationEventType.OrderValidated => (NotificationKind.OrderStatus, NotificationTemplates.OrderValidated,
                Order(e, OrderStatus.Validated), recipients.FiringChiefsOf(ComparsaOf(e), NotificationKind.OrderStatus)),
            _ => throw new InvalidOperationException($"Unknown notification event type {e.Type}."),
        };
        return (plan.Item1, plan.Item2, plan.Item3, plan.Item4.ToList());
    }

    private static DeliveryData Order(NotificationEventEntry e, OrderStatus status) => new()
    {
        EditionId = e.EditionId,
        ComparsaId = ComparsaOf(e),
        OrderId = e.OrderId ?? throw new InvalidOperationException("An order event has no order."),
        OrderStatus = status,
    };

    private static Guid ComparsaOf(NotificationEventEntry e) => e.ComparsaId ?? throw new InvalidOperationException("An order event has no comparsa.");

    [LoggerMessage(Level = LogLevel.Information, Message = "Notification event {EventId} ({Type}) superseded by a later one: nothing to send")]
    private static partial void LogSuperseded(ILogger logger, Guid eventId, NotificationEventType type);

    [LoggerMessage(Level = LogLevel.Error, Message = "Notification event {EventId} ({Type}) cannot be planned and is set aside")]
    private static partial void LogUnplannable(ILogger logger, Guid eventId, NotificationEventType type);
}
