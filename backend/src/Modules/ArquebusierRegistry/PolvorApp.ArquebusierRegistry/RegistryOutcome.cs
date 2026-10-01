using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PolvorApp.SharedKernel.Http;

namespace PolvorApp.ArquebusierRegistry;

/// <summary>How a registry operation ended (design D5). Every value but <see cref="Done"/> is blocking.</summary>
internal enum RegistryOutcome
{
    Done,
    ArquebusierNotFound,
    ComparsaNotFound,
    ComparsaInactive,
    SameComparsa,
    NationalIdTaken,
    FederationIdTaken,
    ArquebusierModified,
    OwnedWeaponNotFound,

    /// <summary>The chosen weapon model does not exist: a field error (400 weaponModelId: notFound).</summary>
    WeaponModelNotFound,
    WeaponModelInactive,
    OwnershipGuideTaken,
    OwnedWeaponModified,

    /// <summary>Another request held the row past the lock timeout: retry later (503).</summary>
    Busy,

    /// <summary>The arquebusier has no photo of that kind (404).</summary>
    PhotoNotFound,

    /// <summary>A license photo for an arquebusier without a license (409).</summary>
    PhotoNeedsLicense,

    /// <summary>Two first uploads of the same kind raced; this one lost (409, reload).</summary>
    PhotoModified,

    /// <summary>The object storage could not be reached (503, retryable).</summary>
    StorageUnavailable,

    /// <summary>The uploaded image broke a photo rule: a field error the endpoint builds (400 file: reason).</summary>
    ImageRejected,
}

/// <summary>Problem codes of the registry, translated by the UI as <c>registry:errors.&lt;code&gt;</c>.</summary>
internal static class RegistryProblems
{
    public const string ArquebusierNotFound = "arquebusiers.notFound";
    public const string ComparsaNotFound = "arquebusiers.comparsaNotFound";
    public const string ComparsaInactive = "arquebusiers.comparsaInactive";
    public const string SameComparsa = "arquebusiers.sameComparsa";
    public const string NationalIdTaken = "arquebusiers.nationalIdTaken";
    public const string FederationIdTaken = "arquebusiers.federationIdTaken";
    public const string ArquebusierModified = "arquebusiers.modified";
    public const string OwnedWeaponNotFound = "ownedWeapons.notFound";
    public const string WeaponModelInactive = "ownedWeapons.modelInactive";
    public const string OwnershipGuideTaken = "ownedWeapons.guideTaken";
    public const string OwnedWeaponModified = "ownedWeapons.modified";
    public const string Busy = "registry.busy";
    public const string PhotoNotFound = "photos.notFound";
    public const string PhotoNeedsLicense = "photos.noLicense";
    public const string PhotoModified = "photos.modified";
    public const string StorageUnavailable = "storage.unavailable";

    public static ProblemHttpResult From(RegistryOutcome outcome) => outcome switch
    {
        RegistryOutcome.ArquebusierNotFound => ProblemResults.NotFound(ArquebusierNotFound),
        RegistryOutcome.ComparsaNotFound => ProblemResults.NotFound(ComparsaNotFound),
        RegistryOutcome.ComparsaInactive => ProblemResults.Conflict(ComparsaInactive),
        RegistryOutcome.SameComparsa => ProblemResults.Conflict(SameComparsa),
        RegistryOutcome.NationalIdTaken => ProblemResults.Conflict(NationalIdTaken),
        RegistryOutcome.FederationIdTaken => ProblemResults.Conflict(FederationIdTaken),
        RegistryOutcome.ArquebusierModified => ProblemResults.Conflict(ArquebusierModified),
        RegistryOutcome.OwnedWeaponNotFound => ProblemResults.NotFound(OwnedWeaponNotFound),
        RegistryOutcome.WeaponModelNotFound => ProblemResults.Invalid(new Dictionary<string, string> { ["weaponModelId"] = "notFound" }),
        RegistryOutcome.WeaponModelInactive => ProblemResults.Conflict(WeaponModelInactive),
        RegistryOutcome.OwnershipGuideTaken => ProblemResults.Conflict(OwnershipGuideTaken),
        RegistryOutcome.OwnedWeaponModified => ProblemResults.Conflict(OwnedWeaponModified),
        RegistryOutcome.Busy => ProblemResults.Problem(StatusCodes.Status503ServiceUnavailable, Busy),
        RegistryOutcome.PhotoNotFound => ProblemResults.NotFound(PhotoNotFound),
        RegistryOutcome.PhotoNeedsLicense => ProblemResults.Conflict(PhotoNeedsLicense),
        RegistryOutcome.PhotoModified => ProblemResults.Conflict(PhotoModified),
        RegistryOutcome.StorageUnavailable => ProblemResults.Problem(StatusCodes.Status503ServiceUnavailable, StorageUnavailable),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Not a problem outcome."),
    };

    /// <summary>The constraint a failed save violated, when it was a unique or foreign-key violation (design D5).</summary>
    public static string? ViolatedConstraint(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation,
        } postgres
            ? postgres.ConstraintName
            : null;
}
