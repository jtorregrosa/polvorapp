using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.SharedKernel.Text;

namespace PolvorApp.Exports.Definitions;

/// <summary>
/// The powder supplier's order (spec: Powder supplier export): powder and caps per comparsa and in
/// total, without personal data (SEC-06).
/// </summary>
internal sealed class PowderSupplierExport : IExportDefinition
{
    public string Name => "powder-supplier";

    public string Version => ExportRows.ProvisionalVersion;

    public bool Provisional => true;

    public ExportAudience Audience => ExportAudience.Recipient;

    public ExportTable Build(ExportData data, ExportTexts texts)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(texts);
        var totals = data.Orders
            .GroupBy(order => order.ComparsaId)
            .Select(group =>
            {
                var entries = group.SelectMany(order => order.Entries).ToList();
                if (entries.Any(e => e.CapsBoxes > 0 && e.CapsType is null))
                {
                    // The database forbids it (ck_edition_entries_caps_type); never under-order caps silently.
                    throw new InvalidOperationException($"An entry of comparsa {group.Key} has caps boxes without a type.");
                }

                return (
                    Comparsa: ExportRows.Comparsa(data, group.Key),
                    PowderKg: entries.Sum(e => e.PowderKg),
                    Normal: entries.Where(e => e.CapsType == CapsType.Normal).Sum(e => e.CapsBoxes),
                    Small: entries.Where(e => e.CapsType == CapsType.Small).Sum(e => e.CapsBoxes));
            })
            .OrderBy(t => t.Comparsa, SpanishOrder.Names)
            .ToList();
        var rows = totals.Select(t => (IReadOnlyList<object?>)[t.Comparsa, t.PowderKg, t.Normal, t.Small]).ToList();
        return new ExportTable(
            ExportRows.FileStem(data.EditionYear, Name, Provisional),
            texts.Format(texts.PowderSupplierTitle, data.EditionYear),
            ExportRows.Notices(texts, Provisional),
            texts.Format(texts.VersionLine, Name, Version),
            [
                new(texts.Comparsa, ExportCellType.Text),
                new(texts.PowderKg, ExportCellType.Integer),
                new(texts.NormalCapsBoxes, ExportCellType.Integer),
                new(texts.SmallCapsBoxes, ExportCellType.Integer),
            ],
            rows,
            [texts.Total, totals.Sum(t => t.PowderKg), totals.Sum(t => t.Normal), totals.Sum(t => t.Small)]);
    }
}
