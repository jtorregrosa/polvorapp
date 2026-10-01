using System.Text.Json.Serialization;
using PolvorApp.ArquebusierRegistry.Arquebusiers;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.FederationCatalog.Contracts;

namespace PolvorApp.ArquebusierRegistry.Endpoints;

/// <summary>
/// Registers an arquebusier. Codes and dates (<c>yyyy-MM-dd</c>) arrive as text so errors name the
/// field; unknown members are rejected (400).
/// </summary>
/// <param name="ComparsaId">An active comparsa in the caller's scope.</param>
/// <param name="FederationId">Id in the Federation's external app, 1 to 999999999; unique (BR-02).</param>
/// <param name="NationalId">DNI or NIE; spaces, tabs and hyphens are removed (BR-01); unique (BR-02).</param>
/// <param name="FirstName">1 to 100 characters after trimming.</param>
/// <param name="LastName">1 to 100 characters after trimming.</param>
/// <param name="BirthDate">Not in the future and not before 1900-01-01.</param>
/// <param name="Email">Optional plain email address.</param>
/// <param name="Phone">Optional; digits and spaces with an optional leading <c>+</c>, at most 20 characters.</param>
/// <param name="Gender"><c>MALE</c>, <c>FEMALE</c> or <c>UNSPECIFIED</c>.</param>
/// <param name="Status"><c>ACTIVE</c> (default) or <c>RESERVE</c>.</param>
/// <param name="TrainingCompletedOn">Date the course was done, not in the future; absent when not done.</param>
/// <param name="License">The current license; absent when the arquebusier has none.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record RegisterArquebusierRequest(
    Guid? ComparsaId,
    int? FederationId,
    string? NationalId,
    string? FirstName,
    string? LastName,
    string? BirthDate,
    string? Email,
    string? Phone,
    string? Gender,
    string? Status,
    string? TrainingCompletedOn,
    LicenseRequest? License)
{
    public ArquebusierFields Fields() =>
        new(FederationId, NationalId, FirstName, LastName, BirthDate, Email, Phone, Gender, Status, TrainingCompletedOn, License?.Fields());
}

/// <summary>
/// Replaces every editable field of an arquebusier; an omitted optional field is cleared. Unknown
/// members are rejected (400), so a misspelt field can never clear data silently.
/// </summary>
/// <param name="FederationId">See <see cref="RegisterArquebusierRequest"/>.</param>
/// <param name="NationalId">See <see cref="RegisterArquebusierRequest"/>.</param>
/// <param name="FirstName">See <see cref="RegisterArquebusierRequest"/>.</param>
/// <param name="LastName">See <see cref="RegisterArquebusierRequest"/>.</param>
/// <param name="BirthDate">See <see cref="RegisterArquebusierRequest"/>.</param>
/// <param name="Email">See <see cref="RegisterArquebusierRequest"/>.</param>
/// <param name="Phone">See <see cref="RegisterArquebusierRequest"/>.</param>
/// <param name="Gender">See <see cref="RegisterArquebusierRequest"/>.</param>
/// <param name="Status"><c>ACTIVE</c> or <c>RESERVE</c>; required, because the edit replaces the whole record.</param>
/// <param name="TrainingCompletedOn">See <see cref="RegisterArquebusierRequest"/>.</param>
/// <param name="License">See <see cref="RegisterArquebusierRequest"/>.</param>
/// <param name="Version">The <c>version</c> the edit is based on; an outdated one is rejected with 409.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record UpdateArquebusierRequest(
    int? FederationId,
    string? NationalId,
    string? FirstName,
    string? LastName,
    string? BirthDate,
    string? Email,
    string? Phone,
    string? Gender,
    string? Status,
    string? TrainingCompletedOn,
    LicenseRequest? License,
    uint? Version)
{
    public ArquebusierFields Fields() =>
        new(FederationId, NationalId, FirstName, LastName, BirthDate, Email, Phone, Gender, Status, TrainingCompletedOn, License?.Fields());
}

