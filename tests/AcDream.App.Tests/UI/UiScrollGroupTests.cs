using System.Numerics;
using AcDream.App.UI;
using AcDream.App.UI.Layout.Flex;

namespace AcDream.App.Tests.UI;

/// <summary>
/// <c>scroll="x|y|both"</c> on groups and on a window's content area, through
/// the markup builder, the draw path and the root's input routing: extent,
/// bar settling, the content offset in drawing, hit-testing and screen
/// positions, wheel routing (nested lists, horizontal steps, Shift), focus
/// scrolling into view, clamping, and the window minimum. Headless metrics:
/// 7 points a character, 14-point lines, 24-point controls; bars are 16.
/// </summary>
public sealed class UiScrollGroupTests
{
    private sealed class Binding
    {
        public bool Shown { get; set; } = true;
        public IReadOnlyList<string> Lines { get; } = ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j"];
        public int Selected => -1;
        public IReadOnlyList<string> Choices { get; } = ["one", "two"];
        public string Choice => "one";
    }

    private static (UiRoot Root, UiNineSlicePanel Frame) Mount(
        string body, object? binding = null, PluginUiThemeSettings? themes = null,
        string panel = "w=\"300\" h=\"200\"")
    {
        UiNineSlicePanel frame = MarkupDocument.Build(
            $"<panel x=\"0\" y=\"0\" {panel}>{body}</panel>", binding ?? new Binding(), _ => (0u, 0, 0), themes: themes);
        var root = new UiRoot { Width = 1280, Height = 720 };
        root.AddChild(frame);
        Frame(root);
        return (root, frame);
    }

    private static void Frame(UiRoot root)
    {
        root.Tick(0, 0);
        ThemeDrawCapture.Draw(root);
    }

    private static UiElement Find(UiElement root, string name) =>
        TryFind(root, name) ?? throw new InvalidOperationException($"no element named {name}");

    private static UiElement? TryFind(UiElement element, string name)
    {
        if (element.Name == name) return element;
        foreach (UiElement child in element.Children)
            if (TryFind(child, name) is { } found) return found;
        return null;
    }

    private static UiScrollArea Area(UiElement element) =>
        ((IUiScrollHost)element).ScrollArea ?? throw new InvalidOperationException("not scrolling");

    private static void Wheel(UiRoot root, int x, int y, int dx = 0, int dy = 0, bool shift = false)
    {
        root.OnMouseMove(x, y);
        root.OnScroll(dx, dy, shift);
        Frame(root);
    }

    // ── Markup ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("x", true, false)]
    [InlineData("y", false, true)]
    [InlineData("both", true, true)]
    public void Scroll_names_the_axes_of_an_absolute_or_flex_group(string value, bool x, bool y)
    {
        var (_, frame) = Mount($"""
            <group name="a" x="0" y="0" w="100" h="100" scroll="{value}" />
            <group name="f" x="100" y="0" w="100" h="100" layout="column" scroll="{value}" />
            """);

        Assert.IsType<UiScrollPanel>(Find(frame, "a"));
        Assert.Equal((x, y), (Area(Find(frame, "a")).ScrollsX, Area(Find(frame, "a")).ScrollsY));
        var flex = Assert.IsType<UiFlexGroup>(Find(frame, "f"));
        Assert.Equal((x, y), (flex.Flex.Node.ScrollX, flex.Flex.Node.ScrollY));
        Assert.Equal(UiScrollArea.BarSize, flex.Flex.Node.ScrollbarSize);
    }

    [Fact]
    public void An_unknown_scroll_value_is_a_build_error()
    {
        var error = Assert.Throws<FormatException>(() => Mount("<group name=\"g\" w=\"10\" h=\"10\" scroll=\"down\" />"));
        Assert.Contains("<group name=\"g\"> scroll=\"down\" must be x, y or both", error.Message);
    }

