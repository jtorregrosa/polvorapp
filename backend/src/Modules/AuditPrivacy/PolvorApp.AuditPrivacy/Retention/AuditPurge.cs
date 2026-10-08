using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PolvorApp.AuditPrivacy.Maintenance;
using PolvorApp.AuditPrivacy.Persistence;
using PolvorApp.AuditPrivacy.Viewer;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.AuditPrivacy.Retention;

/// <summary>What one purge deleted.</summary>
internal sealed record AuditPurgeResult(int Security, int Standard)
{
    public int Total => Security + Standard;
}

/// <summary>
/// Deletes the audit entries older than their retention period (spec: Audit retention; design D3): the
/// access and security events after <c>SecurityRetentionDays</c>, every other entry after
/// <c>RetentionYears</c>. It deletes in batches, each in its own transaction, so the table is never
/// locked for long, then records one entry with the counts, only when something was deleted. The two
/// periods are purged independently, each failure is logged with its exception, and what was deleted
/// is recorded even when a period fails or the host is stopping.
/// </summary>
internal sealed partial class AuditPurge(
    AuditDbContext db,
    AuditActionCatalog catalog,
    IAuditLog auditLog,
    IOptions<AuditRetentionOptions> options,
    TimeProvider time,
    ILogger<AuditPurge> logger)
{
    public const int BatchSize = 5000;

    /// <summary>How long recording a purge may take once the deletions are done, even while stopping.</summary>
    public static readonly TimeSpan RecordBudget = TimeSpan.FromSeconds(10);

    public async Task<AuditPurgeResult> RunAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var settings = options.Value;
        var security = new Counter();
        var standard = new Counter();
        var failures = new List<Exception>();
        await DeleteAllAsync(AuditRetentionClass.Security, now.AddDays(-settings.SecurityRetentionDays), security, failures, cancellationToken);
        if (!cancellationToken.IsCancellationRequested)
        {
            await DeleteAllAsync(AuditRetentionClass.Standard, now.AddYears(-settings.RetentionYears), standard, failures, cancellationToken);
        }

        // What was deleted is recorded whatever happened next: deleted entries without a record of them
        // would defeat the accountability the purge keeps.
        var result = new AuditPurgeResult(security.Deleted, standard.Deleted);
        if (result.Total > 0)
        {
            await RecordAsync(result, failures);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return failures switch
        {
            [] => result,
            [var only] => throw new InvalidOperationException("The audit retention purge failed.", only),
            _ => throw new AggregateException("The audit retention purge failed.", failures),
        };
    }

    private async Task RecordAsync(AuditPurgeResult result, List<Exception> failures)
    {
        using var budget = new CancellationTokenSource(RecordBudget);
        try
        {
            await auditLog.RecordAsync(
                new AuditRecord(
                    AuditPrivacyAuditActions.AuditEntriesPurged,
                    AuditPrivacyAuditActions.AuditTrailEntityType,
                    Data: new { security = result.Security, standard = result.Standard },
                    Anonymous: true),
                budget.Token);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The rows are already gone: the log keeps the counts the audit could not.
            LogRecordFailed(logger, result.Security, result.Standard, exception);
            failures.Add(exception);
        }
    }

    /// <summary>
    /// Deletes, batch by batch, the entries of <paramref name="retention"/> older than
    /// <paramref name="cutoff"/>, counting into <paramref name="counter"/>. A failure is logged and kept,
    /// so the other period is still purged.
    /// </summary>
    private async Task DeleteAllAsync(
        AuditRetentionClass retention, DateTimeOffset cutoff, Counter counter, List<Exception> failures, CancellationToken cancellationToken)
    {
        try
        {
            int deleted;
            do
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                deleted = await AuditMaintenance.DeleteExpiredAsync(
                    transaction.GetDbTransaction(), catalog.SecurityActions, retention, cutoff, BatchSize, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                counter.Deleted += deleted;
            }
            while (deleted == BatchSize);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host is stopping: the batches already committed are recorded by the caller, which then stops too.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogPeriodFailed(logger, retention, counter.Deleted, exception);
            failures.Add(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The {Retention} audit retention purge failed after deleting {Deleted} entries")]
    private static partial void LogPeriodFailed(ILogger logger, AuditRetentionClass retention, int deleted, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "The audit retention purge deleted {Security} security and {Standard} other entries but could not record it")]
    private static partial void LogRecordFailed(ILogger logger, int security, int standard, Exception exception);

    private sealed class Counter
    {
        public int Deleted { get; set; }
    }
}
