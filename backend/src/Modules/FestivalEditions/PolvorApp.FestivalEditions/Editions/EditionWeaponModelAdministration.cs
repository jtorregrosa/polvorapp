using Microsoft.EntityFrameworkCore;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions.Persistence;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.FestivalEditions.Editions;

/// <summary>
/// The rental models offered in an edition (spec: Rental models offered in an edition (BR-07)). The
/// set is saved as a whole; only models added to it must be active and rentable, so a model retired
/// after being offered can stay until an Admin removes it.
/// </summary>
internal sealed class EditionWeaponModelAdministration(
    FestivalEditionsDbContext db, ICatalogDirectory catalog, IAuditTrail trail, EditionWriteGuard guard)
{
    /// <summary>Most models a set may name.</summary>
    public const int MaxModels = 100;

    public const string NotFound = "notFound";
    public const string NotRentable = "notRentable";

    /// <summary>Replaces the set; the last save wins (spec: Edition management by Admins).</summary>
    public Task<EditionWrite> SetAsync(Guid id, IReadOnlyCollection<Guid> weaponModelIds, CancellationToken cancellationToken) =>
        guard.RunAsync(nameof(SetAsync), id, async () =>
        {
            await using var transaction = await db.BeginWriteAsync(cancellationToken);
            if (!await db.LockEditionAsync(id, cancellationToken))
            {
                return EditionWrite.Failed(EditionOutcome.NotFound);
            }

            var wanted = weaponModelIds.ToHashSet();
            var current = await db.EditionWeaponModels.Where(m => m.EditionId == id).ToListAsync(cancellationToken);
            var currentIds = current.Select(m => m.WeaponModelId).ToHashSet();
            var added = wanted.Except(currentIds).Order().ToList();
            var removed = current.Where(m => !wanted.Contains(m.WeaponModelId)).ToList();

            if (await RejectionAsync(added, cancellationToken) is { } reason)
            {
                return Rejected(reason);
            }

            var edition = await db.Editions.SingleAsync(e => e.Id == id, cancellationToken);
            if (added.Count == 0 && removed.Count == 0)
            {
                return EditionWrite.Done(edition);
            }

            db.EditionWeaponModels.RemoveRange(removed);
            db.EditionWeaponModels.AddRange(added.Select(m => new EditionWeaponModel { EditionId = id, WeaponModelId = m }));
            trail.Record(db, new AuditRecord(
                "EditionWeaponModelsChanged",
                EditionAdministration.EntityType,
                id.ToString(),
                new { added, removed = removed.Select(m => m.WeaponModelId).Order().ToList() }));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (EditionProblems.IsForeignKeyViolation(exception, FestivalEditionsDbContext.WeaponModelForeignKey))
            {
                // A model was deleted from the catalogue after the check: it no longer exists.
                db.ChangeTracker.Clear();
                guard.LostRace(id, FestivalEditionsDbContext.WeaponModelForeignKey);
                return Rejected(NotFound);
            }

            await transaction.CommitAsync(cancellationToken);

            // The set lives in its own table: the edition row and its version are unchanged.
            return EditionWrite.Done(edition);
        });

    private static EditionWrite Rejected(string reason) =>
        EditionWrite.Invalid(new Dictionary<string, string> { ["weaponModelIds"] = reason });

    /// <summary>Why the added models cannot be offered: one does not exist, or is inactive or not rentable.</summary>
    private async Task<string?> RejectionAsync(List<Guid> added, CancellationToken cancellationToken)
    {
        if (added.Count == 0)
        {
            return null;
        }

        var models = await catalog.FindWeaponModelsAsync(added, cancellationToken);
        if (added.Except(models.Select(m => m.Id)).Any())
        {
            return NotFound;
        }

        return models.All(m => m.Active && m.Rentable) ? null : NotRentable;
    }
}
