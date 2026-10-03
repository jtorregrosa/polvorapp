using PolvorApp.ComparsaOrders.Contracts;

namespace PolvorApp.ComparsaOrders.Orders;

/// <summary>
/// The order of one comparsa for one festival edition (spec: Comparsa orders (UC-12)). There is at
/// most one per comparsa and edition, and it is never deleted: it is the edition's history.
/// </summary>
internal sealed class ComparsaOrder
{
    public const int ReturnReasonMaxLength = 500;

    public required Guid Id { get; init; }

    public required Guid EditionId { get; init; }

    /// <summary>Copied when the order is prepared; an edition's year never changes (design D2).</summary>
    public required int EditionYear { get; init; }

    public required Guid ComparsaId { get; init; }

    public OrderStatus Status { get; set; } = OrderStatus.Draft;

    public required DateTimeOffset PreparedAt { get; init; }

    public required Guid PreparedByUserId { get; init; }

    public DateTimeOffset? SubmittedAt { get; set; }

    public Guid? SubmittedByUserId { get; set; }

    /// <summary>A FiringChief confirmed that the arquebusiers meet the requirements (UC-14).</summary>
    public bool Attested { get; set; }

    /// <summary>An Admin submitted it on the comparsa's behalf, without the attestation.</summary>
    public bool SubmittedByAdmin { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    public Guid? ReviewedByUserId { get; set; }

    /// <summary>Set while the order is returned; cleared when it is submitted or validated again.</summary>
    public string? ReturnReason { get; set; }

    /// <summary>Touched by every write of the order or its entries, so the order's version changes (design D8).</summary>
    public required DateTimeOffset UpdatedAt { get; set; }

    /// <summary>PostgreSQL <c>xmin</c>: a submission or review based on an older version is rejected.</summary>
    public uint Version { get; set; }
}
