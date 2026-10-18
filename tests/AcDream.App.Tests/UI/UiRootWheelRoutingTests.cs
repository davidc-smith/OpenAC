using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

/// <summary>
/// <see cref="UiRoot.OnScroll(int, int, bool)"/>: vertical steps keep their
/// routing, horizontal steps travel as <see cref="UiEventType.ScrollHorizontal"/>
/// to whatever consumes them, Shift turns a vertical step horizontal, and a
/// Shift step nothing takes horizontally is routed as the vertical step it was
/// (to the interface, else the world). A step the interface takes outside an
/// open popup closes the popup, and the hover follows whatever the step moved
/// under the pointer.
/// </summary>
public sealed class UiRootWheelRoutingTests
{
    /// <summary>A box that records the wheel events it is offered and consumes the kinds it is told to.</summary>
    private sealed class Wheel : UiElement
    {
        public bool TakesVertical { get; init; }
        public bool TakesHorizontal { get; init; }
        /// <summary>How far a step it takes moves it, as content scrolled out from under the pointer.</summary>
        public float ShiftOnTake { get; init; }
        public List<(int Type, int Step)> Seen { get; } = [];
        public List<int> Hover { get; } = [];

        public override bool OnEvent(in UiEvent e)
        {
            if (e.Type is UiEventType.HoverEnter or UiEventType.HoverLeave) Hover.Add(e.Type);
            if (e.Type is not (UiEventType.Scroll or UiEventType.ScrollHorizontal)) return false;
            Seen.Add((e.Type, e.Data0));
            bool takes = e.Type == UiEventType.Scroll ? TakesVertical : TakesHorizontal;
            if (takes) Left += ShiftOnTake;
            return takes;
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
    public void A_shift_step_nothing_takes_horizontally_falls_back_to_vertical()
    {
        var (root, _, inner, world) = Tree(outerH: false, innerV: true);

        root.OnScroll(0, 1, shift: true);

        Assert.Equal([(UiEventType.ScrollHorizontal, 1), (UiEventType.Scroll, 1)], inner.Seen);
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

    [Fact]
    public void A_step_taken_outside_an_open_popup_closes_it()
    {
        var (root, _, _, _) = Tree(outerH: true, innerV: true);
        var popup = new Wheel { Left = 300, Top = 10, Width = 50, Height = 50, TakesVertical = true };
        root.AddChild(popup);
        int dismissed = 0;
        root.SetActivePopup(popup, () => dismissed++);

        root.OnScroll(0, -1, shift: false);

        Assert.Equal(1, dismissed);
    }

    [Fact]
    public void A_step_over_the_popup_or_left_untaken_keeps_it_open()
    {
        var (root, _, _, world) = Tree(outerH: false, innerV: false);
        var popup = new Wheel { Left = 300, Top = 10, Width = 50, Height = 50, TakesVertical = true };
        root.AddChild(popup);
        int dismissed = 0;
        root.SetActivePopup(popup, () => dismissed++);

        root.OnScroll(0, -1, shift: false);   // over inner: nothing takes it
        root.OnMouseMove(320, 20);
        root.OnScroll(0, -1, shift: false);   // over the popup itself
        root.OnMouseMove(390, 290);
        root.OnScroll(0, -1, shift: false);   // over the world

        Assert.Equal(0, dismissed);
        Assert.Equal([-1], world);
    }

    [Fact]
    public void Hover_follows_content_a_step_moves_from_under_the_pointer()
    {
        var root = new UiRoot { Width = 400, Height = 300 };
        var outer = new Wheel { Width = 200, Height = 200 };
        var inner = new Wheel { Left = 10, Top = 10, Width = 50, Height = 50, TakesVertical = true, ShiftOnTake = 100 };
        outer.AddChild(inner);
        root.AddChild(outer);
        root.OnMouseMove(20, 20);
        Assert.Equal([UiEventType.HoverEnter], inner.Hover);

        root.OnScroll(0, -1, shift: false);

        Assert.Equal([UiEventType.HoverEnter, UiEventType.HoverLeave], inner.Hover);
        Assert.Equal([UiEventType.HoverEnter], outer.Hover);
    }
}
