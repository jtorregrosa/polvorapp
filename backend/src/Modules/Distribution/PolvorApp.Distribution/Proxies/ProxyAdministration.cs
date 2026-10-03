using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.Distribution.Contracts;
using PolvorApp.Distribution.Persistence;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.IdentityAccess.Contracts;
using PolvorApp.SharedKernel.Auditing;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.Distribution.Proxies;

/// <summary>
/// Registers and removes pickup proxies (spec: Pickup proxies, Who may manage pickup proxies; design D4,
/// D5, D8). The reads through other modules' contracts (entries, registry) happen before the write's
/// transaction, so a write never holds two connections. Inside it: the edition <c>FOR SHARE</c>, then
/// the comparsa's proxy lock — taken before any insert or delete of a proxy — then the absence and
/// uniqueness rules on this module's own table. FiringChiefs act on their comparsas while the edition is
/// in progress; Admins on any comparsa of an edition that is not a draft.
/// </summary>
internal sealed class ProxyAdministration(
    DistributionDbContext db,
    IEditionDirectory editions,
    IEditionEntries entries,
    IArquebusierRoster roster,
    IComparsaScope scope,
    ICurrentUser currentUser,
    IAuditTrail trail,
    DistributionWriteGuard guard,
    TimeProvider time)
{
    public const string EntityType = "PickupProxy";

    /// <summary>
    /// Registers a proxy. Its rules are checked against data read before the write's transaction (entries,
    /// license, days): a change committed in between can store a proxy that no longer holds, which every
    /// read then shows as its problem (spec: Proxies that no longer hold; design D5). The absence and
    /// uniqueness rules, on this module's own table, are checked under the comparsa's lock.
    /// </summary>
    public async Task<DistributionResult<PickupProxy>> RegisterAsync(
        Guid editionId, Guid holderEntryId, Guid proxyEntryId, DistributionType type, CancellationToken cancellationToken)
    {
        var (refusal, holder) = await CheckRegistrationAsync(editionId, holderEntryId, proxyEntryId, type, cancellationToken);
        return refusal ?? await guard.RunAsync(nameof(RegisterAsync), holderEntryId, () => WriteAsync(holder!, proxyEntryId, type, cancellationToken));
    }

    /// <summary>In order: the edition and the holder's scope (404), the edition rule (409), then the registration rules (400).</summary>
    private async Task<(DistributionResult<PickupProxy>? Refusal, EditionEntryFacts? Holder)> CheckRegistrationAsync(
        Guid editionId, Guid holderEntryId, Guid proxyEntryId, DistributionType type, CancellationToken cancellationToken)
    {
        const string Operation = nameof(RegisterAsync);
        var edition = await editions.FindAsync(editionId, cancellationToken);
        if (edition is null || (!currentUser.IsAdmin && edition.Status == EditionStatus.Draft))
        {
            return (Refused(Operation, DistributionOutcome.NotFound, editionId), null);
        }

        var access = await scope.GetAccessAsync(cancellationToken);
        var found = await entries.FindManyAsync([holderEntryId, proxyEntryId], cancellationToken);
        var holder = found.FirstOrDefault(e => e.EntryId == holderEntryId);
        if (holder is null || holder.EditionId != editionId || !access.CanAccess(holder.ComparsaId))
        {
            return (Refused(Operation, DistributionOutcome.NotFound, holderEntryId), null);
        }

        if (!MayWrite(edition.Status))
        {
            return (Refused(Operation, DistributionOutcome.EditionNotInProgress, editionId), null);
        }

        // The proxy's own data (license) is read only once it is known to be of the holder's order.
        var proxy = found.FirstOrDefault(e => e.EntryId == proxyEntryId);
        if ((proxy is null ? ("proxyEntryId", ProxyRules.NotInOrder) : ProxyRules.CheckEntries(holder, proxy, type)) is { } broken)
        {
            return (Invalid(Operation, broken.Field, broken.Reason, holderEntryId), null);
        }

        var days = await db.Days.AsNoTracking().Where(d => d.EditionId == editionId).ToDictionaryAsync(d => d.Type, d => d.Date, cancellationToken);
        var license = await LicenseAsync(proxy!, cancellationToken);
        return ProxyRules.LicenseHolds(license, ProxyRules.ReferenceDate(type, days, edition.FestivalStartsOn))
            ? (null, holder)
            : (Invalid(Operation, "proxyEntryId", ProxyRules.LicenseInvalid, holderEntryId), null);
    }

    /// <summary>The edition rule again under its share lock, the comparsa's lock, the absence and uniqueness rules, then the insert.</summary>
    private async Task<DistributionResult<PickupProxy>> WriteAsync(EditionEntryFacts holder, Guid proxyEntryId, DistributionType type, CancellationToken cancellationToken)
    {
        await using var transaction = await db.BeginWriteAsync(cancellationToken);
        if (await RecheckEditionAsync(holder.EditionId, DistributionOutcome.NotFound, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        await db.LockProxiesAsync(holder.EditionId, holder.ComparsaId, cancellationToken);
        var related = await db.Proxies.Where(p => p.Type == type
                && (p.HolderEntryId == holder.EntryId || p.HolderEntryId == proxyEntryId || p.ProxyEntryId == holder.EntryId))
            .Select(p => new { p.HolderEntryId, p.ProxyEntryId })
            .ToListAsync(cancellationToken);
        var outcome = related.Any(p => p.HolderEntryId == holder.EntryId) ? DistributionOutcome.AlreadyAuthorised
            : related.Any(p => p.HolderEntryId == proxyEntryId) ? DistributionOutcome.ProxyAbsent
            : related.Any(p => p.ProxyEntryId == holder.EntryId) ? DistributionOutcome.HolderIsProxy
            : DistributionOutcome.Done;
        if (outcome != DistributionOutcome.Done)
        {
            return DistributionResult<PickupProxy>.Failed(outcome);
        }

        var now = time.GetUtcNow();
        var created = new PickupProxy
        {
            Id = Guid.CreateVersion7(now),
            EditionId = holder.EditionId,
            ComparsaId = holder.ComparsaId,
            Type = type,
            HolderEntryId = holder.EntryId,
            ProxyEntryId = proxyEntryId,
            CreatedAt = now,
        };
        db.Proxies.Add(created);
        Record("PickupProxyAuthorised", created);
        return await SaveAsync(created, transaction, cancellationToken);
    }

    /// <summary>
    /// The proxy arquebusier's license: none when the entry is no longer linked to the registry. A linked
    /// arquebusier the registry does not return was deleted meanwhile: logged, and treated as no license.
    /// </summary>
    private async Task<ArquebusierLicenseFacts?> LicenseAsync(EditionEntryFacts proxy, CancellationToken cancellationToken)
    {
        if (proxy.ArquebusierId is not { } arquebusierId)
        {
            return null;
        }

        var arquebusier = (await roster.FindManyAsync([arquebusierId], cancellationToken)).SingleOrDefault();
        if (arquebusier is null)
        {
            guard.Inconsistent("proxy arquebusier missing from the registry", proxy.EntryId);
        }

        return arquebusier?.License;
    }

    public async Task<DistributionResult<PickupProxy>> RemoveAsync(Guid proxyId, CancellationToken cancellationToken)
    {
        // A plain read first, to know which comparsa to lock (design D4: the lock before any delete).
        var stored = await db.Proxies.AsNoTracking().SingleOrDefaultAsync(p => p.Id == proxyId, cancellationToken);
        var access = await scope.GetAccessAsync(cancellationToken);
        const string Operation = nameof(RemoveAsync);
        if (stored is null || !access.CanAccess(stored.ComparsaId))
        {
            return Refused(Operation, DistributionOutcome.ProxyNotFound, proxyId);
        }

        var edition = await editions.FindAsync(stored.EditionId, cancellationToken);
        if (edition is null || (!currentUser.IsAdmin && edition.Status == EditionStatus.Draft))
        {
            return Refused(Operation, DistributionOutcome.ProxyNotFound, proxyId);
        }

        if (!MayWrite(edition.Status))
        {
            return Refused(Operation, DistributionOutcome.EditionNotInProgress, proxyId);
        }

        return await guard.RunAsync(Operation, proxyId, async () =>
        {
            await using var transaction = await db.BeginWriteAsync(cancellationToken);
            if (await RecheckEditionAsync(stored.EditionId, DistributionOutcome.ProxyNotFound, cancellationToken) is { } refusal)
            {
                return refusal;
            }

            await db.LockProxiesAsync(stored.EditionId, stored.ComparsaId, cancellationToken);
            var proxy = await db.Proxies.SingleOrDefaultAsync(p => p.Id == proxyId, cancellationToken);
            if (proxy is null)
            {
                // Removed meanwhile, by another user or with its entry (BR-14).
                return DistributionResult<PickupProxy>.Failed(DistributionOutcome.ProxyNotFound);
            }

            db.Proxies.Remove(proxy);
            Record("PickupProxyRemoved", proxy);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // The cascade of an entry deletion (BR-14) takes no proxy lock: it removed the row first.
                db.ChangeTracker.Clear();
                guard.LostRace(proxyId, "entry deleted");
                return DistributionResult<PickupProxy>.Failed(DistributionOutcome.ProxyNotFound);
            }

            await transaction.CommitAsync(cancellationToken);
            return DistributionResult<PickupProxy>.Done(proxy);
        });
    }

    /// <summary>FiringChiefs while the edition is in progress; Admins in any edition that is not a draft.</summary>
    private bool MayWrite(EditionStatus status) =>
        currentUser.IsAdmin ? status != EditionStatus.Draft : status == EditionStatus.InProgress;

    /// <summary>The edition read <c>FOR SHARE</c>: a status move waits for this write, and the rule is checked again.</summary>
    /// <param name="editionId">The edition.</param>
    /// <param name="notFound">The outcome when the edition is gone: the entity the request named.</param>
    /// <param name="cancellationToken">The request token.</param>
    private async Task<DistributionResult<PickupProxy>?> RecheckEditionAsync(Guid editionId, DistributionOutcome notFound, CancellationToken cancellationToken)
    {
        var edition = await db.ReadEditionForWriteAsync(editions, editionId, cancellationToken);
        return edition is null ? DistributionResult<PickupProxy>.Failed(notFound)
            : MayWrite(edition.Status) ? null
            : DistributionResult<PickupProxy>.Failed(DistributionOutcome.EditionNotInProgress);
    }

    private async Task<DistributionResult<PickupProxy>> SaveAsync(PickupProxy proxy, IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DistributionProblems.Violates(exception, PostgresErrorCodes.UniqueViolation, DistributionDbContext.ProxyHolderIndex))
        {
            db.ChangeTracker.Clear();
            guard.LostRace(proxy.HolderEntryId, DistributionDbContext.ProxyHolderIndex);
            return DistributionResult<PickupProxy>.Failed(DistributionOutcome.AlreadyAuthorised);
        }
        catch (DbUpdateException exception) when (
            DistributionProblems.Violates(exception, PostgresErrorCodes.ForeignKeyViolation, DistributionDbContext.ProxyHolderEntryForeignKey)
            || DistributionProblems.Violates(exception, PostgresErrorCodes.ForeignKeyViolation, DistributionDbContext.ProxyProxyEntryForeignKey))
        {
            // An entry was removed with its arquebusier after it was read (BR-14): as if it never existed.
            db.ChangeTracker.Clear();
            guard.LostRace(proxy.HolderEntryId, "entry deleted");
            return DistributionResult<PickupProxy>.Failed(DistributionOutcome.NotFound);
        }

        await transaction.CommitAsync(cancellationToken);
        return DistributionResult<PickupProxy>.Done(proxy);
    }

    private DistributionResult<PickupProxy> Refused(string operation, DistributionOutcome outcome, Guid targetId)
    {
        guard.Rejected(operation, outcome, targetId);
        return DistributionResult<PickupProxy>.Failed(outcome);
    }

    private DistributionResult<PickupProxy> Invalid(string operation, string field, string reason, Guid targetId)
    {
        guard.Rejected(operation, DistributionOutcome.Invalid, targetId);
        return DistributionResult<PickupProxy>.Invalid(new Dictionary<string, string>(StringComparer.Ordinal) { [field] = reason });
    }

    /// <summary>The comparsa, the type and both entries by id: never a name or DNI/NIE (spec: Distribution changes are audited).</summary>
    private void Record(string action, PickupProxy proxy) =>
        trail.Record(db, new AuditRecord(
            action,
            EntityType,
            proxy.Id.ToString(),
            new { proxy.EditionId, type = EnumCodes.ToCode(proxy.Type), proxy.HolderEntryId, proxy.ProxyEntryId },
            ComparsaId: proxy.ComparsaId));
}
