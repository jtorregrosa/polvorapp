using System.Globalization;
using Microsoft.Extensions.Options;
using MimeKit;
using PolvorApp.SharedKernel.Email;
using PolvorApp.SharedKernel.Hosting;

namespace PolvorApp.Api.Platform.Email;

/// <summary>How the SMTP connection is secured.</summary>
internal enum EmailSecurity
{
    /// <summary>Plain SMTP: only for the local mail catcher (Development and Testing).</summary>
    None,

    /// <summary>Upgrade with STARTTLS (usually port 587).</summary>
    StartTls,

    /// <summary>TLS from the first byte (usually port 465).</summary>
    SslOnConnect,
}

/// <summary>SMTP settings from <c>Email__*</c> environment variables (spec: Transactional email).</summary>
internal sealed class EmailOptions
{
    public const string Section = "Email";
    public const int MaxTimeoutSeconds = 120;

    public string? SmtpHost { get; set; }

    public int SmtpPort { get; set; } = 587;

    public EmailSecurity Security { get; set; } = EmailSecurity.StartTls;

    /// <summary>
    /// Sender address, e.g. <c>no-reply@example.org</c>. Only the address counts: the name shown with it
    /// is the sender name of the Federation settings (add-federation-settings).
    /// </summary>
    public string? From { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }

    /// <summary>Deadline for the whole send (connect, TLS, authentication and transfer).</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Settings present in configuration that could not be parsed; reported by name only.</summary>
    public IReadOnlyList<string> UnparsableSettings { get; set; } = [];

    /// <summary>
    /// Reads the settings as strings and parses them here, so an invalid value never reaches an
    /// exception message (the configuration binder would include it).
    /// </summary>
    public static void Bind(EmailOptions options, IConfiguration configuration)
    {
        var section = configuration.GetSection(Section);
        var unparsable = new List<string>();
        options.SmtpHost = section["SmtpHost"];
        options.From = section["From"];
        options.Username = section["Username"];
        options.Password = section["Password"];

        if (section["SmtpPort"] is { } port)
        {
            if (int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                options.SmtpPort = parsed;
            }
            else
            {
                unparsable.Add($"{Section}:SmtpPort");
            }
        }

        if (section["TimeoutSeconds"] is { } timeout)
        {
            if (int.TryParse(timeout, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                options.TimeoutSeconds = parsed;
            }
            else
            {
                unparsable.Add($"{Section}:TimeoutSeconds");
            }
        }

        if (section["Security"] is { } security)
        {
            // Names only: Enum.TryParse would also accept numbers such as "1".
            var match = Enum.GetValues<EmailSecurity>()
                .Where(value => string.Equals(value.ToString(), security, StringComparison.OrdinalIgnoreCase))
                .Select(value => (EmailSecurity?)value)
                .FirstOrDefault();
            if (match is { } parsed)
            {
                options.Security = parsed;
            }
            else
            {
                unparsable.Add($"{Section}:Security");
            }
        }

        options.UnparsableSettings = unparsable;
    }
}

/// <summary>Fails startup naming the setting, never its value (spec: Configuration from environment).</summary>
internal sealed class EmailOptionsValidator(IHostEnvironment environment) : IValidateOptions<EmailOptions>
{
    private const int MaxPort = 65535;

    public ValidateOptionsResult Validate(string? name, EmailOptions options)
    {
        var failures = new List<string>();
        failures.AddRange(options.UnparsableSettings.Select(setting => $"The {setting} setting is not valid."));

        if (string.IsNullOrWhiteSpace(options.SmtpHost))
        {
            failures.Add("The Email:SmtpHost setting is required.");
        }

        if (options.SmtpPort is < 1 or > MaxPort)
        {
            failures.Add("The Email:SmtpPort setting is not a valid port.");
        }

        if (options.TimeoutSeconds is < 1 or > EmailOptions.MaxTimeoutSeconds)
        {
            failures.Add($"The Email:TimeoutSeconds setting must be between 1 and {EmailOptions.MaxTimeoutSeconds}.");
        }

        if (string.IsNullOrWhiteSpace(options.From))
        {
            failures.Add("The Email:From setting is required.");
        }
        else if (!MailboxAddress.TryParse(options.From, out var sender) || string.IsNullOrEmpty(sender.Domain))
        {
            failures.Add("The Email:From setting is not a valid address.");
        }

        var hasUsername = !string.IsNullOrEmpty(options.Username);
        if (hasUsername != !string.IsNullOrEmpty(options.Password))
        {
            failures.Add("The Email:Username and Email:Password settings must be set together.");
        }

        if (options.Security == EmailSecurity.None)
        {
            if (hasUsername)
            {
                failures.Add("The Email:Security setting must not be None when SMTP credentials are set.");
            }

            if (!LocalEnvironments.IsLocal(environment))
            {
                failures.Add("The Email:Security setting must not be None outside Development and Testing.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>
/// Validates <see cref="PublicUrlOptions"/>: an absolute http(s) URL without query, fragment or user
/// info, and https outside Development and Testing, because links carry one-time tokens.
/// </summary>
internal sealed class PublicUrlOptionsValidator(IHostEnvironment environment) : IValidateOptions<PublicUrlOptions>
{
    public ValidateOptionsResult Validate(string? name, PublicUrlOptions options)
    {
        var url = options.PublicBaseUrl;
        var valid = url is { IsAbsoluteUri: true }
            && (url.Scheme == Uri.UriSchemeHttps || (url.Scheme == Uri.UriSchemeHttp && LocalEnvironments.IsLocal(environment)))
            && url.Query.Length == 0 && url.Fragment.Length == 0 && url.UserInfo.Length == 0;
        return valid
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"The {PublicUrlOptions.Key} setting must be an absolute https URL (http only locally) without query or fragment.");
    }
}
