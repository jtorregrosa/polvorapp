using System.Data.Common;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.ComparsaOrders.Entries;
using PolvorApp.ComparsaOrders.Persistence;

namespace PolvorApp.ComparsaOrders.Orders;

/// <summary>
/// The entries as other modules read them (add-distribution-planning, design D3); unscoped, read-only.
/// Each entry is read with its order in one query.
/// </summary>
internal sealed class EditionEntries(ComparsaOrdersDbContext db, IArquebusierRoster roster) : IEditionEntries
{
    public async Task<IReadOnlyList<Guid>> ListEntryIdsOfPersonAsync(string normalisedNationalId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalisedNationalId);
        var arquebusierId = (await roster.FindLenderAsync(normalisedNationalId, cancellationToken))?.ArquebusierId;
        return await db.Entries.AsNoTracking()
            .Where(e => e.NationalId == normalisedNationalId || (arquebusierId != null && e.ArquebusierId == arquebusierId))
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EditionEntryFacts>> ListAsync(Guid editionId, Guid comparsaId, CancellationToken cancellationToken) =>
        await Facts(db.Orders.Where(o => o.EditionId == editionId && o.ComparsaId == comparsaId)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<EditionEntryFacts>> FindManyAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entryIds);
        if (entryIds.Count == 0)
        {
            return [];
        }

        Guid[] ids = [.. entryIds.Distinct()];
        if (ids.Length > IEditionEntries.MaxIds)
        {
            throw new ArgumentOutOfRangeException(nameof(entryIds), ids.Length, $"At most {IEditionEntries.MaxIds} entries are read at once.");
        }

        return await Facts(db.Orders, entry => ids.Contains(entry.Id)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, OrderStatus>> ListOrderStatusesAsync(Guid editionId, CancellationToken cancellationToken) =>
        await db.Orders.AsNoTracking().Where(o => o.EditionId == editionId)
            .Select(o => new { o.ComparsaId, o.Status })
            .ToDictionaryAsync(o => o.ComparsaId, o => o.Status, cancellationToken);

    public async Task<IReadOnlyList<OrderSummary>> ListOrdersAsync(Guid editionId, CancellationToken cancellationToken) =>
        await db.Orders.AsNoTracking().Where(o => o.EditionId == editionId)
            .Select(o => new OrderSummary(o.Id, o.ComparsaId, o.Status))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Runs on the caller's connection, which already runs <paramref name="transaction"/>, so the row
    /// locks belong to the caller; the SQL stays here, in the module that owns the table.
    /// </summary>
    public async Task<bool> AnyErasedForWriteAsync(IReadOnlyCollection<Guid> entryIds, DbTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entryIds);
        ArgumentNullException.ThrowIfNull(transaction);
        var connection = transaction.Connection ?? throw new InvalidOperationException("The transaction has no connection.");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM (SELECT erased_at FROM orders.edition_entries WHERE id = ANY($1) ORDER BY id FOR SHARE) e WHERE e.erased_at IS NOT NULL)";
        var parameter = command.CreateParameter();
        parameter.Value = entryIds.Distinct().ToArray();
        command.Parameters.Add(parameter);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private IQueryable<EditionEntryFacts> Facts(IQueryable<ComparsaOrder> orders, Expression<Func<EditionEntry, bool>>? filter = null)
    {
        var entries = db.Entries.AsNoTracking();
        if (filter is not null)
        {
            entries = entries.Where(filter);
        }

        return entries.Join(
            orders.AsNoTracking(),
            entry => entry.OrderId,
            order => order.Id,
            (entry, order) => new EditionEntryFacts(
                entry.Id,
                order.Id,
                order.EditionId,
                order.ComparsaId,
                order.Status,
                entry.ArquebusierId,
                entry.Status == ArquebusierStatus.Active,
                entry.PowderKg,
                entry.WeaponSource,
                entry.RentalWeaponModelId,
                entry.Flask,
                new ExportedPerson(entry.FirstName, entry.LastName, entry.NationalId, entry.FederationId),
                entry.ErasedAt != null));
    }
}
