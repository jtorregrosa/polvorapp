using PolvorApp.FederationCatalog;

namespace PolvorApp.Api.Tests.Catalog;

/// <summary>"Sorted by name" in Spanish order, whatever the database collation (design D4).</summary>
public sealed class CatalogOrderTests
{
    [Fact]
    public void Accented_and_lowercase_names_sort_next_to_their_plain_forms()
    {
        string[] names = ["Zeta", "álamo", "Alamo", "Ñora", "Nora", "Oliva", "Álamo"];

        Assert.Equal(["Alamo", "álamo", "Álamo", "Nora", "Ñora", "Oliva", "Zeta"], names.Order(CatalogOrder.Names));
    }
}
