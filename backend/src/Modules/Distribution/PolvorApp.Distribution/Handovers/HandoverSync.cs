using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.Distribution.Days;
using PolvorApp.Distribution.Documents;
using PolvorApp.Distribution.Persistence;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.Distribution.Handovers;

/// <summary>A handover as a device captured it; read and shaped by the endpoint (design D2).</summary>
internal sealed record HandoverInput(
    Guid Id,
    Guid HolderEntryId,
    int DistributionNumber,
    HandoverCollector CollectedBy,
    Guid? CollectorEntryId,
    string? RentalFlaskNumber,
    string? Traceability1,
    string? Traceability2,
    DateTimeOffset CollectedAt)
{
    public override string ToString() => nameof(HandoverInput);
}

/// <summary>One handover of a batch by its id: its input, or none when its fields could not be read (refused as invalid).</summary>
internal sealed record SyncItem(Guid Id, HandoverInput? Input);

/// <summary>How one handover of a sync ended (spec: Handover sync and conflicts).</summary>
[JsonConverter(typeof(CodeEnumConverter<SyncOutcome>))]
internal enum SyncOutcome
{
    [JsonStringEnumMemberName("RECORDED")]
    Recorded,

    /// <summary>The same id with the same data: a resend, recorded once.</summary>
    [JsonStringEnumMemberName("ALREADY_RECORDED")]
    AlreadyRecorded,

    [JsonStringEnumMemberName("REFUSED")]
    Refused,
}

/// <summary>One handover's outcome: the stored handover, or the reason and, for another device's, the one already recorded.</summary>
internal sealed record SyncResult(Guid Id, SyncOutcome Outcome, string? Code, HandoverResponse? Handover, HandoverResponse? Existing);

