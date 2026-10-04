using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Endpoints;
using PolvorApp.ComparsaOrders.Persistence;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Validation;

namespace PolvorApp.ComparsaOrders.Loans;

/// <summary>
/// The lender lookup (spec: Lender lookup (UC-13, BR-12); design D9): the only read of another
/// comparsa's arquebusiers. It needs a valid DNI/NIE, answers only an exact match with names, comparsa
/// and weapons (never an ownership guide), and is audited without the identifier. The endpoint is
/// rate-limited per user.
/// </summary>
internal sealed class LenderLookup(
    ComparsaOrdersDbContext db,
    ICurrentUser currentUser,
    IComparsaScope scope,
    IEditionDirectory editions,
    IArquebusierRoster roster,
    ICatalogDirectory catalog,
    IAuditTrail trail)
{
    public const string EntityType = "WeaponLoan";

    /// <summary>
    /// The lender, or why the caller may not look one up: an invalid ID (400), a FiringChief with no
    /// comparsa (404) or closed orders for a FiringChief (409).
    /// </summary>
    public async Task<OrderResult<LenderLookupResponse>> FindAsync(string? nationalId, CancellationToken cancellationToken)
    {
        var parsed = NationalId.Parse(nationalId);
        if (parsed.Value is not { } normalised)
        {
            return OrderResult<LenderLookupResponse>.Invalid(new Dictionary<string, string> { ["nationalId"] = parsed.Error ?? InputFields.Invalid });
        }

        // Only users who may edit an order now: a FiringChief with a comparsa, while the current
        // edition's orders are open.
        if (!currentUser.IsAdmin)
        {
            if ((await scope.GetAccessAsync(cancellationToken)).ComparsaIds.Count == 0)
            {
                return OrderResult<LenderLookupResponse>.Failed(OrderOutcome.NotFound);
            }

            if ((await editions.GetCurrentAsync(cancellationToken)) is not { OrdersOpen: true })
            {
                return OrderResult<LenderLookupResponse>.Failed(OrderOutcome.Closed);
            }
        }

        var lender = await roster.FindLenderAsync(normalised, cancellationToken);
        trail.Record(db, new AuditRecord(ComparsaOrdersAuditActions.LoanLenderLookedUp, EntityType, null, new { found = lender is not null }));
        await db.SaveChangesAsync(cancellationToken);
        return OrderResult<LenderLookupResponse>.Done(lender is null
            ? new LenderLookupResponse(Registered: false, null)
            : new LenderLookupResponse(Registered: true, await ToResponseAsync(lender, cancellationToken)));
    }

    /// <summary>
    /// A loan write refused with <c>lenderRegistered</c> told the caller that an arquebusier has that DNI:
    /// it is recorded like a lookup, without the identifier (design D9). The refused write is discarded
    /// first, so only this entry is saved.
    /// </summary>
    public async Task RecordRegisteredProbeAsync(CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        trail.Record(db, new AuditRecord(ComparsaOrdersAuditActions.LoanLenderLookedUp, EntityType, null, new { found = true, via = "externalLoan" }));
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<LenderResponse> ToResponseAsync(LenderSummary lender, CancellationToken cancellationToken)
    {
        var comparsa = await catalog.FindComparsaAsync(lender.ComparsaId, cancellationToken)
            ?? throw new InvalidOperationException($"Lender {lender.ArquebusierId} belongs to a missing comparsa.");
        var models = (await catalog.FindWeaponModelsAsync([.. lender.Weapons.Select(w => w.WeaponModelId).Distinct()], cancellationToken))
            .ToDictionary(m => m.Id, m => m.Label);
        return new LenderResponse(
            lender.FirstName,
            lender.LastName,
            comparsa.Name,
            [.. lender.Weapons.Select(w => new LenderWeaponResponse(
                w.Id,
                new WeaponModelReference(w.WeaponModelId, models.TryGetValue(w.WeaponModelId, out var label)
                    ? label
                    : throw new InvalidOperationException($"Weapon model {w.WeaponModelId} is missing from the catalogue.")),
                w.WeaponNumber))]);
    }
}
