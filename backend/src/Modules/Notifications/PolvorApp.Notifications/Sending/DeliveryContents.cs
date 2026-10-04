using System.Globalization;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.Notifications.Emails;
using PolvorApp.Notifications.Persistence;
using PolvorApp.Notifications.Rules;
using PolvorApp.SharedKernel.Time;

namespace PolvorApp.Notifications.Sending;

/// <summary>The email of a delivery, or why it is not sent (logged and stored as the delivery's <c>last_error</c>).</summary>
internal sealed record PreparedContent(NotificationContent? Content, string? SkipReason)
{
    public static PreparedContent Skip(string reason) => new(null, reason);
}

/// <summary>
/// Decides, when a delivery is about to be sent, whether it still holds for this recipient and what the
/// email says (design D6, D7). It reads the current edition, orders, comparsas and milestones through
/// their contracts, and checks the recipient's current role and assignments against the template: a
/// FiringChief hears only about the comparsas assigned to them (BR-12), an Admin only about
/// submissions. Data a template needs and lacks is a bug, not "no longer holds": it throws.
/// </summary>
internal sealed class DeliveryContents(IEditionDirectory editions, IEditionEntries entries, ICatalogDirectory catalog, TimeProvider time)
{
    private static readonly StringComparer SpanishOrder = StringComparer.Create(CultureInfo.GetCultureInfo("es-ES"), ignoreCase: true);

    public async Task<PreparedContent> BuildAsync(NotificationDelivery delivery, UserSummary recipient, IReadOnlySet<Guid> comparsas, CancellationToken cancellationToken)
    {
        var data = DeliveryData.Parse(delivery.Data);
        return delivery.Template switch
        {
            NotificationTemplates.OrdersOpened or NotificationTemplates.OrdersClosed =>
                await OrderWindowAsync(data, delivery.Template, comparsas, cancellationToken),
            NotificationTemplates.OrdersClosingSoon or NotificationTemplates.OrdersClosingTomorrow =>
                await ClosingAsync(data, delivery.Template, comparsas, cancellationToken),
            NotificationTemplates.OrderSubmitted or NotificationTemplates.OrderReturned or NotificationTemplates.OrderValidated =>
                await OrderStatusAsync(data, delivery.Template, recipient, comparsas, cancellationToken),
            NotificationTemplates.MilestoneReminder => await MilestoneAsync(data, recipient, cancellationToken),
            NotificationTemplates.LicenseDigest => await DigestAsync(data, comparsas, cancellationToken),
            _ => throw new InvalidOperationException($"Unknown notification template {delivery.Template}."),
        };
    }

    /// <summary>To FiringChiefs with at least one active comparsa, while the orders are still in the state announced.</summary>
    private async Task<PreparedContent> OrderWindowAsync(DeliveryData data, string template, IReadOnlySet<Guid> comparsas, CancellationToken cancellationToken)
    {
        var open = Required(data.OrdersOpen);
        if (comparsas.Count == 0)
        {
            return PreparedContent.Skip("NoComparsa");
        }

        var edition = await editions.FindAsync(Required(data.EditionId), cancellationToken);
        return (open, edition) switch
        {
            (_, null) => PreparedContent.Skip("EditionGone"),
            (true, { OrdersOpen: true, OrdersCloseOn: { } closeOn } opened) when template == NotificationTemplates.OrdersOpened =>
                new(new NotificationContent.OrdersOpened(opened.Year, closeOn), null),
            (false, { OrdersOpen: false, Status: EditionStatus.InProgress or EditionStatus.Closed } closed) when template == NotificationTemplates.OrdersClosed =>
                new(new NotificationContent.OrdersClosed(closed.Year), null),
            _ => PreparedContent.Skip("OrdersChanged"),
        };
    }

    private async Task<PreparedContent> ClosingAsync(DeliveryData data, string template, IReadOnlySet<Guid> comparsas, CancellationToken cancellationToken)
    {
        var comparsaId = Required(data.ComparsaId);
        var closeOn = Required(data.Date);
        var today = FederationCalendar.Today(time);
        if (!comparsas.Contains(comparsaId))
        {
            return PreparedContent.Skip("NotAssigned");
        }

        if (closeOn < today
            || await editions.FindAsync(Required(data.EditionId), cancellationToken) is not { OrdersOpen: true } edition
            || edition.OrdersCloseOn != closeOn)
        {
            return PreparedContent.Skip("OrdersChanged");
        }

        var order = (await entries.ListOrdersAsync(edition.Id, cancellationToken)).FirstOrDefault(o => o.ComparsaId == comparsaId);
        PendingOrderState? state = order?.Status switch
        {
            null => PendingOrderState.NotPrepared,
            OrderStatus.Draft => PendingOrderState.Draft,
            OrderStatus.Returned => PendingOrderState.Returned,
            _ => null,
        };
        if (state is not { } pending)
        {
            return PreparedContent.Skip("OrderSubmitted");
        }

        if (await ComparsaNameAsync(comparsaId, cancellationToken) is not { } comparsa)
        {
            return PreparedContent.Skip("ComparsaGone");
        }

        var reminder = template == NotificationTemplates.OrdersClosingSoon ? CloseReminder.Week : CloseReminder.LastDays;
        return new(new NotificationContent.OrdersClosing(reminder, comparsa, edition.Year, closeOn, today, pending, order?.OrderId), null);
    }

