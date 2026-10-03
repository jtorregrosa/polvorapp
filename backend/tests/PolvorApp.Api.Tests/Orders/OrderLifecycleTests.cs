using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Persistence;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Orders;

/// <summary>Specs "Submitting an order (UC-14)" and "Reviewing orders (UC-15)" (design D8).</summary>
public sealed class OrderLifecycleTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private OrderTestHost _orders = null!;

    public async ValueTask InitializeAsync() => _orders = await OrderTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    [Fact]
    public async Task A_FiringChief_submits_with_the_attestation_and_the_warnings_remain()
    {
        var (arquebusier, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Sin Curso Sintético");
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);

        var submitted = await MoveAsync(_orders.FiringChief, order, "submit", new { attestation = true });

        Assert.Equal("SUBMITTED", submitted.GetProperty("status").GetString());
        var submission = submitted.GetProperty("submission");
        Assert.Equal((true, false), (submission.GetProperty("attested").GetBoolean(), submission.GetProperty("byAdmin").GetBoolean()));
        Assert.NotEqual(JsonValueKind.Null, submission.GetProperty("at").ValueKind);
        var entry = Assert.Single(submitted.GetProperty("entries").EnumerateArray());
        Assert.Contains("COURSE_MISSING", entry.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()));
        var audit = Assert.Single(await _orders.Host.AuditEntriesAsync("ComparsaOrderSubmitted"));
        using var data = JsonDocument.Parse(audit.Data!);
        Assert.Equal(("DRAFT", "SUBMITTED", true, 1), (
            data.RootElement.GetProperty("previous").GetString(), data.RootElement.GetProperty("current").GetString(),
            data.RootElement.GetProperty("attested").GetBoolean(), data.RootElement.GetProperty("entriesWithWarnings").GetInt32()));
        Assert.Equal(arquebusier.Id.ToString(), entry.GetProperty("arquebusier").GetProperty("id").GetString());
    }

    [Fact]
    public async Task An_Admin_submits_on_the_comparsas_behalf_with_closed_orders()
    {
        var order = await _orders.PrepareAsync(_orders.Admin, _orders.Own.Id);
        await _orders.SetOrdersOpenAsync(false);

        var submitted = await MoveAsync(_orders.Admin, order, "submit", new { });

        var submission = submitted.GetProperty("submission");
        Assert.Equal(("SUBMITTED", false, true), (submitted.GetProperty("status").GetString(), submission.GetProperty("attested").GetBoolean(), submission.GetProperty("byAdmin").GetBoolean()));
        var audit = Assert.Single(await _orders.Host.AuditEntriesAsync("ComparsaOrderSubmitted"));
        Assert.Contains("\"byAdmin\"", audit.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Submission_refreshes_the_history_copies()
    {
        var (arquebusier, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Antes Sintético");
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        await RenameAsync(arquebusier.Id, "Después Sintético");

        await MoveAsync(_orders.FiringChief, order, "submit", new { attestation = true });

        var copy = await _orders.ReadOrdersAsync(db => db.Entries.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Después Sintético", copy.LastName);
    }

    [Fact]
    public async Task A_FiringChief_must_attest()
    {
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);

        using var response = await PostAsync(_orders.FiringChief, order, "submit", new { attestation = false });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.Equal("DRAFT", (await OrderTestHost.GetOrderAsync(_orders.FiringChief, Id(order))).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Only_draft_and_returned_orders_are_submitted()
    {
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        await MoveAsync(_orders.FiringChief, order, "submit", new { attestation = true });

        using var again = await PostAsync(_orders.FiringChief, await Reload(order), "submit", new { attestation = true });

        await AssertProblemAsync(again, HttpStatusCode.Conflict, "orders.invalidTransition");
    }

    [Fact]
    public async Task A_FiringChief_cannot_submit_with_closed_orders()
    {
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        await _orders.SetOrdersOpenAsync(false);

        using var response = await PostAsync(_orders.FiringChief, order, "submit", new { attestation = true });

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "orders.closed");
    }

    [Fact]
    public async Task A_stale_version_is_refused()
    {
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        await TouchAsync(Id(order));

        using var response = await PostAsync(_orders.FiringChief, order, "submit", new { attestation = true });

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "orders.modified");
    }

    [Theory]
    [InlineData("validate")]
    [InlineData("return")]
    public async Task A_stale_version_is_refused_on_review(string move)
    {
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        var submitted = await MoveAsync(_orders.FiringChief, order, "submit", new { attestation = true });
        await TouchAsync(Id(order));

        using var response = await PostAsync(_orders.Admin, submitted, move, new { reason = "Motivo sintético" });

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "orders.modified");
    }

    [Fact]
    public async Task A_FiringChief_cannot_submit_another_comparsas_order()
    {
        var order = await _orders.PrepareAsync(_orders.Admin, _orders.Other.Id);

        using var response = await PostAsync(_orders.FiringChief, order, "submit", new { attestation = true });

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "orders.notFound");
        Assert.Equal("DRAFT", (await Reload(order)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_submission_keeps_the_version_of_entries_whose_copy_did_not_change()
    {
        await _orders.AddArquebusierAsync(_orders.Own.Id, "Quieto Sintético", weapons: 1);
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);

        var submitted = await MoveAsync(_orders.FiringChief, order, "submit", new { attestation = true });

        Assert.Equal(
            order.GetProperty("entries")[0].GetProperty("version").GetUInt32(),
            submitted.GetProperty("entries")[0].GetProperty("version").GetUInt32());
    }

    [Fact]
    public async Task A_submission_refreshes_the_copy_of_a_registered_lenders_loan()
    {
        var (lender, weapons) = await _orders.AddArquebusierAsync(_orders.Other.Id, "Prestamista Sintético", weapons: 1);
        var (borrower, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Prestatario Sintético");
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        var entryId = order.GetProperty("entries")[0].GetProperty("id").GetString();
        await using (var scope = _orders.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ComparsaOrdersDbContext>();
            var entry = await db.Entries.SingleAsync(e => e.ArquebusierId == borrower.Id, TestContext.Current.CancellationToken);
            entry.WeaponSource = PolvorApp.ComparsaOrders.Entries.WeaponSource.Loan;
            db.Loans.Add(new PolvorApp.ComparsaOrders.Loans.WeaponLoan
            {
                Id = Guid.CreateVersion7(),
                EntryId = entry.Id,
                LenderKind = PolvorApp.ComparsaOrders.Entries.LenderKind.Arquebusier,
                LenderOwnedWeaponId = weapons[0].Id,
                LenderFirstName = lender.FirstName,
                LenderLastName = lender.LastName,
                LenderNationalId = lender.NationalId,
                LenderComparsaId = _orders.Other.Id,
                WeaponModelId = weapons[0].WeaponModelId,
                WeaponNumber = weapons[0].WeaponNumber,
                OwnershipGuideNumber = weapons[0].OwnershipGuideNumber,
                CopiedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await RenameAsync(lender.Id, "Renombrado Sintético");
        var submitted = await MoveAsync(_orders.FiringChief, await Reload(order), "submit", new { attestation = true });

        var loan = submitted.GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("id").GetString() == entryId).GetProperty("loan");
        Assert.Equal("Renombrado Sintético", loan.GetProperty("lenderLastName").GetString());
    }

    [Fact]
    public async Task A_removed_owned_weapon_blocks_the_submission_and_names_the_entry()
    {
        var (_, weapons) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Vendedor Sintético", weapons: 1);
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        await using (var scope = _orders.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>().OwnedWeapons
                .Where(w => w.Id == weapons[0].Id).ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        var reloaded = await Reload(order);
        using var response = await PostAsync(_orders.FiringChief, reloaded, "submit", new { attestation = true });

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "orders.entriesInvalid");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var invalid = Assert.Single(problem.RootElement.GetProperty("entries").EnumerateArray());
        Assert.Equal(reloaded.GetProperty("entries")[0].GetProperty("id").GetString(), invalid.GetProperty("entryId").GetString());
        Assert.Equal(["ownedWeaponMissing"], invalid.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()));
        Assert.Equal(["ownedWeaponMissing"], reloaded.GetProperty("entries")[0].GetProperty("issues").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task A_returned_order_is_fixed_and_submitted_again()
    {
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        await MoveAsync(_orders.FiringChief, order, "submit", new { attestation = true });
        var returned = await MoveAsync(_orders.Admin, await Reload(order), "return", new { reason = "Revisad la cantimplora.\nGracias." });

        var again = await MoveAsync(_orders.FiringChief, returned, "submit", new { attestation = true });

        Assert.Equal("RETURNED", returned.GetProperty("status").GetString());
        Assert.Equal("Revisad la cantimplora.\nGracias.", returned.GetProperty("review").GetProperty("returnReason").GetString());
        Assert.Equal("SUBMITTED", again.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, again.GetProperty("review").GetProperty("returnReason").ValueKind);
    }

    [Fact]
    public async Task An_Admin_validates_a_submitted_order_and_closes_one_never_submitted()
    {
        await _orders.AddArquebusierAsync(_orders.Other.Id, "Otro Sintético");
        var submitted = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        await MoveAsync(_orders.FiringChief, submitted, "submit", new { attestation = true });
        var draft = await _orders.PrepareAsync(_orders.Admin, _orders.Other.Id);
        await _orders.SetOrdersOpenAsync(false);

        var validated = await MoveAsync(_orders.Admin, await Reload(submitted), "validate", new { });
        var closed = await MoveAsync(_orders.Admin, draft, "validate", new { });

        Assert.Equal("VALIDATED", validated.GetProperty("status").GetString());
        Assert.Equal(("VALIDATED", JsonValueKind.Null), (closed.GetProperty("status").GetString(), closed.GetProperty("submission").ValueKind));
        var audits = await _orders.Host.AuditEntriesAsync("ComparsaOrderValidated");
        Assert.Equal(
            ["DRAFT>VALIDATED", "SUBMITTED>VALIDATED"],
            audits.Select(a =>
            {
                using var data = JsonDocument.Parse(a.Data!);
                return data.RootElement.GetProperty("previous").GetString() + ">" + data.RootElement.GetProperty("current").GetString();
            }).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_validated_order_can_be_returned_and_a_draft_cannot()
    {
        var order = await _orders.PrepareAsync(_orders.Admin, _orders.Own.Id);

        using var draft = await PostAsync(_orders.Admin, order, "return", new { reason = "Motivo sintético" });
        var validated = await MoveAsync(_orders.Admin, await Reload(order), "validate", new { });
        var returned = await MoveAsync(_orders.Admin, validated, "return", new { reason = "Motivo sintético" });
        using var again = await PostAsync(_orders.Admin, returned, "validate", new { });

        await AssertProblemAsync(draft, HttpStatusCode.Conflict, "orders.invalidTransition");
        Assert.Equal("RETURNED", returned.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task A_return_needs_a_reason(string? reason)
    {
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        await MoveAsync(_orders.FiringChief, order, "submit", new { attestation = true });

        using var response = await PostAsync(_orders.Admin, await Reload(order), "return", new { reason });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
    }

    [Fact]
    public async Task A_reason_over_500_characters_is_refused()
    {
        var order = await _orders.PrepareAsync(_orders.Admin, _orders.Own.Id);

        using var response = await PostAsync(_orders.Admin, order, "return", new { reason = new string('a', 501) });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
    }

    [Fact]
    public async Task Validating_an_invalid_order_is_refused()
    {
        var order = await _orders.PrepareAsync(_orders.Admin, _orders.Own.Id);
        var (arquebusier, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Alquiler Sintético");
        await using (var scope = _orders.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ComparsaOrdersDbContext>();
            var entry = OrderData.NewEntry(await db.Orders.SingleAsync(TestContext.Current.CancellationToken), arquebusier.Id);
            (entry.WeaponSource, entry.RentalWeaponModelId) = (PolvorApp.ComparsaOrders.Entries.WeaponSource.Rental, _orders.NotOffered.Id);
            db.Entries.Add(entry);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var response = await PostAsync(_orders.Admin, await Reload(order), "validate", new { });

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "orders.entriesInvalid");
    }

    [Fact]
    public async Task A_FiringChief_cannot_review()
    {
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        await MoveAsync(_orders.FiringChief, order, "submit", new { attestation = true });

        using var validate = await PostAsync(_orders.FiringChief, await Reload(order), "validate", new { });
        using var reject = await PostAsync(_orders.FiringChief, await Reload(order), "return", new { reason = "Motivo" });

        Assert.Equal(HttpStatusCode.Forbidden, validate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, reject.StatusCode);
        Assert.Equal("SUBMITTED", (await Reload(order)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_return_does_not_copy_the_reason_into_the_audit_trail()
    {
        var order = await _orders.PrepareAsync(_orders.Admin, _orders.Own.Id);
        var validated = await MoveAsync(_orders.Admin, order, "validate", new { });

        await MoveAsync(_orders.Admin, validated, "return", new { reason = "Texto libre sintético" });

        var audit = Assert.Single(await _orders.Host.AuditEntriesAsync("ComparsaOrderReturned"));
        Assert.DoesNotContain("Texto libre", audit.Data, StringComparison.Ordinal);
    }

    private static Guid Id(JsonElement order) => Guid.Parse(order.GetProperty("id").GetString()!);

    private Task<JsonElement> Reload(JsonElement order) => OrderTestHost.GetOrderAsync(_orders.Admin, Id(order));

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, JsonElement order, string move, object body)
    {
        var json = JsonSerializer.SerializeToNode(body)!.AsObject();
        json["version"] = order.GetProperty("version").GetUInt32();
        return client.PostAsync($"/api/comparsa-orders/{Id(order)}/{move}", json);
    }

    private static async Task<JsonElement> MoveAsync(HttpClient client, JsonElement order, string move, object body)
    {
        using var response = await PostAsync(client, order, move, body);
        return await ReadAsync<JsonElement>(response);
    }

    private async Task RenameAsync(Guid arquebusierId, string lastName)
    {
        await using var scope = _orders.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>().Arquebusiers.Where(a => a.Id == arquebusierId)
            .ExecuteUpdateAsync(a => a.SetProperty(x => x.LastName, lastName), TestContext.Current.CancellationToken);
    }

    private async Task TouchAsync(Guid orderId)
    {
        await using var scope = _orders.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ComparsaOrdersDbContext>().Orders.Where(o => o.Id == orderId)
            .ExecuteUpdateAsync(o => o.SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow), TestContext.Current.CancellationToken);
    }
}
