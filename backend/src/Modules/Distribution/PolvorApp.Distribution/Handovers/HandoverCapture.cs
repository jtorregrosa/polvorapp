using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PolvorApp.Distribution.Contracts;
using PolvorApp.Distribution.Days;
using PolvorApp.Distribution.Documents;
using PolvorApp.Distribution.Persistence;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Http;

namespace PolvorApp.Distribution.Handovers;

/// <summary>
/// The capture package of a powder day (spec: Offline capture package (UC-21); design D2): read like the
/// printed list, so the numbers agree, with the handovers already recorded. Admin-only at the endpoint.
/// The download is audited before it is returned, without names or DNI/NIE; one that cannot be audited
/// is not returned (SEC-05).
/// </summary>
internal sealed partial class HandoverCapture(
    DistributionDbContext db,
    IEditionDirectory editions,
    DistributionListReader lists,
    IAuditLog auditLog,
    TimeProvider time,
    ILogger<HandoverCapture> logger)
{
    public async Task<Results<Ok<CapturePackageResponse>, ProblemHttpResult>> PackageAsync(Guid dayId, CancellationToken cancellationToken)
    {
        var day = await db.Days.AsNoTracking().Include(d => d.Slots).SingleOrDefaultAsync(d => d.Id == dayId, cancellationToken);
        var edition = day is null ? null : await editions.FindAsync(day.EditionId, cancellationToken);
        if (day is null || edition is null)
        {
            return DistributionProblems.From(DistributionOutcome.NotFound);
        }

        if (Refusal(day, edition) is { } refusal)
        {
            return DistributionProblems.From(refusal);
        }

        var data = await lists.ReadAsync(day, edition, cancellationToken);
        var rows = DistributionLists.Rows(data, DistributionTexts.For(CultureInfo.CurrentUICulture)).Rows;
        var handovers = await db.Handovers.AsNoTracking().Where(h => h.DistributionId == day.Id).OrderBy(h => h.RecordedAt).ToListAsync(cancellationToken);
        try
        {
            await auditLog.RecordAsync(
                new AuditRecord(
                    DistributionAuditActions.CapturePackageDownloaded,
                    DistributionDayAdministration.EntityType,
                    day.Id.ToString(),
                    new { editionId = edition.Id, editionYear = edition.Year, rows = rows.Count, handovers = handovers.Count },
                    null),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogAuditFailed(logger, exception, day.Id);
            return ProblemResults.Problem(StatusCodes.Status503ServiceUnavailable, DistributionProblems.AuditUnavailable);
        }

        return TypedResults.Ok(new CapturePackageResponse(
            day.Id,
            edition.Id,
            edition.Year,
            day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            day.Location,
            time.GetUtcNow(),
            [.. rows.Select(Row)],
            [.. handovers.Select(HandoverResponse.From)]));
    }

    /// <summary>Why handovers cannot be captured for the day: only the powder day of the edition in progress.</summary>
    public static DistributionOutcome? Refusal(DistributionDay day, EditionSnapshot edition)
    {
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(edition);
        if (day.Type != DistributionType.Powder)
        {
            return DistributionOutcome.CaptureNotPowder;
        }

        return edition.Status == EditionStatus.InProgress ? null : DistributionOutcome.EditionNotInProgress;
    }

    private static CaptureRowResponse Row(ListRow row) => new(
        row.Number,
        row.Holder.Slot?.ToString("HH:mm", CultureInfo.InvariantCulture),
        row.Holder.ComparsaId,
        row.Holder.ComparsaName,
        row.Holder.EntryId,
        row.Identity.LastName,
        row.Identity.FirstName,
        row.Identity.NationalId,
        row.Entry.PowderKg,
        row.Entry.Flask,
        row.Proxy is { } proxy ? new CapturePersonResponse(proxy.EntryId, proxy.Identity.LastName, proxy.Identity.FirstName, proxy.Identity.NationalId) : null);

    [LoggerMessage(Level = LogLevel.Error, Message = "Capture package of distribution {DistributionId} could not be audited and was not returned")]
    private static partial void LogAuditFailed(ILogger logger, Exception exception, Guid distributionId);
}
