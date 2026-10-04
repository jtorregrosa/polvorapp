using System.Globalization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using PolvorApp.IdentityAccess.Security;
using PolvorApp.IdentityAccess.Users;
using PolvorApp.SharedKernel.Email;

namespace PolvorApp.IdentityAccess.Emails;

/// <summary>
/// Marker for the email texts (<c>EmailTexts*.resx</c> beside this file). Spanish is neutral;
/// Valencian lives in <c>.ca</c>, the parent culture of ca-ES-valencia.
/// </summary>
internal sealed class EmailTexts;

/// <summary>
/// Invitation and password-reset emails in the recipient's language, with a plain-text body and a
/// minimal HTML alternative (no remote content, no tracking) — design D9.
/// </summary>
internal sealed class IdentityEmails(IStringLocalizer<EmailTexts> texts, IOptions<PublicUrlOptions> urls, IEmailSender sender, IEmailOutbox outbox)
{
    public const string InvitationTemplate = "Invitation";
    public const string PasswordResetTemplate = "PasswordReset";
    public const string InvitationPath = "invitations/accept";
    public const string PasswordResetPath = "password/reset";

    public Task SendInvitationAsync(User user, string token, CancellationToken cancellationToken) =>
        SendAsync(user, InvitationTemplate, InvitationPath, token, (int)new InvitationTokenProviderOptions().TokenLifespan.TotalDays, cancellationToken);

    /// <summary>Queued, so the reset response time does not depend on the SMTP server (spec: identical response).</summary>
    public void QueuePasswordReset(User user, string token) =>
        outbox.Enqueue(Compose(user, PasswordResetTemplate, PasswordResetPath, token, (int)IdentityConfiguration.PasswordResetLifetime.TotalMinutes));

    private Task SendAsync(User user, string template, string path, string token, int validity, CancellationToken cancellationToken) =>
        sender.SendAsync(Compose(user, template, path, token, validity), cancellationToken);

    private EmailMessage Compose(User user, string template, string path, string token, int validity)
    {
        var email = user.Email ?? throw new InvalidOperationException("The user has no email address.");
        var link = urls.Value.Link(path, new Dictionary<string, string> { ["user"] = user.Id.ToString(), ["token"] = token });
        var (subject, text) = Render(user, template, link, validity);
        return new EmailMessage(email, user.Name, subject, text, PlainTextHtml.Render(text, [link]), template);
    }

    private (string Subject, string Text) Render(User user, string template, Uri link, int validity)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(user.Locale);
            return (Text(texts[$"{template}.Subject"]), Text(texts[$"{template}.Text", user.Name, link.AbsoluteUri, validity]));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>A missing text would otherwise be sent as its key.</summary>
    private static string Text(LocalizedString text) =>
        text.ResourceNotFound ? throw new InvalidOperationException($"The email text {text.Name} is missing.") : text.Value;
}
