using Microsoft.Extensions.Logging;
using PolvorApp.SharedKernel.Storage;

namespace PolvorApp.FederationCatalog.Logos;

/// <summary>
/// Erases stored logo images once their references are gone (design D4, D5). Best-effort and
/// bounded: the change that removed the reference is already committed, so a failure is logged and
/// left to the orphan sweep, never reported to the caller.
/// </summary>
internal sealed partial class LogoObjects(IObjectStorage storage, ILogger<LogoObjects> logger)
{
    /// <summary>Longest a request waits for its erasure after its commit.</summary>
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    public async Task DeleteAsync(string? key)
    {
        if (key is null)
        {
            return;
        }

        // Not the request's token: a client that hangs up must not leave the image behind.
        using var budget = new CancellationTokenSource(Budget);
        try
        {
            await storage.DeleteAsync(key, budget.Token);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogLeftForSweep(logger, key, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not erase logo image {Key}; it is left for the orphan sweep")]
    private static partial void LogLeftForSweep(ILogger logger, string key, Exception exception);
}
