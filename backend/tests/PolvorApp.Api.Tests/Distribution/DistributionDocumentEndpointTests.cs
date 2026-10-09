using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Exports;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComparsaOrders.Orders;
using PolvorApp.ComparsaOrders.Persistence;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Logos;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Storage;
using UglyToad.PdfPig;
using static PolvorApp.Api.Tests.Infrastructure.DistributionData;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;
namespace PolvorApp.Api.Tests.Distribution;

/// <summary>
/// Specs "Distribution lists (UC-20)", "Pickup authorisation form (UC-19)" and "Distribution documents are
/// protected and audited" (design D6, D7, D8): who downloads what, in which language, and the audit.
/// </summary>
public sealed class DistributionDocumentEndpointTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private OrderTestHost _orders = null!;
    private ComparsaOrder _order = null!;
    private EditionEntry _holder = null!;
    private EditionEntry _proxy = null!;
    private Guid _powderDay;
    private Guid _proxyId;

    public async ValueTask InitializeAsync()
    {
        _orders = await OrderTestHost.StartAsync(postgres, mailpit);
        (_order, _holder, _proxy, _powderDay, _proxyId) = await ArrangeAsync(_orders);
    }

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_Admin_downloads_the_powder_list_as_excel_and_pdf_and_each_is_audited()
    {
        using var excel = await _orders.Admin.GetAsync($"/api/distribution/distributions/{_powderDay}/list/xlsx", Token);
        using var pdf = await _orders.Admin.GetAsync($"/api/distribution/distributions/{_powderDay}/list/pdf", Token);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (excel.StatusCode, pdf.StatusCode));
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", excel.Content.Headers.ContentType?.MediaType);
        Assert.Equal(("polvorapp-2031-powder-distribution-list.xlsx", "polvorapp-2031-powder-distribution-list.pdf"), (FileName(excel), FileName(pdf)));
        Assert.True(pdf.Headers.CacheControl?.NoStore);
        using var workbook = new XLWorkbook(new MemoryStream(await excel.Content.ReadAsByteArrayAsync(Token)));
        Assert.Equal("Abad Sintética, Arcabucero", workbook.Worksheets.Single().Cell(7, 4).GetString());
        var entries = await _orders.Host.AuditEntriesAsync("DistributionDocumentDownloaded");
        Assert.Equal(2, entries.Count);
        var entry = entries[0];
        Assert.Equal(("DistributionDocument", "powder-distribution-list", _orders.Registry.AdminId), (entry.EntityType, entry.EntityId, entry.ActorUserId));
        using var data = JsonDocument.Parse(entry.Data!);
        Assert.Equal(("1", 2031, "POWDER", 1), (data.RootElement.GetProperty("version").GetString(), data.RootElement.GetProperty("editionYear").GetInt32(), data.RootElement.GetProperty("type").GetString(), data.RootElement.GetProperty("rows").GetInt32()));
        Assert.DoesNotContain(_holder.NationalId!, entry.Data!, StringComparison.Ordinal);
        Assert.DoesNotContain("Abad", entry.Data!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_name_the_fonts_cannot_draw_refuses_the_pdf_list_and_the_form_but_not_the_excel_list()
    {
        var holder = await _orders.AddLicensedEntryAsync(_order, "漢字 Sintético", ValidThrough, powderKg: 1);
        var proxy = await _orders.AddLicensedEntryAsync(_order, "Autorizado Sintético", ValidThrough, status: ArquebusierStatus.Reserve);
        using var registered = await _orders.FiringChief.RegisterProxyAsync(_orders.Current.Id, holder.Id, proxy.Id);
        var proxyId = (await ReadAsync<JsonElement>(registered)).GetProperty("id").GetGuid();

        using var pdf = await _orders.Admin.GetAsync($"/api/distribution/distributions/{_powderDay}/list/pdf", Token);
        using var form = await _orders.FiringChief.GetAsync($"/api/distribution/proxies/{proxyId}/form", Token);
        using var excel = await _orders.Admin.GetAsync($"/api/distribution/distributions/{_powderDay}/list/xlsx", Token);

        await AssertProblemAsync(pdf, HttpStatusCode.Conflict, "distribution.textUnprintable");
        await AssertProblemAsync(form, HttpStatusCode.Conflict, "distribution.textUnprintable");
        Assert.DoesNotContain("漢字", await pdf.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, excel.StatusCode);
        Assert.Single(await _orders.Host.AuditEntriesAsync("DistributionDocumentDownloaded"));
    }

    [Fact]
    public async Task A_list_leaves_out_orders_not_validated_and_proxies_of_the_other_type()
    {
        var submitted = await _orders.AddOrderAsync(_orders.Current, _orders.Other.Id, OrderStatus.Submitted);
        await _orders.AddLicensedEntryAsync(submitted, "Pendiente Sintética", ValidThrough, powderKg: 2);
        var rental = await _orders.AddLicensedEntryAsync(_order, "Alquila Sintética", ValidThrough, powderKg: 1, weaponSource: WeaponSource.Rental);
        using (var weapons = await _orders.FiringChief.RegisterProxyAsync(_orders.Current.Id, rental.Id, _proxy.Id, "WEAPONS"))
        {
            Assert.Equal(HttpStatusCode.Created, weapons.StatusCode);
        }

        using var response = await _orders.Admin.GetAsync($"/api/distribution/distributions/{_powderDay}/list/xlsx", Token);

        using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync(Token)));
        var sheet = workbook.Worksheets.Single();
        var rows = Enumerable.Range(7, 3).Select(r => (sheet.Cell(r, 4).GetString(), sheet.Cell(r, 11).GetString())).Where(r => r.Item1.Length > 0).ToList();
        Assert.Equal([("Abad Sintética, Arcabucero", "Zamora Sintético, Arcabucero"), ("Alquila Sintética, Arcabucero", string.Empty)], rows);
    }

    [Fact]
    public async Task A_form_without_a_planned_day_and_with_a_pending_holder_license_prints_neither()
    {
        var order = await _orders.AddOrderAsync(_orders.Current, _orders.Inactive.Id);
        var holder = await _orders.AddLicensedEntryAsync(order, "Pendiente Sintética", null, weaponSource: WeaponSource.Rental, pending: true);
        var proxy = await _orders.AddLicensedEntryAsync(order, "Vecino Sintético", ValidThrough, status: ArquebusierStatus.Reserve);
        using var created = await _orders.Admin.RegisterProxyAsync(_orders.Current.Id, holder.Id, proxy.Id, "WEAPONS");
        var proxyId = (await ReadAsync<JsonElement>(created)).GetProperty("id").GetGuid();

        using var response = await _orders.Admin.GetAsync($"/api/distribution/proxies/{proxyId}/form", Token);

        var lines = DocumentText.Pdf(await response.Content.ReadAsByteArrayAsync(Token)).Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        Assert.Equal("Licencia de armas:", lines.First(l => l.StartsWith("Licencia de armas:", StringComparison.Ordinal)));
        Assert.Contains(lines, l => l.StartsWith("Ante la imposibilidad de recoger el arma de alquiler que me corresponde el día del reparto, por", StringComparison.Ordinal));
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task In_a_closed_edition_the_FiringChief_still_prints_the_form_but_never_in_a_draft()
    {
        await _orders.SetCurrentStatusAsync(PolvorApp.FestivalEditions.Contracts.EditionStatus.Closed);
        using (var closed = await _orders.FiringChief.GetAsync($"/api/distribution/proxies/{_proxyId}/form", Token))
        {
            Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        }

        await _orders.SetCurrentStatusAsync(PolvorApp.FestivalEditions.Contracts.EditionStatus.Draft);
        using var draft = await _orders.FiringChief.GetAsync($"/api/distribution/proxies/{_proxyId}/form", Token);

        await AssertProblemAsync(draft, HttpStatusCode.NotFound, "proxies.notFound");
    }

    [Fact]
    public async Task A_FiringChief_cannot_download_a_list_and_unknown_days_or_formats_are_not_found()
    {
        using var firingChief = await _orders.FiringChief.GetAsync($"/api/distribution/distributions/{_powderDay}/list/pdf", Token);
        using var unknownDay = await _orders.Admin.GetAsync($"/api/distribution/distributions/{Guid.CreateVersion7()}/list/pdf", Token);
        using var unknownFormat = await _orders.Admin.GetAsync($"/api/distribution/distributions/{_powderDay}/list/csv", Token);

        Assert.Equal(HttpStatusCode.Forbidden, firingChief.StatusCode);
        await AssertProblemAsync(unknownDay, HttpStatusCode.NotFound, "distribution.notFound");
        Assert.Equal(HttpStatusCode.NotFound, unknownFormat.StatusCode);
        Assert.Empty(await _orders.Host.AuditEntriesAsync("DistributionDocumentDownloaded"));
    }

    [Fact]
    public async Task The_list_follows_the_users_language()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/distribution/distributions/{_powderDay}/list/xlsx");
        request.Headers.TryAddWithoutValidation("Accept-Language", "ca-ES-valencia");

        using var response = await _orders.Admin.SendAsync(request, Token);

        using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync(Token)));
        Assert.Equal("Cognoms i nom", workbook.Worksheets.Single().Cell(6, 4).GetString());
    }

    [Fact]
    public async Task The_comparsas_FiringChief_downloads_the_form_in_their_language_and_it_is_audited()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/distribution/proxies/{_proxyId}/form");
        request.Headers.TryAddWithoutValidation("Accept-Language", "ca-ES-valencia");

        using var response = await _orders.FiringChief.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var name = FileName(response)!;
        Assert.StartsWith("polvorapp-2031-pickup-authorisation-powder-", name, StringComparison.Ordinal);
        Assert.DoesNotContain("abad", name, StringComparison.OrdinalIgnoreCase);
        var text = DocumentText.Pdf(await response.Content.ReadAsByteArrayAsync(Token));
        Assert.Contains("Autorització de recollida de pólvora — Festes 2031", text, StringComparison.Ordinal);
        Assert.Contains(_holder.NationalId!, text, StringComparison.Ordinal);
        var entry = Assert.Single(await _orders.Host.AuditEntriesAsync("DistributionDocumentDownloaded"));
        Assert.Equal(("pickup-authorisation", (Guid?)_orders.Own.Id), (entry.EntityId, entry.ComparsaId));
        Assert.DoesNotContain(_holder.NationalId!, entry.Data!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_form_prints_the_official_name_of_the_settings()
    {
        await using (var scope = _orders.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>().Database.ExecuteSqlRawAsync(
                "UPDATE catalog.federation_settings SET official_name_ca = 'Unió Sintètica de Comparses de Prova'", Token);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/distribution/proxies/{_proxyId}/form");
        request.Headers.TryAddWithoutValidation("Accept-Language", "ca-ES-valencia");
        using var response = await _orders.FiringChief.SendAsync(request, Token);

        var text = DocumentText.Pdf(await response.Content.ReadAsByteArrayAsync(Token));
        Assert.Contains("Unió Sintètica de Comparses de Prova", text, StringComparison.Ordinal);
        Assert.DoesNotContain(TestFederationNames.Valencian, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Another_comparsas_FiringChief_does_not_get_the_form()
    {
        var otherOrder = await _orders.AddOrderAsync(_orders.Current, _orders.Other.Id);
        var otherHolder = await _orders.AddLicensedEntryAsync(otherOrder, "Vecina Sintética", ValidThrough, powderKg: 1);
        var otherProxy = await _orders.AddLicensedEntryAsync(otherOrder, "Vecino Sintético", ValidThrough, status: ArquebusierStatus.Reserve);
        using var created = await _orders.Admin.RegisterProxyAsync(_orders.Current.Id, otherHolder.Id, otherProxy.Id);
        var otherId = (await ReadAsync<JsonElement>(created)).GetProperty("id").GetGuid();

        using var response = await _orders.FiringChief.GetAsync($"/api/distribution/proxies/{otherId}/form", Token);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "proxies.notFound");
    }

    [Fact]
    public async Task A_proxy_that_no_longer_holds_has_no_form()
    {
        await using (var scope = _orders.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ComparsaOrdersDbContext>().Entries
                .Where(e => e.Id == _holder.Id).ExecuteUpdateAsync(e => e.SetProperty(x => x.PowderKg, 0), Token);
        }

        using var response = await _orders.FiringChief.GetAsync($"/api/distribution/proxies/{_proxyId}/form", Token);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "proxies.notApplicable");
    }

    [Fact]
    public async Task A_proxy_whose_license_no_longer_holds_has_no_form()
    {
        await using (var scope = _orders.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ArquebusierRegistry.Persistence.ArquebusierRegistryDbContext>().Arquebusiers
                .Where(a => a.Id == _proxy.ArquebusierId).ExecuteUpdateAsync(a => a.SetProperty(x => x.LicenseExpiresOn, new DateOnly(2031, 4, 1)), Token);
        }

        using var response = await _orders.FiringChief.GetAsync($"/api/distribution/proxies/{_proxyId}/form", Token);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "proxies.licenseInvalid");
    }

    [Fact]
    public async Task A_document_that_cannot_be_audited_is_not_returned()
    {
        await using var failing = await OrderTestHost.StartAsync(postgres, mailpit, services => services.AddScoped<IAuditLog, FailingAuditLog>());
        var (_, _, _, day, proxy) = await ArrangeAsync(failing);

        using var list = await failing.Admin.GetAsync($"/api/distribution/distributions/{day}/list/pdf", Token);
        using var form = await failing.Admin.GetAsync($"/api/distribution/proxies/{proxy}/form", Token);

        await AssertProblemAsync(list, HttpStatusCode.ServiceUnavailable, "distribution.auditUnavailable");
        await AssertProblemAsync(form, HttpStatusCode.ServiceUnavailable, "distribution.auditUnavailable");
    }

    [Fact]
    public async Task Without_the_settings_no_form_is_printed_with_a_fallback_name_nor_audited()
    {
        await using var unreadable = await OrderTestHost.StartAsync(postgres, mailpit, services => services.AddScoped<IFederationSettings, UnreadableFederationSettings>());
        var (_, _, _, _, proxy) = await ArrangeAsync(unreadable);

        using var form = await unreadable.Admin.GetAsync($"/api/distribution/proxies/{proxy}/form", Token);

        Assert.Equal(HttpStatusCode.InternalServerError, form.StatusCode);
        Assert.NotEqual("application/pdf", form.Content.Headers.ContentType?.MediaType);
        Assert.Empty(await unreadable.Host.AuditEntriesAsync("DistributionDocumentDownloaded"));
    }

    [Fact]
    public async Task A_form_with_a_logo_the_storage_cannot_serve_is_unavailable()
    {
        await using var outage = await OrderTestHost.StartAsync(postgres, mailpit, services => services.AddSingleton<IObjectStorage, OutageStorage>());
        var (_, _, _, _, proxy) = await ArrangeAsync(outage);
        await using (var scope = outage.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
            var settings = await db.FederationSettings.SingleAsync(Token);
            var logoId = Guid.CreateVersion7();
            settings.Logo = new ComparsaLogo { Id = logoId, ObjectKey = LogoStorage.KeyFor(logoId), Width = 400, Height = 300, SizeBytes = 2048, UploadedAt = DateTimeOffset.UtcNow };
            await db.SaveChangesAsync(Token);
        }

        using var form = await outage.Admin.GetAsync($"/api/distribution/proxies/{proxy}/form", Token);

        await AssertProblemAsync(form, HttpStatusCode.ServiceUnavailable, "storage.unavailable");
        Assert.Empty(await outage.Host.AuditEntriesAsync("DistributionDocumentDownloaded"));
    }

    [Fact]
    public async Task An_Admin_downloads_the_weapons_list_with_the_rented_model_and_its_audit()
    {
        await _orders.AddLicensedEntryAsync(_order, "Alquila Sintética", ValidThrough, weaponSource: WeaponSource.Rental);
        var day = await PlanWeaponsDayAsync(_orders);

        using var excel = await _orders.Admin.GetAsync($"/api/distribution/distributions/{day}/list/xlsx", Token);

        Assert.Equal(HttpStatusCode.OK, excel.StatusCode);
        Assert.Equal("polvorapp-2031-weapons-distribution-list.xlsx", FileName(excel));
        var grid = DocumentText.Grid(await excel.Content.ReadAsByteArrayAsync(Token));
        Assert.Contains("Alquila Sintética, Arcabucero", grid, StringComparison.Ordinal);
        Assert.Contains(_orders.Offered.Label, grid, StringComparison.Ordinal);
        Assert.DoesNotContain("Abad Sintética", grid, StringComparison.Ordinal);
        var entry = Assert.Single(await _orders.Host.AuditEntriesAsync("DistributionDocumentDownloaded"));
        Assert.Equal("weapons-distribution-list", entry.EntityId);
        using var data = JsonDocument.Parse(entry.Data!);
        Assert.Equal(("WEAPONS", 1), (data.RootElement.GetProperty("type").GetString(), data.RootElement.GetProperty("rows").GetInt32()));
    }

    [Fact]
    public async Task A_list_without_holders_has_its_headings_and_no_rows()
    {
        // Nobody rents a weapon in the validated order: the weapons list is empty.
        var day = await PlanWeaponsDayAsync(_orders);

        using var excel = await _orders.Admin.GetAsync($"/api/distribution/distributions/{day}/list/xlsx", Token);
        using var pdf = await _orders.Admin.GetAsync($"/api/distribution/distributions/{day}/list/pdf", Token);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (excel.StatusCode, pdf.StatusCode));
        var text = DocumentText.Pdf(await pdf.Content.ReadAsByteArrayAsync(Token));
        Assert.Contains("Apellidos y nombre", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Abad Sintética", text, StringComparison.Ordinal);
        var entries = await _orders.Host.AuditEntriesAsync("DistributionDocumentDownloaded");
        Assert.All(entries, entry =>
        {
            using var data = JsonDocument.Parse(entry.Data!);
            Assert.Equal(0, data.RootElement.GetProperty("rows").GetInt32());
        });
    }

    [Fact]
    public async Task The_form_carries_the_Federation_logo_once_uploaded_and_no_image_before()
    {
        using var plain = await _orders.FiringChief.GetAsync($"/api/distribution/proxies/{_proxyId}/form", Token);
        using (var document = PdfDocument.Open(await plain.Content.ReadAsByteArrayAsync(Token)))
        {
            Assert.Empty(document.GetPage(1).GetImages());
        }

        await using var withLogo = await OrderTestHost.StartAsync(
            postgres, mailpit, services => services.AddSingleton<IObjectStorage>(new LogoOnlyStorage(TestImages.Png(400, 300, transparent: true))));
        var (_, _, _, _, proxy) = await ArrangeAsync(withLogo);
        await using (var scope = withLogo.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
            var settings = await db.FederationSettings.SingleAsync(Token);
            var logoId = Guid.CreateVersion7();
            settings.Logo = new ComparsaLogo { Id = logoId, ObjectKey = LogoStorage.KeyFor(logoId), Width = 400, Height = 300, SizeBytes = 2048, UploadedAt = DateTimeOffset.UtcNow };
            await db.SaveChangesAsync(Token);
        }

        using var form = await withLogo.FiringChief.GetAsync($"/api/distribution/proxies/{proxy}/form", Token);

        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
        using var pdf = PdfDocument.Open(await form.Content.ReadAsByteArrayAsync(Token));
        Assert.Single(pdf.GetPage(1).GetImages());
    }

    [Fact]
    public async Task Too_many_documents_are_refused()
    {
        await using var limited = await OrderTestHost.StartAsync(postgres, mailpit, settings: new Dictionary<string, string?> { ["RateLimits:Exports:PermitLimit"] = "1" });
        var (_, _, _, day, _) = await ArrangeAsync(limited);

        using var allowed = await limited.Admin.GetAsync($"/api/distribution/distributions/{day}/list/pdf", Token);
        using var refused = await limited.Admin.GetAsync($"/api/distribution/distributions/{day}/list/pdf", Token);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.TooManyRequests), (allowed.StatusCode, refused.StatusCode));
    }

    /// <summary>A validated order of Norte with a 2 kg holder and a reserve proxy, the powder day planned and the proxy registered.</summary>
    private static async Task<(ComparsaOrder Order, EditionEntry Holder, EditionEntry Proxy, Guid PowderDay, Guid ProxyId)> ArrangeAsync(OrderTestHost host)
    {
        var order = await host.AddOrderAsync(host.Current, host.Own.Id);
        var holder = await host.AddLicensedEntryAsync(order, "Abad Sintética", ValidThrough, powderKg: 2);
        var proxy = await host.AddLicensedEntryAsync(order, "Zamora Sintético", ValidThrough, status: ArquebusierStatus.Reserve);
        using var day = await DistributionRequests.PlanAsync(host.Admin, host.Current.Id, "POWDER", "2031-04-18", "Paraje Sintético");
        var dayId = (await ReadAsync<JsonElement>(day)).GetProperty("id").GetGuid();
        using var registered = await host.FiringChief.RegisterProxyAsync(host.Current.Id, holder.Id, proxy.Id);
        var proxyId = (await ReadAsync<JsonElement>(registered)).GetProperty("id").GetGuid();
        return (order, holder, proxy, dayId, proxyId);
    }

    private static async Task<Guid> PlanWeaponsDayAsync(OrderTestHost host)
    {
        using var day = await DistributionRequests.PlanAsync(host.Admin, host.Current.Id, "WEAPONS", "2031-04-12", "Almacén Sintético");
        Assert.Equal(HttpStatusCode.Created, day.StatusCode);
        return (await ReadAsync<JsonElement>(day)).GetProperty("id").GetGuid();
    }

    private static string? FileName(HttpResponseMessage response) =>
        response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

    private sealed class FailingAuditLog : IAuditLog
    {
        public Task RecordAsync(AuditRecord record, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Synthetic audit outage.");
    }

    /// <summary>A storage that serves one synthetic PNG for any key: the Federation logo the form reads.</summary>
    private sealed class LogoOnlyStorage(byte[] png) : IObjectStorage
    {
        public Task PutAsync(string key, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<StoredObject?> GetAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult<StoredObject?>(new StoredObject(new MemoryStream(png), "image/png", png.Length));

        public Task DeleteAsync(string key, CancellationToken cancellationToken) => Task.CompletedTask;

        public async IAsyncEnumerable<StoredObjectInfo> ListAsync(string prefix, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield break;
        }
    }

    private sealed class OutageStorage : IObjectStorage
    {
        public Task PutAsync(string key, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken) =>
            throw new StorageUnavailableException();

        public Task<StoredObject?> GetAsync(string key, CancellationToken cancellationToken) => throw new StorageUnavailableException();

        public Task DeleteAsync(string key, CancellationToken cancellationToken) => throw new StorageUnavailableException();

        public async IAsyncEnumerable<StoredObjectInfo> ListAsync(string prefix, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            throw new StorageUnavailableException();
#pragma warning disable CS0162 // An iterator needs a yield.
            yield break;
#pragma warning restore CS0162
        }
    }
}
