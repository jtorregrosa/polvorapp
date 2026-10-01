using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ArquebusierRegistry.Licenses;
using PolvorApp.ArquebusierRegistry.OwnedWeapons;

namespace PolvorApp.ArquebusierRegistry.Arquebusiers;

/// <summary>
/// A member registered for firing activities (spec: Arquebusier data). PolvorApp is the authoritative
/// source for these data (Q-05). The current license is stored in the <c>License*</c> columns of the
/// same row: at most one, without number or history (design D3).
/// </summary>
internal sealed class Arquebusier
{
    public const int NameMaxLength = 100;
    public const int EmailMaxLength = 254;
    public const int PhoneMaxLength = 20;

    /// <summary>A DNI (8 digits + letter) or an NIE (X/Y/Z + 7 digits + letter), normalised.</summary>
    public const int NationalIdLength = 9;

    public required Guid Id { get; init; }

    /// <summary>Changed only by a transfer (UC-29), never by an edit.</summary>
    public required Guid ComparsaId { get; set; }

    public required int FederationId { get; set; }

    public required string NationalId { get; set; }

    public required string FirstName { get; set; }

    public required string LastName { get; set; }

    public required DateOnly BirthDate { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public required Gender Gender { get; set; }

    public ArquebusierStatus Status { get; set; } = ArquebusierStatus.Active;

    /// <summary>Date the mandatory course was done; null when it is not done (UC-03).</summary>
    public DateOnly? TrainingCompletedOn { get; set; }

    /// <summary>Null when the arquebusier has no license.</summary>
    public LicenseType? LicenseType { get; set; }

    /// <summary>Applied for but not issued: then there are no dates.</summary>
    public bool LicensePending { get; set; }

    public DateOnly? LicenseIssuedOn { get; set; }

    public DateOnly? LicenseExpiresOn { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>PostgreSQL <c>xmin</c>: an edit based on an older version is rejected (spec: Registering and editing).</summary>
    public uint Version { get; set; }

    public List<OwnedWeapon> OwnedWeapons { get; } = [];

    /// <summary>The stored license as a value object; null when the arquebusier has none.</summary>
    public License? CurrentLicense() =>
        LicenseType is { } type ? new License(type, LicensePending, LicenseIssuedOn, LicenseExpiresOn) : null;
}
