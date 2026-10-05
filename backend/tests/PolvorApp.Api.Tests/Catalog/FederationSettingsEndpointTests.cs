using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.IdentityAccess.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Catalog;

/// <summary>
/// Spec "Federation settings" (add-federation-settings, design D2): Admins read every section and
/// save one at a time against the current version; every signed-in user reads the identity through
/// <c>GET /federation</c>; every save is audited with the previous and new values.
/// </summary>
public sealed class FederationSettingsEndpointTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private IdentityTestHost _host = null!;
    private HttpClient _admin = null!;
    private HttpClient _chief = null!;
    private Guid _adminId;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = await IdentityTestHost.StartAsync(postgres, mailpit);
        var admin = await _host.CreateUserAsync("admin.ajustes@example.test", UserRole.Admin);
        _adminId = admin.Id;
        _admin = await _host.SignInAsync(admin);
        _chief = await _host.SignInAsync(await _host.CreateUserAsync("jefe.ajustes@example.test"));
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        _chief.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task An_Admin_reads_every_section_with_the_starting_values()
    {
        var settings = await SettingsAsync();

        var identity = settings.GetProperty("identity");
        Assert.Equal(
            ("Unión de Comparsas de Moros y Cristianos «Ber-Largas»", "Unió de Comparses de Moros i Cristians «Ber-Largas»", "Unión de Comparsas"),
            (identity.GetProperty("officialNameEs").GetString(), identity.GetProperty("officialNameCa").GetString(), identity.GetProperty("shortName").GetString()));
        Assert.Equal(JsonValueKind.Null, identity.GetProperty("contactEmail").ValueKind);
        Assert.Equal(JsonValueKind.Null, identity.GetProperty("website").ValueKind);
        var emails = settings.GetProperty("emails");
        Assert.Equal("PolvorApp", emails.GetProperty("senderName").GetString());
        Assert.Equal(JsonValueKind.Null, emails.GetProperty("replyTo").ValueKind);
        Assert.False(string.IsNullOrEmpty(emails.GetProperty("senderAddress").GetString()));
        Assert.Equal(7, settings.GetProperty("orders").GetProperty("closeReminderLeadDays").GetInt32());
        Assert.Equal(7, settings.GetProperty("calendar").GetProperty("milestoneLeadDays").GetInt32());
        Assert.True(settings.GetProperty("version").GetUInt32() > 0);
    }

    [Fact]
    public async Task An_Admin_saves_each_section_and_each_save_is_audited_with_previous_and_new_values()
    {
        var settings = await SaveAsync("identity", new
        {
            officialNameEs = "  Unión Sintética de Comparsas  ",
            officialNameCa = "Unió Sintètica de Comparses",
            shortName = "Unión Sintética",
            contactEmail = "info@federacion.example",
            website = "https://federacion.example/",
        });
        settings = await SaveAsync("emails", new { senderName = "Unión de Comparsas · PolvorApp", replyTo = "secretaria@federacion.example" }, settings);
        settings = await SaveAsync("orders", new { closeReminderLeadDays = 10 }, settings);
        settings = await SaveAsync("calendar", new { milestoneLeadDays = 3 }, settings);

        Assert.Equal("Unión Sintética de Comparsas", settings.GetProperty("identity").GetProperty("officialNameEs").GetString());
        Assert.Equal("https://federacion.example/", settings.GetProperty("identity").GetProperty("website").GetString());
        Assert.Equal("secretaria@federacion.example", settings.GetProperty("emails").GetProperty("replyTo").GetString());
        Assert.Equal((10, 3), (settings.GetProperty("orders").GetProperty("closeReminderLeadDays").GetInt32(), settings.GetProperty("calendar").GetProperty("milestoneLeadDays").GetInt32()));
        var entries = await _host.AuditEntriesAsync("FederationSettingsChanged");
        Assert.Equal(["calendar", "emails", "identity", "orders"], entries.Select(e => Section(e.Data!)).Order(StringComparer.Ordinal));
        using var emails = JsonDocument.Parse(entries.Single(e => Section(e.Data!) == "emails").Data!);
        Assert.Equal("PolvorApp", emails.RootElement.GetProperty("previous").GetProperty("senderName").GetString());
        Assert.Equal("Unión de Comparsas · PolvorApp", emails.RootElement.GetProperty("current").GetProperty("senderName").GetString());
        Assert.All(entries, e => Assert.Equal(("FederationSettings", "1", (Guid?)_adminId), (e.EntityType, e.EntityId, e.ActorUserId)));
    }

    [Fact]
    public async Task An_unchanged_save_is_not_audited()
    {
        await SaveAsync("calendar", new { milestoneLeadDays = 7 });

        Assert.Empty(await _host.AuditEntriesAsync("FederationSettingsChanged"));
    }

    [Fact]
    public async Task Clearing_the_optional_values_stores_none()
    {
        var settings = await SaveAsync("emails", new { senderName = "PolvorApp", replyTo = "secretaria@federacion.example" });

        settings = await SaveAsync("emails", new { senderName = "PolvorApp", replyTo = "  " }, settings);

        Assert.Equal(JsonValueKind.Null, settings.GetProperty("emails").GetProperty("replyTo").ValueKind);
    }

    [Theory]
    [MemberData(nameof(InvalidSaves))]
    public async Task Invalid_values_are_refused_by_field(string section, object body, string field, string reason)
    {
        var version = (await SettingsAsync()).GetProperty("version").GetUInt32();

        using var response = await PutAsync(section, body, version);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal(new Dictionary<string, string> { [field] = reason }, await ErrorsAsync(response));
        Assert.Empty(await _host.AuditEntriesAsync("FederationSettingsChanged"));
        Assert.Equal(version, (await SettingsAsync()).GetProperty("version").GetUInt32());
    }

    public static TheoryData<string, object, string, string> InvalidSaves() => new()
    {
        { "identity", Identity(officialNameEs: "   "), "officialNameEs", "required" },
        { "identity", Identity(officialNameCa: new string('a', 151)), "officialNameCa", "tooLong" },
        { "identity", Identity(shortName: new string('a', 41)), "shortName", "tooLong" },
        { "identity", Identity(contactEmail: "sin-arroba"), "contactEmail", "invalid" },
        { "identity", Identity(contactEmail: "info@"), "contactEmail", "invalid" },
        { "identity", Identity(contactEmail: "Nombre <info@federacion.example>"), "contactEmail", "invalid" },
        { "identity", Identity(website: "http://federacion.example"), "website", "invalid" },
        { "identity", Identity(website: "federacion.example"), "website", "invalid" },
        { "identity", Identity(website: "https://" + new string('a', 200) + ".example"), "website", "tooLong" },
        { "identity", Identity(website: "https:\\federacion.example"), "website", "invalid" },
        { "identity", Identity(website: "https://federacion.example/<b>"), "website", "invalid" },
        { "identity", Identity(website: "https://federacion.example/\"x"), "website", "invalid" },
        { "identity", Identity(website: "https://localhost"), "website", "invalid" },
        { "identity", Identity(website: "https://[::1]/"), "website", "invalid" },
        { "identity", Identity(website: "https://192.0.2.10/"), "website", "invalid" },
        { "identity", Identity(website: "https://usuario@federacion.example/"), "website", "invalid" },
        { "identity", Identity(website: "https://\u0430\u0440\u0440\u04CF\u0435.example/"), "website", "invalid" },
        { "emails", new { senderName = "Unión\r\nBcc: a@b.example" }, "senderName", "invalid" },
        { "emails", new { senderName = "Unión <falsa>" }, "senderName", "invalid" },
        { "emails", new { senderName = "\"Unión\"" }, "senderName", "invalid" },
        { "emails", new { senderName = "soporte@banco.example" }, "senderName", "invalid" },
        { "emails", new { senderName = "Unión\u2028Bcc" }, "senderName", "invalid" },
        { "emails", new { senderName = "Unión\u202Eevil" }, "senderName", "invalid" },
        { "emails", new { senderName = new string('a', 81) }, "senderName", "tooLong" },
        { "emails", new { senderName = "" }, "senderName", "required" },
        { "emails", new { senderName = "PolvorApp", replyTo = "no es correo" }, "replyTo", "invalid" },
        { "orders", new { closeReminderLeadDays = 1 }, "closeReminderLeadDays", "outOfRange" },
        { "orders", new { closeReminderLeadDays = 15 }, "closeReminderLeadDays", "outOfRange" },
        { "orders", new { }, "closeReminderLeadDays", "required" },
        { "calendar", new { milestoneLeadDays = 0 }, "milestoneLeadDays", "outOfRange" },
        { "calendar", new { milestoneLeadDays = 30 }, "milestoneLeadDays", "outOfRange" },
    };

    [Fact]
    public async Task Values_at_the_limits_are_accepted_and_padding_is_trimmed()
    {
        var settings = await SaveAsync("identity", new
        {
            officialNameEs = "  " + new string('e', 150) + "  ",
            officialNameCa = new string('c', 150),
            shortName = new string('s', 40),
            contactEmail = "info@federacion.example",
            website = "https://federacion.example",
        });
        settings = await SaveAsync("emails", new { senderName = new string('n', 80) }, settings);
        settings = await SaveAsync("orders", new { closeReminderLeadDays = 2 }, settings);
        settings = await SaveAsync("calendar", new { milestoneLeadDays = 1 }, settings);
        settings = await SaveAsync("orders", new { closeReminderLeadDays = 14 }, settings);
        settings = await SaveAsync("calendar", new { milestoneLeadDays = 14 }, settings);

        Assert.Equal(new string('e', 150), settings.GetProperty("identity").GetProperty("officialNameEs").GetString());
        Assert.Equal("https://federacion.example", settings.GetProperty("identity").GetProperty("website").GetString());
        Assert.Equal(80, settings.GetProperty("emails").GetProperty("senderName").GetString()!.Length);
        Assert.Equal((14, 14), (settings.GetProperty("orders").GetProperty("closeReminderLeadDays").GetInt32(), settings.GetProperty("calendar").GetProperty("milestoneLeadDays").GetInt32()));
    }

    [Fact]
    public async Task A_save_answers_with_a_new_version_and_an_unchanged_one_keeps_it()
    {
        var before = (await SettingsAsync()).GetProperty("version").GetUInt32();

        var changed = await SaveAsync("calendar", new { milestoneLeadDays = 5 });
        var unchanged = await SaveAsync("calendar", new { milestoneLeadDays = 5 }, changed);

        Assert.NotEqual(before, changed.GetProperty("version").GetUInt32());
        Assert.Equal(changed.GetProperty("version").GetUInt32(), unchanged.GetProperty("version").GetUInt32());
    }

    [Fact]
    public async Task Of_two_saves_on_the_same_version_one_wins_and_the_other_is_a_conflict()
    {
        var version = (await SettingsAsync()).GetProperty("version").GetUInt32();

        var responses = await Task.WhenAll(
            PutAsync("emails", new { senderName = "Primera Administradora" }, version),
            PutAsync("emails", new { senderName = "Segunda Administradora" }, version));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).Order());
        Assert.Single(await _host.AuditEntriesAsync("FederationSettingsChanged"));
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task A_save_based_on_an_older_version_is_a_conflict()
    {
        var stale = (await SettingsAsync()).GetProperty("version").GetUInt32();
        await SaveAsync("emails", new { senderName = "Primera Administradora" });

        using var response = await PutAsync("emails", new { senderName = "Segunda Administradora" }, stale);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "federationSettings.modified");
        Assert.Equal("Primera Administradora", (await SettingsAsync()).GetProperty("emails").GetProperty("senderName").GetString());
    }

    [Fact]
    public async Task A_save_without_a_version_is_refused()
    {
        using var response = await _admin.PutAsJsonAsync("/api/federation-settings/calendar", new { milestoneLeadDays = 3 }, Token);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal("required", (await ErrorsAsync(response))["version"]);
    }

    [Fact]
    public async Task A_FiringChief_can_neither_read_nor_change_the_settings()
    {
        var version = (await SettingsAsync()).GetProperty("version").GetUInt32();

        using var read = await _chief.GetAsync("/api/federation-settings", Token);
        using var identity = await PutAsync("identity", Identity(shortName: "Intrusa"), version, _chief);
        using var emails = await PutAsync("emails", new { senderName = "Intrusa" }, version, _chief);
        using var orders = await PutAsync("orders", new { closeReminderLeadDays = 3 }, version, _chief);
        using var calendar = await PutAsync("calendar", new { milestoneLeadDays = 3 }, version, _chief);

        Assert.All([read, identity, emails, orders, calendar], r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
        Assert.Equal(version, (await SettingsAsync()).GetProperty("version").GetUInt32());
        Assert.Empty(await _host.AuditEntriesAsync("FederationSettingsChanged"));
    }

    [Fact]
    public async Task Every_signed_in_user_reads_the_identity_with_the_logo_flag()
    {
        await SaveAsync("identity", Identity(shortName: "Unión Sintética"));

        using var response = await _chief.GetAsync("/api/federation", Token);

        var federation = await ReadAsync<JsonElement>(response);
        Assert.Equal("Unión Sintética", federation.GetProperty("shortName").GetString());
        Assert.Equal("Unión de Comparsas de Moros y Cristianos «Ber-Largas»", federation.GetProperty("officialNameEs").GetString());
        Assert.Equal("Unió de Comparses de Moros i Cristians «Ber-Largas»", federation.GetProperty("officialNameCa").GetString());
        Assert.Equal(JsonValueKind.Null, federation.GetProperty("logo").ValueKind);
        Assert.False(federation.TryGetProperty("senderName", out _));
    }

    [Fact]
    public async Task An_anonymous_user_reads_nothing()
    {
        using var anonymous = await _host.NewClientAsync();

        using var settings = await anonymous.GetAsync("/api/federation-settings", Token);
        using var federation = await anonymous.GetAsync("/api/federation", Token);

        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized), (settings.StatusCode, federation.StatusCode));
    }

    private static object Identity(
        string officialNameEs = "Unión de Comparsas de Moros y Cristianos «Ber-Largas»",
        string officialNameCa = "Unió de Comparses de Moros i Cristians «Ber-Largas»",
        string shortName = "Unión de Comparsas",
        string? contactEmail = null,
        string? website = null) =>
        new { officialNameEs, officialNameCa, shortName, contactEmail, website };

    private static string Section(string data)
    {
        using var document = JsonDocument.Parse(data);
        return document.RootElement.GetProperty("section").GetString()!;
    }

    private async Task<JsonElement> SettingsAsync()
    {
        using var response = await _admin.GetAsync("/api/federation-settings", Token);
        return await ReadAsync<JsonElement>(response);
    }

    /// <summary>Saves a section on top of <paramref name="current"/> (or the stored settings) and returns the settings.</summary>
    private async Task<JsonElement> SaveAsync(string section, object body, JsonElement? current = null)
    {
        var version = (current ?? await SettingsAsync()).GetProperty("version").GetUInt32();
        using var response = await PutAsync(section, body, version);
        return await ReadAsync<JsonElement>(response);
    }

    private Task<HttpResponseMessage> PutAsync(string section, object body, uint version, HttpClient? client = null)
    {
        var fields = JsonSerializer.SerializeToNode(body)!.AsObject();
        fields["version"] = version;
        return (client ?? _admin).PutAsJsonAsync($"/api/federation-settings/{section}", fields, Token);
    }
}
