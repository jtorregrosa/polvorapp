using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.FederationCatalog.WeaponModels;

/// <summary>A validated create or edit request (spec: Weapon models).</summary>
/// <param name="Kind">Weapon kind.</param>
/// <param name="Side">Side; null only for a pistol.</param>
/// <param name="Handedness">Handedness; null only for a pistol.</param>
/// <param name="Size">Size; null only for a pistol.</param>
/// <param name="Rentable">False for a pistol (BR-07).</param>
/// <param name="Label">Trimmed, NFC-normalised Federation label.</param>
internal sealed record WeaponModelInput(WeaponKind Kind, Side? Side, Handedness? Handedness, WeaponSize? Size, bool Rentable, string Label);

/// <summary>
/// Weapon catalogue management by Admins (specs: Weapon models, Weapon catalogue access, Deleting
/// comparsas and weapon models). Same rules as comparsas: audited in the same transaction, no-ops
/// not recorded, existing rows locked before a change (design D4/D10).
/// </summary>
internal sealed partial class WeaponModelAdministration(
    FederationCatalogDbContext db,
    IAuditTrail trail,
    TimeProvider time,
    IEnumerable<ICatalogUsage> usages,
    ILogger<WeaponModelAdministration> logger)
{
    public const string EntityType = "WeaponModel";

    public async Task<(CatalogOutcome Outcome, WeaponModel? Model)> CreateAsync(WeaponModelInput input, CancellationToken cancellationToken)
    {
        var model = new WeaponModel { Id = Guid.CreateVersion7(time.GetUtcNow()), Kind = input.Kind, Label = input.Label, CreatedAt = time.GetUtcNow() };
        Apply(model, input);
        db.WeaponModels.Add(model);
        Record(FederationCatalogAuditActions.WeaponModelCreated, model, Snapshot(model));
        return await SaveAsync(model, cancellationToken);
    }

    public async Task<(CatalogOutcome Outcome, WeaponModel? Model)> UpdateAsync(Guid id, WeaponModelInput input, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var model = await db.LockWeaponModelForChangeAsync(id, cancellationToken);
        if (model is null)
        {
            return (CatalogOutcome.WeaponModelNotFound, null);
        }

        var previous = EditableFields(model);
        var current = new EditableFieldsSnapshot(input.Kind, input.Side, input.Handedness, input.Size, input.Rentable, input.Label);
        if (previous == current)
        {
            return (CatalogOutcome.Done, model);
        }

        Apply(model, input);
        Record(FederationCatalogAuditActions.WeaponModelUpdated, model, new { previous, current });
        return await CommitAsync(transaction, await SaveAsync(model, cancellationToken), cancellationToken);
    }

    public async Task<(CatalogOutcome Outcome, WeaponModel? Model)> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var model = await db.LockWeaponModelForChangeAsync(id, cancellationToken);
        if (model is null)
        {
            return (CatalogOutcome.WeaponModelNotFound, null);
        }

        if (model.Active == active)
        {
            return (CatalogOutcome.Done, model);
        }

        model.Active = active;
        Record(active ? FederationCatalogAuditActions.WeaponModelReactivated : FederationCatalogAuditActions.WeaponModelDeactivated, model);
        return await CommitAsync(transaction, await SaveAsync(model, cancellationToken), cancellationToken);
    }

    /// <summary>Deletes an unused model; the row is locked before the usage checks (design D10).</summary>
    public async Task<CatalogOutcome> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var model = await db.LockWeaponModelForDeleteAsync(id, cancellationToken);
        if (model is null)
        {
            return CatalogOutcome.WeaponModelNotFound;
        }

        foreach (var usage in usages)
        {
            if (await usage.IsWeaponModelInUseAsync(id, cancellationToken))
            {
                return CatalogOutcome.WeaponModelInUse;
            }
        }

        db.WeaponModels.Remove(model);
        Record(FederationCatalogAuditActions.WeaponModelDeleted, model, Snapshot(model));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation } reference)
        {
            // A later module references models by foreign key but registered no usage check: still
            // blocked, but the missing check is a bug worth seeing in the logs.
            db.ChangeTracker.Clear();
            LogReferencedWithoutUsageCheck(logger, id, reference.TableName, reference.ConstraintName);
            return CatalogOutcome.WeaponModelInUse;
        }

        await transaction.CommitAsync(cancellationToken);
        return CatalogOutcome.Done;
    }

    private static void Apply(WeaponModel model, WeaponModelInput input)
    {
        model.Kind = input.Kind;
        model.Side = input.Side;
        model.Handedness = input.Handedness;
        model.Size = input.Size;
        model.Rentable = input.Rentable;
        model.Label = input.Label;
    }

    /// <summary>The whole model, for the created and deleted entries.</summary>
    private static ModelSnapshot Snapshot(WeaponModel model) =>
        new(model.Kind, model.Side, model.Handedness, model.Size, model.Rentable, model.Label, model.Active);

    /// <summary>What an edit can change; a record, so two snapshots compare by value.</summary>
    private static EditableFieldsSnapshot EditableFields(WeaponModel model) =>
        new(model.Kind, model.Side, model.Handedness, model.Size, model.Rentable, model.Label);

    private void Record(string action, WeaponModel model, object? data = null) =>
        trail.Record(db, new AuditRecord(action, EntityType, model.Id.ToString(), data));

    /// <summary>Saves the change with its audit entry; a lost race on a unique index is blocking.</summary>
    private async Task<(CatalogOutcome Outcome, WeaponModel? Model)> SaveAsync(WeaponModel model, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return (CatalogOutcome.Done, model);
        }
        catch (DbUpdateException exception) when (UniqueRuleBroken(exception) is { } outcome)
        {
            // The failed change and its audit entry must never be saved by a later SaveChanges.
            db.ChangeTracker.Clear();
            return (outcome, null);
        }
    }

    /// <summary>The blocking rule a unique violation broke, or null for any other failure (rethrown).</summary>
    private static CatalogOutcome? UniqueRuleBroken(DbUpdateException exception) =>
        CatalogProblems.IsUniqueViolation(exception, FederationCatalogDbContext.WeaponModelLabelIndex) ? CatalogOutcome.LabelTaken
        : CatalogProblems.IsUniqueViolation(exception, FederationCatalogDbContext.WeaponModelCombinationIndex) ? CatalogOutcome.CombinationTaken
        : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Weapon model {WeaponModelId} is referenced by {Table} ({Constraint}) but no usage check reported it.")]
    private static partial void LogReferencedWithoutUsageCheck(ILogger logger, Guid weaponModelId, string? table, string? constraint);

    private static async Task<(CatalogOutcome Outcome, WeaponModel? Model)> CommitAsync(
        IDbContextTransaction transaction, (CatalogOutcome Outcome, WeaponModel? Model) result, CancellationToken cancellationToken)
    {
        if (result.Outcome == CatalogOutcome.Done)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return result;
    }

    /// <param name="Kind">Weapon kind.</param>
    /// <param name="Side">Side.</param>
    /// <param name="Handedness">Handedness.</param>
    /// <param name="Size">Size.</param>
    /// <param name="Rentable">Rentable flag.</param>
    /// <param name="Label">Federation label.</param>
    /// <param name="Active">Active flag.</param>
    private sealed record ModelSnapshot(WeaponKind Kind, Side? Side, Handedness? Handedness, WeaponSize? Size, bool Rentable, string Label, bool Active);

    /// <param name="Kind">Weapon kind.</param>
    /// <param name="Side">Side.</param>
    /// <param name="Handedness">Handedness.</param>
    /// <param name="Size">Size.</param>
    /// <param name="Rentable">Rentable flag.</param>
    /// <param name="Label">Federation label.</param>
    private sealed record EditableFieldsSnapshot(WeaponKind Kind, Side? Side, Handedness? Handedness, WeaponSize? Size, bool Rentable, string Label);
}
