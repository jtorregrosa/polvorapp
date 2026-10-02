using Microsoft.EntityFrameworkCore;
using Npgsql;
using PolvorApp.FederationCatalog.Comparsas;
using PolvorApp.FederationCatalog.WeaponModels;

namespace PolvorApp.FederationCatalog.Persistence;

/// <summary>
/// Row locks of the catalogue (design D4/D10). They must run inside an explicit transaction: the
/// lock lasts until it ends, and a stuck lock fails the request after 5 s instead of holding the
/// connection. The table name is literal SQL (identifiers cannot be parameters); the catalogue
/// tests exercise every lock, so a rename fails them.
/// </summary>
internal static class CatalogLocks
{
    /// <summary>
    /// For a change that keeps the key (edit, deactivate): <c>FOR NO KEY UPDATE</c> serialises it
    /// with other changes and with assignments (which share-lock the row), but does not block
    /// inserts of rows that reference the comparsa by foreign key.
    /// </summary>
    public static Task<Comparsa?> LockComparsaForChangeAsync(this FederationCatalogDbContext db, Guid id, CancellationToken cancellationToken) =>
        LockAsync(db, db.Comparsas.FromSql($"SELECT * FROM catalog.comparsas WHERE id = {id} FOR NO KEY UPDATE"), cancellationToken);

    /// <summary>For a deletion: <c>FOR UPDATE</c> also blocks new references until it commits (design D10).</summary>
    public static Task<Comparsa?> LockComparsaForDeleteAsync(this FederationCatalogDbContext db, Guid id, CancellationToken cancellationToken) =>
        LockAsync(db, db.Comparsas.FromSql($"SELECT * FROM catalog.comparsas WHERE id = {id} FOR UPDATE"), cancellationToken);

    /// <summary>
    /// The comparsa, protected from changes and deletion while other readers may share it (an
    /// assignment checks it is active, then inserts); read-only, null when it does not exist.
    /// </summary>
    public static Task<Comparsa?> LockComparsaForShareAsync(this FederationCatalogDbContext db, Guid id, CancellationToken cancellationToken) =>
        LockAsync(db, db.Comparsas.FromSql($"SELECT * FROM catalog.comparsas WHERE id = {id} FOR SHARE").AsNoTracking(), cancellationToken);

    /// <summary>For an edit or (de)activation of a weapon model; see <see cref="LockComparsaForChangeAsync"/>.</summary>
    public static Task<WeaponModel?> LockWeaponModelForChangeAsync(this FederationCatalogDbContext db, Guid id, CancellationToken cancellationToken) =>
        LockAsync(db, db.WeaponModels.FromSql($"SELECT * FROM catalog.weapon_models WHERE id = {id} FOR NO KEY UPDATE"), cancellationToken);

    /// <summary>For a deletion of a weapon model; see <see cref="LockComparsaForDeleteAsync"/>.</summary>
    public static Task<WeaponModel?> LockWeaponModelForDeleteAsync(this FederationCatalogDbContext db, Guid id, CancellationToken cancellationToken) =>
        LockAsync(db, db.WeaponModels.FromSql($"SELECT * FROM catalog.weapon_models WHERE id = {id} FOR UPDATE"), cancellationToken);

    /// <summary>
    /// Whether <paramref name="exception"/> is a lock wait that hit the 5 s timeout, or the database
    /// aborting one side of a deadlock: both are retryable, answered as busy (add-comparsa-logos).
    /// </summary>
    public static bool IsRetryable(Exception exception) =>
        (exception as PostgresException ?? exception.InnerException as PostgresException)?.SqlState
            is PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.DeadlockDetected;

    /// <remarks>
    /// Tracked results come back fresh only if the context has not loaded the row before in this
    /// scope; every caller locks first, before any other read of the row.
    /// </remarks>
    private static async Task<T?> LockAsync<T>(FederationCatalogDbContext db, IQueryable<T> query, CancellationToken cancellationToken)
        where T : class
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Catalogue row locks need an explicit transaction.");
        }

        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", cancellationToken);
        return await query.SingleOrDefaultAsync(cancellationToken);
    }
}
