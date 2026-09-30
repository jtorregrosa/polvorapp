using System.Text;

namespace PolvorApp.SharedKernel.Email;

/// <summary>A transactional email (NFR-11), already rendered in the recipient's language.</summary>
/// <param name="ToAddress">Recipient address.</param>
/// <param name="ToName">Recipient display name, if any.</param>
/// <param name="Subject">Subject line.</param>
/// <param name="TextBody">Plain-text body; always present.</param>
/// <param name="HtmlBody">Optional HTML alternative (no remote content, no tracking).</param>
/// <param name="Template">Culture-independent template name, the only part of the message that is logged.</param>
public sealed record EmailMessage(
    string ToAddress,
    string? ToName,
    string Subject,
    string TextBody,
    string? HtmlBody,
    string Template)
{
    /// <summary>
    /// <c>ToString</c> shows the template only: the address, subject and bodies hold personal data
    /// and one-time links that must never reach a log (NFR-12).
    /// </summary>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Template = ").Append(Template);
        return true;
    }
}

/// <summary>Sends transactional emails through the configured SMTP server (spec: Transactional email).</summary>
public interface IEmailSender
{
    /// <exception cref="EmailDeliveryException">The message could not be handed to the SMTP server.</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled.</exception>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// The message could not be delivered to the SMTP server. Carries no message content and must never
/// wrap a mail-library exception, whose text may include addresses (NFR-12).
/// </summary>
public sealed class EmailDeliveryException : Exception
{
    public EmailDeliveryException()
        : base("The email could not be sent.")
    {
    }

    public EmailDeliveryException(string message)
        : base(message)
    {
    }

    public EmailDeliveryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
