using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.Exports.Contracts;

namespace PolvorApp.Exports.Definitions;

/// <summary>
/// The rental company's list (spec: Rental company export): who rents a weapon model or a flask; no
/// license, owned weapon or loan data (SEC-06).
/// </summary>
internal sealed class RentalCompanyExport : IExportDefinition
{
    public string Name => "rental-company";

    public string Version => ExportRows.ProvisionalVersion;

    public bool Provisional => true;

    public ExportAudience Audience => ExportAudience.Recipient;

    public DocumentTable Build(ExportData data, ExportTexts texts)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(texts);
        var rows = ExportRows.Of(data)
            .Where(row => row.Entry.WeaponSource == WeaponSource.Rental || row.Entry.Flask is FlaskOption.Rental1Kg or FlaskOption.Rental2Kg)
            .Select(row => (IReadOnlyList<object?>)
            [
                row.Name,
                row.NationalId,
                row.Comparsa,
                row.Entry.WeaponSource == WeaponSource.Rental ? ExportRows.Model(data, row.Entry.RentalWeaponModelId) : null,
                row.Entry.Flask is FlaskOption.Rental1Kg or FlaskOption.Rental2Kg ? texts.Flasks[row.Entry.Flask] : null,
            ])
            .ToList();
        return new DocumentTable(
            ExportRows.FileStem(data.EditionYear, Name, Provisional),
            texts.Format(texts.RentalCompanyTitle, data.EditionYear),
            ExportRows.Notices(texts, Provisional),
            texts.Format(texts.VersionLine, Name, Version),
            [
                new(texts.Name, DocumentCellType.Text),
                new(texts.NationalId, DocumentCellType.Text),
                new(texts.Comparsa, DocumentCellType.Text),
                new(texts.WeaponModel, DocumentCellType.Text),
                new(texts.Flask, DocumentCellType.Text),
            ],
            rows,
            null);
    }
}