/// <summary>
/// Records the handovers a device captured (spec: Powder handovers, Handover sync and conflicts; design
/// D2, D3). The powder list is read once per batch; then each handover is checked and stored in its own
/// transaction with its audit entry, so a refusal never blocks the others. The database's keys decide
/// races between devices and between resends. Admin-only at the endpoint.
/// </summary>
/// <remarks>
/// A failed save inside the item's transaction rolls back to EF Core's automatic savepoint, so the
/// transaction stays usable for the lookups that explain a lost race.
/// </remarks>
internal sealed class HandoverSync(
    DistributionDbContext db,
    IEditionDirectory editions,
    DistributionListReader lists,
    IAuditTrail trail,
    DistributionWriteGuard guard,
    TimeProvider time)
{
    public const string EntityType = "Handover";

    /// <summary>The batch of one request (spec: at most 100).</summary>
    public const int MaxBatch = 100;

    /// <summary>The day's results, or the day's refusal (not found, not the powder day).</summary>
    public async Task<DistributionResult<IReadOnlyList<SyncResult>>> SyncAsync(Guid dayId, IReadOnlyList<SyncItem> items, CancellationToken cancellationToken)
    {
        var day = await db.Days.AsNoTracking().Include(d => d.Slots).SingleOrDefaultAsync(d => d.Id == dayId, cancellationToken);
        var edition = day is null ? null : await editions.FindAsync(day.EditionId, cancellationToken);
        if (day is null || edition is null)
        {
            return DistributionResult<IReadOnlyList<SyncResult>>.Failed(DistributionOutcome.NotFound);
        }

        var refusal = HandoverCapture.Refusal(day, edition);
        if (refusal == DistributionOutcome.CaptureNotPowder)
        {
            return DistributionResult<IReadOnlyList<SyncResult>>.Failed(DistributionOutcome.CaptureNotPowder);
        }

        // A closed edition takes nothing: each handover says so, and the device keeps it as a conflict.
        var rows = refusal is null
            ? DistributionLists.Rows(await lists.ReadAsync(day, edition, cancellationToken), DistributionTexts.Spanish).Rows.ToDictionary(r => r.Holder.EntryId)
            : null;
        var results = new List<SyncResult>(items.Count);
        foreach (var item in items)
        {
            var result = item.Input is not { } input ? Refused(item.Id, DistributionProblems.HandoverInvalid)
                : rows is null ? Refused(item.Id, DistributionProblems.EditionNotInProgress)
                : await RecordAsync(day, Normalised(input), rows, cancellationToken);
            if (result.Outcome == SyncOutcome.Refused)
            {
                guard.RefusedHandover(item.Id, result.Code!);
            }

            results.Add(result);
        }

        return DistributionResult<IReadOnlyList<SyncResult>>.Done(results);
    }

    private async Task<SyncResult> RecordAsync(DistributionDay day, HandoverInput input, Dictionary<Guid, ListRow> rows, CancellationToken cancellationToken)
    {
        try
        {
            return await StoreAsync(day, input, rows, cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // A value the database refuses for a reason this code does not explain (e.g. a character a
            // text column cannot hold): this handover is refused, the rest of the batch goes on.
            db.ChangeTracker.Clear();
            guard.Failed(input.Id, exception);
            return Refused(input.Id, DistributionProblems.HandoverInvalid);
        }
    }

    private async Task<SyncResult> StoreAsync(DistributionDay day, HandoverInput input, Dictionary<Guid, ListRow> rows, CancellationToken cancellationToken)
    {
        var result = await guard.RunAsync(nameof(SyncAsync), input.Id, async () =>
        {
            await using var transaction = await db.BeginWriteAsync(cancellationToken);
            if (await ResendAsync(day, input, cancellationToken) is { } resend)
            {
                return Done(resend);
            }

            var edition = await db.ReadEditionForWriteAsync(editions, day.EditionId, cancellationToken);
            if (edition?.Status != EditionStatus.InProgress)
            {
                return Done(Refused(input.Id, DistributionProblems.EditionNotInProgress));
            }

            var (refusal, handover) = Check(day, input, rows);
            if (handover is null)
            {
                return Done(Refused(input.Id, refusal ?? DistributionProblems.HandoverInvalid));
            }

            db.Handovers.Add(handover);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (LostRace(exception) is { } constraint)
            {
                db.ChangeTracker.Clear();
                guard.LostRace(input.Id, constraint);
                return Done(await ExplainAsync(day, input, constraint, cancellationToken));
            }

            // Saved apart, in the same transaction, so its failure is told from a refused value: a
            // handover that cannot be audited is not recorded and stays pending (SEC-05).
            trail.Record(db, new AuditRecord(DistributionAuditActions.HandoverRecorded, EntityType, handover.Id.ToString(), AuditData(handover)));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception)
            {
                db.ChangeTracker.Clear();
                guard.Failed(input.Id, exception);
                return Done(Refused(input.Id, DistributionProblems.Busy));
            }

            await transaction.CommitAsync(cancellationToken);
            return Done(new SyncResult(input.Id, SyncOutcome.Recorded, null, HandoverResponse.From(handover), null));
        });

        // A lock timeout or deadlock: the device keeps the handover pending and sends it again.
        return result is { Outcome: DistributionOutcome.Done, Value: { } done } ? done : Refused(input.Id, DistributionProblems.Busy);
    }

    /// <summary>A handover already stored under this id: a resend when the data is the same, a changed one otherwise.</summary>
    private async Task<SyncResult?> ResendAsync(DistributionDay day, HandoverInput input, CancellationToken cancellationToken) =>
        await db.Handovers.AsNoTracking().SingleOrDefaultAsync(h => h.Id == input.Id, cancellationToken) is { } stored
            ? Same(stored, day.Id, input)
                ? new SyncResult(input.Id, SyncOutcome.AlreadyRecorded, null, HandoverResponse.From(stored), null)
                : Refused(input.Id, DistributionProblems.HandoverChanged)
            : null;

    /// <summary>Why a save lost a race: the same id sent twice at once, another device's handover or flask number, or a row removed meanwhile.</summary>
    private async Task<SyncResult> ExplainAsync(DistributionDay day, HandoverInput input, string constraint, CancellationToken cancellationToken)
    {
        switch (constraint)
        {
            case DistributionDbContext.HandoverKey:
                return await ResendAsync(day, input, cancellationToken)
                    ?? throw new InvalidOperationException($"Handover {input.Id} broke its key but cannot be read back.");
            case DistributionDbContext.HandoverHolderIndex:
                // Undone again before it could be read: the device sends this one again later.
                return await db.Handovers.AsNoTracking()
                    .SingleOrDefaultAsync(h => h.DistributionId == day.Id && h.HolderEntryId == input.HolderEntryId, cancellationToken) is { } existing
                    ? new SyncResult(input.Id, SyncOutcome.Refused, DistributionProblems.AlreadyHandedOver, null, HandoverResponse.From(existing))
                    : Refused(input.Id, DistributionProblems.Busy);
            case DistributionDbContext.HandoverFlaskIndex:
                return Refused(input.Id, DistributionProblems.FlaskNumberTaken);
            case DistributionDbContext.HandoverDayForeignKey:
                return Refused(input.Id, DistributionProblems.NotFound);
            case DistributionDbContext.HandoverHolderEntryForeignKey:
                return Refused(input.Id, DistributionProblems.NotInList);
            default:
                return Refused(input.Id, DistributionProblems.ProxyNotValid);
        }
    }

    /// <summary>The blocking rules of a handover against the day's list (spec: Powder handovers); the handover to store when it passes.</summary>
    private (string? Refusal, Handover? Handover) Check(DistributionDay day, HandoverInput input, Dictionary<Guid, ListRow> rows)
    {
        if (input.RentalFlaskNumber?.Length > Handover.FlaskNumberMaxLength || input.Traceability1?.Length > Handover.TraceabilityMaxLength
            || input.Traceability2?.Length > Handover.TraceabilityMaxLength || input.DistributionNumber < 1
            || (input.CollectedBy == HandoverCollector.Holder && input.CollectorEntryId is not null))
        {
            return (DistributionProblems.HandoverInvalid, null);
        }

        if (!rows.TryGetValue(input.HolderEntryId, out var row))
        {
            return (DistributionProblems.NotInList, null);
        }

        if (input.CollectedBy == HandoverCollector.Proxy && (input.CollectorEntryId is not { } collector || row.Proxy?.EntryId != collector))
        {
            return (DistributionProblems.ProxyNotValid, null);
        }

        var rented = row.Entry.Flask is FlaskOption.Rental1Kg or FlaskOption.Rental2Kg;
        if (rented != input.RentalFlaskNumber is not null)
        {
            return (rented ? DistributionProblems.FlaskNumberRequired : DistributionProblems.FlaskNumberNotRented, null);
        }

        return (null, new Handover
        {
            Id = input.Id,
            DistributionId = day.Id,
            HolderEntryId = input.HolderEntryId,
            DistributionNumber = input.DistributionNumber,
            CollectedBy = input.CollectedBy,
            CollectorEntryId = input.CollectorEntryId,
            PowderKg = (short)row.Entry.PowderKg,
            RentalFlaskNumber = input.RentalFlaskNumber,
            Traceability1 = input.Traceability1,
            Traceability2 = input.Traceability2,
            CollectedAt = input.CollectedAt,
            RecordedAt = time.GetUtcNow(),
        });
    }

    /// <summary>
    /// Texts trimmed (blank is none) and the device's time in UTC at the database's microsecond precision,
    /// so a resend of the same instant in another offset reads as the same handover.
    /// </summary>
    private static HandoverInput Normalised(HandoverInput input)
    {
        var utc = input.CollectedAt.ToUniversalTime();
        return input with
        {
            RentalFlaskNumber = Trimmed(input.RentalFlaskNumber),
            Traceability1 = Trimmed(input.Traceability1),
            Traceability2 = Trimmed(input.Traceability2),
            CollectedAt = utc.AddTicks(-(utc.Ticks % (TimeSpan.TicksPerMillisecond / 1000))),
        };
    }

    /// <summary>A resend: the same day, holder and captured data (the server's kilograms and times aside).</summary>
    private static bool Same(Handover stored, Guid dayId, HandoverInput input) =>
        stored.DistributionId == dayId
        && stored.HolderEntryId == input.HolderEntryId
        && stored.DistributionNumber == input.DistributionNumber
        && stored.CollectedBy == input.CollectedBy
        && stored.CollectorEntryId == input.CollectorEntryId
        && string.Equals(stored.RentalFlaskNumber, input.RentalFlaskNumber, StringComparison.Ordinal)
        && string.Equals(stored.Traceability1, input.Traceability1, StringComparison.Ordinal)
        && string.Equals(stored.Traceability2, input.Traceability2, StringComparison.Ordinal)
        && stored.CollectedAt == input.CollectedAt;

    /// <summary>The key a save lost a race on, or null when the failure is not one this code explains.</summary>
    private static string? LostRace(DbUpdateException exception) =>
        new[]
        {
            (PostgresErrorCodes.UniqueViolation, DistributionDbContext.HandoverKey),
            (PostgresErrorCodes.UniqueViolation, DistributionDbContext.HandoverHolderIndex),
            (PostgresErrorCodes.UniqueViolation, DistributionDbContext.HandoverFlaskIndex),
            (PostgresErrorCodes.ForeignKeyViolation, DistributionDbContext.HandoverDayForeignKey),
            (PostgresErrorCodes.ForeignKeyViolation, DistributionDbContext.HandoverHolderEntryForeignKey),
            (PostgresErrorCodes.ForeignKeyViolation, DistributionDbContext.HandoverCollectorEntryForeignKey),
        }
        .Where(rule => DistributionProblems.Violates(exception, rule.Item1, rule.Item2))
        .Select(rule => rule.Item2)
        .FirstOrDefault();

    /// <summary>The audit data: ids, the flask number and the role; never a name or a DNI/NIE (SEC-05).</summary>
    internal static object AuditData(Handover handover) => new
    {
        distributionId = handover.DistributionId,
        entryId = handover.HolderEntryId,
        handover.RentalFlaskNumber,
        byProxy = handover.CollectedBy == HandoverCollector.Proxy,
        handover.DistributionNumber,
        powderKg = handover.PowderKg,
        collectedAt = handover.CollectedAt.ToString("O", CultureInfo.InvariantCulture),
    };

    private static string? Trimmed(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static SyncResult Refused(Guid id, string code) => new(id, SyncOutcome.Refused, code, null, null);

    private static DistributionResult<SyncResult> Done(SyncResult result) => DistributionResult<SyncResult>.Done(result);
}
