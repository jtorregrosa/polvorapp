using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.Exports.Contracts;
using PolvorApp.Exports.Definitions;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Codes;
using PolvorApp.SharedKernel.Http;

namespace PolvorApp.Exports.Endpoints;

/// <summary>
/// Builds an export for the caller (design D5): checks the edition and the scope, builds the table,
/// renders the file and records it in the audit trail before returning it. A file that could not be
/// audited is not returned (SEC-05).
/// </summary>
internal sealed partial class ExportService(
    IEnumerable<IExportDefinition> definitions,
    ExportDataLoader loader,
    IEditionDirectory editions,
    ICatalogDirectory catalog,
    IComparsaScope scope,
    ICurrentUser currentUser,
    IAuditLog auditLog,
    IDocumentRenderer renderer,
    ILogger<ExportService> logger)
{
    public const string AuditAction = "ExportDownloaded";
    public const string NotPrepared = "exports.notPrepared";
    public const string AuditUnavailable = "exports.auditUnavailable";

    /// <summary>The definitions, for the exports page (Admins).</summary>
    public async Task<ExportCatalogResponse?> CatalogAsync(Guid editionId, CancellationToken cancellationToken) =>
        await editions.FindAsync(editionId, cancellationToken) is null
            ? null
            : new ExportCatalogResponse([.. definitions.Select(d => new ExportDefinitionResponse(d.Name, d.Version, d.Provisional, d.Audience))]);

    /// <summary>A recipient export of the edition's validated orders (Admins; the route requires it).</summary>
    public async Task<Results<FileContentHttpResult, ProblemHttpResult>> RecipientAsync(
        Guid editionId, string definitionName, string format, CancellationToken cancellationToken)
    {
        var definition = definitions.SingleOrDefault(d => d.Audience == ExportAudience.Recipient && d.Name == definitionName);
        if (definition is null || ParseFormat(format) is not { } parsed
            || await editions.FindAsync(editionId, cancellationToken) is not { } edition)
        {
            return ProblemResults.NotFound("exports.notFound");
        }

        var data = await loader.LoadValidatedAsync(edition.Year, edition.Id, cancellationToken);
        var table = definition.Build(data, ExportTexts.Spanish);
        return await DeliverAsync(definition, table, parsed, edition, comparsaId: null, orderStatus: null, cancellationToken);
    }

    /// <summary>A comparsa's list, in any order status (a draft until validated), in the user's language.</summary>
    public async Task<Results<FileContentHttpResult, ProblemHttpResult>> ComparsaListAsync(
        Guid editionId, Guid comparsaId, string format, CancellationToken cancellationToken)
    {
        if (ParseFormat(format) is not { } parsed)
        {
            return ProblemResults.NotFound("exports.notFound");
        }

        var edition = await editions.FindAsync(editionId, cancellationToken);
        var access = await scope.GetAccessAsync(cancellationToken);
        if (edition is null
            || (!currentUser.IsAdmin && edition.Status == EditionStatus.Draft)
            || !access.CanAccess(comparsaId)
            || await catalog.FindComparsaAsync(comparsaId, cancellationToken) is null)
        {
            return ProblemResults.NotFound("exports.notFound");
        }

        if (await loader.LoadComparsaAsync(edition.Year, edition.Id, comparsaId, cancellationToken) is not { } data)
        {
            return ProblemResults.Conflict(NotPrepared);
        }

        var definition = definitions.Single(d => d.Audience == ExportAudience.Comparsa);
        var table = definition.Build(data, ExportTexts.For(CultureInfo.CurrentUICulture));
        return await DeliverAsync(definition, table, parsed, edition, comparsaId, data.Orders[0].Status, cancellationToken);
    }

    private async Task<Results<FileContentHttpResult, ProblemHttpResult>> DeliverAsync(
        IExportDefinition definition, DocumentTable table, DocumentFileFormat format, EditionSnapshot edition, Guid? comparsaId, OrderStatus? orderStatus,
        CancellationToken cancellationToken)
    {
        var document = renderer.RenderTable(table, format);
        try
        {
            await auditLog.RecordAsync(
                new AuditRecord(
                    AuditAction,
                    "Export",
                    definition.Name,
                    new
                    {
                        version = definition.Version,
                        format = EnumCodes.ToCode(format),
                        editionId = edition.Id,
                        editionYear = edition.Year,
                        rows = table.Rows.Count,
                        orderStatus = orderStatus is { } status ? EnumCodes.ToCode(status) : null,
                    },
                    comparsaId),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // No file leaves unaudited (SEC-05).
            LogAuditFailed(logger, exception, definition.Name, edition.Id);
            return ProblemResults.Problem(StatusCodes.Status503ServiceUnavailable, AuditUnavailable);
        }

        return TypedResults.File(document.Content.ToArray(), document.ContentType, document.FileName);
    }

    /// <summary><c>xlsx</c> or <c>pdf</c>, as in the route; anything else is not found.</summary>
    private static DocumentFileFormat? ParseFormat(string format) => format switch
    {
        "xlsx" => DocumentFileFormat.Xlsx,
        "pdf" => DocumentFileFormat.Pdf,
        _ => null,
    };

    [LoggerMessage(Level = LogLevel.Error, Message = "Export {Definition} of edition {EditionId} could not be audited and was not returned")]
    private static partial void LogAuditFailed(ILogger logger, Exception exception, string definition, Guid editionId);
}
