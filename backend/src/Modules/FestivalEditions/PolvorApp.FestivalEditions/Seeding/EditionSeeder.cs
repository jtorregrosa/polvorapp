using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.FestivalEditions.Editions;
using PolvorApp.FestivalEditions.Persistence;
using PolvorApp.SharedKernel.Hosting;
using PolvorApp.SharedKernel.Seeding;
using PolvorApp.SharedKernel.Time;

namespace PolvorApp.FestivalEditions.Seeding;

/// <summary>
/// Synthetic festival editions for development, staging and E2E tests (spec: Synthetic edition data,
/// SEC-11, design D11): a closed past edition, the current edition in progress with its orders open,
/// and a draft for the following year. Dates are relative to the seed date, so the current edition's
/// order window includes it on a freshly seeded database; existing rows are never refreshed. Prices
/// and titles are invented. Fixed identifiers; it skips what exists, a year another edition uses, and
/// a second edition in progress, so it can run again. It writes no audit entries (not a user action).
/// The weapon models come from the catalogue seeder (order 20), whose ids are repeated here.
/// </summary>
internal sealed partial class EditionSeeder(
    FestivalEditionsDbContext db,
    ICatalogDirectory catalog,
    TimeProvider time,
    IHostEnvironment environment,
    ILogger<EditionSeeder> logger) : IDataSeeder
{
    public static readonly Guid PastEdition = new("0193a500-0000-7000-8000-000000000001");
    public static readonly Guid CurrentEdition = new("0193a500-0000-7000-8000-000000000002");
    public static readonly Guid DraftEdition = new("0193a500-0000-7000-8000-000000000003");

    /// <summary>The catalogue seeder's trabucos and active arcabuces (numbers 1, 2, 5, 6, 7).</summary>
    private static readonly IReadOnlyList<Guid> RentableModels =
        [.. new[] { 1, 2, 5, 6, 7 }.Select(number => new Guid($"0193a200-0000-7000-8000-{number:D12}"))];

    /// <summary>Synthetic prices: invented amounts, not the Federation's.</summary>
    private static readonly EditionPrices Prices = new(48.00m, 3.75m, 25.00m, 5.00m);

    /// <summary>Days from the seed date and title of the current edition's milestones.</summary>
    private static readonly IReadOnlyList<(int Days, string Title)> CurrentMilestones =
    [
        (-30, "Convocatoria sintética de licencias"),
        (10, "Curso sintético de formación"),
        (20, "Plazo sintético de nuevos arcabuceros"),
        (50, "Reparto sintético de pólvora"),
    ];

    /// <summary>After the catalogue (20) and the registry (30): editions offer catalogue models.</summary>
    public int Order => 40;

    public static IReadOnlyList<Guid> EditionIds { get; } = [PastEdition, CurrentEdition, DraftEdition];

    /// <summary>
    /// The current edition's milestone reminded by email (add-notifications): within 7 days of the seed date, so
    /// <c>send-notifications</c> sends its reminder; the other milestones have <c>notify</c> off.
    /// </summary>
    public static (int Days, string Title) ReminderMilestone { get; } = (5, "Entrega sintética de documentación");

    public static Guid ReminderMilestoneId => MilestoneId(CurrentMilestones.Count + 2);

    public static int MilestoneCount => CurrentMilestones.Count + 2;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await RefuseRealDataOutsideLocalAsync(cancellationToken);
        var now = time.GetUtcNow();
        var today = FederationCalendar.Today(time);
        var current = Current(today, now);
        var editions = new[] { Past(current.Year - 1, now), current, Draft(current.Year + 1, now) };

        var added = 0;
        foreach (var edition in editions)
        {
            if (await CanAddAsync(edition, cancellationToken))
            {
                db.Editions.Add(edition);
                added++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        var models = await AddModelsAsync(cancellationToken);
        var milestones = await AddMilestonesAsync(today, current.Year, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        LogSeeded(logger, added, models, milestones);
    }

    /// <summary>The current edition: orders opened two weeks ago, the festival in two months.</summary>
    private static FestivalEdition Current(DateOnly today, DateTimeOffset now)
    {
        var starts = today.AddDays(60);

        // The festival stays inside its year even when the seed date is late in December.
        var ends = starts.AddDays(3).Year == starts.Year ? starts.AddDays(3) : new DateOnly(starts.Year, 12, 31);
        return WithPrices(new FestivalEdition
        {
            Id = CurrentEdition,
            Year = starts.Year,
            FestivalStartsOn = starts,
            FestivalEndsOn = ends,
            OrdersOpenOn = today.AddDays(-14),
            OrdersCloseOn = today.AddDays(30),
            Status = EditionStatus.InProgress,
            OrdersOpen = true,
            CreatedAt = now,
            StatusChangedAt = now,
        });
    }

    private static FestivalEdition Past(int year, DateTimeOffset now) => WithPrices(new FestivalEdition
    {
        Id = PastEdition,
        Year = year,
        FestivalStartsOn = new DateOnly(year, 4, 22),
        FestivalEndsOn = new DateOnly(year, 4, 25),
        OrdersOpenOn = new DateOnly(year, 1, 10),
        OrdersCloseOn = new DateOnly(year, 2, 10),
        Status = EditionStatus.Closed,
        CreatedAt = now,
        StatusChangedAt = now,
    });

    /// <summary>Next year's draft, as a new edition starts: the prices copied, no dates or milestones yet.</summary>
    private static FestivalEdition Draft(int year, DateTimeOffset now) => WithPrices(new FestivalEdition
    {
        Id = DraftEdition,
        Year = year,
        FestivalStartsOn = new DateOnly(year, 4, 22),
        FestivalEndsOn = new DateOnly(year, 4, 25),
        CreatedAt = now,
    });

    private static FestivalEdition WithPrices(FestivalEdition edition)
    {
        (edition.PowderPerKg, edition.CapsBox, edition.WeaponRental, edition.FlaskRental) =
            (Prices.PowderPerKg, Prices.CapsBox, Prices.WeaponRental, Prices.FlaskRental);
        return edition;
    }

    private async Task<bool> CanAddAsync(FestivalEdition edition, CancellationToken cancellationToken)
    {
        if (await db.Editions.AnyAsync(e => e.Id == edition.Id, cancellationToken))
        {
            return false;
        }

        var clash = await db.Editions.AnyAsync(
            e => e.Year == edition.Year || (edition.Status == EditionStatus.InProgress && e.Status == EditionStatus.InProgress),
            cancellationToken);
        if (clash)
        {
            LogSkipped(logger, "edition", edition.Id);
            return false;
        }

        return true;
    }

    /// <summary>Every seeded edition offers the seeded rental models that exist and are still rentable.</summary>
    private async Task<int> AddModelsAsync(CancellationToken cancellationToken)
    {
        var rentable = (await catalog.FindWeaponModelsAsync(RentableModels, cancellationToken))
            .Where(m => m.Active && m.Rentable)
            .Select(m => m.Id)
            .ToList();
        if (rentable.Count < RentableModels.Count)
        {
            LogSkipped(logger, "weapon model set", CurrentEdition);
        }

        var editions = await db.Editions.Where(e => EditionIds.Contains(e.Id)).Select(e => e.Id).ToListAsync(cancellationToken);
        var existing = await db.EditionWeaponModels.Where(m => editions.Contains(m.EditionId)).ToListAsync(cancellationToken);
        var added = 0;
        foreach (var edition in editions)
        {
            foreach (var model in rentable.Where(model => !existing.Any(e => e.EditionId == edition && e.WeaponModelId == model)))
            {
                db.EditionWeaponModels.Add(new EditionWeaponModel { EditionId = edition, WeaponModelId = model });
                added++;
            }
        }

        return added;
    }

    private async Task<int> AddMilestonesAsync(DateOnly today, int currentYear, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var wanted = CurrentMilestones
            .Select((m, i) => (Id: MilestoneId(i + 1), Edition: CurrentEdition, Date: today.AddDays(m.Days), m.Title))
            .Append((Id: MilestoneId(CurrentMilestones.Count + 1), Edition: PastEdition, Date: new DateOnly(currentYear - 1, 4, 30), Title: "Balance sintético de la edición"))
            .Append((Id: ReminderMilestoneId, Edition: CurrentEdition, Date: today.AddDays(ReminderMilestone.Days), ReminderMilestone.Title))
            .ToList();
        var editions = await db.Editions.Where(e => EditionIds.Contains(e.Id)).Select(e => e.Id).ToListAsync(cancellationToken);
        var ids = wanted.Select(m => m.Id).ToList();
        var existing = await db.Milestones.Where(m => ids.Contains(m.Id)).Select(m => m.Id).ToListAsync(cancellationToken);
        var added = 0;
        foreach (var milestone in wanted.Where(m => !existing.Contains(m.Id) && editions.Contains(m.Edition)))
        {
            db.Milestones.Add(new CalendarMilestone
            {
                Id = milestone.Id,
                EditionId = milestone.Edition,
                Date = milestone.Date,
                Title = milestone.Title,
                Notify = milestone.Id == ReminderMilestoneId,
                CreatedAt = now,
            });
            added++;
        }

        return added;
    }

    private static Guid MilestoneId(int number) => new($"0193a510-0000-7000-8000-{number:D12}");

    private async Task RefuseRealDataOutsideLocalAsync(CancellationToken cancellationToken)
    {
        if (LocalEnvironments.IsLocal(environment))
        {
            return;
        }

        var milestoneIds = Enumerable.Range(1, MilestoneCount).Select(MilestoneId).ToList();
        if (await db.Editions.AnyAsync(e => !EditionIds.Contains(e.Id), cancellationToken)
            || await db.Milestones.AnyAsync(m => !milestoneIds.Contains(m.Id), cancellationToken))
        {
            throw new InvalidOperationException("The database holds editions that are not synthetic (rows this seeder did not create); refusing to seed it (NFR-13).");
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Synthetic editions ensured: {Editions} editions, {Models} rental models and {Milestones} milestones added")]
    private static partial void LogSeeded(ILogger logger, int editions, int models, int milestones);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Synthetic {Kind} {Id} skipped: its year, the edition in progress or its catalogue models are taken or missing")]
    private static partial void LogSkipped(ILogger logger, string kind, Guid id);
}
