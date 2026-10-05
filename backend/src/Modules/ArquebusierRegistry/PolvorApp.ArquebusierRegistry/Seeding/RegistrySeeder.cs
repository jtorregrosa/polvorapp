using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
/// design D9; realistic-seed-data, designs D2–D6). The scenarios dataset holds 14 fictional
/// arquebusiers with realistic names in the scenario comparsas, covering both statuses, every license
/// state (dates relative to the seed date, so "valid" and "expired" stay true over time), no license,
/// course done and not done, and owned weapons of every kind; together they show every compliance
/// warning, including an expiring license and an arquebusier under 18. The full dataset adds the
/// <see cref="SyntheticPeople"/> population of the added comparsas. DNI/NIE come from
/// <see cref="SyntheticNationalIds"/>; emails use the reserved <c>polvorapp.example</c> domain; the
/// phones are in the Spanish mobile range (there is no reserved fictional range): PolvorApp never
/// calls or messages them. ID photos are the committed generated faces of people who do not exist,
/// matched by gender and age; license photos are specimen cards drawn from the holder's own data.
/// Every image goes through the same normaliser as uploads, and one that went missing from the
/// storage is stored again. Fixed identifiers; existing rows are left untouched and a row whose
/// comparsa, model or unique value is missing or taken is skipped with a warning, so it can run
/// again. It writes no audit entries (it is not a user action). The comparsas and models come from
/// the catalogue seeder (order 20); this module cannot reference it, so the model ids are repeated
/// here and tests keep them equal.
/// </summary>
internal sealed partial class RegistrySeeder(
    ArquebusierRegistryDbContext db,
    ICatalogDirectory catalog,
    IObjectStorage storage,
    IImageNormalizer images,
    SyntheticImages syntheticImages,
    ISpecimenCardPainter cards,
    IConfiguration configuration,
    TimeProvider time,
    IHostEnvironment environment,
    ILogger<RegistrySeeder> logger) : IDataSeeder
{
    private const int PhotoProgressStep = 100;

    private static readonly Guid Norte = SyntheticComparsas.ByNumber(1).Id;
    private static readonly Guid Sur = SyntheticComparsas.ByNumber(2).Id;
    private static readonly Guid Este = SyntheticComparsas.ByNumber(3).Id;
    private static readonly Guid Oeste = SyntheticComparsas.ByNumber(4).Id;

    /// <summary>The scenario arquebusiers (realistic-seed-data, design D2): Norte is Cruzados, Sur Abencerrajes, Este Hospitalarios, Oeste Zegríes.</summary>
    private static readonly IReadOnlyList<ArquebusierSeed> Scenarios =
    [
        new(1, Norte, "Vicent", "Sempere Llorens", Gender.Male, ArquebusierStatus.Active, LicenseSeed.Valid, Course: true),
        new(2, Norte, "Amparo", "Pastor Gomis", Gender.Female, ArquebusierStatus.Active, LicenseSeed.ValidProf, Course: true),
        new(3, Norte, "Pau", "Alberola Navarro", Gender.Unspecified, ArquebusierStatus.Active, LicenseSeed.Expired, Course: false),
        new(4, Norte, "Remedios", "Lledó Pérez", Gender.Female, ArquebusierStatus.Reserve, LicenseSeed.None, Course: true),
        new(5, Norte, "Youssef", "El Amrani", Gender.Male, ArquebusierStatus.Active, LicenseSeed.Pending, Course: false, Nie: true),
        new(6, Sur, "Mari Carmen", "Ferrándiz Soler", Gender.Female, ArquebusierStatus.Active, LicenseSeed.Valid, Course: true),
        new(7, Sur, "Josep Ramon", "Candela Martínez", Gender.Male, ArquebusierStatus.Active, LicenseSeed.Valid, Course: true),
        new(8, Sur, "Pepa", "Mira Carbonell", Gender.Female, ArquebusierStatus.Reserve, LicenseSeed.Expired, Course: true),
        new(9, Sur, "Toni", "Baeza Ripoll", Gender.Male, ArquebusierStatus.Active, LicenseSeed.Pending, Course: false),
        new(10, Este, "Ioana", "Popescu", Gender.Female, ArquebusierStatus.Active, LicenseSeed.Valid, Course: true, Nie: true),
        new(11, Este, "Rafael", "Climent Esteve", Gender.Male, ArquebusierStatus.Active, LicenseSeed.None, Course: false),
        new(12, Este, "Àlex", "Beltrà Riquelme", Gender.Unspecified, ArquebusierStatus.Reserve, LicenseSeed.Valid, Course: true),
        new(13, Oeste, "Francisco", "Asensi Mollà", Gender.Male, ArquebusierStatus.Reserve, LicenseSeed.Expired, Course: true),

        // Under 18, with a license expiring within 12 months, no course and no ID photo: four warnings
        // (change add-compliance-insights, design D9).
        new(14, Norte, "Laia", "Sempere Pastor", Gender.Female, ArquebusierStatus.Active, LicenseSeed.Expiring, Course: false, AgeYears: 16),
    ];

    /// <summary>Owner number, catalogue model number (see the catalogue seeder) and weapon number.</summary>
    private static readonly IReadOnlyList<(int Owner, int Model, int Number)> ScenarioWeapons =
    [
        (1, 1, 1),  // TRABUCO CRISTIANO DIESTRO
        (2, 9, 2),  // PISTOLA
        (6, 5, 3),  // ARCABUZ MORO DIESTRO
        (7, 6, 4),  // ARCABUZ MORO DIESTRO (PEQUEÑO)
        (10, 8, 5), // ARCABUZ MORO ZURDO (PEQUEÑO), deactivated: an existing weapon keeps its model
    ];

    /// <summary>Scenario arquebusiers with an ID photo, and those of them (licensed) with both license photos.</summary>
    private static readonly IReadOnlyList<(int Owner, ArquebusierPhotoKind Kind)> ScenarioPhotos =
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

    public static int ArquebusierCount => Scenarios.Count;

    public static int OwnedWeaponCount => ScenarioWeapons.Count;

    public static int PhotoCount => ScenarioPhotos.Count;

    /// <summary>The seeded arquebusier with all three photos (E2E and tests rely on it).</summary>
    public static Guid AllPhotosArquebusier => SyntheticPeople.ArquebusierId(1);

    /// <summary>A seeded arquebusier without any photo.</summary>
    public static Guid NoPhotosArquebusier => SyntheticPeople.ArquebusierId(4);

    /// <summary>After the catalogue seeder (20): arquebusiers refer to its comparsas and models.</summary>
    public int Order => 30;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var dataset = SeedDatasets.Read(configuration);
        var today = FederationCalendar.Today(time);
        var now = time.GetUtcNow();
        var plan = Plan(dataset, today, now);
        // The full dataset is a superset: a database seeded with it may later run the scenarios.
        await RefuseRealDataOutsideLocalAsync(dataset == SeedDataset.Full ? plan : Plan(SeedDataset.Full, today, now), cancellationToken);
        var added = await AddArquebusiersAsync(plan, cancellationToken);
        var weapons = await AddOwnedWeaponsAsync(plan, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        var photos = await AddPhotosAsync(plan, today, now, cancellationToken);
        LogSeeded(logger, added, weapons, photos);
    }

    /// <summary>Everything the dataset holds, built in memory: a pure function of the dataset and the day.</summary>
    private static SeedPlan Plan(SeedDataset dataset, DateOnly today, DateTimeOffset now)
    {
        var arquebusiers = Scenarios.Select(s => s.ToArquebusier(today, now)).ToList();
        var weapons = ScenarioWeapons
            .Select(w => new WeaponSeed(WeaponId(w.Number), SyntheticPeople.ArquebusierId(w.Owner), ModelId(w.Model),
                (1000 + w.Number).ToString(CultureInfo.InvariantCulture), ScenarioGuide(w.Number)))
            .ToList();
        var photos = ScenarioPhotos.Select(p => (Owner: SyntheticPeople.ArquebusierId(p.Owner), p.Kind)).ToList();

        if (dataset == SeedDataset.Full)
        {
            foreach (var person in SyntheticPeople.Population(today))
            {
                arquebusiers.Add(ToArquebusier(person, now));
                if (person.Weapon is { } weapon)
                {
                    weapons.Add(new WeaponSeed(WeaponId(person.Number), person.Id, ModelId(ModelNumber(weapon)), weapon.WeaponNumber, weapon.Guide));
                }

                if (person.IdPhoto)
                {
                    photos.Add((person.Id, ArquebusierPhotoKind.Id));
                }

                if (person.LicensePhotos != SyntheticLicensePhotos.None)
                {
                    photos.Add((person.Id, ArquebusierPhotoKind.LicenseFront));
                }

                if (person.LicensePhotos == SyntheticLicensePhotos.Both)
                {
                    photos.Add((person.Id, ArquebusierPhotoKind.LicenseBack));
                }
            }
        }

        // A duplicate inside the plan is a generator bug, not a conflict with existing data: fail.
        RequireUnique(arquebusiers.Select(a => a.NationalId), "national ID");
        RequireUnique(arquebusiers.Select(a => a.FederationId.ToString(CultureInfo.InvariantCulture)), "federation ID");
        RequireUnique(weapons.Select(w => w.Guide), "ownership guide");
        return new SeedPlan(arquebusiers, weapons, photos);
    }

    private static void RequireUnique(IEnumerable<string> values, string what)
    {
        if (values.GroupBy(v => v, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } duplicate)
        {
            throw new InvalidOperationException($"The synthetic registry plan repeats the {what} {duplicate.Key}.");
        }
    }

    private static Arquebusier ToArquebusier(SyntheticPerson person, DateTimeOffset now)
    {
        var license = person.License;
        return new Arquebusier
        {
            Id = person.Id,
            ComparsaId = SyntheticComparsas.ByNumber(person.ComparsaNumber).Id,
            FederationId = FederationIdOf(person.Number),
            NationalId = person.NationalId,
            FirstName = person.FirstName,
            LastName = person.LastName,
            BirthDate = person.BirthDate,
            Email = person.Email,
            Phone = person.Phone,
            Gender = person.Gender == SyntheticGender.Male ? Gender.Male : Gender.Female,
            Status = person.Reserve ? ArquebusierStatus.Reserve : ArquebusierStatus.Active,
            TrainingCompletedOn = person.CourseCompletedOn,
            LicenseType = license.Type switch
            {
                SyntheticLicenseType.Ae => LicenseType.Ae,
                SyntheticLicenseType.AProf => LicenseType.AProf,
                _ => null,
            },
            LicensePending = license.Pending,
            LicenseIssuedOn = license.IssuedOn,
            LicenseExpiresOn = license.ExpiresOn,
            CreatedAt = now,
        };
    }

    /// <summary>The catalogue seeder's model numbers: trabucos 1–4, arcabuces 5–8 (right/left, normal/small), pistol 9.</summary>
    private static int ModelNumber(SyntheticOwnedWeapon weapon) => weapon.Kind switch
    {
        SyntheticWeaponKind.Pistol => 9,
        SyntheticWeaponKind.Trabuco => 1 + (weapon.LeftHanded ? 2 : 0) + (weapon.Small ? 1 : 0),
        _ => 5 + (weapon.LeftHanded ? 2 : 0) + (weapon.Small ? 1 : 0),
    };

    /// <summary>
    /// Stores each image before its reference, as uploads do (design D2). Keys derive from the fixed
    /// photo ids, so a rerun overwrites the same objects and restores one that went missing.
    /// </summary>
    private async Task<int> AddPhotosAsync(SeedPlan plan, DateOnly today, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await db.Photos.AsNoTracking().ToDictionaryAsync(p => p.Id, cancellationToken);
        var owners = await db.Arquebusiers.AsNoTracking().ToDictionaryAsync(a => a.Id, cancellationToken);
        var faces = AssignFaces(plan, today);
        var taken = existing.Values.Select(p => (p.ArquebusierId, p.Kind)).ToHashSet();
        var (added, handled) = (0, 0);
        foreach (var (ownerId, kind) in plan.Photos)
        {
            if (++handled % PhotoProgressStep == 0)
            {
                LogPhotoProgress(logger, handled, plan.Photos.Count);
            }

            var id = PhotoIdOf(ownerId, kind);
            if (!owners.TryGetValue(ownerId, out var owner))
            {
                LogSkipped(logger, "photo", id, "owner missing (skipped above)");
                continue;
            }

            if (existing.TryGetValue(id, out var stored))
            {
                await RestoreImageAsync(owner, kind, faces, stored.ObjectKey, cancellationToken);
                continue;
            }

            if (PhotoStorage.NeedsLicense(kind) && owner.LicenseIssuedOn is null)
            {
                LogSkipped(logger, "photo", id, "the owner has no issued license");
                continue;
            }

            if (!taken.Add((ownerId, kind)))
            {
                LogSkipped(logger, "photo", id, "the owner already has another photo of this kind");
                continue;
            }

            var key = ArquebusierPhoto.KeyFor(id);
            var image = await StoreImageAsync(owner, kind, faces, key, cancellationToken);
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

    /// <summary>One generated face per ID photo of the plan, by gender and age band (design D5).</summary>
    private Dictionary<Guid, SyntheticFace> AssignFaces(SeedPlan plan, DateOnly today)
    {
        var people = plan.Arquebusiers.ToDictionary(a => a.Id);
        var requests = plan.Photos
            .Where(p => p.Kind == ArquebusierPhotoKind.Id && people.ContainsKey(p.Owner))
            .Select((p, index) => (Request: new SyntheticFaceRequest(index, GenderOf(people[p.Owner].Gender), people[p.Owner].BirthDate), p.Owner))
            .ToList();
        var assignment = syntheticImages.AssignFaces(requests.Select(r => r.Request), today);
        if (assignment.Reused > 0 || assignment.OutsideBand > 0)
        {
            LogFacesStretched(logger, requests.Count, assignment.Reused, assignment.OutsideBand);
        }

        return requests.ToDictionary(r => r.Owner, r => assignment.Faces[r.Request.Key]);
    }

    private static SyntheticGender? GenderOf(Gender gender) => gender switch
    {
        Gender.Male => SyntheticGender.Male,
        Gender.Female => SyntheticGender.Female,
        _ => null,
    };

    private async Task RestoreImageAsync(Arquebusier owner, ArquebusierPhotoKind kind, IReadOnlyDictionary<Guid, SyntheticFace> faces, string key, CancellationToken cancellationToken)
    {
        if (await storage.GetAsync(key, cancellationToken) is { } present)
        {
            await present.DisposeAsync();
            return;
        }

        await StoreImageAsync(owner, kind, faces, key, cancellationToken);
    }

    private async Task<NormalizedImage> StoreImageAsync(Arquebusier owner, ArquebusierPhotoKind kind, IReadOnlyDictionary<Guid, SyntheticFace> faces, string key, CancellationToken cancellationToken)
    {
        var (source, name) = kind switch
        {
            ArquebusierPhotoKind.Id when faces.TryGetValue(owner.Id, out var face) => (syntheticImages.Read(face), face.File),
            ArquebusierPhotoKind.Id => throw new InvalidOperationException($"No generated face was assigned to the seeded arquebusier {owner.Id}."),
            ArquebusierPhotoKind.LicenseFront => (cards.Front(CardOf(owner)), "license front"),
            _ => (cards.Back(CardOf(owner)), "license back"),
        };
        using var stream = new MemoryStream(source);
        var image = await images.NormalizeAsync(stream, PhotoStorage.RulesFor(kind), cancellationToken) switch
        {
            NormalizedImage normalized => normalized,
            RejectedImage rejected => throw new InvalidOperationException($"The synthetic {kind} photo {name} breaks the photo rules ({rejected.Reason})."),
            _ => throw new InvalidOperationException($"The synthetic {kind} photo {name} could not be normalised."),
        };
        await storage.PutAsync(key, image.Content, PhotoStorage.ContentType, cancellationToken);
        return image;
    }

    private static SpecimenCardData CardOf(Arquebusier owner) => new(
        owner.NationalId,
        owner.FirstName,
        owner.LastName,
        owner.BirthDate,
        owner.LicenseType == LicenseType.AProf ? SyntheticLicenseType.AProf : SyntheticLicenseType.Ae,
        owner.LicenseIssuedOn ?? throw new InvalidOperationException($"The seeded arquebusier {owner.Id} has no issued license for its license photos."),
        owner.LicenseExpiresOn ?? throw new InvalidOperationException($"The seeded arquebusier {owner.Id} has no license expiry for its license photos."));

    private static Guid PhotoIdOf(Guid owner, ArquebusierPhotoKind kind) =>
        new($"0193a500-0000-7000-8000-{NumberOf(owner):D6}{(int)kind:D6}");

    /// <summary>The number in an arquebusier's fixed id (its last 12 digits).</summary>
    private static long NumberOf(Guid arquebusier) => long.Parse(arquebusier.ToString()[^12..], CultureInfo.InvariantCulture);

    private async Task<int> AddArquebusiersAsync(SeedPlan plan, CancellationToken cancellationToken)
    {
        var existing = await db.Arquebusiers.AsNoTracking()
            .Select(a => new { a.Id, a.NationalId, a.FederationId })
            .ToListAsync(cancellationToken);
        var ids = existing.Select(a => a.Id).ToHashSet();
        var nationalIds = existing.Select(a => a.NationalId).ToHashSet(StringComparer.Ordinal);
        var federationIds = existing.Select(a => a.FederationId).ToHashSet();
        var comparsas = (await catalog.FindComparsasAsync([.. plan.Arquebusiers.Select(a => a.ComparsaId).Distinct()], cancellationToken))
            .Select(c => c.Id)
            .ToHashSet();
        var added = 0;
        var skipped = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var arquebusier in plan.Arquebusiers.Where(a => !ids.Contains(a.Id)))
        {
            var reason = !comparsas.Contains(arquebusier.ComparsaId) ? "comparsa missing"
                : nationalIds.Contains(arquebusier.NationalId) ? "national ID taken"
                : federationIds.Contains(arquebusier.FederationId) ? "federation ID taken"
                : null;
            if (reason is not null)
            {
                LogSkipped(logger, "arquebusier", arquebusier.Id, reason);
                skipped[reason] = skipped.GetValueOrDefault(reason) + 1;
                continue;
            }

            nationalIds.Add(arquebusier.NationalId);
            federationIds.Add(arquebusier.FederationId);
            db.Arquebusiers.Add(arquebusier);
            added++;
        }

        LogSkippedTotals(logger, "arquebusiers", skipped);
        return added;
    }

    private async Task<int> AddOwnedWeaponsAsync(SeedPlan plan, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await db.OwnedWeapons.AsNoTracking().Select(w => new { w.Id, w.OwnershipGuideNumber }).ToListAsync(cancellationToken);
        var ids = existing.Select(w => w.Id).ToHashSet();
        var guides = existing.Select(w => w.OwnershipGuideNumber).ToHashSet(StringComparer.Ordinal);
        var owners = db.ChangeTracker.Entries<Arquebusier>().Select(e => e.Entity.Id)
            .Concat(await db.Arquebusiers.AsNoTracking().Select(a => a.Id).ToListAsync(cancellationToken))
            .ToHashSet();
        var models = (await catalog.FindWeaponModelsAsync([.. plan.Weapons.Select(w => w.ModelId).Distinct()], cancellationToken))
            .Select(m => m.Id)
            .ToHashSet();
        var added = 0;
        var skipped = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var weapon in plan.Weapons.Where(w => !ids.Contains(w.Id)))
        {
            var reason = !owners.Contains(weapon.OwnerId) ? "owner missing (skipped above)"
                : !models.Contains(weapon.ModelId) ? "catalogue model missing"
                : guides.Contains(weapon.Guide) ? "ownership guide taken"
                : null;
            if (reason is not null)
            {
                LogSkipped(logger, "owned weapon", weapon.Id, reason);
                skipped[reason] = skipped.GetValueOrDefault(reason) + 1;
                continue;
            }

            guides.Add(weapon.Guide);

            db.OwnedWeapons.Add(new OwnedWeapon
            {
                Id = weapon.Id,
                ArquebusierId = weapon.OwnerId,
                WeaponModelId = weapon.ModelId,
                WeaponNumber = weapon.Number,
                OwnershipGuideNumber = weapon.Guide,
                CreatedAt = now,
            });
            added++;
        }

        LogSkippedTotals(logger, "owned weapons", skipped);
        return added;
    }

    private static void LogSkippedTotals(ILogger logger, string what, Dictionary<string, int> skipped)
    {
        if (skipped.Count > 0)
        {
            LogSkippedSummary(logger, skipped.Values.Sum(), what, string.Join(", ", skipped.Select(s => $"{s.Value} {s.Key}")));
        }
    }

    private static Guid ModelId(int number) => new($"0193a200-0000-7000-8000-{number:D12}");

    private static Guid WeaponId(int number) => new($"0193a400-0000-7000-8000-{number:D12}");

    /// <summary>Below 100000, so a scenario guide never equals a population one (<c>GP-</c> and 6 digits from 100000).</summary>
    private static string ScenarioGuide(int number) => $"GP-{number:D6}";

    private static int FederationIdOf(int number) => 100_000 + number;

    /// <summary>
    /// Staging may be reachable and must only ever hold synthetic data (NFR-13): before writing anything,
    /// refuse a database with any arquebusier, owned weapon or photo this seeder would not have created.
    /// </summary>
    private async Task RefuseRealDataOutsideLocalAsync(SeedPlan plan, CancellationToken cancellationToken)
    {
        if (LocalEnvironments.IsLocal(environment))
        {
            return;
        }

        var arquebusierIds = plan.Arquebusiers.Select(a => a.Id).ToList();
        var weaponIds = plan.Weapons.Select(w => w.Id).ToList();
        var photoIds = plan.Photos.Select(p => PhotoIdOf(p.Owner, p.Kind)).ToList();
        if (await db.Arquebusiers.AnyAsync(a => !arquebusierIds.Contains(a.Id), cancellationToken)
            || await db.OwnedWeapons.AnyAsync(w => !weaponIds.Contains(w.Id), cancellationToken)
            || await db.Photos.AnyAsync(p => !photoIds.Contains(p.Id), cancellationToken))
        {
            // A photo uploaded or replaced by hand counts as real: reset the database to seed it again.
            throw new InvalidOperationException("The database holds registry data that is not synthetic (rows or photos this seeder did not create); refusing to seed it (NFR-13). Reset the database to seed it again.");
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Synthetic registry ensured: {Arquebusiers} arquebusiers, {Weapons} owned weapons and {Photos} photos added")]
    private static partial void LogSeeded(ILogger logger, int arquebusiers, int weapons, int photos);

    [LoggerMessage(Level = LogLevel.Information, Message = "Synthetic photos: {Done} of {Total} handled")]
    private static partial void LogPhotoProgress(ILogger logger, int done, int total);

    [LoggerMessage(Level = LogLevel.Information, Message = "Synthetic faces stretched: of {Photos} ID photos, {Reused} reuse a face and {OutsideBand} use a face of another age band")]
    private static partial void LogFacesStretched(ILogger logger, int photos, int reused, int outsideBand);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Synthetic {Kind} {Id} skipped: {Reason}")]
    private static partial void LogSkipped(ILogger logger, string kind, Guid id, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Count} synthetic {What} skipped: {Reasons}")]
    private static partial void LogSkippedSummary(ILogger logger, int count, string what, string reasons);

    private sealed record WeaponSeed(Guid Id, Guid OwnerId, Guid ModelId, string Number, string Guide);

    private sealed record SeedPlan(
        IReadOnlyList<Arquebusier> Arquebusiers,
        IReadOnlyList<WeaponSeed> Weapons,
        IReadOnlyList<(Guid Owner, ArquebusierPhotoKind Kind)> Photos);

    /// <param name="Number">Last part of the fixed identifier; also gives the DNI/NIE and federation id.</param>
    /// <param name="ComparsaId">Seeded comparsa.</param>
    /// <param name="FirstName">Invented first name.</param>
    /// <param name="LastName">Invented surnames.</param>
    /// <param name="Gender">Gender.</param>
    /// <param name="Status">Active or Reserve.</param>
    /// <param name="License">Which license state the arquebusier has.</param>
    /// <param name="Course">Whether the course is done.</param>
    /// <param name="Nie">An NIE instead of a DNI.</param>
    /// <param name="AgeYears">A fixed age on the seed day; otherwise 20 + Number years.</param>
    private sealed record ArquebusierSeed(
        int Number, Guid ComparsaId, string FirstName, string LastName, Gender Gender, ArquebusierStatus Status,
        LicenseSeed License, bool Course, bool Nie = false, int? AgeYears = null)
    {
        public Arquebusier ToArquebusier(DateOnly today, DateTimeOffset now)
        {
            var arquebusier = new Arquebusier
            {
                Id = SyntheticPeople.ArquebusierId(Number),
                ComparsaId = ComparsaId,
                FederationId = FederationIdOf(Number),
                NationalId = Nie ? SyntheticNationalIds.ScenarioNie(Number) : SyntheticNationalIds.ScenarioDni(Number),
                FirstName = FirstName,
                LastName = LastName,
                BirthDate = today.AddYears(-(AgeYears ?? 20 + Number)).AddDays(-Number * 7),
                Email = Number % 4 == 0 ? null : SyntheticPeople.EmailFor(FirstName, Nie ? LastName : LastName.Split(' ')[0]),
                Phone = Number % 3 == 0 ? null : string.Create(CultureInfo.InvariantCulture, $"+34 6{Number * 37 % 100:D2} {Number * 271 % 1000:D3} {Number * 613 % 1000:D3}"),
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
