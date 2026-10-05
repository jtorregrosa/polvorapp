using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.Notifications.Emails;
using PolvorApp.Notifications.Rules;
using PolvorApp.SharedKernel.Email;

namespace PolvorApp.Api.Tests.Notifications;

/// <summary>Spec "Email content" (design D7): every template in the three languages, with only what an email may hold.</summary>
public sealed class NotificationEmailsTests
{
    private const string Base = "https://polvorapp.example/app";
    private static readonly Guid OrderId = Guid.Parse("0190a8b0-0000-7000-8000-000000000001");
    private static readonly Guid EditionId = Guid.Parse("0190a8b0-0000-7000-8000-000000000002");

    public static TheoryData<string, string, string> ClosingDates => new()
    {
        { "es-ES", "10 de febrero de 2031", "Los pedidos de las Fiestas 2031 cierran el 10 de febrero de 2031" },
        { "ca-ES-valencia", "10 de febrer de 2031", "Les comandes de les Festes 2031 es tanquen el 10 de febrer de 2031" },
        { "en", "10 February 2031", "The orders of Fiestas 2031 close on 10 February 2031" },
    };

    private static readonly NotificationContent[] AllContents =
    [
        new NotificationContent.OrdersOpened(2031, new DateOnly(2031, 2, 10)),
        new NotificationContent.OrdersClosed(2031),
        new NotificationContent.OrdersClosing(CloseReminder.Week, "Comparsa Sintética Sur", 2031, new DateOnly(2031, 2, 10), new DateOnly(2031, 2, 3), PendingOrderState.Draft, OrderId),
        new NotificationContent.OrdersClosing(CloseReminder.LastDays, "Comparsa Sintética Sur", 2031, new DateOnly(2031, 2, 10), new DateOnly(2031, 2, 9), PendingOrderState.NotPrepared, null),
        new NotificationContent.OrderSubmitted("Comparsa Sintética Norte", 2031, OrderId),
        new NotificationContent.OrderReturned("Comparsa Sintética Sur", 2031, OrderId),
        new NotificationContent.OrderValidated("Comparsa Sintética Sur", 2031, OrderId),
        new NotificationContent.MilestoneReminder("Plazo sintético", new DateOnly(2030, 11, 30), 2031, EditionId),
        new NotificationContent.LicenseDigest(new DateOnly(2031, 1, 1), [("Comparsa Sintética Norte", new LicenseDigestCounts(Guid.Empty, 1, 0, 2, 3))]),
    ];

    public static TheoryData<int> Contents => new(Enumerable.Range(0, AllContents.Length));

