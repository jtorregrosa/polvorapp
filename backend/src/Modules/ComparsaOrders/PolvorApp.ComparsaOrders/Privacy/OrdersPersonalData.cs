using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.AuditPrivacy.Contracts;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComparsaOrders.Loans;
using PolvorApp.ComparsaOrders.Persistence;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.SharedKernel.Codes;
using PolvorApp.SharedKernel.Persistence;

namespace PolvorApp.ComparsaOrders.Privacy;

/// <summary>
/// The orders' part of a GDPR request about a person (UC-26; add-audit-privacy, design D5–D7): their
/// edition entries, found by the copy's DNI/NIE or through the registered arquebusier, and the loans in
/// which they are the lender. The erasure anonymises them (spec: Erased entries): the copied identity
/// and weapon number and guide are blanked and the row is marked erased; quantities, model, status and
/// order are kept, so totals and billing do not change.
/// </summary>
internal sealed class OrdersPersonalData(
    ComparsaOrdersDbContext db,
    IArquebusierRoster roster,
    IEditionDirectory editions,
    ICatalogDirectory catalog,
    TimeProvider time) : IPersonalDataParticipant
{
    public const string EntriesSheet = "entries";
    public const string LenderLoansSheet = "lenderLoans";

    public int Order => PersonalDataParticipantOrder.Orders;

    public async Task<PersonalDataSummary> DescribeAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        if (subject is not PersonalDataSubject.Person(var nationalId))
        {
            return PersonalDataSummary.Empty;
        }

        var (arquebusierId, weapons) = await RegisteredAsync(nationalId, cancellationToken);
        var entries = await (
            from entry in EntriesOf(nationalId, arquebusierId)
            join order in db.Orders on entry.OrderId equals order.Id
            orderby order.EditionYear
            select new { entry.Id, order.EditionId, order.EditionYear, order.ComparsaId, order.Status, entry.ErasedAt })
            .ToListAsync(cancellationToken);
        var editionStates = new Dictionary<Guid, EditionSnapshot>();
        foreach (var editionId in entries.Select(e => e.EditionId).Distinct())
        {
            // An order always has its edition (a foreign key): a missing one is an integrity error, not
            // a reason to leave out the warnings it drives.
            editionStates[editionId] = await editions.FindAsync(editionId, cancellationToken)
                ?? throw new InvalidOperationException("An order's edition is missing.");
        }

        var loans = await (
            from loan in LoansOf(nationalId, weapons)
            join entry in db.Entries on loan.EntryId equals entry.Id
            join order in db.Orders on entry.OrderId equals order.Id
            group loan by order.EditionYear into byYear
            orderby byYear.Key
            select new LenderLoansSummary(byYear.Key, byYear.Count()))
            .ToListAsync(cancellationToken);

        return new PersonalDataSummary
        {
            Entries =
            [
                .. entries.Select(e => new EditionEntrySummary(
                    e.Id,
                    e.EditionId,
                    e.EditionYear,
                    EnumCodes.ToCode(editionStates[e.EditionId].Status),
                    editionStates[e.EditionId].OrdersOpen,
                    e.ComparsaId,
                    EnumCodes.ToCode(e.Status),
                    e.ErasedAt is not null)),
            ],
            LenderLoans = loans,
        };
    }

    public async Task<PersonalDataExportPart> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        if (subject is not PersonalDataSubject.Person(var nationalId))
        {
            return PersonalDataExportPart.Empty;
        }

        var (arquebusierId, weapons) = await RegisteredAsync(nationalId, cancellationToken);
        var entries = await (
            from entry in EntriesOf(nationalId, arquebusierId)
            join order in db.Orders on entry.OrderId equals order.Id
            orderby order.EditionYear
            select new { Entry = entry, order.EditionYear, order.ComparsaId })
            .ToListAsync(cancellationToken);
        var borrowedLoans = await db.Loans.AsNoTracking()
            .Where(l => entries.Select(e => e.Entry.Id).Contains(l.EntryId))
            .ToDictionaryAsync(l => l.EntryId, cancellationToken);
        var lent = await (
            from loan in LoansOf(nationalId, weapons)
            join entry in db.Entries on loan.EntryId equals entry.Id
            join order in db.Orders on entry.OrderId equals order.Id
            orderby order.EditionYear
            select new { Loan = loan, order.EditionYear })
            .ToListAsync(cancellationToken);

        var comparsas = (await catalog.FindComparsasAsync([.. entries.Select(e => e.ComparsaId).Distinct()], cancellationToken))
            .ToDictionary(c => c.Id, c => c.Name);
        Guid[] modelIds =
        [
            .. entries.SelectMany(e => new[] { e.Entry.RentalWeaponModelId, e.Entry.OwnedWeaponModelId }).OfType<Guid>(),
            .. borrowedLoans.Values.Select(l => l.WeaponModelId).OfType<Guid>(),
            .. lent.Select(l => l.Loan.WeaponModelId).OfType<Guid>(),
        ];
        var models = (await catalog.FindWeaponModelsAsync([.. modelIds.Distinct()], cancellationToken)).ToDictionary(m => m.Id, m => m.Label);
        string? Model(Guid? id) => id is { } modelId ? models.GetValueOrDefault(modelId) : null;

        var sheets = new List<PersonalDataSheet>();
        if (entries.Count > 0)
        {
            sheets.Add(new PersonalDataSheet(
                EntriesSheet,
                ["edition", "comparsa", "status", "powderKg", "capsBoxes", "capsType", "weaponSource", "flask", "rentalModel",
                 "ownedWeaponModel", "ownedWeaponNumber", "ownedWeaponGuideNumber", "loanLender", "loanWeaponModel",
                 "firstName", "lastName", "nationalId", "federationId", "erased"],
                [
                    .. entries.Select(e =>
                    {
                        var entry = e.Entry;
                        var loan = borrowedLoans.GetValueOrDefault(entry.Id);
                        return (IReadOnlyList<object?>)
                        [
                            e.EditionYear, comparsas.GetValueOrDefault(e.ComparsaId), EnumCodes.ToCode(entry.Status), entry.PowderKg,
                            entry.CapsBoxes, entry.CapsType is { } caps ? EnumCodes.ToCode(caps) : null, EnumCodes.ToCode(entry.WeaponSource),
                            EnumCodes.ToCode(entry.Flask), Model(entry.RentalWeaponModelId), Model(entry.OwnedWeaponModelId), entry.OwnedWeaponNumber,
                            entry.OwnedWeaponGuideNumber,
                            // The lender is another person: only their kind, never their identity.
                            loan is null ? null : EnumCodes.ToCode(loan.LenderKind), Model(loan?.WeaponModelId),
                            entry.FirstName, entry.LastName, entry.NationalId, entry.FederationId, entry.ErasedAt is not null,
                        ];
                    }),
                ]));
        }

        if (lent.Count > 0)
        {
            sheets.Add(new PersonalDataSheet(
                LenderLoansSheet,
                ["edition", "weaponModel", "weaponNumber", "ownershipGuideNumber", "firstName", "lastName", "nationalId"],
                [
                    .. lent.Select(l => (IReadOnlyList<object?>)
                    [
                        l.EditionYear, Model(l.Loan.WeaponModelId), l.Loan.WeaponNumber, l.Loan.OwnershipGuideNumber,
                        l.Loan.LenderFirstName, l.Loan.LenderLastName, l.Loan.LenderNationalId,
                    ]),
                ]));
        }

        return new PersonalDataExportPart(sheets, []);
    }

    public async Task PrepareErasureAsync(
        PersonalDataSubject subject, PersonalDataErasure erasure, DbTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(erasure);
        if (subject is not PersonalDataSubject.Person(var nationalId))
        {
            return;
        }

        await db.EnlistAsync(transaction, cancellationToken);
        // The module's lock order, as for an order write or a deletion: the edition in progress FOR SHARE
        // (a status move waits for the erasure), then the person's orders FOR UPDATE by id, so the
        // erasure serialises with the writes of those orders without a deadlock.
        if (await editions.GetCurrentAsync(cancellationToken) is { } current)
        {
            await editions.ReadForOrderWriteAsync(current.Id, transaction, cancellationToken);
        }

        Guid[] weapons = [.. erasure.OwnedWeaponIds];
        var entryIds = await EntriesOf(nationalId, erasure.ArquebusierId).Select(e => e.Id).ToListAsync(cancellationToken);
        var lenderLoans = await LoansOf(nationalId, weapons).Select(l => new { l.Id, l.EntryId }).ToListAsync(cancellationToken);
        var loanEntryIds = lenderLoans.Select(l => l.EntryId).ToList();
        Guid[] orderIds = await db.Entries.Where(e => entryIds.Contains(e.Id) || loanEntryIds.Contains(e.Id))
            .Select(e => e.OrderId).Distinct().ToArrayAsync(cancellationToken);
        await db.Database.SqlQuery<Guid>(
            $"SELECT id AS \"Value\" FROM orders.comparsa_orders WHERE id = ANY({orderIds}) ORDER BY id FOR UPDATE").ToListAsync(cancellationToken);
        erasure.AddEntries(entryIds);
        erasure.AddLoans(lenderLoans.Select(l => l.Id));
    }

    public async Task EraseAsync(
        PersonalDataSubject subject, PersonalDataErasure erasure, DbTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(erasure);
        if (subject is not PersonalDataSubject.Person(var nationalId))
        {
            return;
        }

        await db.EnlistAsync(transaction, cancellationToken);
        var now = time.GetUtcNow();
        // The orders are locked now: what Prepare found, plus anything added with the person's DNI/NIE
        // between its read and its lock (the registry links are gone by now, so the copy finds them).
        // Added to the erasure too, so the participants after this one (the proxies) see them.
        Guid[] weapons = [.. erasure.OwnedWeaponIds];
        erasure.AddEntries(await EntriesOf(nationalId, null).Select(e => e.Id).ToListAsync(cancellationToken));
        erasure.AddLoans(await LoansOf(nationalId, weapons).Select(l => l.Id).ToListAsync(cancellationToken));
        Guid[] entryIds = [.. erasure.EntryIds];
        Guid[] loanIds = [.. erasure.LoanIds];

        // The entry of the edition in progress may be gone already (the registry deletion removes it
        // while the orders are open); the others are anonymised. UPDATE ... RETURNING reports only the
        // rows this erasure changed, so a second erasure of the same person counts nothing.
        var entryOrders = await db.Database.SqlQuery<Guid>(
            $"""
            UPDATE orders.edition_entries
            SET arquebusier_id = NULL, first_name = NULL, last_name = NULL, national_id = NULL, federation_id = NULL,
                owned_weapon_id = NULL, owned_weapon_number = NULL, owned_weapon_guide_number = NULL, erased_at = {now}
            WHERE id = ANY({entryIds}) AND erased_at IS NULL
            RETURNING order_id AS "Value"
            """).ToListAsync(cancellationToken);
        var loanEntries = await db.Database.SqlQuery<Guid>(
            $"""
            UPDATE orders.weapon_loans
            SET lender_owned_weapon_id = NULL, lender_first_name = NULL, lender_last_name = NULL, lender_national_id = NULL,
                weapon_number = NULL, ownership_guide_number = NULL, erased_at = {now}
            WHERE id = ANY({loanIds}) AND erased_at IS NULL
            RETURNING entry_id AS "Value"
            """).ToListAsync(cancellationToken);

        // A new version for the orders touched, so a submission based on what they were is refused.
        Guid[] touched = [.. entryOrders.Distinct()];
        Guid[] borrowers = [.. loanEntries];
        await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE orders.comparsa_orders SET updated_at = {now}
            WHERE id = ANY({touched}) OR id IN (SELECT order_id FROM orders.edition_entries WHERE id = ANY({borrowers}))
            """,
            cancellationToken);
        var entries = entryOrders.Count;
        var loans = loanEntries.Count;
        erasure.Count("entriesAnonymised", entries);
        erasure.Count("loansAnonymised", loans);
    }

    /// <summary>The registered arquebusier with this DNI/NIE and their owned weapons, so loans of them are found after a DNI correction.</summary>
    private async Task<(Guid? ArquebusierId, Guid[] Weapons)> RegisteredAsync(string nationalId, CancellationToken cancellationToken)
    {
        var lender = await roster.FindLenderAsync(nationalId, cancellationToken);
        return (lender?.ArquebusierId, lender is null ? [] : [.. lender.Weapons.Select(w => w.Id)]);
    }

    private IQueryable<EditionEntry> EntriesOf(string nationalId, Guid? arquebusierId) =>
        db.Entries.AsNoTracking().Where(e => e.NationalId == nationalId || (arquebusierId != null && e.ArquebusierId == arquebusierId));

    private IQueryable<WeaponLoan> LoansOf(string nationalId, Guid[] ownedWeaponIds) =>
        db.Loans.AsNoTracking().Where(l => l.LenderNationalId == nationalId
            || (l.LenderOwnedWeaponId != null && ownedWeaponIds.Contains(l.LenderOwnedWeaponId.Value)));
}
