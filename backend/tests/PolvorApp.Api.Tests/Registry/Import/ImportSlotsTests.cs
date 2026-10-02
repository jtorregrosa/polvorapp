using PolvorApp.ArquebusierRegistry.Import;

namespace PolvorApp.Api.Tests.Registry.Import;

/// <summary>Design D9: at most two workbooks are read at a time, and every slot is given back.</summary>
public sealed class ImportSlotsTests
{
    private static readonly TimeSpan ShortWait = TimeSpan.FromMilliseconds(50);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_slots_are_handed_out_and_a_third_waits_in_vain()
    {
        using var slots = new ImportSlots(ShortWait);

        using var first = await slots.EnterAsync(Token);
        using var second = await slots.EnterAsync(Token);
        var third = await slots.EnterAsync(Token);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Null(third);
    }

    [Fact]
    public async Task A_released_slot_can_be_taken_again()
    {
        using var slots = new ImportSlots(ShortWait);
        using var kept = await slots.EnterAsync(Token);
        var released = await slots.EnterAsync(Token);

        released!.Dispose();

        using var again = await slots.EnterAsync(Token);
        Assert.NotNull(again);
    }

    [Fact]
    public async Task Releasing_a_slot_twice_gives_back_only_one()
    {
        using var slots = new ImportSlots(ShortWait);
        var slot = await slots.EnterAsync(Token);

        slot!.Dispose();
        slot.Dispose();

        using var first = await slots.EnterAsync(Token);
        using var second = await slots.EnterAsync(Token);
        Assert.Null(await slots.EnterAsync(Token));
    }

    [Fact]
    public async Task A_cancelled_wait_takes_no_slot()
    {
        using var slots = new ImportSlots(TimeSpan.FromSeconds(30));
        var first = await slots.EnterAsync(Token);
        using var second = await slots.EnterAsync(Token);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => slots.EnterAsync(cancellation.Token));

        first!.Dispose();
        using var third = await slots.EnterAsync(Token);
        Assert.NotNull(third);
    }
}
