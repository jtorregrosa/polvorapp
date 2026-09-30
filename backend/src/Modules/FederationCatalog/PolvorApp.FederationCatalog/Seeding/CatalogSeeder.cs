using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PolvorApp.FederationCatalog.Assignments;
using PolvorApp.FederationCatalog.Comparsas;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.FederationCatalog.WeaponModels;
using PolvorApp.SharedKernel.Hosting;
using PolvorApp.SharedKernel.Seeding;

namespace PolvorApp.FederationCatalog.Seeding;

/// <summary>
/// Synthetic catalogue for development, staging and E2E tests (spec: Synthetic catalogue data,
/// SEC-11, design D7): fictional comparsas of both sides (one inactive), assignments for the
/// seeded FiringChiefs (one comparsa with two chiefs, one chief with two comparsas) and a weapon
/// catalogue with every kind. Fixed identifiers; existing rows are left untouched and a row whose
/// name, label or combination another row already uses is skipped with a warning, so it can run
/// again. Everything is saved at once, so a failure leaves nothing behind. Like the identity
/// seeder it writes no audit entries (it is not a user action). The assignments rely on the
/// identity seeder (order 10) having created the two FiringChiefs; there is no cross-module key.
/// Real comparsas and models are entered by an Admin (maintainer decision).
/// </summary>
internal sealed partial class CatalogSeeder(
    FederationCatalogDbContext db, TimeProvider time, IHostEnvironment environment, ILogger<CatalogSeeder> logger) : IDataSeeder
{
    /// <summary>"Jefe Sintético Uno" of the identity seeder, which this module cannot reference; a test keeps it equal.</summary>
    internal static readonly Guid JefeUno = new("0193a000-0000-7000-8000-000000000002");

    /// <summary>"Jefa Sintética Dos" of the identity seeder; a test keeps it equal.</summary>
    internal static readonly Guid JefaDos = new("0193a000-0000-7000-8000-000000000003");

    internal static readonly Guid Norte = new("0193a100-0000-7000-8000-000000000001");
    internal static readonly Guid Sur = new("0193a100-0000-7000-8000-000000000002");
    private static readonly Guid Este = new("0193a100-0000-7000-8000-000000000003");
    private static readonly Guid Oeste = new("0193a100-0000-7000-8000-000000000004");

    private static readonly IReadOnlyList<(Guid Id, string Name, Side Side, bool Active)> Comparsas =
    [
        (Norte, "Comparsa Sintética Norte", Side.Christian, true),
        (Sur, "Comparsa Sintética Sur", Side.Moorish, true),
        (Este, "Comparsa Sintética Este", Side.Christian, true),
        (Oeste, "Comparsa Sintética Oeste", Side.Moorish, false),
    ];

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

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await RefuseRealDataOutsideLocalAsync(cancellationToken);
        var now = time.GetUtcNow();
        await AddComparsasAsync(now, cancellationToken);
        await AddAssignmentsAsync(now, cancellationToken);
        await AddModelsAsync(now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        LogSeeded(logger, Comparsas.Count, Assignments.Count, Models.Count);
    }

    private async Task AddComparsasAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await db.Comparsas.AsNoTracking().Select(c => new { c.Id, c.Name }).ToListAsync(cancellationToken);
        var ids = existing.Select(c => c.Id).ToHashSet();
        var names = existing.Select(c => c.Name.ToLowerInvariant()).ToHashSet();
        foreach (var (id, name, side, active) in Comparsas.Where(c => !ids.Contains(c.Id)))
        {
            if (!names.Add(name.ToLowerInvariant()))
            {
                LogTaken(logger, "comparsa", id);
                continue;
            }

            db.Comparsas.Add(new Comparsa { Id = id, Name = name, Side = side, Active = active, CreatedAt = now });
        }
    }

    private async Task AddAssignmentsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = (await db.Assignments.AsNoTracking().Select(a => new { a.ComparsaId, a.UserId }).ToListAsync(cancellationToken))
            .Select(a => (a.ComparsaId, a.UserId))
            .ToHashSet();
        var comparsas = db.ChangeTracker.Entries<Comparsa>().Select(e => e.Entity.Id)
            .Concat(await db.Comparsas.AsNoTracking().Select(c => c.Id).ToListAsync(cancellationToken))
            .ToHashSet();
        foreach (var (comparsaId, userId) in Assignments.Where(a => !existing.Contains(a) && comparsas.Contains(a.ComparsaId)))
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
                Rentable = model.Kind != WeaponKind.Pistol,
                Label = model.Label,
                Active = model.Active,
                CreatedAt = now,
            });
        }
    }

    /// <summary>
    /// Staging may be reachable and must only ever hold synthetic data (NFR-13): before writing
    /// anything, refuse a database with any comparsa, weapon model or assignment this seeder
    /// would not have created, as the identity seeder does for users.
    /// </summary>
    private async Task RefuseRealDataOutsideLocalAsync(CancellationToken cancellationToken)
    {
        if (LocalEnvironments.IsLocal(environment))
        {
            return;
        }

        var comparsaIds = Comparsas.Select(c => c.Id).ToList();
        var modelIds = Models.Select(m => m.Id).ToList();
        var userIds = new List<Guid> { JefeUno, JefaDos };
        if (await db.Comparsas.AnyAsync(c => !comparsaIds.Contains(c.Id), cancellationToken)
            || await db.WeaponModels.AnyAsync(m => !modelIds.Contains(m.Id), cancellationToken)
            || await db.Assignments.AnyAsync(a => !comparsaIds.Contains(a.ComparsaId) || !userIds.Contains(a.UserId), cancellationToken))
        {
            throw new InvalidOperationException("The database holds catalogue data that is not synthetic; refusing to seed it (NFR-13).");
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Synthetic catalogue ensured: {Comparsas} comparsas, {Assignments} assignments, {Models} weapon models")]
    private static partial void LogSeeded(ILogger logger, int comparsas, int assignments, int models);

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
