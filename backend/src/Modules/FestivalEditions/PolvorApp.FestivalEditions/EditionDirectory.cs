using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using PolvorApp.FederationCatalog.Contracts;
using PolvorApp.FestivalEditions.Contracts;
using PolvorApp.FestivalEditions.Editions;
using PolvorApp.FestivalEditions.Persistence;
using PolvorApp.SharedKernel.Codes;

namespace PolvorApp.FestivalEditions;

/// <summary>
/// Read-only edition lookups for other modules, e.g. comparsa orders (design D7). Drafts are
/// returned too: a caller acting for a FiringChief must treat a <see cref="EditionStatus.Draft"/> as
/// not found (BR-12). The reads are not one snapshot; orders reread inside their own write
/// transaction with <see cref="ReadForOrderWriteAsync"/>.
/// </summary>
internal sealed class EditionDirectory(FestivalEditionsDbContext db, ICatalogDirectory catalog) : IEditionDirectory
{
    public async Task<EditionSnapshot?> GetCurrentAsync(CancellationToken cancellationToken) =>
        await db.Editions.AsNoTracking().SingleOrDefaultAsync(e => e.Status == EditionStatus.InProgress, cancellationToken) is { } edition
            ? await SnapshotAsync(EditionRow.Of(edition), cancellationToken)
            : null;

    public async Task<EditionSnapshot?> FindAsync(Guid editionId, CancellationToken cancellationToken) =>
        await db.Editions.AsNoTracking().SingleOrDefaultAsync(e => e.Id == editionId, cancellationToken) is { } edition
            ? await SnapshotAsync(EditionRow.Of(edition), cancellationToken)
            : null;

    /// <summary>
    /// Runs on the caller's connection, which already runs <paramref name="transaction"/>: PostgreSQL
    /// has one transaction per connection, so the row lock belongs to the caller (add-comparsa-orders,
    /// design D4). The SQL stays here, in the module that owns the table.
    /// </summary>
    public async Task<EditionSnapshot?> ReadForOrderWriteAsync(Guid editionId, DbTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var connection = transaction.Connection ?? throw new InvalidOperationException("The transaction has no connection.");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT year, status, orders_open, festival_starts_on, festival_ends_on,"
            + " powder_per_kg, caps_box, weapon_rental, flask_rental"
            + " FROM editions.festival_editions WHERE id = $1 FOR SHARE";
        var parameter = command.CreateParameter();
        parameter.Value = editionId;
        command.Parameters.Add(parameter);

        EditionRow row;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            row = new EditionRow(
                editionId,
                reader.GetInt32(0),
                EnumCodes.Parse<EditionStatus>(reader.GetString(1)),
                reader.GetBoolean(2),
                reader.GetFieldValue<DateOnly>(3),
                reader.GetFieldValue<DateOnly>(4),
                new EditionPrices(
                    Price(reader, "powder_per_kg"), Price(reader, "caps_box"), Price(reader, "weapon_rental"), Price(reader, "flask_rental")));
        }

        return await SnapshotAsync(row, cancellationToken);
    }

    private async Task<EditionSnapshot> SnapshotAsync(EditionRow edition, CancellationToken cancellationToken)
    {
        var ids = await db.EditionWeaponModels.AsNoTracking()
            .Where(m => m.EditionId == edition.Id)
            .Select(m => m.WeaponModelId)
            .ToListAsync(cancellationToken);
        var models = await catalog.FindWeaponModelsAsync(ids, cancellationToken);
        if (models.Count != ids.Count)
        {
            // The foreign key prevents this; orders must never run on a silently truncated offer.
            throw new InvalidOperationException($"Edition {edition.Id} references weapon models the catalogue does not have.");
        }

        return new EditionSnapshot(
            edition.Id,
            edition.Year,
            edition.Status,
            edition.Status == EditionStatus.InProgress && edition.OrdersOpen,
            edition.FestivalStartsOn,
            edition.FestivalEndsOn,
            models.Where(m => m.Active && m.Rentable).Select(m => m.Id).Order().ToList(),
            edition.Prices);
    }

    /// <summary>A price column by name, so a reordered SELECT list cannot swap two prices; null when not set.</summary>
    private static decimal? Price(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
    }

    /// <summary>The columns a snapshot needs, from the entity or from the locking read.</summary>
    private sealed record EditionRow(
        Guid Id, int Year, EditionStatus Status, bool OrdersOpen, DateOnly FestivalStartsOn, DateOnly FestivalEndsOn, EditionPrices Prices)
    {
        public static EditionRow Of(FestivalEdition edition) =>
            new(edition.Id, edition.Year, edition.Status, edition.OrdersOpen, edition.FestivalStartsOn, edition.FestivalEndsOn, edition.GetPrices());
    }
}
