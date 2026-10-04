using System.Globalization;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.Distribution.Contracts;
using PolvorApp.Distribution.Proxies;
using PolvorApp.Exports.Contracts;

namespace PolvorApp.Distribution.Documents;

/// <summary>A stored proxy of the list's type, by its two entries.</summary>
internal sealed record ListProxy(Guid HolderEntryId, Guid ProxyEntryId);

/// <summary>
/// A built list and what it left out on purpose, for the audit: the proxies whose license does not hold
/// on the day, and those whose entry is not in the validated orders (read in between, design D6).
/// </summary>
internal sealed record BuiltList(DocumentTable Table, int ProxiesWithoutLicense, int ProxiesWithoutEntry, int ErasedNames);

/// <summary>
/// What a distribution list is built from, read once per request (design D6): the day, the slots, the
/// validated orders, the catalogue's names and labels, the arquebusiers still in the registry and the
/// day's proxies.
/// </summary>
internal sealed record DistributionListData(
    int EditionYear,
    DistributionType Type,
    DateOnly Date,
    string Location,
    IReadOnlyDictionary<Guid, TimeOnly> Slots,
    IReadOnlyList<ExportedOrder> Orders,
    IReadOnlyDictionary<Guid, string> ComparsaNames,
    IReadOnlyDictionary<Guid, string> ModelLabels,
    IReadOnlyDictionary<Guid, RosterArquebusier> Arquebusiers,
    IReadOnlyList<ListProxy> Proxies)
{
    /// <summary>The type name only.</summary>
    public override string ToString() => nameof(DistributionListData);
}

/// <summary>
/// The powder and weapons distribution lists (spec: Distribution lists (UC-20); design D6), pure functions
/// of <see cref="DistributionListData"/>: one row per holder of the validated orders — the powder list's
/// <c>ACTIVE</c> entries with powder, the weapons list's <c>ACTIVE</c> rentals — numbered across the day,
/// with empty columns to fill in by hand and the proxy whose license holds on the day. No license,
/// contact or birth data (SEC-06).
/// </summary>
internal static class DistributionLists
{
    public const string Version = "1";

    public static string Name(DistributionType type) => type == DistributionType.Powder ? "powder-distribution-list" : "weapons-distribution-list";

    public static DocumentTable Build(DistributionListData data, DistributionTexts texts) => BuildWithCounts(data, texts).Table;

