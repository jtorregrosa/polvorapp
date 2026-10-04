using PolvorApp.Notifications.Contracts;
using PolvorApp.Notifications.Rules;

namespace PolvorApp.Notifications.Emails;

/// <summary>The state an order is in when its planned close reminder is sent (design D9).</summary>
internal enum PendingOrderState
{
    NotPrepared,
    Draft,
    Returned,
}

/// <summary>
/// What one notification email says, with every name already read (design D7). It holds only what the
/// spec allows in an email: years, comparsa names, counts, dates, order states, milestone titles and the
/// identifiers that make the links. Never arquebusiers' data or return reasons.
/// </summary>
internal abstract record NotificationContent(NotificationKind Kind, string Template)
{
    public sealed record OrdersOpened(int Year, DateOnly CloseOn)
        : NotificationContent(NotificationKind.OrderWindow, NotificationTemplates.OrdersOpened);

    public sealed record OrdersClosed(int Year)
        : NotificationContent(NotificationKind.OrderWindow, NotificationTemplates.OrdersClosed);

    /// <param name="OrderId">The order, or null when the comparsa has not prepared it (the link opens the orders).</param>
    public sealed record OrdersClosing(CloseReminder Reminder, string Comparsa, int Year, DateOnly CloseOn, DateOnly Today, PendingOrderState State, Guid? OrderId)
        : NotificationContent(
            NotificationKind.OrderWindow,
            Reminder == CloseReminder.Week ? NotificationTemplates.OrdersClosingSoon : NotificationTemplates.OrdersClosingTomorrow);

    public sealed record OrderSubmitted(string Comparsa, int Year, Guid OrderId)
        : NotificationContent(NotificationKind.OrderStatus, NotificationTemplates.OrderSubmitted);

    public sealed record OrderReturned(string Comparsa, int Year, Guid OrderId)
        : NotificationContent(NotificationKind.OrderStatus, NotificationTemplates.OrderReturned);

    public sealed record OrderValidated(string Comparsa, int Year, Guid OrderId)
        : NotificationContent(NotificationKind.OrderStatus, NotificationTemplates.OrderValidated);

    public sealed record MilestoneReminder(string Title, DateOnly Date, int Year, Guid EditionId)
        : NotificationContent(NotificationKind.MilestoneReminder, NotificationTemplates.MilestoneReminder);

    /// <param name="Comparsas">Each comparsa with something to report, by name.</param>
    public sealed record LicenseDigest(DateOnly Date, IReadOnlyList<(string Comparsa, LicenseDigestCounts Counts)> Comparsas)
        : NotificationContent(NotificationKind.LicenseDigest, NotificationTemplates.LicenseDigest);
}

/// <summary>The culture-independent template names: the only part of an email that is logged.</summary>
internal static class NotificationTemplates
{
    public const string OrdersOpened = "OrdersOpened";
    public const string OrdersClosed = "OrdersClosed";
    public const string OrdersClosingSoon = "OrdersClosingSoon";
    public const string OrdersClosingTomorrow = "OrdersClosingTomorrow";
    public const string OrderSubmitted = "OrderSubmitted";
    public const string OrderReturned = "OrderReturned";
    public const string OrderValidated = "OrderValidated";
    public const string MilestoneReminder = "MilestoneReminder";
    public const string LicenseDigest = "LicenseDigest";
}
