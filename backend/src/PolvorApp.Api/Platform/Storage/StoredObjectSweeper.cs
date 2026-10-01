using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using PolvorApp.SharedKernel.Storage;

namespace PolvorApp.Api.Platform.Storage;

/// <summary>
/// Deletes stored objects that no record references (spec: Stored file cleanup, design D2). Each
/// <see cref="IStoredObjectOwner"/> is asked, a page at a time, which of its old objects are still
/// referenced; the others are deleted. It never deletes on doubt:
/// <list type="bullet">
/// <item>objects younger than <see cref="Grace"/> are kept, so an upload whose reference is about to be committed is safe;</item>
/// <item>any failure to list or to read the references stops the owner's run before deleting anything;</item>
/// <item>a run that would delete more than <see cref="SuspiciousCount"/> objects and more than half of what it scanned is
/// stopped and logged as an error: that looks like a database and a bucket that do not belong together (a restore,
/// a wrong connection string, an owner bug), not like orphans;</item>
/// <item>owners whose prefix is malformed or overlaps another owner's are skipped.</item>
/// </list>
/// </summary>
internal sealed partial class StoredObjectSweeper(
    IServiceScopeFactory scopes,
    IObjectStorage storage,
    IOptions<StorageOptions> options,
    TimeProvider time,
    ILogger<StoredObjectSweeper> logger) : BackgroundService
{
    public const int PageSize = 500;
    public const int SuspiciousCount = 100;
    public static readonly TimeSpan Grace = TimeSpan.FromHours(1);

    /// <summary>Runs one sweep over every owner.</summary>
    public async Task SweepAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var owners = scope.ServiceProvider.GetServices<IStoredObjectOwner>().ToList();
        foreach (var owner in owners)
        {
            if (!HasOwnPrefix(owner, owners))
            {
                LogInvalidPrefix(logger, owner.GetType().Name);
                continue;
            }

            await SweepOwnerAsync(owner, cancellationToken);
        }
    }

    /// <summary>The first run waits one interval: a restart loop must not turn into a sweep loop.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.SweepEnabled)
        {
            LogDisabled(logger);
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.Value.SweepIntervalMinutes), time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await SweepAsync(stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    // A failure outside an owner's run (e.g. resolving the owners) must not stop the host.
                    LogRunFailed(logger, exception);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown.
        }
    }

    private static bool HasOwnPrefix(IStoredObjectOwner owner, List<IStoredObjectOwner> owners) =>
        OwnerPrefix().IsMatch(owner.Prefix)
        && !owners.Any(other => !ReferenceEquals(other, owner)
            && (other.Prefix.StartsWith(owner.Prefix, StringComparison.Ordinal) || owner.Prefix.StartsWith(other.Prefix, StringComparison.Ordinal)));

    private async Task SweepOwnerAsync(IStoredObjectOwner owner, CancellationToken cancellationToken)
    {
        List<string> unreferenced;
        var scanned = 0;
        try
        {
            (scanned, unreferenced) = await FindUnreferencedAsync(owner, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Never delete on doubt: the next run tries again.
            LogFailed(logger, owner.Prefix, scanned, exception);
            return;
        }

        if (unreferenced.Count > SuspiciousCount && unreferenced.Count * 2 > scanned)
        {
            LogSuspicious(logger, owner.Prefix, scanned, unreferenced.Count);
            return;
        }

        var (deleted, failed) = (0, 0);
        foreach (var key in unreferenced)
        {
            try
            {
                await storage.DeleteAsync(key, cancellationToken);
                deleted++;
            }
            catch (StorageUnavailableException exception)
            {
                // One undeletable object must not keep the others forever.
                failed++;
                LogDeleteFailed(logger, key, exception);
            }
        }

        LogSwept(logger, owner.Prefix, scanned, deleted, failed);
    }

    /// <summary>Every object of the owner older than the grace period that no record references.</summary>
    private async Task<(int Scanned, List<string> Unreferenced)> FindUnreferencedAsync(IStoredObjectOwner owner, CancellationToken cancellationToken)
    {
        var (scanned, cutoff) = (0, time.GetUtcNow() - Grace);
        var unreferenced = new List<string>();
        var page = new List<string>(PageSize);
        await foreach (var item in storage.ListAsync(owner.Prefix, cancellationToken))
        {
            scanned++;
            if (item.LastModified >= cutoff)
            {
                continue;
            }

            page.Add(item.Key);
            if (page.Count == PageSize)
            {
                unreferenced.AddRange(await UnreferencedAsync(owner, page, cancellationToken));
                page.Clear();
            }
        }

        if (page.Count > 0)
        {
            unreferenced.AddRange(await UnreferencedAsync(owner, page, cancellationToken));
        }

        return (scanned, unreferenced);
    }

    private static async Task<IEnumerable<string>> UnreferencedAsync(IStoredObjectOwner owner, List<string> keys, CancellationToken cancellationToken)
    {
        var referenced = await owner.FilterReferencedAsync([.. keys], cancellationToken);
        return keys.Where(key => !referenced.Contains(key)).ToList();
    }

    /// <summary><c>&lt;module&gt;/&lt;collection&gt;/</c>, as in the modules README.</summary>
    [GeneratedRegex("^[a-z0-9-]+/[a-z0-9-]+/$", RegexOptions.CultureInvariant)]
    private static partial Regex OwnerPrefix();

    [LoggerMessage(Level = LogLevel.Information, Message = "Stored object sweep of {Prefix}: {Scanned} scanned, {Deleted} deleted, {Failed} failed")]
    private static partial void LogSwept(ILogger logger, string prefix, int scanned, int deleted, int failed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stored object sweep of {Prefix} stopped after {Scanned} scanned; nothing was deleted")]
    private static partial void LogFailed(ILogger logger, string prefix, int scanned, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Stored object sweep of {Prefix} refused to delete {Unreferenced} of {Scanned} objects: the database and the bucket may not belong together")]
    private static partial void LogSuspicious(ILogger logger, string prefix, int scanned, int unreferenced);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stored object sweep could not delete {Key}")]
    private static partial void LogDeleteFailed(ILogger logger, string key, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Stored object sweep skipped {Owner}: its prefix is malformed or overlaps another owner's")]
    private static partial void LogInvalidPrefix(ILogger logger, string owner);

    [LoggerMessage(Level = LogLevel.Error, Message = "Stored object sweep run failed")]
    private static partial void LogRunFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stored object sweep is disabled (Storage:SweepEnabled)")]
    private static partial void LogDisabled(ILogger logger);
}
