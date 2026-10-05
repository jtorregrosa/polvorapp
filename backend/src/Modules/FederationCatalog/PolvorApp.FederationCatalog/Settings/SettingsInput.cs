using System.Text;
using PolvorApp.FederationCatalog.Endpoints;
using PolvorApp.FederationCatalog.Logos;
using PolvorApp.SharedKernel.Validation;

namespace PolvorApp.FederationCatalog.Settings;

/// <summary>
/// Reads the settings requests (spec: Federation settings; design D2), naming every invalid field with
/// its reason. Texts are trimmed and in NFC; line breaks and invisible characters are refused.
/// </summary>
internal static class SettingsInput
{
    public const string OutOfRange = "outOfRange";

    /// <summary>Characters a sender name may not hold: they would read as address syntax in a <c>From</c> header.</summary>
    private const string SenderNameForbidden = "<>\"";

    public static IdentityFields? Identity(IdentitySettingsRequest request, IDictionary<string, string> errors)
    {
        var officialNameEs = InputFields.Text(request.OfficialNameEs, "officialNameEs", FederationSettings.OfficialNameMaxLength, errors);
        var officialNameCa = InputFields.Text(request.OfficialNameCa, "officialNameCa", FederationSettings.OfficialNameMaxLength, errors);
        var shortName = InputFields.Text(request.ShortName, "shortName", FederationSettings.ShortNameMaxLength, errors);
        var contactEmail = OptionalEmail(request.ContactEmail, "contactEmail", errors);
        var website = OptionalWebsite(request.Website, "website", errors);
        return errors.Count == 0 ? new IdentityFields(officialNameEs!, officialNameCa!, shortName!, contactEmail, website) : null;
    }

    public static EmailFields? Emails(EmailSettingsRequest request, IDictionary<string, string> errors)
    {
        var senderName = InputFields.Text(request.SenderName, "senderName", FederationSettings.SenderNameMaxLength, errors);
        if (senderName is not null && senderName.AsSpan().IndexOfAny(SenderNameForbidden) >= 0)
        {
            errors["senderName"] = InputFields.Invalid;
        }

        var replyTo = OptionalEmail(request.ReplyTo, "replyTo", errors);
        return errors.Count == 0 ? new EmailFields(senderName!, replyTo) : null;
    }

    public static OrderFields? Orders(OrderSettingsRequest request, IDictionary<string, string> errors) =>
        Days(request.CloseReminderLeadDays, "closeReminderLeadDays", FederationSettings.MinCloseReminderLeadDays, FederationSettings.MaxCloseReminderLeadDays, errors) is { } days
            ? new OrderFields(days)
            : null;

    public static CalendarFields? Calendar(CalendarSettingsRequest request, IDictionary<string, string> errors) =>
        Days(request.MilestoneLeadDays, "milestoneLeadDays", FederationSettings.MinMilestoneLeadDays, FederationSettings.MaxMilestoneLeadDays, errors) is { } days
            ? new CalendarFields(days)
            : null;

    /// <summary>The version a save is based on; required.</summary>
    public static uint? Version(uint? version, IDictionary<string, string> errors)
    {
        if (version is null)
        {
            errors["version"] = InputFields.Required;
        }

        return version;
    }

    private static int? Days(int? days, string field, int min, int max, IDictionary<string, string> errors)
    {
        if (days is null)
        {
            errors[field] = InputFields.Required;
            return null;
        }

        if (days < min || days > max)
        {
            errors[field] = OutOfRange;
            return null;
        }

        return days;
    }

    /// <summary>An optional plain address (no display name, a dotted domain); blank is none.</summary>
    private static string? OptionalEmail(string? value, string field, IDictionary<string, string> errors)
    {
        var text = Optional(value, field, FederationSettings.EmailMaxLength, errors);
        if (text is not null && !InputFields.IsPlainEmail(text))
        {
            errors[field] = InputFields.Invalid;
            return null;
        }

        return text;
    }

    /// <summary>An optional absolute <c>https</c> address without credentials; blank is none.</summary>
    private static string? OptionalWebsite(string? value, string field, IDictionary<string, string> errors)
    {
        var text = Optional(value, field, FederationSettings.WebsiteMaxLength, errors);
        if (text is not null
            && !(Uri.TryCreate(text, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps
                && uri.Host.Length > 0
                && uri.UserInfo.Length == 0
                && !text.Any(char.IsWhiteSpace)))
        {
            errors[field] = InputFields.Invalid;
            return null;
        }

        return text;
    }

    /// <summary>Blank or absent is none; otherwise the trimmed text, checked as <see cref="InputFields.Text"/> does.</summary>
    private static string? Optional(string? value, string field, int maxLength, IDictionary<string, string> errors) =>
        string.IsNullOrWhiteSpace(value) ? null : InputFields.Text(value.Normalize(NormalizationForm.FormC), field, maxLength, errors);
}
