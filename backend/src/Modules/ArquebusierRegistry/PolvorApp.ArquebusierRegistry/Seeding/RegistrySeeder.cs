using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PolvorApp.ArquebusierRegistry.Arquebusiers;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ArquebusierRegistry.OwnedWeapons;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.ArquebusierRegistry.Photos;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.SharedKernel.Hosting;
using PolvorApp.SharedKernel.Images;
using PolvorApp.SharedKernel.Seeding;
using PolvorApp.SharedKernel.Storage;
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
/// calls or messages them. Some arquebusiers get generated placeholder photos (flat shapes, no faces or
/// text; add-arquebusier-photos, design D10), stored through the same normaliser as uploads; a photo
/// whose image went missing from the storage is stored again. Together the arquebusiers show every
/// compliance warning (add-compliance-insights, design D9), including an expiring license and an
/// arquebusier under 18, on a freshly seeded database; existing rows are never refreshed.
/// </summary>
internal sealed partial class RegistrySeeder(
    ArquebusierRegistryDbContext db,
    ICatalogDirectory catalog,
    IObjectStorage storage,
    IImageNormalizer images,
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

        // Under 18, with a license expiring within 12 months, no course and no ID photo: four warnings
        // (change add-compliance-insights, design D9).
        new(14, Norte, "Arcabucera", "Sintética Catorce", Gender.Female, ArquebusierStatus.Active, LicenseSeed.Expiring, Course: false, AgeYears: 16),
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

    /// <summary>Arquebusiers with an ID photo, and those of them (licensed) with both license photos.</summary>
    private static readonly IReadOnlyList<(int Owner, ArquebusierPhotoKind Kind)> Photos =
    [
        .. new[] { 1, 2, 3, 6, 7, 10, 12 }.Select(owner => (owner, ArquebusierPhotoKind.Id)),
        .. new[] { 1, 2, 6 }.SelectMany(owner => new[] { (owner, ArquebusierPhotoKind.LicenseFront), (owner, ArquebusierPhotoKind.LicenseBack) }),

        // An issued license with only its front photo: the LICENSE_PHOTOS_MISSING warning.
        (7, ArquebusierPhotoKind.LicenseFront),

        // License photos but no ID photo (design D9).
        (14, ArquebusierPhotoKind.LicenseFront),
        (14, ArquebusierPhotoKind.LicenseBack),
    ];

    private enum LicenseSeed
    {
        None,
        Pending,
        Valid,
        ValidProf,
        Expiring,
        Expired,
    }

    public static int ArquebusierCount => Arquebusiers.Count;

    public static int OwnedWeaponCount => OwnedWeapons.Count;

    public static int PhotoCount => Photos.Count;

    /// <summary>The seeded arquebusier with all three photos (E2E and tests rely on it).</summary>
    public static Guid AllPhotosArquebusier => ArquebusierSeed.IdOf(1);

    /// <summary>A seeded arquebusier without any photo.</summary>
    public static Guid NoPhotosArquebusier => ArquebusierSeed.IdOf(4);

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
        var photos = await AddPhotosAsync(now, cancellationToken);
        LogSeeded(logger, added, weapons, photos);
    }

    /// <summary>
    /// Stores each image before its reference, as uploads do (design D2). Keys derive from the fixed
    /// photo ids, so a rerun overwrites the same objects and restores one that went missing.
    /// </summary>
    private async Task<int> AddPhotosAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await db.Photos.AsNoTracking().ToDictionaryAsync(p => p.Id, cancellationToken);
        var owners = await db.Arquebusiers.AsNoTracking()
            .Select(a => new { a.Id, HasLicense = a.LicenseType != null })
            .ToDictionaryAsync(a => a.Id, a => a.HasLicense, cancellationToken);
        var taken = existing.Values.Select(p => (p.ArquebusierId, p.Kind)).ToHashSet();
        var added = 0;
        foreach (var (owner, kind) in Photos)
        {
            var id = PhotoIdOf(owner, kind);
            if (existing.TryGetValue(id, out var stored))
            {
                await RestoreImageAsync(owner, kind, stored.ObjectKey, cancellationToken);
                continue;
            }

            var ownerId = ArquebusierSeed.IdOf(owner);
            if (!owners.TryGetValue(ownerId, out var hasLicense) || (PhotoStorage.NeedsLicense(kind) && !hasLicense) || !taken.Add((ownerId, kind)))
            {
                LogSkipped(logger, "photo", id);
                continue;
            }

            var key = ArquebusierPhoto.KeyFor(id);
            var image = await StoreImageAsync(owner, kind, key, cancellationToken);
            db.Photos.Add(new ArquebusierPhoto
            {
                Id = id,
                ArquebusierId = ownerId,
                Kind = kind,
                ObjectKey = key,
                Width = image.Width,
                Height = image.Height,
                SizeBytes = image.Content.Length,
                UploadedAt = now,
            });
            added++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return added;
    }

    private async Task RestoreImageAsync(int owner, ArquebusierPhotoKind kind, string key, CancellationToken cancellationToken)
    {
        if (await storage.GetAsync(key, cancellationToken) is { } present)
        {
            await present.DisposeAsync();
            return;
        }

        await StoreImageAsync(owner, kind, key, cancellationToken);
    }

    private async Task<NormalizedImage> StoreImageAsync(int owner, ArquebusierPhotoKind kind, string key, CancellationToken cancellationToken)
    {
        var source = kind switch
        {
            ArquebusierPhotoKind.Id => SyntheticPhotos.IdPhoto(owner),
            ArquebusierPhotoKind.LicenseFront => SyntheticPhotos.LicenseSide(front: true, owner),
            _ => SyntheticPhotos.LicenseSide(front: false, owner),
        };
        using var stream = new MemoryStream(source);
        var image = await images.NormalizeAsync(stream, PhotoStorage.RulesFor(kind), cancellationToken) switch
        {
            NormalizedImage normalized => normalized,
            RejectedImage rejected => throw new InvalidOperationException($"The synthetic {kind} photo breaks the photo rules ({rejected.Reason})."),
            _ => throw new InvalidOperationException($"The synthetic {kind} photo could not be normalised."),
        };
        await storage.PutAsync(key, image.Content, PhotoStorage.ContentType, cancellationToken);
        return image;
    }

    private static Guid PhotoIdOf(int owner, ArquebusierPhotoKind kind) =>
        new($"0193a500-0000-7000-8000-{owner:D6}{(int)kind:D6}");

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
        var photoIds = Photos.Select(p => PhotoIdOf(p.Owner, p.Kind)).ToList();
        if (await db.Arquebusiers.AnyAsync(a => !arquebusierIds.Contains(a.Id), cancellationToken)
            || await db.OwnedWeapons.AnyAsync(w => !weaponIds.Contains(w.Id), cancellationToken)
            || await db.Photos.AnyAsync(p => !photoIds.Contains(p.Id), cancellationToken))
        {
            // A photo uploaded or replaced by hand counts as real: reset the database to seed it again.
            throw new InvalidOperationException("The database holds registry data that is not synthetic (rows or photos this seeder did not create); refusing to seed it (NFR-13).");
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Synthetic registry ensured: {Arquebusiers} arquebusiers, {Weapons} owned weapons and {Photos} photos added")]
    private static partial void LogSeeded(ILogger logger, int arquebusiers, int weapons, int photos);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Synthetic {Kind} {Id} skipped: its comparsa, model, owner or license is missing, or another row uses its unique values")]
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
    /// <param name="AgeYears">A fixed age on the seed day; otherwise 20 + Number years.</param>
    private sealed record ArquebusierSeed(
        int Number, Guid ComparsaId, string FirstName, string LastName, Gender Gender, ArquebusierStatus Status,
        LicenseSeed License, bool Course, bool Nie = false, int? AgeYears = null)
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
                BirthDate = today.AddYears(-(AgeYears ?? 20 + Number)).AddDays(-Number * 7),
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
                LicenseSeed.Expiring => (LicenseType.Ae, false, today.AddMonths(3).AddYears(-5), today.AddMonths(3)),
                LicenseSeed.Expired => (LicenseType.Ae, false, today.AddYears(-6), today.AddYears(-1)),
                _ => ((LicenseType?)null, false, (DateOnly?)null, (DateOnly?)null),
            };
            return arquebusier;
        }
    }
}
