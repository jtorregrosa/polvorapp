using PolvorApp.FederationCatalog.Contracts;

namespace PolvorApp.Api.Tests.Infrastructure;

/// <summary>Settings that cannot be read, so a test can check nothing is printed with a fallback name.</summary>
internal sealed class UnreadableFederationSettings : IFederationSettings
{
    public Task<FederationSettingsSnapshot> GetAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Synthetic settings failure.");
}
