using PolvorApp.Distribution.Days;

namespace PolvorApp.Api.Tests.Distribution;

/// <summary>Spec "Distribution slots": the set saved replaces the previous one; the diff drives the save and the audit (design D8).</summary>
public sealed class SlotDiffTests
{
    private static readonly Guid Norte = Guid.CreateVersion7();
    private static readonly Guid Sur = Guid.CreateVersion7();
    private static readonly Guid Este = Guid.CreateVersion7();

    [Fact]
    public void Added_moved_and_removed_comparsas_are_told_apart()
    {
        var diff = SlotDiff.Compute(
            new Dictionary<Guid, TimeOnly> { [Norte] = new(9, 0), [Sur] = new(9, 30) },
            [new SlotInput(Norte, new TimeOnly(10, 0)), new SlotInput(Este, new TimeOnly(11, 0))]);

        Assert.Equal([new SlotInput(Este, new TimeOnly(11, 0))], diff.Added);
        Assert.Equal([new SlotInput(Norte, new TimeOnly(10, 0))], diff.Moved);
        Assert.Equal([Sur], diff.Removed);
        Assert.Equal(
            new[] { (Norte, (string?)"09:00", (string?)"10:00"), (Sur, "09:30", null), (Este, null, "11:00") }.OrderBy(c => c.Item1),
            diff.Changes.Select(c => (c.ComparsaId, c.Previous, c.Current)));
    }

    [Fact]
    public void The_same_set_is_no_change()
    {
        var diff = SlotDiff.Compute(new Dictionary<Guid, TimeOnly> { [Norte] = new(9, 0) }, [new SlotInput(Norte, new TimeOnly(9, 0))]);

        Assert.True(diff.IsEmpty);
        Assert.Empty(diff.Changes);
    }

    [Fact]
    public void An_empty_set_removes_every_slot()
    {
        var diff = SlotDiff.Compute(new Dictionary<Guid, TimeOnly> { [Norte] = new(9, 0), [Sur] = new(9, 30) }, []);

        Assert.Equal(new[] { Norte, Sur }.Order(), diff.Removed.Order());
        Assert.Empty(diff.Added);
    }
}
