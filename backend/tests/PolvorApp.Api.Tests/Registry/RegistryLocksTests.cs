using Microsoft.EntityFrameworkCore;
using Npgsql;
using PolvorApp.ArquebusierRegistry.Persistence;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>
/// Which database errors are worth a retry (design D5): a lock timeout or a deadlock, however deep
/// the save and the execution strategy wrap it, and nothing else.
/// </summary>
public sealed class RegistryLocksTests
{
    [Theory]
    [InlineData(PostgresErrorCodes.LockNotAvailable, true)]
    [InlineData(PostgresErrorCodes.DeadlockDetected, true)]
    [InlineData(PostgresErrorCodes.UniqueViolation, false)]
    public void A_wrapped_database_error_is_found(string sqlState, bool retryable)
    {
        var postgres = new PostgresException("synthetic", "ERROR", "ERROR", sqlState);
        var save = new DbUpdateException("save failed", postgres);
        var strategy = new InvalidOperationException("An exception has been raised that is likely due to a transient failure.", save);

        Assert.Equal(retryable, RegistryLocks.IsRetryable(postgres));
        Assert.Equal(retryable, RegistryLocks.IsRetryable(save));
        Assert.Equal(retryable, RegistryLocks.IsRetryable(strategy));
    }

    [Fact]
    public void Other_errors_are_not_retryable() =>
        Assert.False(RegistryLocks.IsRetryable(new InvalidOperationException("not a database error", new TimeoutException())));
}
