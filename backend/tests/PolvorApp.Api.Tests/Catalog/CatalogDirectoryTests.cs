using Microsoft.Extensions.DependencyInjection;
using PolvorApp.Api.Platform.Database;
using PolvorApp.Api.Tests.Infrastructure;
using PolvorApp.FederationCatalog.Comparsas;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Persistence;
using PolvorApp.FederationCatalog.WeaponModels;

namespace PolvorApp.Api.Tests.Catalog;

/// <summary>
/// The read contract other modules use to validate and show comparsas and weapon models
/// (change add-arquebusier-registry, design D2).
/// </summary>
public sealed class CatalogDirectoryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly Comparsa _active = new() { Id = Guid.CreateVersion7(), Name = "Comparsa Sintética Activa", Side = Side.Moorish, CreatedAt = Now };
    private readonly Comparsa _inactive = new() { Id = Guid.CreateVersion7(), Name = "Comparsa Sintética Inactiva", Side = Side.Christian, Active = false, CreatedAt = Now };
    private readonly WeaponModel _arcabuz = new()
    {
        Id = Guid.CreateVersion7(),
        Kind = WeaponKind.Arcabuz,
        Side = Side.Moorish,
        Handedness = Handedness.Left,
        Size = WeaponSize.Small,
        Rentable = true,
        Label = "ARCABUZ MORO ZURDO (PEQUEÑO)",
        CreatedAt = Now,
    };
    private readonly WeaponModel _pistol = new() { Id = Guid.CreateVersion7(), Kind = WeaponKind.Pistol, Label = "PISTOLA", Active = false, CreatedAt = Now };

    private ApiFactory? _factory;

    public async ValueTask InitializeAsync()
    {
        _factory = new ApiFactory(await postgres.CreateMigratedDatabaseAsync());
        Assert.Equal(0, await MigrateCommand.RunAsync(_factory.Services, TestContext.Current.CancellationToken));
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
        db.Comparsas.AddRange(_active, _inactive);
        db.WeaponModels.AddRange(_arcabuz, _pistol);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task Unknown_ids_are_not_found()
    {
        var (directory, scope) = Resolve();
        await using (scope)
        {
            Assert.Null(await directory.FindComparsaAsync(Guid.CreateVersion7(), TestContext.Current.CancellationToken));
            Assert.Null(await directory.FindWeaponModelAsync(Guid.CreateVersion7(), TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task A_comparsa_is_returned_with_its_side_and_state()
    {
        var (directory, scope) = Resolve();
        await using (scope)
        {
            Assert.Equal(new ComparsaSummary(_inactive.Id, _inactive.Name, Side.Christian, Active: false),
                await directory.FindComparsaAsync(_inactive.Id, TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Several_comparsas_skip_unknown_ids()
    {
        var (directory, scope) = Resolve();
        await using (scope)
        {
            var found = await directory.FindComparsasAsync([_active.Id, Guid.CreateVersion7(), _inactive.Id], TestContext.Current.CancellationToken);

            Assert.Equal(new[] { _active.Id, _inactive.Id }.Order(), found.Select(c => c.Id).Order());
            Assert.True(found.Single(c => c.Id == _active.Id).Active);
        }
    }

    [Fact]
    public async Task Weapon_models_are_returned_with_their_attributes_and_state()
    {
        var (directory, scope) = Resolve();
        await using (scope)
        {
            Assert.Equal(
                new WeaponModelSummary(_arcabuz.Id, WeaponKind.Arcabuz, Side.Moorish, Handedness.Left, WeaponSize.Small, _arcabuz.Label, Active: true),
                await directory.FindWeaponModelAsync(_arcabuz.Id, TestContext.Current.CancellationToken));

            var found = await directory.FindWeaponModelsAsync([_pistol.Id, Guid.CreateVersion7()], TestContext.Current.CancellationToken);

            Assert.Equal([new WeaponModelSummary(_pistol.Id, WeaponKind.Pistol, null, null, null, "PISTOLA", Active: false)], found);
        }
    }

    [Fact]
    public async Task Empty_and_repeated_ids_are_handled()
    {
        var (directory, scope) = Resolve();
        await using (scope)
        {
            Assert.Empty(await directory.FindComparsasAsync([], TestContext.Current.CancellationToken));
            Assert.Empty(await directory.FindWeaponModelsAsync([], TestContext.Current.CancellationToken));
            Assert.Single(await directory.FindComparsasAsync([_active.Id, _active.Id], TestContext.Current.CancellationToken));
            Assert.Single(await directory.FindWeaponModelsAsync([_arcabuz.Id, _arcabuz.Id], TestContext.Current.CancellationToken));
        }
    }

    private (ICatalogDirectory Directory, AsyncServiceScope Scope) Resolve()
    {
        var scope = _factory!.Services.CreateAsyncScope();
        return (scope.ServiceProvider.GetRequiredService<ICatalogDirectory>(), scope);
    }
}
