using PolvorApp.FestivalEditions.Contracts;

namespace PolvorApp.FestivalEditions.Editions;

/// <summary>
/// One yearly festival edition (glossary: <c>FestivalEdition</c>; spec: Festival editions (UC-10)).
/// The order window dates are the published plan; only <see cref="OrdersOpen"/> decides whether
/// FiringChiefs may edit orders (BR-10). The four prices are the <c>EditionPrices</c> (design D1).
/// </summary>
internal sealed class FestivalEdition
{
    public const int MinYear = 2000;
    public const int MaxYear = 2100;

    public required Guid Id { get; init; }

    /// <summary>Unique; never changes after creation.</summary>
    public required int Year { get; init; }

    public required DateOnly FestivalStartsOn { get; set; }

    public required DateOnly FestivalEndsOn { get; set; }

    public DateOnly? OrdersOpenOn { get; set; }

    public DateOnly? OrdersCloseOn { get; set; }

    public EditionStatus Status { get; set; } = EditionStatus.Draft;

    /// <summary>Only an edition in progress may have open orders (the database checks it too).</summary>
    public bool OrdersOpen { get; set; }

    public decimal? PowderPerKg { get; set; }

    public decimal? CapsBox { get; set; }

    public decimal? WeaponRental { get; set; }

    public decimal? FlaskRental { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? StatusChangedAt { get; set; }

    /// <summary>PostgreSQL <c>xmin</c>: an edit or move based on an outdated version is refused.</summary>
    public uint Version { get; set; }
}
