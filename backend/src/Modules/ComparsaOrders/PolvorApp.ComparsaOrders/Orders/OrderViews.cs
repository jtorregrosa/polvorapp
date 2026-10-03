using Microsoft.EntityFrameworkCore;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.Billing.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Endpoints;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComparsaOrders.Loans;
using PolvorApp.ComparsaOrders.Persistence;
using PolvorApp.ComparsaOrders.Totals;
using PolvorApp.ComplianceInsights.Contracts;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Text;

namespace PolvorApp.ComparsaOrders.Orders;

/// <summary>
/// Reads of comparsa orders (specs: Order visibility (BR-12), Orders screens; design D6). An order
/// outside the caller's scope, or of a draft edition for a FiringChief, reads as missing.
/// </summary>
internal sealed class OrderViews(
    ComparsaOrdersDbContext db,
    IComparsaScope scope,
    ICurrentUser currentUser,
    IEditionDirectory editions,
    ICatalogDirectory catalog,
    IArquebusierRoster roster,
    IComplianceRules rules,
    IUserDirectory users,
    IParticipationHistory participation,
    IBillingCalculator billing)
{
    /// <summary>The order as the caller may see it, or null.</summary>
    public async Task<OrderResponse?> FindAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order is null || !(await scope.GetAccessAsync(cancellationToken)).CanAccess(order.ComparsaId))
        {
            return null;
        }

        var edition = await editions.FindAsync(order.EditionId, cancellationToken)
            ?? throw new InvalidOperationException($"Order {order.Id} references a missing edition.");
        return !currentUser.IsAdmin && edition.Status == EditionStatus.Draft
            ? null
            : await ToResponseAsync(order, edition, cancellationToken);
    }

    private async Task<OrderResponse> ToResponseAsync(ComparsaOrder order, EditionSnapshot edition, CancellationToken cancellationToken)
    {
        var comparsa = await catalog.FindComparsaAsync(order.ComparsaId, cancellationToken)
            ?? throw new InvalidOperationException($"Order {order.Id} references a missing comparsa.");
        var entries = await db.Entries.AsNoTracking().Where(e => e.OrderId == order.Id).ToListAsync(cancellationToken);
        var comparsaRoster = await roster.ListByComparsaAsync(order.ComparsaId, cancellationToken);
        var live = await LiveArquebusiersAsync(entries, comparsaRoster, cancellationToken);
        var loans = await db.Loans.AsNoTracking()
            .Where(l => db.Entries.Any(e => e.Id == l.EntryId && e.OrderId == order.Id))
            .ToDictionaryAsync(l => l.EntryId, cancellationToken);
        var models = await ModelLabelsAsync(entries, live, loans.Values, edition.OfferedWeaponModelIds, cancellationToken);
        var comparsaNames = await ComparsaNamesAsync(loans.Values, cancellationToken);
        var names = await UserNamesAsync(order, cancellationToken);
        var offered = edition.OfferedWeaponModelIds.ToHashSet();
        var firstYear = await participation.FirstYearAsync(
            order.EditionYear, [.. entries.Select(e => e.ArquebusierId).OfType<Guid>().Where(live.ContainsKey)], cancellationToken);

        var entryResponses = entries
            .Select(e => ToResponse(
                e,
                e.ArquebusierId is { } id ? live.GetValueOrDefault(id) : null,
                loans.GetValueOrDefault(e.Id),
                comparsaNames,
                models,
                edition,
                offered,
                firstYear))
            .OrderBy(e => e.Arquebusier.LastName, SpanishOrder.Names)
            .ThenBy(e => e.Arquebusier.FirstName, SpanishOrder.Names)
            .ThenBy(e => e.Id)
            .ToList();
        var notInOrder = (await NotInEditionAsync(order.EditionId, comparsaRoster, cancellationToken))
            .Select(a => new NotInOrderResponse(a.Id, a.FirstName, a.LastName, a.Status))
            .ToList();
        var lentOut = await LentOutAsync(order, comparsaRoster, cancellationToken);
        var (canEdit, readOnlyReason) = Editable(order, edition);
        var totals = OrderTotals.Of(entries, entryResponses.Where(e => e.Warnings.Count > 0).Select(e => e.Id).ToHashSet());

        return new OrderResponse(
            order.Id,
            order.Version,
            order.Status,
            new OrderEditionResponse(edition.Id, edition.Year, edition.Status, edition.OrdersOpen),
            new OrderComparsaResponse(comparsa.Id, comparsa.Name, comparsa.Side),
            order.PreparedAt,
            order.SubmittedAt is { } submittedAt
                ? new OrderSubmissionResponse(submittedAt, Name(names, order.SubmittedByUserId), order.Attested, order.SubmittedByAdmin)
                : null,
            order.ReviewedAt is { } reviewedAt
                ? new OrderReviewResponse(reviewedAt, Name(names, order.ReviewedByUserId), order.ReturnReason)
                : null,
            canEdit,
            readOnlyReason,
            entryResponses,
            notInOrder,
            lentOut,
            OrderTotalsResponse.From(totals, models),
            [.. edition.OfferedWeaponModelIds
                .Select(id => new WeaponModelReference(id, Label(models, id)))
                .OrderBy(m => m.Label, SpanishOrder.Names)
                .ThenBy(m => m.Id)],
            BillingSummaryResponse.From(billing.Summarise(
                BillingMapping.QuantitiesOf(totals), BillingMapping.PricesOf(edition.Prices), BillingMapping.StateOfOrder(order.Status))));
    }

    /// <summary>
    /// The loans in the order's edition of weapons the comparsa's arquebusiers own now. The borrowers'
    /// orders stay out of reach: only their names and comparsa are read (BR-12 exception).
    /// </summary>
    private async Task<List<LentOutResponse>> LentOutAsync(
        ComparsaOrder order, IReadOnlyList<RosterArquebusier> comparsaRoster, CancellationToken cancellationToken)
    {
        var weapons = comparsaRoster.SelectMany(a => a.Weapons).ToDictionary(w => w.Id);
        if (weapons.Count == 0)
        {
            return [];
        }

        Guid[] weaponIds = [.. weapons.Keys];
        var rows = await db.Loans.AsNoTracking()
            .Where(l => l.LenderOwnedWeaponId != null && weaponIds.Contains(l.LenderOwnedWeaponId.Value))
            .Join(db.Entries.AsNoTracking(), l => l.EntryId, e => e.Id, (l, e) => new { Loan = l, Entry = e })
            .Join(db.Orders.AsNoTracking().Where(o => o.EditionId == order.EditionId), x => x.Entry.OrderId, o => o.Id, (x, o) => new
            {
                LoanId = x.Loan.Id,
                WeaponId = x.Loan.LenderOwnedWeaponId!.Value,
                x.Entry.ArquebusierId,
                x.Entry.FirstName,
                x.Entry.LastName,
                o.ComparsaId,
            })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        // Stored links only: the borrowers' current names, or their entries' copies once they left.
        Guid[] borrowerIds = [.. rows.Select(r => r.ArquebusierId).OfType<Guid>().Distinct()];
        var borrowers = (await roster.FindManyAsync(borrowerIds, cancellationToken)).ToDictionary(a => a.Id);
        var comparsas = (await catalog.FindComparsasAsync([.. rows.Select(r => r.ComparsaId).Distinct()], cancellationToken)).ToDictionary(c => c.Id, c => c.Name);
        var models = (await catalog.FindWeaponModelsAsync([.. rows.Select(r => weapons[r.WeaponId].WeaponModelId).Distinct()], cancellationToken)).ToDictionary(m => m.Id, m => m.Label);
        var owners = comparsaRoster.ToDictionary(a => a.Id);

        return [.. rows
            .Select(r =>
            {
                var weapon = weapons[r.WeaponId];
                var owner = owners[weapon.OwnerId];
                var borrower = r.ArquebusierId is { } id && borrowers.TryGetValue(id, out var live) ? live : null;
                return new LentOutResponse(
                    r.LoanId,
                    new WeaponModelReference(weapon.WeaponModelId, Label(models, weapon.WeaponModelId)),
                    weapon.WeaponNumber,
                    owner.FirstName,
                    owner.LastName,
                    borrower?.FirstName ?? r.FirstName,
                    borrower?.LastName ?? r.LastName,
                    comparsas.TryGetValue(r.ComparsaId, out var name) ? name : throw new InvalidOperationException($"Comparsa {r.ComparsaId} is missing from the catalogue."));
            })
            .OrderBy(l => l.LenderLastName, SpanishOrder.Names)
            .ThenBy(l => l.BorrowerLastName, SpanishOrder.Names)
            .ThenBy(l => l.LoanId)];
    }

    /// <summary>BR-10 as the caller sees it now; the writes check it again under the edition lock.</summary>
    private (bool CanEdit, string? Reason) Editable(ComparsaOrder order, EditionSnapshot edition) =>
        currentUser.IsAdmin ? (true, null)
        : !edition.OrdersOpen ? (false, ReadOnlyReasons.OrdersClosed)
        : order.Status == OrderStatus.Validated ? (false, ReadOnlyReasons.Validated)
        : (true, null);

    private EntryResponse ToResponse(
        EditionEntry entry,
        RosterArquebusier? arquebusier,
        WeaponLoan? loan,
        Dictionary<Guid, string> comparsas,
        Dictionary<Guid, string> models,
        EditionSnapshot edition,
        IReadOnlySet<Guid> offered,
        FirstYearResult firstYear)
    {
        var person = arquebusier is null
            ? new EntryArquebusierResponse(null, entry.FirstName, entry.LastName, entry.NationalId, entry.FederationId, InRegistry: false)
            : new EntryArquebusierResponse(arquebusier.Id, arquebusier.FirstName, arquebusier.LastName, arquebusier.NationalId, arquebusier.FederationId, InRegistry: true);
        var owned = entry.WeaponSource == WeaponSource.Owned ? OwnedWeapon(entry, arquebusier, models) : null;
        var rental = entry.RentalWeaponModelId is { } model ? new WeaponModelReference(model, Label(models, model)) : null;
        var warnings = edition.Status == EditionStatus.InProgress && arquebusier is not null && entry.Status == ArquebusierStatus.Active
            ? rules.EvaluateForFestival(EntryCopies.FactsOf(arquebusier), edition.FestivalStartsOn, edition.FestivalEndsOn)
            : [];

        return new EntryResponse(
            entry.Id,
            entry.Version,
            person,
            entry.Status,
            entry.PowderKg,
            entry.CapsBoxes,
            entry.CapsType,
            entry.WeaponSource,
            owned,
            rental,
            loan is null ? null : ToResponse(loan, models, comparsas),
            entry.Flask,
            warnings,
            EntryIssues.Of(entry, loan, offered),
            arquebusier is null ? null : firstYear.Of(arquebusier.Id),
            arquebusier is null
                ? []
                : [.. arquebusier.Weapons.Select(w => new EntryWeaponResponse(w.Id, w.WeaponModelId, Label(models, w.WeaponModelId), w.WeaponNumber, Removed: false))]);
    }

    /// <summary>The loan from its stored copy; an external owner's guide and DNI were typed by the borrower's comparsa.</summary>
    private static EntryLoanResponse ToResponse(WeaponLoan loan, Dictionary<Guid, string> models, Dictionary<Guid, string> comparsas)
    {
        var external = loan.LenderKind == LenderKind.External;
        return new EntryLoanResponse(
            loan.LenderKind,
            loan.LenderFirstName,
            loan.LenderLastName,
            loan.LenderComparsaId is { } comparsa ? comparsas.GetValueOrDefault(comparsa) : null,
            loan.LenderOwnedWeaponId,
            loan.WeaponModelId is { } model ? new WeaponModelReference(model, Label(models, model)) : null,
            loan.WeaponNumber,
            external ? loan.OwnershipGuideNumber : null,
            external ? loan.LenderNationalId : null,
            WeaponRemoved: !external && loan.LenderOwnedWeaponId is null);
    }

    /// <summary>The live weapon while it is in the registry, else the entry's copy with its guide, marked removed.</summary>
    private static EntryOwnedWeaponResponse OwnedWeapon(EditionEntry entry, RosterArquebusier? arquebusier, Dictionary<Guid, string> models)
    {
        if (arquebusier?.Weapons.SingleOrDefault(w => w.Id == entry.OwnedWeaponId) is { } weapon)
        {
            return new EntryOwnedWeaponResponse(weapon.Id, weapon.WeaponModelId, Label(models, weapon.WeaponModelId), weapon.WeaponNumber, OwnershipGuideNumber: null, Removed: false);
        }

        return new EntryOwnedWeaponResponse(
            null,
            entry.OwnedWeaponModelId,
            entry.OwnedWeaponModelId is { } model ? Label(models, model) : null,
            entry.OwnedWeaponNumber,
            entry.OwnedWeaponGuideNumber,
            Removed: true);
    }

    /// <summary>
    /// The registry data of the entries' arquebusiers: the comparsa's roster, plus those transferred out
    /// since. A view takes no lock, so an arquebusier deleted after the entries were read is missing here;
    /// their entry then shows its copy, as it will once the deletion's key nulls the link (design D3).
    /// </summary>
    private async Task<Dictionary<Guid, RosterArquebusier>> LiveArquebusiersAsync(
        List<EditionEntry> entries, IReadOnlyList<RosterArquebusier> comparsaRoster, CancellationToken cancellationToken)
    {
        var live = comparsaRoster.ToDictionary(a => a.Id);
        Guid[] elsewhere = [.. entries.Select(e => e.ArquebusierId).OfType<Guid>().Where(id => !live.ContainsKey(id))];
        if (elsewhere.Length == 0)
        {
            return live;
        }

        // Stored links only (transferred arquebusiers), never ids from a request.
        foreach (var arquebusier in await roster.FindManyAsync(elsewhere, cancellationToken))
        {
            live[arquebusier.Id] = arquebusier;
        }

        return live;
    }

    private async Task<List<RosterArquebusier>> NotInEditionAsync(
        Guid editionId, IReadOnlyList<RosterArquebusier> comparsaRoster, CancellationToken cancellationToken)
    {
        Guid[] ids = [.. comparsaRoster.Select(a => a.Id)];
        var taken = (await db.Entries.AsNoTracking()
            .Where(e => e.EditionId == editionId && e.ArquebusierId != null && ids.Contains(e.ArquebusierId.Value))
            .Select(e => e.ArquebusierId!.Value)
            .ToListAsync(cancellationToken))
            .ToHashSet();
        return [.. comparsaRoster.Where(a => !taken.Contains(a.Id))];
    }

    /// <summary>The labels of every model the response names: entries, the entries' arquebusiers' weapons, loans and the offered models.</summary>
    private async Task<Dictionary<Guid, string>> ModelLabelsAsync(
        List<EditionEntry> entries,
        Dictionary<Guid, RosterArquebusier> live,
        IEnumerable<WeaponLoan> loans,
        IEnumerable<Guid> offered,
        CancellationToken cancellationToken)
    {
        var liveWeapons = entries
            .Where(e => e.ArquebusierId is { } id && live.ContainsKey(id))
            .SelectMany(e => live[e.ArquebusierId!.Value].Weapons)
            .Select(w => (Guid?)w.WeaponModelId);
        Guid[] ids = [.. entries
            .SelectMany(e => new[] { e.RentalWeaponModelId, e.OwnedWeaponModelId })
            .Concat(liveWeapons)
            .Concat(loans.Select(l => l.WeaponModelId))
            .Concat(offered.Select(id => (Guid?)id))
            .OfType<Guid>()
            .Distinct()];
        return (await catalog.FindWeaponModelsAsync(ids, cancellationToken)).ToDictionary(m => m.Id, m => m.Label);
    }

    private async Task<Dictionary<Guid, string>> ComparsaNamesAsync(IEnumerable<WeaponLoan> loans, CancellationToken cancellationToken)
    {
        Guid[] ids = [.. loans.Select(l => l.LenderComparsaId).OfType<Guid>().Distinct()];
        return ids.Length == 0 ? [] : (await catalog.FindComparsasAsync(ids, cancellationToken)).ToDictionary(c => c.Id, c => c.Name);
    }

    private async Task<Dictionary<Guid, string>> UserNamesAsync(ComparsaOrder order, CancellationToken cancellationToken)
    {
        Guid[] ids = [.. new[] { order.SubmittedByUserId, order.ReviewedByUserId }.OfType<Guid>().Distinct()];
        return ids.Length == 0 ? [] : (await users.FindManyAsync(ids, cancellationToken)).ToDictionary(u => u.Id, u => u.Name);
    }

    private static string? Name(Dictionary<Guid, string> names, Guid? userId) =>
        userId is { } id && names.TryGetValue(id, out var name) ? name : null;

    private static string Label(Dictionary<Guid, string> models, Guid modelId) =>
        models.TryGetValue(modelId, out var label)
            ? label
            : throw new InvalidOperationException($"Weapon model {modelId} is missing from the catalogue.");
}
