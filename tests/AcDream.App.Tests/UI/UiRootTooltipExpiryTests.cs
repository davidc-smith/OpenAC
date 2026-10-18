using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

/// <summary>A tooltip that runs out of time hides, but the pointer is still over its element.</summary>
public sealed class UiRootTooltipExpiryTests
{
    private sealed class HoverRecorder : UiElement
    {
        public List<int> Events { get; } = new();

        public override bool OnEvent(in UiEvent e)
        {
            Events.Add(e.Type);
            return base.OnEvent(in e);
        }
    }

    private static (UiRoot Root, HoverRecorder Target, List<UiElement> Hidden) Harness()
    {
        var root = new UiRoot { Width = 400, Height = 400 };
        var target = new HoverRecorder { Left = 100, Top = 100, Width = 50, Height = 20 };
        root.AddChild(target);
        var hidden = new List<UiElement>();
        root.TooltipHide += hidden.Add;
        root.Tick(0.016, 0);
        root.OnMouseMove(110, 110);
        return (root, target, hidden);
    }

    [Fact]
    public void ATimedOutTooltipHidesWithoutTellingTheElementThePointerLeft()
    {
        var (root, target, hidden) = Harness();
        root.Tick(0.016, root.TooltipDelayMs);
        Assert.Contains(UiEventType.Tooltip, target.Events);

        root.Tick(0.016, root.TooltipDelayMs + root.TooltipDurationMs);

        Assert.Equal([target], hidden);
        Assert.DoesNotContain(UiEventType.HoverLeave, target.Events);
    }

    [Fact]
    public void AStillPointerDoesNotBringATimedOutTooltipBack()
    {
        var (root, target, _) = Harness();
        long expiry = root.TooltipDelayMs + root.TooltipDurationMs;
        root.Tick(0.016, root.TooltipDelayMs);
        root.Tick(0.016, expiry);

        root.Tick(0.016, expiry + root.TooltipDelayMs * 4);

        Assert.Single(target.Events, e => e == UiEventType.Tooltip);
    }

    [Fact]
    public void MovingOverTheElementAgainBringsTheTooltipBackAfterTheDwell()
    {
        var (root, target, _) = Harness();
        long expiry = root.TooltipDelayMs + root.TooltipDurationMs;
        root.Tick(0.016, root.TooltipDelayMs);
        root.Tick(0.016, expiry);

        root.Tick(0.016, expiry + 100);
        root.OnMouseMove(112, 110);
        root.Tick(0.016, expiry + 100 + root.TooltipDelayMs - 1);
        Assert.Single(target.Events, e => e == UiEventType.Tooltip);
        root.Tick(0.016, expiry + 100 + root.TooltipDelayMs);

        Assert.Equal(2, target.Events.Count(e => e == UiEventType.Tooltip));
        Assert.Single(target.Events, e => e == UiEventType.HoverEnter);
    }

    [Fact]
    public void LeavingAfterATimedOutTooltipStillSendsHoverLeave()
    {
        var (root, target, hidden) = Harness();
        root.Tick(0.016, root.TooltipDelayMs);
        root.Tick(0.016, root.TooltipDelayMs + root.TooltipDurationMs);

        root.OnMouseMove(300, 300);

        Assert.Single(target.Events, e => e == UiEventType.HoverLeave);
        Assert.Single(hidden);
    }
}
