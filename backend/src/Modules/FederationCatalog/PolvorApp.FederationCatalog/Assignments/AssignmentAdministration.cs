using Microsoft.EntityFrameworkCore;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.FederationCatalog.Assignments;

/// <summary>
/// FiringChief assignments by Admins (spec: FiringChief assignments). The user lives in the identity
/// module and is looked up through <see cref="IUserDirectory"/> before the transaction, so no lock is
/// held while waiting on another connection; the comparsa is then share-locked, so it cannot be
/// deactivated or deleted between the check and the insert (design D4). An existing assignment is
/// always a success; only new ones must be eligible. Missing removals change nothing.
/// </summary>
internal sealed class AssignmentAdministration(
    FederationCatalogDbContext db, IUserDirectory users, IAuditTrail trail, TimeProvider time)
{
    public const string EntityType = "FiringChiefAssignment";

    public async Task<CatalogOutcome> AssignAsync(Guid comparsaId, Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.FindAsync(userId, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var comparsa = await db.LockComparsaForShareAsync(comparsaId, cancellationToken);
        if (comparsa is null)
        {
            return CatalogOutcome.ComparsaNotFound;
        }

        if (user is null)
        {
            return CatalogOutcome.UserNotFound;
        }

        if (await ExistsAsync(comparsaId, userId, cancellationToken))
        {
            return CatalogOutcome.Done;
        }

        var rejection = user switch
        {
            { Role: not UserRole.FiringChief } => CatalogOutcome.NotFiringChief,
            { Status: UserStatus.Deactivated } => CatalogOutcome.UserDeactivated,
            _ when !comparsa.Active => CatalogOutcome.ComparsaInactive,
            _ => (CatalogOutcome?)null,
        };
        if (rejection is { } blocked)
        {
            return blocked;
        }

        db.Assignments.Add(new FiringChiefAssignment { ComparsaId = comparsaId, UserId = userId, AssignedAt = time.GetUtcNow() });
        Record("FiringChiefAssigned", comparsaId, userId);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (CatalogProblems.IsUniqueViolation(exception, FederationCatalogDbContext.AssignmentKey))
        {
            // A concurrent identical request won: the assignment exists once, recorded once.
            // Return at once: the transaction is aborted and rolls back on dispose.
            db.ChangeTracker.Clear();
            return CatalogOutcome.Done;
        }

        await transaction.CommitAsync(cancellationToken);
        return CatalogOutcome.Done;
    }

    /// <summary>
    /// Removes an assignment. An unknown user is 404 like an unknown comparsa (spec); users are
    /// never deleted, so an assignment can never outlive its user.
    /// </summary>
    public async Task<CatalogOutcome> UnassignAsync(Guid comparsaId, Guid userId, CancellationToken cancellationToken)
    {
        if (!await db.Comparsas.AnyAsync(c => c.Id == comparsaId, cancellationToken))
        {
            return CatalogOutcome.ComparsaNotFound;
        }

        if (await users.FindAsync(userId, cancellationToken) is null)
        {
            return CatalogOutcome.UserNotFound;
        }

        var assignment = await db.Assignments.SingleOrDefaultAsync(a => a.ComparsaId == comparsaId && a.UserId == userId, cancellationToken);
        if (assignment is null)
        {
            return CatalogOutcome.Done;
        }

        db.Assignments.Remove(assignment);
        Record("FiringChiefUnassigned", comparsaId, userId);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent identical request removed it first: nothing left to change or record.
            db.ChangeTracker.Clear();
        }

        return CatalogOutcome.Done;
    }

    private Task<bool> ExistsAsync(Guid comparsaId, Guid userId, CancellationToken cancellationToken) =>
        db.Assignments.AnyAsync(a => a.ComparsaId == comparsaId && a.UserId == userId, cancellationToken);

    private void Record(string action, Guid comparsaId, Guid userId) =>
        trail.Record(db, new AuditRecord(action, EntityType, userId.ToString(), new { userId }, ComparsaId: comparsaId));
}
