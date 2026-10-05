using PolvorApp.FederationCatalog.Assignments;
using PolvorApp.FederationCatalog.Comparsas;
using PolvorApp.FederationCatalog.Logos;
using PolvorApp.FederationCatalog.WeaponModels;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.FederationCatalog;

/// <summary>The audit action codes this module records (add-audit-privacy, design D2).</summary>
internal static class FederationCatalogAuditActions
{
    public const string ComparsaCreated = "ComparsaCreated";
    public const string ComparsaUpdated = "ComparsaUpdated";
    public const string ComparsaDeleted = "ComparsaDeleted";
    public const string ComparsaDeactivated = "ComparsaDeactivated";
    public const string ComparsaReactivated = "ComparsaReactivated";
    public const string ComparsaLogoUploaded = "ComparsaLogoUploaded";
    public const string ComparsaLogoRemoved = "ComparsaLogoRemoved";
    public const string FederationLogoUploaded = "FederationLogoUploaded";
    public const string FederationLogoRemoved = "FederationLogoRemoved";
    public const string FederationSettingsChanged = "FederationSettingsChanged";
    public const string WeaponModelCreated = "WeaponModelCreated";
    public const string WeaponModelUpdated = "WeaponModelUpdated";
    public const string WeaponModelDeleted = "WeaponModelDeleted";
    public const string WeaponModelDeactivated = "WeaponModelDeactivated";
    public const string WeaponModelReactivated = "WeaponModelReactivated";
    public const string FiringChiefAssigned = "FiringChiefAssigned";
    public const string FiringChiefUnassigned = "FiringChiefUnassigned";

    public static readonly IReadOnlyList<AuditActionDefinition> All =
    [
        new(ComparsaCreated, ComparsaAdministration.EntityType),
        new(ComparsaUpdated, ComparsaAdministration.EntityType),
        new(ComparsaDeleted, ComparsaAdministration.EntityType),
        new(ComparsaDeactivated, ComparsaAdministration.EntityType),
        new(ComparsaReactivated, ComparsaAdministration.EntityType),
        new(ComparsaLogoUploaded, ComparsaAdministration.EntityType),
        new(ComparsaLogoRemoved, ComparsaAdministration.EntityType),
        new(FederationLogoUploaded, FederationLogoAdministration.EntityType),
        new(FederationLogoRemoved, FederationLogoAdministration.EntityType),
        new(FederationSettingsChanged, Settings.FederationSettingsAdministration.EntityType),
        new(WeaponModelCreated, WeaponModelAdministration.EntityType),
        new(WeaponModelUpdated, WeaponModelAdministration.EntityType),
        new(WeaponModelDeleted, WeaponModelAdministration.EntityType),
        new(WeaponModelDeactivated, WeaponModelAdministration.EntityType),
        new(WeaponModelReactivated, WeaponModelAdministration.EntityType),
        new(FiringChiefAssigned, AssignmentAdministration.EntityType),
        new(FiringChiefUnassigned, AssignmentAdministration.EntityType),
    ];
}
