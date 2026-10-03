using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Entries;
using static PolvorApp.Api.Tests.Infrastructure.IdentityAssertions;

namespace PolvorApp.Api.Tests.Orders;

/// <summary>Spec "Weapon loans (UC-13, BR-09)" on the entry edit (design D9, D3).</summary>
public sealed class LoanTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private OrderTestHost _orders = null!;

    public async ValueTask InitializeAsync() => _orders = await OrderTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _orders.DisposeAsync();

    [Fact]
    public async Task A_loan_from_an_arquebusier_of_another_comparsa_keeps_a_copy_and_shows_no_guide()
    {
        var (lender, weapons) = await _orders.AddArquebusierAsync(_orders.Other.Id, "Prestamista Sintético", weapons: 1);
        var (borrower, _) = await BorrowerAsync();
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);

        var entry = await SaveAsync(order, borrower, new { ownedWeaponId = weapons[0].Id });

        var loan = entry.GetProperty("loan");
        Assert.Equal(("ARQUEBUSIER", "Prestamista Sintético", _orders.Other.Name, weapons[0].WeaponNumber), (
            loan.GetProperty("lenderKind").GetString(), loan.GetProperty("lenderLastName").GetString(),
            loan.GetProperty("lenderComparsaName").GetString(), loan.GetProperty("weaponNumber").GetString()));
        Assert.Equal(JsonValueKind.Null, loan.GetProperty("ownershipGuideNumber").ValueKind);
        Assert.Equal(JsonValueKind.Null, loan.GetProperty("externalNationalId").ValueKind);
        var stored = await _orders.ReadOrdersAsync(db => db.Loans.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal((lender.NationalId, weapons[0].OwnershipGuideNumber, (Guid?)_orders.Other.Id), (stored.LenderNationalId, stored.OwnershipGuideNumber, stored.LenderComparsaId));
    }

    [Fact]
    public async Task Lending_the_borrowers_own_weapon_is_refused()
    {
        var (borrower, weapons) = await BorrowerAsync(weapons: 1);
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);

        using var response = await PutAsync(order, borrower, new { ownedWeaponId = weapons[0].Id });

        Assert.Equal("ownWeapon", await ErrorAsync(response, "loan.ownedWeaponId"));
    }

    [Fact]
    public async Task An_unknown_weapon_is_not_found()
    {
        var (borrower, _) = await BorrowerAsync();
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);

        using var response = await PutAsync(order, borrower, new { ownedWeaponId = Guid.CreateVersion7() });

        Assert.Equal("notFound", await ErrorAsync(response, "loan.ownedWeaponId"));
    }

    [Fact]
    public async Task An_external_owner_is_stored_with_the_guide_upper_cased()
    {
        var (borrower, _) = await BorrowerAsync();
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);

        var entry = await SaveAsync(order, borrower, new { external = External(RegistryData.NextIdentity().NationalId) });

        var loan = entry.GetProperty("loan");
        Assert.Equal(("EXTERNAL", "Externo Sintético", "AB-123", _orders.Offered.Label), (
            loan.GetProperty("lenderKind").GetString(), loan.GetProperty("lenderLastName").GetString(),
            loan.GetProperty("ownershipGuideNumber").GetString(), loan.GetProperty("weaponModel").GetProperty("label").GetString()));
    }

    [Fact]
    public async Task An_invalid_or_registered_external_national_id_is_refused()
    {
        var (registered, _) = await _orders.AddArquebusierAsync(_orders.Other.Id, "Registrado Sintético");
        var (borrower, _) = await BorrowerAsync();
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);

        using var invalid = await PutAsync(order, borrower, new { external = External("12345678A") });
        using var taken = await PutAsync(order, borrower, new { external = External(registered.NationalId) });

        Assert.Equal("checkLetter", await ErrorAsync(invalid, "loan.nationalId"));
        Assert.Equal("lenderRegistered", await ErrorAsync(taken, "loan.nationalId"));
    }

    [Fact]
    public async Task One_weapon_may_be_lent_to_two_borrowers()
    {
        var (_, weapons) = await _orders.AddArquebusierAsync(_orders.Other.Id, "Generoso Sintético", weapons: 1);
        var (first, _) = await BorrowerAsync("Primero Sintético");
        var (second, _) = await BorrowerAsync("Segundo Sintético");
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);

        await SaveAsync(order, first, new { ownedWeaponId = weapons[0].Id });
        var reloaded = await OrderTestHost.GetOrderAsync(_orders.FiringChief, OrderId(order));
        await SaveAsync(reloaded, second, new { ownedWeaponId = weapons[0].Id });

        Assert.Equal(2, await _orders.ReadOrdersAsync(db => db.Loans.CountAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Leaving_or_replacing_a_loan_deletes_its_row()
    {
        var (_, weapons) = await _orders.AddArquebusierAsync(_orders.Other.Id, "Cambiante Sintético", weapons: 1);
        var (borrower, _) = await BorrowerAsync();
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);

        var external = await SaveAsync(order, borrower, new { external = External(RegistryData.NextIdentity().NationalId) });
        var externalId = await _orders.ReadOrdersAsync(db => db.Loans.Select(l => l.Id).SingleAsync(TestContext.Current.CancellationToken));
        var registered = await SaveAsync(await Reload(order), borrower, new { ownedWeaponId = weapons[0].Id });
        var registeredLoan = await _orders.ReadOrdersAsync(db => db.Loans.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken));
        var rental = await SaveEntryAsync(await Reload(order), borrower, body => (body["weaponSource"], body["rentalWeaponModelId"]) = ("RENTAL", _orders.Offered.Id));

        Assert.Equal("EXTERNAL", external.GetProperty("loan").GetProperty("lenderKind").GetString());
        Assert.NotEqual(externalId, registeredLoan.Id);
        Assert.Equal(LenderKind.Arquebusier, registeredLoan.LenderKind);
        Assert.Equal("ARQUEBUSIER", registered.GetProperty("loan").GetProperty("lenderKind").GetString());
        Assert.Equal(JsonValueKind.Null, rental.GetProperty("loan").ValueKind);
        Assert.Equal(0, await _orders.ReadOrdersAsync(db => db.Loans.CountAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Editing_other_values_keeps_the_loan()
    {
        var (_, weapons) = await _orders.AddArquebusierAsync(_orders.Other.Id, "Fiel Sintético", weapons: 1);
        var (borrower, _) = await BorrowerAsync();
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        await SaveAsync(order, borrower, new { ownedWeaponId = weapons[0].Id });

        var saved = await SaveEntryAsync(await Reload(order), borrower, body => body["powderKg"] = 2);

        Assert.Equal((2, "ARQUEBUSIER"), (saved.GetProperty("powderKg").GetInt32(), saved.GetProperty("loan").GetProperty("lenderKind").GetString()));
        Assert.Equal(1, await _orders.ReadOrdersAsync(db => db.Loans.CountAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task A_registered_lenders_copy_is_refreshed_on_save()
    {
        var (lender, weapons) = await _orders.AddArquebusierAsync(_orders.Other.Id, "Viejo Sintético", weapons: 1);
        var (borrower, _) = await BorrowerAsync();
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        await SaveAsync(order, borrower, new { ownedWeaponId = weapons[0].Id });
        await using (var scope = _orders.Services.CreateAsyncScope())
        {
            var registry = scope.ServiceProvider.GetRequiredService<PolvorApp.ArquebusierRegistry.Persistence.ArquebusierRegistryDbContext>();
            await registry.Arquebusiers.Where(a => a.Id == lender.Id)
                .ExecuteUpdateAsync(a => a.SetProperty(x => x.LastName, "Nuevo Sintético"), TestContext.Current.CancellationToken);
        }

        var saved = await SaveEntryAsync(await Reload(order), borrower, body => body["powderKg"] = 1);

        Assert.Equal("Nuevo Sintético", saved.GetProperty("loan").GetProperty("lenderLastName").GetString());
    }

    [Fact]
    public async Task An_unchanged_external_loan_is_kept_after_its_owner_registers()
    {
        var (borrower, _) = await BorrowerAsync();
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        var nationalId = RegistryData.NextIdentity().NationalId;
        var saved = await SaveAsync(order, borrower, new { external = External(nationalId) });
        var joined = RegistryData.NewArquebusier(_orders.Other.Id, "Ya Registrado Sintético");
        joined.NationalId = nationalId;
        await _orders.Services.SaveRegistryAsync(joined);

        var again = await SaveEntryAsync(await Reload(order), borrower, body =>
        {
            body["powderKg"] = 2;
            body["loan"] = new
            {
                external = new
                {
                    firstName = "Prestamista",
                    lastName = "Externo Sintético",
                    nationalId,
                    weaponModelId = _orders.Offered.Id,
                    weaponNumber = "37-17",
                    ownershipGuideNumber = "AB-123",
                },
            };
        });

        Assert.Equal((2, "EXTERNAL"), (again.GetProperty("powderKg").GetInt32(), again.GetProperty("loan").GetProperty("lenderKind").GetString()));
        Assert.Equal("EXTERNAL", saved.GetProperty("loan").GetProperty("lenderKind").GetString());
    }

    [Fact]
    public async Task An_external_model_must_exist_and_be_active()
    {
        var retired = RegistryData.NewWeaponModel("PISTOLA SINTÉTICA RETIRADA", PolvorApp.FederationCatalog.Contracts.WeaponKind.Pistol, active: false);
        await _orders.Services.SaveCatalogAsync(retired);
        var (borrower, _) = await BorrowerAsync();
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);

        using var unknown = await PutExternalAsync(order, borrower, Guid.CreateVersion7());
        using var inactive = await PutExternalAsync(order, borrower, retired.Id);

        Assert.Equal("notFound", await ErrorAsync(unknown, "loan.weaponModelId"));
        Assert.Equal("inactive", await ErrorAsync(inactive, "loan.weaponModelId"));
    }

    [Fact]
    public async Task A_registered_owner_typed_as_external_is_audited_like_a_lookup()
    {
        var (registered, _) = await _orders.AddArquebusierAsync(_orders.Other.Id, "Sondeado Sintético");
        var (borrower, _) = await BorrowerAsync();
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);

        using var response = await PutAsync(order, borrower, new { external = External(registered.NationalId) });

        Assert.Equal("lenderRegistered", await ErrorAsync(response, "loan.nationalId"));
        var audit = Assert.Single(await _orders.Host.AuditEntriesAsync("LoanLenderLookedUp"));
        Assert.DoesNotContain(registered.NationalId, audit.Data, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await _orders.ReadOrdersAsync(db => db.Loans.CountAsync(TestContext.Current.CancellationToken)));
        Assert.Empty(await _orders.Host.AuditEntriesAsync("EditionEntryUpdated"));
    }

    [Fact]
    public async Task A_registered_lenders_dni_and_guide_never_reach_the_borrowers_order()
    {
        var (lender, weapons) = await _orders.AddArquebusierAsync(_orders.Other.Id, "Discreto Sintético", weapons: 1);
        var (borrower, _) = await BorrowerAsync();
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);

        await SaveAsync(order, borrower, new { ownedWeaponId = weapons[0].Id });

        var text = (await Reload(order)).GetRawText();
        Assert.DoesNotContain(lender.NationalId, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(weapons[0].OwnershipGuideNumber, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_loan_edit_audits_the_field_names_without_the_external_values()
    {
        var (borrower, _) = await BorrowerAsync();
        var order = await _orders.PrepareAsync(_orders.FiringChief, _orders.Own.Id);
        var nationalId = RegistryData.NextIdentity().NationalId;

        await SaveAsync(order, borrower, new { external = External(nationalId) });

        var audit = Assert.Single(await _orders.Host.AuditEntriesAsync("EditionEntryUpdated"));
        using var data = JsonDocument.Parse(audit.Data!);
        Assert.Equal(["weaponSource", "loan"], data.RootElement.GetProperty("fields").EnumerateArray().Select(f => f.GetString()));
        Assert.DoesNotContain(nationalId, audit.Data, StringComparison.Ordinal);
        Assert.DoesNotContain("Externo", audit.Data, StringComparison.Ordinal);
    }

    private static object External(string nationalId) => new
    {
        firstName = "Prestamista",
        lastName = "Externo Sintético",
        nationalId,
        weaponModelId = (Guid?)null,
        weaponNumber = "37-17",
        ownershipGuideNumber = "ab-123",
    };

    private Task<(PolvorApp.ArquebusierRegistry.Arquebusiers.Arquebusier Arquebusier, List<PolvorApp.ArquebusierRegistry.OwnedWeapons.OwnedWeapon> Weapons)> BorrowerAsync(
        string lastName = "Prestatario Sintético", int weapons = 0) =>
        _orders.AddArquebusierAsync(_orders.Own.Id, lastName, weapons: weapons);

    private static Guid OrderId(JsonElement order) => Guid.Parse(order.GetProperty("id").GetString()!);

    private Task<JsonElement> Reload(JsonElement order) => OrderTestHost.GetOrderAsync(_orders.FiringChief, OrderId(order));

    private static JsonElement Entry(JsonElement order, Guid arquebusierId) =>
        order.GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("arquebusier").GetProperty("id").GetString() == arquebusierId.ToString());

    private static Dictionary<string, object?> Body(JsonElement entry, Action<Dictionary<string, object?>> change)
    {
        var body = new Dictionary<string, object?>
        {
            ["version"] = entry.GetProperty("version").GetUInt32(),
            ["status"] = "ACTIVE",
            ["powderKg"] = entry.GetProperty("powderKg").GetInt32(),
            ["capsBoxes"] = 0,
            ["weaponSource"] = entry.GetProperty("weaponSource").GetString(),
            ["flask"] = "NONE",
        };
        change(body);
        return body;
    }

    /// <summary>Sets the borrower's entry to <c>LOAN</c> with <paramref name="loan"/>; an external owner gets the offered model.</summary>
    private Task<HttpResponseMessage> PutAsync(JsonElement order, PolvorApp.ArquebusierRegistry.Arquebusiers.Arquebusier borrower, object loan)
    {
        var json = JsonSerializer.SerializeToNode(loan)!.AsObject();
        if (json["external"] is { } external)
        {
            external["weaponModelId"] = _orders.Offered.Id.ToString();
        }

        var entry = Entry(order, borrower.Id);
        return _orders.FiringChief.PutAsJsonAsync(
            $"/api/comparsa-orders/{OrderId(order)}/entries/{entry.GetProperty("id").GetString()}",
            Body(entry, body => (body["weaponSource"], body["loan"]) = ("LOAN", json)),
            TestContext.Current.CancellationToken);
    }

    private Task<HttpResponseMessage> PutExternalAsync(JsonElement order, PolvorApp.ArquebusierRegistry.Arquebusiers.Arquebusier borrower, Guid modelId)
    {
        var entry = Entry(order, borrower.Id);
        var external = new
        {
            firstName = "Prestamista",
            lastName = "Externo Sintético",
            nationalId = RegistryData.NextIdentity().NationalId,
            weaponModelId = modelId,
            weaponNumber = "37-17",
            ownershipGuideNumber = "AB-123",
        };
        return _orders.FiringChief.PutAsJsonAsync(
            $"/api/comparsa-orders/{OrderId(order)}/entries/{entry.GetProperty("id").GetString()}",
            Body(entry, body => (body["weaponSource"], body["loan"]) = ("LOAN", new { external })),
            TestContext.Current.CancellationToken);
    }

    private async Task<JsonElement> SaveAsync(JsonElement order, PolvorApp.ArquebusierRegistry.Arquebusiers.Arquebusier borrower, object loan)
    {
        using var response = await PutAsync(order, borrower, loan);
        return Entry(await ReadAsync<JsonElement>(response), borrower.Id);
    }

    private async Task<JsonElement> SaveEntryAsync(JsonElement order, PolvorApp.ArquebusierRegistry.Arquebusiers.Arquebusier borrower, Action<Dictionary<string, object?>> change)
    {
        var entry = Entry(order, borrower.Id);
        using var response = await _orders.FiringChief.PutAsJsonAsync(
            $"/api/comparsa-orders/{OrderId(order)}/entries/{entry.GetProperty("id").GetString()}",
            Body(entry, change),
            TestContext.Current.CancellationToken);
        return Entry(await ReadAsync<JsonElement>(response), borrower.Id);
    }

    private static async Task<string?> ErrorAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return problem.RootElement.GetProperty("errors").GetProperty(field).GetString();
    }
}