    [Theory]
    [MemberData(nameof(Contents))]
    public void Every_template_renders_in_every_language_with_its_footer_and_links(int index)
    {
        var content = AllContents[index];
        foreach (var locale in new[] { "es-ES", "ca-ES-valencia", "en" })
        {
            var email = Emails().Render(Recipient(locale), content, Federation());

            Assert.Equal(content.Template, email.Template);
            Assert.Equal("persona.sintetica@example.test", email.ToAddress);
            Assert.False(string.IsNullOrWhiteSpace(email.Subject));
            Assert.DoesNotContain('\n', email.Subject);
            Assert.StartsWith(locale == "en" ? "Hello Persona Sintética," : "Hola, Persona Sintética:", email.TextBody, StringComparison.Ordinal);
            Assert.EndsWith($"{Base}/account?section=notifications\n\nUnión de Comparsas", email.TextBody, StringComparison.Ordinal);
            Assert.Contains($"{Base}/", email.TextBody, StringComparison.Ordinal);
            Assert.DoesNotContain("{", email.TextBody, StringComparison.Ordinal);
            Assert.Contains($"<a href=\"{Base}/account?section=notifications\">", email.HtmlBody, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(ClosingDates))]
    public void Dates_are_written_in_the_recipients_language(string locale, string date, string sentence)
    {
        var email = Emails().Render(
            Recipient(locale),
            new NotificationContent.OrdersClosing(CloseReminder.Week, "Comparsa Sintética Sur", 2031, new DateOnly(2031, 2, 10), new DateOnly(2031, 2, 3), PendingOrderState.Draft, OrderId), Federation());

        Assert.Contains(date, email.Subject, StringComparison.Ordinal);
        Assert.Contains(sentence, email.TextBody, StringComparison.Ordinal);
        Assert.Contains($"{Base}/orders/{OrderId}", email.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Valencian_dates_use_the_elided_preposition_before_a_vowel()
    {
        var email = Emails().Render(Recipient("ca-ES-valencia"), new NotificationContent.MilestoneReminder("Curs sintètic", new DateOnly(2031, 4, 1), 2031, EditionId), Federation());

        Assert.Contains("1 d’abril de 2031", email.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void The_last_reminder_says_tomorrow_or_today()
    {
        NotificationContent Closing(DateOnly today) =>
            new NotificationContent.OrdersClosing(CloseReminder.LastDays, "Comparsa Sintética Sur", 2031, new DateOnly(2031, 2, 10), today, PendingOrderState.Returned, OrderId);

        var tomorrow = Emails().Render(Recipient("es-ES"), Closing(new DateOnly(2031, 2, 9)), Federation());
        var today = Emails().Render(Recipient("es-ES"), Closing(new DateOnly(2031, 2, 10)), Federation());

        Assert.Contains("cierran mañana, el 10 de febrero de 2031, y el pedido de Comparsa Sintética Sur está devuelto", tomorrow.TextBody, StringComparison.Ordinal);
        Assert.Contains("cierran hoy", today.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void The_return_email_asks_to_read_the_reason_in_the_app()
    {
        var email = Emails().Render(Recipient("es-ES"), new NotificationContent.OrderReturned("Comparsa Sintética Sur", 2031, OrderId), Federation());

        Assert.Equal("Pedido devuelto: Comparsa Sintética Sur, Fiestas 2031", email.Subject);
        Assert.Contains("Lee el motivo y corrige el pedido en PolvorApp", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("estado de los pedidos", email.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void The_digest_lists_counts_per_comparsa_and_leaves_zero_lines_out()
    {
        var email = Emails().Render(
            Recipient("es-ES"),
            new NotificationContent.LicenseDigest(
                new DateOnly(2031, 1, 1),
                [("Comparsa Sintética Norte", new LicenseDigestCounts(Guid.Empty, 0, 1, 0, 2)), ("Comparsa Sintética Sur", new LicenseDigestCounts(Guid.Empty, 3, 0, 0, 0))]), Federation());

        Assert.Equal("Resumen de licencias de enero de 2031", email.Subject);
        Assert.Contains("Comparsa Sintética Norte\n- Licencia en trámite: 1\n- Caduca en los próximos 90 días: 2\n\nComparsa Sintética Sur\n- Sin licencia: 3", email.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("caducada", email.TextBody, StringComparison.Ordinal);
        Assert.Contains($"{Base}/\n", email.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Names_and_titles_are_html_encoded()
    {
        var email = Emails().Render(Recipient("en"), new NotificationContent.MilestoneReminder("<b>Course</b> & co", new DateOnly(2031, 4, 1), 2031, EditionId), Federation());

        Assert.Contains("&lt;b&gt;Course&lt;/b&gt; &amp; co", email.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>", email.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void The_three_text_files_have_the_same_keys()
    {
        var folder = Path.Combine(RepositoryRoot(), "backend", "src", "Modules", "Notifications", "PolvorApp.Notifications", "Emails");
        HashSet<string> Keys(string file) => System.Xml.Linq.XDocument.Load(Path.Combine(folder, file)).Root!
            .Elements("data").Select(d => (string)d.Attribute("name")!).ToHashSet(StringComparer.Ordinal);

        var spanish = Keys("NotificationTexts.resx");

        Assert.Equal(spanish.Order(StringComparer.Ordinal), Keys("NotificationTexts.ca.resx").Order(StringComparer.Ordinal));
        Assert.Equal(spanish.Order(StringComparer.Ordinal), Keys("NotificationTexts.en.resx").Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("es-ES", "Contacto: info@federacion.example")]
    [InlineData("ca-ES-valencia", "Contacte: info@federacion.example")]
    [InlineData("en", "Contact: info@federacion.example")]
    public void The_footer_ends_with_the_Federation_short_name_and_its_public_contact(string locale, string contact)
    {
        var federation = Federation() with { ShortName = "Unión Sintética", ContactEmail = "info@federacion.example", Website = "https://federacion.example/?a=1&b=<2>" };

        var email = Emails().Render(Recipient(locale), new NotificationContent.OrdersClosed(2031), federation);

        Assert.EndsWith($"{Base}/account?section=notifications\n\nUnión Sintética\n{contact}\nhttps://federacion.example/?a=1&b=<2>", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("https://federacion.example/?a=1&amp;b=&lt;2&gt;", email.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"https://federacion.example", email.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_locale_falls_back_to_spanish()
    {
        var email = Emails().Render(Recipient("fr-FR"), new NotificationContent.OrdersClosed(2031), Federation());

        Assert.Equal("Pedidos cerrados: Fiestas 2031", email.Subject);
    }

    [Fact]
    public void A_missing_text_fails_instead_of_sending_its_key()
    {
        var emails = new NotificationEmails(new MissingTexts(), Urls(), NullLogger<NotificationEmails>.Instance);

        Assert.Throws<InvalidOperationException>(() => emails.Render(Recipient("es-ES"), new NotificationContent.OrdersClosed(2031), Federation()));
    }

    [Fact]
    public void The_logged_form_of_an_email_is_its_template_only()
    {
        var email = Emails().Render(Recipient("es-ES"), new NotificationContent.OrderValidated("Comparsa Sintética Sur", 2031, OrderId), Federation());

        Assert.DoesNotContain("example.test", email.ToString(), StringComparison.Ordinal);
        Assert.Contains("OrderValidated", email.ToString(), StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }

    private static UserSummary Recipient(string locale) =>
        new(Guid.CreateVersion7(), "Persona Sintética", "persona.sintetica@example.test", UserRole.FiringChief, UserStatus.Active, locale);

    private static FederationSettingsSnapshot Federation() =>
        new("Unión Sintética de Comparsas", "Unió Sintètica de Comparses", "Unión de Comparsas", null, null, "PolvorApp", null, 7, 7);

    private static IOptions<PublicUrlOptions> Urls() => Options.Create(new PublicUrlOptions { PublicBaseUrl = new Uri(Base) });

    private static NotificationEmails Emails()
    {
        var services = new ServiceCollection().AddLogging().AddLocalization().BuildServiceProvider();
        return new NotificationEmails(services.GetRequiredService<IStringLocalizer<NotificationTexts>>(), Urls(), NullLogger<NotificationEmails>.Instance);
    }

    private sealed class MissingTexts : IStringLocalizer<NotificationTexts>
    {
        public LocalizedString this[string name] => new(name, name, resourceNotFound: true);

        public LocalizedString this[string name, params object[] arguments] => new(name, name, resourceNotFound: true);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
