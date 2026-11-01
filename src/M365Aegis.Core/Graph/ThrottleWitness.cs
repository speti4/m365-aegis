namespace M365Aegis.Core.Graph;

/// <summary>
/// Counts the throttled answers (a 429 or a 503, retried or not) of the calls made under one logical read (task 117):
/// the snapshot's take begins one per mailbox read, and the <see cref="Snapshots.AdaptiveGate"/> backs off when a
/// read's count is above zero. The witness flows with the async context (<see cref="AsyncLocal{T}"/>), so reads
/// running at once each see their own calls and none of the others', which a shared counter could not tell apart; a
/// call made under no witness is counted nowhere.
/// </summary>
internal sealed class ThrottleWitness
{
    private static readonly AsyncLocal<ThrottleWitness?> Current = new();
    private int _count;

    private ThrottleWitness()
    {
    }

    /// <summary>How many throttled answers the calls under this witness got so far.</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>Begins a witness for the calls that follow in this async flow (the caller's own flow, not its caller's).</summary>
    public static ThrottleWitness Begin()
    {
        var witness = new ThrottleWitness();
        Current.Value = witness;
        return witness;
    }

    /// <summary>A client's note of a throttled answer; nothing happens without a witness in the flow.</summary>
    public static void Note()
    {
        if (Current.Value is { } witness)
        {
            Interlocked.Increment(ref witness._count);
        }
    }
}
