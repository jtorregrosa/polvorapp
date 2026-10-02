using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComplianceInsights.Contracts;

namespace PolvorApp.ArquebusierRegistry.Insights;

/// <summary>The license columns of an arquebusier row.</summary>
internal readonly record struct LicenseColumns(LicenseType? Type, bool Pending, DateOnly? ExpiresOn);

/// <summary>Which photos an arquebusier has.</summary>
internal readonly record struct PhotoFlags(bool Id, bool LicenseFront, bool LicenseBack);

/// <summary>
/// Turns the registry's columns into the facts the compliance rules read (design D3). The list, the
/// detail and <see cref="IArquebusierFacts"/> all go through <see cref="LicenseOf"/>, so the license
/// shape is decided in one place.
/// </summary>
internal static class RegistryCompliance
{
    public static ComplianceFacts FactsOf(Guid arquebusierId, DateOnly birthDate, DateOnly? trainingCompletedOn, LicenseColumns license, PhotoFlags photos) =>
        new(
            birthDate,
            LicenseOf(arquebusierId, license, photos) switch
            {
                null => null,
                ArquebusierLicenseFacts.Pending => new ComplianceLicense.Pending(),
                ArquebusierLicenseFacts.Issued issued => new ComplianceLicense.Issued(issued.ExpiresOn, issued.HasFrontPhoto, issued.HasBackPhoto),
                _ => throw new InvalidOperationException($"Unknown license shape of arquebusier {arquebusierId}."),
            },
            trainingCompletedOn,
            photos.Id);

    /// <summary>
    /// The license of a row, or null when there is none. The database guarantees that an issued
    /// license has its dates (<c>ck_arquebusiers_license</c>), so a missing expiry is a broken
    /// invariant, reported with the arquebusier's identifier (not personal data).
    /// </summary>
    public static ArquebusierLicenseFacts? LicenseOf(Guid arquebusierId, LicenseColumns license, PhotoFlags photos) => license switch
    {
        { Type: null } => null,
        { Pending: true } => new ArquebusierLicenseFacts.Pending(),
        { ExpiresOn: { } expiresOn } => new ArquebusierLicenseFacts.Issued(expiresOn, photos.LicenseFront, photos.LicenseBack),
        _ => throw new InvalidOperationException($"The issued license of arquebusier {arquebusierId} has no expiry date."),
    };
}
