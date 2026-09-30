using System.Globalization;

namespace PolvorApp.FederationCatalog;

/// <summary>
/// Order of names and labels in lists ("sorted by name"): Spanish linguistic order ignoring case,
/// where accents only break ties (primary-level order), so "Álamo" sorts next to "Alamo" and not
/// after "Zeta", whatever the database collation. Catalogue lists are small (tens of rows), so
/// they are sorted in memory.
/// </summary>
internal static class CatalogOrder
{
    public static readonly StringComparer Names = StringComparer.Create(CultureInfo.GetCultureInfo("es-ES"), CompareOptions.IgnoreCase);
}
