using PolvorApp.ArquebusierRegistry.Arquebusiers;
using PolvorApp.ArquebusierRegistry.Import;
using PolvorApp.ArquebusierRegistry.Lock;
using PolvorApp.ArquebusierRegistry.OwnedWeapons;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.ArquebusierRegistry;

/// <summary>The audit action codes this module records (add-audit-privacy, design D2).</summary>
internal static class ArquebusierRegistryAuditActions
{
    public const string ArquebusierUpdated = "ArquebusierUpdated";
    public const string ArquebusierTransferred = "ArquebusierTransferred";
    public const string ArquebusierDeleted = "ArquebusierDeleted";
    public const string ArquebusierPhotoUploaded = "ArquebusierPhotoUploaded";
    public const string ArquebusierPhotoRemoved = "ArquebusierPhotoRemoved";
    public const string RegistryLocked = "RegistryLocked";
    public const string RegistryUnlocked = "RegistryUnlocked";
    public const string OwnedWeaponAdded = "OwnedWeaponAdded";
    public const string OwnedWeaponUpdated = "OwnedWeaponUpdated";
    public const string OwnedWeaponRemoved = "OwnedWeaponRemoved";

    public static readonly IReadOnlyList<AuditActionDefinition> All =
    [
        new(ArquebusierAdministration.RegisteredAction, ArquebusierAdministration.EntityType),
        new(ArquebusierUpdated, ArquebusierAdministration.EntityType),
        new(ArquebusierTransferred, ArquebusierAdministration.EntityType),
        new(ArquebusierDeleted, ArquebusierAdministration.EntityType),
        new(ArquebusierPhotoUploaded, ArquebusierAdministration.EntityType),
        new(ArquebusierPhotoRemoved, ArquebusierAdministration.EntityType),
        new(ArquebusierImporter.ImportedAction, ArquebusierImporter.ComparsaEntityType),
        new(RegistryLocked, RegistryLockAdministration.EntityType),
        new(RegistryUnlocked, RegistryLockAdministration.EntityType),
        new(OwnedWeaponAdded, OwnedWeaponAdministration.EntityType),
        new(OwnedWeaponUpdated, OwnedWeaponAdministration.EntityType),
        new(OwnedWeaponRemoved, OwnedWeaponAdministration.EntityType),
    ];
}
