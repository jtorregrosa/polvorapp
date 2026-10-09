using System.Net;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.Distribution.Persistence;
using PolvorApp.FestivalEditions.Contracts;
using static PolvorApp.Api.Tests.Distribution.HandoverSyncEndpointTests;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Distribution;

/// <summary>
/// Spec "Powder handovers (UC-21)": an Admin undoes a recorded handover with its version, which frees the
/// holder and the flask number; and "Handover screens": the plan counts the powder day's handovers.
/// </summary>
public sealed class HandoverUndoEndpointTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private OrderTestHost _orders = null!;
    private Arranged _day = null!;

    public async ValueTask InitializeAsync()
    {
        _orders = await OrderTestHost.StartAsync(postgres, mailpit);
        _day = await ArrangeAsync(_orders);
    }

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Undoing_frees_the_holder_and_the_flask_number()
    {
        var recorded = Assert.Single(await SyncAsync(_orders.Admin, _day.PowderDay, Item(_day.Holder.Id, flask: "P-117")));
        var handover = recorded.GetProperty("handover");

        using var undo = await UndoAsync(_orders.Admin, handover.GetProperty("id").GetGuid(), handover.GetProperty("version").GetUInt32());

        Assert.Equal(HttpStatusCode.NoContent, undo.StatusCode);
        Assert.Equal(0, await CountAsync());
        var again = await SyncAsync(_orders.Admin, _day.PowderDay, Item(_day.Small.Id, flask: "P-117"));
        Assert.Equal("RECORDED", Assert.Single(again).GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task A_stale_version_is_refused_and_keeps_the_handover()
    {
        var handover = Assert.Single(await SyncAsync(_orders.Admin, _day.PowderDay, Item(_day.Owned.Id))).GetProperty("handover");

        using var undo = await UndoAsync(_orders.Admin, handover.GetProperty("id").GetGuid(), handover.GetProperty("version").GetUInt32() + 1);

        await AssertProblemAsync(undo, HttpStatusCode.Conflict, "distribution.modified");
        Assert.Equal(1, await CountAsync());
    }

    [Fact]
    public async Task Outside_an_edition_in_progress_nothing_is_undone()
    {
        var handover = Assert.Single(await SyncAsync(_orders.Admin, _day.PowderDay, Item(_day.Owned.Id))).GetProperty("handover");
        await _orders.SetCurrentStatusAsync(EditionStatus.Closed);

        using var undo = await UndoAsync(_orders.Admin, handover.GetProperty("id").GetGuid(), handover.GetProperty("version").GetUInt32());

        await AssertProblemAsync(undo, HttpStatusCode.Conflict, "distribution.editionNotInProgress");
        Assert.Equal(1, await CountAsync());
    }

    [Fact]
    public async Task A_FiringChief_cannot_undo_and_an_unknown_handover_is_not_found()
    {
        var handover = Assert.Single(await SyncAsync(_orders.Admin, _day.PowderDay, Item(_day.Owned.Id))).GetProperty("handover");

        using var firingChief = await UndoAsync(_orders.FiringChief, handover.GetProperty("id").GetGuid(), handover.GetProperty("version").GetUInt32());
        using var unknown = await UndoAsync(_orders.Admin, Guid.CreateVersion7(), 1);

        Assert.Equal(HttpStatusCode.Forbidden, firingChief.StatusCode);
        await AssertProblemAsync(unknown, HttpStatusCode.NotFound, "distribution.notFound");
        Assert.Equal(1, await CountAsync());
    }

    [Fact]
    public async Task Recording_and_undoing_are_audited_without_names()
    {
        var handover = Assert.Single(await SyncAsync(_orders.Admin, _day.PowderDay, Item(_day.Holder.Id, collector: _day.Proxy.Id, flask: "P-117"))).GetProperty("handover");
        using (var undo = await UndoAsync(_orders.Admin, handover.GetProperty("id").GetGuid(), handover.GetProperty("version").GetUInt32()))
        {
            Assert.Equal(HttpStatusCode.NoContent, undo.StatusCode);
        }

        foreach (var action in new[] { "HandoverRecorded", "HandoverUndone" })
        {
            var entry = Assert.Single(await _orders.Host.AuditEntriesAsync(action));
            Assert.Equal(("Handover", handover.GetProperty("id").GetString(), _orders.Registry.AdminId), (entry.EntityType, entry.EntityId, entry.ActorUserId));
            using var data = JsonDocument.Parse(entry.Data!);
            Assert.Equal(("P-117", true, _day.Holder.Id), (data.RootElement.GetProperty("rentalFlaskNumber").GetString(), data.RootElement.GetProperty("byProxy").GetBoolean(), data.RootElement.GetProperty("entryId").GetGuid()));
            Assert.DoesNotContain(_day.Holder.NationalId!, entry.Data!, StringComparison.Ordinal);
            Assert.DoesNotContain("Abad", entry.Data!, StringComparison.Ordinal);
            Assert.DoesNotContain("Zamora", entry.Data!, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_plan_counts_the_powder_day_handovers_for_Admins_only()
    {
        await SyncAsync(_orders.Admin, _day.PowderDay, Item(_day.Owned.Id));

        var admin = await DistributionRequests.PlanOfAsync(_orders.Admin, _orders.Current.Id);
        var firingChief = await DistributionRequests.PlanOfAsync(_orders.FiringChief, _orders.Current.Id);

        var powder = admin.GetProperty("days").EnumerateArray().Single(d => d.GetProperty("type").GetString() == "POWDER");
        Assert.Equal((1, 3), (powder.GetProperty("handovers").GetProperty("recorded").GetInt32(), powder.GetProperty("handovers").GetProperty("holders").GetInt32()));
        var hidden = firingChief.GetProperty("days").EnumerateArray().Single(d => d.GetProperty("type").GetString() == "POWDER");
        Assert.True(!hidden.TryGetProperty("handovers", out var counts) || counts.ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task The_powder_list_fills_in_a_recorded_handover()
    {
        await SyncAsync(_orders.Admin, _day.PowderDay, Item(_day.Holder.Id, collector: _day.Proxy.Id, flask: "P-117", traceability1: "A3"));

        using var excel = await _orders.Admin.GetAsync($"/api/distribution/distributions/{_day.PowderDay}/list/xlsx", Token);

        using var workbook = new XLWorkbook(new MemoryStream(await excel.Content.ReadAsByteArrayAsync(Token)));
        var sheet = workbook.Worksheets.Single();
        var header = sheet.RowsUsed().First(r => r.Cell(1).GetString() == "Nº");
        int Column(string name) => header.CellsUsed().Single(c => c.GetString() == name).Address.ColumnNumber;
        var row = sheet.RowsUsed().Single(r => r.Cell(Column("Apellidos y nombre")).GetString() == "Abad Sintética, Arcabucero");
        Assert.Equal(("P-117", "A3", "Autorizado"),
            (row.Cell(Column("Nº cantimplora")).GetString(), row.Cell(Column("Trazabilidad 1")).GetString(), row.Cell(Column("Recogida por")).GetString()));
        Assert.Contains(Enumerable.Range(1, 6).Select(r => sheet.Cell(r, 1).GetString()), line => line == "Entregas registradas: 1");
    }

    private static Task<HttpResponseMessage> UndoAsync(HttpClient client, Guid id, uint version) =>
        client.DeleteAsync($"/api/distribution/handovers/{id}?version={version}", Token);

    private async Task<int> CountAsync()
    {
        await using var scope = _orders.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DistributionDbContext>().Handovers.CountAsync(Token);
    }
}
