using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using PolvorApp.AuditPrivacy.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.Notifications.Persistence;
using PolvorApp.Notifications.Preferences;
using PolvorApp.SharedKernel.Codes;
using PolvorApp.SharedKernel.Persistence;

namespace PolvorApp.Notifications.Privacy;

/// <summary>
/// The notifications' part of a GDPR request about a user (UC-26, SEC-09; add-audit-privacy, design D5):
/// their preferences per kind and their delivery records (no address or content is stored), exported
/// and removed by the erasure.
/// </summary>
internal sealed class NotificationsPersonalData(NotificationsDbContext db, IUserDirectory users) : IPersonalDataParticipant
{
    public const string PreferencesSheet = "notificationPreferences";
    public const string DeliveriesSheet = "notificationDeliveries";

    public int Order => PersonalDataParticipantOrder.Notifications;

    public async Task<PersonalDataSummary> DescribeAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        if (subject is not PersonalDataSubject.UserAccount(var userId))
        {
            return PersonalDataSummary.Empty;
        }

        var deliveries = await db.Deliveries.CountAsync(d => d.UserId == userId, cancellationToken);
        return deliveries == 0
            ? PersonalDataSummary.Empty
            : new PersonalDataSummary { Counts = new Dictionary<string, int> { ["notificationDeliveries"] = deliveries } };
    }

    public async Task<PersonalDataExportPart> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        if (subject is not PersonalDataSubject.UserAccount(var userId) || await users.FindAsync(userId, cancellationToken) is not { } user)
        {
            return PersonalDataExportPart.Empty;
        }

        var off = await db.OptOuts.AsNoTracking().Where(o => o.UserId == userId).Select(o => o.Kind).ToListAsync(cancellationToken);
        var deliveries = await db.Deliveries.AsNoTracking().Where(d => d.UserId == userId).OrderBy(d => d.CreatedAt).ToListAsync(cancellationToken);
        List<PersonalDataSheet> sheets =
        [
            new(
                PreferencesSheet,
                ["kind", "on"],
                [.. NotificationKinds.For(user.Role).Select(kind => (IReadOnlyList<object?>)[EnumCodes.ToCode(kind), !off.Contains(kind)])]),
        ];
        if (deliveries.Count > 0)
        {
            sheets.Add(new PersonalDataSheet(
                DeliveriesSheet,
                ["kind", "template", "status", "attempts", "createdAt", "sentAt"],
                [
                    .. deliveries.Select(d => (IReadOnlyList<object?>)
                    [
                        EnumCodes.ToCode(d.Kind), d.Template, EnumCodes.ToCode(d.Status), d.Attempts, d.CreatedAt, d.SentAt,
                    ]),
                ]));
        }

        return new PersonalDataExportPart(sheets, []);
    }

    public Task PrepareErasureAsync(
        PersonalDataSubject subject, PersonalDataErasure erasure, DbTransaction transaction, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public async Task EraseAsync(
        PersonalDataSubject subject, PersonalDataErasure erasure, DbTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(erasure);
        if (subject is not PersonalDataSubject.UserAccount(var userId) || !erasure.UserFound)
        {
            return;
        }

        await db.EnlistAsync(transaction, cancellationToken);
        var optOuts = await db.Database.ExecuteSqlAsync($"DELETE FROM notifications.notification_opt_outs WHERE user_id = {userId}", cancellationToken);
        var deliveries = await db.Database.ExecuteSqlAsync($"DELETE FROM notifications.notification_deliveries WHERE user_id = {userId}", cancellationToken);
        erasure.Count("notificationPreferencesRemoved", optOuts);
        erasure.Count("notificationDeliveriesRemoved", deliveries);
    }
}
