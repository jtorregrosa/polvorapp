using PolvorApp.ComplianceInsights.Contracts;
using PolvorApp.ComplianceInsights.Statistics;
using PolvorApp.FederationCatalog.Contracts;

namespace PolvorApp.ComplianceInsights.Endpoints;

/// <summary>The warning summary within the caller's scope (spec: Warning summary).</summary>
/// <param name="Active">Arquebusiers with status ACTIVE.</param>
/// <param name="Reserve">Arquebusiers with status RESERVE.</param>
/// <param name="WithWarnings">Arquebusiers with at least one warning, each counted once.</param>
/// <param name="Warnings">Every warning in rule order, with how many arquebusiers have it (zero included).</param>
internal sealed record ComplianceSummaryResponse(int Active, int Reserve, int WithWarnings, IReadOnlyList<WarningCountResponse> Warnings);

/// <summary>How many arquebusiers have a warning.</summary>
/// <param name="Code">The warning.</param>
/// <param name="Count">Arquebusiers that have it.</param>
internal sealed record WarningCountResponse(ComplianceWarning Code, int Count);

/// <summary>
/// Statistics and equality report of the caller's scope (spec: Statistics (UC-07)): counts and
/// comparsa names only, never data of an arquebusier.
/// </summary>
/// <param name="Total">Arquebusiers counted.</param>
/// <param name="Active">Of them, ACTIVE.</param>
/// <param name="Reserve">Of them, RESERVE.</param>
/// <param name="Gender">Arquebusiers of each gender.</param>
/// <param name="AgeBrackets">Every age bracket, youngest first, by gender.</param>
/// <param name="Course">Training course done and not done, by gender.</param>
/// <param name="Licenses">Arquebusiers in each license state.</param>
/// <param name="OwnedWeapons">Arquebusiers with and without an owned weapon, and weapons by kind.</param>
/// <param name="Comparsas">One row per comparsa with arquebusiers, sorted by name; empty with a comparsa filter or a single comparsa in scope.</param>
internal sealed record ComplianceStatisticsResponse(
    int Total,
    int Active,
    int Reserve,
    GenderCounts Gender,
    IReadOnlyList<AgeBracketCounts> AgeBrackets,
    CourseCounts Course,
    LicenseStateCounts Licenses,
    OwnedWeaponCounts OwnedWeapons,
    IReadOnlyList<ComparsaStatisticsResponse> Comparsas);

/// <summary>Counts by gender.</summary>
/// <param name="Male">Gender MALE.</param>
/// <param name="Female">Gender FEMALE.</param>
/// <param name="Unspecified">Gender UNSPECIFIED.</param>
internal sealed record GenderCounts(int Male, int Female, int Unspecified);

/// <summary>An age bracket by gender.</summary>
/// <param name="Bracket">The bracket.</param>
/// <param name="Counts">Arquebusiers in it, by gender.</param>
internal sealed record AgeBracketCounts(AgeBracket Bracket, GenderCounts Counts);

/// <summary>The training course by gender.</summary>
/// <param name="Done">With the course done.</param>
/// <param name="NotDone">Without the course.</param>
internal sealed record CourseCounts(GenderCounts Done, GenderCounts NotDone);

/// <summary>Arquebusiers in each license state.</summary>
/// <param name="Valid">Valid and not expiring soon.</param>
/// <param name="Expiring">Valid but expiring soon (the warning LICENSE_EXPIRING).</param>
/// <param name="Expired">Expired.</param>
/// <param name="Pending">Pending.</param>
/// <param name="None">Without a license.</param>
internal sealed record LicenseStateCounts(int Valid, int Expiring, int Expired, int Pending, int None);

/// <summary>Owned weapons.</summary>
/// <param name="WithWeapon">Arquebusiers with at least one owned weapon, by gender.</param>
/// <param name="WithoutWeapon">Arquebusiers without any, by gender.</param>
/// <param name="ByKind">Owned weapons of each kind, every kind included.</param>
internal sealed record OwnedWeaponCounts(GenderCounts WithWeapon, GenderCounts WithoutWeapon, IReadOnlyList<WeaponKindCount> ByKind);

/// <summary>Owned weapons of a kind.</summary>
/// <param name="Kind">The weapon kind.</param>
/// <param name="Count">Owned weapons of that kind.</param>
internal sealed record WeaponKindCount(WeaponKind Kind, int Count);

/// <summary>The figures of one comparsa.</summary>
/// <param name="ComparsaId">The comparsa.</param>
/// <param name="Name">Its name.</param>
/// <param name="Total">Arquebusiers counted.</param>
/// <param name="Active">Of them, ACTIVE.</param>
/// <param name="Reserve">Of them, RESERVE.</param>
/// <param name="Gender">By gender.</param>
/// <param name="WithWarnings">With at least one compliance warning.</param>
internal sealed record ComparsaStatisticsResponse(Guid ComparsaId, string Name, int Total, int Active, int Reserve, GenderCounts Gender, int WithWarnings);
