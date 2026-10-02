using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PolvorApp.SharedKernel.Http;

namespace PolvorApp.FederationCatalog;

/// <summary>How a catalogue operation ended (design D4). Every value but <see cref="Done"/> is blocking.</summary>
internal enum CatalogOutcome
{
    Done,
    ComparsaNotFound,
    NameTaken,
    ComparsaInUse,
    UserNotFound,
    NotFiringChief,
    UserDeactivated,
    ComparsaInactive,
    WeaponModelNotFound,
    LabelTaken,
    CombinationTaken,
    WeaponModelInUse,
    LogoNotFound,

    /// <summary>The image broke a logo rule; the endpoint names the rule (spec: Logo validation and processing).</summary>
    ImageRejected,
    StorageUnavailable,

    /// <summary>The image processor stayed busy; retryable.</summary>
    Busy,
}

/// <summary>Problem codes of the catalogue, translated by the UI as <c>catalog:errors.&lt;code&gt;</c>.</summary>
internal static class CatalogProblems
{
    public const string ComparsaNotFound = "comparsas.notFound";
    public const string NameTaken = "comparsas.nameTaken";
    public const string ComparsaInUse = "comparsas.inUse";
    public const string UserNotFound = "assignments.userNotFound";
    public const string NotFiringChief = "assignments.notFiringChief";
    public const string UserDeactivated = "assignments.userDeactivated";
    public const string ComparsaInactive = "assignments.comparsaInactive";
    public const string WeaponModelNotFound = "weaponModels.notFound";
    public const string LabelTaken = "weaponModels.labelTaken";
    public const string CombinationTaken = "weaponModels.combinationTaken";
    public const string WeaponModelInUse = "weaponModels.inUse";
    public const string LogoNotFound = "logos.notFound";
    public const string StorageUnavailable = "storage.unavailable";
    public const string Busy = "catalog.busy";

    public static ProblemHttpResult From(CatalogOutcome outcome) => outcome switch
    {
        CatalogOutcome.ComparsaNotFound => ProblemResults.NotFound(ComparsaNotFound),
        CatalogOutcome.NameTaken => ProblemResults.Conflict(NameTaken),
        CatalogOutcome.ComparsaInUse => ProblemResults.Conflict(ComparsaInUse),
        CatalogOutcome.UserNotFound => ProblemResults.NotFound(UserNotFound),
        CatalogOutcome.NotFiringChief => ProblemResults.Conflict(NotFiringChief),
        CatalogOutcome.UserDeactivated => ProblemResults.Conflict(UserDeactivated),
        CatalogOutcome.ComparsaInactive => ProblemResults.Conflict(ComparsaInactive),
        CatalogOutcome.WeaponModelNotFound => ProblemResults.NotFound(WeaponModelNotFound),
        CatalogOutcome.LabelTaken => ProblemResults.Conflict(LabelTaken),
        CatalogOutcome.CombinationTaken => ProblemResults.Conflict(CombinationTaken),
        CatalogOutcome.WeaponModelInUse => ProblemResults.Conflict(WeaponModelInUse),
        CatalogOutcome.LogoNotFound => ProblemResults.NotFound(LogoNotFound),
        CatalogOutcome.StorageUnavailable => ProblemResults.Problem(StatusCodes.Status503ServiceUnavailable, StorageUnavailable),
        CatalogOutcome.Busy => ProblemResults.Problem(StatusCodes.Status503ServiceUnavailable, Busy),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Not a problem outcome."),
    };

    /// <summary>Whether saving failed on the named unique index: a blocking rule lost a race (design D3).</summary>
    public static bool IsUniqueViolation(DbUpdateException exception, string indexName) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && postgres.ConstraintName == indexName;
}
