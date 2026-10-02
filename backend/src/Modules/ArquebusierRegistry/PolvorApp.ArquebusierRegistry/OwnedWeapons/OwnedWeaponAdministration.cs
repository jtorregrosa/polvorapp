using Microsoft.EntityFrameworkCore;
using PolvorApp.ArquebusierRegistry.Arquebusiers;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.ArquebusierRegistry.OwnedWeapons;

/// <summary>
/// Writes to owned weapons (spec: Owned weapons, UC-04; design D5, D10). Each write first takes
/// <c>FOR KEY SHARE</c> on the owner within the caller's scope, so it never lands on an arquebusier
/// that a concurrent transfer or deletion just took out of that scope (BR-12). Audit entries carry
/// the model and the changed field names, never the numbers (D7).
/// </summary>
internal sealed class OwnedWeaponAdministration(
    ArquebusierRegistryDbContext db,
    IComparsaScope scope,
    ICatalogDirectory catalog,
    IAuditTrail trail,
    TimeProvider time,
    RegistryWriteGuard guard)
{
    public const string EntityType = "OwnedWeapon";

    public Task<(RegistryOutcome Outcome, OwnedWeapon? Weapon)> AddAsync(
        Guid arquebusierId, OwnedWeaponInput input, CancellationToken cancellationToken) =>
        guard.RunAsync<OwnedWeapon>(nameof(AddAsync), arquebusierId, null, async () =>
        {
            // Catalog and scope lookups run before the transaction: no row lock is held while they
            // run and a request never needs two pooled connections at once. The foreign key decides races.
            var access = await scope.GetAccessAsync(cancellationToken);
            var modelProblem = await ModelProblemAsync(input.WeaponModelId, cancellationToken);
            await using var transaction = await guard.BeginWriteAsync(cancellationToken);
            if (await OwnerComparsaAsync(arquebusierId, access, cancellationToken) is not { } comparsaId)
            {
                return (RegistryOutcome.ArquebusierNotFound, null);
            }

            if (modelProblem is { } problem)
            {
                return (problem, null);
            }

            if (await GuideTakenAsync(input.OwnershipGuideNumber, exceptId: null, cancellationToken))
            {
                return (RegistryOutcome.OwnershipGuideTaken, null);
            }

            var now = time.GetUtcNow();
            var weapon = new OwnedWeapon
            {
                Id = Guid.CreateVersion7(now),
                ArquebusierId = arquebusierId,
                WeaponModelId = input.WeaponModelId,
                WeaponNumber = input.WeaponNumber,
                OwnershipGuideNumber = input.OwnershipGuideNumber,
                CreatedAt = now,
            };
            db.OwnedWeapons.Add(weapon);
            Record("OwnedWeaponAdded", weapon, comparsaId, new { arquebusierId, weaponModelId = weapon.WeaponModelId });
            return await CommitAsync(weapon, RegistryOutcome.OwnedWeaponModified, transaction, cancellationToken);
        });

    /// <summary>Replaces a weapon's model and numbers if <paramref name="version"/> is still current.</summary>
    public Task<(RegistryOutcome Outcome, OwnedWeapon? Weapon)> UpdateAsync(
        Guid arquebusierId, Guid weaponId, OwnedWeaponInput input, uint version, CancellationToken cancellationToken) =>
        guard.RunAsync<OwnedWeapon>(nameof(UpdateAsync), arquebusierId, weaponId, async () =>
        {
            var access = await scope.GetAccessAsync(cancellationToken);
            var modelProblem = await ModelProblemAsync(input.WeaponModelId, cancellationToken);
            await using var transaction = await guard.BeginWriteAsync(cancellationToken);
            if (await OwnerComparsaAsync(arquebusierId, access, cancellationToken) is not { } comparsaId)
            {
                return (RegistryOutcome.ArquebusierNotFound, null);
            }

            var weapon = await db.OwnedWeapons.SingleOrDefaultAsync(w => w.Id == weaponId && w.ArquebusierId == arquebusierId, cancellationToken);
            if (weapon is null)
            {
                return (RegistryOutcome.OwnedWeaponNotFound, null);
            }

            if (weapon.Version != version)
            {
                return (RegistryOutcome.OwnedWeaponModified, null);
            }

            var changedFields = new (string Field, bool Changed)[]
            {
                ("weaponModelId", weapon.WeaponModelId != input.WeaponModelId),
                ("weaponNumber", weapon.WeaponNumber != input.WeaponNumber),
                ("ownershipGuideNumber", weapon.OwnershipGuideNumber != input.OwnershipGuideNumber),
            }.Where(c => c.Changed).Select(c => c.Field).ToList();
            if (changedFields.Count == 0)
            {
                return (RegistryOutcome.Done, weapon);
            }

            // An existing weapon keeps a model deactivated later; only a new choice must be active.
            if (weapon.WeaponModelId != input.WeaponModelId && modelProblem is { } problem)
            {
                return (problem, null);
            }

            if (await GuideTakenAsync(input.OwnershipGuideNumber, exceptId: weaponId, cancellationToken))
            {
                return (RegistryOutcome.OwnershipGuideTaken, null);
            }

            (weapon.WeaponModelId, weapon.WeaponNumber, weapon.OwnershipGuideNumber) = (input.WeaponModelId, input.WeaponNumber, input.OwnershipGuideNumber);
            Record("OwnedWeaponUpdated", weapon, comparsaId, new { arquebusierId, changedFields });
            return await CommitAsync(weapon, RegistryOutcome.OwnedWeaponModified, transaction, cancellationToken);
        });

    public Task<(RegistryOutcome Outcome, OwnedWeapon? Weapon)> RemoveAsync(
        Guid arquebusierId, Guid weaponId, CancellationToken cancellationToken) =>
        guard.RunAsync<OwnedWeapon>(nameof(RemoveAsync), arquebusierId, weaponId, async () =>
        {
            var access = await scope.GetAccessAsync(cancellationToken);
            await using var transaction = await guard.BeginWriteAsync(cancellationToken);
            if (await OwnerComparsaAsync(arquebusierId, access, cancellationToken) is not { } comparsaId)
            {
                return (RegistryOutcome.ArquebusierNotFound, null);
            }

            var weapon = await db.OwnedWeapons.SingleOrDefaultAsync(w => w.Id == weaponId && w.ArquebusierId == arquebusierId, cancellationToken);
            if (weapon is null)
            {
                return (RegistryOutcome.OwnedWeaponNotFound, null);
            }

            db.OwnedWeapons.Remove(weapon);
            Record("OwnedWeaponRemoved", weapon, comparsaId, new { arquebusierId, weaponModelId = weapon.WeaponModelId });

            // Weapon rows are not locked: a concurrent removal of the same weapon makes this one a 404.
            return await CommitAsync(weapon, RegistryOutcome.OwnedWeaponNotFound, transaction, cancellationToken);
        });

    /// <summary>Locks the owner <c>FOR KEY SHARE</c> within the scope and returns its comparsa; null when out of reach.</summary>
    private async Task<Guid?> OwnerComparsaAsync(Guid arquebusierId, ComparsaAccess access, CancellationToken cancellationToken) =>
        await db.LockArquebusierForKeyShareAsync(arquebusierId, access, cancellationToken)
            ? await db.Arquebusiers.Where(a => a.Id == arquebusierId).Select(a => a.ComparsaId).SingleAsync(cancellationToken)
            : null;

    /// <summary>A new model choice must exist (400) and be active (409).</summary>
    private async Task<RegistryOutcome?> ModelProblemAsync(Guid weaponModelId, CancellationToken cancellationToken) =>
        await catalog.FindWeaponModelAsync(weaponModelId, cancellationToken) switch
        {
            null => RegistryOutcome.WeaponModelNotFound,
            { Active: false } => RegistryOutcome.WeaponModelInactive,
            _ => null,
        };

    /// <summary>Spec "Owned weapons": the guide is unique across the Federation; the answer never says whose.</summary>
    private Task<bool> GuideTakenAsync(string guide, Guid? exceptId, CancellationToken cancellationToken) =>
        db.OwnedWeapons.AsNoTracking().AnyAsync(w => w.OwnershipGuideNumber == guide && w.Id != exceptId, cancellationToken);

    private async Task<(RegistryOutcome Outcome, OwnedWeapon? Weapon)> CommitAsync(
        OwnedWeapon weapon, RegistryOutcome onConcurrentChange, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another request changed or removed the weapon after it was loaded (its xmin moved).
            db.ChangeTracker.Clear();
            return (onConcurrentChange, null);
        }
        catch (DbUpdateException exception) when (RegistryProblems.ViolatedConstraint(exception) is { } constraint
            && OutcomeOf(constraint) is { } outcome)
        {
            db.ChangeTracker.Clear();
            guard.LostRace(weapon.Id, constraint);
            return (outcome, null);
        }

        await transaction.CommitAsync(cancellationToken);
        return (RegistryOutcome.Done, weapon);
    }

    private static RegistryOutcome? OutcomeOf(string constraint) => constraint switch
    {
        ArquebusierRegistryDbContext.OwnershipGuideIndex => RegistryOutcome.OwnershipGuideTaken,

        // The model or the owner was deleted after the checks (design D10, locking rules).
        ArquebusierRegistryDbContext.WeaponModelForeignKey => RegistryOutcome.WeaponModelNotFound,
        ArquebusierRegistryDbContext.ArquebusierForeignKey => RegistryOutcome.ArquebusierNotFound, // defensive: the parent lock prevents it
        _ => null,
    };

    private void Record(string action, OwnedWeapon weapon, Guid comparsaId, object data) =>
        trail.Record(db, new AuditRecord(action, EntityType, weapon.Id.ToString(), data, ComparsaId: comparsaId));
}
