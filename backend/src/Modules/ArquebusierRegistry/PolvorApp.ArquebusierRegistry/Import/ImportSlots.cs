namespace PolvorApp.ArquebusierRegistry.Import;

/// <summary>
/// Lets at most two workbooks be read at a time across the API (design D9): the rate limit bounds
/// how often one Admin uploads, not how many loads run together, and each load builds a large model
/// in memory. A request that waits too long for a slot is answered as busy.
/// </summary>
internal sealed class ImportSlots : IDisposable
{
    public const int Count = 2;

    public static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _slots = new(Count, Count);
    private readonly TimeSpan _wait;

    public ImportSlots()
        : this(DefaultWait)
    {
    }

    /// <summary>For tests: a shorter wait than the 5 s the API uses.</summary>
    internal ImportSlots(TimeSpan wait) => _wait = wait;

    /// <summary>A slot to release when the reading is done, or null when none freed up in time.</summary>
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
