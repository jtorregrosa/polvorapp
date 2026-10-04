using PolvorApp.Notifications.Preferences;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.Notifications;

/// <summary>The audit action codes this module records (add-audit-privacy, design D2).</summary>
internal static class NotificationsAuditActions
{
    public const string UserEntityType = "User";

    public static readonly IReadOnlyList<AuditActionDefinition> All =
    [
        new(NotificationPreferences.AuditAction, UserEntityType),
    ];
}
