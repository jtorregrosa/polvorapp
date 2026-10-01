using PolvorApp.SharedKernel.Text;

namespace PolvorApp.Api.Tests.SharedKernel;

/// <summary>"Sorted by name" in Spanish order, whatever the database collation (add-federation-catalog design D4).</summary>
public sealed class SpanishOrderTests
{
    [Fact]
    public void Accented_and_lowercase_names_sort_next_to_their_plain_forms()
    {
        string[] names = ["Zeta", "álamo", "Alamo", "Ñora", "Nora", "Oliva", "Álamo"];

        Assert.Equal(["Alamo", "álamo", "Álamo", "Nora", "Ñora", "Oliva", "Zeta"], names.Order(SpanishOrder.Names));
    }
}
