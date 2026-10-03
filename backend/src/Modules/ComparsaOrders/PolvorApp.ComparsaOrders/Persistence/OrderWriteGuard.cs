using Microsoft.Extensions.Logging;
using PolvorApp.IdentityAccess.Contracts;

namespace PolvorApp.ComparsaOrders.Persistence;

/// <summary>
/// Runs an order write (design D1, D8), as the other modules' write guards do: a lock timeout or
/// deadlock becomes the retryable <see cref="OrderOutcome.Busy"/>, and every rejection is logged at
/// Warning with the operation, outcome and ids only (never personal data, NFR-12).
/// </summary>
internal sealed partial class OrderWriteGuard(ComparsaOrdersDbContext db, ICurrentUser currentUser, ILogger<OrderWriteGuard> logger)
{
    public async Task<OrderResult<T>> RunAsync<T>(string operation, Guid? orderId, Func<Task<OrderResult<T>>> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        OrderResult<T> result;
        try
        {
            result = await write();
        }
        catch (Exception exception) when (OrderProblems.IsRetryable(exception))
        {
            db.ChangeTracker.Clear();
            result = OrderResult<T>.Failed(OrderOutcome.Busy);
        }

        if (result.Outcome != OrderOutcome.Done)
        {
            LogRejected(logger, operation, result.Outcome, orderId, currentUser.UserId);
        }

        return result;
    }

    /// <summary>Logs that a write lost a race on a database constraint (ids and constraint name only).</summary>
    public void LostRace(Guid? orderId, string constraint) => LogLostRace(logger, orderId, constraint);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Order {Operation} rejected with {Outcome} for order {OrderId} by user {UserId}")]
    private static partial void LogRejected(ILogger logger, string operation, OrderOutcome outcome, Guid? orderId, Guid? userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Order write for {OrderId} lost a race on {Constraint}")]
    private static partial void LogLostRace(ILogger logger, Guid? orderId, string constraint);
}
