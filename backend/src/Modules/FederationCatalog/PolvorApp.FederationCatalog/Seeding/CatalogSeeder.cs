using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PolvorApp.FederationCatalog.Assignments;
using PolvorApp.FederationCatalog.Comparsas;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Logos;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.FederationCatalog.WeaponModels;
using PolvorApp.SharedKernel.Hosting;
using PolvorApp.SharedKernel.Images;
using PolvorApp.SharedKernel.Seeding;
using PolvorApp.SharedKernel.Storage;

namespace PolvorApp.FederationCatalog.Seeding;

/// <summary>
/// Synthetic catalogue for development, staging and E2E tests (spec: Synthetic catalogue data,
/// SEC-11, design D7; realistic-seed-data, design D2 and D8): the invented comparsas of
/// <see cref="SyntheticComparsas"/> (four in the scenarios dataset, twenty in the full one, one
/// inactive), assignments for the seeded FiringChiefs (one comparsa with two chiefs, one chief with
/// two comparsas, and one chief per added comparsa in the full dataset), the committed generated
/// emblems for every comparsa but Abencerrajes (Cruzados' is dark), and a weapon catalogue with
/// every kind. Fixed identifiers; existing rows are left untouched and a row whose name, label or
/// combination another row already uses is skipped with a warning, so it can run again. The rows
/// are saved at once; the logos need the object storage and are stored after them, so a failure
/// there leaves the rows without logos until a rerun completes them. A seeded logo an Admin removed
/// is added again; one an Admin replaced is kept locally and refused as real data elsewhere. Like
/// the identity seeder it writes no audit entries (it is not a user action). The assignments rely on
/// the identity seeder (order 10) having created the FiringChiefs; there is no cross-module key.
/// Real comparsas and models are entered by an Admin (maintainer decision).
/// </summary>
internal sealed partial class CatalogSeeder(
    FederationCatalogDbContext db,
    IObjectStorage storage,
    IImageNormalizer images,
    SyntheticImages syntheticImages,
    IConfiguration configuration,
    TimeProvider time,
    IHostEnvironment environment,
    ILogger<CatalogSeeder> logger) : IDataSeeder
{
    /// <summary>"Joan Moltó Sala" (jefe.uno@) of the identity seeder, which this module cannot reference; a test keeps it equal.</summary>
    internal static readonly Guid JefeUno = new("0193a000-0000-7000-8000-000000000002");

    /// <summary>"Elena Verdú Ivorra" (jefa.dos@) of the identity seeder; a test keeps it equal.</summary>
    internal static readonly Guid JefaDos = new("0193a000-0000-7000-8000-000000000003");

    /// <summary>Cruzados: both scenario FiringChiefs and the dark emblem.</summary>
    internal static readonly Guid Norte = SyntheticComparsas.ByNumber(1).Id;

    /// <summary>Abencerrajes: Jefe Uno's second comparsa, never a logo.</summary>
    internal static readonly Guid Sur = SyntheticComparsas.ByNumber(2).Id;

    internal static readonly IReadOnlyList<(Guid ComparsaId, Guid UserId)> Assignments =
        [(Norte, JefeUno), (Sur, JefeUno), (Norte, JefaDos)];

    private static readonly IReadOnlyList<WeaponModelSeed> Models =
    [
        new(1, WeaponKind.Trabuco, Side.Christian, Handedness.Right, WeaponSize.Normal, "TRABUCO CRISTIANO DIESTRO"),
        new(2, WeaponKind.Trabuco, Side.Christian, Handedness.Right, WeaponSize.Small, "TRABUCO CRISTIANO DIESTRO (PEQUEÑO)"),
        new(3, WeaponKind.Trabuco, Side.Christian, Handedness.Left, WeaponSize.Normal, "TRABUCO CRISTIANO ZURDO"),
        new(4, WeaponKind.Trabuco, Side.Christian, Handedness.Left, WeaponSize.Small, "TRABUCO CRISTIANO ZURDO (PEQUEÑO)"),
        new(5, WeaponKind.Arcabuz, Side.Moorish, Handedness.Right, WeaponSize.Normal, "ARCABUZ MORO DIESTRO"),
        new(6, WeaponKind.Arcabuz, Side.Moorish, Handedness.Right, WeaponSize.Small, "ARCABUZ MORO DIESTRO (PEQUEÑO)"),
        new(7, WeaponKind.Arcabuz, Side.Moorish, Handedness.Left, WeaponSize.Normal, "ARCABUZ MORO ZURDO"),
        new(8, WeaponKind.Arcabuz, Side.Moorish, Handedness.Left, WeaponSize.Small, "ARCABUZ MORO ZURDO (PEQUEÑO)", Active: false),
        new(9, WeaponKind.Pistol, null, null, null, "PISTOLA"),
    ];

    /// <summary>After the identity seeder (10): the assignments refer to its users.</summary>
    public int Order => 20;

    /// <summary>The assignments of <paramref name="dataset"/>: the scenario ones, plus each added comparsa's FiringChief.</summary>
    internal static IReadOnlyList<(Guid ComparsaId, Guid UserId)> AssignmentsFor(SeedDataset dataset) => dataset == SeedDataset.Full
        ? [.. Assignments, .. SyntheticPeople.FiringChiefs.Select(c => (SyntheticComparsas.ByNumber(c.ComparsaNumber).Id, c.Id))]
        : Assignments;

    /// <summary>One fixed logo id per seeded comparsa that has a logo, and the comparsa's name to find its emblem.</summary>
    internal static IReadOnlyList<(Guid ComparsaId, Guid LogoId, string Comparsa)> LogosFor(SeedDataset dataset) =>
    [
        .. SyntheticComparsas.Of(dataset).Where(c => c.HasLogo).Select(c =>
            (c.Id, new Guid($"0193a600-0000-7000-8000-{c.Number.ToString("D12", CultureInfo.InvariantCulture)}"), c.Name)),
    ];

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var dataset = SeedDatasets.Read(configuration);
        await RefuseRealDataOutsideLocalAsync(cancellationToken);
        var now = time.GetUtcNow();
        await AddComparsasAsync(dataset, now, cancellationToken);
        await AddAssignmentsAsync(dataset, now, cancellationToken);
        await AddModelsAsync(now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        var logos = await AddLogosAsync(dataset, now, cancellationToken);
        var (comparsas, assignments) = (SyntheticComparsas.Of(dataset).Count, AssignmentsFor(dataset).Count);
        LogSeeded(logger, dataset, comparsas, assignments, Models.Count, logos);
    }

    /// <summary>
    /// Stores each image before its reference, as uploads do (design D4). Keys derive from the fixed
    /// logo ids, so a rerun overwrites the same objects and restores one that went missing. A seeded
    /// comparsa whose logo an Admin replaced keeps it.
    /// </summary>
    private async Task<int> AddLogosAsync(SeedDataset dataset, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var logos = LogosFor(dataset)
            .Select(l => (l.ComparsaId, l.LogoId, Image: syntheticImages.LogoFor(l.Comparsa)
                ?? throw new InvalidOperationException($"The committed emblems have no logo for the seeded comparsa {l.Comparsa}.")))
            .ToList();
        var ids = logos.Select(l => l.ComparsaId).ToList();
        var comparsas = await db.Comparsas.Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
        var added = 0;
        foreach (var (comparsaId, logoId, emblem) in logos)
        {
            var source = (Name: emblem.File, Read: (Func<byte[]>)(() => syntheticImages.Read(emblem)));
            if (!comparsas.TryGetValue(comparsaId, out var comparsa))
            {
                LogTaken(logger, "logo (its comparsa was skipped)", logoId);
                continue;
            }

            if (comparsa.Logo is { } current && current.Id != logoId)
            {
                // An Admin replaced it locally: theirs is kept (documented above).
                continue;
            }

            if (comparsa.Logo is { } seeded)
            {
                await RestoreImageAsync(seeded.ObjectKey, source, cancellationToken);
                continue;
            }

            var key = LogoStorage.KeyFor(logoId);
            var image = await StoreImageAsync(key, source, cancellationToken);
            comparsa.Logo = new ComparsaLogo
            {
                Id = logoId,
                ObjectKey = key,
                Width = image.Width,
                Height = image.Height,
                SizeBytes = image.Content.Length,
                UploadedAt = now,
            };
            added++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return added;
    }

    private async Task RestoreImageAsync(string key, (string Name, Func<byte[]> Read) source, CancellationToken cancellationToken)
    {
        if (await storage.GetAsync(key, cancellationToken) is { } present)
        {
            await present.DisposeAsync();
            return;
        }

        await StoreImageAsync(key, source, cancellationToken);
    }

    private async Task<NormalizedImage> StoreImageAsync(string key, (string Name, Func<byte[]> Read) source, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(source.Read());
        var image = await images.NormalizeAsync(stream, LogoStorage.Rules, cancellationToken) switch
        {
            NormalizedImage normalized => normalized,
            RejectedImage rejected => throw new InvalidOperationException($"The synthetic logo {source.Name} breaks the logo rules ({rejected.Reason})."),
            _ => throw new InvalidOperationException($"The synthetic logo {source.Name} could not be normalised."),
        };
        await storage.PutAsync(key, image.Content, LogoStorage.ContentType, cancellationToken);
        return image;
    }

    private async Task AddComparsasAsync(SeedDataset dataset, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await db.Comparsas.AsNoTracking().Select(c => new { c.Id, c.Name }).ToListAsync(cancellationToken);
        var ids = existing.Select(c => c.Id).ToHashSet();
        var names = existing.Select(c => c.Name.ToLowerInvariant()).ToHashSet();
        foreach (var comparsa in SyntheticComparsas.Of(dataset).Where(c => !ids.Contains(c.Id)))
        {
            if (!names.Add(comparsa.Name.ToLowerInvariant()))
            {
                LogTaken(logger, "comparsa", comparsa.Id);
                continue;
            }

            db.Comparsas.Add(new Comparsa
            {
                Id = comparsa.Id,
                Name = comparsa.Name,
                Side = comparsa.Christian ? Side.Christian : Side.Moorish,
                Active = comparsa.Active,
                CreatedAt = now,
            });
        }
    }

    private async Task AddAssignmentsAsync(SeedDataset dataset, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = (await db.Assignments.AsNoTracking().Select(a => new { a.ComparsaId, a.UserId }).ToListAsync(cancellationToken))
            .Select(a => (a.ComparsaId, a.UserId))
            .ToHashSet();
        var comparsas = db.ChangeTracker.Entries<Comparsa>().Select(e => e.Entity.Id)
            .Concat(await db.Comparsas.AsNoTracking().Select(c => c.Id).ToListAsync(cancellationToken))
            .ToHashSet();
        foreach (var (comparsaId, userId) in AssignmentsFor(dataset).Where(a => !existing.Contains(a) && comparsas.Contains(a.ComparsaId)))
        {
            db.Assignments.Add(new FiringChiefAssignment { ComparsaId = comparsaId, UserId = userId, AssignedAt = now });
        }
    }

    private async Task AddModelsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await db.WeaponModels.AsNoTracking()
            .Select(m => new { m.Id, m.Label, m.Kind, m.Side, m.Handedness, m.Size })
            .ToListAsync(cancellationToken);
        var ids = existing.Select(m => m.Id).ToHashSet();
        var labels = existing.Select(m => m.Label.ToLowerInvariant()).ToHashSet();
        var combinations = existing.Where(m => m.Kind != WeaponKind.Pistol).Select(m => (m.Kind, m.Side, m.Handedness, m.Size)).ToHashSet();
        foreach (var model in Models.Where(m => !ids.Contains(m.Id)))
        {
            var combinationTaken = model.Kind != WeaponKind.Pistol && !combinations.Add((model.Kind, model.Side, model.Handedness, model.Size));
            if (!labels.Add(model.Label.ToLowerInvariant()) || combinationTaken)
            {
                LogTaken(logger, "weapon model", model.Id);
                continue;
            }

            db.WeaponModels.Add(new WeaponModel
            {
                Id = model.Id,
                Kind = model.Kind,
                Side = model.Side,
                Handedness = model.Handedness,
                Size = model.Size,
                // Any kind may be rentable (BR-07); the seed's pistol stays non-rentable to exercise a non-rentable model.
                Rentable = model.Kind != WeaponKind.Pistol,
                Label = model.Label,
                Active = model.Active,
                CreatedAt = now,
            });
        }
    }

    /// <summary>
    /// Staging may be reachable and must only ever hold synthetic data (NFR-13): before writing
    /// anything, refuse a database with any comparsa, logo, weapon model or assignment this seeder
    /// would not have created, as the identity seeder does for users. A logo uploaded by hand may
    /// be a real emblem (ADR-0012).
    /// </summary>
    private async Task RefuseRealDataOutsideLocalAsync(CancellationToken cancellationToken)
    {
        if (LocalEnvironments.IsLocal(environment))
        {
            return;
        }

        // The full dataset is a superset: a database seeded with it may later run the scenarios.
        var comparsaIds = SyntheticComparsas.All.Select(c => c.Id).ToList();
        var logoIds = LogosFor(SeedDataset.Full).Select(l => l.LogoId).ToList();
        var modelIds = Models.Select(m => m.Id).ToList();
        var assignments = AssignmentsFor(SeedDataset.Full);
        var userIds = assignments.Select(a => a.UserId).Distinct().ToList();
        if (await db.Comparsas.AnyAsync(c => !comparsaIds.Contains(c.Id), cancellationToken)
            || await db.Comparsas.AnyAsync(c => c.Logo != null && !logoIds.Contains(c.Logo.Id), cancellationToken)
            || await db.WeaponModels.AnyAsync(m => !modelIds.Contains(m.Id), cancellationToken)
            || await db.Assignments.AnyAsync(a => !comparsaIds.Contains(a.ComparsaId) || !userIds.Contains(a.UserId), cancellationToken))
        {
            throw new InvalidOperationException("The database holds catalogue data that is not synthetic; refusing to seed it (NFR-13).");
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Synthetic catalogue ({Dataset}) planned {Comparsas} comparsas, {Assignments} assignments and {Models} weapon models, each added unless present or skipped above; {Logos} new logos")]
    private static partial void LogSeeded(ILogger logger, SeedDataset dataset, int comparsas, int assignments, int models, int logos);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Synthetic {Kind} {Id} skipped: another row already uses its name, label or combination")]
    private static partial void LogTaken(ILogger logger, string kind, Guid id);

    /// <param name="Number">Last part of the fixed identifier.</param>
    /// <param name="Kind">Weapon kind.</param>
    /// <param name="Side">Side, null for the pistol.</param>
    /// <param name="Handedness">Handedness, null for the pistol.</param>
    /// <param name="Size">Size, null for the pistol.</param>
    /// <param name="Label">Label in the Federation's naming style.</param>
    /// <param name="Active">False for the one retired model.</param>
    private sealed record WeaponModelSeed(int Number, WeaponKind Kind, Side? Side, Handedness? Handedness, WeaponSize? Size, string Label, bool Active = true)
    {
        public Guid Id { get; } = new($"0193a200-0000-7000-8000-{Number:D12}");
    }
}
