using System.Text.Json.Serialization;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.Notifications.Contracts;

/// <summary>What happened, for the notifications it causes (design D2).</summary>
[JsonConverter(typeof(CodeEnumConverter<NotificationEventType>))]
public enum NotificationEventType
{
    /// <summary>An Admin opened the orders of the edition in progress.</summary>
    [JsonStringEnumMemberName("ORDERS_OPENED")]
    OrdersOpened,

    /// <summary>An Admin closed the orders of the edition in progress.</summary>
    [JsonStringEnumMemberName("ORDERS_CLOSED")]
    OrdersClosed,

    /// <summary>A FiringChief submitted an order (an Admin's submission on the comparsa's behalf is not an event).</summary>
    [JsonStringEnumMemberName("ORDER_SUBMITTED")]
    OrderSubmitted,

    /// <summary>An Admin returned an order.</summary>
    [JsonStringEnumMemberName("ORDER_RETURNED")]
    OrderReturned,

    /// <summary>An Admin validated an order.</summary>
    [JsonStringEnumMemberName("ORDER_VALIDATED")]
    OrderValidated,
}

/// <summary>
/// An event another module records with <see cref="INotificationOutbox"/> in the transaction of the
/// change that caused it (design D2). It holds identifiers only: never names, reasons or other
/// personal data, which are read when the email is rendered.
/// </summary>
public sealed record NotificationEvent
{
    private NotificationEvent(NotificationEventType type, Guid editionId, Guid? comparsaId, Guid? orderId) =>
        (Type, EditionId, ComparsaId, OrderId) = (type, editionId, comparsaId, orderId);

    public NotificationEventType Type { get; }

    public Guid EditionId { get; }

    /// <summary>The order's comparsa; null for the orders opened or closed.</summary>
    public Guid? ComparsaId { get; }

    /// <summary>The order; null for the orders opened or closed.</summary>
    public Guid? OrderId { get; }

    public static NotificationEvent OrdersOpened(Guid editionId) => new(NotificationEventType.OrdersOpened, editionId, null, null);

    public static NotificationEvent OrdersClosed(Guid editionId) => new(NotificationEventType.OrdersClosed, editionId, null, null);

    public static NotificationEvent OrderSubmitted(Guid editionId, Guid comparsaId, Guid orderId) =>
        new(NotificationEventType.OrderSubmitted, editionId, comparsaId, orderId);

    public static NotificationEvent OrderReturned(Guid editionId, Guid comparsaId, Guid orderId) =>
        new(NotificationEventType.OrderReturned, editionId, comparsaId, orderId);

    public static NotificationEvent OrderValidated(Guid editionId, Guid comparsaId, Guid orderId) =>
        new(NotificationEventType.OrderValidated, editionId, comparsaId, orderId);
}
