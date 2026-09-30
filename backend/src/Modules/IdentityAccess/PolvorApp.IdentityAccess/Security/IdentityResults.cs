using Microsoft.AspNetCore.Identity;

namespace PolvorApp.IdentityAccess.Security;

/// <summary>
/// Identity reports failures (including concurrency-stamp conflicts) as results, not exceptions.
/// Flows that must not continue after a failed write use <see cref="ThrowIfFailed"/>; the thrown
/// exception rolls back any open transaction and surfaces as a logged 500 instead of silently
/// losing the change and its audit entry.
/// </summary>
internal static class IdentityResults
{
    public static void ThrowIfFailed(this IdentityResult result, string operation)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"{operation} failed: {string.Join(", ", result.Errors.Select(e => e.Code))}.");
        }
    }

    public static async Task ThrowIfFailedAsync(this Task<IdentityResult> result, string operation) =>
        (await result).ThrowIfFailed(operation);
}
