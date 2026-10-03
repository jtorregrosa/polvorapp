using System.Data.Common;
using Npgsql;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.FestivalEditions.Contracts;

namespace PolvorApp.ComparsaOrders;

/// <summary>
/// The orders' part of an arquebusier's deletion (spec: Entries after registry changes (BR-13, BR-14);
/// design D3), run by the registry inside its own transaction after it locked the arquebusier. In the
/// module's lock order it reads the current edition <c>FOR SHARE</c>, then locks, by id, every order
/// whose entries or loans the deletion touches, so it serialises with the order writes. While the orders
/// are open it removes the arquebusier's entry of the edition in progress, with its loan; every other
/// entry is kept as history by the <c>ON DELETE SET NULL</c> keys. The SQL runs on the registry's
/// connection, which already runs its transaction.
/// </summary>
internal sealed class OrdersDeletionParticipant(IEditionDirectory editions) : IArquebusierDeletionParticipant
{
    public async Task<ArquebusierDeletionEffect> OnDeletingAsync(
        Guid arquebusierId, IReadOnlyCollection<Guid> ownedWeaponIds, DbTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ownedWeaponIds);
        ArgumentNullException.ThrowIfNull(transaction);
        var current = await editions.GetCurrentAsync(cancellationToken);
        var edition = current is null ? null : await editions.ReadForOrderWriteAsync(current.Id, transaction, cancellationToken);
        Guid[] weapons = [.. ownedWeaponIds];
        var locked = await LockOrdersAsync(transaction, arquebusierId, weapons, cancellationToken);
        if (locked.Count == 0 || edition is not { OrdersOpen: true })
        {
            // Closed orders and past editions: SET NULL keeps every entry as history.
            return ArquebusierDeletionEffect.None;
        }

        var removed = await RemoveCurrentEntryAsync(transaction, edition.Id, arquebusierId, cancellationToken);
        if (removed.Count == 0)
        {
            return ArquebusierDeletionEffect.None;
        }

        Guid[] orders = [.. removed.Select(r => r.OrderId).Distinct()];
        await TouchAsync(transaction, orders, cancellationToken);
        return new ArquebusierDeletionEffect([.. removed.Select(r => r.EntryId)], orders);
    }

    /// <summary>Locks, in id order, the orders holding an entry of the arquebusier or of one of their weapons, or a loan of one of their weapons.</summary>
    private static async Task<List<Guid>> LockOrdersAsync(DbTransaction transaction, Guid arquebusierId, Guid[] weapons, CancellationToken cancellationToken)
    {
        await using var command = Command(
            transaction,
            """
            SELECT o.id FROM orders.comparsa_orders o
            WHERE o.id IN (
                SELECT e.order_id FROM orders.edition_entries e
                WHERE e.arquebusier_id = $1 OR e.owned_weapon_id = ANY($2)
                UNION
                SELECT e.order_id FROM orders.weapon_loans l
                JOIN orders.edition_entries e ON e.id = l.entry_id
                WHERE l.lender_owned_weapon_id = ANY($2))
            ORDER BY o.id
            FOR UPDATE OF o
            """,
            arquebusierId,
            weapons);
        var orders = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            orders.Add(reader.GetGuid(0));
        }

        return orders;
    }

    /// <summary>Removes the arquebusier's entry of the edition; its loan goes with it (ON DELETE CASCADE).</summary>
    private static async Task<List<(Guid EntryId, Guid OrderId)>> RemoveCurrentEntryAsync(
        DbTransaction transaction, Guid editionId, Guid arquebusierId, CancellationToken cancellationToken)
    {
        await using var command = Command(
            transaction,
            "DELETE FROM orders.edition_entries WHERE edition_id = $1 AND arquebusier_id = $2 RETURNING id, order_id",
            editionId,
            arquebusierId);
        var removed = new List<(Guid, Guid)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            removed.Add((reader.GetGuid(0), reader.GetGuid(1)));
        }

        return removed;
    }

    /// <summary>Changes the orders' version, so a submission based on the order before the removal is refused (design D8).</summary>
    private static async Task TouchAsync(DbTransaction transaction, Guid[] orders, CancellationToken cancellationToken)
    {
        await using var command = Command(transaction, "UPDATE orders.comparsa_orders SET updated_at = now() WHERE id = ANY($1)", orders);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DbCommand Command(DbTransaction transaction, string sql, params object[] values)
    {
        var command = (transaction.Connection ?? throw new InvalidOperationException("The transaction has no connection.")).CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var value in values)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = value });
        }

        return command;
    }
}
