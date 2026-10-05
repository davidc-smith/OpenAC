using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

/// <summary>
/// <see cref="UiRoot.OnScroll(int, int, bool)"/>: vertical steps keep their
/// routing, horizontal steps travel as <see cref="UiEventType.ScrollHorizontal"/>
/// to whatever consumes them, Shift turns a vertical step horizontal, and only
/// a Shift step reaches the world (as the vertical step it was).
/// </summary>
public sealed class UiRootWheelRoutingTests
{
    /// <summary>A box that records the wheel events it is offered and consumes the kinds it is told to.</summary>
    private sealed class Wheel : UiElement
    {
        public bool TakesVertical { get; init; }
        public bool TakesHorizontal { get; init; }
        public List<(int Type, int Step)> Seen { get; } = [];

        public override bool OnEvent(in UiEvent e)
        {
            if (e.Type is not (UiEventType.Scroll or UiEventType.ScrollHorizontal)) return false;
            Seen.Add((e.Type, e.Data0));
            return e.Type == UiEventType.Scroll ? TakesVertical : TakesHorizontal;
        }
    }

    private static (UiRoot Root, Wheel Outer, Wheel Inner, List<int> World) Tree(bool outerH, bool innerV)
    {
        var root = new UiRoot { Width = 400, Height = 300 };
        var outer = new Wheel { Width = 200, Height = 200, TakesHorizontal = outerH };
        var inner = new Wheel { Left = 10, Top = 10, Width = 50, Height = 50, TakesVertical = innerV };
        outer.AddChild(inner);
        root.AddChild(outer);
        var world = new List<int>();
        root.WorldScrollFallThrough += world.Add;
        root.OnMouseMove(20, 20);
        return (root, outer, inner, world);
    }

    [Fact]
    public void A_vertical_step_goes_to_the_element_under_the_pointer()
    {
        var (root, outer, inner, world) = Tree(outerH: true, innerV: true);

        root.OnScroll(0, -1, shift: false);

        Assert.Equal([(UiEventType.Scroll, -1)], inner.Seen);
        Assert.Empty(outer.Seen);
        Assert.Empty(world);
    }

    [Fact]
    public void The_one_argument_overload_is_a_vertical_step()
    {
        var (root, _, inner, _) = Tree(outerH: true, innerV: true);

        root.OnScroll(1);

        Assert.Equal([(UiEventType.Scroll, 1)], inner.Seen);
    }

    [Fact]
    public void A_horizontal_step_bubbles_past_an_element_that_only_scrolls_vertically()
    {
        var (root, outer, inner, _) = Tree(outerH: true, innerV: true);

        root.OnScroll(-1, 0, shift: false);

        Assert.Equal([(UiEventType.ScrollHorizontal, -1)], inner.Seen);
        Assert.Equal([(UiEventType.ScrollHorizontal, -1)], outer.Seen);
    }

    [Fact]
    public void Shift_turns_a_vertical_step_horizontal()
    {
        var (root, outer, inner, world) = Tree(outerH: true, innerV: true);

        root.OnScroll(0, 1, shift: true);

        Assert.Equal([(UiEventType.ScrollHorizontal, 1)], inner.Seen);
        Assert.Equal([(UiEventType.ScrollHorizontal, 1)], outer.Seen);
        Assert.Empty(world);
    }

    [Fact]
    public void A_shift_step_nothing_takes_over_the_interface_is_dropped()
    {
        var (root, _, _, world) = Tree(outerH: false, innerV: true);

        root.OnScroll(0, 1, shift: true);

        Assert.Empty(world);
    }

    [Fact]
    public void Over_the_world_only_a_shift_step_falls_through_as_vertical()
    {
        var (root, _, _, world) = Tree(outerH: true, innerV: true);
        root.OnMouseMove(350, 250);

        root.OnScroll(1, 0, shift: false);
        root.OnScroll(0, -1, shift: true);
        root.OnScroll(0, 1, shift: false);

        Assert.Equal([-1, 1], world);
    }

    [Fact]
    public void A_diagonal_step_routes_both_ways()
    {
        var (root, outer, inner, _) = Tree(outerH: true, innerV: true);

        root.OnScroll(1, -1, shift: false);

        Assert.Contains((UiEventType.ScrollHorizontal, 1), outer.Seen);
        Assert.Contains((UiEventType.Scroll, -1), inner.Seen);
    }
}