    [Fact]
    public void A_group_without_scroll_is_unchanged()
    {
        var (_, frame) = Mount("<group name=\"g\" x=\"0\" y=\"0\" w=\"100\" h=\"100\" />");

        UiElement group = Find(frame, "g");
        Assert.Equal(typeof(UiPanel), group.GetType());
        Assert.True(group.ClickThrough);
        Assert.Null(group.ContentViewport);
    }

    // ── Extent and bars ─────────────────────────────────────────────────

    [Fact]
    public void Content_that_fits_shows_no_bar()
    {
        var (_, frame) = Mount("""
            <group name="g" x="0" y="0" w="100" h="100" scroll="y">
              <button name="b" x="0" y="0" w="80" h="24" text="OK" />
            </group>
            """);

        UiScrollArea area = Area(Find(frame, "g"));
        Assert.False(area.Vertical.Visible);
        Assert.Equal(new Vector2(100f, 100f), Find(frame, "g").ContentViewport);
    }

    [Fact]
    public void Absolute_content_past_the_bottom_shows_the_vertical_bar_beside_the_viewport()
    {
        var (_, frame) = Mount("""
            <group name="g" x="0" y="0" w="100" h="100" scroll="y">
              <button name="b" x="0" y="150" w="50" h="24" text="OK" />
            </group>
            """);

        UiScrollArea area = Area(Find(frame, "g"));
        Assert.True(area.Vertical.Visible);
        Assert.Equal(new Vector4(84f, 0f, 16f, 100f),
            new Vector4(area.Vertical.Left, area.Vertical.Top, area.Vertical.Width, area.Vertical.Height));
        Assert.Equal(174, area.Y.ContentHeight);
        Assert.Equal(100, area.Y.ViewHeight);
        Assert.Equal(new Vector2(84f, 100f), Find(frame, "g").ContentViewport);
    }

    [Fact]
    public void Children_at_negative_coordinates_are_not_reachable()
    {
        var (_, frame) = Mount("""
            <group name="g" x="0" y="0" w="100" h="100" scroll="y">
              <button x="0" y="-60" w="50" h="24" text="Up" />
              <button x="0" y="60" w="50" h="24" text="Down" />
            </group>
            """);

        UiScrollArea area = Area(Find(frame, "g"));
        Assert.Equal(84, area.Y.ContentHeight);
        Assert.False(area.Vertical.Visible);
    }

    [Fact]
    public void A_right_anchored_child_moves_in_beside_the_bar()
    {
        var (_, frame) = Mount("""
            <group name="g" x="0" y="0" w="100" h="100" scroll="y">
              <button name="r" x="70" y="0" w="30" h="24" anchor="top right" text="R" />
              <button x="0" y="200" w="30" h="24" text="Far" />
            </group>
            """);

        Assert.Equal(54f, Find(frame, "r").Left);
    }

    [Fact]
    public void A_vertical_bar_that_makes_the_content_too_wide_brings_the_horizontal_bar()
    {
        var (_, frame) = Mount("""
            <group name="g" x="0" y="0" w="100" h="100" scroll="both">
              <button x="0" y="0" w="90" h="24" text="Wide" />
              <button x="0" y="150" w="30" h="24" text="Far" />
            </group>
            """);

        UiScrollArea area = Area(Find(frame, "g"));
        Assert.True(area.Vertical.Visible);
        Assert.True(area.Horizontal.Visible);
        Assert.Equal(new Vector2(84f, 84f), Find(frame, "g").ContentViewport);
        // The corner square is left empty.
        Assert.Equal(84f, area.Vertical.Height);
        Assert.Equal(84f, area.Horizontal.Width);
    }

    [Fact]
    public void A_scrolling_flex_column_keeps_its_items_at_their_size_beside_the_bar()
    {
        var (_, frame) = Mount("""
            <group name="g" x="0" y="0" w="120" h="60" layout="column" scroll="y">
              <button name="a" text="One" />
              <button name="b" text="Two" />
              <button name="c" text="Three" />
            </group>
            """);

        UiScrollArea area = Area(Find(frame, "g"));
        Assert.True(area.Vertical.Visible);
        Assert.Equal(new FlexRect(0f, 48f, 104f, 24f), Rect(Find(frame, "c")));
        Assert.Equal(72, area.Y.ContentHeight);
    }

