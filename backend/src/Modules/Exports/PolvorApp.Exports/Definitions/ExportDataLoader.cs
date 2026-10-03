using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.FederationCatalog.Contracts;

namespace PolvorApp.Exports.Definitions;

/// <summary>
/// Reads what the definitions need, once per request (design D3): the orders, the comparsa names and
/// model labels of the catalogue, and the arquebusiers still in the registry. It applies no scope: the
/// caller passes only what the user may export (BR-12).
/// </summary>
internal sealed class ExportDataLoader(IOrderExports orders, ICatalogDirectory catalog, IArquebusierRoster roster)
{
    /// <summary>The validated orders of the edition, for the recipient exports.</summary>
    public async Task<ExportData> LoadValidatedAsync(int editionYear, Guid editionId, CancellationToken cancellationToken) =>
        await WithReferencesAsync(editionYear, await orders.ListValidatedAsync(editionId, cancellationToken), cancellationToken);

    /// <summary>The comparsa's order in any status, or null when the comparsa has not prepared one.</summary>
    public async Task<ExportData?> LoadComparsaAsync(int editionYear, Guid editionId, Guid comparsaId, CancellationToken cancellationToken) =>
        await orders.FindAsync(editionId, comparsaId, cancellationToken) is { } order
            ? await WithReferencesAsync(editionYear, [order], cancellationToken)
            : null;

    private async Task<ExportData> WithReferencesAsync(int editionYear, IReadOnlyList<ExportedOrder> found, CancellationToken cancellationToken)
    {
        var entries = found.SelectMany(o => o.Entries).ToList();
        Guid[] arquebusierIds = [.. entries.Select(e => e.ArquebusierId).OfType<Guid>().Distinct()];
        var live = arquebusierIds.Length == 0 ? [] : await roster.FindManyAsync(arquebusierIds, cancellationToken);
        Guid[] comparsaIds = [.. found.Select(o => o.ComparsaId).Distinct()];
        var comparsas = comparsaIds.Length == 0 ? [] : await catalog.FindComparsasAsync(comparsaIds, cancellationToken);
        Guid[] modelIds =
        [
            .. entries.Select(e => e.RentalWeaponModelId)
                .Concat(entries.Select(e => e.OwnedWeapon?.WeaponModelId))
                .Concat(entries.Select(e => e.Loan?.Weapon.WeaponModelId))
                .Concat(live.SelectMany(a => a.Weapons).Select(w => (Guid?)w.WeaponModelId))
                .OfType<Guid>()
                .Distinct(),
        ];
        var models = modelIds.Length == 0 ? [] : await catalog.FindWeaponModelsAsync(modelIds, cancellationToken);
        return new ExportData(
            editionYear,
            found,
            comparsas.ToDictionary(c => c.Id, c => c.Name),
            models.ToDictionary(m => m.Id, m => m.Label),
            live.ToDictionary(a => a.Id));
    }
}
