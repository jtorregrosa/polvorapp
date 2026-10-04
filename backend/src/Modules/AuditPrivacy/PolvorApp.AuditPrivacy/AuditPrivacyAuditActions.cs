using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.AuditPrivacy;

/// <summary>The audit action codes this module records (design D2, D3, D11).</summary>
internal static class AuditPrivacyAuditActions
{
    public const string PersonLookedUp = "PersonLookedUp";
    public const string PersonalDataExported = "PersonalDataExported";
    public const string PersonalDataErased = "PersonalDataErased";
    public const string AuditEntriesPurged = "AuditEntriesPurged";

    public const string RequestEntityType = "PersonalDataRequest";
    public const string AuditTrailEntityType = "AuditTrail";

    public static readonly IReadOnlyList<AuditActionDefinition> All =
    [
        new(PersonLookedUp, RequestEntityType, AuditRetentionClass.Security),
        new(PersonalDataExported, RequestEntityType),
        new(PersonalDataErased, RequestEntityType),
        new(AuditEntriesPurged, AuditTrailEntityType),
    ];
}
