using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.Api.Tests.Registry;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.Api.Tests.Privacy;

/// <summary>
/// Spec audit-privacy "GDPR request reference", "Looking up a person", "Exporting / Erasing a person's /
/// user's data" (UC-26): the endpoints, their validation, permissions, files and audit entries.
/// </summary>
public sealed class PrivacyEndpointTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private const string Reference = "REQ-2030-07";
    private readonly FailingExportAudit _failing = new();
    private RegistryTestHost _registry = null!;

    public async ValueTask InitializeAsync() =>
        _registry = await RegistryTestHost.StartAsync(postgres, mailpit, services => _failing.Decorate(services));

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public async Task A_lookup_shows_the_registered_person_and_is_audited_without_the_dni()
    {
        var (_, nationalId) = await RegisterAsync();

        var found = await PostAsync<JsonElement>("/api/privacy/people/lookup", new { nationalId, reference = Reference });

        Assert.True(found.GetProperty("found").GetBoolean());
        Assert.Equal("Arcabucera", found.GetProperty("registry").GetProperty("firstName").GetString());
        Assert.Equal("Comparsa Sintética Propia", found.GetProperty("registry").GetProperty("comparsaName").GetString());
        var entry = Assert.Single(await _registry.Host.AuditEntriesAsync("PersonLookedUp"));
        Assert.DoesNotContain(nationalId, entry.Data!, StringComparison.Ordinal);
        Assert.Contains("\"found\": true", entry.Data!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_lookup_of_nobody_says_so_and_is_audited_as_not_found()
    {
        var found = await PostAsync<JsonElement>("/api/privacy/people/lookup", new { nationalId = RegistryData.NextIdentity().NationalId });

        Assert.False(found.GetProperty("found").GetBoolean());
        Assert.Contains("\"found\": false", Assert.Single(await _registry.Host.AuditEntriesAsync("PersonLookedUp")).Data!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/privacy/people/lookup", "12345678A", "x", "nationalId")]
    [InlineData("/api/privacy/people/export", "00000000T", null, "reference")]
    [InlineData("/api/privacy/people/erasure", "00000000T", "Solicitud de 12345678Z", "reference")]
    [InlineData("/api/privacy/people/erasure", "00000000T", "pide nadie@example.test", "reference")]
    [InlineData("/api/privacy/people/erasure", "00000000T", "dos\nlíneas", "reference")]
    [InlineData("/api/privacy/people/erasure", "00000000T", "Solicitud 1234 5678 Z", "reference")]
    [InlineData("/api/privacy/people/erasure", "00000000T", "Solicitud 12345678/Z", "reference")]
    [InlineData("/api/privacy/people/erasure", "00000000T", "Solicitud 12345678_z", "reference")]
    [InlineData("/api/privacy/people/erasure", "00000000T", "Solicitud 12.345.678-Z", "reference")]
    [InlineData("/api/privacy/people/export", "00000000T", "NIE X 1234567 L", "reference")]
    [InlineData("/api/privacy/people/lookup", "00000000T", "Solicitud 12345678,Z", "reference")]
    [InlineData("/api/privacy/people/export", "00000000T", "   ", "reference")]
    [InlineData("/api/privacy/people/export", "00000000T", "R-123456789012345678901234567890123456789012345678901", "reference")]
    public async Task Invalid_input_is_rejected_naming_the_field_and_nothing_is_audited(string path, string nationalId, string? reference, string field)
    {
        using var response = await _registry.Admin.PostAsJsonAsync(path, new { nationalId, reference }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), field);
        foreach (var action in new[] { "PersonLookedUp", "PersonalDataExported", "PersonalDataErased" })
        {
            Assert.Empty(await _registry.Host.AuditEntriesAsync(action));
        }
    }

    [Theory]
    [InlineData("REQ-2030-0000123-A")]
    [InlineData("Registro de entrada 2030/0457")]
    public async Task A_reference_without_a_dni_or_an_email_is_accepted(string reference)
    {
        using var response = await _registry.Admin.PostAsJsonAsync(
            "/api/privacy/people/lookup", new { nationalId = RegistryData.NextIdentity().NationalId, reference }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_person_export_is_a_zip_with_the_workbook_and_photo_audited_by_reference()
    {
        var (id, nationalId) = await RegisterAsync();
        using (var photo = await PhotoRequests.UploadAsync(_registry.Admin, id, "id", TestImages.Jpeg(600, 800)))
        {
            Assert.True(photo.IsSuccessStatusCode);
        }

        using var response = await _registry.Admin.PostAsJsonAsync("/api/privacy/people/export", new { nationalId, reference = Reference }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName;
        Assert.StartsWith("polvorapp-personal-data-", fileName, StringComparison.Ordinal);
        Assert.DoesNotContain(nationalId, fileName, StringComparison.OrdinalIgnoreCase);
        using var zip = new ZipArchive(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        Assert.Equal(["personal-data.xlsx", "photos/id.jpg"], zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
        using var workbook = new XLWorkbook(Copy(zip.GetEntry("personal-data.xlsx")!));
        Assert.Equal(["Ficha", "Sobre estos datos"], workbook.Worksheets.Select(w => w.Name));
        Assert.Equal(nationalId, workbook.Worksheet("Ficha").Cell(2, 1).GetString());
        Assert.Contains(workbook.Worksheet("Sobre estos datos").CellsUsed(), c => c.GetString() == Reference);
        var audit = Assert.Single(await _registry.Host.AuditEntriesAsync("PersonalDataExported"));
        Assert.Contains(Reference, audit.Data!, StringComparison.Ordinal);
        Assert.DoesNotContain(nationalId, audit.Data!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_export_that_cannot_be_audited_sends_nothing()
    {
        var (_, nationalId) = await RegisterAsync();
        _failing.Fail = true;

        using var response = await _registry.Admin.PostAsJsonAsync("/api/privacy/people/export", new { nationalId, reference = Reference }, TestContext.Current.CancellationToken);

        await IdentityAssertions.AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "privacy.auditUnavailable");
    }

    [Fact]
    public async Task Exporting_or_erasing_nobody_is_not_found()
    {
        var nobody = RegistryData.NextIdentity().NationalId;

        using var export = await _registry.Admin.PostAsJsonAsync("/api/privacy/people/export", new { nationalId = nobody, reference = Reference }, TestContext.Current.CancellationToken);
        using var erase = await _registry.Admin.PostAsJsonAsync("/api/privacy/people/erasure", new { nationalId = nobody, reference = Reference }, TestContext.Current.CancellationToken);

        await IdentityAssertions.AssertProblemAsync(export, HttpStatusCode.NotFound, "privacy.notFound");
        await IdentityAssertions.AssertProblemAsync(erase, HttpStatusCode.NotFound, "privacy.notFound");
        var probes = await _registry.Host.AuditEntriesAsync("PersonLookedUp");
        Assert.Equal(2, probes.Count);
        Assert.All(probes, e => Assert.Contains("\"found\": false", e.Data!, StringComparison.Ordinal));
        Assert.All(probes, e => Assert.DoesNotContain(nobody, e.Data!, StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_export_is_written_in_the_admins_language()
    {
        var (_, nationalId) = await RegisterAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/privacy/people/export")
        {
            Content = JsonContent.Create(new { nationalId, reference = Reference }),
        };
        request.Headers.AcceptLanguage.ParseAdd("ca-ES-valencia");

        using var response = await _registry.Admin.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var zip = new ZipArchive(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        using var workbook = new XLWorkbook(Copy(zip.GetEntry("personal-data.xlsx")!));
        Assert.Equal(["Fitxa", "Sobre aquestes dades"], workbook.Worksheets.Select(w => w.Name));
    }

    [Fact]
    public async Task A_lookup_warns_about_the_editions_that_are_not_closed()
    {
        var (id, nationalId) = await RegisterAsync();
        var open = OrderData.NewEdition(2031, PolvorApp.FestivalEditions.Contracts.EditionStatus.InProgress, ordersOpen: true);
        var past = OrderData.NewEdition(2030, PolvorApp.FestivalEditions.Contracts.EditionStatus.Closed);
        await _registry.Services.SaveEditionsAsync(open, past);
        var openOrder = OrderData.NewOrder(open, _registry.Own.Id, PolvorApp.ComparsaOrders.Contracts.OrderStatus.Submitted);
        var pastOrder = OrderData.NewOrder(past, _registry.Own.Id, PolvorApp.ComparsaOrders.Contracts.OrderStatus.Validated);
        await _registry.Services.SaveOrdersAsync(openOrder, pastOrder, OrderData.NewEntry(openOrder, id), OrderData.NewEntry(pastOrder, id));

        var found = await PostAsync<JsonElement>("/api/privacy/people/lookup", new { nationalId });

        var warning = Assert.Single(found.GetProperty("warnings").EnumerateArray());
        Assert.Equal(("entryRemoved", 2031, "SUBMITTED"), (warning.GetProperty("kind").GetString(), warning.GetProperty("editionYear").GetInt32(), warning.GetProperty("orderStatus").GetString()));
        Assert.Equal(2, found.GetProperty("entries").GetArrayLength());
    }

    /// <summary>Spec scenario "Warning about an open edition": a validated order of an edition in progress, orders closed.</summary>
    [Fact]
    public async Task A_lookup_warns_that_the_lists_of_an_edition_in_progress_will_no_longer_name_them()
    {
        var (id, nationalId) = await RegisterAsync();
        var current = OrderData.NewEdition(2031, PolvorApp.FestivalEditions.Contracts.EditionStatus.InProgress, ordersOpen: false);
        await _registry.Services.SaveEditionsAsync(current);
        var order = OrderData.NewOrder(current, _registry.Own.Id, PolvorApp.ComparsaOrders.Contracts.OrderStatus.Validated);
        await _registry.Services.SaveOrdersAsync(order, OrderData.NewEntry(order, id));

        var found = await PostAsync<JsonElement>("/api/privacy/people/lookup", new { nationalId });

        var warning = Assert.Single(found.GetProperty("warnings").EnumerateArray());
        Assert.Equal(("listsChange", 2031, _registry.Own.Name, "VALIDATED"), (
            warning.GetProperty("kind").GetString(), warning.GetProperty("editionYear").GetInt32(),
            warning.GetProperty("comparsaName").GetString(), warning.GetProperty("orderStatus").GetString()));
    }

    [Fact]
    public async Task A_person_erasure_answers_the_counts_audits_the_reference_and_a_second_one_finds_nothing()
    {
        var (id, nationalId) = await RegisterAsync();

        var erased = await PostAsync<JsonElement>("/api/privacy/people/erasure", new { nationalId, reference = Reference });
        using var again = await _registry.Admin.PostAsJsonAsync("/api/privacy/people/erasure", new { nationalId, reference = Reference }, TestContext.Current.CancellationToken);

        Assert.Equal(1, erased.GetProperty("counts").GetProperty("arquebusiersDeleted").GetInt32());
        Assert.Equal(0, erased.GetProperty("filesPending").GetInt32());
        await IdentityAssertions.AssertProblemAsync(again, HttpStatusCode.NotFound, "privacy.notFound");
        using var gone = await _registry.Admin.GetAsync($"/api/arquebusiers/{id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        var audit = Assert.Single(await _registry.Host.AuditEntriesAsync("PersonalDataErased"));
        Assert.Contains(Reference, audit.Data!, StringComparison.Ordinal);
        Assert.DoesNotContain(nationalId, audit.Data!, StringComparison.Ordinal);
        Assert.Equal(_registry.AdminId, audit.ActorUserId);
    }

    [Fact]
    public async Task A_user_export_holds_the_profile_and_an_erased_user_has_none()
    {
        var chief = await _registry.Host.CreateUserAsync("jefa.exportable@example.test");

        using var export = await _registry.Admin.PostAsJsonAsync($"/api/privacy/users/{chief.Id}/export", new { reference = Reference }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        using (var zip = new ZipArchive(await export.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken)))
        using (var workbook = new XLWorkbook(Copy(zip.GetEntry("personal-data.xlsx")!)))
        {
            Assert.Contains(workbook.Worksheet("Perfil").CellsUsed(), c => c.GetString() == chief.Email);
        }

        await PostAsync<JsonElement>($"/api/privacy/users/{chief.Id}/erasure", new { reference = Reference });
        using var after = await _registry.Admin.PostAsJsonAsync($"/api/privacy/users/{chief.Id}/export", new { reference = Reference }, TestContext.Current.CancellationToken);
        await IdentityAssertions.AssertProblemAsync(after, HttpStatusCode.NotFound, "privacy.notFound");
    }

    [Fact]
    public async Task A_user_erasure_refuses_yourself_and_a_second_time()
    {
        var chief = await _registry.Host.CreateUserAsync("jefa.borrable@example.test");

        using var self = await _registry.Admin.PostAsJsonAsync($"/api/privacy/users/{_registry.AdminId}/erasure", new { reference = Reference }, TestContext.Current.CancellationToken);
        var erased = await PostAsync<JsonElement>($"/api/privacy/users/{chief.Id}/erasure", new { reference = Reference });
        using var again = await _registry.Admin.PostAsJsonAsync($"/api/privacy/users/{chief.Id}/erasure", new { reference = Reference }, TestContext.Current.CancellationToken);
        using var unknown = await _registry.Admin.PostAsJsonAsync($"/api/privacy/users/{Guid.CreateVersion7()}/erasure", new { reference = Reference }, TestContext.Current.CancellationToken);

        await IdentityAssertions.AssertProblemAsync(self, HttpStatusCode.Conflict, "privacy.selfErasure");
        Assert.Equal(1, erased.GetProperty("counts").GetProperty("usersErased").GetInt32());
        await IdentityAssertions.AssertProblemAsync(again, HttpStatusCode.Conflict, "privacy.alreadyErased");
        await IdentityAssertions.AssertProblemAsync(unknown, HttpStatusCode.NotFound, "privacy.notFound");
        Assert.Single(await _registry.Host.AuditEntriesAsync("PersonalDataErased"));
    }

    [Theory]
    [InlineData("/api/privacy/people/lookup")]
    [InlineData("/api/privacy/people/export")]
    [InlineData("/api/privacy/people/erasure")]
    public async Task A_firing_chief_is_refused_on_people(string path)
    {
        using var response = await _registry.FiringChief.PostAsJsonAsync(path, new { nationalId = "00000000T", reference = Reference }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("export")]
    [InlineData("erasure")]
    public async Task A_firing_chief_is_refused_on_users(string action)
    {
        using var response = await _registry.FiringChief.PostAsJsonAsync(
            $"/api/privacy/users/{_registry.AdminId}/{action}", new { reference = Reference }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<(Guid Id, string NationalId)> RegisterAsync()
    {
        var arquebusier = await _registry.RegisterAsync(_registry.Own.Id);
        return (arquebusier.GetProperty("id").GetGuid(), arquebusier.GetProperty("nationalId").GetString()!);
    }

    private async Task<T> PostAsync<T>(string path, object body)
    {
        using var response = await _registry.Admin.PostAsJsonAsync(path, body, TestContext.Current.CancellationToken);
        return await IdentityAssertions.ReadAsync<T>(response);
    }

    private static MemoryStream Copy(ZipArchiveEntry entry)
    {
        var copy = new MemoryStream();
        using (var stream = entry.Open())
        {
            stream.CopyTo(copy);
        }

        copy.Position = 0;
        return copy;
    }

    /// <summary>Fails the audit of exports when asked, to prove that an unaudited file is never sent.</summary>
    private sealed class FailingExportAudit
    {
        public bool Fail { get; set; }

        public void Decorate(IServiceCollection services)
        {
            var original = services.Last(d => d.ServiceType == typeof(IAuditLog));
            services.Remove(original);
            services.AddScoped<IAuditLog>(provider =>
                new Failing((IAuditLog)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!), this));
        }

        private sealed class Failing(IAuditLog inner, FailingExportAudit owner) : IAuditLog
        {
            public Task RecordAsync(AuditRecord record, CancellationToken cancellationToken) =>
                owner.Fail && record.Action == "PersonalDataExported"
                    ? throw new InvalidOperationException("Synthetic audit failure.")
                    : inner.RecordAsync(record, cancellationToken);
        }
    }
}

/// <summary>Spec audit-privacy "Looking up a person": lookups are rate limited per user.</summary>
public sealed class PrivacyRateLimitTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private RegistryTestHost _registry = null!;

    public async ValueTask InitializeAsync() =>
        _registry = await RegistryTestHost.StartAsync(postgres, mailpit, settings: new Dictionary<string, string?> { ["RateLimits:Privacy:PermitLimit"] = "2" });

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public async Task Too_many_lookups_in_a_minute_are_refused()
    {
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            using var response = await _registry.Admin.PostAsJsonAsync(
                "/api/privacy/people/lookup", new { nationalId = RegistryData.NextIdentity().NationalId }, TestContext.Current.CancellationToken);
            statuses.Add(response.StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
    }
}