    [Fact]
    public void A_nested_flex_group_scrolls_inside_its_flex_root()
    {
        var (_, frame) = Mount("""
            <group x="0" y="0" w="120" h="100" layout="column">
              <label text="Head" />
              <group name="g" grow="1" layout="column" scroll="y">
                <button text="One" />
                <button text="Two" />
                <button text="Three" />
                <button text="Four" />
              </group>
            </group>
            """);

        UiElement g = Find(frame, "g");
        Assert.Equal(new FlexRect(0f, 14f, 120f, 86f), Rect(g));
        Assert.True(Area(g).Vertical.Visible);
        Assert.Equal(96, Area(g).Y.ContentHeight);
    }

    private static FlexRect Rect(UiElement e) => new(e.Left, e.Top, e.Width, e.Height);

    // ── Offset ──────────────────────────────────────────────────────────

    [Fact]
    public void Scrolling_moves_the_content_in_screen_positions_and_hit_tests_but_not_the_bar()
    {
        var (root, frame) = Mount("""
            <group name="g" x="10" y="10" w="100" h="100" scroll="y">
              <button name="b" x="0" y="150" w="50" h="24" text="OK" />
            </group>
            """);
        UiElement group = Find(frame, "g"), button = Find(frame, "b");

        Area(group).Y.SetScrollY(70);

        Assert.Equal(new Vector2(10f, 90f), button.ScreenPosition);
        Assert.Same(button, root.Pick(20, 100));
        Assert.Equal(new Vector2(94f, 10f), Area(group).Vertical.ScreenPosition);
    }

    [Fact]
    public void Scrolled_content_outside_the_viewport_is_not_hit()
    {
        var (root, frame) = Mount("""
            <group name="g" x="0" y="0" w="100" h="100" scroll="y">
              <button name="b" x="0" y="90" w="50" h="40" text="OK" />
              <button x="0" y="200" w="50" h="24" text="Far" />
            </group>
            """);

        // The button's lower part, at y 100..130, is below the viewport.
        Assert.NotSame(Find(frame, "b"), root.Pick(20, 110));
    }

    [Fact]
    public void Scrolled_content_draws_offset_and_clipped_to_the_viewport()
    {
        var (root, frame) = Mount("""
            <group name="g" x="0" y="0" w="100" h="50" scroll="y">
              <group x="0" y="40" w="60" h="40" background="#FFFF0000" />
            </group>
            """);
        Area(Find(frame, "g")).Y.SetScrollY(30);

        (var renderer, var context) = ThemeDrawCapture.Context(root.Width, root.Height);
        root.Draw(context);
        var red = ThemeDrawCapture.Vertices(renderer)
            .Where(v => v.Color.X > 0.9f && v.Color.Y < 0.1f && v.Color.Z < 0.1f)
            .Select(v => v.Position.Y)
            .ToList();

        Assert.NotEmpty(red);
        Assert.Equal(10f, red.Min(), 2);
        Assert.Equal(50f, red.Max(), 2);
    }

    [Fact]
    public void A_menu_in_a_scrolled_group_opens_its_popup_where_the_menu_is()
    {
        var (_, frame) = Mount("""
            <group name="g" x="0" y="0" w="200" h="60" scroll="y">
              <menu name="m" x="0" y="80" w="100" h="24" items="{Choices}" selected="{Choice}" />
              <button x="0" y="200" w="50" h="24" text="Far" />
            </group>
            """);

        Area(Find(frame, "g")).Y.SetScrollY(50);

        Assert.Equal(new Vector2(0f, 30f), Find(frame, "m").ScreenPosition);
    }

    // ── Wheel ───────────────────────────────────────────────────────────

