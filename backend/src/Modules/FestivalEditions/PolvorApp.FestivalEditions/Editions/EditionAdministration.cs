using Microsoft.EntityFrameworkCore;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.FestivalEditions.Persistence;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Validation;

namespace PolvorApp.FestivalEditions.Editions;

/// <summary>
/// Edition management by Admins (specs: Festival editions (UC-10), Edition prices, New editions start
/// from the previous one, Edition management by Admins). Every write runs through the write guard in
/// a transaction with a lock timeout, locks the edition row, is audited in the same transaction, and
/// a no-op records nothing (design D3, D9). Status and orders are in <see cref="EditionLifecycle"/>.
/// </summary>
internal sealed class EditionAdministration(
    FestivalEditionsDbContext db,
    ICatalogDirectory catalog,
    IEnumerable<IEditionUsage> usages,
    IAuditTrail trail,
    EditionWriteGuard guard,
    TimeProvider time)
{
    public const string EntityType = "FestivalEdition";

    /// <summary>The orders' foreign key to an edition (add-comparsa-orders, design D2).</summary>
    private const string OrdersEditionForeignKey = "fk_comparsa_orders_edition";

    /// <summary>Creates a draft with the prices and still-rentable models of the latest earlier edition (design D5).</summary>
    public Task<EditionWrite> CreateAsync(int year, DateOnly festivalStartsOn, DateOnly festivalEndsOn, CancellationToken cancellationToken) =>
        guard.RunAsync(nameof(CreateAsync), null, async () =>
        {
            await using var transaction = await db.BeginWriteAsync(cancellationToken);
            if (await db.Editions.AnyAsync(e => e.Year == year, cancellationToken))
            {
                return EditionWrite.Failed(EditionOutcome.YearTaken);
            }

            var previous = await db.Editions.AsNoTracking()
                .Where(e => e.Year < year)
                .OrderByDescending(e => e.Year)
                .FirstOrDefaultAsync(cancellationToken);
            var prices = previous is null ? EditionPrices.None : previous.GetPrices();
            var (copied, dropped) = previous is null ? (new List<Guid>(), new List<Guid>()) : await CopyableModelsAsync(previous.Id, cancellationToken);

            var now = time.GetUtcNow();
            var edition = new FestivalEdition
            {
                Id = Guid.CreateVersion7(now),
                Year = year,
                FestivalStartsOn = festivalStartsOn,
                FestivalEndsOn = festivalEndsOn,
                PowderPerKg = prices.PowderPerKg,
                CapsBox = prices.CapsBox,
                WeaponRental = prices.WeaponRental,
                FlaskRental = prices.FlaskRental,
                CreatedAt = now,
            };
            db.Editions.Add(edition);
            db.EditionWeaponModels.AddRange(copied.Select(id => new EditionWeaponModel { EditionId = edition.Id, WeaponModelId = id }));
            Record("EditionCreated", edition, new
            {
                year,
                festivalStartsOn,
                festivalEndsOn,
                copiedFrom = previous?.Year,
                prices,
                weaponModelIds = copied,
                droppedWeaponModelIds = dropped,
            });

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (EditionProblems.IsUniqueViolation(exception, FestivalEditionsDbContext.YearIndex))
            {
                // A concurrent create of the same year won the race.
                db.ChangeTracker.Clear();
                guard.LostRace(null, FestivalEditionsDbContext.YearIndex);
                return EditionWrite.Failed(EditionOutcome.YearTaken);
            }
            catch (DbUpdateException exception) when (EditionProblems.IsForeignKeyViolation(exception, FestivalEditionsDbContext.WeaponModelForeignKey))
            {
                // A copied model was deleted from the catalogue meanwhile: retryable, the copy will skip it.
                db.ChangeTracker.Clear();
                guard.LostRace(null, FestivalEditionsDbContext.WeaponModelForeignKey);
                return EditionWrite.Failed(EditionOutcome.Busy);
            }

            await transaction.CommitAsync(cancellationToken);
            return EditionWrite.Done(edition);
        });

    /// <summary>
    /// Replaces the dates, order window and prices; allowed in any status (BR-10). Outside a draft
    /// the window dates and prices stay required, since starting the edition needed them.
    /// </summary>
    public Task<EditionWrite> UpdateAsync(Guid id, EditionFields fields, uint version, CancellationToken cancellationToken) =>
        guard.RunAsync(nameof(UpdateAsync), id, async () =>
        {
            await using var transaction = await db.BeginWriteAsync(cancellationToken);
            if (!await db.LockEditionAsync(id, cancellationToken))
            {
                return EditionWrite.Failed(EditionOutcome.NotFound);
            }

            var edition = await db.Editions.SingleAsync(e => e.Id == id, cancellationToken);
            if (edition.Version != version)
            {
                return EditionWrite.Failed(EditionOutcome.Modified);
            }

            var (input, errors) = EditionInput.Read(edition.Year, fields);
            if (input is null)
            {
                return EditionWrite.Invalid(errors);
            }

            if (edition.Status != EditionStatus.Draft && RequiredOutsideDraft(input) is { Count: > 0 } missing)
            {
                return EditionWrite.Invalid(missing);
            }

            var previous = Editable(edition);
            var current = new EditableFields(input.FestivalStartsOn, input.FestivalEndsOn, input.OrdersOpenOn, input.OrdersCloseOn, input.Prices);
            if (previous == current)
            {
                return EditionWrite.Done(edition);
            }

            Apply(edition, input);
            Record("EditionUpdated", edition, Changes(previous, current));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return EditionWrite.Done(edition);
        });

    /// <summary>Deletes a draft with its models and milestones (spec: Edition management by Admins).</summary>
    public Task<EditionWrite> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        guard.RunAsync(nameof(DeleteAsync), id, async () =>
        {
            await using var transaction = await db.BeginWriteAsync(cancellationToken);
            if (!await db.LockEditionAsync(id, cancellationToken))
            {
                return EditionWrite.Failed(EditionOutcome.NotFound);
            }

            var edition = await db.Editions.SingleAsync(e => e.Id == id, cancellationToken);
            if (edition.Status != EditionStatus.Draft)
            {
                return EditionWrite.Failed(EditionOutcome.NotDraft);
            }

            // Asked under the row lock: a preparation reads the edition FOR SHARE, so it either
            // committed before this lock (and is seen) or waits behind it (add-comparsa-orders, D4).
            foreach (var usage in usages)
            {
                if (await usage.IsEditionInUseAsync(id, cancellationToken))
                {
                    return EditionWrite.Failed(EditionOutcome.InUse);
                }
            }

            var modelIds = await db.EditionWeaponModels.Where(m => m.EditionId == id).Select(m => m.WeaponModelId).ToListAsync(cancellationToken);
            var milestoneCount = await db.Milestones.CountAsync(m => m.EditionId == id, cancellationToken);
            db.Editions.Remove(edition);
            Record("EditionDeleted", edition, new
            {
                edition.Year,
                edition.Status,
                edition.FestivalStartsOn,
                edition.FestivalEndsOn,
                edition.OrdersOpenOn,
                edition.OrdersCloseOn,
                prices = edition.GetPrices(),
                weaponModelIds = modelIds,
                milestoneCount,
            });
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (EditionProblems.IsForeignKeyViolation(exception, OrdersEditionForeignKey))
            {
                // A reference the vetoes did not report; the database is the authority.
                db.ChangeTracker.Clear();
                guard.LostRace(id, OrdersEditionForeignKey);
                return EditionWrite.Failed(EditionOutcome.InUse);
            }

            await transaction.CommitAsync(cancellationToken);
            return EditionWrite.Done(edition);
        });

    /// <summary>The previous edition's models that are still active and rentable, and the ones left out.</summary>
    private async Task<(List<Guid> Copied, List<Guid> Dropped)> CopyableModelsAsync(Guid editionId, CancellationToken cancellationToken)
    {
        var ids = await db.EditionWeaponModels.AsNoTracking()
            .Where(m => m.EditionId == editionId)
            .Select(m => m.WeaponModelId)
            .ToListAsync(cancellationToken);
        var models = await catalog.FindWeaponModelsAsync(ids, cancellationToken);
        var copied = models.Where(m => m.Active && m.Rentable).Select(m => m.Id).Order().ToList();
        return (copied, ids.Except(copied).Order().ToList());
    }

    private static Dictionary<string, string> RequiredOutsideDraft(EditionInput input)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        Require(errors, input.OrdersOpenOn is null, "ordersOpenOn");
        Require(errors, input.OrdersCloseOn is null, "ordersCloseOn");
        Require(errors, input.Prices.PowderPerKg is null, "prices.powderPerKg");
        Require(errors, input.Prices.CapsBox is null, "prices.capsBox");
        Require(errors, input.Prices.WeaponRental is null, "prices.weaponRental");
        Require(errors, input.Prices.FlaskRental is null, "prices.flaskRental");
        return errors;
    }

    private static void Require(Dictionary<string, string> errors, bool absent, string field)
    {
        if (absent)
        {
            errors[field] = InputFields.Required;
        }
    }

    private static void Apply(FestivalEdition edition, EditionInput input)
    {
        edition.FestivalStartsOn = input.FestivalStartsOn;
        edition.FestivalEndsOn = input.FestivalEndsOn;
        edition.OrdersOpenOn = input.OrdersOpenOn;
        edition.OrdersCloseOn = input.OrdersCloseOn;
        edition.PowderPerKg = input.Prices.PowderPerKg;
        edition.CapsBox = input.Prices.CapsBox;
        edition.WeaponRental = input.Prices.WeaponRental;
        edition.FlaskRental = input.Prices.FlaskRental;
    }

    private static EditableFields Editable(FestivalEdition edition) =>
        new(edition.FestivalStartsOn, edition.FestivalEndsOn, edition.OrdersOpenOn, edition.OrdersCloseOn, edition.GetPrices());

    /// <summary>The changed fields only, with their previous and new values (spec: Edition changes are audited).</summary>
    private static object Changes(EditableFields previous, EditableFields current)
    {
        var before = new Dictionary<string, object?>(StringComparer.Ordinal);
        var after = new Dictionary<string, object?>(StringComparer.Ordinal);
        Compare("festivalStartsOn", previous.FestivalStartsOn, current.FestivalStartsOn);
        Compare("festivalEndsOn", previous.FestivalEndsOn, current.FestivalEndsOn);
        Compare("ordersOpenOn", previous.OrdersOpenOn, current.OrdersOpenOn);
        Compare("ordersCloseOn", previous.OrdersCloseOn, current.OrdersCloseOn);
        Compare("prices.powderPerKg", previous.Prices.PowderPerKg, current.Prices.PowderPerKg);
        Compare("prices.capsBox", previous.Prices.CapsBox, current.Prices.CapsBox);
        Compare("prices.weaponRental", previous.Prices.WeaponRental, current.Prices.WeaponRental);
        Compare("prices.flaskRental", previous.Prices.FlaskRental, current.Prices.FlaskRental);
        return new { previous = before, current = after };

        void Compare<T>(string field, T was, T now)
        {
            if (!EqualityComparer<T>.Default.Equals(was, now))
            {
                before[field] = was;
                after[field] = now;
            }
        }
    }

    private void Record(string action, FestivalEdition edition, object? data = null) =>
        trail.Record(db, new AuditRecord(action, EntityType, edition.Id.ToString(), data));

    /// <param name="FestivalStartsOn">First festival day.</param>
    /// <param name="FestivalEndsOn">Last festival day.</param>
    /// <param name="OrdersOpenOn">Planned opening of the orders.</param>
    /// <param name="OrdersCloseOn">Planned closing of the orders.</param>
    /// <param name="Prices">The prices.</param>
    private sealed record EditableFields(DateOnly FestivalStartsOn, DateOnly FestivalEndsOn, DateOnly? OrdersOpenOn, DateOnly? OrdersCloseOn, EditionPrices Prices);
}
