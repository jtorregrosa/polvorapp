using PolvorApp.SharedKernel.Auditing;

namespace PolvorApp.Badges;

/// <summary>The audit action codes this module records (add-audit-privacy, design D2; add-badges, design D7).</summary>
internal static class BadgesAuditActions
{
    /// <summary>A badge sheet was returned; ids, counts and the language only, never names or images.</summary>
    public const string BadgesDownloaded = "BadgesDownloaded";

    public const string EntityType = "Badges";

    public static readonly IReadOnlyList<AuditActionDefinition> All =
    [
        new(BadgesDownloaded, EntityType),
    ];
}
