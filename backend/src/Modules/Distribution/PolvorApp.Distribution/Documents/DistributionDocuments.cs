using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.Distribution.Contracts;
using PolvorApp.Distribution.Persistence;
using PolvorApp.Distribution.Proxies;
using PolvorApp.Exports.Contracts;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Codes;
using PolvorApp.SharedKernel.Http;
using PolvorApp.SharedKernel.Storage;

namespace PolvorApp.Distribution.Documents;

/// <summary>
/// Builds the distribution documents for the caller (spec: Distribution lists, Pickup authorisation form,
/// Distribution documents are protected and audited; design D6, D7): checks who may have it, reads the data,
/// builds and renders the document, records the download in the audit trail and only then returns it. A
/// document that could not be audited is not returned (SEC-05). Nothing is stored.
/// </summary>
internal sealed partial class DistributionDocuments(
    DistributionDbContext db,
    IEditionDirectory editions,
    IOrderExports orders,
    IEditionEntries entries,
    IArquebusierRoster roster,
    ICatalogDirectory catalog,
    IFederationSettings federation,
    IComparsaScope scope,
    ICurrentUser currentUser,
    IDocumentRenderer renderer,
    IAuditLog auditLog,
    ILogger<DistributionDocuments> logger)
{
    public const string AuditAction = "DistributionDocumentDownloaded";
    public const string EntityType = "DistributionDocument";

    /// <summary>The platform's problem code for a storage outage, as the catalogue answers it.</summary>
    public const string StorageUnavailable = "storage.unavailable";

    /// <summary>A day's list (Admins only, at the endpoint), from the edition's validated orders.</summary>
    public async Task<Results<FileContentHttpResult, ProblemHttpResult>> ListAsync(Guid dayId, DocumentFileFormat format, CancellationToken cancellationToken)
    {
        var day = await db.Days.AsNoTracking().Include(d => d.Slots).SingleOrDefaultAsync(d => d.Id == dayId, cancellationToken);
        var edition = day is null ? null : await editions.FindAsync(day.EditionId, cancellationToken);
        if (day is null || edition is null)
        {
            return DistributionProblems.From(DistributionOutcome.NotFound);
        }

        var validated = await orders.ListValidatedAsync(edition.Id, cancellationToken);
        var all = validated.SelectMany(o => o.Entries).ToList();
        var proxies = await db.Proxies.AsNoTracking().Where(p => p.EditionId == edition.Id && p.Type == day.Type)
            .Select(p => new ListProxy(p.HolderEntryId, p.ProxyEntryId)).ToListAsync(cancellationToken);
        var data = new DistributionListData(
            edition.Year,
            day.Type,
            day.Date,
            day.Location,
            day.Slots.ToDictionary(s => s.ComparsaId, s => s.StartsAt),
            validated,
            await ComparsaNamesAsync(validated.Select(o => o.ComparsaId), cancellationToken),
            await ModelLabelsAsync(all.Select(e => e.RentalWeaponModelId).OfType<Guid>(), cancellationToken),
            await LiveAsync(all.Select(e => e.ArquebusierId), cancellationToken),
            proxies);
        var built = DistributionLists.BuildWithCounts(data, DistributionTexts.For(CultureInfo.CurrentUICulture));
        if (built.ProxiesWithoutEntry > 0 || built.ErasedNames > 0)
        {
            // Read between the orders and the proxies (no shared snapshot, design D6), or erased on a GDPR request.
            LogListGaps(logger, day.Id, built.ProxiesWithoutEntry, built.ErasedNames);
        }

        var document = renderer.RenderTable(built.Table, format);
        return await DeliverAsync(
            document,
            DistributionLists.Name(day.Type),
            new
            {
                version = DistributionLists.Version,
                format = EnumCodes.ToCode(format),
                editionId = edition.Id,
                editionYear = edition.Year,
                type = EnumCodes.ToCode(day.Type),
                rows = built.Table.Rows.Count,
                proxiesLeftOut = built.ProxiesWithoutLicense + built.ProxiesWithoutEntry,
            },
            comparsaId: null,
            cancellationToken);
    }

    /// <summary>A proxy's authorisation form, for Admins and the comparsa's FiringChiefs; refused while the proxy has a problem.</summary>
    public async Task<Results<FileContentHttpResult, ProblemHttpResult>> FormAsync(Guid proxyId, CancellationToken cancellationToken)
    {
        var proxy = await db.Proxies.AsNoTracking().SingleOrDefaultAsync(p => p.Id == proxyId, cancellationToken);
        var access = await scope.GetAccessAsync(cancellationToken);
        var edition = proxy is null || !access.CanAccess(proxy.ComparsaId) ? null : await editions.FindAsync(proxy.EditionId, cancellationToken);
        if (proxy is null || edition is null || (!currentUser.IsAdmin && edition.Status == EditionStatus.Draft))
        {
            return DistributionProblems.From(DistributionOutcome.ProxyNotFound);
        }

        var facts = (await entries.FindManyAsync([proxy.HolderEntryId, proxy.ProxyEntryId], cancellationToken)).ToDictionary(f => f.EntryId);
        if (!facts.TryGetValue(proxy.HolderEntryId, out var holder) || !facts.TryGetValue(proxy.ProxyEntryId, out var proxyEntry))
        {
            // Removed with its entry right now (BR-14); logged so that a lasting miss shows as the inconsistency it is.
            LogEntryMissing(logger, proxy.Id);
            return DistributionProblems.From(DistributionOutcome.ProxyNotFound);
        }

        if (holder.OrderId != proxyEntry.OrderId || holder.ComparsaId != proxy.ComparsaId || holder.EditionId != proxy.EditionId)
        {
            // Never printed: the people would come from one order and the comparsa from another (BR-06).
            throw new InvalidOperationException($"Pickup proxy {proxy.Id} does not match the order of its entries.");
        }

        var day = await db.Days.AsNoTracking().SingleOrDefaultAsync(d => d.EditionId == edition.Id && d.Type == proxy.Type, cancellationToken);
        var live = await LiveAsync([holder.ArquebusierId, proxyEntry.ArquebusierId], cancellationToken);
        var texts = DistributionTexts.For(CultureInfo.CurrentUICulture);
        var federationName = (await federation.GetAsync(cancellationToken)).OfficialName(texts.NameForm);
        var proxyLicense = License(live, proxyEntry);
        var problem = ProxyRules.ProblemOf(holder, proxy.Type, proxyLicense, day?.Date ?? edition.FestivalStartsOn);
        if (problem is { } refused)
        {
            return DistributionProblems.From(refused == ProxyProblem.NotApplicable ? DistributionOutcome.NotApplicable : DistributionOutcome.LicenseInvalid);
        }

        DocumentImage? logo;
        try
        {
            logo = await catalog.ReadFederationLogoAsync(cancellationToken) is { } image ? DocumentImage.FromPng(image.Png) : null;
        }
        catch (StorageUnavailableException exception)
        {
            // An uploaded logo the storage cannot serve: the form is not printed without it (spec: Federation
            // logo), and the user retries. Without an uploaded logo the form prints without one.
            LogLogoUnavailable(logger, exception, proxy.Id);
            return ProblemResults.Problem(StatusCodes.Status503ServiceUnavailable, StorageUnavailable);
        }

        var comparsa = (await ComparsaNamesAsync([proxy.ComparsaId], cancellationToken)).TryGetValue(proxy.ComparsaId, out var name)
            ? name
            : throw new InvalidOperationException($"Comparsa {proxy.ComparsaId} of pickup proxy {proxy.Id} is missing from the catalogue.");
        var data = new PickupFormData(
            edition.Year, proxy.Type, proxy.Id, comparsa, Person(texts, live, holder), Person(texts, live, proxyEntry), day?.Date, day?.Location, logo, federationName);
        var document = renderer.RenderForm(PickupAuthorisationForm.Build(data, texts));
        return await DeliverAsync(
            document,
            PickupAuthorisationForm.Name,
            new { version = PickupAuthorisationForm.Version, format = EnumCodes.ToCode(DocumentFileFormat.Pdf), editionId = edition.Id, editionYear = edition.Year, type = EnumCodes.ToCode(proxy.Type), proxyId = proxy.Id },
            proxy.ComparsaId,
            cancellationToken);
    }

    /// <summary>Audits the download (no personal data), then returns the file; no audit, no file.</summary>
    private async Task<Results<FileContentHttpResult, ProblemHttpResult>> DeliverAsync(
        RenderedDocument document, string name, object data, Guid? comparsaId, CancellationToken cancellationToken)
    {
        try
        {
            await auditLog.RecordAsync(new AuditRecord(AuditAction, EntityType, name, data, comparsaId), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogAuditFailed(logger, exception, name, comparsaId);
            return ProblemResults.Problem(StatusCodes.Status503ServiceUnavailable, DistributionProblems.AuditUnavailable);
        }

        return TypedResults.File(document.Content.ToArray(), document.ContentType, document.FileName);
    }

    private static ArquebusierLicenseFacts? License(IReadOnlyDictionary<Guid, RosterArquebusier> live, EditionEntryFacts entry) =>
        entry.ArquebusierId is { } id && live.TryGetValue(id, out var arquebusier) ? arquebusier.License : null;

    /// <summary>
    /// The person as the form shows them: from the registry while present, from the entry's copy otherwise.
    /// Only an issued license prints its type: a pending one is a license the person lacks (spec: left blank).
    /// A copy erased on a GDPR request prints the placeholder, never a blank.
    /// </summary>
    private static FormPerson Person(DistributionTexts texts, IReadOnlyDictionary<Guid, RosterArquebusier> live, EditionEntryFacts entry)
    {
        if (entry.ArquebusierId is { } id && live.TryGetValue(id, out var arquebusier))
        {
            var type = arquebusier.License is ArquebusierLicenseFacts.Issued issued ? issued.Type : (LicenseType?)null;
            return new FormPerson(Name(arquebusier.LastName, arquebusier.FirstName), arquebusier.NationalId, type);
        }

        var name = Name(entry.Copy.LastName, entry.Copy.FirstName);
        return new FormPerson(name.Length > 0 ? name : texts.ErasedPerson, entry.Copy.NationalId, null);
    }

    private static string Name(string? lastName, string? firstName) =>
        string.Join(", ", new[] { lastName, firstName }.Where(part => !string.IsNullOrWhiteSpace(part)));

    private async Task<IReadOnlyDictionary<Guid, RosterArquebusier>> LiveAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
    {
        Guid[] distinct = [.. ids.OfType<Guid>().Distinct()];
        return distinct.Length == 0 ? new Dictionary<Guid, RosterArquebusier>() : (await roster.FindManyAsync(distinct, cancellationToken)).ToDictionary(a => a.Id);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> ComparsaNamesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        Guid[] distinct = [.. ids.Distinct()];
        return distinct.Length == 0 ? new Dictionary<Guid, string>() : (await catalog.FindComparsasAsync(distinct, cancellationToken)).ToDictionary(c => c.Id, c => c.Name);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> ModelLabelsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        Guid[] distinct = [.. ids.Distinct()];
        return distinct.Length == 0 ? new Dictionary<Guid, string>() : (await catalog.FindWeaponModelsAsync(distinct, cancellationToken)).ToDictionary(m => m.Id, m => m.Label);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Distribution document {Document} for comparsa {ComparsaId} could not be audited and was not returned")]
    private static partial void LogAuditFailed(ILogger logger, Exception exception, string document, Guid? comparsaId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Distribution list of {DistributionId}: {ProxiesWithoutEntry} proxies without their entry, {ErasedNames} erased names")]
    private static partial void LogListGaps(ILogger logger, Guid distributionId, int proxiesWithoutEntry, int erasedNames);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pickup proxy {ProxyId} has an entry that is gone; no form")]
    private static partial void LogEntryMissing(ILogger logger, Guid proxyId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Federation logo could not be read for the form of pickup proxy {ProxyId}")]
    private static partial void LogLogoUnavailable(ILogger logger, Exception exception, Guid proxyId);
}
