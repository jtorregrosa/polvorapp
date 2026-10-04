using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Npgsql;
using PolvorApp.AuditPrivacy.Contracts;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Http;
using PolvorApp.SharedKernel.Security;
using PolvorApp.SharedKernel.Storage;
using PolvorApp.SharedKernel.Validation;

namespace PolvorApp.AuditPrivacy.Privacy;

/// <summary>A person lookup: their DNI/NIE (in the body, never in the address) and, optionally, the request reference.</summary>
internal sealed record PersonLookupRequest(string? NationalId, string? Reference = null);

/// <summary>A GDPR export or erasure about a person: their DNI/NIE and the request reference.</summary>
internal sealed record PersonRequest(string? NationalId, string? Reference);

/// <summary>A GDPR request about a user: the request reference.</summary>
internal sealed record UserRequest(string? Reference);

/// <summary>The registered arquebusier found, to name the person in the confirmation.</summary>
internal sealed record RegistryRecordResponse(Guid ArquebusierId, string FirstName, string LastName, string? ComparsaName, string Status, int OwnedWeapons, int Photos);

/// <summary>A person's entry in an edition.</summary>
internal sealed record PersonEntryResponse(int EditionYear, string EditionStatus, bool OrdersOpen, string? ComparsaName, string OrderStatus, bool Erased);

/// <summary>What an erasure will do to an edition that is not closed (spec: Erasing a person's data, warnings).</summary>
/// <param name="Kind"><c>entryRemoved</c> (orders open: the entry is deleted) or <c>listsChange</c> (the lists no longer name them).</param>
internal sealed record ErasureWarningResponse(string Kind, int EditionYear, string? ComparsaName, string OrderStatus);

/// <summary>What PolvorApp holds about a person (spec: Looking up a person (UC-26)).</summary>
internal sealed record PersonLookupResponse(
    bool Found,
    RegistryRecordResponse? Registry,
    List<PersonEntryResponse> Entries,
    List<LenderLoansSummary> LenderLoans,
    int PickupProxies,
    List<ErasureWarningResponse> Warnings);

/// <summary>What an erasure changed (counts only) and how many stored files the sweep still has to delete.</summary>
internal sealed record ErasureResponse(Dictionary<string, int> Counts, int FilesPending);

/// <summary>Category of the GDPR request logs.</summary>
internal sealed class PrivacyRequestLog;

/// <summary>
/// GDPR requests (UC-26; design D11), Admins only and rate limited: look a person up by DNI/NIE, export
/// or erase a person's or a user's data. The DNI/NIE travels in request bodies only. Every request is
/// audited with its reference and counts, never with the DNI/NIE or a name, also when nothing is held;
/// a file or an erasure that cannot be audited does not happen. Failures are logged by kind, never
/// with the reference or the person.
/// </summary>
internal static partial class PrivacyEndpoints
{
    public const string NotFound = "privacy.notFound";
    public const string Busy = "privacy.busy";
    public const string AuditUnavailable = "privacy.auditUnavailable";
    public const string StorageUnavailable = "privacy.storageUnavailable";
    public const int ReferenceMaxLength = 50;

    /// <summary>A request body holds a DNI/NIE and a reference: anything bigger is refused before it is read.</summary>
    public const long MaxBodyBytes = 4096;

