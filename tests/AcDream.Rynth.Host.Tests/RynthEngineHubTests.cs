using AcDream.Rynth.EngineUi;
using RynthCore.Engine.UI.Data;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthEngineHubTests : IDisposable
{
    private readonly List<string> _log = new();

    public RynthEngineHubTests()
    {
        RynthEngineHub.LeaveAll();
        UiDataHub.Clear();
        RynthEngineHub.Log = _log.Add;
    }

    public void Dispose()
    {
        RynthEngineHub.LeaveAll();
        RynthEngineHub.Log = _ => { };
    }

    private sealed class ProbeSource(string name, bool throws = false) : UiSource(name, 1000)
    {
        public int Polls, Resets;

        protected internal override void Poll()
        {
            Polls++;
            if (throws)
                throw new InvalidOperationException("boom");
        }

        protected internal override void Reset()
        {
            Resets++;
            base.Reset();
        }
    }

    [Fact]
    public void ASourceThatThrowsDoesNotStopTheOthersOrRepeatEveryTick()
    {
        var bad = new ProbeSource("Bad", throws: true);
        var good = new ProbeSource("Good");
        bad.Subscribe();
        good.Subscribe();

        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        RynthEngineHub.PollAll([bad, good], now);
        RynthEngineHub.PollAll([bad, good], now);

        Assert.Equal(1, good.Polls);
        Assert.Equal(1, bad.Polls);
        Assert.Single(_log, line => line == "[Rynth] Bad poll: InvalidOperationException: boom");
    }

    [Fact]
    public void NothingRunsWithNoParts()
    {
        bool ran = false;
        UiDataHub.Post("x", () => ran = true);
        Assert.Equal(0, RynthEngineHub.Tick());
        Assert.False(ran);
    }

    [Fact]
    public void ATickRunsHooksDrainsAndPollsEachSharedSourceOnce()
    {
        var shared = new ProbeSource("Shared");
        var own = new ProbeSource("Own");
        shared.Subscribe();
        own.Subscribe();
        var order = new List<string>();
        RynthEngineHub.Join(new RynthEngineHub.Part("A", [shared], BeforeDrain: () => order.Add("A before"), AfterDrain: () => order.Add("A after")));
        RynthEngineHub.Join(new RynthEngineHub.Part("A", [own]));   // same name: ignored
        RynthEngineHub.Join(new RynthEngineHub.Part("B", [shared, own], BeforeDrain: () => order.Add("B before")));
        UiDataHub.Post("posted", () => order.Add("posted"));

        Assert.Equal(1, RynthEngineHub.Tick());

        Assert.Equal(["A before", "B before", "posted", "A after"], order);
        Assert.Equal(1, shared.Polls);
        Assert.Equal(1, own.Polls);
    }

    [Fact]
    public void LeavingResetsThePartsSourcesAndTheLastLeaveClearsTheQueue()
    {
        var a = new ProbeSource("A1");
        var b = new ProbeSource("B1");
        int unloaded = 0;
        void OnUnloaded() => unloaded++;
        RynthCore.Engine.Plugins.PluginManager.PluginsUnloaded += OnUnloaded;
        try
        {
            RynthEngineHub.Join(new RynthEngineHub.Part("A", [a]));
            RynthEngineHub.Join(new RynthEngineHub.Part("B", [b]));
            bool ran = false;
            UiDataHub.Post("x", () => ran = true);

            RynthEngineHub.Leave("A");
            Assert.Equal((1, 0, 0), (a.Resets, b.Resets, unloaded));

            RynthEngineHub.Leave("B");
            Assert.Equal((1, 1), (b.Resets, unloaded));
            Assert.Equal(0, UiDataHub.Drain(_ => { }));
            Assert.False(ran);
        }
        finally { RynthCore.Engine.Plugins.PluginManager.PluginsUnloaded -= OnUnloaded; }
    }
}
