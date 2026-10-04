using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.Notifications.Contracts;
using PolvorApp.Notifications.Persistence;
using PolvorApp.SharedKernel.Hosting;
using PolvorApp.SharedKernel.Seeding;

namespace PolvorApp.Notifications.Seeding;

/// <summary>
/// Synthetic notification data for development, staging and E2E tests (spec: Synthetic notification
/// data; SEC-11): the seeded FiringChief "Jefa Sintética Dos" has the license digest turned off, so the
/// account page shows a kind off. The milestone reminded by email is seeded by the editions seeder.
/// Seeding sends no email and records no audit entry. Fixed identifiers; it can run again.
/// </summary>
internal sealed partial class NotificationSeeder(
    NotificationsDbContext db, IUserDirectory users, TimeProvider time, IHostEnvironment environment, ILogger<NotificationSeeder> logger) : IDataSeeder
{
    /// <summary>The identity seeder's "Jefa Sintética Dos".</summary>
    public static readonly Guid SeededChief = new("0193a000-0000-7000-8000-000000000003");

    public const NotificationKind SeededOptOut = NotificationKind.LicenseDigest;

    /// <summary>After every module whose data notifications read (60 is distribution).</summary>
    public int Order => 70;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await RefuseRealDataOutsideLocalAsync(cancellationToken);
        if (await users.FindAsync(SeededChief, cancellationToken) is null)
        {
            LogSkipped(logger, SeededChief);
            return;
        }

        var added = 0;
        if (!await db.OptOuts.AnyAsync(o => o.UserId == SeededChief && o.Kind == SeededOptOut, cancellationToken))
        {
            db.OptOuts.Add(new NotificationOptOut { UserId = SeededChief, Kind = SeededOptOut, CreatedAt = time.GetUtcNow() });
            await db.SaveChangesAsync(cancellationToken);
            added++;
        }

        LogSeeded(logger, added);
    }

    /// <summary>Outside a local environment the seeder refuses a database with notification data it did not write (SEC-11).</summary>
    private async Task RefuseRealDataOutsideLocalAsync(CancellationToken cancellationToken)
    {
        if (LocalEnvironments.IsLocal(environment))
        {
            return;
        }

        if (await db.OptOuts.AnyAsync(o => o.UserId != SeededChief || o.Kind != SeededOptOut, cancellationToken)
            || await db.Deliveries.AnyAsync(cancellationToken))
        {
            throw new InvalidOperationException("The database holds notification data that is not synthetic: the seed refuses to run.");
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Notification seed: {OptOuts} opt-outs added")]
    private static partial void LogSeeded(ILogger logger, int optOuts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification seed skipped: the seeded user {UserId} is missing")]
    private static partial void LogSkipped(ILogger logger, Guid userId);
}