    public static IEndpointRouteBuilder MapPrivacyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var privacy = endpoints.MapGroup("/privacy").WithTags("Privacy").RequireAuthorization(AuthorizationPolicies.Admin)
            .RequireRateLimiting(RateLimitPolicies.Privacy)
            .WithMetadata(new RequestSizeLimitAttribute(MaxBodyBytes))
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        privacy.MapPost("/people/lookup", LookUpAsync).WithName("LookUpPerson")
            .WithSummary("What PolvorApp holds about a person, by DNI/NIE.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        privacy.MapPost("/people/export", ExportPersonAsync).WithName("ExportPersonData")
            .WithSummary("A ZIP with the person's data and photos.")
            .Produces<Stream>(StatusCodes.Status200OK, PersonalDataPackage.ContentType)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        privacy.MapPost("/people/erasure", ErasePersonAsync).WithName("ErasePersonData")
            .WithSummary("Erases everything held about the person.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        privacy.MapPost("/users/{id:guid}/export", ExportUserAsync).WithName("ExportUserData")
            .WithSummary("A ZIP with the user's data.")
            .Produces<Stream>(StatusCodes.Status200OK, PersonalDataPackage.ContentType)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        privacy.MapPost("/users/{id:guid}/erasure", EraseUserAsync).WithName("EraseUserData")
            .WithSummary("Erases the user's data; the user stays, anonymised, as ERASED.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return endpoints;
    }

    private static async Task<Results<Ok<PersonLookupResponse>, ProblemHttpResult>> LookUpAsync(
        PersonLookupRequest request,
        PersonalDataRequests requests,
        ICatalogDirectory catalog,
        IAuditLog auditLog,
        ILogger<PrivacyRequestLog> logger,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        var nationalId = ParseNationalId(request.NationalId, errors);
        var reference = string.IsNullOrWhiteSpace(request.Reference) ? null : ParseReference(request.Reference, errors);
        if (nationalId is null || errors.Count > 0)
        {
            return ProblemResults.Invalid(errors);
        }

        var summary = await requests.DescribeAsync(new PersonalDataSubject.Person(nationalId), cancellationToken);
        if (!await TryAuditAsync(auditLog, logger, LookedUp(summary.HoldsAnything, reference, via: null), cancellationToken))
        {
            return ProblemResults.Problem(StatusCodes.Status503ServiceUnavailable, AuditUnavailable);
        }

        return TypedResults.Ok(await LookupResponseAsync(summary, catalog, logger, cancellationToken));
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> ExportPersonAsync(
        PersonRequest request,
        PersonalDataRequests requests,
        PersonalDataPackager packager,
        IAuditLog auditLog,
        ILogger<PrivacyRequestLog> logger,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        var nationalId = ParseNationalId(request.NationalId, errors);
        var reference = ParseReference(request.Reference, errors);
        if (nationalId is null || reference is null || errors.Count > 0)
        {
            return ProblemResults.Invalid(errors);
        }

        return await ExportAsync(new PersonalDataSubject.Person(nationalId), "person", null, reference, requests, packager, auditLog, logger, cancellationToken);
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> ExportUserAsync(
        Guid id,
        UserRequest request,
        IUserDirectory users,
        PersonalDataRequests requests,
        PersonalDataPackager packager,
        IAuditLog auditLog,
        ILogger<PrivacyRequestLog> logger,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        var reference = ParseReference(request.Reference, errors);
        if (reference is null || errors.Count > 0)
        {
            return ProblemResults.Invalid(errors);
        }

        // An erased user's data is gone: there is nothing left to export.
        if (await users.FindAsync(id, cancellationToken) is not { Status: not UserStatus.Erased })
        {
            return ProblemResults.NotFound(NotFound);
        }

        return await ExportAsync(new PersonalDataSubject.UserAccount(id), "user", id.ToString(), reference, requests, packager, auditLog, logger, cancellationToken);
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> ExportAsync(
        PersonalDataSubject subject,
        string subjectKind,
        string? entityId,
        string reference,
        PersonalDataRequests requests,
        PersonalDataPackager packager,
        IAuditLog auditLog,
        ILogger<PrivacyRequestLog> logger,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PersonalDataExportPart> parts;
        try
        {
            parts = await requests.ExportAsync(subject, cancellationToken);
        }
        catch (StorageUnavailableException)
        {
            // The photos cannot be read: an export without them would be incomplete.
            LogStorageUnavailable(logger, subjectKind);
            return ProblemResults.Problem(StatusCodes.Status503ServiceUnavailable, StorageUnavailable);
        }

        if (!parts.Any(p => p.Sheets.Any(s => s.Rows.Count > 0)))
        {
            // Probing with an export is audited like a lookup.
            return await TryAuditAsync(auditLog, logger, LookedUp(false, reference, via: "export"), cancellationToken)
                ? ProblemResults.NotFound(NotFound)
                : ProblemResults.Problem(StatusCodes.Status503ServiceUnavailable, AuditUnavailable);
        }

        var package = packager.Build(parts, reference);
        var audited = await TryAuditAsync(
            auditLog,
            logger,
            new AuditRecord(
                AuditPrivacyAuditActions.PersonalDataExported,
                AuditPrivacyAuditActions.RequestEntityType,
                entityId,
                new { subject = subjectKind, reference, sheets = package.Sheets, files = package.Files, notes = package.Notes }),
            cancellationToken);
        return audited
            ? TypedResults.File(package.Content, PersonalDataPackage.ContentType, package.FileName)
            : ProblemResults.Problem(StatusCodes.Status503ServiceUnavailable, AuditUnavailable);
    }

    private static async Task<Results<Ok<ErasureResponse>, ProblemHttpResult>> ErasePersonAsync(
        PersonRequest request, PersonalDataRequests requests, IAuditLog auditLog, ILogger<PrivacyRequestLog> logger, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        var nationalId = ParseNationalId(request.NationalId, errors);
        var reference = ParseReference(request.Reference, errors);
        if (nationalId is null || reference is null || errors.Count > 0)
        {
            return ProblemResults.Invalid(errors);
        }

        var result = await EraseAsync(new PersonalDataSubject.Person(nationalId), "person", null, reference, requests, logger, cancellationToken);
        if (result.Result is ProblemHttpResult { StatusCode: StatusCodes.Status404NotFound }
            && !await TryAuditAsync(auditLog, logger, LookedUp(false, reference, via: "erasure"), cancellationToken))
        {
            return ProblemResults.Problem(StatusCodes.Status503ServiceUnavailable, AuditUnavailable);
        }

        return result;
    }

    private static async Task<Results<Ok<ErasureResponse>, ProblemHttpResult>> EraseUserAsync(
        Guid id, UserRequest request, PersonalDataRequests requests, ILogger<PrivacyRequestLog> logger, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        var reference = ParseReference(request.Reference, errors);
        if (reference is null || errors.Count > 0)
        {
            return ProblemResults.Invalid(errors);
        }

        return await EraseAsync(new PersonalDataSubject.UserAccount(id), "user", id.ToString(), reference, requests, logger, cancellationToken);
    }

    private static async Task<Results<Ok<ErasureResponse>, ProblemHttpResult>> EraseAsync(
        PersonalDataSubject subject,
        string subjectKind,
        string? entityId,
        string reference,
        PersonalDataRequests requests,
        ILogger<PrivacyRequestLog> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await requests.EraseAsync(
                subject,
                counts => new AuditRecord(
                    AuditPrivacyAuditActions.PersonalDataErased,
                    AuditPrivacyAuditActions.RequestEntityType,
                    entityId,
                    new { subject = subjectKind, reference, counts }),
                cancellationToken);
            return result is null
                ? ProblemResults.NotFound(NotFound)
                : TypedResults.Ok(new ErasureResponse(new Dictionary<string, int>(result.Counts), result.ObjectsPending));
        }
        catch (PersonalDataErasureRefusedException refused)
        {
            return ProblemResults.Conflict(refused.Code);
        }
        catch (Exception exception) when (BusyState(exception) is { } sqlState)
        {
            LogBusy(logger, subjectKind, sqlState);
            return ProblemResults.Problem(StatusCodes.Status503ServiceUnavailable, Busy);
        }
    }

    private static AuditRecord LookedUp(bool found, string? reference, string? via) =>
        new(AuditPrivacyAuditActions.PersonLookedUp, AuditPrivacyAuditActions.RequestEntityType, Data: new { found, reference, via });

    private static async Task<PersonLookupResponse> LookupResponseAsync(
        PersonalDataSummary summary, ICatalogDirectory catalog, ILogger logger, CancellationToken cancellationToken)
    {
        Guid[] comparsaIds = [.. summary.Entries.Select(e => e.ComparsaId).Concat(summary.Registry is { } record ? [record.ComparsaId] : []).Distinct()];
        var names = (await catalog.FindComparsasAsync(comparsaIds, cancellationToken)).ToDictionary(c => c.Id, c => c.Name);
        string? Name(Guid id)
        {
            if (names.TryGetValue(id, out var name))
            {
                return name;
            }

            LogComparsaMissing(logger, id);
            return null;
        }

        var registry = summary.Registry is { } r
            ? new RegistryRecordResponse(r.ArquebusierId, r.FirstName, r.LastName, Name(r.ComparsaId), r.Status, r.OwnedWeapons, r.Photos)
            : null;
        var warnings = summary.Entries
            .Where(e => !e.Erased && e.EditionStatus != "CLOSED")
            .Select(e => new ErasureWarningResponse(
                registry is not null && e.OrdersOpen && e.EditionStatus == "IN_PROGRESS" ? "entryRemoved" : "listsChange",
                e.EditionYear,
                Name(e.ComparsaId),
                e.OrderStatus))
            .ToList();
        return new PersonLookupResponse(
            summary.HoldsAnything,
            registry,
            [.. summary.Entries.Select(e => new PersonEntryResponse(e.EditionYear, e.EditionStatus, e.OrdersOpen, Name(e.ComparsaId), e.OrderStatus, e.Erased))],
            [.. summary.LenderLoans],
            summary.Counts.GetValueOrDefault(PersonalDataCounts.PickupProxies),
            warnings);
    }

    private static string? ParseNationalId(string? value, Dictionary<string, string> errors)
    {
        var parsed = NationalId.Parse(value);
        if (parsed.Error is { } error)
        {
            errors["nationalId"] = error;
        }

        return parsed.Value;
    }

    /// <summary>
    /// The request reference: required, 1 to 50 characters on one line. It is kept in the audit trail,
    /// so it must not be an email or contain a valid DNI/NIE, however it is spaced (design D11).
    /// </summary>
    private static string? ParseReference(string? value, Dictionary<string, string> errors)
    {
        var reference = InputFields.Text(value, "reference", ReferenceMaxLength, errors);
        if (reference is not null && (reference.Contains('@', StringComparison.Ordinal) || ContainsNationalId(reference)))
        {
            errors["reference"] = InputFields.Invalid;
            return null;
        }

        return reference;
    }

    private static bool ContainsNationalId(string reference)
    {
        var compact = Separators().Replace(reference, string.Empty);
        return NationalIdCandidate().Matches(compact).Any(m => NationalId.Parse(m.Value).Value is not null);
    }

    private static async Task<bool> TryAuditAsync(IAuditLog auditLog, ILogger logger, AuditRecord record, CancellationToken cancellationToken)
    {
        try
        {
            await auditLog.RecordAsync(record, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        {
            LogAuditFailed(logger, record.Action, exception);
            return false;
        }
    }

    /// <summary>The SQLSTATE of a lock timeout or deadlock, possibly wrapped: the request can be retried.</summary>
    private static string? BusyState(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.DeadlockDetected } postgres)
            {
                return postgres.SqlState;
            }
        }

        return null;
    }

    /// <summary>Everything but ASCII letters and digits: a DNI/NIE split by any mark (space, dot, slash, comma...) is still found.</summary>
    [GeneratedRegex(@"[^0-9A-Za-z]")]
    private static partial Regex Separators();

    [GeneratedRegex(@"[XYZ][0-9]{7}[A-Z]|[0-9]{8}[A-Z]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NationalIdCandidate();

    [LoggerMessage(Level = LogLevel.Error, Message = "A GDPR request could not be audited ({Action}); nothing was sent")]
    private static partial void LogAuditFailed(ILogger logger, string action, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A {Subject} erasure waited too long for a lock ({SqlState}); it can be retried")]
    private static partial void LogBusy(ILogger logger, string subject, string sqlState);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A {Subject} export could not read the stored photos")]
    private static partial void LogStorageUnavailable(ILogger logger, string subject);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Comparsa {ComparsaId} of a person lookup is missing from the catalogue")]
    private static partial void LogComparsaMissing(ILogger logger, Guid comparsaId);
}
