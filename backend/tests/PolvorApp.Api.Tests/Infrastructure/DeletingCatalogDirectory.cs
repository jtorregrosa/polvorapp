using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Persistence;

namespace PolvorApp.Api.Tests.Infrastructure;

/// <summary>
/// Replays the race of design D10: right after the registry looks up <see cref="Target"/> (a comparsa)
/// or <see cref="ModelTarget"/> (a weapon model) in the catalog, the row is deleted, so the registry
/// write meets the cross-schema foreign key.
/// </summary>
public sealed class DeletingCatalogDirectory
{
    /// <summary>The comparsa to delete when it is next looked up; nothing happens while null.</summary>
    public Guid? Target { get; set; }

    /// <summary>The weapon model to delete when it is next looked up; nothing happens while null.</summary>
    public Guid? ModelTarget { get; set; }

    /// <summary>Whether the comparsa or weapon model was actually deleted.</summary>
    public bool Deleted { get; private set; }

    /// <summary>Wraps the host's catalog directory with this deleting one.</summary>
    public static DeletingCatalogDirectory Decorate(IServiceCollection services)
    {
        var deleting = new DeletingCatalogDirectory();
        var original = services.Last(d => d.ServiceType == typeof(ICatalogDirectory));
        services.Remove(original);
        services.AddScoped<ICatalogDirectory>(provider => new DeletingDirectory(
            (ICatalogDirectory)ActivatorUtilities.CreateInstance(
                provider, original.ImplementationType ?? throw new InvalidOperationException("The catalog directory must be registered by type.")), provider, deleting));
        return deleting;
    }

    private sealed class DeletingDirectory(ICatalogDirectory inner, IServiceProvider services, DeletingCatalogDirectory owner) : ICatalogDirectory
    {
        public async Task<ComparsaSummary?> FindComparsaAsync(Guid comparsaId, CancellationToken cancellationToken)
        {
            var comparsa = await inner.FindComparsaAsync(comparsaId, cancellationToken);
            if (comparsaId == owner.Target)
            {
                owner.Target = null;
                await using var scope = services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
                await db.Assignments.Where(a => a.ComparsaId == comparsaId).ExecuteDeleteAsync(cancellationToken);
                owner.Deleted = await db.Comparsas.Where(c => c.Id == comparsaId).ExecuteDeleteAsync(cancellationToken) == 1;
            }

            return comparsa;
        }

        public Task<IReadOnlyList<ComparsaSummary>> FindComparsasAsync(IReadOnlyCollection<Guid> comparsaIds, CancellationToken cancellationToken) =>
            inner.FindComparsasAsync(comparsaIds, cancellationToken);

        public async Task<WeaponModelSummary?> FindWeaponModelAsync(Guid weaponModelId, CancellationToken cancellationToken)
        {
            var model = await inner.FindWeaponModelAsync(weaponModelId, cancellationToken);
            if (weaponModelId == owner.ModelTarget)
            {
                owner.ModelTarget = null;
                await using var scope = services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<FederationCatalogDbContext>();
                owner.Deleted = await db.WeaponModels.Where(m => m.Id == weaponModelId).ExecuteDeleteAsync(cancellationToken) == 1;
            }

            return model;
        }

        public Task<IReadOnlyList<WeaponModelSummary>> FindWeaponModelsAsync(IReadOnlyCollection<Guid> weaponModelIds, CancellationToken cancellationToken) =>
            inner.FindWeaponModelsAsync(weaponModelIds, cancellationToken);

        public Task<ComparsaLogoImage?> ReadComparsaLogoAsync(Guid comparsaId, CancellationToken cancellationToken) =>
            inner.ReadComparsaLogoAsync(comparsaId, cancellationToken);
    }
}
