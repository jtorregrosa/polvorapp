using PolvorApp.FederationCatalog.Logos;

namespace PolvorApp.FederationCatalog.Endpoints;

/// <summary>The identity section (spec: Federation settings).</summary>
/// <param name="OfficialNameEs">The official name in Spanish, printed in Spanish and English documents.</param>
/// <param name="OfficialNameCa">The official name in Valencian, printed in Valencian documents.</param>
/// <param name="ShortName">The short name for the interface and the end of emails.</param>
/// <param name="ContactEmail">The Federation's public contact address, or null.</param>
/// <param name="Website">The Federation's public https website, or null.</param>
internal sealed record IdentitySettingsResponse(string OfficialNameEs, string OfficialNameCa, string ShortName, string? ContactEmail, string? Website);

/// <summary>The emails section.</summary>
/// <param name="SenderName">The name shown with the sender address.</param>
/// <param name="ReplyTo">Where replies go, or null.</param>
/// <param name="SenderAddress">The deployment's sender address (an environment variable, read-only here).</param>
internal sealed record EmailSettingsResponse(string SenderName, string? ReplyTo, string SenderAddress);

/// <summary>The orders section.</summary>
/// <param name="CloseReminderLeadDays">Days before the planned close the first close reminder is sent (2–14).</param>
internal sealed record OrderSettingsResponse(int CloseReminderLeadDays);

/// <summary>The calendar section.</summary>
/// <param name="MilestoneLeadDays">Days before a milestone its reminder is sent (1–14).</param>
internal sealed record CalendarSettingsResponse(int MilestoneLeadDays);

/// <summary>Every settings section as Admins read and edit them (spec: Federation settings, Settings screen).</summary>
/// <param name="Version">The settings version; a save based on another one is refused with <c>409</c>.</param>
/// <param name="Identity">Names and public contact.</param>
/// <param name="Emails">Sender name and reply-to.</param>
/// <param name="Orders">Close reminder lead time.</param>
/// <param name="Calendar">Milestone reminder lead time.</param>
internal sealed record FederationSettingsResponse(
    uint Version,
    IdentitySettingsResponse Identity,
    EmailSettingsResponse Emails,
    OrderSettingsResponse Orders,
    CalendarSettingsResponse Calendar)
{
    public static FederationSettingsResponse From(FederationSettings settings, string senderAddress) => new(
        settings.Version,
        new IdentitySettingsResponse(settings.OfficialNameEs, settings.OfficialNameCa, settings.ShortName, settings.ContactEmail, settings.Website),
        new EmailSettingsResponse(settings.SenderName, settings.ReplyTo, senderAddress),
        new OrderSettingsResponse(settings.CloseReminderLeadDays),
        new CalendarSettingsResponse(settings.MilestoneLeadDays));
}

/// <summary>Saves the identity section. Blank optional values are stored as none.</summary>
/// <param name="Version">The settings version the change is based on.</param>
/// <param name="OfficialNameEs">1 to 150 characters after trimming.</param>
/// <param name="OfficialNameCa">1 to 150 characters after trimming.</param>
/// <param name="ShortName">1 to 40 characters after trimming.</param>
/// <param name="ContactEmail">An email address of the Federation, or blank.</param>
/// <param name="Website">An absolute https address of at most 200 characters, or blank.</param>
internal sealed record IdentitySettingsRequest(uint? Version, string? OfficialNameEs, string? OfficialNameCa, string? ShortName, string? ContactEmail, string? Website);

/// <summary>Saves the emails section.</summary>
/// <param name="Version">The settings version the change is based on.</param>
/// <param name="SenderName">1 to 80 characters, without <c>&lt;</c>, <c>&gt;</c>, <c>"</c> or line breaks.</param>
/// <param name="ReplyTo">An email address, or blank for none.</param>
internal sealed record EmailSettingsRequest(uint? Version, string? SenderName, string? ReplyTo);

/// <summary>Saves the orders section.</summary>
/// <param name="Version">The settings version the change is based on.</param>
/// <param name="CloseReminderLeadDays">2 to 14.</param>
internal sealed record OrderSettingsRequest(uint? Version, int? CloseReminderLeadDays);

/// <summary>Saves the calendar section.</summary>
/// <param name="Version">The settings version the change is based on.</param>
/// <param name="MilestoneLeadDays">1 to 14.</param>
internal sealed record CalendarSettingsRequest(uint? Version, int? MilestoneLeadDays);
