using System.Net.Sockets;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using PolvorApp.SharedKernel.Email;

namespace PolvorApp.Api.Platform.Email;

/// <summary>
/// SMTP delivery with MailKit (design D9). One connection per message: volume is a few emails a
/// day. Logs contain the template, the failing phase and PII-free error codes only — never
/// addresses, subjects, bodies or server messages, which may hold personal data or one-time links
/// (NFR-12).
/// </summary>
internal sealed partial class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private enum Phase
    {
        Compose,
        Connect,
        Authenticate,
        Send,
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settings = options.Value;
        // Validated at startup (EmailOptionsValidator); these guards only satisfy nullability.
        var host = settings.SmtpHost ?? throw new InvalidOperationException("The Email:SmtpHost setting is required.");
        var from = settings.From ?? throw new InvalidOperationException("The Email:From setting is required.");

        // MailKit's own timeout applies per socket operation; this deadline bounds the whole send.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

        using var client = new SmtpClient { Timeout = settings.TimeoutSeconds * 1000 };
        var phase = Phase.Compose;
        try
        {
            using var mime = Compose(message, from);
            phase = Phase.Connect;
            await client.ConnectAsync(host, settings.SmtpPort, ToSocketOptions(settings.Security), deadline.Token);
            if (settings is { Username: { Length: > 0 } username, Password: { } password })
            {
                phase = Phase.Authenticate;
                await client.AuthenticateAsync(username, password, deadline.Token);
            }

            phase = Phase.Send;
            await client.SendAsync(mime, deadline.Token);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogFailed(logger, message.Template, phase.ToString(), exception.GetType().Name, ErrorCode(exception));
            throw new EmailDeliveryException();
        }

        // The server accepted the message: a failing QUIT must not report it as unsent.
        await DisconnectQuietlyAsync(client, message.Template);
        LogSent(logger, message.Template);
    }

    private async Task DisconnectQuietlyAsync(SmtpClient client, string template)
    {
        try
        {
            await client.DisconnectAsync(quit: true, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or SocketException or SmtpProtocolException or SmtpCommandException)
        {
            LogDisconnectFailed(logger, template);
        }
    }

    private static MimeMessage Compose(EmailMessage message, string from)
    {
        var mime = new MimeMessage { Subject = message.Subject };
        mime.From.Add(MailboxAddress.Parse(from));
        mime.To.Add(new MailboxAddress(message.ToName ?? string.Empty, message.ToAddress));
        mime.Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody();
        return mime;
    }

    /// <summary>Codes that explain a failure without personal data (the texts may contain addresses).</summary>
    private static string ErrorCode(Exception exception) => exception switch
    {
        SmtpCommandException command => $"{(int)command.StatusCode} {command.ErrorCode}",
        SocketException socket => socket.SocketErrorCode.ToString(),
        OperationCanceledException => "Timeout",
        _ => "-",
    };

    private static SecureSocketOptions ToSocketOptions(EmailSecurity security) => security switch
    {
        EmailSecurity.None => SecureSocketOptions.None,
        EmailSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        _ => SecureSocketOptions.StartTls,
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Email {Template} sent")]
    private static partial void LogSent(ILogger logger, string template);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email {Template} could not be sent: {Phase} failed ({ErrorType}, {ErrorCode})")]
    private static partial void LogFailed(ILogger logger, string template, string phase, string errorType, string errorCode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Email {Template} was sent but closing the SMTP connection failed")]
    private static partial void LogDisconnectFailed(ILogger logger, string template);
}
