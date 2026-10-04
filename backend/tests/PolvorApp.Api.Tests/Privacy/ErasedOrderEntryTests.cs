using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.AuditPrivacy.Contracts;
using PolvorApp.AuditPrivacy.Privacy;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Loans;
using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.Api.Tests.Privacy;

/// <summary>Spec comparsa-orders "Erased entries": shown without identity, read-only, still counted.</summary>
public sealed class ErasedOrderEntryTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private OrderTestHost _orders = null!;

    public async ValueTask InitializeAsync() => _orders = await OrderTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    [Fact]
    public async Task An_erased_entry_shows_no_identity_cannot_be_edited_and_still_counts()
    {
        var (person, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Borrable Sintética");
        var order = await _orders.PrepareAsync(_orders.Admin, _orders.Own.Id);
        var orderId = order.GetProperty("id").GetGuid();
        await _orders.ReadOrdersAsync(db => db.Entries.Where(e => e.ArquebusierId == person.Id)
            .ExecuteUpdateAsync(e => e.SetProperty(x => x.PowderKg, 2), TestContext.Current.CancellationToken));
        await _orders.SetOrdersOpenAsync(false);
        await EraseAsync(person.NationalId);
        await _orders.SetOrdersOpenAsync(true);

        var after = await OrderTestHost.GetOrderAsync(_orders.Admin, orderId);
        var entry = after.GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("erased").GetBoolean());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("arquebusier").GetProperty("nationalId").ValueKind);
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("arquebusier").GetProperty("lastName").ValueKind);
        Assert.Equal(2, after.GetProperty("totals").GetProperty("powderKg").GetInt32());

        using var edit = await _orders.Admin.PutAsJsonAsync(
            $"/api/comparsa-orders/{orderId}/entries/{entry.GetProperty("id").GetString()}",
            new { version = entry.GetProperty("version").GetUInt32(), status = "ACTIVE", powderKg = 1, capsBoxes = 0, weaponSource = "NONE", flask = "NONE" },
            TestContext.Current.CancellationToken);
        await IdentityAssertions.AssertProblemAsync(edit, HttpStatusCode.Conflict, "orders.entryErased");
    }

    /// <summary>
    /// Spec comparsa-orders "Erased entries": quantities keep counting in the orders dashboard and
    /// billing, and the erased person is never found by the lender lookup again.
    /// </summary>
    [Fact]
    public async Task The_dashboard_and_billing_keep_an_erased_entry_and_the_lookup_no_longer_finds_them()
    {
        var (person, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Contada Sintética");
        var order = await _orders.PrepareAsync(_orders.Admin, _orders.Own.Id);
        var orderId = order.GetProperty("id").GetGuid();
        await _orders.ReadOrdersAsync(db => db.Entries.Where(e => e.ArquebusierId == person.Id)
            .ExecuteUpdateAsync(e => e.SetProperty(x => x.PowderKg, 2), TestContext.Current.CancellationToken));
        // Closed first, so the entry is anonymised rather than removed and only the erasure changes.
        await _orders.SetOrdersOpenAsync(false);
        var (totalsBefore, billingBefore) = await CountsAsync(orderId);

        await EraseAsync(person.NationalId);

        var (totalsAfter, billingAfter) = await CountsAsync(orderId);
        // Quantities stay; the compliance warnings were the registry record's, which is gone (BR-04).
        Assert.Equal(WithoutWarnings(totalsBefore), WithoutWarnings(totalsAfter));
        Assert.Equal(0, JsonNode.Parse(totalsAfter)!["entriesWithWarnings"]!.GetValue<int>());
        Assert.Equal(billingBefore, billingAfter);
        await _orders.SetOrdersOpenAsync(true);
        using var lookup = await _orders.FiringChief.PostAsJsonAsync(
            "/api/comparsa-orders/lender-lookup", new { nationalId = person.NationalId }, TestContext.Current.CancellationToken);
        var found = await IdentityAssertions.ReadAsync<JsonElement>(lookup);
        Assert.False(found.GetProperty("registered").GetBoolean());
        Assert.Equal(JsonValueKind.Null, found.GetProperty("lender").ValueKind);
    }

    private static string WithoutWarnings(string totals)
    {
        var node = JsonNode.Parse(totals)!.AsObject();
        node.Remove("entriesWithWarnings");
        return node.ToJsonString();
    }

    /// <summary>The order's totals on the orders dashboard, and its billing, as JSON.</summary>
    private async Task<(string Totals, string Billing)> CountsAsync(Guid orderId)
    {
        using var response = await _orders.Admin.GetAsync("/api/comparsa-orders/overview", TestContext.Current.CancellationToken);
        var overview = await IdentityAssertions.ReadAsync<JsonElement>(response);
        var row = overview.GetProperty("rows").EnumerateArray()
            .Single(r => r.GetProperty("comparsa").GetProperty("id").GetGuid() == _orders.Own.Id);
        var billing = (await OrderTestHost.GetOrderAsync(_orders.Admin, orderId)).GetProperty("billing");
        Assert.Equal(2, row.GetProperty("totals").GetProperty("powderKg").GetInt32());
        return (row.GetProperty("totals").GetRawText(), billing.GetRawText());
    }

    [Fact]
    public async Task A_borrower_keeps_an_erased_lenders_loan_read_only_until_the_weapon_source_changes()
    {
        var (borrower, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Recibe Sintética");
        var order = await _orders.PrepareAsync(_orders.Admin, _orders.Own.Id);
        var orderId = order.GetProperty("id").GetGuid();
        var entryId = order.GetProperty("entries").EnumerateArray()
            .Single(e => e.GetProperty("arquebusier").GetProperty("id").GetString() == borrower.Id.ToString()).GetProperty("id").GetGuid();
        var lender = RegistryData.NextIdentity().NationalId;
        await _orders.ReadOrdersAsync(async db =>
        {
            await db.Entries.Where(e => e.Id == entryId).ExecuteUpdateAsync(e => e.SetProperty(x => x.WeaponSource, WeaponSource.Loan), TestContext.Current.CancellationToken);
            db.Loans.Add(new WeaponLoan
            {
                Id = Guid.CreateVersion7(),
                EntryId = entryId,
                LenderKind = LenderKind.External,
                CopiedAt = DateTimeOffset.UtcNow,
                LenderFirstName = "Presta",
                LenderLastName = "Externa Sintética",
                LenderNationalId = lender,
                WeaponModelId = _orders.Offered.Id,
                WeaponNumber = "9",
                OwnershipGuideNumber = "GUIA-BORRADA-1",
            });
            return await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        });
        await EraseAsync(lender);

        var entry = (await OrderTestHost.GetOrderAsync(_orders.Admin, orderId)).GetProperty("entries").EnumerateArray()
            .Single(e => e.GetProperty("id").GetGuid() == entryId);
        Assert.True(entry.GetProperty("loan").GetProperty("lenderErased").GetBoolean());
        Assert.False(entry.GetProperty("erased").GetBoolean());

        using var keep = await PutAsync(orderId, entry, "LOAN");
        await IdentityAssertions.AssertProblemAsync(keep, HttpStatusCode.Conflict, "orders.entryErased");
        using var change = await PutAsync(orderId, entry, "NONE");
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        Assert.False(await _orders.ReadOrdersAsync(db => db.Loans.AnyAsync(l => l.EntryId == entryId, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task The_lenders_comparsa_sees_a_weapon_lent_to_an_erased_borrower_without_their_name()
    {
        var (_, weapons) = await _orders.AddArquebusierAsync(_orders.Other.Id, "Dueña Sintética", weapons: 1);
        var (borrower, _) = await _orders.AddArquebusierAsync(_orders.Own.Id, "Prestataria Borrable");
        var order = await _orders.PrepareAsync(_orders.Admin, _orders.Own.Id);
        var entry = order.GetProperty("entries").EnumerateArray()
            .Single(e => e.GetProperty("arquebusier").GetProperty("id").GetString() == borrower.Id.ToString());
        using (var lend = await _orders.Admin.PutAsJsonAsync(
            $"/api/comparsa-orders/{order.GetProperty("id").GetString()}/entries/{entry.GetProperty("id").GetString()}",
            new { version = entry.GetProperty("version").GetUInt32(), status = "ACTIVE", powderKg = 1, capsBoxes = 0, weaponSource = "LOAN", loan = new { ownedWeaponId = weapons[0].Id }, flask = "NONE" },
            TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, lend.StatusCode);
        }

        await _orders.SetOrdersOpenAsync(false);
        await EraseAsync(borrower.NationalId);
        await _orders.SetOrdersOpenAsync(true);

        var lent = Assert.Single((await _orders.PrepareAsync(_orders.Admin, _orders.Other.Id)).GetProperty("lentOut").EnumerateArray());
        Assert.True(lent.GetProperty("borrowerErased").GetBoolean());
        Assert.Equal(JsonValueKind.Null, lent.GetProperty("borrowerLastName").ValueKind);
        Assert.Equal(weapons[0].WeaponNumber, lent.GetProperty("weaponNumber").GetString());
    }

    private Task<HttpResponseMessage> PutAsync(Guid orderId, JsonElement entry, string source) =>
        _orders.Admin.PutAsJsonAsync(
            $"/api/comparsa-orders/{orderId}/entries/{entry.GetProperty("id").GetString()}",
            new
            {
                version = entry.GetProperty("version").GetUInt32(),
                status = "ACTIVE",
                powderKg = 0,
                capsBoxes = 0,
                weaponSource = source,
                flask = "NONE",
                loan = source == "LOAN"
                    ? new { external = new { firstName = "Otra", lastName = "Sintética", nationalId = "00000000T", weaponModelId = _orders.Offered.Id, weaponNumber = "1", ownershipGuideNumber = "G-1" } }
                    : null,
            },
            TestContext.Current.CancellationToken);

    private async Task EraseAsync(string nationalId)
    {
        await using var scope = _orders.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<PersonalDataRequests>().EraseAsync(
            new PersonalDataSubject.Person(nationalId),
            counts => new AuditRecord("PersonalDataErased", "PersonalDataRequest", Data: new { counts }),
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }
}