    [Fact]
    public void The_wheel_scrolls_a_group_by_three_lines_over_empty_space()
    {
        var (root, frame) = Mount("""
            <group name="g" x="0" y="0" w="100" h="100" scroll="y">
              <button x="0" y="250" w="50" h="24" text="Far" />
            </group>
            """);

        Wheel(root, 50, 50, dy: -1);

        Assert.Equal(3 * UiScrollArea.LineStep, Area(Find(frame, "g")).Y.ScrollY);
    }

    [Fact]
    public void A_group_with_nothing_to_scroll_lets_the_wheel_through()
    {
        var (root, frame) = Mount("""
            <group name="outer" x="0" y="0" w="100" h="100" scroll="y">
              <group name="inner" x="0" y="0" w="100" h="50" scroll="y" />
              <button x="0" y="250" w="50" h="24" text="Far" />
            </group>
            """);

        Wheel(root, 50, 20, dy: -1);

        Assert.Equal(48, Area(Find(frame, "outer")).Y.ScrollY);
    }

    [Fact]
    public void A_list_inside_a_scrolling_group_scrolls_itself()
    {
        var (root, frame) = Mount("""
            <group name="g" x="0" y="0" w="200" h="100" scroll="y">
              <list name="l" x="0" y="0" w="150" h="40" items="{Lines}" selected="{Selected}" />
              <button x="0" y="250" w="50" h="24" text="Far" />
            </group>
            """);

        Wheel(root, 20, 20, dy: -1);

        Assert.Equal(0, Area(Find(frame, "g")).Y.ScrollY);
        Assert.Equal(1, ((UiMarkupList)Find(frame, "l")).TopRowForTest);
    }

    [Fact]
    public void A_horizontal_step_scrolls_a_horizontal_group_and_passes_a_vertical_one()
    {
        var (root, frame) = Mount("""
            <group name="outer" x="0" y="0" w="100" h="100" scroll="x">
              <group name="inner" x="0" y="0" w="100" h="80" scroll="y">
                <button x="0" y="200" w="50" h="24" text="Down" />
              </group>
              <button x="250" y="0" w="50" h="24" text="Right" />
            </group>
            """);

        Wheel(root, 50, 40, dx: -1);

        Assert.Equal(48, Area(Find(frame, "outer")).X.ScrollY);
        Assert.Equal(0, Area(Find(frame, "inner")).Y.ScrollY);
    }

    [Fact]
    public void Shift_turns_the_wheel_horizontal_and_a_list_does_not_see_it()
    {
        var (root, frame) = Mount("""
            <group name="g" x="0" y="0" w="200" h="100" scroll="x">
              <list name="l" x="0" y="0" w="150" h="40" items="{Lines}" selected="{Selected}" />
              <button x="400" y="0" w="50" h="24" text="Right" />
            </group>
            """);

        Wheel(root, 20, 20, dy: -1, shift: true);

        Assert.Equal(48, Area(Find(frame, "g")).X.ScrollY);
        Assert.Equal(0, ((UiMarkupList)Find(frame, "l")).TopRowForTest);
    }

    [Fact]
    public void Shift_over_a_list_in_a_group_without_a_horizontal_scroller_scrolls_the_list()
    {
        var (root, frame) = Mount("""
            <group name="g" x="0" y="0" w="200" h="100" scroll="y">
              <list name="l" x="0" y="0" w="150" h="40" items="{Lines}" selected="{Selected}" />
              <button x="0" y="250" w="50" h="24" text="Far" />
            </group>
            """);

        Wheel(root, 20, 20, dy: -1, shift: true);

        Assert.Equal(1, ((UiMarkupList)Find(frame, "l")).TopRowForTest);
        Assert.Equal(0, Area(Find(frame, "g")).Y.ScrollY);
    }

    private sealed class PressRecorder : UiElement
    {
        public int? DownY;
        public PressRecorder() { AcceptsFocus = true; }
        public override bool OnEvent(in UiEvent e)
        {
            if (e.Type == UiEventType.MouseDown) DownY = e.Data2;
            return true;
        }
    }

