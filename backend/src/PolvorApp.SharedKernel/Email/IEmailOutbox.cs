namespace PolvorApp.SharedKernel.Email;

/// <summary>
/// Sends emails after the request has been answered, for flows whose response time must not
/// depend on the SMTP server (e.g. password reset: timing must not reveal whether an account
/// exists). Failures are logged by the sender; the message is not persisted, so a lost email is
/// recovered by asking again.
/// </summary>
public interface IEmailOutbox
{
    /// <summary>Queues the message; never throws for delivery problems.</summary>
    void Enqueue(EmailMessage message);
}
