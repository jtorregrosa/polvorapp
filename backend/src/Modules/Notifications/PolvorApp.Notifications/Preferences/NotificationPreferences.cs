using Microsoft.EntityFrameworkCore;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.Notifications.Contracts;
using PolvorApp.Notifications.Persistence;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.Notifications.Preferences;

/// <summary>Whether one kind is on for the user.</summary>
internal sealed record NotificationPreference(NotificationKind Kind, bool Enabled);

/// <summary>
/// A user's own notification preferences (spec: Notification preferences; design D4, D11): the kinds of
/// their role, each on unless an opt-out row exists. Saving changes only the kinds given, and records one
/// audit entry with the kinds turned on and off when anything changed.
/// </summary>
internal sealed class NotificationPreferences(NotificationsDbContext db, IAuditTrail trail, TimeProvider time)
{
    public const string AuditAction = "NotificationPreferencesChanged";

    public async Task<IReadOnlyList<NotificationPreference>> GetAsync(Guid userId, UserRole role, CancellationToken cancellationToken)
    {
        var off = await OptedOutAsync(userId, cancellationToken);
        return NotificationKinds.For(role).Select(kind => new NotificationPreference(kind, !off.Contains(kind))).ToList();
    }

    /// <summary>Applies <paramref name="changes"/>, already validated against the role, and returns the role's preferences.</summary>
    public async Task<IReadOnlyList<NotificationPreference>> SaveAsync(
        Guid userId, UserRole role, IReadOnlyCollection<NotificationPreference> changes, CancellationToken cancellationToken)
    {
        await using var transaction = await db.BeginWriteAsync(cancellationToken);
        await db.LockPreferencesAsync(userId, cancellationToken);
        var off = await OptedOutAsync(userId, cancellationToken);

        var turnedOff = changes.Where(c => !c.Enabled && !off.Contains(c.Kind)).Select(c => c.Kind).ToList();
        var turnedOn = changes.Where(c => c.Enabled && off.Contains(c.Kind)).Select(c => c.Kind).ToList();
        if (turnedOff.Count > 0 || turnedOn.Count > 0)
        {
            var now = time.GetUtcNow();
            db.OptOuts.AddRange(turnedOff.Select(kind => new NotificationOptOut { UserId = userId, Kind = kind, CreatedAt = now }));
            await db.OptOuts.Where(o => o.UserId == userId && turnedOn.Contains(o.Kind)).ExecuteDeleteAsync(cancellationToken);
            trail.Record(db, new AuditRecord(
                AuditAction,
                "User",
                userId.ToString(),
                new { turnedOff = turnedOff.Select(k => EnumCodes.ToCode(k)).ToList(), turnedOn = turnedOn.Select(k => EnumCodes.ToCode(k)).ToList() }));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            off.ExceptWith(turnedOn);
            off.UnionWith(turnedOff);
        }

        return NotificationKinds.For(role).Select(kind => new NotificationPreference(kind, !off.Contains(kind))).ToList();
    }

    private async Task<HashSet<NotificationKind>> OptedOutAsync(Guid userId, CancellationToken cancellationToken) =>
        (await db.OptOuts.AsNoTracking().Where(o => o.UserId == userId).Select(o => o.Kind).ToListAsync(cancellationToken)).ToHashSet();
}
