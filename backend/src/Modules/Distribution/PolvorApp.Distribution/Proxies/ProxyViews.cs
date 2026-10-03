using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.Distribution.Contracts;
using PolvorApp.Distribution.Persistence;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Text;

namespace PolvorApp.Distribution.Proxies;

/// <summary>A person of a proxy as the page names them: the entry and "Last name, First name".</summary>
internal sealed record ProxyPersonResponse(Guid EntryId, string Name);

/// <summary>A pickup proxy as the API returns it, with the problem it shows now (spec: Proxies that no longer hold).</summary>
internal sealed record ProxyResponse(
    Guid Id,
    Guid EditionId,
    Guid ComparsaId,
    string ComparsaName,
    DistributionType Type,
    ProxyPersonResponse Holder,
    ProxyPersonResponse Proxy,
    ProxyProblem? Problem);

/// <summary>Why an entry cannot collect for others of a type: <c>licenseInvalid</c> or <c>proxyAbsent</c> (they have a proxy themselves).</summary>
internal sealed record ProxyRestriction(DistributionType Type, string Reason);

/// <summary>
/// An entry of the comparsa's order, for the "Add proxy" panel (design D5): the types a proxy may be
/// registered for with it as holder, and why it cannot collect for others of a type.
/// </summary>
internal sealed record ProxyCandidateResponse(
    Guid EntryId,
    string Name,
    bool IsActive,
    IReadOnlyList<DistributionType> CanBeHeldFor,
    IReadOnlyList<ProxyRestriction> CannotCollect);