    [Fact]
    public void A_press_reports_its_position_as_it_was_before_focus_scrolled_the_target_into_view()
    {
        var (root, frame) = Mount("""
            <group name="g" x="0" y="0" w="200" h="100" scroll="y">
              <button x="0" y="250" w="50" h="24" text="Far" />
            </group>
            """);
        var rec = new PressRecorder { Left = 0, Top = 80, Width = 100, Height = 60 };
        Find(frame, "g").AddChild(rec);
        Frame(root);

        root.OnMouseMove(10, 90);
        root.OnMouseDown(UiMouseButton.Left, 10, 90);

        Assert.True(Area(Find(frame, "g")).Y.ScrollY > 0, "focus should have revealed the target");
        Assert.Equal(10, rec.DownY);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_press_on_empty_scrolling_space_drags_the_window(bool rootContent)
    {
        var (root, frame) = rootContent
            ? Mount("""<group w="100" h="400" />""", panel: "w=\"300\" h=\"200\" layout=\"column\" scroll=\"y\"")
            : Mount("""
                <group name="g" x="0" y="0" w="200" h="100" scroll="y">
                  <button x="0" y="250" w="50" h="24" text="Far" />
                </group>
                """);
        Assert.True(frame.Draggable);
        float l = frame.Left, t = frame.Top;

        root.OnMouseMove(150, 60);
        root.OnMouseDown(UiMouseButton.Left, 150, 60);
        root.OnMouseMove(180, 80);
        root.OnMouseUp(UiMouseButton.Left, 180, 80);

        Assert.Equal((l + 30, t + 20), (frame.Left, frame.Top));
    }

    [Fact]
    public void A_horizontal_step_over_the_world_goes_nowhere_but_a_shift_step_reaches_it_as_vertical()
    {
        var root = new UiRoot { Width = 1280, Height = 720 };
        var world = new List<int>();
        root.WorldScrollFallThrough += world.Add;
        root.OnMouseMove(600, 600);

        root.OnScroll(1, 0, shift: false);
        root.OnScroll(0, -1, shift: true);

        Assert.Equal(new[] { -1 }, world);
    }

    [Fact]
    public void The_old_vertical_entry_point_still_routes_vertically()
    {
        var (root, frame) = Mount("""
            <group name="g" x="0" y="0" w="100" h="100" scroll="y">
              <button x="0" y="250" w="50" h="24" text="Far" />
            </group>
            """);

        root.OnMouseMove(50, 50);
        root.OnScroll(-1);

        Assert.Equal(48, Area(Find(frame, "g")).Y.ScrollY);
    }

    // ── Bars ────────────────────────────────────────────────────────────

    [Fact]
    public void Pressing_below_the_thumb_pages_down()
    {
        var (root, frame) = Mount("""
            <group name="g" x="0" y="0" w="100" h="100" scroll="y">
              <button x="0" y="350" w="50" h="24" text="Far" />
            </group>
            """);

        root.OnMouseMove(92, 80);
        root.OnMouseDown(UiMouseButton.Left, 92, 80);
        root.OnMouseUp(UiMouseButton.Left, 92, 80);

        Assert.Equal(100, Area(Find(frame, "g")).Y.ScrollY);
    }

    [Fact]
    public void Bars_take_the_window_theme_and_go_back_to_classic_art()
    {
        var themes = new PluginUiThemeSettings();
        var (root, frame) = Mount("""
            <group name="g" x="0" y="0" w="100" h="100" scroll="both" />
            """, themes: themes, panel: "w=\"300\" h=\"200\" theme=\"plugin\"");
        UiScrollArea area = Area(Find(frame, "g"));

        themes.Theme = PluginUiTheme.Moss;
        Frame(root);
        Assert.NotNull(area.Vertical.ThemePalette);
        Assert.NotNull(area.Horizontal.ThemePalette);
        Assert.False(area.Vertical.RetailArt);

        themes.Theme = PluginUiTheme.Classic;
        Frame(root);
        Assert.Null(area.Vertical.ThemePalette);
        Assert.True(area.Vertical.RetailArt);
    }

    // ── Position ────────────────────────────────────────────────────────

    [Fact]
    public void The_position_clamps_when_the_group_grows()
    {
        var (root, frame) = Mount("""
            <group name="g" x="0" y="0" w="100" h="100" scroll="y" anchor="top left bottom">
              <button x="0" y="150" w="50" h="24" text="Far" />
            </group>
            """, panel: "w=\"300\" h=\"200\" resizable=\"true\"");
        UiScrollArea area = Area(Find(frame, "g"));
        area.Y.ScrollToEnd();
        Assert.Equal(74, area.Y.ScrollY);

        frame.Height = 300f;
        Frame(root);

        Assert.Equal(0, area.Y.ScrollY);
        Assert.False(area.Vertical.Visible);
    }

    [Fact]
    public void The_position_survives_hide_and_show()
    {
        var binding = new Binding();
        var (root, frame) = Mount("""
            <group name="g" x="0" y="0" w="100" h="100" scroll="y" visible="{Shown}">
              <button x="0" y="250" w="50" h="24" text="Far" />
            </group>
            """, binding);
        Area(Find(frame, "g")).Y.SetScrollY(60);

        binding.Shown = false;
        Frame(root);
        binding.Shown = true;
        Frame(root);

        Assert.Equal(60, Area(Find(frame, "g")).Y.ScrollY);
    }

    [Fact]
    public void Focus_scrolls_a_field_below_the_viewport_into_view()
    {
        var (root, frame) = Mount("""
            <group name="g" x="0" y="0" w="200" h="100" scroll="y">
              <field name="f1" x="0" y="0" w="100" h="24" />
              <field name="f2" x="0" y="200" w="100" h="24" />
            </group>
            """);

        root.SetKeyboardFocus(Find(frame, "f2"));

        Assert.Equal(124, Area(Find(frame, "g")).Y.ScrollY);

        root.SetKeyboardFocus(Find(frame, "f1"));

        Assert.Equal(0, Area(Find(frame, "g")).Y.ScrollY);
    }

    [Fact]
    public void Focus_inside_nested_scrolling_groups_scrolls_each_of_them()
    {
        var (root, frame) = Mount("""
            <group name="outer" x="0" y="0" w="200" h="100" scroll="y">
              <group name="inner" x="0" y="150" w="200" h="60" scroll="y">
                <field name="f" x="0" y="100" w="100" h="24" />
              </group>
            </group>
            """);

        root.SetKeyboardFocus(Find(frame, "f"));
        Frame(root);

        Assert.Equal(64, Area(Find(frame, "inner")).Y.ScrollY);
        Assert.Equal(110, Area(Find(frame, "outer")).Y.ScrollY);
        Assert.Equal(new Vector2(0f, 76f), Find(frame, "f").ScreenPosition);
    }

    // ── Flex sizing ─────────────────────────────────────────────────────

    [Fact]
    public void An_absolute_scrolling_group_in_flex_may_shrink_to_its_viewport_minimum()
    {
        var (_, frame) = Mount("""
            <group x="0" y="0" w="200" h="100" layout="column">
              <group name="g" w="200" h="300" scroll="y" />
              <button name="b" text="OK" />
            </group>
            """);

        Assert.Equal(76f, Find(frame, "g").Height);
        Assert.Equal(76f, Find(frame, "b").Top);
    }

    [Fact]
    public void A_scrolling_grid_shows_every_line_when_its_window_has_room()
    {
        var (_, frame) = Mount("""
            <group x="0" y="0" w="200" h="300" layout="column">
              <group name="grid" layout="row" wrap="true" scroll="y">
                <group w="60" h="60" /><group w="60" h="60" /><group w="60" h="60" />
                <group w="60" h="60" /><group w="60" h="60" />
              </group>
              <button name="b" text="OK" />
            </group>
            """);

        Assert.Equal(120f, Find(frame, "grid").Height);
        Assert.False(Area(Find(frame, "grid")).Vertical.Visible);
    }
}
