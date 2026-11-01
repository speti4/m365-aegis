using M365Aegis.Core.Snapshots;

namespace M365Aegis.Core.Tests.Store;

/// <summary>The take's adaptive gate (task 117): the ceiling, the halving on a throttled release, the growth after a clean round.</summary>
public sealed class AdaptiveGateTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AThrottledReleaseHalvesTheLimitByHoldingPermitsBackAndACleanRoundGrowsIt()
    {
        using var gate = new AdaptiveGate(8);
        Task[] running = [.. Enumerable.Range(0, 8).Select(_ => gate.WaitAsync(Ct))];
        Task ninth = gate.WaitAsync(Ct);

        Assert.All(running, wait => Assert.True(wait.IsCompletedSuccessfully));
        Assert.False(ninth.IsCompleted);
        Assert.Equal((8, 8, 8), (gate.Max, gate.Limit, gate.Lowest));

        // One throttled answer: the limit halves, and the permit stays out instead of letting the ninth in.
        gate.Release(throttled: true);
        Assert.Equal((4, 4), (gate.Limit, gate.Lowest));
        Assert.False(ninth.IsCompleted);

        // Three clean releases bring the reads in flight down to the limit; the ninth still waits.
        gate.Release(throttled: false);
        gate.Release(throttled: false);
        gate.Release(throttled: false);
        Assert.Equal(4, gate.Limit);
        Assert.False(ninth.IsCompleted);

        // The fourth clean release is a round at the limit: the limit grows to five, the ninth and one more may run.
        gate.Release(throttled: false);
        await ninth.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Equal((5, 4), (gate.Limit, gate.Lowest));
        Task tenth = gate.WaitAsync(Ct);
        Task eleventh = gate.WaitAsync(Ct);
        Assert.True(tenth.IsCompletedSuccessfully);
        Assert.False(eleventh.IsCompleted);
    }

    [Fact]
    public async Task TheLimitNeverFallsBelowOneAndNeverGrowsPastTheCeiling()
    {
        using var gate = new AdaptiveGate(2);
        await gate.WaitAsync(Ct);
        await gate.WaitAsync(Ct);

        gate.Release(throttled: true);
        gate.Release(throttled: true);
        Assert.Equal((1, 1), (gate.Limit, gate.Lowest));

        // One read at a time now; each clean release is a round, so the limit is back at two after one.
        await gate.WaitAsync(Ct);
        Task second = gate.WaitAsync(Ct);
        Assert.False(second.IsCompleted);
        gate.Release(throttled: false);
        await second.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Equal(2, gate.Limit);
        gate.Release(throttled: false);
        Assert.Equal((2, 1), (gate.Limit, gate.Lowest));
        Task third = gate.WaitAsync(Ct);
        Task fourth = gate.WaitAsync(Ct);
        Task fifth = gate.WaitAsync(Ct);
        Assert.True(third.IsCompletedSuccessfully && fourth.IsCompletedSuccessfully);
        Assert.False(fifth.IsCompleted);
    }

    [Fact]
    public void ACeilingBelowOneIsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new AdaptiveGate(0));
}
