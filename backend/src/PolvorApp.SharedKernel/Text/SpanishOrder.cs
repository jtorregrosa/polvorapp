using System.Globalization;

namespace PolvorApp.SharedKernel.Text;

/// <summary>
/// Order of names and labels in lists ("sorted by name"): Spanish linguistic order ignoring case,
/// where accents only break ties (primary-level order), so "Álamo" sorts next to "Alamo" and not
/// after "Zeta", whatever the database collation. Lists are small (tens to a few hundred rows),
/// so they are sorted in memory.
/// </summary>
public static class SpanishOrder
{
    public static readonly StringComparer Names = StringComparer.Create(CultureInfo.GetCultureInfo("es-ES"), CompareOptions.IgnoreCase);
}
