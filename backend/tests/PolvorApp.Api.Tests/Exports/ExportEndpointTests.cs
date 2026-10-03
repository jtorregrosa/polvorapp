using System.Net;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Orders;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.SharedKernel.Auditing;
using UglyToad.PdfPig;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;
using static PolvorApp.Api.Tests.Infrastructure.OrderData;

namespace PolvorApp.Api.Tests.Exports;

/// <summary>
/// The export endpoints (specs: Who may export (BR-12), Comparsa list export, Excel and PDF, Exports
/// are audited, Exports are protected in transit; design D5, D7).
/// </summary>
public sealed class ExportEndpointTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private OrderTestHost _orders = null!;

    public async ValueTask InitializeAsync() => _orders = await OrderTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    private string Base => $"/api/exports/editions/{_orders.Current.Id}";

    [Fact]
    public async Task An_Admin_lists_the_definitions()
    {
        using var response = await _orders.Admin.GetAsync(Base, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var catalog = await ReadAsync<JsonElement>(response);
        var definitions = catalog.GetProperty("definitions").EnumerateArray().ToList();
        Assert.Equal(["powder-supplier", "rental-company", "arms-authority", "comparsa-list"], definitions.Select(d => d.GetProperty("name").GetString()));
        Assert.All(definitions, d => Assert.True(d.GetProperty("provisional").GetBoolean()));
        Assert.Equal("COMPARSA", definitions[3].GetProperty("audience").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("/recipients/arms-authority/pdf")]
    public async Task A_FiringChief_gets_no_federation_export(string path)
    {
        using var response = await _orders.FiringChief.GetAsync(Base + path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Empty(await _orders.Host.AuditEntriesAsync("ExportDownloaded"));
    }

    [Fact]
    public async Task The_catalogue_of_an_unknown_edition_is_not_found()
    {
        using var response = await _orders.Admin.GetAsync($"/api/exports/editions/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_recipient_download_is_audited_without_personal_data()
    {
        var nationalId = await SaveOrderAsync(_orders.Own.Id, OrderStatus.Validated);

        using var response = await _orders.Admin.GetAsync($"{Base}/recipients/arms-authority/pdf", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = Assert.Single(await _orders.Host.AuditEntriesAsync("ExportDownloaded"));
        Assert.Equal(("Export", "arms-authority", (Guid?)null), (entry.EntityType, entry.EntityId, entry.ComparsaId));
        using var data = JsonDocument.Parse(entry.Data!);
        Assert.Equal(("provisional-1", "PDF", _orders.Current.Id.ToString(), 0), (
            data.RootElement.GetProperty("version").GetString(), data.RootElement.GetProperty("format").GetString(),
            data.RootElement.GetProperty("editionId").GetString(), data.RootElement.GetProperty("rows").GetInt32()));
        Assert.DoesNotContain(nationalId, entry.Data!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recipient_exports_include_validated_orders_and_leave_out_the_others()
    {
        await SaveOrderAsync(_orders.Own.Id, OrderStatus.Validated);
        await SaveOrderAsync(_orders.Other.Id, OrderStatus.Submitted);

        using var response = await _orders.Admin.GetAsync($"{Base}/recipients/powder-supplier/xlsx", TestContext.Current.CancellationToken);

        using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)));
        var sheet = workbook.Worksheets.Single();
        Assert.Equal((_orders.Own.Name, "Total"), (sheet.Cell(6, 1).GetString(), sheet.Cell(7, 1).GetString()));
    }

    [Theory]
    [InlineData("powder-supplier", "xlsx", Xlsx)]
    [InlineData("rental-company", "xlsx", Xlsx)]
    [InlineData("arms-authority", "xlsx", Xlsx)]
    [InlineData("powder-supplier", "pdf", "application/pdf")]
    [InlineData("rental-company", "pdf", "application/pdf")]
    [InlineData("arms-authority", "pdf", "application/pdf")]
    public async Task An_Admin_downloads_each_recipient_export(string definition, string format, string contentType)
    {
        await SaveOrderAsync(_orders.Own.Id, OrderStatus.Validated);

        using var response = await _orders.Admin.GetAsync($"{Base}/recipients/{definition}/{format}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"polvorapp-2031-{definition}-provisional.{format}", FileName(response));
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("/recipients/arms-authority/csv")]
    [InlineData("/recipients/badges/pdf")]
    [InlineData("/recipients/comparsa-list/pdf")]
    public async Task An_unknown_format_or_definition_is_not_found(string path)
    {
        using var response = await _orders.Admin.GetAsync(Base + path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task An_unknown_edition_is_not_found()
    {
        using var response = await _orders.Admin.GetAsync($"/api/exports/editions/{Guid.NewGuid()}/recipients/arms-authority/xlsx", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Only_validated_orders_reach_the_recipients_and_none_gives_an_empty_file()
    {
        await SaveOrderAsync(_orders.Own.Id, OrderStatus.Submitted);

        using var response = await _orders.Admin.GetAsync($"{Base}/recipients/rental-company/xlsx", TestContext.Current.CancellationToken);

        using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)));
        var sheet = workbook.Worksheets.Single();
        Assert.Equal("Apellidos y nombre", sheet.Cell(5, 1).GetString());
        Assert.True(sheet.Cell(6, 1).IsEmpty());
    }

    [Fact]
    public async Task An_Admin_downloads_any_comparsas_list()
    {
        await SaveOrderAsync(_orders.Other.Id, OrderStatus.Validated);

        using var response = await _orders.Admin.GetAsync($"{Base}/comparsas/{_orders.Other.Id}/pdf", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.EndsWith("-provisional.pdf", FileName(response), StringComparison.Ordinal);
        Assert.DoesNotContain("-draft", FileName(response), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(OrderStatus.Draft)]
    [InlineData(OrderStatus.Submitted)]
    [InlineData(OrderStatus.Returned)]
    public async Task A_FiringChief_downloads_their_list_as_a_draft_until_validated(OrderStatus status)
    {
        await SaveOrderAsync(_orders.Own.Id, status);

        using var response = await _orders.FiringChief.GetAsync($"{Base}/comparsas/{_orders.Own.Id}/pdf", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.EndsWith("-draft-provisional.pdf", FileName(response), StringComparison.Ordinal);
        using var pdf = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Contains("BORRADOR", string.Join(" ", pdf.GetPage(1).GetWords().Select(w => w.Text)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_FiringChief_gets_no_list_of_another_comparsa_nor_an_unknown_one()
    {
        await SaveOrderAsync(_orders.Other.Id, OrderStatus.Validated);

        using var other = await _orders.FiringChief.GetAsync($"{Base}/comparsas/{_orders.Other.Id}/xlsx", TestContext.Current.CancellationToken);
        using var unknown = await _orders.FiringChief.GetAsync($"{Base}/comparsas/{Guid.NewGuid()}/xlsx", TestContext.Current.CancellationToken);

        await AssertProblemAsync(other, HttpStatusCode.NotFound, "exports.notFound");
        await AssertProblemAsync(unknown, HttpStatusCode.NotFound, "exports.notFound");
        Assert.True(other.Headers.CacheControl?.NoStore);
        Assert.Empty(await _orders.Host.AuditEntriesAsync("ExportDownloaded"));
    }

    [Fact]
    public async Task A_list_in_an_unknown_format_is_not_found()
    {
        await SaveOrderAsync(_orders.Own.Id, OrderStatus.Validated);

        using var response = await _orders.Admin.GetAsync($"{Base}/comparsas/{_orders.Own.Id}/docx", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_FiringChief_gets_no_list_of_a_draft_edition()
    {
        await SaveOrderAsync(_orders.Own.Id, OrderStatus.Draft);
        await _orders.SetCurrentStatusAsync(EditionStatus.Draft);

        using var response = await _orders.FiringChief.GetAsync($"{Base}/comparsas/{_orders.Own.Id}/xlsx", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_comparsa_without_an_order_has_no_list()
    {
        using var response = await _orders.FiringChief.GetAsync($"{Base}/comparsas/{_orders.Own.Id}/xlsx", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "exports.notPrepared");
        Assert.Empty(await _orders.Host.AuditEntriesAsync("ExportDownloaded"));
    }

    [Fact]
    public async Task The_list_follows_the_users_language()
    {
        await SaveOrderAsync(_orders.Own.Id, OrderStatus.Validated);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Base}/comparsas/{_orders.Own.Id}/xlsx");
        request.Headers.TryAddWithoutValidation("Accept-Language", "ca-ES-valencia");

        using var response = await _orders.FiringChief.SendAsync(request, TestContext.Current.CancellationToken);

        using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)));
        Assert.Equal("Cognoms i nom", workbook.Worksheets.Single().Cell(5, 1).GetString());
    }

    [Fact]
    public async Task A_download_is_audited_without_personal_data()
    {
        var nationalId = await SaveOrderAsync(_orders.Own.Id, OrderStatus.Submitted);

        using var response = await _orders.FiringChief.GetAsync($"{Base}/comparsas/{_orders.Own.Id}/xlsx", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = Assert.Single(await _orders.Host.AuditEntriesAsync("ExportDownloaded"));
        Assert.Equal(("Export", "comparsa-list", (Guid?)_orders.Own.Id), (entry.EntityType, entry.EntityId, entry.ComparsaId));
        Assert.NotNull(entry.ActorUserId);
        using var data = JsonDocument.Parse(entry.Data!);
        Assert.Equal(("XLSX", "SUBMITTED", 2031, 1), (
            data.RootElement.GetProperty("format").GetString(), data.RootElement.GetProperty("orderStatus").GetString(),
            data.RootElement.GetProperty("editionYear").GetInt32(), data.RootElement.GetProperty("rows").GetInt32()));
        Assert.DoesNotContain(nationalId, entry.Data!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_export_that_cannot_be_audited_is_not_returned()
    {
        await using var failing = await OrderTestHost.StartAsync(postgres, mailpit, services => services.AddScoped<IAuditLog, FailingAuditLog>());

        using var response = await failing.Admin.GetAsync($"/api/exports/editions/{failing.Current.Id}/recipients/powder-supplier/xlsx", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "exports.auditUnavailable");
    }

    [Fact]
    public async Task Too_many_exports_are_refused()
    {
        await using var limited = await OrderTestHost.StartAsync(
            postgres, mailpit, settings: new Dictionary<string, string?> { ["RateLimits:Exports:PermitLimit"] = "2" });
        var path = $"/api/exports/editions/{limited.Current.Id}/recipients/powder-supplier/xlsx";

        for (var i = 0; i < 2; i++)
        {
            using var allowed = await limited.Admin.GetAsync(path, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var refused = await limited.Admin.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.True(refused.Headers.CacheControl?.NoStore);

        // The limit is per user: another user is still served (here, refused for having no order).
        using var other = await limited.FiringChief.GetAsync(
            $"/api/exports/editions/{limited.Current.Id}/comparsas/{limited.Own.Id}/xlsx", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);
    }

    /// <summary>An order of the current edition with one powder entry; returns the entry's synthetic DNI/NIE.</summary>
    private async Task<string> SaveOrderAsync(Guid comparsaId, OrderStatus status)
    {
        var order = NewOrder(_orders.Current, comparsaId, status);
        if (status == OrderStatus.Returned)
        {
            order.ReturnReason = "Motivo sintético";
        }

        var entry = NewEntry(order, null);
        entry.PowderKg = 2;
        await _orders.Services.SaveOrdersAsync(order, entry);
        return entry.NationalId!;
    }

    private static string? FileName(HttpResponseMessage response) =>
        response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

    private sealed class FailingAuditLog : IAuditLog
    {
        public Task RecordAsync(AuditRecord record, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Synthetic audit outage.");
    }
}
