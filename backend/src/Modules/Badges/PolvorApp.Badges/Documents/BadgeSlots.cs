namespace PolvorApp.Badges.Documents;

/// <summary>
/// Lets at most two badge sheets be built at a time across the API (design D3): the rate limit bounds
/// how often one Admin downloads, not how many sheets of up to 200 photos are in memory together. A
/// request that waits too long for a slot is answered as busy, as the registry import does.
/// </summary>
internal sealed class BadgeSlots : IDisposable
{
    public const int Count = 2;

    public static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _slots = new(Count, Count);
    private readonly TimeSpan _wait;

    public BadgeSlots()
        : this(DefaultWait)
    {
    }

    /// <summary>For tests: a shorter wait than the 10 s the API uses.</summary>
    internal BadgeSlots(TimeSpan wait) => _wait = wait;

    /// <summary>A slot to release when the sheet is built, or null when none freed up in time.</summary>
    public async Task<IDisposable?> EnterAsync(CancellationToken cancellationToken) =>
        await _slots.WaitAsync(_wait, cancellationToken) ? new Slot(_slots) : null;

    public void Dispose() => _slots.Dispose();

    private sealed class Slot(SemaphoreSlim slots) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                slots.Release();
            }
        }
    }
}
