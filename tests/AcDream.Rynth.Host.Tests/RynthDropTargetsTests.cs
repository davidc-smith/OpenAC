using System.Numerics;
using AcDream.Rynth.EngineUi;
using RynthCore.Engine.ImGuiBackend;

namespace AcDream.Rynth.Host.Tests;

// Tests in this assembly run one at a time (FakeGameView.cs: DisableTestParallelization),
// which keeps the static cursor source and drop targets to one test at a time. Targets stay
// published (keyed, live for 500 ms), so each test gets its own key and its own 100x100 rect,
// far from where any face draws, so an earlier test's target is never under this one's points.
public sealed class RynthDropTargetsTests : IDisposable
{
    private static int _n;
    private readonly string _key;
    private readonly Vector2 _min;

    public RynthDropTargetsTests()
    {
        int n = Interlocked.Increment(ref _n);
        _key = $"test-drop-{n}";
        _min = new Vector2(10_000 + 1_000 * n, 10_000);
        Win32CursorStandIn.Source = () => new Vector2(-1, -1);
    }

    public void Dispose() => Win32CursorStandIn.Source = null;

    private Vector2 Max => _min + new Vector2(100, 100);

    private Vector2 Inside => _min + new Vector2(50, 50);

    private Vector2 Outside => _min + new Vector2(150, 50);

    [Fact]
    public void AReleaseOverAnAcceptingTargetIsTakenOnce()
    {
        UiDropTargets.Publish(_key, "Item Count", _min, Max, accepting: true);

        Assert.True(RynthDropClaim.Claim(0x5001u, Inside));
        Assert.True(UiDropTargets.TryTake(_key, out uint item));
        Assert.Equal(0x5001u, item);
        Assert.False(UiDropTargets.TryTake(_key, out _));
    }

    [Fact]
    public void OutsideOrNotAcceptingIsNotClaimed()
    {
        UiDropTargets.Publish(_key, "Item Count", _min, Max, accepting: true);
        Assert.False(RynthDropClaim.Claim(0x5001u, Outside));

        UiDropTargets.Publish(_key, "Item Count", _min, Max, accepting: false);
        Assert.False(RynthDropClaim.Claim(0x5001u, Inside));
        Assert.False(UiDropTargets.TryTake(_key, out _));
    }

    [Fact]
    public void AStaleTargetIsNotClaimed()
    {
        UiDropTargets.Publish(_key, "Item Count", _min, Max, accepting: true);
        Thread.Sleep(600);   // their StaleMs is 500
        Assert.False(RynthDropClaim.Claim(0x5001u, Inside));
    }

    [Fact]
    public void TheCursorIsTheDefaultSourceAgainAfterAClaim()
    {
        UiDropTargets.Publish(_key, "Item Count", _min, Max, accepting: true);
        Assert.True(RynthDropClaim.Claim(0x5001u, Inside));
        Assert.False(UiDropTargets.IsCursorOver(_key));   // default source is (-1,-1)
    }
}
