using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PolvorApp.ArquebusierRegistry.Arquebusiers;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ArquebusierRegistry.OwnedWeapons;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.SharedKernel.Hosting;
using PolvorApp.SharedKernel.Seeding;
using PolvorApp.SharedKernel.Time;

namespace PolvorApp.ArquebusierRegistry.Seeding;

/// <summary>
/// Synthetic registry for development, staging and E2E tests (spec: Synthetic registry data, SEC-11,
/// design D9). Fictional arquebusiers in the seeded comparsas with valid synthetic DNI/NIE built from
/// very low numbers unlikely to be in use, covering both statuses, every license state (dates relative to the seed
/// date, so "valid" and "expired" stay true over time), no license, course done and not done, and owned
/// weapons of every kind. Fixed identifiers; existing rows are left untouched and a row whose comparsa,
/// model or unique value is missing or taken is skipped with a warning, so it can run again. It writes
/// no audit entries (it is not a user action). The comparsas and models come from the catalogue seeder
/// (order 20); this module cannot reference it, so their ids are repeated here and tests keep them equal.
/// The phones are in the Spanish mobile range (there is no reserved fictional range): PolvorApp never
/// calls or messages them.
/// </summary>
internal sealed partial class RegistrySeeder(
    ArquebusierRegistryDbContext db,
    ICatalogDirectory catalog,
    TimeProvider time,
    IHostEnvironment environment,
    ILogger<RegistrySeeder> logger) : IDataSeeder
{
    private static readonly Guid Norte = new("0193a100-0000-7000-8000-000000000001");
    private static readonly Guid Sur = new("0193a100-0000-7000-8000-000000000002");
    private static readonly Guid Este = new("0193a100-0000-7000-8000-000000000003");
    private static readonly Guid Oeste = new("0193a100-0000-7000-8000-000000000004");

    private static readonly IReadOnlyList<ArquebusierSeed> Arquebusiers =
    [
        new(1, Norte, "Arcabucero", "Sintético Uno", Gender.Male, ArquebusierStatus.Active, LicenseSeed.Valid, Course: true),
        new(2, Norte, "Arcabucera", "Sintética Dos", Gender.Female, ArquebusierStatus.Active, LicenseSeed.ValidProf, Course: true),
        new(3, Norte, "Arcabucero", "Sintético Tres", Gender.Unspecified, ArquebusierStatus.Active, LicenseSeed.Expired, Course: false),
        new(4, Norte, "Arcabucera", "Sintética Cuatro", Gender.Female, ArquebusierStatus.Reserve, LicenseSeed.None, Course: true),
        new(5, Norte, "Arcabucero", "Sintético Cinco", Gender.Male, ArquebusierStatus.Active, LicenseSeed.Pending, Course: false, Nie: true),
        new(6, Sur, "Arcabucera", "Sintética Seis", Gender.Female, ArquebusierStatus.Active, LicenseSeed.Valid, Course: true),
        new(7, Sur, "Arcabucero", "Sintético Siete", Gender.Male, ArquebusierStatus.Active, LicenseSeed.Valid, Course: true),
        new(8, Sur, "Arcabucera", "Sintética Ocho", Gender.Female, ArquebusierStatus.Reserve, LicenseSeed.Expired, Course: true),
        new(9, Sur, "Arcabucero", "Sintético Nueve", Gender.Male, ArquebusierStatus.Active, LicenseSeed.Pending, Course: false),
        new(10, Este, "Arcabucera", "Sintética Diez", Gender.Female, ArquebusierStatus.Active, LicenseSeed.Valid, Course: true, Nie: true),
        new(11, Este, "Arcabucero", "Sintético Once", Gender.Male, ArquebusierStatus.Active, LicenseSeed.None, Course: false),
        new(12, Este, "Arcabucera", "Sintética Doce", Gender.Unspecified, ArquebusierStatus.Reserve, LicenseSeed.Valid, Course: true),
        new(13, Oeste, "Arcabucero", "Sintético Trece", Gender.Male, ArquebusierStatus.Reserve, LicenseSeed.Expired, Course: true),
    ];

    /// <summary>Owner number, catalogue model number (see the catalogue seeder) and guide number.</summary>
    private static readonly IReadOnlyList<(int Owner, int Model, int Number)> OwnedWeapons =
    [
        (1, 1, 1),  // TRABUCO CRISTIANO DIESTRO
        (2, 9, 2),  // PISTOLA
        (6, 5, 3),  // ARCABUZ MORO DIESTRO
        (7, 6, 4),  // ARCABUZ MORO DIESTRO (PEQUEÑO)
        (10, 8, 5), // ARCABUZ MORO ZURDO (PEQUEÑO), deactivated: an existing weapon keeps its model
    ];

    private enum LicenseSeed
    {
        None,
        Pending,
        Valid,
        ValidProf,
        Expired,
    }

    public static int ArquebusierCount => Arquebusiers.Count;

    public static int OwnedWeaponCount => OwnedWeapons.Count;

    /// <summary>After the catalogue seeder (20): arquebusiers refer to its comparsas and models.</summary>
    public int Order => 30;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await RefuseRealDataOutsideLocalAsync(cancellationToken);
        var now = time.GetUtcNow();
        var today = FederationCalendar.Today(time);
        var added = await AddArquebusiersAsync(now, today, cancellationToken);
        var weapons = await AddOwnedWeaponsAsync(now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        LogSeeded(logger, added, weapons);
    }

    private async Task<int> AddArquebusiersAsync(DateTimeOffset now, DateOnly today, CancellationToken cancellationToken)
    {
        var existing = await db.Arquebusiers.AsNoTracking()
            .Select(a => new { a.Id, a.NationalId, a.FederationId })
            .ToListAsync(cancellationToken);
        var ids = existing.Select(a => a.Id).ToHashSet();
        var nationalIds = existing.Select(a => a.NationalId).ToHashSet(StringComparer.Ordinal);
        var federationIds = existing.Select(a => a.FederationId).ToHashSet();
        var comparsas = (await catalog.FindComparsasAsync([.. Arquebusiers.Select(a => a.ComparsaId).Distinct()], cancellationToken))
            .Select(c => c.Id)
            .ToHashSet();
        var added = 0;
        foreach (var seed in Arquebusiers.Where(a => !ids.Contains(a.Id)))
        {
            if (!comparsas.Contains(seed.ComparsaId) || !nationalIds.Add(seed.NationalId) || !federationIds.Add(seed.FederationId))
            {
                LogSkipped(logger, "arquebusier", seed.Id);
                continue;
            }

            db.Arquebusiers.Add(seed.ToArquebusier(now, today));
            added++;
        }

        return added;
    }

    private async Task<int> AddOwnedWeaponsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await db.OwnedWeapons.AsNoTracking().Select(w => new { w.Id, w.OwnershipGuideNumber }).ToListAsync(cancellationToken);
        var ids = existing.Select(w => w.Id).ToHashSet();
        var guides = existing.Select(w => w.OwnershipGuideNumber).ToHashSet(StringComparer.Ordinal);
        var owners = db.ChangeTracker.Entries<Arquebusier>().Select(e => e.Entity.Id)
            .Concat(await db.Arquebusiers.AsNoTracking().Select(a => a.Id).ToListAsync(cancellationToken))
            .ToHashSet();
        var models = (await catalog.FindWeaponModelsAsync([.. OwnedWeapons.Select(w => ModelId(w.Model)).Distinct()], cancellationToken))
            .Select(m => m.Id)
            .ToHashSet();
        var added = 0;
        foreach (var (owner, model, number) in OwnedWeapons)
        {
            var id = new Guid($"0193a400-0000-7000-8000-{number:D12}");
            var guide = $"SINT-{number:D4}";
            if (ids.Contains(id))
            {
                continue;
            }

            if (!owners.Contains(ArquebusierSeed.IdOf(owner)) || !models.Contains(ModelId(model)) || !guides.Add(guide))
            {
                LogSkipped(logger, "owned weapon", id);
                continue;
            }

            db.OwnedWeapons.Add(new OwnedWeapon
            {
                Id = id,
                ArquebusierId = ArquebusierSeed.IdOf(owner),
                WeaponModelId = ModelId(model),
                WeaponNumber = (1000 + number).ToString(CultureInfo.InvariantCulture),
                OwnershipGuideNumber = guide,
                CreatedAt = now,
            });
            added++;
        }

        return added;
    }

    private static Guid ModelId(int number) => new($"0193a200-0000-7000-8000-{number:D12}");

    /// <summary>
    /// Staging may be reachable and must only ever hold synthetic data (NFR-13): before writing anything,
    /// refuse a database with any arquebusier or owned weapon this seeder would not have created.
    /// </summary>
    private async Task RefuseRealDataOutsideLocalAsync(CancellationToken cancellationToken)
    {
        if (LocalEnvironments.IsLocal(environment))
        {
            return;
        }

        var arquebusierIds = Arquebusiers.Select(a => a.Id).ToList();
        var weaponIds = OwnedWeapons.Select(w => new Guid($"0193a400-0000-7000-8000-{w.Number:D12}")).ToList();
        if (await db.Arquebusiers.AnyAsync(a => !arquebusierIds.Contains(a.Id), cancellationToken)
            || await db.OwnedWeapons.AnyAsync(w => !weaponIds.Contains(w.Id), cancellationToken))
        {
            throw new InvalidOperationException("The database holds registry data that is not synthetic; refusing to seed it (NFR-13).");
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Synthetic registry ensured: {Arquebusiers} arquebusiers and {Weapons} owned weapons added")]
    private static partial void LogSeeded(ILogger logger, int arquebusiers, int weapons);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Synthetic {Kind} {Id} skipped: its comparsa or model is missing, or another row uses its unique values")]
    private static partial void LogSkipped(ILogger logger, string kind, Guid id);

    /// <param name="Number">Last part of the fixed identifier; also gives the synthetic DNI/NIE and federation id.</param>
    /// <param name="ComparsaId">Seeded comparsa.</param>
    /// <param name="FirstName">Synthetic first name.</param>
    /// <param name="LastName">Synthetic last name.</param>
    /// <param name="Gender">Gender.</param>
    /// <param name="Status">Active or Reserve.</param>
    /// <param name="License">Which license state the arquebusier has.</param>
    /// <param name="Course">Whether the course is done.</param>
    /// <param name="Nie">An NIE (X prefix) instead of a DNI.</param>
    private sealed record ArquebusierSeed(
        int Number, Guid ComparsaId, string FirstName, string LastName, Gender Gender, ArquebusierStatus Status,
        LicenseSeed License, bool Course, bool Nie = false)
    {
        private const string Letters = "TRWAGMYFPDXBNJZSQVHLCKE";

        public Guid Id => IdOf(Number);

        /// <summary>Very low numbers, unlikely to be in use; the check letter is computed as BR-01 requires.</summary>
        public string NationalId => Nie
            ? $"X{Number:D7}{Letters[Number % Letters.Length]}"
            : $"{Number:D8}{Letters[Number % Letters.Length]}";

        public int FederationId => 100_000 + Number;

        public static Guid IdOf(int number) => new($"0193a300-0000-7000-8000-{number:D12}");

        public Arquebusier ToArquebusier(DateTimeOffset now, DateOnly today)
        {
            var arquebusier = new Arquebusier
            {
                Id = Id,
                ComparsaId = ComparsaId,
                FederationId = FederationId,
                NationalId = NationalId,
                FirstName = FirstName,
                LastName = LastName,
                BirthDate = today.AddYears(-20 - Number).AddDays(-Number * 7),
                Email = Number % 4 == 0 ? null : $"arcabucero.{Number:D2}@polvorapp.example",
                Phone = Number % 3 == 0 ? null : $"+34 600 000 {Number:D3}",
                Gender = Gender,
                Status = Status,
                TrainingCompletedOn = Course ? today.AddYears(-1).AddDays(-Number) : null,
                CreatedAt = now,
            };
            (arquebusier.LicenseType, arquebusier.LicensePending, arquebusier.LicenseIssuedOn, arquebusier.LicenseExpiresOn) = License switch
            {
                LicenseSeed.Pending => (LicenseType.Ae, true, (DateOnly?)null, (DateOnly?)null),
                LicenseSeed.Valid => (LicenseType.Ae, false, today.AddYears(-1), today.AddYears(4)),
                LicenseSeed.ValidProf => (LicenseType.AProf, false, today.AddMonths(-2), today.AddMonths(10)),
                LicenseSeed.Expired => (LicenseType.Ae, false, today.AddYears(-6), today.AddYears(-1)),
                _ => ((LicenseType?)null, false, (DateOnly?)null, (DateOnly?)null),
            };
            return arquebusier;
        }
    }
}
