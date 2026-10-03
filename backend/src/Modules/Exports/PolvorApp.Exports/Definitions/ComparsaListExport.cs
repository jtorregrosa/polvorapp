using PolvorApp.ComparsaOrders.Contracts;

namespace PolvorApp.Exports.Definitions;

/// <summary>
/// One comparsa's list (spec: Comparsa list export): every entry of its order with the order's totals,
/// in the user's language; a draft while the order is not validated (maintainer decision).
/// </summary>
internal sealed class ComparsaListExport : IExportDefinition
{
    public string Name => "comparsa-list";

    public string Version => ExportRows.ProvisionalVersion;

    public bool Provisional => true;

    public ExportAudience Audience => ExportAudience.Comparsa;

    /// <param name="data">Exactly one order: the comparsa's.</param>
    /// <param name="texts">The user's language.</param>
    public ExportTable Build(ExportData data, ExportTexts texts)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(texts);
        var order = data.Orders.Count == 1
            ? data.Orders[0]
            : throw new ArgumentException("A comparsa list is built from exactly one order.", nameof(data));
        var comparsa = ExportRows.Comparsa(data, order.ComparsaId);
        OrderStatus? draft = order.Status == OrderStatus.Validated ? null : order.Status;
        var entries = ExportRows.Of(data);
        var rows = entries.Select(row => Row(row, data, texts)).ToList();
        return new ExportTable(
            ExportRows.FileStem(data.EditionYear, Name, Provisional, comparsa, draft is not null),
            texts.Format(texts.ComparsaListTitle, data.EditionYear, comparsa),
            ExportRows.Notices(texts, Provisional, draft),
            texts.Format(texts.VersionLine, Name, Version),
            [
                new(texts.Name, ExportCellType.Text),
                new(texts.NationalId, ExportCellType.Text),
                new(texts.FederationId, ExportCellType.Integer),
                new(texts.Status, ExportCellType.Text),
                new(texts.PowderKg, ExportCellType.Integer),
                new(texts.CapsBoxes, ExportCellType.Integer),
                new(texts.CapsKind, ExportCellType.Text),
                new(texts.Weapon, ExportCellType.Text),
                new(texts.Flask, ExportCellType.Text),
            ],
            rows,
            [texts.Total, null, null, null, entries.Sum(r => r.Entry.PowderKg), entries.Sum(r => r.Entry.CapsBoxes), null, null, null]);
    }

    private static IReadOnlyList<object?> Row(ExportRow row, ExportData data, ExportTexts texts)
    {
        var entry = row.Entry;
        return
        [
            row.Name,
            row.NationalId,
            row.FederationId,
            entry.IsActive ? texts.Active : texts.Reserve,
            entry.PowderKg,
            entry.CapsBoxes,
            entry.CapsType is { } type && entry.CapsBoxes > 0 ? texts.CapsTypes[type] : null,
            Weapon(entry, data, texts),
            texts.Flasks[entry.Flask],
        ];
    }

    /// <summary>"Propia: MODEL NUMBER", "Alquiler: MODEL", "Cesión: MODEL NUMBER, cedida por NAME", "Cesión: sin datos" or "Sin arma".</summary>
    private static string Weapon(ExportedEntry entry, ExportData data, ExportTexts texts)
    {
        var source = texts.WeaponSources[entry.WeaponSource];
        var detail = entry.WeaponSource switch
        {
            WeaponSource.Owned when ExportRows.OwnedWeapon(data, entry) is { } owned => Describe(data, owned.WeaponModelId, owned.WeaponNumber),
            WeaponSource.Rental => ExportRows.Model(data, entry.RentalWeaponModelId),
            WeaponSource.Loan when entry.Loan is { } loan => Join(
                ", ",
                Describe(data, loan.Weapon.WeaponModelId, loan.Weapon.WeaponNumber),
                texts.Format(texts.LentBy, ExportRows.PersonName(loan.Lender.LastName, loan.Lender.FirstName))),
            _ => null,
        };
        if (entry.WeaponSource == WeaponSource.None)
        {
            return source;
        }

        return $"{source}: {(string.IsNullOrWhiteSpace(detail) ? texts.NoDetails : detail)}";
    }

    private static string Join(string separator, params string?[] parts) =>
        string.Join(separator, parts.Where(part => !string.IsNullOrWhiteSpace(part)));

    private static string Describe(ExportData data, Guid? modelId, string? number) =>
        Join(" ", ExportRows.Model(data, modelId), number);
}
