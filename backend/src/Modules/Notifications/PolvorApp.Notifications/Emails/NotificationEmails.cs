using System.Globalization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.Notifications.Contracts;
using PolvorApp.Notifications.Rules;
using PolvorApp.SharedKernel.Codes;
using PolvorApp.SharedKernel.Email;

namespace PolvorApp.Notifications.Emails;

/// <summary>
/// Marker for the email texts (<c>NotificationTexts*.resx</c> beside this file). Spanish is neutral;
/// Valencian lives in <c>.ca</c>, the parent culture of ca-ES-valencia.
/// </summary>
internal sealed class NotificationTexts;

/// <summary>
/// Renders a notification in the recipient's language (spec: Email content; design D7): a greeting,
/// the message with its link, a footer naming the kind with the settings link, then the Federation's
/// short name and, when set, its public contact address and website (add-federation-settings). Plain
/// text plus a minimal HTML alternative; no remote content or tracking. Only PolvorApp's own pages are
/// links: the Federation's website is shown as text.
/// </summary>
internal sealed partial class NotificationEmails(IStringLocalizer<NotificationTexts> texts, IOptions<PublicUrlOptions> urls, ILogger<NotificationEmails> logger)
{
    /// <summary>The account page opens at the notification settings with this query (a fragment cannot be linked).</summary>
    public const string SettingsPath = "account";

    private const string FallbackLocale = "es-ES";

    private static readonly string[] Locales = ["es-ES", "ca-ES-valencia", "en"];

    private static readonly Dictionary<string, string> NoQuery = [];

    public EmailMessage Render(UserSummary recipient, NotificationContent content, FederationSettingsSnapshot federation)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(federation);
        var known = Locales.Contains(recipient.Locale, StringComparer.Ordinal);
        if (!known)
        {
            // The identity module only stores the three locales; a different one is a data problem.
            LogUnknownLocale(logger, recipient.Id);
        }

        var culture = CultureInfo.GetCultureInfo(known ? recipient.Locale : FallbackLocale);
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = culture;
            var link = LinkOf(content);
            var settings = urls.Value.Link(SettingsPath, new Dictionary<string, string> { ["section"] = "notifications" });
            var (subject, message) = Compose(content, culture, link.AbsoluteUri);
            var footer = Text($"Footer.{EnumCodes.ToCode(content.Kind)}");
            var body = $"{Text("Greeting", recipient.Name)}\n\n{message}\n\n—\n{footer}\n{settings.AbsoluteUri}\n\n{FederationLines(federation)}";
            return new EmailMessage(recipient.Email, recipient.Name, subject, body, PlainTextHtml.Render(body, [link, settings]), content.Template);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>The Federation's short name, then its public contact address and website when set.</summary>
    private string FederationLines(FederationSettingsSnapshot federation)
    {
        var lines = new List<string> { federation.ShortName };
        if (federation.ContactEmail is { } contact)
        {
            lines.Add(Text("Footer.Contact", contact));
        }

        if (federation.Website is { } website)
        {
            lines.Add(website);
        }

        return string.Join('\n', lines);
    }

    private Uri LinkOf(NotificationContent content) => content switch
    {
        NotificationContent.OrdersOpened or NotificationContent.OrdersClosed => Link("orders"),
        NotificationContent.OrdersClosing { OrderId: { } orderId } => Link($"orders/{orderId}"),
        NotificationContent.OrdersClosing => Link("orders"),
        NotificationContent.OrderSubmitted submitted => Link($"orders/{submitted.OrderId}"),
        NotificationContent.OrderReturned returned => Link($"orders/{returned.OrderId}"),
        NotificationContent.OrderValidated validated => Link($"orders/{validated.OrderId}"),
        NotificationContent.MilestoneReminder milestone => Link($"editions/{milestone.EditionId}"),
        NotificationContent.LicenseDigest => Link("/"),
        _ => throw new ArgumentOutOfRangeException(nameof(content), content.Template, "Unknown notification content."),
    };

    private (string Subject, string Message) Compose(NotificationContent content, CultureInfo culture, string link)
    {
        var template = content.Template;
        string Date(DateOnly date) => date.ToString(Text("DateFormat"), culture);
        object[] arguments = content switch
        {
            NotificationContent.OrdersOpened opened => [Year(opened.Year), Date(opened.CloseOn), link],
            NotificationContent.OrdersClosed closed => [Year(closed.Year), link],
            NotificationContent.OrdersClosing closing =>
            [
                closing.Comparsa, Year(closing.Year), Date(closing.CloseOn), Text($"OrderState.{StateCode(closing.State)}"), link,
                Text(closing.CloseOn > closing.Today ? "When.Tomorrow" : "When.Today"),
            ],
            NotificationContent.OrderSubmitted submitted => [submitted.Comparsa, Year(submitted.Year), link],
            NotificationContent.OrderReturned returned => [returned.Comparsa, Year(returned.Year), link],
            NotificationContent.OrderValidated validated => [validated.Comparsa, Year(validated.Year), link],
            NotificationContent.MilestoneReminder milestone => [milestone.Title, Date(milestone.Date), Year(milestone.Year), link],
            NotificationContent.LicenseDigest digest => [digest.Date.ToString(Text("MonthFormat"), culture), DigestBlocks(digest), link],
            _ => throw new ArgumentOutOfRangeException(nameof(content), template, "Unknown notification content."),
        };

        return (Text($"{template}.Subject", arguments), Text($"{template}.Text", arguments));
    }

    /// <summary>One block per comparsa: its name, then one line per non-zero count.</summary>
    private string DigestBlocks(NotificationContent.LicenseDigest digest) =>
        string.Join("\n\n", digest.Comparsas.Select(c =>
        {
            var lines = new List<string> { c.Comparsa };
            AddLine(lines, "LicenseDigest.Missing", c.Counts.Missing);
            AddLine(lines, "LicenseDigest.Pending", c.Counts.Pending);
            AddLine(lines, "LicenseDigest.Expired", c.Counts.Expired);
            AddLine(lines, "LicenseDigest.ExpiringSoon", c.Counts.ExpiringSoon);
            return string.Join('\n', lines);
        }));

    private void AddLine(List<string> lines, string key, int count)
    {
        if (count > 0)
        {
            lines.Add(Text(key, count));
        }
    }

    private Uri Link(string path) => urls.Value.Link(path, NoQuery);

    /// <summary>A year as written, never with a group separator.</summary>
    private static string Year(int year) => year.ToString(CultureInfo.InvariantCulture);

    private static string StateCode(PendingOrderState state) => state switch
    {
        PendingOrderState.NotPrepared => "NOT_PREPARED",
        PendingOrderState.Draft => "DRAFT",
        _ => "RETURNED",
    };

    /// <summary>A missing text would otherwise be sent as its key.</summary>
    private string Text(string key, params object[] arguments)
    {
        var text = arguments.Length == 0 ? texts[key] : texts[key, arguments];
        return text.ResourceNotFound ? throw new InvalidOperationException($"The email text {key} is missing.") : text.Value;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "User {UserId} has an unknown locale: the notification is written in Spanish")]
    private static partial void LogUnknownLocale(ILogger logger, Guid userId);
}
