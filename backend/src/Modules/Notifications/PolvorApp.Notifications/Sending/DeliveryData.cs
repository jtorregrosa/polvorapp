using System.Text.Json;
using System.Text.Json.Serialization;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.Notifications.Emails;

namespace PolvorApp.Notifications.Sending;

/// <summary>One comparsa's counts in a license digest, frozen when the digest was worked out (design D8).</summary>
internal sealed record DigestEntry(Guid ComparsaId, int Missing, int Pending, int Expired, int ExpiringSoon);

/// <summary>
/// What a delivery is about (design D3): identifiers, dates, counts and the state it announces, so the
/// email can be rendered and checked when it is sent. Never names, addresses, titles or reasons.
/// </summary>
internal sealed record DeliveryData
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public Guid? EditionId { get; init; }

    public Guid? ComparsaId { get; init; }

    public Guid? OrderId { get; init; }

    public Guid? MilestoneId { get; init; }

    /// <summary>The order status a review or submission email announces.</summary>
    public OrderStatus? OrderStatus { get; init; }

    /// <summary>Whether the orders were opened (true) or closed (false).</summary>
    public bool? OrdersOpen { get; init; }

    /// <summary>The planned close date, the milestone's date, or the digest date.</summary>
    public DateOnly? Date { get; init; }

    /// <summary>The order's state when a close reminder was worked out.</summary>
    public PendingOrderState? State { get; init; }

    public IReadOnlyList<DigestEntry>? Digest { get; init; }

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    public static DeliveryData Parse(string json) =>
        JsonSerializer.Deserialize<DeliveryData>(json, Json) ?? throw new InvalidOperationException("A delivery has no data.");
}
