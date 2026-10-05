using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Text;

namespace PolvorApp.ComplianceInsights.Endpoints;

/// <summary>
/// The edition trends of the caller's scope (add-statistics-trends, design D2, D3): the counts come from
/// the comparsa orders, the gender of the registry today is joined here by arquebusier id, and only
/// counts leave this class (docs/compliance.md: gender for equality reports only).
/// </summary>
internal sealed class TrendQueries(IComparsaScope scope, ScopedFacts scopedFacts, ICatalogDirectory catalog, IEditionTrends trends)
{
    /// <summary>The trends, or null when <paramref name="comparsaId"/> is unknown or outside the scope (BR-12).</summary>
    public async Task<TrendsResponse?> TrendsAsync(Guid? comparsaId, CancellationToken cancellationToken)
    {
        var access = await scope.GetAccessAsync(cancellationToken);
        if (comparsaId is { } wanted && (!access.CanAccess(wanted) || await catalog.FindComparsaAsync(wanted, cancellationToken) is null))
        {
            return null;
        }

        var reading = comparsaId is { } only ? ComparsaAccess.Only([only]) : access;
        var rows = await trends.ListAsync(reading.IsAll ? null : [.. reading.ComparsaIds], cancellationToken);
        var genders = (await scopedFacts.ReadAsync(reading, cancellationToken)).ToDictionary(f => f.Facts.ArquebusierId, f => f.Facts.Gender);
        var kinds = await KindsAsync(rows, cancellationToken);
        var perComparsa = comparsaId is null && (access.IsAll || access.ComparsaIds.Count > 1);
        var comparsas = perComparsa ? await NamesAsync(rows, cancellationToken) : [];

        return new TrendsResponse(
            [.. rows.Select(row => new TrendRowResponse(
                row.Year,
                row.Provisional,
                row.Active,
                row.Reserve,
                GendersOf(row, genders),
                row.FirstYear,
                row.PowderKg,
                row.CapsBoxes,
                new WeaponSourceCounts(row.Owned, row.Rental, row.Loan, row.NoWeapon),
                [.. Enum.GetValues<WeaponKind>().Select(kind => new WeaponKindCount(
                    kind, row.RentalsByModel.Where(r => kinds[r.Key] == kind).Sum(r => r.Value)))],
                row.FlaskRentals,
                perComparsa
                    ? [.. row.ActiveByComparsa.Select(c => new ComparsaTrendCount(c.Key, c.Value)).OrderBy(c => c.ComparsaId)]
                    : []))],
            comparsas);
    }

    /// <summary>The genders of the registry today; an entry without its arquebusier in the scope is unknown.</summary>
    private static TrendGenderCounts GendersOf(EditionTrendRow row, Dictionary<Guid, Gender> genders)
    {
        var known = row.ActiveArquebusierIds.Where(genders.ContainsKey).Select(id => genders[id]).ToList();
        return new TrendGenderCounts(
            known.Count(g => g == Gender.Male),
            known.Count(g => g == Gender.Female),
            known.Count(g => g == Gender.Unspecified),
            row.Active - known.Count);
    }

    private async Task<Dictionary<Guid, WeaponKind>> KindsAsync(IReadOnlyList<EditionTrendRow> rows, CancellationToken cancellationToken)
    {
        Guid[] modelIds = [.. rows.SelectMany(r => r.RentalsByModel.Keys).Distinct()];
        var kinds = (await catalog.FindWeaponModelsAsync(modelIds, cancellationToken)).ToDictionary(m => m.Id, m => m.Kind);
        var missing = modelIds.FirstOrDefault(id => !kinds.ContainsKey(id));
        return missing == Guid.Empty ? kinds : throw new InvalidOperationException($"Weapon model {missing} is missing from the catalog.");
    }

    private async Task<IReadOnlyList<TrendComparsaResponse>> NamesAsync(IReadOnlyList<EditionTrendRow> rows, CancellationToken cancellationToken)
    {
        Guid[] ids = [.. rows.SelectMany(r => r.ActiveByComparsa.Keys).Distinct()];
        var found = await catalog.FindComparsasAsync(ids, cancellationToken);
        return [.. found.Select(c => new TrendComparsaResponse(c.Id, c.Name)).OrderBy(c => c.Name, SpanishOrder.Names)];
    }
}
