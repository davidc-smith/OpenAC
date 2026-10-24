using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using AcDream.App.Hosting;
using AcDream.Content;
using AcDream.Content.Pak;
using AcDream.Core.Physics;
using AcDream.Core.Plugins;
using AcDream.Runtime;
using DatReaderWriter.DBObjs;
using Xunit.Abstractions;

namespace AcDream.App.Tests.Hosting;

/// <summary>
/// The session's collision is built on the update thread a window draws
/// from. Loading a landblock happens on a worker; what the update thread does
/// is cut into steps, and no step may hold the update for more than a few
/// milliseconds, or the window hitches each time the character comes near a
/// new landblock. The one step that is not cut is the commit that makes a
/// finished landblock live at once; its cost is reported. Measured on real
/// game files: a large dungeon and a town.
/// It runs alone: other tests running beside it would be measured too.
/// </summary>
[Trait("Lane", "Timing")]
[Collection(ThreadSchedulingCollection.Name)]
public sealed class GraphicalSessionCollisionTimingTests : IDisposable
{
    /// <summary>The most one step of collision work may take on the update thread.</summary>
    private static readonly TimeSpan StepLimit = TimeSpan.FromMilliseconds(5);

    private const uint Player = 0x5000000Bu;

    private readonly ITestOutputHelper _output;
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "acdream-collision-timing-" + Guid.NewGuid().ToString("N"));

    public GraphicalSessionCollisionTimingTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Theory]
    [InlineData(0x0105032Bu)]
    [InlineData(0xA9B40021u)]
    public void NoStepOfPublishingTheNeighborhoodHoldsTheUpdate(uint arrival)
    {
        string? datDirectory = System.Environment.GetEnvironmentVariable("ACDREAM_DAT_DIR");
        string? package = System.Environment.GetEnvironmentVariable("ACDREAM_PAK_PATH");
        Assert.True(Directory.Exists(datDirectory), "Lane=Timing needs an installed ACDREAM_DAT_DIR.");
        Assert.True(File.Exists(package), "Lane=Timing needs a validated ACDREAM_PAK_PATH.");

        using var dats = new BoundedTestDatCollection(datDirectory!);
        var bounded = (IDatReaderWriter)dats;
        using var prepared = new PakPreparedAssetSource(package!, bounded);
        float[] heights = Assert.IsType<Region>(bounded.Get<Region>(0x13000000u))
            .LandDefs.LandHeightTable;
        var content = new GraphicalCollisionContent(
            bounded,
            new object(),
            prepared,
            ImmutableArray.Create(heights));

        // The first arrival also pays for compiling the code; the second
        // is what every later landblock costs.
        StepTimes cold = Publish(content, arrival);
        StepTimes warm = Publish(content, arrival);
        _output.WriteLine($"cold: {cold}");
        _output.WriteLine($"warm: {warm}");

        Assert.True(
            warm.LongestStep <= StepLimit,
            $"One step held the update for {warm.LongestStep.TotalMilliseconds:0.0} ms; {warm}");
    }

    /// <summary>
    /// A fresh session arriving at <paramref name="arrival"/>, its collision
    /// published by the host's update until the neighborhood is complete,
    /// timing each step of the update. A step that made a landblock live is a
    /// commit; time the runtime spent collecting garbage is not the step's.
    /// </summary>
    private StepTimes Publish(GraphicalCollisionContent content, uint arrival)
    {
        using GraphicalSessionHost host = new(
            RuntimeOptions.Parse(_directory, _ => null) with
            {
                SessionId = "collision-timing",
                StatusFilePath = Path.Combine(_directory, "status.jsonl"),
            },
            new WorldGameState(),
            new WorldEvents());
        PhysicsEngine engine = host.Runtime.EntityObjects.Physics.Engine;
        var times = new StepTimes();
        long last = 0;
        TimeSpan lastPause = TimeSpan.Zero;
        int lastLandblocks = 0;
        var collision = new GraphicalSessionCollision(
            host.Runtime,
            () =>
            {
                // Called once as the update starts and then between steps.
                long now = Stopwatch.GetTimestamp();
                TimeSpan pause = GC.GetTotalPauseDuration();
                int landblocks = engine.LandblockCount;
                if (last != 0)
                {
                    TimeSpan step = Stopwatch.GetElapsedTime(last, now) - (pause - lastPause);
                    times.Add(step, commit: landblocks != lastLandblocks);
                }
                last = now;
                lastPause = pause;
                lastLandblocks = landblocks;
                return now;
            },
            GraphicalSessionCollision.FrameShare);
        using IDisposable binding = collision.BindContent(content);
        Arrive(host.Runtime, arrival);

        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(120))
        {
            last = 0;
            collision.Tick();
            if (collision.IsReady(arrival) && collision.Neighborhood.IsQuiescent)
                break;
            Thread.Sleep(1);
        }
        Assert.True(collision.IsReady(arrival), "The neighborhood was never published.");
        times.Landblocks = engine.LandblockCount;
        return times;
    }

    /// <summary>The server creates the character at a cell.</summary>
    private static void Arrive(GameRuntime runtime, uint cellId)
    {
        runtime.PlayerIdentity.ServerGuid = Player;
        Assert.NotNull(runtime.EntityObjects
            .RegisterEntity(GraphicalSessionCollisionTests.Spawn(Player, cellId))
            .Canonical);
        runtime.EntityObjects.Physics.ObserveLocalPlayerCreate(cellId);
    }

    private sealed class StepTimes
    {
        private readonly List<TimeSpan> _steps = [];
        private readonly List<TimeSpan> _commits = [];

        public int Landblocks { get; set; }

        /// <summary>The longest step that was not a commit.</summary>
        public TimeSpan LongestStep => _steps.Count == 0 ? TimeSpan.Zero : _steps.Max();

        public void Add(TimeSpan elapsed, bool commit) =>
            (commit ? _commits : _steps).Add(elapsed);

        public override string ToString() => string.Create(
            CultureInfo.InvariantCulture,
            $"{Landblocks} landblocks, {_steps.Count} steps "
            + $"(total {_steps.Sum(step => step.TotalMilliseconds):0} ms, longest ms: {Worst(_steps)}), "
            + $"commits ms: {string.Join(", ", _commits.Select(Milliseconds))}");

        private static string Worst(List<TimeSpan> steps) =>
            string.Join(", ", steps.OrderDescending().Take(6).Select(Milliseconds));

        private static string Milliseconds(TimeSpan span) =>
            span.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture);
    }
}
