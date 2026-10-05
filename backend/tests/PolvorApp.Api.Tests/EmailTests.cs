using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PolvorApp.Api.Platform.Email;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.SharedKernel.Email;

namespace PolvorApp.Api.Tests;

/// <summary>Spec platform: "Transactional email".</summary>
public sealed class EmailTests(MailpitFixture mailpit)
{
    private const string Subject = "Asunto sintético";
    private const string LinkSentinel = "enlace-secreto-sentinel";

    [Theory]
    [InlineData("Email:SmtpHost")]
    [InlineData("Email:From")]
    [InlineData("App:PublicBaseUrl")]
    public async Task Startup_fails_naming_a_missing_email_setting(string setting)
    {
        await using var factory = new ApiFactory("Host=localhost", settings: new Dictionary<string, string?> { [setting] = null });

        var exception = factory.StartupFailure<OptionsValidationException>();

        Assert.Contains(setting, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Email:From", "not-an-address-sentinel")]
    [InlineData("App:PublicBaseUrl", "ftp://not-web-sentinel")]
    [InlineData("App:PublicBaseUrl", "http://localhost:8080/?q=query-sentinel")]
    [InlineData("Email:SmtpPort", "70000")]
    [InlineData("Email:SmtpPort", "port-sentinel")]
    [InlineData("Email:TimeoutSeconds", "999")]
    [InlineData("Email:Security", "Carrier-pigeon-sentinel")]
    [InlineData("Email:Security", "1")]
    public async Task Startup_fails_on_an_invalid_email_setting_without_disclosing_its_value(string setting, string value)
    {
        await using var factory = new ApiFactory("Host=localhost", settings: new Dictionary<string, string?> { [setting] = value });

        var exception = factory.StartupFailure<OptionsValidationException>();

        Assert.Contains(setting, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(value, exception.Message.Replace(setting, string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Startup_fails_when_only_one_smtp_credential_is_set()
    {
        await using var factory = new ApiFactory("Host=localhost", settings: new Dictionary<string, string?>
        {
            ["Email:Security"] = "StartTls",
            ["Email:Username"] = "user",
        });

        var exception = factory.StartupFailure<OptionsValidationException>();

        Assert.Contains("Email:Password", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Credentials_are_never_sent_over_plain_smtp()
    {
        await using var factory = new ApiFactory("Host=localhost", settings: new Dictionary<string, string?>
        {
            ["Email:Username"] = "user",
            ["Email:Password"] = "password-sentinel",
        });

        var exception = factory.StartupFailure<OptionsValidationException>();

        Assert.Contains("Email:Security", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Outside_local_environments_plain_smtp_and_http_links_are_refused()
    {
        await using var factory = new ApiFactory("Host=localhost", environment: "Production", settings: new Dictionary<string, string?>
        {
            ["Email:Security"] = "None",
            ["App:PublicBaseUrl"] = "http://polvorapp.example",
        });

        // Both options fail, so startup reports them together.
        var exception = Assert.Throws<AggregateException>(() => factory.CreateClient());

        Assert.Contains("Email:Security", exception.Message, StringComparison.Ordinal);
        Assert.Contains("App:PublicBaseUrl", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://polvorapp.example", "invitations/accept", "https://polvorapp.example/invitations/accept?user=u&token=a%2Bb%2F%3D%26")]
    [InlineData("https://polvorapp.example/", "/invitations/accept", "https://polvorapp.example/invitations/accept?user=u&token=a%2Bb%2F%3D%26")]
    [InlineData("https://host.example/polvorapp", "invitations/accept", "https://host.example/polvorapp/invitations/accept?user=u&token=a%2Bb%2F%3D%26")]
    [InlineData("https://host.example/polvorapp/", "/invitations/accept", "https://host.example/polvorapp/invitations/accept?user=u&token=a%2Bb%2F%3D%26")]
    public void Links_keep_the_base_path_and_escape_their_query(string baseUrl, string path, string expected)
    {
        var options = new PublicUrlOptions { PublicBaseUrl = new Uri(baseUrl) };

        var link = options.Link(path, new Dictionary<string, string> { ["user"] = "u", ["token"] = "a+b/=&" });

        Assert.Equal(expected, link.AbsoluteUri);
    }

    [Theory]
    [InlineData("//evil.example/x")]
    [InlineData("https://evil.example/x")]
    [InlineData("path?query=1")]
    [InlineData("")]
    public void Links_cannot_leave_the_ui_origin(string path)
    {
        var options = new PublicUrlOptions { PublicBaseUrl = new Uri("https://polvorapp.example") };

        Assert.Throws<ArgumentException>(() => options.Link(path, new Dictionary<string, string>()));
    }

    [Fact]
    public void A_message_prints_only_its_template()
    {
        var message = new EmailMessage("persona@example.test", "Persona", Subject, LinkSentinel, null, "SyntheticTemplate");

        Assert.Equal("EmailMessage { Template = SyntheticTemplate }", message.ToString());
    }

    [Fact]
    public async Task A_message_is_delivered_with_a_plain_text_part_and_nothing_of_it_is_logged()
    {
        var logs = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var sender = Sender(mailpit.SmtpHost, mailpit.SmtpPortOnHost, loggerFactory);
        const string recipient = "destinataria.sintetica@example.test";

        await sender.SendAsync(Message(recipient, "<p>HTML</p>"), TestContext.Current.CancellationToken);

        var message = await mailpit.WaitForMessageAsync(recipient);
        Assert.Equal(Subject, message.Subject);
        Assert.Contains(LinkSentinel, message.Text, StringComparison.Ordinal);
        Assert.Contains("<p>HTML</p>", message.Html, StringComparison.Ordinal);
        Assert.Contains(logs.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("SyntheticTemplate", StringComparison.Ordinal));
        AssertNothingOfTheMessageIsLogged(logs, recipient);
    }

    [Fact]
    public async Task The_sender_name_and_reply_to_of_the_settings_go_with_the_configured_address()
    {
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var sender = Sender(mailpit.SmtpHost, mailpit.SmtpPortOnHost, loggerFactory, new EmailSenderProfile("Unión de Comparsas · PolvorApp", "secretaria@federacion.example"));
        const string recipient = "remitente.sintetica@example.test";

        await sender.SendAsync(Message(recipient), TestContext.Current.CancellationToken);

        var message = await mailpit.WaitForMessageAsync(recipient);
        Assert.Equal(new MailpitAddress("Unión de Comparsas · PolvorApp", "no-reply@polvorapp.example"), message.From);
        Assert.Equal([new MailpitAddress(string.Empty, "secretaria@federacion.example")], message.ReplyTo);
    }

    [Fact]
    public async Task Without_a_reply_to_none_is_sent()
    {
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var sender = Sender(mailpit.SmtpHost, mailpit.SmtpPortOnHost, loggerFactory, new EmailSenderProfile("PolvorApp", null));
        const string recipient = "sin.respuesta.sintetica@example.test";

        await sender.SendAsync(Message(recipient), TestContext.Current.CancellationToken);

        var message = await mailpit.WaitForMessageAsync(recipient);
        Assert.Equal(new MailpitAddress("PolvorApp", "no-reply@polvorapp.example"), message.From);
        Assert.Empty(message.ReplyTo ?? []);
    }

    [Fact]
    public async Task A_profile_that_cannot_be_read_fails_the_delivery_in_the_compose_phase()
    {
        var logs = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var sender = Sender(mailpit.SmtpHost, mailpit.SmtpPortOnHost, loggerFactory, profile: null);

        var error = await Assert.ThrowsAsync<EmailDeliveryException>(() => sender.SendAsync(Message("sin.perfil@example.test"), TestContext.Current.CancellationToken));

        Assert.Equal(("Compose", "InvalidOperationException"), (error.Phase, error.Code));
        Assert.Contains(logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("Compose failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unreachable_server_raises_a_delivery_exception_and_logs_the_phase()
    {
        var logs = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var sender = Sender("127.0.0.1", RejectingSmtpServer.ClosedPort(), loggerFactory);
        const string recipient = "otra.destinataria@example.test";

        await Assert.ThrowsAsync<EmailDeliveryException>(() => sender.SendAsync(Message(recipient), TestContext.Current.CancellationToken));

        Assert.Contains(logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("Connect failed", StringComparison.Ordinal));
        AssertNothingOfTheMessageIsLogged(logs, recipient);
    }

    [Fact]
    public async Task A_rejected_recipient_raises_a_delivery_exception_with_the_status_code_but_not_the_address()
    {
        await using var server = new RejectingSmtpServer();
        var logs = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var sender = Sender("127.0.0.1", server.Port, loggerFactory);
        const string recipient = "rechazada.sintetica@example.test";

        await Assert.ThrowsAsync<EmailDeliveryException>(() => sender.SendAsync(Message(recipient), TestContext.Current.CancellationToken));

        Assert.Contains(logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("Send failed", StringComparison.Ordinal)
            && e.Message.Contains("550", StringComparison.Ordinal));
        AssertNothingOfTheMessageIsLogged(logs, recipient);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_reported_as_a_delivery_failure()
    {
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var sender = Sender(mailpit.SmtpHost, mailpit.SmtpPortOnHost, loggerFactory);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sender.SendAsync(Message("cancelada@example.test"), cancelled.Token));
    }

    private static EmailMessage Message(string recipient, string? html = null) =>
        new(recipient, "Persona Sintética", Subject, $"Texto con {LinkSentinel}", html, "SyntheticTemplate");

    private static void AssertNothingOfTheMessageIsLogged(CapturingLoggerProvider logs, string recipient)
    {
        foreach (var fragment in new[] { recipient, Subject, LinkSentinel })
        {
            Assert.DoesNotContain(logs.Entries, e =>
                e.Message.Contains(fragment, StringComparison.Ordinal)
                || (e.Exception?.Contains(fragment, StringComparison.Ordinal) ?? false));
        }
    }

    private static SmtpEmailSender Sender(string host, int port, ILoggerFactory loggerFactory) =>
        Sender(host, port, loggerFactory, new EmailSenderProfile("PolvorApp", null));

    /// <summary>A sender whose profile is <paramref name="profile"/>, or whose profile read fails when null.</summary>
    private static SmtpEmailSender Sender(string host, int port, ILoggerFactory loggerFactory, EmailSenderProfile? profile)
    {
        var options = Options.Create(new EmailOptions
        {
            SmtpHost = host,
            SmtpPort = port,
            From = "PolvorApp <no-reply@polvorapp.example>",
            Security = EmailSecurity.None,
            TimeoutSeconds = 5,
        });
        return new SmtpEmailSender(options, new FixedProfile(profile), loggerFactory.CreateLogger<SmtpEmailSender>());
    }

    private sealed class FixedProfile(EmailSenderProfile? profile) : IEmailSenderProfile
    {
        public string SenderAddress => "no-reply@polvorapp.example";

        public Task<EmailSenderProfile> GetAsync(CancellationToken cancellationToken) =>
            profile is null ? throw new InvalidOperationException("The settings cannot be read.") : Task.FromResult(profile);
    }
}
