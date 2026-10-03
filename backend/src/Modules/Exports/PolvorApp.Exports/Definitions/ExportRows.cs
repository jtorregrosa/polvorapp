using System.Globalization;
using PolvorApp.ArquebusierRegistry.Contracts;
using PolvorApp.ComparsaOrders.Contracts;
using PolvorApp.SharedKernel.Codes;
using PolvorApp.SharedKernel.Text;

namespace PolvorApp.Exports.Definitions;

/// <summary>An entry with what every definition reads from it: who it is and in which comparsa.</summary>
internal sealed record ExportRow(ExportedEntry Entry, string Comparsa, string? LastName, string? FirstName, string? NationalId, int? FederationId)
{
    /// <summary>"Last name, First name", as lists sort and show them.</summary>
    public string Name => ExportRows.PersonName(LastName, FirstName);

    /// <summary>The type name only.</summary>
    public override string ToString() => nameof(ExportRow);
}

/// <summary>What the definitions share (design D3): who an entry is, its weapon, sorting and names.</summary>
internal static class ExportRows
{
    /// <summary>The version of every definition until its recipient's template arrives (Q-44).</summary>
    public const string ProvisionalVersion = "provisional-1";

    /// <summary>"Last name, First name"; the parts that are missing are left out.</summary>
    public static string PersonName(string? lastName, string? firstName) =>
        string.Join(", ", new[] { lastName, firstName }.Where(part => !string.IsNullOrWhiteSpace(part)));

    /// <summary>
    /// The entries of the orders with their person, sorted by comparsa and then by last and first
    /// name in Spanish order. The person comes from the registry while the arquebusier is in it, as
    /// the order page shows it, and from the entry's copy otherwise.
    /// </summary>
    public static List<ExportRow> Of(ExportData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return [.. data.Orders
            .SelectMany(order => order.Entries.Select(entry => Row(entry, Comparsa(data, order.ComparsaId), data)))
            .OrderBy(row => row.Comparsa, SpanishOrder.Names)
            .ThenBy(row => row.LastName ?? string.Empty, SpanishOrder.Names)
            .ThenBy(row => row.FirstName ?? string.Empty, SpanishOrder.Names)
            .ThenBy(row => row.Entry.EntryId)];
    }

    public static string Comparsa(ExportData data, Guid comparsaId) =>
        data.ComparsaNames.TryGetValue(comparsaId, out var name)
            ? name
            : throw new InvalidOperationException($"Comparsa {comparsaId} of an order is missing from the catalogue.");

    /// <summary>A weapon model's label; a model missing from the catalogue fails rather than export a blank.</summary>
    public static string? Model(ExportData data, Guid? modelId) =>
        modelId is not { } id ? null
        : data.ModelLabels.TryGetValue(id, out var label) ? label
        : throw new InvalidOperationException($"Weapon model {id} of an entry is missing from the catalogue.");

    /// <summary>The owned weapon from the registry while it is there, from the entry's copy otherwise.</summary>
    public static ExportedWeapon? OwnedWeapon(ExportData data, ExportedEntry entry)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.ArquebusierId is { } id && entry.OwnedWeaponId is { } weaponId
            && data.Arquebusiers.TryGetValue(id, out var live)
            && live.Weapons.FirstOrDefault(w => w.Id == weaponId) is { } weapon)
        {
            return new ExportedWeapon(weapon.WeaponModelId, weapon.WeaponNumber, weapon.OwnershipGuideNumber);
        }

        return entry.OwnedWeapon;
    }

    /// <summary>The license of an arquebusier still in the registry, or null.</summary>
    public static ArquebusierLicenseFacts? License(ExportData data, ExportedEntry entry) =>
        entry.ArquebusierId is { } id && data.Arquebusiers.TryGetValue(id, out var live) ? live.License : null;

    /// <summary>A license type as the Federation writes it: <c>AE</c>, <c>A-PROF</c>.</summary>
    public static string LicenseType(LicenseType type) => EnumCodes.ToCode(type).Replace('_', '-');

    /// <summary>The file name without extension (design D5).</summary>
    public static string FileStem(int year, string definition, bool provisional, string? comparsa = null, bool draft = false)
    {
        var parts = new List<string> { "polvorapp", year.ToString(CultureInfo.InvariantCulture), definition };
        if (comparsa is not null)
        {
            var slug = FileSlug.Of(comparsa);
            parts.Add(slug.Length > 0 ? slug : "comparsa");
        }

        if (draft)
        {
            parts.Add("draft");
        }

        if (provisional)
        {
            parts.Add("provisional");
        }

        return string.Join('-', parts);
    }

    /// <summary>The notices: first, for a draft, the order's status (spec: Comparsa list export); then the provisional one.</summary>
    public static List<string> Notices(ExportTexts texts, bool provisional, OrderStatus? draftStatus = null)
    {
        var notices = new List<string>();
        if (draftStatus is { } status)
        {
            notices.Add(texts.Format(texts.DraftNotice, texts.OrderStatuses[status]));
        }

        if (provisional)
        {
            notices.Add(texts.ProvisionalNotice);
        }

        return notices;
    }

    private static ExportRow Row(ExportedEntry entry, string comparsa, ExportData data) =>
        entry.ArquebusierId is { } id && data.Arquebusiers.TryGetValue(id, out var live)
            ? new ExportRow(entry, comparsa, live.LastName, live.FirstName, live.NationalId, live.FederationId)
            : new ExportRow(entry, comparsa, entry.Person.LastName, entry.Person.FirstName, entry.Person.NationalId, entry.Person.FederationId);
}
