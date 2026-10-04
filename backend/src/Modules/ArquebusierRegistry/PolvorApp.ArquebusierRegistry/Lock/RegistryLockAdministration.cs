using Microsoft.EntityFrameworkCore;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.ArquebusierRegistry.Lock;

/// <summary>
/// The registry lock (spec: Registry lock (BR-10, UC-11); add-festival-editions, design D8). Locking
/// takes the settings row <c>FOR UPDATE</c>, so it waits for FiringChief writes that read it
/// <c>FOR SHARE</c>, and every later FiringChief write sees it.
/// </summary>
internal sealed class RegistryLockAdministration(ArquebusierRegistryDbContext db, IAuditTrail trail, TimeProvider time, RegistryWriteGuard guard)
{
    public const string EntityType = "Registry";

    public Task<RegistrySettings> ReadAsync(CancellationToken cancellationToken) =>
        db.Settings.AsNoTracking().SingleAsync(s => s.Id == RegistrySettings.SingletonId, cancellationToken);

    /// <summary>Locks or unlocks; setting the current state changes and records nothing.</summary>
    public Task<(RegistryOutcome Outcome, RegistrySettings? Settings)> SetAsync(bool locked, CancellationToken cancellationToken) =>
        guard.RunAsync<RegistrySettings>(nameof(SetAsync), null, null, async () =>
        {
            await using var transaction = await db.BeginWriteAsync(cancellationToken);
            await db.Database
                .SqlQuery<int>($"SELECT 1 AS \"Value\" FROM registry.registry_settings WHERE id = {RegistrySettings.SingletonId} FOR UPDATE")
                .ToListAsync(cancellationToken);
            var settings = await db.Settings.SingleAsync(s => s.Id == RegistrySettings.SingletonId, cancellationToken);
            if (settings.Locked == locked)
            {
                return (RegistryOutcome.Done, settings);
            }

            settings.Locked = locked;
            settings.LockedChangedAt = time.GetUtcNow();
            trail.Record(db, new AuditRecord(locked ? ArquebusierRegistryAuditActions.RegistryLocked : ArquebusierRegistryAuditActions.RegistryUnlocked, EntityType));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (RegistryOutcome.Done, settings);
        });
}
