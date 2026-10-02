using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using PolvorApp.ArquebusierRegistry.Import;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Http;
using PolvorApp.SharedKernel.Security;
using PolvorApp.SharedKernel.Validation;

namespace PolvorApp.ArquebusierRegistry.Endpoints;

/// <summary>
/// Specs "Import template (UC-09)", "Import validation report (UC-09)" and "All-or-nothing import
/// (UC-09)" (design D2). Every route is for Admins only: a FiringChief is refused before the body is
/// read. The group is separate from <c>/arquebusiers</c> because uploads need a larger body limit.
/// </summary>
internal static class ImportEndpoints
{
    private const string ComparsaField = "comparsaId";

    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // The uploads raise this limit for themselves; every other route takes no body to speak of.
        var group = endpoints.MapGroup("/arquebusiers/import").WithTags("Arquebusiers")
            .WithMetadata(new RequestSizeLimitAttribute(ArquebusierEndpoints.MaxBodyBytes))
            .RequireAuthorization(AuthorizationPolicies.Admin)
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden);
        group.MapGet("/template", Template).WithName("DownloadArquebusierImportTemplate")
            .WithSummary("The import template in the caller's language (Admin only); it holds no data.")
            .Produces<Stream>(StatusCodes.Status200OK, ImportTemplateWriter.ContentType)
            .RequireRateLimiting(RateLimitPolicies.PersonalDataWrites).ProducesProblem(StatusCodes.Status429TooManyRequests);
        group.MapPost("/preview", PreviewAsync).WithName("PreviewArquebusierImport")
            .WithSummary("Checks an import file for an active comparsa and returns its validation report; nothing is stored (Admin only).")
            .Upload();
        group.MapPost("/", ImportAsync).WithName("ImportArquebusiers")
            .WithSummary("Imports a file into an active comparsa: every row is registered in one transaction, or none when a row has an error (400 with the report) (Admin only).")
            .Upload();
        return endpoints;
    }

    /// <summary>What the check and the import share: a multipart workbook of at most 2 MB, rate-limited per user.</summary>
    private static RouteHandlerBuilder Upload(this RouteHandlerBuilder builder) =>
        builder.Accepts<ArquebusierImportForm>("multipart/form-data")
            .WithMetadata(new RequestSizeLimitAttribute(SpreadsheetUploads.MaxRequestBytes(ImportWorkbookReader.MaxFileBytes)))
            .DisableAntiforgery() // The platform middleware checks X-XSRF-TOKEN before the body is read.
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireRateLimiting(RateLimitPolicies.SpreadsheetImports).ProducesProblem(StatusCodes.Status429TooManyRequests);

    /// <summary>Not audited: the template holds no data (spec: Import template). Its language follows the request.</summary>
    private static FileContentHttpResult Template(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers.Vary = "Accept-Language";
        return TypedResults.File(
            ImportTemplateWriter.Write(ImportTemplateTexts.For(CultureInfo.CurrentUICulture)),
            ImportTemplateWriter.ContentType,
            ImportTemplateWriter.FileName);
    }

    /// <summary>Not audited: a check writes and exports nothing (spec: Import validation report).</summary>
    private static async Task<Results<Ok<ArquebusierImportReport>, ProblemHttpResult>> PreviewAsync(
        HttpRequest request, HttpResponse response, ArquebusierImporter importer, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        var (form, invalid) = await ReadFormAsync(request, loggers.CreateLogger(typeof(ImportEndpoints)), cancellationToken);
        if (form is null)
        {
            return invalid!;
        }

        var check = await importer.CheckAsync(form.Value.ComparsaId, form.Value.Content, cancellationToken);
        return check switch
        {
            { FileProblem: { } problem } => FileProblem(problem),
            { Outcome: RegistryOutcome.Done, Result: { } result } => TypedResults.Ok(result.Report),
            _ => RegistryProblems.From(check.Outcome),
        };
    }

    /// <summary>
    /// Audited in the same transaction when it stores anything (spec: Imports are audited). Rows with
    /// errors answer <c>400 arquebusierImport.rowErrors</c> with the report, so the page can show it.
    /// </summary>
    private static async Task<Results<Ok<ArquebusierImportResult>, ProblemHttpResult>> ImportAsync(
        HttpRequest request, HttpResponse response, ArquebusierImporter importer, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        var (form, invalid) = await ReadFormAsync(request, loggers.CreateLogger(typeof(ImportEndpoints)), cancellationToken);
        if (form is null)
        {
            return invalid!;
        }

        var import = await importer.ImportAsync(form.Value.ComparsaId, form.Value.Content, cancellationToken);
        return import switch
        {
            { FileProblem: { } problem } => FileProblem(problem),
            { Outcome: RegistryOutcome.ImportRowErrors, Result: { } result } => ProblemResults.Problem(
                StatusCodes.Status400BadRequest, RegistryProblems.ImportRowErrors, new Dictionary<string, object?> { ["report"] = result.Report }),
            { Outcome: RegistryOutcome.Done } => TypedResults.Ok(new ArquebusierImportResult(form.Value.ComparsaId, import.ImportedCount)),
            _ => RegistryProblems.From(import.Outcome),
        };
    }

    /// <summary>The comparsa and the workbook, or a <c>400</c> naming every missing or invalid field.</summary>
    private static async Task<((Guid ComparsaId, byte[] Content)? Form, ProblemHttpResult? Invalid)> ReadFormAsync(
        HttpRequest request, ILogger logger, CancellationToken cancellationToken)
    {
        var upload = await SpreadsheetUploads.ReadAsync(request, ImportWorkbookReader.MaxFileBytes, logger, cancellationToken);
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        var comparsaText = upload.Field(ComparsaField);
        var comparsaId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(comparsaText))
        {
            errors[ComparsaField] = InputFields.Required;
        }
        else if (!Guid.TryParse(comparsaText, CultureInfo.InvariantCulture, out comparsaId))
        {
            errors[ComparsaField] = InputFields.Invalid;
        }

        if (!upload.HasFile)
        {
            errors[SpreadsheetUploads.FileField] = upload.Problem;
        }

        if (errors.Count > 0 || !upload.HasFile)
        {
            return (null, ProblemResults.Invalid(errors));
        }

        return ((comparsaId, await upload.ReadContentAsync(cancellationToken)), null);
    }

    /// <summary>A file that cannot be read at all: <c>file</c> names the reason, <c>columns</c> the columns concerned.</summary>
    private static ProblemHttpResult FileProblem(ImportFileProblem problem)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["errors"] = new Dictionary<string, string> { [SpreadsheetUploads.FileField] = problem.Reason },
        };
        if (problem.Columns.Count > 0)
        {
            extensions["columns"] = problem.Columns;
        }

        return ProblemResults.Problem(StatusCodes.Status400BadRequest, ProblemResults.Validation, extensions);
    }
}

/// <summary>An import upload.</summary>
/// <param name="ComparsaId">The active comparsa the arquebusiers belong to.</param>
/// <param name="File">The workbook: an <c>.xlsx</c> file of at most 2 MB and 1000 data rows.</param>
internal sealed record ArquebusierImportForm(Guid ComparsaId, IFormFile File);

/// <summary>A completed import.</summary>
/// <param name="ComparsaId">The comparsa the arquebusiers were registered in.</param>
/// <param name="ImportedCount">How many arquebusiers were registered.</param>
internal sealed record ArquebusierImportResult(Guid ComparsaId, int ImportedCount);