    public static BuiltList BuildWithCounts(DistributionListData data, DistributionTexts texts)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(texts);
        var entries = data.Orders.SelectMany(o => o.Entries.Select(e => (Order: o, Entry: e))).ToDictionary(x => x.Entry.EntryId);
        var proxies = data.Proxies.ToDictionary(p => p.HolderEntryId, p => p.ProxyEntryId);
        var holders = entries.Values
            .Where(x => Collects(x.Entry, data.Type))
            .Select(x => (x.Entry, Holder: Holder(data, texts, x.Order, x.Entry)))
            .ToDictionary(x => x.Holder.EntryId);
        var counts = new Counts();
        var rows = DistributionNumbering.Number(holders.Values.Select(h => h.Holder))
            .Select(numbered => Row(data, texts, numbered, holders[numbered.Holder.EntryId].Entry, ProxyOf(data, texts, entries, proxies, numbered.Holder.EntryId, counts), counts))
            .ToList();
        var name = Name(data.Type);
        var table = new DocumentTable(
            $"polvorapp-{data.EditionYear.ToString(CultureInfo.InvariantCulture)}-{name}",
            texts.Format(data.Type == DistributionType.Powder ? texts.PowderListTitle : texts.WeaponsListTitle, data.EditionYear),
            [texts.Format(texts.DayLine, DistributionTexts.Date(data.Date), data.Location), texts.NumberingNotice],
            texts.Format(texts.VersionLine, name, Version),
            Columns(data.Type, texts),
            rows,
            null);
        return new BuiltList(table, counts.WithoutLicense, counts.WithoutEntry, counts.Erased);
    }

    // An erased person collects nothing on a list (spec: Erased entries in distribution).
    private static bool Collects(ExportedEntry entry, DistributionType type) => entry.IsActive && !entry.Erased && type switch
    {
        DistributionType.Powder => entry.PowderKg > 0,
        DistributionType.Weapons => entry.WeaponSource == WeaponSource.Rental,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown distribution type."),
    };

    private static List<DocumentColumn> Columns(DistributionType type, DistributionTexts texts)
    {
        List<DocumentColumn> columns =
        [
            new(texts.Number, DocumentCellType.Number),
            new(texts.Slot, DocumentCellType.Text),
            new(texts.Comparsa, DocumentCellType.Text),
            new(texts.Name, DocumentCellType.Text),
            new(texts.NationalId, DocumentCellType.Text),
        ];
        columns.AddRange(type == DistributionType.Powder
            ?
            [
                new(texts.PowderKg, DocumentCellType.Number),
                new(texts.Flask, DocumentCellType.Text),
                new(texts.FlaskNumber, DocumentCellType.Text, ForHandwriting: true),
                new(texts.Traceability1, DocumentCellType.Text, ForHandwriting: true),
                new(texts.Traceability2, DocumentCellType.Text, ForHandwriting: true),
            ]
            :
            [
                new(texts.WeaponModel, DocumentCellType.Text),
                new(texts.WeaponNumber, DocumentCellType.Text, ForHandwriting: true),
            ]);
        columns.Add(new(texts.Proxy, DocumentCellType.Text));
        columns.Add(new(texts.ProxyNationalId, DocumentCellType.Text));
        return columns;
    }

    private static List<object?> Row(DistributionListData data, DistributionTexts texts, NumberedHolder numbered, ExportedEntry entry, Identity? proxy, Counts counts)
    {
        var holder = numbered.Holder;
        var identity = IdentityOf(data, texts, entry);
        counts.Erased += identity.Erased ? 1 : 0;
        List<object?> row =
        [
            numbered.Number,
            holder.Slot?.ToString("HH:mm", CultureInfo.InvariantCulture),
            holder.ComparsaName,
            identity.Name,
            identity.NationalId,
        ];
        if (data.Type == DistributionType.Powder)
        {
            row.AddRange([entry.PowderKg, texts.Flasks[entry.Flask], null, null, null]);
        }
        else
        {
            row.AddRange([Model(data, entry.RentalWeaponModelId), null]);
        }

        row.Add(proxy?.Name);
        row.Add(proxy?.NationalId);
        return row;
    }

    /// <summary>The holder's proxy, only while their license holds on the day (spec: Proxies that no longer hold).</summary>
    private static Identity? ProxyOf(
        DistributionListData data,
        DistributionTexts texts,
        Dictionary<Guid, (ExportedOrder Order, ExportedEntry Entry)> entries,
        Dictionary<Guid, Guid> proxies,
        Guid holderEntryId,
        Counts counts)
    {
        if (!proxies.TryGetValue(holderEntryId, out var proxyEntryId))
        {
            return null;
        }

        // A proxy erased after it was authorised (a race with the erasure) counts as missing.
        if (!entries.TryGetValue(proxyEntryId, out var proxy) || proxy.Entry.Erased)
        {
            counts.WithoutEntry++;
            return null;
        }

        var license = proxy.Entry.ArquebusierId is { } id && data.Arquebusiers.TryGetValue(id, out var arquebusier) ? arquebusier.License : null;
        if (!ProxyRules.LicenseHolds(license, data.Date))
        {
            counts.WithoutLicense++;
            return null;
        }

        return IdentityOf(data, texts, proxy.Entry);
    }

    private static ListHolder Holder(DistributionListData data, DistributionTexts texts, ExportedOrder order, ExportedEntry entry)
    {
        var identity = IdentityOf(data, texts, entry);
        var comparsa = data.ComparsaNames.TryGetValue(order.ComparsaId, out var name)
            ? name
            : throw new InvalidOperationException($"Comparsa {order.ComparsaId} of an order is missing from the catalogue.");
        TimeOnly? slot = data.Slots.TryGetValue(order.ComparsaId, out var startsAt) ? startsAt : null;
        return new ListHolder(entry.EntryId, order.ComparsaId, comparsa, slot, identity.LastName, identity.FirstName);
    }

    /// <summary>
    /// From the registry while the arquebusier is in it, from the entry's copy otherwise; a copy erased on a
    /// GDPR request prints the placeholder, never a blank.
    /// </summary>
    private static Identity IdentityOf(DistributionListData data, DistributionTexts texts, ExportedEntry entry)
    {
        if (entry.ArquebusierId is { } id && data.Arquebusiers.TryGetValue(id, out var live))
        {
            return new Identity(live.LastName, live.FirstName, live.NationalId, Erased: false);
        }

        var (last, first) = (entry.Person.LastName, entry.Person.FirstName);
        return string.IsNullOrWhiteSpace(last) && string.IsNullOrWhiteSpace(first)
            ? new Identity(texts.ErasedPerson, string.Empty, entry.Person.NationalId, Erased: true)
            : new Identity(last ?? string.Empty, first ?? string.Empty, entry.Person.NationalId, Erased: false);
    }

    /// <summary>A rented model's label; a model missing from the catalogue fails rather than print a blank.</summary>
    private static string Model(DistributionListData data, Guid? modelId) =>
        modelId is { } id && data.ModelLabels.TryGetValue(id, out var label)
            ? label
            : throw new InvalidOperationException($"Rented model {modelId} of an entry is missing from the catalogue.");

    /// <summary>What was left out or replaced while building one list.</summary>
    private sealed class Counts
    {
        public int WithoutLicense { get; set; }

        public int WithoutEntry { get; set; }

        public int Erased { get; set; }
    }

    private sealed record Identity(string LastName, string FirstName, string? NationalId, bool Erased)
    {
        public string Name => string.Join(", ", new[] { LastName, FirstName }.Where(part => part.Length > 0));

        public override string ToString() => nameof(Identity);
    }
}
