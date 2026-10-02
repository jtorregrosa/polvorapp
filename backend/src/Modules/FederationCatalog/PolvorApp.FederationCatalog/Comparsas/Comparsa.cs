using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Logos;

namespace PolvorApp.FederationCatalog.Comparsas;

/// <summary>A festival troupe (spec: Comparsas). Its name is unique case-insensitively.</summary>
internal sealed class Comparsa
{
    public const int NameMaxLength = 100;

    public required Guid Id { get; init; }

    public required string Name { get; set; }

    public required Side Side { get; set; }

    public bool Active { get; set; } = true;

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>The comparsa's logo, if an Admin uploaded one (spec: Comparsa logos).</summary>
    public ComparsaLogo? Logo { get; set; }
}
