using Microsoft.EntityFrameworkCore;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FederationCatalog.Logos;
using PolvorApp.FederationCatalog.Persistence;

namespace PolvorApp.FederationCatalog.Settings;

/// <summary>
/// <see cref="IFederationSettings"/> (design D3): one untracked read of the single row, kept for the
/// scope, so a request or a notification run sees one consistent set of settings.
/// </summary>
internal sealed class FederationSettingsReader(FederationCatalogDbContext db) : IFederationSettings
{
    private FederationSettingsSnapshot? _snapshot;

    public async Task<FederationSettingsSnapshot> GetAsync(CancellationToken cancellationToken) =>
        _snapshot ??= Snapshot(await db.FederationSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("The Federation settings row is missing; run the migrations."));

    public static FederationSettingsSnapshot Snapshot(FederationSettings settings) => new(
        settings.OfficialNameEs,
        settings.OfficialNameCa,
        settings.ShortName,
        settings.ContactEmail,
        settings.Website,
        settings.SenderName,
        settings.ReplyTo,
        settings.CloseReminderLeadDays,
        settings.MilestoneLeadDays);
}
