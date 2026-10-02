using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.FederationCatalog.Contracts;

namespace PolvorApp.Api.Tests.Registry;

/// <summary>
/// The registry's read contract for the compliance module (design D3): the facts the rules and the
/// statistics need, for the comparsas the caller asks for, without identifying data.
/// </summary>
[Collection(PostgresGroup.Name)]
public sealed class ArquebusierFactsTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private IdentityTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await IdentityTestHost.StartAsync(postgres, mailpit);

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    private async Task<IReadOnlyList<ArquebusierFacts>> ReadAsync(Func<IArquebusierFacts, Task<IReadOnlyList<ArquebusierFacts>>> read)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<IArquebusierFacts>());
    }

    [Fact]
    public async Task All_lists_every_comparsa_and_ids_restrict_the_list()
    {
        var norte = RegistryData.NewComparsa("Comparsa Sintética Norte");
        var sur = RegistryData.NewComparsa("Comparsa Sintética Sur", active: false);
        var model = RegistryData.NewWeaponModel("ARCABUZ SINTÉTICO AJENO");
        await _host.Services.SaveCatalogAsync(norte, sur, model);
        var inNorte = RegistryData.NewArquebusier(norte.Id);
        var inSur = RegistryData.NewArquebusier(sur.Id);
        await _host.Services.SaveRegistryAsync(inNorte, inSur, RegistryData.NewOwnedWeapon(inSur.Id, model.Id, "SINT-AJENA-1"));
        var ct = TestContext.Current.CancellationToken;

        var all = await ReadAsync(facts => facts.ListAllAsync(ct));
        var onlyNorte = await ReadAsync(facts => facts.ListAsync(new HashSet<Guid> { norte.Id }, ct));
        var none = await ReadAsync(facts => facts.ListAsync(new HashSet<Guid>(), ct));

        Assert.Equal(new[] { norte.Id, sur.Id }.Order(), all.Select(f => f.ComparsaId).Order());
        Assert.Equal([norte.Id], onlyNorte.Select(f => f.ComparsaId));
        Assert.Empty(onlyNorte.Single().OwnedWeaponModelIds);
        Assert.Equal([model.Id], all.Single(f => f.ComparsaId == sur.Id).OwnedWeaponModelIds);
        Assert.Empty(none);
    }

    [Fact]
    public async Task Facts_carry_the_license_course_photos_and_owned_weapons()
    {
        var comparsa = RegistryData.NewComparsa("Comparsa Sintética Datos");
        var trabuco = RegistryData.NewWeaponModel("TRABUCO SINTÉTICO", WeaponKind.Trabuco);
        var pistol = RegistryData.NewWeaponModel("PISTOLA SINTÉTICA", WeaponKind.Pistol);
        await _host.Services.SaveCatalogAsync(comparsa, trabuco, pistol);

        // Birth years tell the four apart, since the facts carry no identifier.
        var frontOnly = RegistryData.NewArquebusier(comparsa.Id);
        (frontOnly.BirthDate, frontOnly.Gender, frontOnly.Status, frontOnly.TrainingCompletedOn) =
            (new DateOnly(1981, 1, 1), Gender.Female, ArquebusierStatus.Reserve, new DateOnly(2025, 11, 15));
        (frontOnly.LicenseType, frontOnly.LicensePending, frontOnly.LicenseIssuedOn, frontOnly.LicenseExpiresOn) =
            (LicenseType.Ae, false, new DateOnly(2024, 3, 10), new DateOnly(2029, 3, 10));
        var bothPhotos = RegistryData.NewArquebusier(comparsa.Id);
        bothPhotos.BirthDate = new DateOnly(1982, 1, 1);
        (bothPhotos.LicenseType, bothPhotos.LicensePending, bothPhotos.LicenseIssuedOn, bothPhotos.LicenseExpiresOn) =
            (LicenseType.AProf, false, new DateOnly(2026, 2, 1), new DateOnly(2027, 2, 1));
        var pending = RegistryData.NewArquebusier(comparsa.Id);
        pending.BirthDate = new DateOnly(1983, 1, 1);
        (pending.LicenseType, pending.LicensePending) = (LicenseType.AProf, true);
        var unlicensed = RegistryData.NewArquebusier(comparsa.Id);
        unlicensed.BirthDate = new DateOnly(1984, 1, 1);
        await _host.Services.SaveRegistryAsync(
            frontOnly,
            bothPhotos,
            pending,
            unlicensed,
            RegistryData.NewPhoto(frontOnly.Id, ArquebusierPhotoKind.Id),
            RegistryData.NewPhoto(frontOnly.Id, ArquebusierPhotoKind.LicenseFront),
            RegistryData.NewPhoto(bothPhotos.Id, ArquebusierPhotoKind.LicenseFront),
            RegistryData.NewPhoto(bothPhotos.Id, ArquebusierPhotoKind.LicenseBack),
            RegistryData.NewPhoto(pending.Id, ArquebusierPhotoKind.LicenseBack),
            RegistryData.NewOwnedWeapon(frontOnly.Id, trabuco.Id, "SINT-HECHOS-1"),
            RegistryData.NewOwnedWeapon(frontOnly.Id, pistol.Id, "SINT-HECHOS-2"));

        var facts = (await ReadAsync(f => f.ListAsync([comparsa.Id], TestContext.Current.CancellationToken)))
            .ToDictionary(f => f.BirthDate.Year);

        var first = facts[1981];
        Assert.Equal((comparsa.Id, ArquebusierStatus.Reserve, Gender.Female), (first.ComparsaId, first.Status, first.Gender));
        Assert.Equal(new ArquebusierLicenseFacts.Issued(new DateOnly(2029, 3, 10), HasFrontPhoto: true, HasBackPhoto: false), first.License);
        Assert.Equal(new DateOnly(2025, 11, 15), first.TrainingCompletedOn);
        Assert.True(first.HasIdPhoto);
        Assert.Equal(new[] { trabuco.Id, pistol.Id }.Order(), first.OwnedWeaponModelIds.Order());

        Assert.Equal(new ArquebusierLicenseFacts.Issued(new DateOnly(2027, 2, 1), HasFrontPhoto: true, HasBackPhoto: true), facts[1982].License);
        Assert.IsType<ArquebusierLicenseFacts.Pending>(facts[1983].License);
        Assert.Null(facts[1984].License);
        Assert.False(facts[1984].HasIdPhoto);
        Assert.Null(facts[1984].TrainingCompletedOn);
        Assert.Empty(facts[1984].OwnedWeaponModelIds);
    }

    [Theory]
    [InlineData(typeof(ArquebusierFacts))]
    [InlineData(typeof(ArquebusierLicenseFacts.Issued))]
    [InlineData(typeof(ArquebusierLicenseFacts.Pending))]
    public void Facts_hold_no_identifying_data(Type type)
    {
        string[] forbidden = ["Id", "NationalId", "FederationId", "Email", "Phone"];

        var properties = type.GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(properties, name => forbidden.Contains(name) || name.Contains("Name", StringComparison.Ordinal));
    }

    [Fact]
    public void Facts_print_no_personal_value()
    {
        var license = new ArquebusierLicenseFacts.Issued(new DateOnly(2029, 3, 10), HasFrontPhoto: true, HasBackPhoto: true);
        var facts = new ArquebusierFacts(Guid.NewGuid(), ArquebusierStatus.Active, Gender.Male, new DateOnly(1990, 5, 1), license, null, HasIdPhoto: false, []);

        Assert.DoesNotContain("1990", facts.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("2029", license.ToString(), StringComparison.Ordinal);
    }
}
