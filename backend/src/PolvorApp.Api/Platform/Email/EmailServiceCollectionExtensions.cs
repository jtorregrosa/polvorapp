using Microsoft.Extensions.Options;
using PolvorApp.SharedKernel.Email;

namespace PolvorApp.Api.Platform.Email;

internal static class EmailServiceCollectionExtensions
{
    /// <summary>Registers validated SMTP and public-URL settings and the SMTP sender (NFR-11).</summary>
    public static IServiceCollection AddPlatformEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<EmailOptions>, EmailOptionsValidator>();
        services.AddSingleton<IValidateOptions<PublicUrlOptions>, PublicUrlOptionsValidator>();
        var email = services.AddOptions<EmailOptions>().Configure(o => EmailOptions.Bind(o, configuration));
        var publicUrl = services.AddOptions<PublicUrlOptions>().Configure(o =>
            o.PublicBaseUrl = Uri.TryCreate(configuration[PublicUrlOptions.Key], UriKind.Absolute, out var url) ? url : null);

        // Build-time OpenAPI generation runs Program without configuration (bootstrap-platform D5).
        if (!BuildTimeDocumentGeneration.IsActive)
        {
            email.ValidateOnStart();
            publicUrl.ValidateOnStart();
        }

        services.AddSingleton<IEmailSenderProfile, FederationSenderProfile>();
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddSingleton<BackgroundEmailOutbox>();
        services.AddSingleton<IEmailOutbox>(provider => provider.GetRequiredService<BackgroundEmailOutbox>());
        services.AddHostedService(provider => provider.GetRequiredService<BackgroundEmailOutbox>());
        return services;
    }
}
