using AcDream.Rynth.EngineUi;

namespace AcDream.Rynth.Host.Tests;

[Collection("Rynth")]
public sealed class RynthActionQueueTests : IDisposable
{
    private readonly List<string> _log = [];
    private bool _blocked;
    private long _now;

    public void Dispose() => RynthFrameThread.Reset();

    private RynthActionQueue NewQueue() => new(_log.Add, () => _blocked, () => _now);

    [Fact]
    public void TheFrameThreadIsTheOneThatLatched()
    {
        Assert.False(RynthFrameThread.IsCurrent);
        RynthFrameThread.Latch();
        Assert.True(RynthFrameThread.IsCurrent);
        bool other = true;
        var t = new Thread(() => other = RynthFrameThread.IsCurrent);
        t.Start();
        t.Join();
        Assert.False(other);
    }

    [Fact]
    public void EntriesRunInOrderWhenDrained()
    {
        var queue = NewQueue();
        var ran = new List<int>();
        Assert.True(queue.TryEnqueue(RynthActionKind.Start, "a", () => { ran.Add(1); return true; }));
        Assert.True(queue.TryEnqueue(RynthActionKind.Stop, "b", () => { ran.Add(2); return true; }));
        Assert.Empty(ran);
        Assert.Equal(2, queue.Drain());
        Assert.Equal([1, 2], ran);
        Assert.Equal(0, queue.Drain());
    }

    [Fact]
    public void StartsAreRefusedWhilePortallingOrDeadButStopsAreTaken()
    {
        var queue = NewQueue();
        _blocked = true;
        Assert.False(queue.TryEnqueue(RynthActionKind.Start, "use", () => true));
        Assert.True(queue.TryEnqueue(RynthActionKind.Stop, "stop", () => true));
        Assert.Equal(1, queue.Drain());
    }

    [Fact]
    public void TheReserveKeepsRoomForStops()
    {
        var queue = NewQueue();
        for (int i = 0; i < RynthActionQueue.Capacity - RynthActionQueue.StopReserve; i++)
            Assert.True(queue.TryEnqueue(RynthActionKind.Start, "s", () => true));
        Assert.False(queue.TryEnqueue(RynthActionKind.Start, "s", () => true));
        for (int i = 0; i < RynthActionQueue.StopReserve; i++)
            Assert.True(queue.TryEnqueue(RynthActionKind.Stop, "x", () => true));
        Assert.False(queue.TryEnqueue(RynthActionKind.Stop, "x", () => true));
        Assert.Equal(2, queue.Dropped);
    }

    [Fact]
    public void AFailingEntryIsLoggedOnceAndTheDrainGoesOn()
    {
        var queue = NewQueue();
        bool after = false;
        queue.TryEnqueue(RynthActionKind.Start, "use", () => throw new InvalidOperationException("boom"));
        queue.TryEnqueue(RynthActionKind.Start, "use", () => false);
        queue.TryEnqueue(RynthActionKind.Start, "select", () => after = true);
        queue.Drain();
        Assert.True(after);
        Assert.Single(_log, l => l.Contains("use", StringComparison.Ordinal));
        _now += 10_000;
        queue.TryEnqueue(RynthActionKind.Start, "use", () => false);
        queue.Drain();
        Assert.Equal(2, _log.Count(l => l.Contains("use", StringComparison.Ordinal)));
    }
}
