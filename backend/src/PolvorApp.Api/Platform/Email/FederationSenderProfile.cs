using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.SharedKernel.Email;

namespace PolvorApp.Api.Platform.Email;

/// <summary>
/// The sender profile from the Federation settings (add-federation-settings, design D4), read in a
/// scope of its own for every message, so an Admin's change applies to the next email. When the
/// settings cannot be read the message is not sent and the delivery rules retry it: no name is invented.
/// </summary>
internal sealed class FederationSenderProfile(IServiceScopeFactory scopes) : IEmailSenderProfile
{
    public async Task<EmailSenderProfile> GetAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var settings = await scope.ServiceProvider.GetRequiredService<IFederationSettings>().GetAsync(cancellationToken);
        return new EmailSenderProfile(settings.SenderName, settings.ReplyTo);
    }
}
