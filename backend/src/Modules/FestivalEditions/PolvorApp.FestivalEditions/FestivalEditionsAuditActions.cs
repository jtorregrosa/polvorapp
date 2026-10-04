using PolvorApp.FestivalEditions.Editions;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.FestivalEditions;

/// <summary>The audit action codes this module records (add-audit-privacy, design D2).</summary>
internal static class FestivalEditionsAuditActions
{
    public const string EditionCreated = "EditionCreated";
    public const string EditionUpdated = "EditionUpdated";
    public const string EditionDeleted = "EditionDeleted";
    public const string EditionStatusChanged = "EditionStatusChanged";
    public const string EditionOrdersOpened = "EditionOrdersOpened";
    public const string EditionOrdersClosed = "EditionOrdersClosed";
    public const string EditionWeaponModelsChanged = "EditionWeaponModelsChanged";
    public const string CalendarMilestoneAdded = "CalendarMilestoneAdded";
    public const string CalendarMilestoneUpdated = "CalendarMilestoneUpdated";
    public const string CalendarMilestoneRemoved = "CalendarMilestoneRemoved";

    public static readonly IReadOnlyList<AuditActionDefinition> All =
    [
        new(EditionCreated, EditionAdministration.EntityType),
        new(EditionUpdated, EditionAdministration.EntityType),
        new(EditionDeleted, EditionAdministration.EntityType),
        new(EditionStatusChanged, EditionAdministration.EntityType),
        new(EditionOrdersOpened, EditionAdministration.EntityType),
        new(EditionOrdersClosed, EditionAdministration.EntityType),
        new(EditionWeaponModelsChanged, EditionAdministration.EntityType),
        new(CalendarMilestoneAdded, CalendarMilestoneAdministration.EntityType),
        new(CalendarMilestoneUpdated, CalendarMilestoneAdministration.EntityType),
        new(CalendarMilestoneRemoved, CalendarMilestoneAdministration.EntityType),
    ];
}
