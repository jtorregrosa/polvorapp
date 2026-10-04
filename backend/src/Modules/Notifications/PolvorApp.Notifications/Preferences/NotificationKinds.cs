using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.Notifications.Contracts;

namespace PolvorApp.Notifications.Preferences;

/// <summary>The kinds that apply to each role, in the order screens list them (spec: Notification preferences).</summary>
internal static class NotificationKinds
{
    private static readonly NotificationKind[] FiringChief =
        [NotificationKind.LicenseDigest, NotificationKind.OrderWindow, NotificationKind.OrderStatus, NotificationKind.MilestoneReminder];

    private static readonly NotificationKind[] Admin = [NotificationKind.OrderStatus, NotificationKind.MilestoneReminder];

    public static IReadOnlyList<NotificationKind> For(UserRole role) => role switch
    {
        UserRole.FiringChief => FiringChief,
        UserRole.Admin => Admin,
        _ => [],
    };

    public static bool AppliesTo(NotificationKind kind, UserRole role) => For(role).Contains(kind);
}