    /// <summary>A submission goes to Admins; a return or validation to the comparsa's FiringChiefs (spec: Order status emails).</summary>
    private async Task<PreparedContent> OrderStatusAsync(
        DeliveryData data, string template, UserSummary recipient, IReadOnlySet<Guid> comparsas, CancellationToken cancellationToken)
    {
        var comparsaId = Required(data.ComparsaId);
        var orderId = Required(data.OrderId);
        var expectedRole = template == NotificationTemplates.OrderSubmitted ? UserRole.Admin : UserRole.FiringChief;
        if (recipient.Role != expectedRole)
        {
            return PreparedContent.Skip("RoleChanged");
        }

        if (expectedRole == UserRole.FiringChief && !comparsas.Contains(comparsaId))
        {
            return PreparedContent.Skip("NotAssigned");
        }

        var edition = await editions.FindAsync(Required(data.EditionId), cancellationToken);
        var order = edition is null ? null : (await entries.ListOrdersAsync(edition.Id, cancellationToken)).FirstOrDefault(o => o.OrderId == orderId);
        if (edition is null || order is null || order.ComparsaId != comparsaId)
        {
            return PreparedContent.Skip("OrderGone");
        }

        if (order.Status != data.OrderStatus)
        {
            return PreparedContent.Skip("OrderChanged");
        }

        if (await ComparsaNameAsync(comparsaId, cancellationToken) is not { } comparsa)
        {
            return PreparedContent.Skip("ComparsaGone");
        }

        NotificationContent content = template switch
        {
            NotificationTemplates.OrderSubmitted => new NotificationContent.OrderSubmitted(comparsa, edition.Year, orderId),
            NotificationTemplates.OrderReturned => new NotificationContent.OrderReturned(comparsa, edition.Year, orderId),
            _ => new NotificationContent.OrderValidated(comparsa, edition.Year, orderId),
        };
        return new(content, null);
    }

    private async Task<PreparedContent> MilestoneAsync(DeliveryData data, UserSummary recipient, CancellationToken cancellationToken)
    {
        var date = Required(data.Date);
        var milestoneId = Required(data.MilestoneId);
        if (date < FederationCalendar.Today(time))
        {
            return PreparedContent.Skip("Past");
        }

        // Still marked, still on that date, edition not closed; a FiringChief only once the edition is in progress.
        var milestone = (await editions.ListMilestonesToNotifyAsync(date, date, cancellationToken)).FirstOrDefault(m => m.Id == milestoneId);
        if (milestone is null)
        {
            return PreparedContent.Skip("MilestoneChanged");
        }

        return recipient.Role == UserRole.FiringChief && !Schedule.RemindsFiringChiefs(milestone.EditionStatus)
            ? PreparedContent.Skip("EditionNotVisible")
            : new(new NotificationContent.MilestoneReminder(milestone.Title, milestone.Date, milestone.EditionYear, milestone.EditionId), null);
    }

    /// <summary>The counts of that morning, for the comparsas still assigned; nothing left means no digest.</summary>
    private async Task<PreparedContent> DigestAsync(DeliveryData data, IReadOnlySet<Guid> comparsas, CancellationToken cancellationToken)
    {
        var digest = data.Digest ?? throw new InvalidOperationException("A license digest delivery has no counts.");
        var kept = digest.Where(d => comparsas.Contains(d.ComparsaId)).ToList();
        var names = (await catalog.FindComparsasAsync(kept.Select(d => d.ComparsaId).ToList(), cancellationToken)).ToDictionary(c => c.Id, c => c.Name);
        var lines = kept
            .Where(d => names.ContainsKey(d.ComparsaId))
            .Select(d => (names[d.ComparsaId], new LicenseDigestCounts(d.ComparsaId, d.Missing, d.Pending, d.Expired, d.ExpiringSoon)))
            .OrderBy(line => line.Item1, SpanishOrder)
            .ToList();
        return lines.Count == 0
            ? PreparedContent.Skip("NotAssigned")
            : new(new NotificationContent.LicenseDigest(Required(data.Date), lines), null);
    }

    private async Task<string?> ComparsaNameAsync(Guid comparsaId, CancellationToken cancellationToken) =>
        (await catalog.FindComparsaAsync(comparsaId, cancellationToken))?.Name;

    private static T Required<T>(T? value)
        where T : struct =>
        value ?? throw new InvalidOperationException("A delivery is missing data its template needs.");
}
