using Microsoft.Extensions.Logging;
using PolvorApp.SharedKernel.Storage;

namespace PolvorApp.ArquebusierRegistry.Photos;

/// <summary>
/// Erases stored photo images once their references are gone (design D2, D7). Best-effort and
/// bounded: the change that removed the references is already committed, so a failure is logged and
/// left to the orphan sweep (spec: Stored photo cleanup), never reported to the caller. During a
/// storage outage it gives up after the first failure instead of waiting for every image.
/// </summary>
internal sealed partial class PhotoObjects(IObjectStorage storage, ILogger<PhotoObjects> logger)
{
    /// <summary>Longest a request waits for its erasures after its commit.</summary>
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    public Task DeleteAsync(string? key) => key is null ? Task.CompletedTask : DeleteAsync([key]);

    public async Task DeleteAsync(IReadOnlyList<string> keys)
    {
        // Not the request's token: a client that hangs up must not leave the images behind.
        using var budget = new CancellationTokenSource(Budget);
        for (var i = 0; i < keys.Count; i++)
        {
            try
            {
                await storage.DeleteAsync(keys[i], budget.Token);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                foreach (var key in keys.Skip(i))
                {
                    LogLeftForSweep(logger, key, exception);
                }

                return;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not erase photo image {Key}; it is left for the orphan sweep")]
    private static partial void LogLeftForSweep(ILogger logger, string key, Exception exception);
}
