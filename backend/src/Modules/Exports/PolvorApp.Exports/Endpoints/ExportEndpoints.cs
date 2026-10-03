using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using PolvorApp.Exports.Definitions;
using PolvorApp.Exports.Writers;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Http;
using PolvorApp.SharedKernel.Security;

namespace PolvorApp.Exports.Endpoints;

/// <summary>One export definition, as the exports page lists it.</summary>
/// <param name="Name">Its name in routes and files, e.g. <c>arms-authority</c>.</param>
/// <param name="Version">The version written in the document.</param>
/// <param name="Provisional">True until the recipient's template arrives.</param>
/// <param name="Audience">A recipient export (Admins) or a comparsa list.</param>
internal sealed record ExportDefinitionResponse(string Name, string Version, bool Provisional, ExportAudience Audience);

/// <summary>The export definitions of an edition.</summary>
internal sealed record ExportCatalogResponse(IReadOnlyList<ExportDefinitionResponse> Definitions);

/// <summary>
/// The exports (spec: Who may export (BR-12); design D5): files generated per request, never stored,
/// audited and limited per user. <c>Cache-Control: no-store</c> comes from the host's security headers,
/// on every API answer, refusals included.
/// </summary>
internal static class ExportEndpoints
{
    public static IEndpointRouteBuilder MapExportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/exports/editions/{editionId:guid}").WithTags("Exports")
            .RequireRateLimiting(RateLimitPolicies.Exports)
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        var admins = group.MapGroup(string.Empty).RequireAuthorization(AuthorizationPolicies.Admin)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        admins.MapGet("/", CatalogAsync).WithName("GetExportCatalog")
            .WithSummary("The export definitions of an edition, with their versions and whether they are provisional (Admins).");
        admins.MapGet("/recipients/{definition}/{format}", RecipientAsync).WithName("DownloadRecipientExport")
            .WithSummary("The powder supplier, rental company or Arms Authority export of the validated orders, as xlsx or pdf; audited (Admins).")
            .Produces<Stream>(StatusCodes.Status200OK, XlsxExportWriter.ContentType, PdfExportWriter.ContentType)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapGet("/comparsas/{comparsaId:guid}/{format}", ComparsaListAsync).WithName("DownloadComparsaList")
            .WithSummary("A comparsa's list of its order, as xlsx or pdf, a draft until the order is validated; audited (Admins and the comparsa's FiringChiefs).")
            .Produces<Stream>(StatusCodes.Status200OK, XlsxExportWriter.ContentType, PdfExportWriter.ContentType)
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return endpoints;
    }

    private static async Task<Results<Ok<ExportCatalogResponse>, ProblemHttpResult>> CatalogAsync(
        Guid editionId, ExportService exports, CancellationToken cancellationToken) =>
        await exports.CatalogAsync(editionId, cancellationToken) is { } catalog
            ? TypedResults.Ok(catalog)
            : ProblemResults.NotFound("exports.notFound");

    private static Task<Results<FileContentHttpResult, ProblemHttpResult>> RecipientAsync(
        Guid editionId, string definition, string format, ExportService exports, CancellationToken cancellationToken) =>
        exports.RecipientAsync(editionId, definition, format, cancellationToken);

    private static Task<Results<FileContentHttpResult, ProblemHttpResult>> ComparsaListAsync(
        Guid editionId, Guid comparsaId, string format, ExportService exports, CancellationToken cancellationToken) =>
        exports.ComparsaListAsync(editionId, comparsaId, format, cancellationToken);

}
