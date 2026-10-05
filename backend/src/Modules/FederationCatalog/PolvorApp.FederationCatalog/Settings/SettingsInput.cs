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

    /// <summary>
    /// Characters a sender name may not hold: address syntax in a <c>From</c> header, and <c>@</c> so the
    /// name can never pose as an address (security review).
    /// </summary>
    private const string SenderNameForbidden = "<>\"@";

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

    /// <summary>
    /// An optional absolute <c>https</c> address without credentials, stored in its canonical form. The
    /// text must already be that form, so what the Admin typed is exactly what emails show: characters
    /// that need escaping (quotes, angle brackets, spaces), backslashes and non-ASCII or punycode
    /// (lookalike) host names are refused (security review). Blank is none.
    /// </summary>
    private static string? OptionalWebsite(string? value, string field, IDictionary<string, string> errors)
    {
        var text = Optional(value, field, FederationSettings.WebsiteMaxLength, errors);
        if (text is null)
        {
            return null;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.HostNameType != UriHostNameType.Dns
            || !uri.Host.Contains('.', StringComparison.Ordinal)
            || uri.UserInfo.Length > 0
            || !Ascii.IsValid(uri.Host)
            || uri.IdnHost != uri.Host
            || uri.Host.Split('.').Any(label => label.StartsWith("xn--", StringComparison.OrdinalIgnoreCase))
            || !IsCanonical(text, uri))
        {
            errors[field] = InputFields.Invalid;
            return null;
        }

        return text;
    }

    /// <summary>Whether <paramref name="text"/> is written exactly as the parsed address (a bare host gains its trailing slash).</summary>
    private static bool IsCanonical(string text, Uri uri) =>
        string.Equals(text, uri.AbsoluteUri, StringComparison.Ordinal)
        || string.Equals(text + "/", uri.AbsoluteUri, StringComparison.Ordinal);

    /// <summary>Blank or absent is none; otherwise the trimmed text, checked as <see cref="InputFields.Text"/> does (oversized input first).</summary>
    private static string? Optional(string? value, string field, int maxLength, IDictionary<string, string> errors) =>
        string.IsNullOrWhiteSpace(value) ? null : InputFields.Text(value, field, maxLength, errors);
}
