using System.Collections.Concurrent;
using PolvorApp.FederationCatalog.Contracts;

namespace PolvorApp.Api.Tests.Infrastructure;

/// <summary>
/// Stands in for the modules that will reference comparsas and weapon models (#5, #9, #10), so the
/// "in use" path of a deletion can be tested now (design D10).
/// </summary>
public sealed class FakeCatalogUsage : ICatalogUsage
{
    public ConcurrentBag<Guid> ComparsasInUse { get; } = [];

    public ConcurrentBag<Guid> WeaponModelsInUse { get; } = [];

    /// <summary>Runs inside the deletion, after the row lock and before the usage answer (design D10).</summary>
    public Func<Guid, CancellationToken, Task>? DuringComparsaCheck { get; set; }

    public async Task<bool> IsComparsaInUseAsync(Guid comparsaId, CancellationToken cancellationToken)
    {
        if (DuringComparsaCheck is { } hook)
        {
            await hook(comparsaId, cancellationToken);
        }

        return ComparsasInUse.Contains(comparsaId);
    }

    public Task<bool> IsWeaponModelInUseAsync(Guid weaponModelId, CancellationToken cancellationToken) =>
        Task.FromResult(WeaponModelsInUse.Contains(weaponModelId));
}