/// <summary>The current license (BR-03).</summary>
/// <param name="Type"><c>AE</c> or <c>A_PROF</c>.</param>
/// <param name="Pending">True while applied for but not issued; a pending license has no dates.</param>
/// <param name="IssuedOn">Issue date, required unless pending and not in the future.</param>
/// <param name="ExpiresOn">Expiry date after the issue date; defaults to 5 years (AE) or 1 year (A_PROF) later.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record LicenseRequest(string? Type, bool? Pending, string? IssuedOn, string? ExpiresOn)
{
    public LicenseFields Fields() => new(Type, Pending, IssuedOn, ExpiresOn);
}

/// <summary>The current license with its derived status.</summary>
/// <param name="Type">License type.</param>
/// <param name="Pending">Applied for but not issued.</param>
/// <param name="IssuedOn">Issue date; null while pending.</param>
/// <param name="ExpiresOn">Expiry date; null while pending.</param>
/// <param name="Status">Derived from today in Europe/Madrid, never stored.</param>
internal sealed record LicenseResponse(LicenseType Type, bool Pending, DateOnly? IssuedOn, DateOnly? ExpiresOn, LicenseStatus Status);

/// <summary>An owned weapon with its catalogue model.</summary>
/// <param name="Id">Owned weapon identifier.</param>
/// <param name="Model">The catalogue model, active or not.</param>
/// <param name="WeaponNumber">Number engraved on the stock.</param>
/// <param name="OwnershipGuideNumber">Ownership guide number, upper-cased.</param>
/// <param name="Version">Version to send back when editing the weapon.</param>
internal sealed record OwnedWeaponResponse(Guid Id, WeaponModelSummary Model, string WeaponNumber, string OwnershipGuideNumber, uint Version);

/// <summary>An arquebusier with everything the detail page shows.</summary>
/// <param name="Id">Arquebusier identifier.</param>
/// <param name="ComparsaId">Current comparsa.</param>
/// <param name="ComparsaName">Name of the current comparsa.</param>
/// <param name="ComparsaActive">False when the comparsa was deactivated; the arquebusier stays editable.</param>
/// <param name="FederationId">Id in the Federation's external app.</param>
/// <param name="NationalId">Normalised DNI or NIE.</param>
/// <param name="FirstName">First name.</param>
/// <param name="LastName">Last name or names.</param>
/// <param name="BirthDate">Birth date; age is derived.</param>
/// <param name="Email">Email, if any.</param>
/// <param name="Phone">Phone, if any.</param>
/// <param name="Gender">Gender, for equality reports.</param>
/// <param name="Status">Active or Reserve.</param>
/// <param name="TrainingCompletedOn">Course date; null when not done.</param>
/// <param name="License">Current license; null when there is none.</param>
/// <param name="OwnedWeapons">Owned weapons.</param>
/// <param name="Version">Version to send back when editing.</param>
internal sealed record ArquebusierResponse(
    Guid Id,
    Guid ComparsaId,
    string ComparsaName,
    bool ComparsaActive,
    int FederationId,
    string NationalId,
    string FirstName,
    string LastName,
    DateOnly BirthDate,
    string? Email,
    string? Phone,
    Gender Gender,
    ArquebusierStatus Status,
    DateOnly? TrainingCompletedOn,
    LicenseResponse? License,
    IReadOnlyList<OwnedWeaponResponse> OwnedWeapons,
    uint Version);

/// <summary>An arquebusier as listed (spec: Arquebusier visibility).</summary>
/// <param name="Id">Arquebusier identifier.</param>
/// <param name="FirstName">First name.</param>
/// <param name="LastName">Last name or names.</param>
/// <param name="NationalId">Normalised DNI or NIE.</param>
/// <param name="FederationId">Id in the Federation's external app.</param>
/// <param name="ComparsaId">Current comparsa.</param>
/// <param name="ComparsaName">Name of the current comparsa.</param>
/// <param name="Status">Active or Reserve.</param>
/// <param name="LicenseStatus">Derived license status; null when there is no license.</param>
/// <param name="LicenseExpiresOn">License expiry date, if issued.</param>
internal sealed record ArquebusierRowResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string NationalId,
    int FederationId,
    Guid ComparsaId,
    string ComparsaName,
    ArquebusierStatus Status,
    LicenseStatus? LicenseStatus,
    DateOnly? LicenseExpiresOn);
