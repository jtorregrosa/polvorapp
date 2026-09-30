using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.FederationCatalog.Comparsas;

/// <summary>A validated create or edit request.</summary>
/// <param name="Name">Trimmed, NFC-normalised name.</param>
/// <param name="Side">Side of the comparsa.</param>
internal sealed record ComparsaInput(string Name, Side Side);

/// <summary>
/// Comparsa management by Admins (specs: Comparsas, Comparsa management by Admins, Deleting
/// comparsas and weapon models). Every change is audited in the same transaction; an operation
/// that changes nothing records nothing. Changes to an existing comparsa lock its row first
/// (design D4/D10), so concurrent changes serialise, the audited "previous" values are the ones
/// actually replaced, and a comparsa deleted meanwhile is simply not found.
/// </summary>
internal sealed partial class ComparsaAdministration(
    FederationCatalogDbContext db,
    IAuditTrail trail,
    TimeProvider time,
    IEnumerable<ICatalogUsage> usages,
    ILogger<ComparsaAdministration> logger)
{
    public const string EntityType = "Comparsa";

    public async Task<(CatalogOutcome Outcome, Comparsa? Comparsa)> CreateAsync(ComparsaInput input, CancellationToken cancellationToken)
    {
        var comparsa = new Comparsa { Id = Guid.CreateVersion7(time.GetUtcNow()), Name = input.Name, Side = input.Side, CreatedAt = time.GetUtcNow() };
        db.Comparsas.Add(comparsa);
        Record("ComparsaCreated", comparsa, new { name = input.Name, side = input.Side });
        return await SaveAsync(comparsa, cancellationToken);
    }

    public async Task<(CatalogOutcome Outcome, Comparsa? Comparsa)> UpdateAsync(Guid id, ComparsaInput input, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var comparsa = await db.LockComparsaForChangeAsync(id, cancellationToken);
        if (comparsa is null)
        {
            return (CatalogOutcome.ComparsaNotFound, null);
        }

        if (comparsa.Name == input.Name && comparsa.Side == input.Side)
        {
            return (CatalogOutcome.Done, comparsa);
        }

        var previous = new { name = comparsa.Name, side = comparsa.Side };
        comparsa.Name = input.Name;
        comparsa.Side = input.Side;
        Record("ComparsaUpdated", comparsa, new { previous, current = new { name = input.Name, side = input.Side } });
        return await CommitAsync(transaction, await SaveAsync(comparsa, cancellationToken), cancellationToken);
    }

    /// <summary>Deactivates or reactivates a comparsa; assignments are kept either way.</summary>
    public async Task<(CatalogOutcome Outcome, Comparsa? Comparsa)> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var comparsa = await db.LockComparsaForChangeAsync(id, cancellationToken);
        if (comparsa is null)
        {
            return (CatalogOutcome.ComparsaNotFound, null);
        }

        if (comparsa.Active == active)
        {
            return (CatalogOutcome.Done, comparsa);
        }

        comparsa.Active = active;
        Record(active ? "ComparsaReactivated" : "ComparsaDeactivated", comparsa);
        return await CommitAsync(transaction, await SaveAsync(comparsa, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Deletes an unused comparsa and its assignments. The row is locked before the usage checks
    /// and the audit snapshot, so a concurrent assignment is either blocked or listed (design D10).
    /// </summary>
    public async Task<CatalogOutcome> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var comparsa = await db.LockComparsaForDeleteAsync(id, cancellationToken);
        if (comparsa is null)
        {
            return CatalogOutcome.ComparsaNotFound;
        }

        foreach (var usage in usages)
        {
            if (await usage.IsComparsaInUseAsync(id, cancellationToken))
            {
                return CatalogOutcome.ComparsaInUse;
            }
        }

        var unassignedUserIds = await db.Assignments.Where(a => a.ComparsaId == id).Select(a => a.UserId).ToListAsync(cancellationToken);
        db.Comparsas.Remove(comparsa);
        Record("ComparsaDeleted", comparsa, new { name = comparsa.Name, side = comparsa.Side, active = comparsa.Active, unassignedUserIds });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation } reference)
        {
            // A later module references comparsas by foreign key but registered no usage check:
            // still blocked, but the missing check is a bug worth seeing in the logs.
            db.ChangeTracker.Clear();
            LogReferencedWithoutUsageCheck(logger, id, reference.TableName, reference.ConstraintName);
            return CatalogOutcome.ComparsaInUse;
        }

        await transaction.CommitAsync(cancellationToken);
        return CatalogOutcome.Done;
    }

    private void Record(string action, Comparsa comparsa, object? data = null) =>
        trail.Record(db, new AuditRecord(action, EntityType, comparsa.Id.ToString(), data, ComparsaId: comparsa.Id));

    /// <summary>Saves the change with its audit entry; a lost race on the unique name is blocking.</summary>
    private async Task<(CatalogOutcome Outcome, Comparsa? Comparsa)> SaveAsync(Comparsa comparsa, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return (CatalogOutcome.Done, comparsa);
        }
        catch (DbUpdateException exception) when (CatalogProblems.IsUniqueViolation(exception, FederationCatalogDbContext.ComparsaNameIndex))
        {
            // The failed change and its audit entry must never be saved by a later SaveChanges.
            db.ChangeTracker.Clear();
            return (CatalogOutcome.NameTaken, null);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Comparsa {ComparsaId} is referenced by {Table} ({Constraint}) but no usage check reported it.")]
    private static partial void LogReferencedWithoutUsageCheck(ILogger logger, Guid comparsaId, string? table, string? constraint);

    private static async Task<(CatalogOutcome Outcome, Comparsa? Comparsa)> CommitAsync(
        IDbContextTransaction transaction, (CatalogOutcome Outcome, Comparsa? Comparsa) result, CancellationToken cancellationToken)
    {
        if (result.Outcome == CatalogOutcome.Done)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return result;
    }
}
