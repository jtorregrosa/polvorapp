using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.Distribution.Contracts;
using PolvorApp.Distribution.Days;
using PolvorApp.Distribution.Documents;
using PolvorApp.Distribution.Handovers;
using PolvorApp.Distribution.Persistence;
using PolvorApp.Distribution.Proxies;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.SharedKernel.Hosting;
using PolvorApp.SharedKernel.Seeding;

namespace PolvorApp.Distribution.Seeding;

/// <summary>
/// Synthetic distribution data for development, staging and E2E tests (spec: Synthetic distribution
/// data; SEC-11): in the current edition, a powder day and a weapons day shortly before the festival,
/// with slots for Norte (09:00) and Sur (09:30) and none for Este; a powder proxy in Norte's order and a
/// weapons proxy in Sur's, whose proxies hold a license valid on the day (the seeded current edition has
/// no reserve with one, so both proxies are active entries). In the past edition, a powder day with the
/// handovers of its first holders, with invented flask numbers (add-offline-distribution-capture D9),
/// so lists and exports show recorded values. The edition, comparsas and entries come from
/// the earlier seeders, whose ids are repeated here. Fixed identifiers; a row that exists is left
/// untouched and one whose edition, comparsa or entry is missing is skipped with a warning, so it can run
/// again. It writes no audit entries.
/// </summary>
internal sealed partial class DistributionSeeder(
    DistributionDbContext db,
    IEditionDirectory editions,
    ICatalogDirectory catalog,
    IEditionEntries entries,
    DistributionListReader lists,
    TimeProvider time,
    IHostEnvironment environment,
    ILogger<DistributionSeeder> logger) : IDataSeeder
{
    private static readonly Guid PastEdition = new("0193a500-0000-7000-8000-000000000001");
    private static readonly Guid CurrentEdition = new("0193a500-0000-7000-8000-000000000002");
    private static readonly Guid PastPowderDay = new("0193a800-0000-7000-8000-000000000003");
    private static readonly Guid Norte = SyntheticComparsas.ByNumber(1).Id;
    private static readonly Guid Sur = SyntheticComparsas.ByNumber(2).Id;

    private static readonly IReadOnlyList<DaySeed> Days =
    [
        new(1, DistributionType.Weapons, DaysBeforeFestival: 10, "Almacén de la Federación"),
        new(2, DistributionType.Powder, DaysBeforeFestival: 4, "Paraje del Reparto"),
    ];

    private static readonly IReadOnlyList<(Guid Comparsa, TimeOnly StartsAt)> Slots = [(Norte, new TimeOnly(9, 0)), (Sur, new TimeOnly(9, 30))];

    /// <summary>The order seeder's entries: Norte's 2 kg holder (10) with a powder carrier (9); Sur's rental (14) with a shooter (15).</summary>
    private static readonly IReadOnlyList<ProxySeed> Proxies =
    [
        new(1, DistributionType.Powder, Norte, HolderEntry: 10, ProxyEntry: 9),
        new(2, DistributionType.Weapons, Sur, HolderEntry: 14, ProxyEntry: 15),
    ];

    public static int ProxyCount => Proxies.Count;

    /// <summary>The past powder day's handovers: its first holders by number.</summary>
    public const int PastHandoverCount = 3;

    /// <summary>After the orders (50): days name comparsas and proxies name entries.</summary>
    public int Order => 60;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await RefuseRealDataOutsideLocalAsync(cancellationToken);
        if (await editions.FindAsync(CurrentEdition, cancellationToken) is not { } edition)
        {
            LogSkipped(logger, "the current edition is missing", CurrentEdition);
            return;
        }

        var now = time.GetUtcNow();
        var days = await AddDaysAsync(edition, now, cancellationToken);
        var proxies = await AddProxiesAsync(now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        var handovers = await AddPastHandoversAsync(now, cancellationToken);
        LogSeeded(logger, days, proxies, handovers);
    }

    /// <summary>The past edition's powder day and the handovers of its first holders, as a sync would have recorded them.</summary>
    private async Task<int> AddPastHandoversAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await editions.FindAsync(PastEdition, cancellationToken) is not { } past)
        {
            LogSkipped(logger, "the past edition is missing", PastEdition);
            return 0;
        }

        var day = await db.Days.AsNoTracking().Include(d => d.Slots).SingleOrDefaultAsync(d => d.EditionId == past.Id && d.Type == DistributionType.Powder, cancellationToken);
        if (day is null)
        {
            day = new DistributionDay
            {
                Id = PastPowderDay,
                EditionId = past.Id,
                Type = DistributionType.Powder,
                Date = past.FestivalStartsOn.AddDays(-4),
                Location = "Paraje del Reparto",
                UpdatedAt = now,
            };
            db.Days.Add(day);
            await db.SaveChangesAsync(cancellationToken);
        }

        // Once only: a later run, e.g. the full dataset after the scenarios, keeps the day's record as it is.
        if (await db.Handovers.AnyAsync(h => h.DistributionId == day.Id, cancellationToken))
        {
            return 0;
        }

        var rows = DistributionLists.Rows(await lists.ReadAsync(day, past, cancellationToken), DistributionTexts.Spanish).Rows.Take(PastHandoverCount).ToList();
        var added = 0;
        foreach (var (row, index) in rows.Select((row, index) => (row, index + 1)))
        {
            var collectedAt = new DateTimeOffset(day.Date, new TimeOnly(9, 0), TimeSpan.Zero).AddMinutes(index * 3);
            db.Handovers.Add(new Handover
            {
                Id = new($"0193a820-0000-7000-8000-{index:D12}"),
                DistributionId = day.Id,
                HolderEntryId = row.Holder.EntryId,
                DistributionNumber = row.Number,
                CollectedBy = HandoverCollector.Holder,
                PowderKg = (short)row.Entry.PowderKg,
                RentalFlaskNumber = row.Entry.Flask is FlaskOption.Rental1Kg or FlaskOption.Rental2Kg ? $"P-{index:D3}" : null,
                Traceability1 = $"T-{index:D2}",
                CollectedAt = collectedAt,
                RecordedAt = collectedAt.AddHours(6),
            });
            added++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return added;
    }

    private async Task<int> AddDaysAsync(EditionSnapshot edition, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await db.Days.AsNoTracking().Where(d => d.EditionId == edition.Id).Select(d => new { d.Id, d.Type }).ToListAsync(cancellationToken);
        var comparsas = (await catalog.FindComparsasAsync([.. Slots.Select(s => s.Comparsa)], cancellationToken)).Select(c => c.Id).ToHashSet();
        var added = 0;
        foreach (var seed in Days.Where(d => existing.All(e => e.Id != d.Id && e.Type != d.Type)))
        {
            // Inside the edition's year and not after the festival (spec: Distribution days).
            var date = edition.FestivalStartsOn.AddDays(-seed.DaysBeforeFestival);
            var firstDay = new DateOnly(edition.Year, 1, 1);
            var day = new DistributionDay
            {
                Id = seed.Id,
                EditionId = edition.Id,
                Type = seed.Type,
                Date = date < firstDay ? firstDay : date,
                Location = seed.Location,
                UpdatedAt = now,
            };
            day.Slots.AddRange(Slots.Where(s => comparsas.Contains(s.Comparsa))
                .Select(s => new DistributionSlot { DistributionId = day.Id, ComparsaId = s.Comparsa, StartsAt = s.StartsAt }));
            db.Days.Add(day);
            added++;
        }

        return added;
    }

    private async Task<int> AddProxiesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        Guid[] ids = [.. Proxies.Select(p => p.Id)];
        var existing = await db.Proxies.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => p.Id).ToListAsync(cancellationToken);
        var found = (await entries.FindManyAsync([.. Proxies.SelectMany(p => new[] { EntryId(p.HolderEntry), EntryId(p.ProxyEntry) })], cancellationToken))
            .ToDictionary(e => e.EntryId);
        var added = 0;
        foreach (var seed in Proxies.Where(p => !existing.Contains(p.Id)))
        {
            if (!found.TryGetValue(EntryId(seed.HolderEntry), out var holder) || !found.ContainsKey(EntryId(seed.ProxyEntry))
                || holder.EditionId != CurrentEdition || holder.ComparsaId != seed.Comparsa
                || await db.Proxies.AnyAsync(p => p.HolderEntryId == holder.EntryId && p.Type == seed.Type, cancellationToken))
            {
                LogSkipped(logger, "an entry is missing or the holder already has a proxy", seed.Id);
                continue;
            }

            db.Proxies.Add(new PickupProxy
            {
                Id = seed.Id,
                EditionId = CurrentEdition,
                ComparsaId = seed.Comparsa,
                Type = seed.Type,
                HolderEntryId = EntryId(seed.HolderEntry),
                ProxyEntryId = EntryId(seed.ProxyEntry),
                CreatedAt = now,
            });
            added++;
        }

        return added;
    }

    /// <summary>Outside a local environment the seeder refuses to run on a database with distribution data it did not write (SEC-11).</summary>
    private async Task RefuseRealDataOutsideLocalAsync(CancellationToken cancellationToken)
    {
        if (LocalEnvironments.IsLocal(environment))
        {
            return;
        }

        Guid[] dayIds = [.. Days.Select(d => d.Id), PastPowderDay];
        Guid[] proxyIds = [.. Proxies.Select(p => p.Id)];
        if (await db.Days.AnyAsync(d => !dayIds.Contains(d.Id), cancellationToken)
            || await db.Proxies.AnyAsync(p => !proxyIds.Contains(p.Id), cancellationToken)
            || await db.Handovers.AnyAsync(h => h.DistributionId != PastPowderDay, cancellationToken))
        {
            throw new InvalidOperationException("The database holds distribution data that is not synthetic: the seed refuses to run.");
        }
    }

    /// <summary>The order seeder's entry ids.</summary>
    private static Guid EntryId(int number) => new($"0193a710-0000-7000-8000-{number:D12}");

    [LoggerMessage(Level = LogLevel.Information, Message = "Distribution seed: {Days} days, {Proxies} proxies and {Handovers} handovers added")]
    private static partial void LogSeeded(ILogger logger, int days, int proxies, int handovers);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Distribution seed skipped {Id}: {Reason}")]
    private static partial void LogSkipped(ILogger logger, string reason, Guid id);

    private sealed record DaySeed(int Number, DistributionType Type, int DaysBeforeFestival, string Location)
    {
        public Guid Id => new($"0193a800-0000-7000-8000-{Number:D12}");
    }

    private sealed record ProxySeed(int Number, DistributionType Type, Guid Comparsa, int HolderEntry, int ProxyEntry)
    {
        public Guid Id => new($"0193a810-0000-7000-8000-{Number:D12}");
    }
}
