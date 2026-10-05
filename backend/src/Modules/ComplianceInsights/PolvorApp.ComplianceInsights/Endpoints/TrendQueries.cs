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
        var genders = await scopedFacts.GendersOfAsync([.. rows.SelectMany(r => r.ActiveArquebusierIds).Distinct()], cancellationToken);
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
                    ? [.. comparsas.Select(c => new ComparsaTrendCount(c.Id, row.ActiveByComparsa.GetValueOrDefault(c.Id)))]
                    : []))],
            comparsas);
    }

    /// <summary>
    /// The genders of the registry today; an entry whose arquebusier is no longer in it is unknown. The
    /// counts come from separate reads, so a concurrent save could make them disagree by one: unknown
    /// never goes below zero.
    /// </summary>
    private static TrendGenderCounts GendersOf(EditionTrendRow row, IReadOnlyDictionary<Guid, Gender> genders)
    {
        List<Gender> known = [];
        foreach (var id in row.ActiveArquebusierIds)
        {
            if (genders.TryGetValue(id, out var gender))
            {
                known.Add(gender);
            }
        }

        return new TrendGenderCounts(
            known.Count(g => g == Gender.Male),
            known.Count(g => g == Gender.Female),
            known.Count(g => g == Gender.Unspecified),
            Math.Max(0, row.Active - known.Count));
    }

    private async Task<Dictionary<Guid, WeaponKind>> KindsAsync(IReadOnlyList<EditionTrendRow> rows, CancellationToken cancellationToken)
    {
        Guid[] modelIds = [.. rows.SelectMany(r => r.RentalsByModel.Keys).Distinct()];
        var kinds = (await catalog.FindWeaponModelsAsync(modelIds, cancellationToken)).ToDictionary(m => m.Id, m => m.Kind);
        // A rented model cannot be deleted while entries use it, so a missing one is an integrity fault.
        foreach (var id in modelIds)
        {
            if (!kinds.ContainsKey(id))
            {
                throw new InvalidOperationException($"Weapon model {id} is missing from the catalog.");
            }
        }

        return kinds;
    }

    private async Task<IReadOnlyList<TrendComparsaResponse>> NamesAsync(IReadOnlyList<EditionTrendRow> rows, CancellationToken cancellationToken)
    {
        Guid[] ids = [.. rows.SelectMany(r => r.ActiveByComparsa.Keys).Distinct()];
        var found = await catalog.FindComparsasAsync(ids, cancellationToken);
        return [.. found.Select(c => new TrendComparsaResponse(c.Id, c.Name)).OrderBy(c => c.Name, SpanishOrder.Names)];
    }
}
