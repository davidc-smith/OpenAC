using AcDream.App.Rendering;
using AcDream.App.Streaming;

namespace AcDream.App.Tests.Streaming;

public sealed class OfflineWorldRevealTests
{
    [Fact]
    public void FirstObservation_WithholdsTheWorldAndWidensBothBudgets()
    {
        var fixture = new Fixture();

        Assert.False(fixture.Reveal.IsWithholdingWorld);
        fixture.Reveal.Observe(0xA9, 0xB4);

        Assert.True(fixture.Reveal.IsWithholdingWorld);
        Assert.Equal(
            ["begin 1 A9B4FFFF r8"],
            fixture.Streaming.Calls);
        Assert.Equal(["begin 1"], fixture.Resources.Calls);
    }

    [Fact]
    public void ReadyArea_ShowsTheWorldAndReturnsTheBudgetsOnce()
    {
        var fixture = new Fixture();
        fixture.Reveal.Observe(0xA9, 0xB4);
        fixture.Reveal.Observe(0xA9, 0xB4);
        Assert.True(fixture.Reveal.IsWithholdingWorld);

        fixture.Ready = true;
        fixture.Reveal.Observe(0xA9, 0xB4);
        fixture.Reveal.Observe(0xA9, 0xB4);

        Assert.False(fixture.Reveal.IsWithholdingWorld);
        Assert.True(fixture.Reveal.IsComplete);
        Assert.Equal(
            ["begin 1 A9B4FFFF r8", "end 1"],
            fixture.Streaming.Calls);
        Assert.Equal(["begin 1", "end 1"], fixture.Resources.Calls);
        Assert.Equal((0xA9B4FFFFu, 4, 8), fixture.LastReadinessQuery);
    }

    [Fact]
    public void CameraMovedWhileWithheld_MovesTheReservation()
    {
        var fixture = new Fixture();
        fixture.Reveal.Observe(0xA9, 0xB4);

        fixture.Reveal.Observe(0xAA, 0xB4);

        Assert.True(fixture.Reveal.IsWithholdingWorld);
        Assert.Equal(
            ["begin 1 A9B4FFFF r8", "end 1", "begin 1 AAB4FFFF r8"],
            fixture.Streaming.Calls);
        Assert.Equal(["begin 1"], fixture.Resources.Calls);
        Assert.Equal((0xAAB4FFFFu, 4, 8), fixture.LastReadinessQuery);
    }

    [Fact]
    public void NearRadiusBeyondFar_IsClampedToFar()
    {
        var fixture = new Fixture { Window = new StreamingRevealWindow(6, 2) };

        fixture.Reveal.Observe(0x10, 0x20);

        Assert.Equal((0x1020FFFFu, 2, 2), fixture.LastReadinessQuery);
        Assert.Equal(["begin 1 1020FFFF r2"], fixture.Streaming.Calls);
    }

    [Fact]
    public void ObserverOffTheMap_IsIgnored()
    {
        var fixture = new Fixture();

        fixture.Reveal.Observe(-1, 0x20);
        fixture.Reveal.Observe(0x10, 255);

        Assert.False(fixture.Reveal.IsWithholdingWorld);
        Assert.Empty(fixture.Streaming.Calls);
        Assert.Empty(fixture.Resources.Calls);
    }

    [Fact]
    public void WithheldPresentation_DrawsTheWorldOnlyWhenNotWithheld()
    {
        var withhold = new Withhold { IsWithholdingWorld = true };
        var inner = new Presentation { DrawWorld = true };
        var policy = new WithheldWorldPresentationPolicy(inner, withhold);

        Assert.False(policy.DrawWorld);
        withhold.IsWithholdingWorld = false;
        Assert.True(policy.DrawWorld);
        inner.DrawWorld = false;
        Assert.False(policy.DrawWorld);
    }

    private sealed class Fixture
    {
        internal Fixture()
        {
            Reveal = new OfflineWorldReveal(
                Streaming,
                Resources,
                () => Window,
                (landblock, near, far) =>
                {
                    LastReadinessQuery = (landblock, near, far);
                    return Ready;
                });
        }

        internal StreamingScheduler Streaming { get; } = new();
        internal RenderResources Resources { get; } = new();
        internal StreamingRevealWindow Window { get; set; } = new(4, 8);
        internal bool Ready { get; set; }
        internal (uint Landblock, int Near, int Far) LastReadinessQuery { get; private set; }
        internal OfflineWorldReveal Reveal { get; }
    }

    private sealed class StreamingScheduler : IWorldRevealStreamingScheduler
    {
        internal List<string> Calls { get; } = [];

        public void BeginDestinationReservation(
            long revealGeneration,
            uint destinationCell,
            int requiredRenderRadius) =>
            Calls.Add(
                $"begin {revealGeneration} {destinationCell:X8} r{requiredRenderRadius}");

        public void EndDestinationReservation(long revealGeneration) =>
            Calls.Add($"end {revealGeneration}");
    }

    private sealed class RenderResources : IWorldRevealRenderResourceScheduler
    {
        internal List<string> Calls { get; } = [];

        public void BeginDestinationReveal(long revealGeneration) =>
            Calls.Add($"begin {revealGeneration}");

        public void EndDestinationReveal(long revealGeneration) =>
            Calls.Add($"end {revealGeneration}");
    }

    private sealed class Withhold : IWorldViewportWithholdSource
    {
        public bool IsWithholdingWorld { get; set; }
    }

    private sealed class Presentation : IWorldScenePresentationPolicy
    {
        public bool DrawWorld { get; set; }
    }
}
