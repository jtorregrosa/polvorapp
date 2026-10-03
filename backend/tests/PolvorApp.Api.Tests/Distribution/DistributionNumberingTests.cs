using PolvorApp.Distribution.Documents;

namespace PolvorApp.Api.Tests.Distribution;

/// <summary>Spec "Global numbering (UC-20)" (design D6): numbers from 1 across the day, comparsas by slot, people by name.</summary>
public sealed class DistributionNumberingTests
{
    private static readonly Guid Norte = Guid.CreateVersion7();
    private static readonly Guid Sur = Guid.CreateVersion7();
    private static readonly Guid Este = Guid.CreateVersion7();
    private static readonly Guid Oeste = Guid.CreateVersion7();

    [Fact]
    public void Comparsas_follow_their_slots_and_slotless_ones_come_last()
    {
        var numbered = DistributionNumbering.Number(
        [
            Holder(Este, "Comparsa Este", null, "Climent"),
            Holder(Sur, "Comparsa Sur", new TimeOnly(9, 30), "Bernabeu"),
            Holder(Norte, "Comparsa Norte", new TimeOnly(9, 0), "Zamora"),
            Holder(Norte, "Comparsa Norte", new TimeOnly(9, 0), "Abad"),
        ]);

        Assert.Equal([(1, "Abad"), (2, "Zamora"), (3, "Bernabeu"), (4, "Climent")], numbered.Select(n => (n.Number, n.Holder.LastName)));
    }

    [Fact]
    public void Comparsas_at_the_same_time_and_slotless_ones_follow_their_names()
    {
        var numbered = DistributionNumbering.Number(
        [
            Holder(Sur, "Ñora Sintética", null, "A"),
            Holder(Oeste, "Nubes Sintéticas", null, "B"),
            Holder(Este, "Zafra", new TimeOnly(10, 0), "C"),
            Holder(Norte, "Álamo", new TimeOnly(10, 0), "D"),
        ]);

        // Spanish order: Álamo before Zafra, Nubes before Ñora.
        Assert.Equal(["Álamo", "Zafra", "Nubes Sintéticas", "Ñora Sintética"], numbered.Select(n => n.Holder.ComparsaName));
    }

    [Fact]
    public void People_follow_the_spanish_order_of_last_and_first_name()
    {
        var numbered = DistributionNumbering.Number(
        [
            Holder(Norte, "Norte", null, "Ñúñez", "Ana"),
            Holder(Norte, "Norte", null, "Nuñez", "Berta"),
            Holder(Norte, "Norte", null, "Álvarez", "Carla"),
            Holder(Norte, "Norte", null, "Álvarez", "Ana"),
        ]);

        Assert.Equal([("Álvarez", "Ana"), ("Álvarez", "Carla"), ("Nuñez", "Berta"), ("Ñúñez", "Ana")], numbered.Select(n => (n.Holder.LastName, n.Holder.FirstName)));
    }

    [Fact]
    public void A_new_holder_renumbers_everyone_after_them()
    {
        ListHolder[] before = [Holder(Norte, "Norte", new TimeOnly(9, 0), "Abad"), Holder(Sur, "Sur", new TimeOnly(9, 30), "Bernabeu")];

        var after = DistributionNumbering.Number([.. before, Holder(Norte, "Norte", new TimeOnly(9, 0), "Alcaraz")]);

        Assert.Equal([(1, "Abad"), (2, "Alcaraz"), (3, "Bernabeu")], after.Select(n => (n.Number, n.Holder.LastName)));
    }

    [Fact]
    public void No_holders_number_nothing() => Assert.Empty(DistributionNumbering.Number([]));

    private static ListHolder Holder(Guid comparsa, string comparsaName, TimeOnly? slot, string lastName, string firstName = "Arcabucero") =>
        new(Guid.CreateVersion7(), comparsa, comparsaName, slot, lastName, firstName);
}