/// <summary>
/// Reads pickup proxies and proxy candidates for the caller (spec: Distribution visibility (BR-12),
/// Proxies that no longer hold; design D5, D7): scoped to the user's comparsas, never a draft edition for
/// a FiringChief, names from the registry while the arquebusier is in it and from the entry's copy
/// otherwise, and every proxy re-checked against today's data.
/// </summary>
internal sealed partial class ProxyViews(
    DistributionDbContext db,
    IEditionDirectory editions,
    IEditionEntries entries,
    IArquebusierRoster roster,
    ICatalogDirectory catalog,
    IComparsaScope scope,
    ICurrentUser currentUser,
    ILogger<ProxyViews> logger)
{
    /// <summary>The proxies of the edition the user may see, optionally of one comparsa; null when the edition or comparsa is not visible.</summary>
    public async Task<IReadOnlyList<ProxyResponse>?> ListAsync(Guid editionId, Guid? comparsaId, CancellationToken cancellationToken)
    {
        if (await VisibleEditionAsync(editionId, cancellationToken) is not { } edition)
        {
            return null;
        }

        var access = await scope.GetAccessAsync(cancellationToken);
        if (comparsaId is { } only && !access.CanAccess(only))
        {
            return null;
        }

        var query = access.Filter(db.Proxies.AsNoTracking().Where(p => p.EditionId == editionId), p => p.ComparsaId);
        if (comparsaId is { } filter)
        {
            query = query.Where(p => p.ComparsaId == filter);
        }

        return await DescribeAsync(edition, await query.ToListAsync(cancellationToken), cancellationToken);
    }

    /// <summary>
    /// One proxy just written by the caller, who may see it; null when it is already gone (its entry was
    /// deleted with its arquebusier right after the commit, BR-14).
    /// </summary>
    public async Task<ProxyResponse?> CreatedAsync(PickupProxy proxy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        var edition = await editions.FindAsync(proxy.EditionId, cancellationToken)
            ?? throw new InvalidOperationException($"Edition {proxy.EditionId} of a new proxy is missing.");
        return (await DescribeAsync(edition, [proxy], cancellationToken)).SingleOrDefault();
    }

    /// <summary>The entries of the comparsa's order with what each may hold or collect; null when the edition or comparsa is not visible.</summary>
    public async Task<IReadOnlyList<ProxyCandidateResponse>?> CandidatesAsync(Guid editionId, Guid comparsaId, CancellationToken cancellationToken)
    {
        if (await VisibleEditionAsync(editionId, cancellationToken) is not { } edition
            || !(await scope.GetAccessAsync(cancellationToken)).CanAccess(comparsaId))
        {
            return null;
        }

        var order = await entries.ListAsync(editionId, comparsaId, cancellationToken);
        var people = await PeopleAsync(order, cancellationToken);
        var references = await ReferencesAsync(edition, cancellationToken);
        var proxies = await db.Proxies.AsNoTracking().Where(p => p.EditionId == editionId && p.ComparsaId == comparsaId)
            .Select(p => new { p.Type, p.HolderEntryId, p.ProxyEntryId }).ToListAsync(cancellationToken);
        DistributionType[] types = [DistributionType.Powder, DistributionType.Weapons];
        return [.. order
            .Select(entry =>
            {
                var person = people[entry.EntryId];
                var holds = types.Where(type => ProxyRules.HasSomethingToCollect(entry, type)
                    && !proxies.Any(p => p.Type == type && (p.HolderEntryId == entry.EntryId || p.ProxyEntryId == entry.EntryId)));
                var restrictions = types.Select(type =>
                        proxies.Any(p => p.Type == type && p.HolderEntryId == entry.EntryId) ? new ProxyRestriction(type, ProxyRules.ProxyAbsent)
                        : !ProxyRules.LicenseHolds(person.License, references(type)) ? new ProxyRestriction(type, ProxyRules.LicenseInvalid)
                        : null)
                    .OfType<ProxyRestriction>();
                return (person, Candidate: new ProxyCandidateResponse(entry.EntryId, person.Name, entry.IsActive, [.. holds], [.. restrictions]));
            })
            .OrderBy(c => c.person.LastName, SpanishOrder.Names)
            .ThenBy(c => c.person.FirstName, SpanishOrder.Names)
            .Select(c => c.Candidate)];
    }

    private async Task<EditionSnapshot?> VisibleEditionAsync(Guid editionId, CancellationToken cancellationToken)
    {
        var edition = await editions.FindAsync(editionId, cancellationToken);
        return edition is null || (!currentUser.IsAdmin && edition.Status == EditionStatus.Draft) ? null : edition;
    }

    private async Task<IReadOnlyList<ProxyResponse>> DescribeAsync(EditionSnapshot edition, List<PickupProxy> proxies, CancellationToken cancellationToken)
    {
        if (proxies.Count == 0)
        {
            return [];
        }

        var facts = new List<EditionEntryFacts>();
        foreach (var chunk in proxies.SelectMany(p => new[] { p.HolderEntryId, p.ProxyEntryId }).Distinct().Chunk(IEditionEntries.MaxIds))
        {
            facts.AddRange(await entries.FindManyAsync(chunk, cancellationToken));
        }

        var byEntry = facts.ToDictionary(f => f.EntryId);
        var people = await PeopleAsync(facts, cancellationToken);
        var references = await ReferencesAsync(edition, cancellationToken);
        var comparsas = (await catalog.FindComparsasAsync([.. proxies.Select(p => p.ComparsaId).Distinct()], cancellationToken)).ToDictionary(c => c.Id, c => c.Name);
        return [.. proxies
            .Where(p => Present(p, byEntry))
            .Select(p =>
            {
                var (holder, proxy) = (people[p.HolderEntryId], people[p.ProxyEntryId]);
                var problem = ProxyRules.ProblemOf(byEntry[p.HolderEntryId], p.Type, proxy.License, references(p.Type));
                // The comparsa key protects it (NO ACTION): a missing name is an inconsistency, never a blank.
                var comparsa = comparsas.TryGetValue(p.ComparsaId, out var name) ? name
                    : throw new InvalidOperationException($"Comparsa {p.ComparsaId} of proxy {p.Id} is missing from the catalogue.");
                return (Holder: holder, Response: new ProxyResponse(
                    p.Id, p.EditionId, p.ComparsaId, comparsa, p.Type,
                    new ProxyPersonResponse(p.HolderEntryId, holder.Name), new ProxyPersonResponse(p.ProxyEntryId, proxy.Name), problem));
            })
            .OrderBy(r => r.Response.ComparsaName, SpanishOrder.Names)
            .ThenBy(r => r.Holder.LastName, SpanishOrder.Names)
            .ThenBy(r => r.Holder.FirstName, SpanishOrder.Names)
            .ThenBy(r => r.Response.Type)
            .Select(r => r.Response)];
    }

    /// <summary>
    /// Whether both entries of the proxy were found. Not finding one means the proxy was removed with it
    /// (cascade, BR-14) between the two reads: it is left out, and logged so a real inconsistency shows.
    /// </summary>
    private bool Present(PickupProxy proxy, Dictionary<Guid, EditionEntryFacts> byEntry)
    {
        if (byEntry.ContainsKey(proxy.HolderEntryId) && byEntry.ContainsKey(proxy.ProxyEntryId))
        {
            return true;
        }

        LogEntryMissing(logger, proxy.Id);
        return false;
    }

    /// <summary>The reference date of each type in the edition (design D5).</summary>
    private async Task<Func<DistributionType, DateOnly>> ReferencesAsync(EditionSnapshot edition, CancellationToken cancellationToken)
    {
        var days = await db.Days.AsNoTracking().Where(d => d.EditionId == edition.Id).ToDictionaryAsync(d => d.Type, d => d.Date, cancellationToken);
        return type => ProxyRules.ReferenceDate(type, days, edition.FestivalStartsOn);
    }

    /// <summary>Each entry's person: live from the registry while the arquebusier is in it, from the entry's copy otherwise.</summary>
    private async Task<Dictionary<Guid, Person>> PeopleAsync(IReadOnlyList<EditionEntryFacts> facts, CancellationToken cancellationToken)
    {
        Guid[] ids = [.. facts.Select(f => f.ArquebusierId).OfType<Guid>().Distinct()];
        var live = ids.Length == 0 ? [] : (await roster.FindManyAsync(ids, cancellationToken)).ToDictionary(a => a.Id);
        foreach (var missing in facts.Where(f => f.ArquebusierId is { } id && !live.ContainsKey(id)))
        {
            // Deleted from the registry after the entry was read: named from the copy, without a license.
            LogArquebusierMissing(logger, missing.EntryId);
        }

        // A copy erased on a GDPR request has no name left (UC-26): the name is then empty.
        return facts.ToDictionary(f => f.EntryId, f => f.ArquebusierId is { } id && live.TryGetValue(id, out var arquebusier)
            ? new Person(arquebusier.LastName, arquebusier.FirstName, arquebusier.License)
            : new Person(f.Copy.LastName ?? string.Empty, f.Copy.FirstName ?? string.Empty, null));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Pickup proxy {ProxyId} left out: one of its entries is gone")]
    private static partial void LogEntryMissing(ILogger logger, Guid proxyId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Entry {EntryId} is linked to an arquebusier the registry no longer returns")]
    private static partial void LogArquebusierMissing(ILogger logger, Guid entryId);

    private sealed record Person(string LastName, string FirstName, ArquebusierLicenseFacts? License)
    {
        public string Name => string.Join(", ", new[] { LastName, FirstName }.Where(part => part.Length > 0));

        public override string ToString() => nameof(Person);
    }
}
