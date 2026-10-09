using System.Net;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComparsaOrders.Orders;
using PolvorApp.Distribution.Handovers;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.SharedKernel.Auditing;
using static PolvorApp.Api.Tests.Infrastructure.DistributionData;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Distribution;

/// <summary>
/// Spec "Offline capture package (UC-21)" (add-offline-distribution-capture D2): the powder day's holders
/// as the printed list numbers them, with their proxy and the handovers already recorded, audited before
/// it is sent and never cached; Admins only, for the powder day of the edition in progress.
/// </summary>
public sealed class HandoverCaptureEndpointTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private OrderTestHost _orders = null!;
    private ComparsaOrder _order = null!;
    private EditionEntry _holder = null!;
    private EditionEntry _proxy = null!;
    private Guid _powderDay;

    public async ValueTask InitializeAsync()
    {
        _orders = await OrderTestHost.StartAsync(postgres, mailpit);
        (_order, _holder, _proxy, _powderDay) = await ArrangeAsync(_orders);
    }

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_Admin_downloads_the_powder_package_with_the_holder_and_their_proxy()
    {
        using var response = await PackageAsync(_orders.Admin, _powderDay);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var package = await ReadAsync<JsonElement>(response);
        Assert.Equal((2031, "2031-04-18", "Paraje Sintético"),
            (package.GetProperty("editionYear").GetInt32(), package.GetProperty("date").GetString(), package.GetProperty("location").GetString()));
        var row = Assert.Single(package.GetProperty("rows").EnumerateArray());
        Assert.Equal((1, _holder.Id, "Abad Sintética", _holder.NationalId),
            (row.GetProperty("number").GetInt32(), row.GetProperty("entryId").GetGuid(), row.GetProperty("lastName").GetString(), row.GetProperty("nationalId").GetString()));
        Assert.Equal((2, "RENTAL_2KG"), (row.GetProperty("powderKg").GetInt32(), row.GetProperty("flask").GetString()));
        var proxy = row.GetProperty("proxy");
        Assert.Equal((_proxy.Id, "Zamora Sintético", _proxy.NationalId),
            (proxy.GetProperty("entryId").GetGuid(), proxy.GetProperty("lastName").GetString(), proxy.GetProperty("nationalId").GetString()));
        Assert.Empty(package.GetProperty("handovers").EnumerateArray());
        var json = package.GetRawText();
        foreach (var field in new[] { "license", "phone", "email", "birth" })
        {
            Assert.DoesNotContain(field, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task The_package_numbers_holders_as_the_printed_list()
    {
        var other = await _orders.AddOrderAsync(_orders.Current, _orders.Other.Id);
        await _orders.AddLicensedEntryAsync(other, "Bernabeu Sintético", ValidThrough, powderKg: 1);
        await _orders.AddLicensedEntryAsync(_order, "Climent Sintética", ValidThrough, powderKg: 1);

        using var response = await PackageAsync(_orders.Admin, _powderDay);
        using var excel = await _orders.Admin.GetAsync($"/api/distribution/distributions/{_powderDay}/list/xlsx", Token);

        var rows = (await ReadAsync<JsonElement>(response)).GetProperty("rows").EnumerateArray()
            .Select(r => (r.GetProperty("number").GetInt32(), r.GetProperty("lastName").GetString() + ", " + r.GetProperty("firstName").GetString()))
            .ToList();
        using var workbook = new XLWorkbook(new MemoryStream(await excel.Content.ReadAsByteArrayAsync(Token)));
        var sheet = workbook.Worksheets.Single();
        var printed = Enumerable.Range(8, rows.Count).Select(r => ((int)sheet.Cell(r, 1).GetDouble(), sheet.Cell(r, 4).GetString())).ToList();
        Assert.Equal(3, rows.Count);
        Assert.Equal(printed, rows);
    }

    [Fact]
    public async Task The_package_holds_the_handovers_already_recorded()
    {
        var handover = new Handover
        {
            Id = Guid.CreateVersion7(),
            DistributionId = _powderDay,
            HolderEntryId = _holder.Id,
            DistributionNumber = 1,
            CollectedBy = HandoverCollector.Proxy,
            CollectorEntryId = _proxy.Id,
            PowderKg = 2,
            RentalFlaskNumber = "P-117",
            CollectedAt = DateTimeOffset.UtcNow,
            RecordedAt = DateTimeOffset.UtcNow,
        };
        await _orders.Services.SaveDistributionAsync(handover);

        using var response = await PackageAsync(_orders.Admin, _powderDay);

        var stored = Assert.Single((await ReadAsync<JsonElement>(response)).GetProperty("handovers").EnumerateArray());
        Assert.Equal((handover.Id, _holder.Id, "PROXY", _proxy.Id, "P-117"),
            (stored.GetProperty("id").GetGuid(), stored.GetProperty("holderEntryId").GetGuid(), stored.GetProperty("collectedBy").GetString(),
             stored.GetProperty("collectorEntryId").GetGuid(), stored.GetProperty("rentalFlaskNumber").GetString()));
        Assert.True(stored.GetProperty("version").GetUInt32() > 0);
    }

    [Fact]
    public async Task The_download_is_audited_without_names_or_national_ids()
    {
        using var response = await PackageAsync(_orders.Admin, _powderDay);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var entry = Assert.Single(await _orders.Host.AuditEntriesAsync("CapturePackageDownloaded"));
        Assert.Equal(("Distribution", _powderDay.ToString(), _orders.Registry.AdminId), (entry.EntityType, entry.EntityId, entry.ActorUserId));
        using var data = JsonDocument.Parse(entry.Data!);
        Assert.Equal(1, data.RootElement.GetProperty("rows").GetInt32());
        Assert.DoesNotContain(_holder.NationalId!, entry.Data!, StringComparison.Ordinal);
        Assert.DoesNotContain("Abad", entry.Data!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_weapons_day_has_no_package()
    {
        using var day = await DistributionRequests.PlanAsync(_orders.Admin, _orders.Current.Id, "WEAPONS", "2031-04-12", "Almacén Sintético");
        var weaponsDay = (await ReadAsync<JsonElement>(day)).GetProperty("id").GetGuid();

        using var response = await PackageAsync(_orders.Admin, weaponsDay);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "distribution.captureNotPowder");
    }

    [Fact]
    public async Task A_closed_edition_has_no_package()
    {
        await _orders.SetCurrentStatusAsync(EditionStatus.Closed);

        using var response = await PackageAsync(_orders.Admin, _powderDay);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "distribution.editionNotInProgress");
    }

    [Fact]
    public async Task A_FiringChief_is_refused_and_an_unknown_day_is_not_found()
    {
        using var firingChief = await PackageAsync(_orders.FiringChief, _powderDay);
        using var unknown = await PackageAsync(_orders.Admin, Guid.CreateVersion7());

        Assert.Equal(HttpStatusCode.Forbidden, firingChief.StatusCode);
        await AssertProblemAsync(unknown, HttpStatusCode.NotFound, "distribution.notFound");
        Assert.Empty(await _orders.Host.AuditEntriesAsync("CapturePackageDownloaded"));
    }

    [Fact]
    public async Task A_package_that_cannot_be_audited_is_not_sent()
    {
        await using var failing = await OrderTestHost.StartAsync(postgres, mailpit, services => services.AddScoped<IAuditLog, FailingAuditLog>());
        var (_, _, _, day) = await ArrangeAsync(failing);

        using var response = await PackageAsync(failing.Admin, day);

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "distribution.auditUnavailable");
    }

    private static Task<HttpResponseMessage> PackageAsync(HttpClient client, Guid dayId) =>
        client.GetAsync($"/api/distribution/distributions/{dayId}/capture", Token);

    private static async Task<(ComparsaOrder Order, EditionEntry Holder, EditionEntry Proxy, Guid PowderDay)> ArrangeAsync(OrderTestHost host)
    {
        var order = await host.AddOrderAsync(host.Current, host.Own.Id);
        var holder = await host.AddLicensedEntryAsync(order, "Abad Sintética", ValidThrough, powderKg: 2, flask: FlaskOption.Rental2Kg);
        var proxy = await host.AddLicensedEntryAsync(order, "Zamora Sintético", ValidThrough, status: ArquebusierStatus.Reserve);
        using var day = await DistributionRequests.PlanAsync(host.Admin, host.Current.Id, "POWDER", "2031-04-18", "Paraje Sintético");
        var dayId = (await ReadAsync<JsonElement>(day)).GetProperty("id").GetGuid();
        using var registered = await host.FiringChief.RegisterProxyAsync(host.Current.Id, holder.Id, proxy.Id);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        return (order, holder, proxy, dayId);
    }

    private sealed class FailingAuditLog : IAuditLog
    {
        public Task RecordAsync(AuditRecord record, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Synthetic audit outage.");
    }
}
