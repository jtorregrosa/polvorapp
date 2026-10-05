namespace PolvorApp.SharedKernel.Email;

/// <summary>
/// How PolvorApp's emails present themselves (add-federation-settings, design D4): the display name
/// shown with the deployment's sender address and, when set, the reply-to address.
/// </summary>
/// <param name="DisplayName">The sender name; the address itself stays a deployment setting.</param>
/// <param name="ReplyTo">Where replies go, or null for none.</param>
public sealed record EmailSenderProfile(string DisplayName, string? ReplyTo);

/// <summary>Reads the current <see cref="EmailSenderProfile"/> for each message.</summary>
public interface IEmailSenderProfile
{
    /// <summary>The deployment's sender address (<c>Email:From</c>, validated at start-up), without any display name.</summary>
    string SenderAddress { get; }

    Task<EmailSenderProfile> GetAsync(CancellationToken cancellationToken);
}
