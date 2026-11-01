namespace M365Aegis.Core.Snapshots;

/// <summary>
/// A bounded-concurrency gate that backs off (task 117): it lets <see cref="Max"/> reads run at once, halves the limit
/// (to one at the least) when a read reports a throttled answer (its <see cref="Graph.ThrottleWitness"/> counted one, or
/// its failure was the throttling itself), and grows it by one after a round of
/// <see cref="Limit"/> reads without one, up to the ceiling again. The limit shrinks by keeping released permits out
/// of circulation instead of taking any, so a shrink never waits on the reads in flight.
/// </summary>
/// <param name="max">The ceiling, at least one.</param>
internal sealed class AdaptiveGate(int max) : IDisposable
{
    private readonly SemaphoreSlim _permits = new(Positive(max), Positive(max));
    private readonly Lock _lock = new();
    private int _limit = Positive(max);
    private int _lowest = Positive(max);
    private int _heldBack;
    private int _streak;

    /// <summary>The ceiling.</summary>
    public int Max { get; } = Positive(max);

    /// <summary>How many reads may run at once now.</summary>
    public int Limit
    {
        get
        {
            lock (_lock)
            {
                return _limit;
            }
        }
    }

    /// <summary>The lowest the limit has been; <see cref="Max"/> when no read was throttled.</summary>
    public int Lowest
    {
        get
        {
            lock (_lock)
            {
                return _lowest;
            }
        }
    }

    /// <summary>Waits for a permit; every wait is followed by one <see cref="Release"/>.</summary>
    public Task WaitAsync(CancellationToken cancellationToken) => _permits.WaitAsync(cancellationToken);

    /// <summary>Gives the permit back; a throttled read halves the limit, a clean round grows it by one.</summary>
    /// <param name="throttled">Whether the read's answer was throttled (a 429 or a 503, retried or not).</param>
    public void Release(bool throttled)
    {
        lock (_lock)
        {
            if (throttled)
            {
                _limit = Math.Max(1, _limit / 2);
                _lowest = Math.Min(_lowest, _limit);
                _streak = 0;
            }
            else if (++_streak >= _limit && _limit < Max)
            {
                _limit++;
                _streak = 0;
            }

            // The permits kept out of the semaphore are what the limit is short of the ceiling: this one stays out while
            // too few are, else it goes back along with any the limit no longer needs out.
            int keepOut = Max - _limit;
            if (_heldBack < keepOut)
            {
                _heldBack++;
                return;
            }

            _permits.Release();
            while (_heldBack > keepOut)
            {
                _heldBack--;
                _permits.Release();
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _permits.Dispose();

    private static int Positive(int max) => max < 1 ? throw new ArgumentOutOfRangeException(nameof(max), max, "the ceiling is at least one") : max;
}
