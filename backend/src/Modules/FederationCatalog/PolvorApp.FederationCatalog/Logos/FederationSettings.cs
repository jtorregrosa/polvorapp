namespace PolvorApp.FederationCatalog.Logos;

/// <summary>
/// The Federation's own settings (add-distribution-planning, design D11): a single row created by the
/// migration, holding the logo printed in documents. The logo is uploaded at run time and never
/// committed: the repository is public and the crest is not PolvorApp's to license.
/// </summary>
internal sealed class FederationSettings
{
    /// <summary>The only row's id; a check constraint refuses any other.</summary>
    public const int SingletonId = 1;

    public required int Id { get; init; }

    /// <summary>The logo, stored as the comparsa logos are (same rules, prefix and sweep).</summary>
    public ComparsaLogo? Logo { get; set; }

    public required DateTimeOffset UpdatedAt { get; set; }
}
