using System.Text.Json.Serialization;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.Notifications.Contracts;

/// <summary>
/// A kind of notification a user can turn on or off (spec: Notification kinds and recipients). Every
/// kind is on until the user turns it off.
/// </summary>
[JsonConverter(typeof(CodeEnumConverter<NotificationKind>))]
public enum NotificationKind
{
    /// <summary>The monthly license digest (FiringChiefs).</summary>
    [JsonStringEnumMemberName("LICENSE_DIGEST")]
    LicenseDigest,

    /// <summary>Orders opened, orders closed and the planned close reminders (FiringChiefs).</summary>
    [JsonStringEnumMemberName("ORDER_WINDOW")]
    OrderWindow,

    /// <summary>Order returned or validated (the order's FiringChiefs); order submitted (Admins).</summary>
    [JsonStringEnumMemberName("ORDER_STATUS")]
    OrderStatus,

    /// <summary>Calendar milestone reminders (Admins and FiringChiefs).</summary>
    [JsonStringEnumMemberName("MILESTONE_REMINDER")]
    MilestoneReminder,
}
