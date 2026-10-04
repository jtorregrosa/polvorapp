using PolvorApp.Exports.Endpoints;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.Exports;

/// <summary>The audit action codes this module records (add-audit-privacy, design D2).</summary>
internal static class ExportsAuditActions
{
    public const string ExportEntityType = "Export";

    public static readonly IReadOnlyList<AuditActionDefinition> All =
    [
        new(ExportService.AuditAction, ExportEntityType),
    ];
}
