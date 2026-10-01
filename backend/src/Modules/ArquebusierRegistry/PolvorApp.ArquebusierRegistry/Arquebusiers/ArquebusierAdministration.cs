using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.ArquebusierRegistry.Arquebusiers;

/// <summary>
/// Writes to arquebusiers (specs: Registering and editing arquebusiers, Federation-wide uniqueness,
/// Transfer, Deleting; design D5, D10). Every write is scoped to the caller's comparsas (BR-12),
/// audited in the same transaction without personal values (D7), and backed by the database
/// constraints when it races with another request. Rejections are logged with ids only (D11).
/// </summary>
internal sealed partial class ArquebusierAdministration(
    ArquebusierRegistryDbContext db,
    IComparsaScope scope,
    ICurrentUser currentUser,
    ICatalogDirectory catalog,
    IAuditTrail trail,
    TimeProvider time,
    ILogger<ArquebusierAdministration> logger)
{
    public const string EntityType = "Arquebusier";

    public Task<(RegistryOutcome Outcome, Arquebusier? Arquebusier)> RegisterAsync(
        Guid comparsaId, ArquebusierInput input, CancellationToken cancellationToken) =>
        GuardedAsync(nameof(RegisterAsync), null, async () =>
        {
            var access = await scope.GetAccessAsync(cancellationToken);
            if (!access.CanAccess(comparsaId))
            {
                return (RegistryOutcome.ComparsaNotFound, null);
            }

            var comparsa = await catalog.FindComparsaAsync(comparsaId, cancellationToken);
            if (comparsa is null)
            {
                return (RegistryOutcome.ComparsaNotFound, null);
            }

            if (!comparsa.Active)
            {
                return (RegistryOutcome.ComparsaInactive, null);
            }

            if (await DuplicateOfAsync(input, exceptId: null, cancellationToken) is { } duplicate)
            {
                return (duplicate, null);
            }

            var now = time.GetUtcNow();
            var arquebusier = new Arquebusier
            {
                Id = Guid.CreateVersion7(now),
                ComparsaId = comparsaId,
                FederationId = input.FederationId,
                NationalId = input.NationalId,
                FirstName = input.FirstName,
                LastName = input.LastName,
                BirthDate = input.BirthDate,
                Gender = input.Gender,
                CreatedAt = now,
            };
            Apply(arquebusier, input);
            db.Arquebusiers.Add(arquebusier);
            Record("ArquebusierRegistered", arquebusier);

            await using var transaction = await db.BeginWriteAsync(cancellationToken);
            var outcome = await SaveAsync(arquebusier.Id, versioned: false, cancellationToken);
            if (outcome != RegistryOutcome.Done)
            {
                return (outcome, null);
            }

            await transaction.CommitAsync(cancellationToken);
            return (RegistryOutcome.Done, arquebusier);
        });

    /// <summary>
    /// Replaces the editable fields of an arquebusier in the caller's scope, if <paramref name="version"/>
    /// is still current. An edit that changes nothing saves and audits nothing (spec: Registering and editing).
    /// </summary>
    public Task<(RegistryOutcome Outcome, Arquebusier? Arquebusier)> UpdateAsync(
        Guid id, ArquebusierInput input, uint version, CancellationToken cancellationToken) =>
        GuardedAsync(nameof(UpdateAsync), id, async () =>
        {
            // Resolved before the transaction, so no row lock is held while it queries the scope.
            var access = await scope.GetAccessAsync(cancellationToken);
            await using var transaction = await db.BeginWriteAsync(cancellationToken);
            if (!await db.LockArquebusierForChangeAsync(id, access, cancellationToken))
            {
                return (RegistryOutcome.ArquebusierNotFound, null);
            }

            var arquebusier = await db.Arquebusiers.SingleAsync(a => a.Id == id, cancellationToken);
            if (arquebusier.Version != version)
            {
                return (RegistryOutcome.ArquebusierModified, null);
            }

            var changedFields = ChangedFields(arquebusier, input);
            if (changedFields.Count == 0)
            {
                return (RegistryOutcome.Done, arquebusier);
            }

            if (await DuplicateOfAsync(input, exceptId: id, cancellationToken) is { } duplicate)
            {
                return (duplicate, null);
            }

            Apply(arquebusier, input);
            Record("ArquebusierUpdated", arquebusier, new { changedFields });
            var outcome = await SaveAsync(id, versioned: true, cancellationToken);
            if (outcome != RegistryOutcome.Done)
            {
                return (outcome, null);
            }

            await transaction.CommitAsync(cancellationToken);
            return (RegistryOutcome.Done, arquebusier);
        });

    /// <summary>
    /// Runs a write, turning a lock timeout (another request holds the row for too long) into the
    /// retryable <see cref="RegistryOutcome.Busy"/> and logging every rejection with ids only (D11).
    /// </summary>
    private async Task<(RegistryOutcome Outcome, Arquebusier? Arquebusier)> GuardedAsync(
        string operation, Guid? arquebusierId, Func<Task<(RegistryOutcome Outcome, Arquebusier? Arquebusier)>> write)
    {
        (RegistryOutcome Outcome, Arquebusier? Arquebusier) result;
        try
        {
            result = await write();
        }
        catch (Exception exception) when (RegistryLocks.IsLockTimeout(exception))
        {
            db.ChangeTracker.Clear();
            result = (RegistryOutcome.Busy, null);
        }

        if (result.Outcome != RegistryOutcome.Done)
        {
            LogRejected(logger, operation, result.Outcome, arquebusierId, currentUser.UserId);
        }

        return result;
    }

    /// <summary>The names of the fields an edit changes, in a stable order; the values are never recorded (D7).</summary>
    private static List<string> ChangedFields(Arquebusier current, ArquebusierInput input)
    {
        var changes = new (string Field, bool Changed)[]
        {
            ("federationId", current.FederationId != input.FederationId),
            ("nationalId", current.NationalId != input.NationalId),
            ("firstName", current.FirstName != input.FirstName),
            ("lastName", current.LastName != input.LastName),
            ("birthDate", current.BirthDate != input.BirthDate),
            ("email", current.Email != input.Email),
            ("phone", current.Phone != input.Phone),
            ("gender", current.Gender != input.Gender),
            ("status", current.Status != input.Status),
            ("trainingCompletedOn", current.TrainingCompletedOn != input.TrainingCompletedOn),
            ("license", current.CurrentLicense() != input.License),
        };
        return [.. changes.Where(c => c.Changed).Select(c => c.Field)];
    }

    /// <summary>Copies the editable fields; the comparsa only changes through a transfer.</summary>
    private static void Apply(Arquebusier arquebusier, ArquebusierInput input)
    {
        arquebusier.FederationId = input.FederationId;
        arquebusier.NationalId = input.NationalId;
        arquebusier.FirstName = input.FirstName;
        arquebusier.LastName = input.LastName;
        arquebusier.BirthDate = input.BirthDate;
        arquebusier.Email = input.Email;
        arquebusier.Phone = input.Phone;
        arquebusier.Gender = input.Gender;
        arquebusier.Status = input.Status;
        arquebusier.TrainingCompletedOn = input.TrainingCompletedOn;
        arquebusier.LicenseType = input.License?.Type;
        arquebusier.LicensePending = input.License?.Pending ?? false;
        arquebusier.LicenseIssuedOn = input.License?.IssuedOn;
        arquebusier.LicenseExpiresOn = input.License?.ExpiresOn;
    }

    /// <summary>
    /// BR-02 pre-check, so the common case gets a clear answer; the unique indexes decide races.
    /// It looks across every comparsa but returns only which value is taken, never whose.
    /// </summary>
    private async Task<RegistryOutcome?> DuplicateOfAsync(ArquebusierInput input, Guid? exceptId, CancellationToken cancellationToken)
    {
        var others = db.Arquebusiers.AsNoTracking().Where(a => a.Id != exceptId);
        if (await others.AnyAsync(a => a.NationalId == input.NationalId, cancellationToken))
        {
            return RegistryOutcome.NationalIdTaken;
        }

        return await others.AnyAsync(a => a.FederationId == input.FederationId, cancellationToken)
            ? RegistryOutcome.FederationIdTaken
            : null;
    }

    /// <summary>
    /// Saves the change with its audit entry, mapping a lost race to its blocking outcome (design D5).
    /// Only an edit of a row loaded with its version can raise a concurrency conflict.
    /// </summary>
    private async Task<RegistryOutcome> SaveAsync(Guid arquebusierId, bool versioned, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return RegistryOutcome.Done;
        }
        catch (DbUpdateConcurrencyException) when (versioned)
        {
            db.ChangeTracker.Clear();
            return RegistryOutcome.ArquebusierModified;
        }
        catch (DbUpdateException exception) when (RegistryProblems.ViolatedConstraint(exception) is { } constraint
            && OutcomeOf(constraint) is { } outcome)
        {
            db.ChangeTracker.Clear();
            LogLostRace(logger, arquebusierId, constraint);
            return outcome;
        }
    }

    private static RegistryOutcome? OutcomeOf(string constraint) => constraint switch
    {
        ArquebusierRegistryDbContext.NationalIdIndex => RegistryOutcome.NationalIdTaken,
        ArquebusierRegistryDbContext.FederationIdIndex => RegistryOutcome.FederationIdTaken,

        // The comparsa was deleted after the catalog lookup (design D10, locking rules).
        ArquebusierRegistryDbContext.ComparsaForeignKey => RegistryOutcome.ComparsaNotFound,
        _ => null,
    };

    /// <summary>Audit entries name what changed, never personal values (design D7).</summary>
    private void Record(string action, Arquebusier arquebusier, object? data = null) =>
        trail.Record(db, new AuditRecord(action, EntityType, arquebusier.Id.ToString(), data, ComparsaId: arquebusier.ComparsaId));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Registry {Operation} rejected with {Outcome} for arquebusier {ArquebusierId} by user {UserId}")]
    private static partial void LogRejected(ILogger logger, string operation, RegistryOutcome outcome, Guid? arquebusierId, Guid? userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Registry write for arquebusier {ArquebusierId} lost a race on {Constraint}")]
    private static partial void LogLostRace(ILogger logger, Guid arquebusierId, string constraint);
}
