using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.FestivalEditions.Editions;
using PolvorApp.FestivalEditions.Persistence;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Text;
using PolvorApp.SharedKernel.Time;

namespace PolvorApp.FestivalEditions.Endpoints;

/// <summary>
/// Reads editions for the API (design D4, D6): FiringChiefs never see a draft, which is answered as
/// if it did not exist (spec: Edition visibility (BR-12)); editions are not scoped to a comparsa.
/// </summary>
internal sealed partial class EditionViews(
    FestivalEditionsDbContext db, ICatalogDirectory catalog, ICurrentUser currentUser, TimeProvider time, ILogger<EditionViews> logger)
{
    /// <summary>The editions the caller can see, newest first.</summary>
    public async Task<List<EditionRowResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var editions = await Visible().OrderByDescending(e => e.Year).ToListAsync(cancellationToken);
        return editions
            .Select(e => new EditionRowResponse(
                e.Id, e.Year, e.FestivalStartsOn, e.FestivalEndsOn, e.Status, e.OrdersOpen, e.Status == EditionStatus.InProgress))
            .ToList();
    }

    /// <summary>The edition, or null when it does not exist or the caller cannot see it.</summary>
    public async Task<EditionResponse?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await Visible().SingleOrDefaultAsync(e => e.Id == id, cancellationToken) is { } edition
            ? await ToResponseAsync(edition, cancellationToken)
            : null;

    /// <summary>The edition in progress, or null; every signed-in user may read it.</summary>
    public async Task<EditionResponse?> CurrentAsync(CancellationToken cancellationToken) =>
        await db.Editions.AsNoTracking().SingleOrDefaultAsync(e => e.Status == EditionStatus.InProgress, cancellationToken) is { } edition
            ? await ToResponseAsync(edition, cancellationToken)
            : null;

    /// <summary>The whole edition: its models with their current catalogue state and its milestones.</summary>
    public async Task<EditionResponse> ToResponseAsync(FestivalEdition edition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(edition);
        var modelIds = await db.EditionWeaponModels.AsNoTracking()
            .Where(m => m.EditionId == edition.Id)
            .Select(m => m.WeaponModelId)
            .ToListAsync(cancellationToken);
        var models = await catalog.FindWeaponModelsAsync(modelIds, cancellationToken);
        if (models.Count != modelIds.Count)
        {
            // The foreign key prevents this; if it ever drifts, the set shown would silently shrink.
            LogMissingModels(logger, edition.Id, modelIds.Except(models.Select(m => m.Id)).ToList());
        }

        var milestones = await db.Milestones.AsNoTracking().Where(m => m.EditionId == edition.Id).ToListAsync(cancellationToken);

        return new EditionResponse(
            edition.Id,
            edition.Year,
            edition.Status,
            edition.OrdersOpen,
            edition.Version,
            edition.FestivalStartsOn,
            edition.FestivalEndsOn,
            edition.OrdersOpenOn,
            edition.OrdersCloseOn,
            new EditionPricesResponse(Money(edition.PowderPerKg), Money(edition.CapsBox), Money(edition.WeaponRental), Money(edition.FlaskRental)),
            models
                .OrderBy(m => m.Label, SpanishOrder.Names)
                .Select(m => new EditionWeaponModelResponse(m.Id, m.Label, m.Kind, IsOffered(m)))
                .ToList(),
            milestones
                .OrderBy(m => m.Date)
                .ThenBy(m => m.Title, SpanishOrder.Names)
                .Select(m => new CalendarMilestoneResponse(m.Id, m.Date, m.Title))
                .ToList(),
            NextWindow(edition, FederationCalendar.Today(time)));
    }

    /// <summary>A model in an edition's set is offered for rental only while it is active and rentable (BR-07).</summary>
    public static bool IsOffered(WeaponModelSummary model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Active && model.Rentable;
    }

    /// <summary>The first order window date on or after <paramref name="today"/>, or null.</summary>
    public static EditionNextWindow? NextWindow(FestivalEdition edition, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(edition);
        if (edition.OrdersOpenOn is { } opens && opens >= today)
        {
            return new EditionNextWindow(EditionWindowKind.Opens, opens);
        }

        return edition.OrdersCloseOn is { } closes && closes >= today ? new EditionNextWindow(EditionWindowKind.Closes, closes) : null;
    }

    /// <summary>
    /// Always two decimals in responses, e.g. <c>55.00</c> (design D2): adding <c>0.00m</c> raises a
    /// decimal's scale to at least 2, and System.Text.Json writes a decimal with its scale.
    /// </summary>
    private static decimal? Money(decimal? amount) => amount is { } value ? decimal.Round(value, 2) + 0.00m : null;

    [LoggerMessage(Level = LogLevel.Error, Message = "Edition {EditionId} offers weapon models the catalogue no longer has: {WeaponModelIds}")]
    private static partial void LogMissingModels(ILogger logger, Guid editionId, IReadOnlyList<Guid> weaponModelIds);

    private IQueryable<FestivalEdition> Visible()
    {
        var editions = db.Editions.AsNoTracking();
        return currentUser.IsAdmin ? editions : editions.Where(e => e.Status != EditionStatus.Draft);
    }
}
