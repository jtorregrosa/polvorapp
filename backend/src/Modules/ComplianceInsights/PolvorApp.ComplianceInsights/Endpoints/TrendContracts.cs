using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.FederationCatalog.Contracts;

namespace PolvorApp.ComplianceInsights.Endpoints;

/// <summary>The participation trends of the caller's scope (spec: Edition trends (UC-07)): counts only.</summary>
/// <param name="Rows">One row per started edition, the 10 most recent, oldest first.</param>
/// <param name="Comparsas">The comparsas named in the rows' per-comparsa figures; empty without them.</param>
internal sealed record TrendsResponse(IReadOnlyList<TrendRowResponse> Rows, IReadOnlyList<TrendComparsaResponse> Comparsas);

/// <summary>One edition's figures.</summary>
/// <param name="Year">The edition's year.</param>
/// <param name="Provisional">The edition is in progress: its figures can still change.</param>
/// <param name="Active"><c>ACTIVE</c> entries.</param>
/// <param name="Reserve"><c>RESERVE</c> entries.</param>
/// <param name="Gender"><c>ACTIVE</c> entries by the gender of the registry today; <c>Unknown</c> when no longer in it.</param>
/// <param name="FirstYear"><c>ACTIVE</c> entries in their first year, or null while unknown (the first edition with orders).</param>
/// <param name="PowderKg">Powder ordered, in kilograms.</param>
/// <param name="CapsBoxes">Caps boxes ordered.</param>
/// <param name="WeaponSources"><c>ACTIVE</c> entries by weapon source.</param>
/// <param name="RentalsByKind">Rented weapons by kind, every kind included.</param>
/// <param name="FlaskRentals">Rented flasks.</param>
/// <param name="Comparsas"><c>ACTIVE</c> entries per comparsa, only for several comparsas without a filter.</param>
internal sealed record TrendRowResponse(
    int Year,
    bool Provisional,
    int Active,
    int Reserve,
    TrendGenderCounts Gender,
    int? FirstYear,
    int PowderKg,
    int CapsBoxes,
    WeaponSourceCounts WeaponSources,
    IReadOnlyList<WeaponKindCount> RentalsByKind,
    int FlaskRentals,
    IReadOnlyList<ComparsaTrendCount> Comparsas);

/// <summary><c>ACTIVE</c> entries by gender, with those whose arquebusier is no longer in the registry.</summary>
internal sealed record TrendGenderCounts(int Male, int Female, int Unspecified, int Unknown);

/// <summary><c>ACTIVE</c> entries by weapon source.</summary>
internal sealed record WeaponSourceCounts(int Owned, int Rental, int Loan, int None);

/// <summary>A comparsa's <c>ACTIVE</c> entries in one edition.</summary>
internal sealed record ComparsaTrendCount(Guid ComparsaId, int Active);

/// <summary>A comparsa named in the per-comparsa figures.</summary>
internal sealed record TrendComparsaResponse(Guid Id, string Name);
