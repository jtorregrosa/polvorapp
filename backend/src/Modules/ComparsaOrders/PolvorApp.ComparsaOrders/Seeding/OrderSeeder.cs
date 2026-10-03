using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComparsaOrders.Loans;
using PolvorApp.ComparsaOrders.Orders;
using PolvorApp.ComparsaOrders.Persistence;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.SharedKernel.Hosting;
using PolvorApp.SharedKernel.Seeding;

namespace PolvorApp.ComparsaOrders.Seeding;

/// <summary>
/// Synthetic comparsa orders for development, staging and E2E tests (SEC-11, design D13):
/// <list type="bullet">
/// <item>the closed past edition has <c>VALIDATED</c> orders of Norte and Sur, so the first-year flag is
/// known in the current edition, and some arquebusiers have no earlier <c>ACTIVE</c> entry;</item>
/// <item>the current edition has Norte <c>SUBMITTED</c> and Sur <c>DRAFT</c> (with one arquebusier not in
/// it); Este is not prepared;</item>
/// <item>every weapon source, flask and caps type, <c>RESERVE</c> entries, an <c>ACTIVE</c> entry without
/// powder and one without a weapon;</item>
/// <item>a loan of a Sur arquebusier's weapon to a Norte entry, an external owner's loan with a synthetic
/// DNI that passes BR-01, and a past entry no longer linked to the registry, with a synthetic copy.</item>
/// </list>
/// The editions, comparsas, arquebusiers, weapons, models and users come from the earlier seeders, whose
/// ids are repeated here; the identity and weapon copies are read from the registry. Fixed identifiers;
/// a row that exists is left untouched, and one whose edition, comparsa, arquebusier, weapon, model or
/// unique slot is missing or taken is skipped with a warning, so it can run again. It writes no audit
/// entries.
/// </summary>
internal sealed partial class OrderSeeder(
    ComparsaOrdersDbContext db,
    IEditionDirectory editions,
    IArquebusierRoster roster,
    ICatalogDirectory catalog,
    TimeProvider time,
    IHostEnvironment environment,
    ILogger<OrderSeeder> logger) : IDataSeeder
{
    public static readonly Guid PastNorteOrder = OrderId(1);
    public static readonly Guid PastSurOrder = OrderId(2);
    public static readonly Guid CurrentNorteOrder = OrderId(3);
    public static readonly Guid CurrentSurOrder = OrderId(4);

    private static readonly Guid PastEdition = new("0193a500-0000-7000-8000-000000000001");
    private static readonly Guid CurrentEdition = new("0193a500-0000-7000-8000-000000000002");
    private static readonly Guid Norte = new("0193a100-0000-7000-8000-000000000001");
    private static readonly Guid Sur = new("0193a100-0000-7000-8000-000000000002");
    private static readonly Guid Admin = new("0193a000-0000-7000-8000-000000000001");
    private static readonly Guid JefeUno = new("0193a000-0000-7000-8000-000000000002");
    private static readonly Guid JefaDos = new("0193a000-0000-7000-8000-000000000003");

    private static readonly IReadOnlyList<OrderSeed> Orders =
    [
        new(1, PastEdition, Norte, OrderStatus.Validated, JefeUno, DaysAgo: 380, SubmittedBy: JefeUno, ReviewedBy: Admin),
        new(2, PastEdition, Sur, OrderStatus.Validated, JefeUno, DaysAgo: 380, SubmittedBy: JefeUno, ReviewedBy: Admin),
        new(3, CurrentEdition, Norte, OrderStatus.Submitted, JefaDos, DaysAgo: 10, SubmittedBy: JefaDos),
        new(4, CurrentEdition, Sur, OrderStatus.Draft, JefeUno, DaysAgo: 5),
    ];

    /// <summary>Arquebusier numbers are the registry seeder's; owned weapon and model numbers are the registry's and the catalogue's.</summary>
    private static readonly IReadOnlyList<EntrySeed> Entries =
    [
        // Past edition, Norte. Arquebusiers 3, 5 and 14 have no ACTIVE entry before the current edition.
        new(1, Order: 1, Arquebusier: 1, ArquebusierStatus.Active, 2, 2, CapsType.Normal, WeaponSource.Owned, FlaskOption.Owned, OwnedWeapon: 1),
        new(2, Order: 1, Arquebusier: 2, ArquebusierStatus.Active, 1, 1, CapsType.Small, WeaponSource.Rental, FlaskOption.Rental1Kg, RentalModel: 2),
        new(3, Order: 1, Arquebusier: 4, ArquebusierStatus.Reserve, 0, 0, null, WeaponSource.None, FlaskOption.None),

        // History of an arquebusier no longer in the registry: the synthetic copy only.
        new(4, Order: 1, Arquebusier: null, ArquebusierStatus.Active, 2, 0, null, WeaponSource.Rental, FlaskOption.Rental2Kg, RentalModel: 1),

        // Past edition, Sur.
        new(5, Order: 2, Arquebusier: 6, ArquebusierStatus.Active, 2, 2, CapsType.Normal, WeaponSource.Rental, FlaskOption.Rental2Kg, RentalModel: 5),
        new(6, Order: 2, Arquebusier: 7, ArquebusierStatus.Active, 1, 0, null, WeaponSource.Owned, FlaskOption.Owned, OwnedWeapon: 4),
        new(7, Order: 2, Arquebusier: 8, ArquebusierStatus.Reserve, 0, 0, null, WeaponSource.None, FlaskOption.None),

        // Current edition, Norte (submitted): a shooter without powder, a powder carrier without a weapon and two loans.
        new(8, Order: 3, Arquebusier: 1, ArquebusierStatus.Active, 2, 3, CapsType.Normal, WeaponSource.Owned, FlaskOption.Owned, OwnedWeapon: 1),
        new(9, Order: 3, Arquebusier: 2, ArquebusierStatus.Active, 0, 2, CapsType.Small, WeaponSource.Owned, FlaskOption.None, OwnedWeapon: 2),
        new(10, Order: 3, Arquebusier: 3, ArquebusierStatus.Active, 2, 0, null, WeaponSource.None, FlaskOption.Rental2Kg),
        new(11, Order: 3, Arquebusier: 4, ArquebusierStatus.Reserve, 0, 0, null, WeaponSource.None, FlaskOption.None),
        new(12, Order: 3, Arquebusier: 5, ArquebusierStatus.Active, 1, 1, CapsType.Normal, WeaponSource.Loan, FlaskOption.Rental1Kg),
        new(13, Order: 3, Arquebusier: 14, ArquebusierStatus.Active, 1, 0, null, WeaponSource.Loan, FlaskOption.None),

        // Current edition, Sur (draft). Arquebusier 9 is not in the order yet.
        new(14, Order: 4, Arquebusier: 6, ArquebusierStatus.Active, 2, 2, CapsType.Normal, WeaponSource.Rental, FlaskOption.Rental2Kg, RentalModel: 5),
        new(15, Order: 4, Arquebusier: 7, ArquebusierStatus.Active, 1, 0, null, WeaponSource.Owned, FlaskOption.Owned, OwnedWeapon: 4),
        new(16, Order: 4, Arquebusier: 8, ArquebusierStatus.Reserve, 0, 0, null, WeaponSource.None, FlaskOption.None),
    ];

    /// <summary>The registered loan: Sur arquebusier 6's owned weapon 3, to entry 12.</summary>
    private const int RegisteredLoanEntry = 12;
    private const int LentWeapon = 3;

    /// <summary>The external owner's loan, to entry 13, of the catalogue's model 1 (a trabuco).</summary>
    private const int ExternalLoanEntry = 13;
    private const int ExternalLoanModel = 1;

    private enum Loan
    {
        Registered = 1,
        External = 2,
    }

    public static int OrderCount => Orders.Count;

    public static int EntryCount => Entries.Count;

    public static int LoanCount => Enum.GetValues<Loan>().Length;

    /// <summary>The external owner's synthetic DNI: a very low number, unlikely to be in use, with the BR-01 check letter.</summary>
    public static string ExternalOwnerNationalId => SyntheticNationalId(92);

    /// <summary>After the editions (40): orders belong to editions and copy registry data.</summary>
    public int Order => 50;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await RefuseRealDataOutsideLocalAsync(cancellationToken);
        var now = time.GetUtcNow();
        var orders = await AddOrdersAsync(now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        var (entries, loans) = await AddEntriesAsync(now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        LogSeeded(logger, orders, entries, loans);
    }

    private async Task<int> AddOrdersAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await db.Orders.AsNoTracking().Select(o => new { o.Id, o.EditionId, o.ComparsaId }).ToListAsync(cancellationToken);
        var comparsas = (await catalog.FindComparsasAsync([.. Orders.Select(o => o.ComparsaId).Distinct()], cancellationToken))
            .Select(c => c.Id)
            .ToHashSet();
        var years = new Dictionary<Guid, int>();
        foreach (var editionId in Orders.Select(o => o.EditionId).Distinct())
        {
            if (await editions.FindAsync(editionId, cancellationToken) is { } edition)
            {
                years[editionId] = edition.Year;
            }
        }

        var added = 0;
        foreach (var seed in Orders.Where(o => existing.All(e => e.Id != o.Id)))
        {
            if (!years.TryGetValue(seed.EditionId, out var year) || !comparsas.Contains(seed.ComparsaId) || existing.Any(e => e.EditionId == seed.EditionId && e.ComparsaId == seed.ComparsaId))
            {
                LogSkipped(logger, "order", seed.Id);
                continue;
            }

            db.Orders.Add(seed.ToOrder(year, now));
            added++;
        }

        return added;
    }

    private async Task<(int Entries, int Loans)> AddEntriesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var orders = await db.Orders.AsNoTracking()
            .Where(o => Orders.Select(s => s.Id).Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, cancellationToken);
        var existing = await db.Entries.AsNoTracking()
            .Select(e => new { e.Id, e.EditionId, e.ArquebusierId })
            .ToListAsync(cancellationToken);
        var linked = (await roster.FindManyAsync([.. Entries.Select(e => e.Arquebusier).OfType<int>().Distinct().Select(ArquebusierId)], cancellationToken))
            .ToDictionary(a => a.Id);
        var models = (await catalog.FindWeaponModelsAsync(
                [.. Entries.Select(e => e.RentalModel).OfType<int>().Append(ExternalLoanModel).Distinct().Select(ModelId)], cancellationToken))
            .Select(m => m.Id)
            .ToHashSet();
        var entries = 0;
        var loans = 0;
        foreach (var seed in Entries.Where(e => existing.All(x => x.Id != e.Id)))
        {
            var arquebusier = seed.Arquebusier is { } number ? linked.GetValueOrDefault(ArquebusierId(number)) : null;
            if (!orders.TryGetValue(OrderId(seed.Order), out var order)
                || (seed.Arquebusier is not null && arquebusier is null)
                || (seed.RentalModel is { } model && !models.Contains(ModelId(model)))
                || (arquebusier is not null && existing.Any(x => x.EditionId == order.EditionId && x.ArquebusierId == arquebusier.Id)))
            {
                LogSkipped(logger, "entry", seed.Id);
                continue;
            }

            var entry = seed.ToEntry(order, arquebusier);
            if (arquebusier is not null && !EntryCopies.TryRefresh(entry, arquebusier, entry.CopiedAt))
            {
                LogSkipped(logger, "entry", seed.Id);
                continue;
            }

            db.Entries.Add(entry);
            entries++;
            if (await LoanForAsync(seed, entry, models, cancellationToken) is { } loan)
            {
                db.Loans.Add(loan);
                loans++;
            }
        }

        return (entries, loans);
    }

    /// <summary>The loan of a <c>LOAN</c> entry; the lender of the registered one is copied from the registry.</summary>
    private async Task<WeaponLoan?> LoanForAsync(EntrySeed seed, EditionEntry entry, HashSet<Guid> models, CancellationToken cancellationToken)
    {
        switch (seed.Id)
        {
            case var id when id == EntryId(ExternalLoanEntry) && !models.Contains(ModelId(ExternalLoanModel)):
                LogSkipped(logger, "loan", LoanId(Loan.External));
                entry.WeaponSource = WeaponSource.None;
                return null;
            case var id when id == EntryId(ExternalLoanEntry):
                return new WeaponLoan
                {
                    Id = LoanId(Loan.External),
                    EntryId = entry.Id,
                    LenderKind = LenderKind.External,
                    LenderFirstName = "Propietaria",
                    LenderLastName = "Externa Sintética",
                    LenderNationalId = ExternalOwnerNationalId,
                    WeaponModelId = ModelId(ExternalLoanModel),
                    WeaponNumber = "EXT-0001",
                    OwnershipGuideNumber = "SINT-EXT-0001",
                    CopiedAt = entry.CopiedAt,
                };
            case var id when id == EntryId(RegisteredLoanEntry):
                var weapon = (await roster.FindOwnedWeaponsAsync([OwnedWeaponId(LentWeapon)], cancellationToken)).SingleOrDefault();
                var lender = weapon is null ? null : (await roster.FindManyAsync([weapon.OwnerId], cancellationToken)).SingleOrDefault();
                if (weapon is null || lender is null)
                {
                    // A LOAN entry must not stay without its loan: drop the source too.
                    LogSkipped(logger, "loan", LoanId(Loan.Registered));
                    entry.WeaponSource = WeaponSource.None;
                    return null;
                }

                return new WeaponLoan
                {
                    Id = LoanId(Loan.Registered),
                    EntryId = entry.Id,
                    LenderKind = LenderKind.Arquebusier,
                    LenderOwnedWeaponId = weapon.Id,
                    LenderFirstName = lender.FirstName,
                    LenderLastName = lender.LastName,
                    LenderNationalId = lender.NationalId,
                    LenderComparsaId = lender.ComparsaId,
                    WeaponModelId = weapon.WeaponModelId,
                    WeaponNumber = weapon.WeaponNumber,
                    OwnershipGuideNumber = weapon.OwnershipGuideNumber,
                    CopiedAt = entry.CopiedAt,
                };
            default:
                return null;
        }
    }

    /// <summary>
    /// Staging may be reachable and must only ever hold synthetic data (NFR-13): before writing anything,
    /// refuse a database with an order, entry or loan this seeder would not have created.
    /// </summary>
    private async Task RefuseRealDataOutsideLocalAsync(CancellationToken cancellationToken)
    {
        if (LocalEnvironments.IsLocal(environment))
        {
            return;
        }

        Guid[] orderIds = [.. Orders.Select(o => o.Id)];
        Guid[] entryIds = [.. Entries.Select(e => e.Id)];
        Guid[] loanIds = [.. Enum.GetValues<Loan>().Select(LoanId)];
        if (await db.Orders.AnyAsync(o => !orderIds.Contains(o.Id), cancellationToken)
            || await db.Entries.AnyAsync(e => !entryIds.Contains(e.Id), cancellationToken)
            || await db.Loans.AnyAsync(l => !loanIds.Contains(l.Id), cancellationToken))
        {
            throw new InvalidOperationException("The database holds orders that are not synthetic (rows this seeder did not create); refusing to seed it (NFR-13).");
        }
    }

    private static Guid OrderId(int number) => new($"0193a700-0000-7000-8000-{number:D12}");

    private static Guid EntryId(int number) => new($"0193a710-0000-7000-8000-{number:D12}");

    private static Guid LoanId(Loan loan) => new($"0193a720-0000-7000-8000-{(int)loan:D12}");

    private static Guid ArquebusierId(int number) => new($"0193a300-0000-7000-8000-{number:D12}");

    private static Guid OwnedWeaponId(int number) => new($"0193a400-0000-7000-8000-{number:D12}");

    private static Guid ModelId(int number) => new($"0193a200-0000-7000-8000-{number:D12}");

    /// <summary>A DNI from a very low number, with the check letter computed as BR-01 requires.</summary>
    private static string SyntheticNationalId(int number) => $"{number:D8}{"TRWAGMYFPDXBNJZSQVHLCKE"[number % 23]}";

    [LoggerMessage(Level = LogLevel.Information, Message = "Synthetic orders ensured: {Orders} orders, {Entries} entries and {Loans} loans added")]
    private static partial void LogSeeded(ILogger logger, int orders, int entries, int loans);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Synthetic {Kind} {Id} skipped: its edition, comparsa, order, arquebusier, weapon or model is missing, or its comparsa or arquebusier already has one")]
    private static partial void LogSkipped(ILogger logger, string kind, Guid id);

    /// <param name="Number">Last part of the fixed identifier.</param>
    /// <param name="EditionId">Seeded edition.</param>
    /// <param name="ComparsaId">Seeded comparsa.</param>
    /// <param name="Status">Order status.</param>
    /// <param name="PreparedBy">Seeded user who prepared it.</param>
    /// <param name="DaysAgo">When it was prepared, relative to the seed date.</param>
    /// <param name="SubmittedBy">Seeded FiringChief who submitted it with the attestation, if any.</param>
    /// <param name="ReviewedBy">Seeded Admin who validated it, if any.</param>
    private sealed record OrderSeed(
        int Number, Guid EditionId, Guid ComparsaId, OrderStatus Status, Guid PreparedBy, int DaysAgo, Guid? SubmittedBy = null, Guid? ReviewedBy = null)
    {
        public Guid Id => OrderId(Number);

        public ComparsaOrder ToOrder(int editionYear, DateTimeOffset now)
        {
            var prepared = now.AddDays(-DaysAgo);
            var submitted = SubmittedBy is null ? (DateTimeOffset?)null : prepared.AddDays(DaysAgo / 2);
            var reviewed = ReviewedBy is null ? (DateTimeOffset?)null : submitted?.AddDays(1);
            return new ComparsaOrder
            {
                Id = Id,
                EditionId = EditionId,
                EditionYear = editionYear,
                ComparsaId = ComparsaId,
                Status = Status,
                PreparedAt = prepared,
                PreparedByUserId = PreparedBy,
                SubmittedAt = submitted,
                SubmittedByUserId = SubmittedBy,
                Attested = SubmittedBy is not null,
                ReviewedAt = reviewed,
                ReviewedByUserId = ReviewedBy,
                UpdatedAt = reviewed ?? submitted ?? prepared,
            };
        }
    }

    /// <param name="Number">Last part of the fixed identifier.</param>
    /// <param name="Order">The order's number.</param>
    /// <param name="Arquebusier">The registry seeder's arquebusier number; null for history of someone no longer in the registry.</param>
    /// <param name="Status">Entry status.</param>
    /// <param name="PowderKg">Powder.</param>
    /// <param name="CapsBoxes">Caps boxes.</param>
    /// <param name="Caps">Caps type, with boxes.</param>
    /// <param name="Source">Weapon source.</param>
    /// <param name="Flask">Flask.</param>
    /// <param name="OwnedWeapon">The registry seeder's owned weapon number, for <c>OWNED</c>.</param>
    /// <param name="RentalModel">The catalogue seeder's model number, for <c>RENTAL</c>.</param>
    private sealed record EntrySeed(
        int Number,
        int Order,
        int? Arquebusier,
        ArquebusierStatus Status,
        int PowderKg,
        int CapsBoxes,
        CapsType? Caps,
        WeaponSource Source,
        FlaskOption Flask,
        int? OwnedWeapon = null,
        int? RentalModel = null)
    {
        public Guid Id => EntryId(Number);

        public EditionEntry ToEntry(ComparsaOrder order, RosterArquebusier? arquebusier)
        {
            var entry = new EditionEntry
            {
                Id = Id,
                OrderId = order.Id,
                EditionId = order.EditionId,
                ArquebusierId = arquebusier?.Id,
                Status = Status,
                PowderKg = PowderKg,
                CapsBoxes = CapsBoxes,
                CapsType = Caps,
                WeaponSource = Source,
                OwnedWeaponId = OwnedWeapon is { } weapon ? OwnedWeaponId(weapon) : null,
                RentalWeaponModelId = RentalModel is { } model ? ModelId(model) : null,
                Flask = Flask,
                CreatedAt = order.PreparedAt,
                CopiedAt = order.PreparedAt,
            };
            if (arquebusier is null)
            {
                // A synthetic copy that no seeded arquebusier has.
                (entry.FirstName, entry.LastName, entry.NationalId, entry.FederationId) =
                    ("Arcabucero", "Sintético Histórico", SyntheticNationalId(91), 100_091);
            }

            return entry;
        }
    }
}
