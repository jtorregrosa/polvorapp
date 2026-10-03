using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;

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

    public ExportTable Build(ExportData data, ExportTexts texts)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(texts);
        var rows = ExportRows.Of(data)
            .Where(row => row.Entry.IsActive && row.Entry.WeaponSource != WeaponSource.None)
            .Select(row => Row(row, data, texts))
            .ToList();
        return new ExportTable(
            ExportRows.FileStem(data.EditionYear, Name, Provisional),
            texts.Format(texts.ArmsAuthorityTitle, data.EditionYear),
            ExportRows.Notices(texts, Provisional),
            texts.Format(texts.VersionLine, Name, Version),
            [
                new(texts.Name, ExportCellType.Text),
                new(texts.NationalId, ExportCellType.Text),
                new(texts.Comparsa, ExportCellType.Text),
                new(texts.License, ExportCellType.Text),
                new(texts.LicenseExpiresOn, ExportCellType.Date),
                new(texts.Weapon, ExportCellType.Text),
                new(texts.WeaponNumber, ExportCellType.Text),
                new(texts.OwnershipGuide, ExportCellType.Text),
                new(texts.Source, ExportCellType.Text),
                new(texts.Lender, ExportCellType.Text),
                new(texts.LenderNationalId, ExportCellType.Text),
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
