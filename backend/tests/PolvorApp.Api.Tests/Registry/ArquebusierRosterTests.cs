using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Platform.Database;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Contracts;
using static PolvorApp.Api.Tests.Infrastructure.RegistryData;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>
/// The registry's per-arquebusier roster for comparsa orders (add-comparsa-orders, design D5): what an
/// order needs about each arquebusier, the lookup of a lender by an exact national ID, and nothing else
/// (no contact data; the birth date only inside the compliance facts).
/// </summary>
public sealed class ArquebusierRosterTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory? _factory;

    private IServiceProvider Services => _factory!.Services;

    public async ValueTask InitializeAsync()
    {
        _factory = new ApiFactory(await postgres.CreateMigratedDatabaseAsync());
        Assert.Equal(0, await MigrateCommand.RunAsync(_factory.Services, TestContext.Current.CancellationToken));
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task The_comparsa_roster_is_sorted_in_spanish_order_with_facts_and_weapons()
    {
        var comparsa = NewComparsa("Comparsa Sintética Lista");
        var other = NewComparsa("Comparsa Sintética Otra");
        var model = NewWeaponModel("ARCABUZ SINTÉTICO LISTA");
        await Services.SaveCatalogAsync(comparsa, other, model);
        var nunez = NewArquebusier(comparsa.Id, "Núñez Sintético");
        var navarro = NewArquebusier(comparsa.Id, "Navarro Sintético");
        navarro.Status = ArquebusierStatus.Reserve;
        (navarro.LicenseType, navarro.LicenseIssuedOn, navarro.LicenseExpiresOn) = (LicenseType.Ae, new DateOnly(2024, 1, 1), new DateOnly(2029, 1, 1));
        navarro.TrainingCompletedOn = new DateOnly(2023, 11, 30);
        var elsewhere = NewArquebusier(other.Id, "Abad Sintético");
        var weapon = NewOwnedWeapon(nunez.Id, model.Id, "GUIA-LISTA-1");
        await Services.SaveRegistryAsync(nunez, navarro, elsewhere, weapon, NewPhoto(navarro.Id, ArquebusierPhotoKind.Id));

        var roster = await WithRosterAsync(roster => roster.ListByComparsaAsync(comparsa.Id, TestContext.Current.CancellationToken));

        Assert.Equal(["Navarro Sintético", "Núñez Sintético"], roster.Select(a => a.LastName));
        var first = roster[0];
        Assert.Equal((navarro.Id, comparsa.Id, navarro.NationalId, navarro.FederationId, ArquebusierStatus.Reserve), (first.Id, first.ComparsaId, first.NationalId, first.FederationId, first.Status));
        Assert.Equal((navarro.BirthDate, new DateOnly(2023, 11, 30), true), (first.BirthDate, first.TrainingCompletedOn, first.HasIdPhoto));
        Assert.Equal(new ArquebusierLicenseFacts.Issued(LicenseType.Ae, new DateOnly(2029, 1, 1), false, false), first.License);
        Assert.Empty(first.Weapons);
        var owned = Assert.Single(roster[1].Weapons);
        Assert.Equal((weapon.Id, nunez.Id, model.Id, "1234", "GUIA-LISTA-1"), (owned.Id, owned.OwnerId, owned.WeaponModelId, owned.WeaponNumber, owned.OwnershipGuideNumber));
    }

    [Fact]
    public async Task Find_many_reads_arquebusiers_of_any_comparsa_and_skips_unknown_ids()
    {
        var comparsa = NewComparsa("Comparsa Sintética Varios");
        var other = NewComparsa("Comparsa Sintética Varios Dos");
        await Services.SaveCatalogAsync(comparsa, other);
        var first = NewArquebusier(comparsa.Id);
        var second = NewArquebusier(other.Id);
        await Services.SaveRegistryAsync(first, second);

        var found = await WithRosterAsync(roster => roster.FindManyAsync([first.Id, second.Id, Guid.CreateVersion7()], TestContext.Current.CancellationToken));

        Assert.Equal(new HashSet<Guid> { first.Id, second.Id }, found.Select(a => a.Id).ToHashSet());
    }

    [Fact]
    public async Task A_lender_is_found_by_the_exact_normalised_national_id()
    {
        var comparsa = NewComparsa("Comparsa Sintética Prestamista");
        var model = NewWeaponModel("TRABUCO SINTÉTICO PRESTADO");
        await Services.SaveCatalogAsync(comparsa, model);
        var owner = NewArquebusier(comparsa.Id, "Prestamista Sintético");
        var weapon = NewOwnedWeapon(owner.Id, model.Id, "GUIA-PRESTADA");
        await Services.SaveRegistryAsync(owner, weapon);

        var lender = await WithRosterAsync(roster => roster.FindLenderAsync(owner.NationalId, TestContext.Current.CancellationToken));
        var missing = await WithRosterAsync(roster => roster.FindLenderAsync(NextIdentity().NationalId, TestContext.Current.CancellationToken));

        Assert.NotNull(lender);
        Assert.Equal((owner.Id, "Arcabucero", "Prestamista Sintético", owner.NationalId, comparsa.Id), (lender.ArquebusierId, lender.FirstName, lender.LastName, lender.NationalId, lender.ComparsaId));
        Assert.Equal([weapon.Id], lender.Weapons.Select(w => w.Id));
        Assert.Null(missing);
    }

    [Fact]
    public async Task Owned_weapons_and_registered_national_ids_are_looked_up()
    {
        var comparsa = NewComparsa("Comparsa Sintética Armas");
        var model = NewWeaponModel("ARCABUZ SINTÉTICO ARMAS");
        await Services.SaveCatalogAsync(comparsa, model);
        var owner = NewArquebusier(comparsa.Id);
        var weapon = NewOwnedWeapon(owner.Id, model.Id, "GUIA-ARMAS");
        await Services.SaveRegistryAsync(owner, weapon);

        var weapons = await WithRosterAsync(roster => roster.FindOwnedWeaponsAsync([weapon.Id, Guid.CreateVersion7()], TestContext.Current.CancellationToken));

        Assert.Equal([(weapon.Id, owner.Id)], weapons.Select(w => (w.Id, w.OwnerId)));
        Assert.True(await WithRosterAsync(roster => roster.IsNationalIdRegisteredAsync(owner.NationalId, TestContext.Current.CancellationToken)));
        Assert.False(await WithRosterAsync(roster => roster.IsNationalIdRegisteredAsync(NextIdentity().NationalId, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public void The_roster_records_print_no_personal_data()
    {
        var weapon = new RosterWeapon(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "1234", "GUIA-SECRETA");
        var arquebusier = new RosterArquebusier(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "Nombre", "Apellido Secreto", "00000101G", 800101,
            ArquebusierStatus.Active, new DateOnly(1990, 5, 1), null, null, HasIdPhoto: false, [weapon]);
        var lender = new LenderSummary(Guid.CreateVersion7(), "Nombre", "Apellido Secreto", "00000101G", Guid.CreateVersion7(), []);

        Assert.Equal(nameof(RosterWeapon), weapon.ToString());
        Assert.Equal(nameof(RosterArquebusier), arquebusier.ToString());
        Assert.Equal(nameof(LenderSummary), lender.ToString());
    }

    [Fact]
    public void A_lender_weapon_has_no_ownership_guide() =>
        Assert.DoesNotContain(typeof(LenderWeapon).GetProperties(), p => p.Name.Contains("Guide", StringComparison.Ordinal));

    private async Task<T> WithRosterAsync<T>(Func<IArquebusierRoster, Task<T>> read)
    {
        await using var scope = Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<IArquebusierRoster>());
    }
}
