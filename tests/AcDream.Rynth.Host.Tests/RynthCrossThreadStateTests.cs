using System.Runtime.InteropServices;
using AcDream.Rynth.EngineUi;
using AcDream.Rynth.Host;
using RynthCore.Engine.Compatibility;
using RynthCore.PluginSdk;

namespace AcDream.Rynth.Host.Tests;

[Collection("Rynth")]
public sealed unsafe class RynthCrossThreadStateTests : IDisposable
{
    private const uint RunForward = 0x41000003;
    private const uint VendorId = 0x80000700, Sword = 0x80000701;
    private readonly List<string> _log = new();

    public void Dispose()
    {
        RynthFrameThread.Reset();
        RynthFrameActions.Queue = null;
        AcMainThreadQueue.WriteToChat = null;
        AcMainThreadQueue.SelectObject = null;
    }

    private static void OnOtherThread(Action work)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { work(); }
            catch (Exception error) { failure = error; }
        });
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw new Xunit.Sdk.XunitException($"other thread threw: {failure}");
    }

    [Fact]
    public void EventsQueuedWhileDeliveringAreNeitherLostNorFatal()
    {
        int delivered = 0;
        var bridge = new RynthEventBridge([], _ => { });
        const int count = 10_000, producers = 4;
        using var start = new Barrier(producers + 1);
        Thread[] threads = [.. Enumerable.Range(0, producers).Select(_ => new Thread(() =>
        {
            start.SignalAndWait();
            for (int i = 0; i < count; i++)
                bridge.Enqueue(RynthEventBridge.EventKind.ChatWindowText, () => Interlocked.Increment(ref delivered));
        }))];
        foreach (Thread thread in threads)
            thread.Start();
        start.SignalAndWait();
        while (threads.Any(thread => thread.IsAlive))
            bridge.DeliverQueued();
        foreach (Thread thread in threads)
            thread.Join();
        bridge.DeliverQueued();
        Assert.Equal(count * producers, delivered);
    }

    [Fact]
    public void OffTheFrameThreadADeliveryNeverReordersEventsQueuedDuringIt()
    {
        var seen = new List<string>();
        void Saw(string what) { lock (seen) seen.Add(what); }
        var bridge = new RynthEventBridge([], _log.Add);
        using var running = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        bridge.Enqueue(RynthEventBridge.EventKind.CreateObject, () =>
        {
            Saw("create 1");
            running.Set();
            release.Wait(TimeSpan.FromSeconds(5));
        });
        RynthFrameThread.Latch();

        var pump = new Thread(bridge.DeliverQueued);
        pump.Start();
        Assert.True(running.Wait(TimeSpan.FromSeconds(5)));
        // The frame thread queues a create and then a delete of the same object while the pump is
        // inside the create kind: neither joins this pass, so the delete can't overtake the create.
        bridge.Enqueue(RynthEventBridge.EventKind.CreateObject, () => Saw("create 2"));
        bridge.Enqueue(RynthEventBridge.EventKind.DeleteObject, () => Saw("delete 2"));
        release.Set();
        pump.Join();
        lock (seen)
            Assert.Equal(["create 1"], seen);

        OnOtherThread(bridge.DeliverQueued);
        lock (seen)
            Assert.Equal(["create 1", "create 2", "delete 2"], seen);
        Assert.Empty(_log);
    }

    [Fact]
    public void AStringSlotKeepsEachThreadsBufferApart()
    {
        var view = new FakeGameView();
        view.Names[1] = "Alpha Drudge Skulker";
        view.Names[2] = "Bravo Mosswart Elder";
        using var table = new RynthApiTable(view, _log.Add);
        var getName = (delegate* unmanaged[Cdecl]<uint, IntPtr>)table.Native->GetObjectNameFn;

        // Deterministic: a call on another thread must not free the pointer this thread holds.
        IntPtr mine = getName(1);
        IntPtr theirs = IntPtr.Zero;
        OnOtherThread(() => theirs = getName(2));
        Assert.NotEqual(mine, theirs);
        Assert.Equal("Alpha Drudge Skulker", Marshal.PtrToStringAnsi(mine));
        string? theirText = null;
        OnOtherThread(() => theirText = Marshal.PtrToStringAnsi(theirs));
        Assert.Equal("Bravo Mosswart Elder", theirText);

        // Concurrent: each thread always reads its own name.
        int wrong = 0;
        using var start = new Barrier(2);
        Thread Reader(uint id, string expected) => new(() =>
        {
            start.SignalAndWait();
            for (int i = 0; i < 1_000; i++)
            {
                IntPtr text = getName(id);
                if (Marshal.PtrToStringAnsi(text) != expected)
                    Interlocked.Increment(ref wrong);
            }
        });
        Thread a = Reader(1, "Alpha Drudge Skulker");
        Thread b = Reader(2, "Bravo Mosswart Elder");
        a.Start();
        b.Start();
        a.Join();
        b.Join();
        Assert.Equal(0, wrong);
    }

    [Fact]
    public void InterfaceFailuresAreCountedUnderALock()
    {
        // GetPluginInterface with no plugins never throws; the lock is exercised through the slot
        // by many threads at once, which must neither throw nor log.
        using var table = new RynthApiTable(new FakeGameView(), _log.Add);
        var getInterface = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, IntPtr>)table.Native->GetPluginInterfaceFn;
        IntPtr name = Marshal.StringToHGlobalAnsi("RynthNav");
        IntPtr iface = Marshal.StringToHGlobalAnsi("RynthNav.Path");
        try
        {
            var threads = Enumerable.Range(0, 4).Select(_ => new Thread(() =>
            {
                for (int i = 0; i < 200; i++)
                    getInterface(name, iface, 1);
            })).ToArray();
            foreach (Thread thread in threads)
                thread.Start();
            foreach (Thread thread in threads)
                thread.Join();
        }
        finally
        {
            Marshal.FreeHGlobal(name);
            Marshal.FreeHGlobal(iface);
        }

        Assert.Empty(_log);
    }

    [Fact]
    public void OffTheFrameThreadMotionSlotsQueueForTheFrame()
    {
        var view = new FakeGameView();
        using var table = new RynthApiTable(view, _log.Add);
        var queue = new RynthActionQueue(_log.Add, () => false);
        table.Actions = queue;
        var setMotion = (delegate* unmanaged[Cdecl]<uint, int, int>)table.Native->SetMotionFn;
        var doMovement = (delegate* unmanaged[Cdecl]<uint, float, int, int>)table.Native->DoMovementFn;
        var stopMovement = (delegate* unmanaged[Cdecl]<uint, int, int>)table.Native->StopMovementFn;
        var setAutoRun = (delegate* unmanaged[Cdecl]<int, int>)table.Native->SetAutoRunFn;
        var stopCompletely = (delegate* unmanaged[Cdecl]<int>)table.Native->StopCompletelyFn;
        RynthFrameThread.Latch();

        int[] results = new int[5];
        OnOtherThread(() =>
        {
            results[0] = setMotion(RunForward, 1);
            results[1] = doMovement(RunForward, 1f, 0);
            results[2] = stopMovement(RunForward, 0);
            results[3] = setAutoRun(1);
            results[4] = stopCompletely();
        });

        Assert.Equal([1, 1, 1, 1, 1], results);
        Assert.Empty(view.Intents);                // nothing touched the holds off the frame
        Assert.Empty(view.Actions);
        Assert.Equal(5, queue.Drain());            // the frame runs them, in order
        Assert.Equal(new AcDream.Plugin.Abstractions.PluginMovementIntent(Forward: true, Run: true), view.Intents[0]);
        Assert.Contains("autorun 1", view.Actions);
        Assert.Equal("stop", view.Actions[^1]);
        Assert.Empty(_log);
    }

    [Fact]
    public void OffTheFrameThreadARefusedStartIsNotQueuedButAReleaseIs()
    {
        var view = new FakeGameView();
        using var table = new RynthApiTable(view, _log.Add);
        var queue = new RynthActionQueue(_log.Add, blocked: () => true);
        table.Actions = queue;
        var setMotion = (delegate* unmanaged[Cdecl]<uint, int, int>)table.Native->SetMotionFn;
        RynthFrameThread.Latch();

        int start = -1, release = -1;
        OnOtherThread(() =>
        {
            start = setMotion(RunForward, 1);
            release = setMotion(RunForward, 0);
        });

        Assert.Equal(0, start);                    // starts are refused while blocked
        Assert.Equal(1, release);                  // a release is a stop: it is queued
        Assert.Equal(1, queue.Drain());
    }

    [Fact]
    public void OnTheFrameThreadMotionSlotsActAtOnce()
    {
        var view = new FakeGameView();
        using var table = new RynthApiTable(view, _log.Add);
        var queue = new RynthActionQueue(_log.Add, () => false);
        table.Actions = queue;
        var setMotion = (delegate* unmanaged[Cdecl]<uint, int, int>)table.Native->SetMotionFn;
        RynthFrameThread.Latch();

        Assert.Equal(1, setMotion(RunForward, 1));

        Assert.Equal(new AcDream.Plugin.Abstractions.PluginMovementIntent(Forward: true, Run: true), view.Intents[^1]);
        Assert.Equal(0, queue.Drain());
    }

    [Fact]
    public void VendorTradesFailQueuedEndsTheRequest()
    {
        var view = new FakeGameView { PlayerId = 0x50000001, OpenVendor = Shop() };
        using var trades = new RynthVendorTrades(view);
        uint id = trades.Buy(0, [(Sword, 1)]);
        Assert.NotEqual(0u, id);
        Assert.True(trades.InFlight);

        trades.FailQueued("the queue was full");

        Assert.False(trades.InFlight);
        Assert.Equal(RynthVendorTrades.StateDone, trades.Status.State);
        Assert.Equal(RynthVendorTrades.ResultAnsweredNoRefresh, trades.Status.Result);
        Assert.Contains("would not send", trades.Status.Message, StringComparison.Ordinal);
        Assert.Contains("the queue was full", trades.Status.Message, StringComparison.Ordinal);

        trades.FailQueued("again");                // nothing in flight: no change
        Assert.Contains("the queue was full", trades.Status.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MainThreadGuardFollowsTheLatchedFrameThread()
    {
        Assert.True(MainThreadGuard.IsOnMainThread());          // not latched: every thread is the main one
        bool off = true;
        OnOtherThread(() => off = MainThreadGuard.IsOnMainThread());
        Assert.True(off);

        RynthFrameThread.Latch();
        Assert.True(MainThreadGuard.IsOnMainThread());
        OnOtherThread(() => off = MainThreadGuard.IsOnMainThread());
        Assert.False(off);
    }

    [Fact]
    public void TheMainThreadQueueStandInRunsOnTheFrameAndQueuesElsewhere()
    {
        var lines = new List<string>();
        var selected = new List<uint>();
        AcMainThreadQueue.WriteToChat = (text, _) => lines.Add(text);
        AcMainThreadQueue.SelectObject = id => { selected.Add(id); return true; };
        var queue = new RynthActionQueue(_log.Add, () => false);
        RynthFrameActions.Queue = queue;
        RynthFrameThread.Latch();

        Assert.True(AcMainThreadQueue.EnqueueWriteToChat("now", 1));
        Assert.True(AcMainThreadQueue.EnqueueSetSelectedObject(7));
        Assert.Equal(["now"], lines);
        Assert.Equal([7u], selected);

        bool wrote = false, chose = false, clicked = false;
        OnOtherThread(() =>
        {
            wrote = AcMainThreadQueue.EnqueueWriteToChat("later", 1);
            chose = AcMainThreadQueue.EnqueueSetSelectedObject(8);
            clicked = AcMainThreadQueue.EnqueueOverlaySelect(9);
        });
        Assert.True(wrote && chose && clicked);
        Assert.Equal(["now"], lines);              // not run off the frame
        Assert.Equal([7u], selected);

        Assert.Equal(3, queue.Drain());
        Assert.Equal(["now", "later"], lines);
        Assert.Equal([7u, 8u, 9u], selected);
    }

    private static RynthVendor Shop() => new(
        VendorId, "Thorsten the Weaponsmith", 0x1u, 0, 10_000, true, 0.9f, 1.3f,
        0, "", 0, -1, 1_234,
        [
            new(Sword, 30, "Sword", 1, 0x06001234, Amount: 5, StackSize: 1, MaxStackSize: 0, Value: 100, UnitValue: 100,
                UnitPrice: 130, Burden: 200, NeedsContainerSlot: false),
        ]);
}
