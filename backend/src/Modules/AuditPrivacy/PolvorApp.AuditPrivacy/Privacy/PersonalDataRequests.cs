using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using PolvorApp.AuditPrivacy.Contracts;
using PolvorApp.AuditPrivacy.Persistence;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Persistence;
using PolvorApp.SharedKernel.Storage;

namespace PolvorApp.AuditPrivacy.Privacy;

/// <summary>
/// What an erasure changed, by count code, for the response and the audit entry (no personal data), and
/// how many stored files are still to be deleted by the orphan sweep.
/// </summary>
internal sealed record ErasureResult(IReadOnlyDictionary<string, int> Counts, int ObjectsPending);

/// <summary>
/// Runs a GDPR request across the modules (UC-26; design D5, D6): every participant describes, exports
/// or erases its part. An erasure runs in one transaction on one connection, in a service scope of its
/// own, so the contexts enlisted in it end with it: every participant first locks and notes what it will
/// change, then changes it, and the erasure is audited in the same transaction. Stored objects are
/// deleted after the commit within a time budget, with the orphan sweep as fallback.
/// </summary>
internal sealed partial class PersonalDataRequests(
    IEnumerable<IPersonalDataParticipant> participants,
    IServiceScopeFactory scopes,
    NpgsqlDataSource dataSource,
    IAuditTrail trail,
    IObjectStorage storage,
    ILogger<PersonalDataRequests> logger)
{
    /// <summary>The longest the stored files of an erasure may take to delete before they are left to the sweep.</summary>
    public static readonly TimeSpan ObjectDeletionBudget = TimeSpan.FromSeconds(10);

    private readonly IReadOnlyList<IPersonalDataParticipant> _participants = [.. participants.OrderBy(p => p.Order)];

    public async Task<PersonalDataSummary> DescribeAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        var summary = PersonalDataSummary.Empty;
        foreach (var participant in _participants)
        {
            summary = summary.Merge(await participant.DescribeAsync(subject, cancellationToken));
        }

        return summary;
    }

    public async Task<IReadOnlyList<PersonalDataExportPart>> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        var parts = new List<PersonalDataExportPart>();
        foreach (var participant in _participants)
        {
            parts.Add(await participant.ExportAsync(subject, cancellationToken));
        }

        return parts;
    }

    /// <summary>
    /// Erases the subject's data and records <paramref name="audit"/>, completed with the counts, in the
    /// same transaction. Null when nothing is held: nothing changes and nothing is audited. A participant
    /// refusing (<see cref="PersonalDataErasureRefusedException"/>) rolls everything back.
    /// </summary>
    public async Task<ErasureResult?> EraseAsync(
        PersonalDataSubject subject, Func<IReadOnlyDictionary<string, int>, AuditRecord> audit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audit);
        var erasure = new PersonalDataErasure();
        await using (var scope = scopes.CreateAsyncScope())
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            await using (var timeout = new NpgsqlCommand("SET LOCAL lock_timeout = '5s'", connection, transaction))
            {
                await timeout.ExecuteNonQueryAsync(cancellationToken);
            }

            IPersonalDataParticipant[] steps = [.. scope.ServiceProvider.GetServices<IPersonalDataParticipant>().OrderBy(p => p.Order)];
            foreach (var participant in steps)
            {
                await participant.PrepareErasureAsync(subject, erasure, transaction, cancellationToken);
            }

            foreach (var participant in steps)
            {
                await participant.EraseAsync(subject, erasure, transaction, cancellationToken);
            }

            if (!erasure.ChangedAnything)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            await auditDb.EnlistAsync(transaction, cancellationToken);
            trail.Record(auditDb, audit(erasure.Counts));
            await auditDb.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        var pending = await DeleteObjectsAsync(erasure.ObjectKeysToDelete);
        return new ErasureResult(erasure.Counts, pending);
    }

    /// <summary>
    /// The records are gone: deletes their stored files within <see cref="ObjectDeletionBudget"/> and stops
    /// at the first failure, leaving the rest to the orphan sweep (SEC-08). Returns how many are left.
    /// </summary>
    private async Task<int> DeleteObjectsAsync(IReadOnlyList<string> keys)
    {
        using var budget = new CancellationTokenSource(ObjectDeletionBudget);
        for (var i = 0; i < keys.Count; i++)
        {
            try
            {
                await storage.DeleteAsync(keys[i], budget.Token);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Keys are random and storage errors carry no personal data (NFR-12).
                LogObjectsLeft(logger, keys.Count - i, exception);
                return keys.Count - i;
            }
        }

        return 0;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "{Count} stored files of a committed erasure could not be deleted now; the orphan sweep will delete them")]
    private static partial void LogObjectsLeft(ILogger logger, int count, Exception exception);
}
