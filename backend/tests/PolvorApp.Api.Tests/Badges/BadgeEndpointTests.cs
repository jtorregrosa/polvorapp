using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Platform.Storage;
using PolvorApp.Api.Tests.Catalog;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.Api.Tests.Registry;
using PolvorApp.Badges.Endpoints;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.SharedKernel.Images;
using PolvorApp.SharedKernel.Storage;
using UglyToad.PdfPig;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;
namespace PolvorApp.Api.Tests.Badges;

/// <summary>
/// <c>POST /api/badges/sheet</c> (spec: Badge batches, Badge language, Badge access and document
/// handling; design D3, D6): Admins only, a comparsa's arquebusiers or a selection, the blocking rules,
/// photos scaled for print, the storage outage, the file name, no caching and the per-user limit.
/// </summary>
public sealed class BadgeEndpointTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private static readonly Uri SheetUri = new("/api/badges/sheet", UriKind.Relative);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly OutageSwitch _storage = new();
    private RegistryTestHost _registry = null!;

    public async ValueTask InitializeAsync() => _registry = await StartAsync();

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public async Task An_admin_prints_a_whole_comparsa_reserves_included_in_alphabetical_order()
    {
        await RegisterAsync(_registry.Own.Id, "Zapata Sintética");
        await RegisterAsync(_registry.Own.Id, "Abad Sintético", status: "RESERVE");
        await RegisterAsync(_registry.Own.Id, "Martín Sintético");

        using var response = await PostAsync(_registry.Admin, new { comparsaId = _registry.Own.Id, language = "es-ES" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "", StringComparison.Ordinal);
        Assert.Equal($"polvorapp-badges-comparsa-sintetica-propia-{_registry.Today:yyyyMMdd}.pdf", FileName(response));
        var text = await TextAsync(response);
        Assert.Contains("ARCABUCERO", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("Abad", StringComparison.Ordinal) < text.IndexOf("Martín", StringComparison.Ordinal));
        Assert.True(text.IndexOf("Martín", StringComparison.Ordinal) < text.IndexOf("Zapata", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("ca-ES-valencia", "Unió Sintètica de Comparses de Prova")]
    [InlineData("en", "Unión Sintética de Comparsas de Prueba")]
    public async Task The_sheet_prints_the_official_name_of_the_settings_in_its_form(string language, string expected)
    {
        await RegisterAsync(_registry.Own.Id, "Nombre Sintético");
        await using (var scope = _registry.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<FederationCatalog.Persistence.FederationCatalogDbContext>().Database.ExecuteSqlRawAsync(
                "UPDATE catalog.federation_settings SET official_name_es = 'Unión Sintética de Comparsas de Prueba', official_name_ca = 'Unió Sintètica de Comparses de Prova'",
                Token);
        }

        using var response = await PostAsync(_registry.Admin, new { comparsaId = _registry.Own.Id, language });

        Assert.Contains(expected, await TextAsync(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_inactive_comparsa_still_prints_its_arquebusiers()
    {
        await _registry.Services.SaveRegistryAsync(RegistryData.NewArquebusier(_registry.Inactive.Id, "Sintético Inactivo"));

        using var response = await PostAsync(_registry.Admin, new { comparsaId = _registry.Inactive.Id, language = "es-ES" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Inactivo", await TextAsync(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_selection_across_comparsas_is_ordered_by_comparsa_and_named_by_its_count()
    {
        var own = await RegisterAsync(_registry.Own.Id, "Abad Sintético");
        var other = await RegisterAsync(_registry.Other.Id, "Zapata Sintética");

        using var response = await PostAsync(_registry.Admin, new { arquebusierIds = new[] { own, other, own }, language = "ca-ES-valencia" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"polvorapp-badges-selection-2-{_registry.Today:yyyyMMdd}.pdf", FileName(response));
        var text = await TextAsync(response);
        Assert.Contains("ARCABUSSER", text, StringComparison.Ordinal);
        Assert.Contains("Cognoms", text, StringComparison.Ordinal);
        // "Comparsa Sintética Ajena" comes before "Comparsa Sintética Propia".
        Assert.True(text.IndexOf("Zapata", StringComparison.Ordinal) < text.IndexOf("Abad", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Photos_are_scaled_for_print_and_a_missing_one_leaves_the_frame_empty()
    {
        var withPhoto = await RegisterAsync(_registry.Own.Id, "Con Foto");
        await RegisterAsync(_registry.Own.Id, "Sin Foto");
        using (var upload = await PhotoRequests.UploadAsync(_registry.Admin, withPhoto, "id", TestImages.Jpeg(900, 1200)))
        {
            Assert.True(upload.IsSuccessStatusCode);
        }

        using var response = await PostAsync(_registry.Admin, new { comparsaId = _registry.Own.Id, language = "es-ES" });

        using var pdf = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync(Token));
        var image = Assert.Single(pdf.GetPage(1).GetImages());
        Assert.Equal((300, 400), (image.WidthInSamples, image.HeightInSamples));
    }

    [Fact]
    public async Task A_firing_chief_cannot_print_badges_and_a_signed_out_user_is_unauthorized()
    {
        await RegisterAsync(_registry.Own.Id, "Sintético Propio");
        using var anonymous = _registry.Host.Factory.CreateClient();

        using var chief = await PostAsync(_registry.FiringChief, new { comparsaId = _registry.Own.Id, language = "es-ES" });
        using var signedOut = await PostAsync(anonymous, new { comparsaId = _registry.Own.Id, language = "es-ES" });

        Assert.Equal(HttpStatusCode.Forbidden, chief.StatusCode);
        // Anti-forgery runs first for unsafe requests without a session: either way no document.
        Assert.Contains(signedOut.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.BadRequest });
        Assert.NotEqual("application/pdf", signedOut.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task The_request_rules_are_reported_by_field()
    {
        using var neither = await PostAsync(_registry.Admin, new { language = "fr" });
        using var both = await PostAsync(_registry.Admin, new { comparsaId = _registry.Own.Id, arquebusierIds = new[] { Guid.NewGuid() }, language = "es-ES" });
        using var tooMany = await PostAsync(_registry.Admin, new { arquebusierIds = Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()), language = "es-ES" });

        await AssertProblemAsync(neither, HttpStatusCode.BadRequest, "validation");
        var errors = await ErrorsAsync(neither);
        Assert.Equal(("required", "invalid"), (errors["batch"], errors["language"]));
        await AssertProblemAsync(both, HttpStatusCode.BadRequest, "validation");
        Assert.Equal("invalid", (await ErrorsAsync(both))["batch"]);
        await AssertProblemAsync(tooMany, HttpStatusCode.BadRequest, "validation");
        Assert.Equal("tooMany", (await ErrorsAsync(tooMany))["arquebusierIds"]);
    }

    [Fact]
    public async Task A_selection_naming_an_arquebusier_no_longer_in_the_registry_is_refused_naming_it()
    {
        var kept = await RegisterAsync(_registry.Own.Id, "Sintético Presente");
        var deleted = await RegisterAsync(_registry.Own.Id, "Sintético Borrado");
        using (var deletion = await _registry.Admin.DeleteAsync(new Uri($"/api/arquebusiers/{deleted}", UriKind.Relative), Token))
        {
            Assert.Equal(HttpStatusCode.NoContent, deletion.StatusCode);
        }

        using var response = await PostAsync(_registry.Admin, new { arquebusierIds = new[] { kept, deleted }, language = "es-ES" });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal("notFound", (await ErrorsAsync(response))["arquebusierIds[1]"]);
    }

    [Fact]
    public async Task An_unknown_or_empty_comparsa_and_one_beyond_200_are_refused()
    {
        await _registry.Services.SaveRegistryAsync([.. Enumerable.Range(0, 201).Select(i => RegistryData.NewArquebusier(_registry.Own.Id, $"Sintético {i}"))]);

        using var unknown = await PostAsync(_registry.Admin, new { comparsaId = Guid.NewGuid(), language = "es-ES" });
        using var empty = await PostAsync(_registry.Admin, new { comparsaId = _registry.Other.Id, language = "es-ES" });
        using var tooMany = await PostAsync(_registry.Admin, new { comparsaId = _registry.Own.Id, language = "es-ES" });

        await AssertProblemAsync(unknown, HttpStatusCode.NotFound, "badges.notFound");
        await AssertProblemAsync(empty, HttpStatusCode.Conflict, "badges.nothingToPrint");
        await AssertProblemAsync(tooMany, HttpStatusCode.Conflict, "badges.tooMany");
    }

    [Theory]
    [InlineData("catalog/")]
    [InlineData("registry/")]
    public async Task A_storage_outage_on_the_logo_or_a_photo_is_retryable_and_prints_nothing(string prefix)
    {
        var id = await RegisterAsync(_registry.Own.Id, "Sintético Foto");
        using (var photo = await PhotoRequests.UploadAsync(_registry.Admin, id, "id", TestImages.Jpeg(600, 800)))
        using (var logo = await FederationLogoRequests.UploadAsync(_registry.Admin, TestImages.Png(400, 400)))
        {
            Assert.True(photo.IsSuccessStatusCode);
            Assert.True(logo.IsSuccessStatusCode);
        }

        _storage.FailingPrefix = prefix;
        using var response = await PostAsync(_registry.Admin, new { comparsaId = _registry.Own.Id, language = "es-ES" });

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "storage.unavailable");
        Assert.Empty(await _registry.Host.AuditEntriesAsync("BadgesDownloaded"));
    }

    [Fact]
    public async Task A_badge_prints_the_six_values_and_no_other_personal_data()
    {
        var body = await _registry.RegisterAsync(_registry.Own.Id, b =>
        {
            b["lastName"] = "Sintético Privado";
            b["firstName"] = "Ana";
            b["birthDate"] = "2012-03-04";
            b["email"] = "ana.privada@polvorapp.example";
            b["phone"] = "+34 611 222 333";
        });

        using var response = await PostAsync(_registry.Admin, new { comparsaId = _registry.Own.Id, language = "es-ES" });

        var text = await TextAsync(response);
        Assert.Contains("Sintético Privado", text, StringComparison.Ordinal);
        Assert.Contains(body.GetProperty("nationalId").GetString()!, text, StringComparison.Ordinal);
        Assert.Contains(body.GetProperty("federationId").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture), text, StringComparison.Ordinal);
        foreach (var hidden in new[] { "polvorapp.example", "611 222", "04/03/2012", "2012-03-04", "FEMALE", "Mujer", "Licencia" })
        {
            Assert.DoesNotContain(hidden, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_photo_the_registry_holds_but_cannot_be_read_refuses_the_sheet_naming_the_arquebusier()
    {
        var unreadable = await RegisterAsync(_registry.Own.Id, "Sintético Ilegible");
        var readable = await RegisterAsync(_registry.Own.Id, "Sintético Legible");
        foreach (var id in new[] { unreadable, readable })
        {
            using var upload = await PhotoRequests.UploadAsync(_registry.Admin, id, "id", TestImages.Jpeg(600, 800));
            Assert.True(upload.IsSuccessStatusCode);
        }

        _storage.MissingKeys = await PhotoKeysAsync(unreadable);
        using var response = await PostAsync(_registry.Admin, new { comparsaId = _registry.Own.Id, language = "es-ES" });

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "badges.photoUnreadable");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        Assert.Equal([unreadable], problem.RootElement.GetProperty("arquebusierIds").EnumerateArray().Select(e => e.GetGuid()));
        Assert.Empty(await _registry.Host.AuditEntriesAsync("BadgesDownloaded"));
    }

    [Fact]
    public async Task A_name_the_fonts_cannot_draw_refuses_the_sheet_with_its_own_code_and_audits_nothing()
    {
        await RegisterAsync(_registry.Own.Id, "漢字 Sintético");

        using var response = await PostAsync(_registry.Admin, new { comparsaId = _registry.Own.Id, language = "es-ES" });

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "badges.textUnprintable");
        Assert.DoesNotContain("漢字", await response.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
        Assert.Empty(await _registry.Host.AuditEntriesAsync("BadgesDownloaded"));
    }

    [Fact]
    public async Task A_busy_image_pipeline_is_retryable()
    {
        await using var busy = await RegistryTestHost.StartAsync(
            postgres, mailpit, services => services.AddSingleton<IImageNormalizer>(provider => new BusyWhenPrinting(provider.GetRequiredService<PolvorApp.Api.Platform.Images.SkiaImageNormalizer>())));
        var id = (await busy.RegisterAsync(busy.Own.Id)).GetProperty("id").GetGuid();
        using (var upload = await PhotoRequests.UploadAsync(busy.Admin, id, "id", TestImages.Jpeg(600, 800)))
        {
            Assert.True(upload.IsSuccessStatusCode);
        }

        using var response = await PostAsync(busy.Admin, new { comparsaId = busy.Own.Id, language = "es-ES" });

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "badges.busy");
    }

    [Fact]
    public async Task Without_the_settings_no_sheet_is_printed_with_a_fallback_name_nor_audited()
    {
        await using var unreadable = await RegistryTestHost.StartAsync(
            postgres, mailpit, services => services.AddScoped<IFederationSettings, UnreadableFederationSettings>());
        await unreadable.Services.SaveRegistryAsync(RegistryData.NewArquebusier(unreadable.Own.Id));

        using var response = await PostAsync(unreadable.Admin, new { comparsaId = unreadable.Own.Id, language = "es-ES" });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.NotEqual("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(await unreadable.Host.AuditEntriesAsync("BadgesDownloaded"));
    }

    [Fact]
    public void The_request_body_is_limited_to_16_kb()
    {
        var endpoint = _registry.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>().Single(e => e.RoutePattern.RawText == "/api/badges/sheet");

        Assert.Equal(BadgeEndpoints.MaxRequestBytes, endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata>()?.MaxRequestBodySize);
        Assert.Equal(16 * 1024, BadgeEndpoints.MaxRequestBytes);
    }

    [Fact]
    public async Task The_logo_is_printed_when_uploaded()
    {
        await RegisterAsync(_registry.Own.Id, "Sintético Sin Foto");
        using (var logo = await FederationLogoRequests.UploadAsync(_registry.Admin, TestImages.Png(400, 400)))
        {
            Assert.True(logo.IsSuccessStatusCode);
        }

        using var response = await PostAsync(_registry.Admin, new { comparsaId = _registry.Own.Id, language = "es-ES" });

        using var pdf = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync(Token));
        Assert.Single(pdf.GetPage(1).GetImages());
    }

    [Fact]
    public async Task Badge_sheets_share_the_per_user_document_limit()
    {
        await using var limited = await StartAsync(new Dictionary<string, string?> { ["RateLimits:Exports:PermitLimit"] = "1" });
        await limited.Services.SaveRegistryAsync(RegistryData.NewArquebusier(limited.Own.Id));

        using var first = await PostAsync(limited.Admin, new { comparsaId = limited.Own.Id, language = "es-ES" });
        using var second = await PostAsync(limited.Admin, new { comparsaId = limited.Own.Id, language = "es-ES" });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    private Task<RegistryTestHost> StartAsync(IReadOnlyDictionary<string, string?>? settings = null) => RegistryTestHost.StartAsync(
        postgres,
        mailpit,
        services => services.AddSingleton<IObjectStorage>(provider => _storage.Wrap(provider.GetRequiredService<S3ObjectStorage>())),
        settings);

    private async Task<Guid> RegisterAsync(Guid comparsaId, string lastName, string status = "ACTIVE")
    {
        var body = await _registry.RegisterAsync(comparsaId, b => (b["lastName"], b["status"]) = (lastName, status));
        return body.GetProperty("id").GetGuid();
    }

    private async Task<HashSet<string>> PhotoKeysAsync(Guid arquebusierId)
    {
        await using var scope = _registry.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PolvorApp.ArquebusierRegistry.Persistence.ArquebusierRegistryDbContext>();
        return [.. Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(db.Photos).Where(p => p.ArquebusierId == arquebusierId).Select(p => p.ObjectKey)];
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, object body) => client.PostAsJsonAsync(SheetUri, body, Token);

    private static string? FileName(HttpResponseMessage response) =>
        response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

    private static async Task<string> TextAsync(HttpResponseMessage response)
    {
        using var pdf = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync(Token));
        return string.Join('\n', pdf.GetPages().Select(page => string.Join(' ', page.GetWords().Select(w => w.Text))));
    }

    /// <summary>The real image pipeline for uploads, staying busy longer than a request waits when a sheet scales photos.</summary>
    private sealed class BusyWhenPrinting(IImageNormalizer inner) : IImageNormalizer
    {
        public Task<ImageNormalization> NormalizeAsync(Stream input, ImageRules rules, CancellationToken cancellationToken) =>
            rules.MaxWidth == 300 ? throw new ImageProcessingBusyException() : inner.NormalizeAsync(input, rules, cancellationToken);
    }

    /// <summary>The real storage, failing for keys under one prefix when a test says so.</summary>
    private sealed class OutageSwitch
    {
        public string? FailingPrefix { get; set; }

        /// <summary>Keys whose objects are gone although the registry still references them.</summary>
        public HashSet<string> MissingKeys { get; set; } = [];

        public IObjectStorage Wrap(IObjectStorage inner) => new Switch(inner, this);

        private sealed class Switch(IObjectStorage inner, OutageSwitch owner) : IObjectStorage
        {
            public Task PutAsync(string key, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken) =>
                inner.PutAsync(key, content, contentType, cancellationToken);

            public Task<StoredObject?> GetAsync(string key, CancellationToken cancellationToken) =>
                owner.FailingPrefix is { } prefix && key.StartsWith(prefix, StringComparison.Ordinal)
                    ? throw new StorageUnavailableException()
                    : owner.MissingKeys.Contains(key) ? Task.FromResult<StoredObject?>(null) : inner.GetAsync(key, cancellationToken);

            public Task DeleteAsync(string key, CancellationToken cancellationToken) => inner.DeleteAsync(key, cancellationToken);

            public IAsyncEnumerable<StoredObjectInfo> ListAsync(string prefix, CancellationToken cancellationToken) => inner.ListAsync(prefix, cancellationToken);
        }
    }
}
