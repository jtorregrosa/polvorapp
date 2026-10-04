using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComparsaOrders.Loans;
using PolvorApp.ComparsaOrders.Orders;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.ComparsaOrders;

/// <summary>The audit action codes this module records (add-audit-privacy, design D2).</summary>
internal static class ComparsaOrdersAuditActions
{
    public const string ComparsaOrderPrepared = "ComparsaOrderPrepared";
    public const string ComparsaOrderSubmitted = "ComparsaOrderSubmitted";
    public const string ComparsaOrderValidated = "ComparsaOrderValidated";
    public const string ComparsaOrderReturned = "ComparsaOrderReturned";
    public const string EditionEntryAdded = "EditionEntryAdded";
    public const string EditionEntryUpdated = "EditionEntryUpdated";
    public const string LoanLenderLookedUp = "LoanLenderLookedUp";

    public static readonly IReadOnlyList<AuditActionDefinition> All =
    [
        new(ComparsaOrderPrepared, OrderAdministration.EntityType),
        new(ComparsaOrderSubmitted, OrderAdministration.EntityType),
        new(ComparsaOrderValidated, OrderAdministration.EntityType),
        new(ComparsaOrderReturned, OrderAdministration.EntityType),
        new(EditionEntryAdded, EntryAdministration.EntityType),
        new(EditionEntryUpdated, EntryAdministration.EntityType),
        new(LoanLenderLookedUp, LenderLookup.EntityType, AuditRetentionClass.Security),
    ];
}
