using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using PolvorApp.Distribution.Contracts;
using PolvorApp.Distribution.Persistence;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.Distribution.Days;

/// <summary>A day as planned or edited: its date and trimmed location.</summary>
internal sealed record DayInput(DateOnly Date, string Location);

/// <summary>One slot as saved: a comparsa and its start time (spec: Distribution slots (UC-18)).</summary>
internal sealed record SlotInput(Guid ComparsaId, TimeOnly StartsAt);

/// <summary>
/// Plans, edits and deletes the distribution days of the edition in progress and saves their slots
/// (spec: Distribution days, Distribution slots; design D4, D8). Admin-only at the endpoint. Every write
/// reads the edition <c>FOR SHARE</c> first, then locks the day, and records its audit entry in the same
/// transaction.
/// </summary>
internal sealed class DistributionDayAdministration(
    DistributionDbContext db,
    IEditionDirectory editions,
    ICatalogDirectory catalog,
    IAuditTrail trail,
    DistributionWriteGuard guard,
    TimeProvider time)
{
    public const string EntityType = "Distribution";

    public Task<DistributionResult<DistributionDay>> PlanAsync(Guid editionId, DistributionType type, DayInput input, CancellationToken cancellationToken) =>
        guard.RunAsync(nameof(PlanAsync), editionId, async () =>
        {
            await using var transaction = await db.BeginWriteAsync(cancellationToken);
            if (await CheckEditionAsync(editionId, input.Date, cancellationToken) is { } refusal)
            {
                return refusal;
            }

            if (await db.Days.AnyAsync(d => d.EditionId == editionId && d.Type == type, cancellationToken))
            {
                return DistributionResult<DistributionDay>.Failed(DistributionOutcome.AlreadyPlanned);
            }

            var now = time.GetUtcNow();
            var day = new DistributionDay
            {
                Id = Guid.CreateVersion7(now),
                EditionId = editionId,
                Type = type,
                Date = input.Date,
                Location = input.Location,
                UpdatedAt = now,
            };
            db.Days.Add(day);
            Record(DistributionAuditActions.DistributionPlanned, day, new { editionId, type = EnumCodes.ToCode(type), date = Iso(input.Date), input.Location });
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (DistributionProblems.Violates(exception, PostgresErrorCodes.UniqueViolation, DistributionDbContext.DayTypeIndex))
            {
                // Another request planned the same type first (spec: also under concurrent requests).
                db.ChangeTracker.Clear();
                guard.LostRace(editionId, DistributionDbContext.DayTypeIndex);
                return DistributionResult<DistributionDay>.Failed(DistributionOutcome.AlreadyPlanned);
            }

            await transaction.CommitAsync(cancellationToken);
            return DistributionResult<DistributionDay>.Done(day);
        });

    /// <summary>Changes the date or location; an unchanged save records nothing.</summary>
    public Task<DistributionResult<DistributionDay>> EditAsync(Guid dayId, DayInput input, uint version, CancellationToken cancellationToken) =>
        guard.RunAsync(nameof(EditAsync), dayId, async () =>
        {
            await using var transaction = await db.BeginWriteAsync(cancellationToken);
            var (refusal, day) = await LockAsync(dayId, version, input.Date, cancellationToken);
            if (refusal is not null)
            {
                return refusal;
            }

            var changed = new Dictionary<string, (string Previous, string Current)>(StringComparer.Ordinal);
            if (day!.Date != input.Date)
            {
                changed["date"] = (Iso(day.Date), Iso(input.Date));
            }

            if (!string.Equals(day.Location, input.Location, StringComparison.Ordinal))
            {
                changed["location"] = (day.Location, input.Location);
            }

            if (changed.Count == 0)
            {
                return DistributionResult<DistributionDay>.Done(day);
            }

            (day.Date, day.Location, day.UpdatedAt) = (input.Date, input.Location, time.GetUtcNow());
            Record(DistributionAuditActions.DistributionEdited, day, new
            {
                day.EditionId,
                previous = changed.ToDictionary(c => c.Key, c => c.Value.Previous, StringComparer.Ordinal),
                current = changed.ToDictionary(c => c.Key, c => c.Value.Current, StringComparer.Ordinal),
            });
            return await SaveAsync(day, transaction, cancellationToken);
        });

    /// <summary>Deletes the day with its slots; pickup proxies do not belong to a day and are kept.</summary>
    public Task<DistributionResult<DistributionDay>> DeleteAsync(Guid dayId, uint version, CancellationToken cancellationToken) =>
        guard.RunAsync(nameof(DeleteAsync), dayId, async () =>
        {
            await using var transaction = await db.BeginWriteAsync(cancellationToken);
            var (refusal, day) = await LockAsync(dayId, version, date: null, cancellationToken);
            if (refusal is not null)
            {
                return refusal;
            }

            // The handovers are the record of the day (UC-21). A handover saved after this check still
            // stops the deletion through the day's RESTRICT key.
            if (await db.Handovers.AnyAsync(h => h.DistributionId == dayId, cancellationToken))
            {
                return DistributionResult<DistributionDay>.Failed(DistributionOutcome.HasHandovers);
            }

            db.Days.Remove(day!);
            Record(DistributionAuditActions.DistributionDeleted, day!, new
            {
                day!.EditionId,
                type = EnumCodes.ToCode(day.Type),
                date = Iso(day.Date),
                day.Location,
                slots = day.Slots.Count,
            });
            return await SaveAsync(day, transaction, cancellationToken);
        });

    /// <summary>
    /// Replaces the day's slots with <paramref name="slots"/> (spec: saved as one set). Unknown comparsas
    /// are named by index; an unchanged set records nothing. The day's version changes with its slots.
    /// </summary>
    public Task<DistributionResult<DistributionDay>> SaveSlotsAsync(Guid dayId, uint version, IReadOnlyList<SlotInput> slots, CancellationToken cancellationToken) =>
        guard.RunAsync(nameof(SaveSlotsAsync), dayId, async () =>
        {
            await using var transaction = await db.BeginWriteAsync(cancellationToken);
            var (refusal, day) = await LockAsync(dayId, version, date: null, cancellationToken);
            if (refusal is not null)
            {
                return refusal;
            }

            var known = (await catalog.FindComparsasAsync([.. slots.Select(s => s.ComparsaId).Distinct()], cancellationToken)).Select(c => c.Id).ToHashSet();
            var errors = UnknownComparsas(slots, known);
            if (errors.Count > 0)
            {
                return DistributionResult<DistributionDay>.Invalid(errors);
            }

            var diff = SlotDiff.Compute(day!.Slots.ToDictionary(s => s.ComparsaId, s => s.StartsAt), slots);
            if (diff.IsEmpty)
            {
                return DistributionResult<DistributionDay>.Done(day);
            }

            var removed = diff.Removed.ToHashSet();
            db.Slots.RemoveRange(day.Slots.Where(s => removed.Contains(s.ComparsaId)));
            foreach (var moved in diff.Moved)
            {
                day.Slots.Single(s => s.ComparsaId == moved.ComparsaId).StartsAt = moved.StartsAt;
            }

            foreach (var added in diff.Added)
            {
                day.Slots.Add(new DistributionSlot { DistributionId = day.Id, ComparsaId = added.ComparsaId, StartsAt = added.StartsAt });
            }

            // Touching the day changes its version (xmin), so a concurrent save of the old set is refused.
            // Marked modified even when the clock has not moved, or EF would skip the update.
            day.UpdatedAt = time.GetUtcNow();
            db.Entry(day).Property(d => d.UpdatedAt).IsModified = true;
            Record(DistributionAuditActions.DistributionSlotsChanged, day, new { day.EditionId, type = EnumCodes.ToCode(day.Type), changes = diff.Changes });
            return await SaveAsync(day, transaction, cancellationToken, slots);
        });

    private static Dictionary<string, string> UnknownComparsas(IReadOnlyList<SlotInput> slots, HashSet<Guid> known)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < slots.Count; i++)
        {
            if (!known.Contains(slots[i].ComparsaId))
            {
                errors[$"slots[{i}].comparsaId"] = DistributionFieldErrors.Unknown;
            }
        }

        return errors;
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The edition rule (in progress) and, when a date is given, the date rule (spec: Distribution days).</summary>
    private async Task<DistributionResult<DistributionDay>?> CheckEditionAsync(Guid editionId, DateOnly? date, CancellationToken cancellationToken)
    {
        var edition = await db.ReadEditionForWriteAsync(editions, editionId, cancellationToken);
        if (edition is null)
        {
            return DistributionResult<DistributionDay>.Failed(DistributionOutcome.NotFound);
        }

        if (edition.Status != EditionStatus.InProgress)
        {
            return DistributionResult<DistributionDay>.Failed(DistributionOutcome.EditionNotInProgress);
        }

        return date is { } day && (day.Year != edition.Year || day > edition.FestivalEndsOn)
            ? DistributionResult<DistributionDay>.Invalid(new Dictionary<string, string>(StringComparer.Ordinal) { ["date"] = DistributionFieldErrors.OutOfEdition })
            : null;
    }

    /// <summary>In lock order: the edition (rule and optional date), then the day, then its version.</summary>
    private async Task<(DistributionResult<DistributionDay>? Refusal, DistributionDay? Day)> LockAsync(
        Guid dayId, uint version, DateOnly? date, CancellationToken cancellationToken)
    {
        var editionId = await db.Days.AsNoTracking().Where(d => d.Id == dayId).Select(d => (Guid?)d.EditionId).SingleOrDefaultAsync(cancellationToken);
        if (editionId is not { } edition)
        {
            return (DistributionResult<DistributionDay>.Failed(DistributionOutcome.NotFound), null);
        }

        if (await CheckEditionAsync(edition, date, cancellationToken) is { } refusal)
        {
            return (refusal, null);
        }

        if (!await db.LockDayAsync(dayId, cancellationToken))
        {
            return (DistributionResult<DistributionDay>.Failed(DistributionOutcome.NotFound), null);
        }

        var day = await db.Days.Include(d => d.Slots).SingleAsync(d => d.Id == dayId, cancellationToken);
        return day.Version == version
            ? (null, day)
            : (DistributionResult<DistributionDay>.Failed(DistributionOutcome.Modified), null);
    }

    /// <param name="day">The day being saved.</param>
    /// <param name="transaction">The write's transaction, committed on success.</param>
    /// <param name="cancellationToken">The request's token.</param>
    /// <param name="slots">For a slot save, the requested set, to name a comparsa deleted meanwhile.</param>
    private async Task<DistributionResult<DistributionDay>> SaveAsync(
        DistributionDay day, IDbContextTransaction transaction, CancellationToken cancellationToken, IReadOnlyList<SlotInput>? slots = null)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            guard.LostRace(day.Id, "concurrency");
            return DistributionResult<DistributionDay>.Failed(DistributionOutcome.Modified);
        }
        catch (DbUpdateException exception) when (DistributionProblems.Violates(exception, PostgresErrorCodes.RestrictViolation, DistributionDbContext.HandoverDayForeignKey))
        {
            // A handover was recorded after the check: the day keeps it.
            db.ChangeTracker.Clear();
            guard.LostRace(day.Id, DistributionDbContext.HandoverDayForeignKey);
            return DistributionResult<DistributionDay>.Failed(DistributionOutcome.HasHandovers);
        }
        catch (DbUpdateException exception) when (slots is not null
            && DistributionProblems.Violates(exception, PostgresErrorCodes.ForeignKeyViolation, DistributionDbContext.SlotComparsaForeignKey))
        {
            // A comparsa was deleted after it was checked: its slot is now unknown (the database names no row).
            db.ChangeTracker.Clear();
            guard.LostRace(day.Id, DistributionDbContext.SlotComparsaForeignKey);
            var known = (await catalog.FindComparsasAsync([.. slots.Select(s => s.ComparsaId)], cancellationToken)).Select(c => c.Id).ToHashSet();
            var unknown = UnknownComparsas(slots, known);
            if (unknown.Count == 0)
            {
                // The key failed but every comparsa exists: not a lost race this code understands.
                throw new InvalidOperationException($"A slot of distribution {day.Id} broke its comparsa key while every comparsa exists.");
            }

            return DistributionResult<DistributionDay>.Invalid(unknown);
        }

        await transaction.CommitAsync(cancellationToken);
        return DistributionResult<DistributionDay>.Done(day);
    }

    private void Record(string action, DistributionDay day, object data) =>
        trail.Record(db, new AuditRecord(action, EntityType, day.Id.ToString(), data));
}
