using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.Api.Tests.Registry;
using PolvorApp.ArquebusierRegistry.Persistence;
using PolvorApp.AuditPrivacy.Contracts;
using PolvorApp.AuditPrivacy.Privacy;
using PolvorApp.FederationCatalog.WeaponModels;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Storage;

namespace PolvorApp.Api.Tests.Privacy;

/// <summary>Spec audit-privacy "Looking up / Exporting / Erasing a person's data", design D5–D6: the registry's part.</summary>
public sealed class RegistryPersonalDataTests(PostgresFixture postgres, MailpitFixture mailpit) : IAsyncLifetime
{
    private readonly WeaponModel _model = RegistryData.NewWeaponModel("ARCABUZ SINTÉTICO PRIVADO");
    private RegistryTestHost _registry = null!;

    public async ValueTask InitializeAsync()
    {
        _registry = await RegistryTestHost.StartAsync(postgres, mailpit);
        await _registry.Services.SaveCatalogAsync(_model);
    }

    public async ValueTask DisposeAsync() => await _registry.DisposeAsync();

    [Fact]
    public async Task A_registered_arquebusier_is_described_with_weapons_and_photos()
    {
        var (id, nationalId) = await RegisterWithWeaponAndPhotoAsync();

        var summary = await RunAsync(r => r.DescribeAsync(new PersonalDataSubject.Person(nationalId), TestContext.Current.CancellationToken));

        Assert.NotNull(summary.Registry);
        Assert.Equal((id, _registry.Own.Id, 1, 1), (summary.Registry.ArquebusierId, summary.Registry.ComparsaId, summary.Registry.OwnedWeapons, summary.Registry.Photos));
    }

    [Fact]
    public async Task The_export_holds_the_record_the_weapons_and_the_photo()
    {
        var (_, nationalId) = await RegisterWithWeaponAndPhotoAsync();

        var parts = await RunAsync(r => r.ExportAsync(new PersonalDataSubject.Person(nationalId), TestContext.Current.CancellationToken));

        var sheets = parts.SelectMany(p => p.Sheets).ToDictionary(s => s.Code);
        var registry = Assert.Single(sheets["registry"].Rows);
        Assert.Equal(nationalId, registry[sheets["registry"].Columns.ToList().IndexOf("nationalId")]);
        Assert.Equal("Comparsa Sintética Propia", registry[sheets["registry"].Columns.ToList().IndexOf("comparsa")]);
        var weapon = Assert.Single(sheets["ownedWeapons"].Rows);
        Assert.Equal(["ARCABUZ SINTÉTICO PRIVADO", "77", "GUIA-PRIVADA-1"], weapon);
        var photo = Assert.Single(parts.SelectMany(p => p.Files));
        Assert.Equal("photos/id.jpg", photo.Name);
        Assert.True(photo.Content.Length > 0);
    }

    [Fact]
    public async Task The_erasure_deletes_the_arquebusier_and_their_images_and_is_audited()
    {
        var (id, nationalId) = await RegisterWithWeaponAndPhotoAsync();
        var photoKey = await PhotoKeyAsync(id);

        var result = await RunAsync(r => r.EraseAsync(new PersonalDataSubject.Person(nationalId), Audit, TestContext.Current.CancellationToken));

        Assert.NotNull(result);
        Assert.Equal(1, result.Counts["arquebusiersDeleted"]);
        Assert.Equal(1, result.Counts["photosDeleted"]);
        using var gone = await _registry.Admin.GetAsync($"/api/arquebusiers/{id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        await using var scope = _registry.Services.CreateAsyncScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IObjectStorage>().GetAsync(photoKey, TestContext.Current.CancellationToken));
        var deleted = Assert.Single(await _registry.Host.AuditEntriesAsync("ArquebusierDeleted"));
        Assert.Contains("\"source\": \"gdprErasure\"", deleted.Data, StringComparison.Ordinal);
        var erased = Assert.Single(await _registry.Host.AuditEntriesAsync("PersonalDataErased"));
        Assert.DoesNotContain(nationalId, erased.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_held_is_empty_and_erases_nothing()
    {
        var nationalId = RegistryData.NextIdentity().NationalId;

        var summary = await RunAsync(r => r.DescribeAsync(new PersonalDataSubject.Person(nationalId), TestContext.Current.CancellationToken));
        var result = await RunAsync(r => r.EraseAsync(new PersonalDataSubject.Person(nationalId), Audit, TestContext.Current.CancellationToken));

        Assert.False(summary.HoldsAnything);
        Assert.Null(result);
        Assert.Empty(await _registry.Host.AuditEntriesAsync("PersonalDataErased"));
    }

    private static AuditRecord Audit(IReadOnlyDictionary<string, int> counts) =>
        new("PersonalDataErased", "PersonalDataRequest", Data: new { reference = "REQ-PRUEBA-1", counts });

    private async Task<T> RunAsync<T>(Func<PersonalDataRequests, Task<T>> run)
    {
        await using var scope = _registry.Services.CreateAsyncScope();
        return await run(scope.ServiceProvider.GetRequiredService<PersonalDataRequests>());
    }

    private async Task<(Guid Id, string NationalId)> RegisterWithWeaponAndPhotoAsync()
    {
        var arquebusier = await _registry.RegisterAsync(_registry.Own.Id);
        var id = arquebusier.GetProperty("id").GetGuid();
        using (var weapon = await _registry.Admin.PostAsJsonAsync(
            $"/api/arquebusiers/{id}/owned-weapons",
            new { weaponModelId = _model.Id, weaponNumber = "77", ownershipGuideNumber = "GUIA-PRIVADA-1" },
            TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Created, weapon.StatusCode);
        }

        using (var photo = await PhotoRequests.UploadAsync(_registry.Admin, id, "id", TestImages.Jpeg(600, 800)))
        {
            Assert.True(photo.IsSuccessStatusCode, await photo.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        return (id, arquebusier.GetProperty("nationalId").GetString()!);
    }

    private async Task<string> PhotoKeyAsync(Guid arquebusierId)
    {
        await using var scope = _registry.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArquebusierRegistryDbContext>();
        return await db.Photos.Where(p => p.ArquebusierId == arquebusierId).Select(p => p.ObjectKey).SingleAsync(TestContext.Current.CancellationToken);
    }
}
