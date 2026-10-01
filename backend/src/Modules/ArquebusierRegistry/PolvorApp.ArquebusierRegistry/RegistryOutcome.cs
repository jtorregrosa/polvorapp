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
