using PolvorApp.Distribution.Days;
using PolvorApp.Distribution.Documents;
using PolvorApp.Distribution.Handovers;
using PolvorApp.Distribution.Proxies;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.Distribution;

/// <summary>The audit action codes this module records (add-audit-privacy, design D2).</summary>
internal static class DistributionAuditActions
{
    public const string DistributionPlanned = "DistributionPlanned";
    public const string DistributionEdited = "DistributionEdited";
    public const string DistributionDeleted = "DistributionDeleted";
    public const string DistributionSlotsChanged = "DistributionSlotsChanged";
    public const string PickupProxyAuthorised = "PickupProxyAuthorised";
    public const string PickupProxyRemoved = "PickupProxyRemoved";
    public const string CapturePackageDownloaded = "CapturePackageDownloaded";
    public const string HandoverRecorded = "HandoverRecorded";
    public const string HandoverUndone = "HandoverUndone";

    public static readonly IReadOnlyList<AuditActionDefinition> All =
    [
        new(DistributionPlanned, DistributionDayAdministration.EntityType),
        new(DistributionEdited, DistributionDayAdministration.EntityType),
        new(DistributionDeleted, DistributionDayAdministration.EntityType),
        new(DistributionSlotsChanged, DistributionDayAdministration.EntityType),
        new(PickupProxyAuthorised, ProxyAdministration.EntityType),
        new(PickupProxyRemoved, ProxyAdministration.EntityType),
        new(DistributionDocuments.AuditAction, DistributionDocuments.EntityType),
        new(CapturePackageDownloaded, DistributionDayAdministration.EntityType),
        new(HandoverRecorded, HandoverSync.EntityType),
        new(HandoverUndone, HandoverSync.EntityType),
    ];
}
