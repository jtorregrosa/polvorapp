using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.Exports.Contracts;

namespace PolvorApp.Exports.Definitions;

/// <summary>
/// The Arms Authority's list (spec: Arms Authority export): every <c>ACTIVE</c> entry with a weapon,
/// with the license from the registry and the weapon's data; for a loan, the lender.
/// </summary>
internal sealed class ArmsAuthorityExport : IExportDefinition
{
    public string Name => "arms-authority";

    public string Version => ExportRows.ProvisionalVersion;

    public bool Provisional => true;

    public ExportAudience Audience => ExportAudience.Recipient;

    public DocumentTable Build(ExportData data, ExportTexts texts)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(texts);
        var rows = ExportRows.Of(data)
            .Where(row => row.Entry.IsActive && row.Entry.WeaponSource != WeaponSource.None)
            .Select(row => Row(row, data, texts))
            .ToList();
        return new DocumentTable(
            ExportRows.FileStem(data.EditionYear, Name, Provisional),
            texts.Format(texts.ArmsAuthorityTitle, data.EditionYear),
            ExportRows.Notices(texts, Provisional),
            texts.Format(texts.VersionLine, Name, Version),
            [
                new(texts.Name, DocumentCellType.Text),
                new(texts.NationalId, DocumentCellType.Text),
                new(texts.Comparsa, DocumentCellType.Text),
                new(texts.License, DocumentCellType.Text),
                new(texts.LicenseExpiresOn, DocumentCellType.Date),
                new(texts.Weapon, DocumentCellType.Text),
                new(texts.WeaponNumber, DocumentCellType.Text),
                new(texts.OwnershipGuide, DocumentCellType.Text),
                new(texts.Source, DocumentCellType.Text),
                new(texts.Lender, DocumentCellType.Text),
                new(texts.LenderNationalId, DocumentCellType.Text),
            ],
            rows,
            null);
    }

    private static IReadOnlyList<object?> Row(ExportRow row, ExportData data, ExportTexts texts)
    {
        var entry = row.Entry;
        var inRegistry = entry.ArquebusierId is { } id && data.Arquebusiers.ContainsKey(id);
        var license = ExportRows.License(data, entry);
        // A validated order cannot hold a weapon without its data (its validation is blocked); never send a blank.
        var weapon = entry.WeaponSource switch
        {
            WeaponSource.Owned => ExportRows.OwnedWeapon(data, entry),
            WeaponSource.Rental => new ExportedWeapon(entry.RentalWeaponModelId, null, null),
            WeaponSource.Loan => entry.Loan?.Weapon,
            _ => null,
        } ?? throw new InvalidOperationException($"Entry {entry.EntryId} has weapon source {entry.WeaponSource} without its weapon data.");
        return
        [
            row.Name,
            row.NationalId,
            row.Comparsa,
            license is not null ? ExportRows.LicenseType(license.Type) : inRegistry ? texts.NoLicense : null,
            license is ArquebusierLicenseFacts.Issued issued ? issued.ExpiresOn : null,
            ExportRows.Model(data, weapon?.WeaponModelId),
            weapon?.WeaponNumber,
            weapon?.OwnershipGuideNumber,
            texts.WeaponSources[entry.WeaponSource],
            entry.Loan is { } loan ? ExportRows.PersonName(loan.Lender.LastName, loan.Lender.FirstName) : null,
            entry.Loan?.Lender.NationalId,
        ];
    }
}
