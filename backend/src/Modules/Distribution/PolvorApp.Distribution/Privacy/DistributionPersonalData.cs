using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using PolvorApp.AuditPrivacy.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.Distribution.Handovers;
using PolvorApp.Distribution.Persistence;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.SharedKernel.Codes;
using PolvorApp.SharedKernel.Persistence;

namespace PolvorApp.Distribution.Privacy;

/// <summary>
/// The distribution's part of a GDPR request about a person (UC-26; add-audit-privacy, design D5): the
/// pickup authorisations in which one of their entries is the holder or the proxy. The other person
/// appears only by their role. The erasure removes those authorisations: an erased entry can be
/// neither (spec: Erased entries in distribution). The powder handovers (UC-21) hold no identity of
/// their own, so the erasure keeps them as history of the anonymised entries
/// (add-offline-distribution-capture D8).
/// </summary>
internal sealed class DistributionPersonalData(DistributionDbContext db, IEditionEntries entries, IEditionDirectory editions)
    : IPersonalDataParticipant
{
    public const string ProxiesSheet = "pickupProxies";
    public const string ProxiesCount = PersonalDataCounts.PickupProxies;
    public const string HandoversSheet = "handovers";

    public int Order => PersonalDataParticipantOrder.Distribution;

    public async Task<PersonalDataSummary> DescribeAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        if (subject is not PersonalDataSubject.Person(var nationalId))
        {
            return PersonalDataSummary.Empty;
        }

        Guid[] ids = [.. await entries.ListEntryIdsOfPersonAsync(nationalId, cancellationToken)];
        var count = await db.Proxies.CountAsync(p => ids.Contains(p.HolderEntryId) || ids.Contains(p.ProxyEntryId), cancellationToken);
        return count == 0 ? PersonalDataSummary.Empty : new PersonalDataSummary { Counts = new Dictionary<string, int> { [ProxiesCount] = count } };
    }

    public async Task<PersonalDataExportPart> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        if (subject is not PersonalDataSubject.Person(var nationalId))
        {
            return PersonalDataExportPart.Empty;
        }

        Guid[] ids = [.. await entries.ListEntryIdsOfPersonAsync(nationalId, cancellationToken)];
        var proxies = await db.Proxies.AsNoTracking()
            .Where(p => ids.Contains(p.HolderEntryId) || ids.Contains(p.ProxyEntryId))
            .ToListAsync(cancellationToken);
        var handovers = await db.Handovers.AsNoTracking()
            .Where(h => ids.Contains(h.HolderEntryId) || (h.CollectorEntryId != null && ids.Contains(h.CollectorEntryId.Value)))
            .Join(db.Days, h => h.DistributionId, d => d.Id, (h, d) => new { Handover = h, d.EditionId })
            .ToListAsync(cancellationToken);
        if (proxies.Count == 0 && handovers.Count == 0)
        {
            return PersonalDataExportPart.Empty;
        }

        var years = new Dictionary<Guid, int?>();
        foreach (var editionId in proxies.Select(p => p.EditionId).Concat(handovers.Select(h => h.EditionId)).Distinct())
        {
            years[editionId] = (await editions.FindAsync(editionId, cancellationToken))?.Year;
        }

        // The other person of a proxy or a handover appears only by role (spec: Exporting a person's data).
        return new PersonalDataExportPart(
            [
                new PersonalDataSheet(
                    ProxiesSheet,
                    ["edition", "type", "participation"],
                    [
                        .. proxies.OrderBy(p => years[p.EditionId]).Select(p => (IReadOnlyList<object?>)
                        [
                            years[p.EditionId], EnumCodes.ToCode(p.Type), ids.Contains(p.HolderEntryId) ? "holder" : "proxy",
                        ]),
                    ]),
                new PersonalDataSheet(
                    HandoversSheet,
                    ["edition", "collectedAt", "powderKg", "rentalFlaskNumber", "traceability1", "traceability2", "participation"],
                    [
                        .. handovers.OrderBy(h => h.Handover.CollectedAt).Select(h => ids.Contains(h.Handover.HolderEntryId)
                            ? (IReadOnlyList<object?>)
                            [
                                years[h.EditionId], h.Handover.CollectedAt, (int)h.Handover.PowderKg, h.Handover.RentalFlaskNumber,
                                h.Handover.Traceability1, h.Handover.Traceability2,
                                h.Handover.CollectedBy == HandoverCollector.Proxy ? "holderByProxy" : "holder",
                            ]
                            // Collected for someone else: what they carried, not the holder's kilograms or codes.
                            : [years[h.EditionId], h.Handover.CollectedAt, null, h.Handover.RentalFlaskNumber, null, null, "proxy"]),
                    ]),
            ],
            []);
    }

    public Task PrepareErasureAsync(
        PersonalDataSubject subject, PersonalDataErasure erasure, DbTransaction transaction, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public async Task EraseAsync(
        PersonalDataSubject subject, PersonalDataErasure erasure, DbTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(erasure);
        if (subject is not PersonalDataSubject.Person || erasure.EntryIds.Count == 0)
        {
            return;
        }

        await db.EnlistAsync(transaction, cancellationToken);
        Guid[] ids = [.. erasure.EntryIds];
        var removed = await db.Database.ExecuteSqlAsync(
            $"DELETE FROM distribution.pickup_proxies WHERE holder_entry_id = ANY({ids}) OR proxy_entry_id = ANY({ids})",
            cancellationToken);
        erasure.Count("pickupProxiesRemoved", removed);
    }
}
