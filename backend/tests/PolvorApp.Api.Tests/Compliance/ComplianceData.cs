using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Arquebusiers;
using PolvorApp.ArquebusierRegistry.Contracts;

namespace PolvorApp.Api.Tests.Compliance;

/// <summary>The license a synthetic arquebusier gets.</summary>
internal enum LicenseCase
{
    Valid,
    Expiring,
    Expired,
    Pending,
    None,
}

/// <summary>Synthetic arquebusiers with chosen compliance traits, relative to the API's today.</summary>
internal static class ComplianceData
{
    /// <summary>
    /// A compliant arquebusier unless told otherwise: of age, course done, ID photo, and an AE license
    /// valid for four more years with both photos. Returns the row and its photo rows.
    /// </summary>
    public static object[] Arquebusier(
        Guid comparsaId,
        DateOnly today,
        int age = 30,
        Gender gender = Gender.Unspecified,
        ArquebusierStatus status = ArquebusierStatus.Active,
        bool course = true,
        bool idPhoto = true,
        LicenseCase license = LicenseCase.Valid,
        bool licensePhotos = true)
    {
        var arquebusier = RegistryData.NewArquebusier(comparsaId);
        arquebusier.BirthDate = today.AddYears(-age).AddDays(-1);
        arquebusier.Gender = gender;
        arquebusier.Status = status;
        arquebusier.TrainingCompletedOn = course ? today.AddYears(-1) : null;
        (arquebusier.LicenseType, arquebusier.LicensePending, arquebusier.LicenseIssuedOn, arquebusier.LicenseExpiresOn) = license switch
        {
            LicenseCase.Valid => (LicenseType.Ae, false, today.AddYears(-1), today.AddYears(4)),
            LicenseCase.Expiring => (LicenseType.Ae, false, today.AddMonths(3).AddYears(-5), today.AddMonths(3)),
            LicenseCase.Expired => (LicenseType.Ae, false, today.AddYears(-6), today.AddDays(-1)),
            LicenseCase.Pending => (LicenseType.Ae, true, null, null),
            LicenseCase.None => ((LicenseType?)null, false, (DateOnly?)null, (DateOnly?)null),
            _ => throw new ArgumentOutOfRangeException(nameof(license), license, null),
        };

        List<object> rows = [arquebusier];
        if (idPhoto)
        {
            rows.Add(RegistryData.NewPhoto(arquebusier.Id, ArquebusierPhotoKind.Id));
        }

        if (licensePhotos && arquebusier.LicenseType is not null)
        {
            rows.Add(RegistryData.NewPhoto(arquebusier.Id, ArquebusierPhotoKind.LicenseFront));
            rows.Add(RegistryData.NewPhoto(arquebusier.Id, ArquebusierPhotoKind.LicenseBack));
        }

        return [.. rows];
    }

    /// <summary>The arquebusier row among <paramref name="rows"/> built by <see cref="Arquebusier"/>.</summary>
    public static Arquebusier Row(object[] rows) => rows.OfType<Arquebusier>().Single();
}
