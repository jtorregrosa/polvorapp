using System.Text.Json.Serialization;
using PolvorApp.Notifications.Contracts;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.Notifications.Persistence;

/// <summary>Where a delivery stands (design D3).</summary>
[JsonConverter(typeof(CodeEnumConverter<DeliveryStatus>))]
internal enum DeliveryStatus
{
    /// <summary>Waiting to be sent, or to be tried again.</summary>
    [JsonStringEnumMemberName("PENDING")]
    Pending,

    /// <summary>Handed to the SMTP server.</summary>
    [JsonStringEnumMemberName("SENT")]
    Sent,

    /// <summary>Not sent on purpose: the recipient is no longer eligible, or the fact no longer holds. Final.</summary>
    [JsonStringEnumMemberName("SKIPPED")]
    Skipped,

    /// <summary>Given up after the retry window. Final.</summary>
    [JsonStringEnumMemberName("FAILED")]
    Failed,
}

/// <summary>
/// One email to one user about one topic (design D3). The unique <c>(user_id, topic)</c> index makes a
/// recipient get each notification once. It never stores the address, subject or body: <see cref="Data"/>
/// holds identifiers, dates and counts only, and the email is rendered when it is sent.
/// </summary>
internal sealed class NotificationDelivery
{
    public const int TopicMaxLength = 200;
    public const int TemplateMaxLength = 64;
    public const int LastErrorMaxLength = 100;

    public required Guid Id { get; init; }

    public required Guid UserId { get; init; }

    public required NotificationKind Kind { get; init; }

    /// <summary>Culture-independent template name, e.g. <c>OrderReturned</c>.</summary>
    public required string Template { get; init; }

    /// <summary>The deduplication key, e.g. <c>event:&lt;id&gt;</c> or <c>digest:2031-01</c>.</summary>
    public required string Topic { get; init; }

    /// <summary>JSON with identifiers, dates and counts: what to render and what must still hold.</summary>
    public required string Data { get; init; }

    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;

    public int Attempts { get; set; }

    public required DateTimeOffset NextAttemptAt { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? SentAt { get; set; }

    /// <summary>The failing phase and error code of the last attempt (e.g. <c>Send 550</c>), never server text.</summary>
    public string? LastError { get; set; }
}

/// <summary>A kind of notification a user turned off (design D4). A kind is on while no row exists.</summary>
internal sealed class NotificationOptOut
{
    public required Guid UserId { get; init; }

    public required NotificationKind Kind { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
