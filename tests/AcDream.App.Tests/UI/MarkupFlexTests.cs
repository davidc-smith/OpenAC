using AcDream.App.UI;
using AcDream.App.UI.Layout.Flex;
using AcDream.App.Rendering;
using DatReaderWriter.Types;

namespace AcDream.App.Tests.UI;

/// <summary>
/// <c>&lt;group layout&gt;</c> through the markup builder and the draw path:
/// placement, nesting both ways, labels in and out of flex, reflow when an
/// item's visibility, caption or theme changes, and what a cached layout costs.
/// Headless metrics: 7 points a character, 14-point lines, 24-point controls.
/// </summary>
public sealed class MarkupFlexTests
{
    private sealed class Binding
    {
        public bool Shown { get; set; } = true;
        public string Caption { get; set; } = "AB";
        public IReadOnlyList<string> Lines { get; } = ["one", "two"];
        public int Selected => -1;
    }

    private static (UiRoot Root, UiNineSlicePanel Frame) Mount(
        string body, object? binding = null, PluginUiThemeSettings? themes = null, string panel = "w=\"300\" h=\"200\"")
    {
        UiNineSlicePanel frame = MarkupDocument.Build(
            $"<panel x=\"0\" y=\"0\" {panel}>{body}</panel>", binding ?? new object(), _ => (0u, 0, 0), themes: themes);
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

    private static UiElement Find(UiElement root, string name)
    {
        if (root.Name == name) return root;
        foreach (UiElement child in root.Children)
            if (TryFind(child, name) is { } found) return found;
        throw new InvalidOperationException($"no element named {name}");
    }

    private static UiElement? TryFind(UiElement element, string name)
    {
        if (element.Name == name) return element;
        foreach (UiElement child in element.Children)
            if (TryFind(child, name) is { } found) return found;
        return null;
    }

    private static FlexRect Rect(UiElement e) => new(e.Left, e.Top, e.Width, e.Height);

    [Fact]
    public void A_row_places_its_items_after_padding_with_gaps()
    {
        var (_, frame) = Mount("""
            <group name="g" x="10" y="10" w="200" h="40" layout="row" gap="4" padding="5">
              <label name="a" text="AB" />
              <button name="b" text="OK" />
            </group>
            """);

        Assert.IsType<UiFlexGroup>(Find(frame, "g"));
        Assert.Equal(new FlexRect(5f, 5f, 14f, 30f), Rect(Find(frame, "a")));
        Assert.Equal(new FlexRect(23f, 5f, 38f, 30f), Rect(Find(frame, "b")));
    }

    [Fact]
    public void Grow_takes_the_free_space_and_justify_end_packs_a_footer()
    {
        var (_, frame) = Mount("""
            <group x="0" y="0" w="300" h="200" layout="column">
              <group name="bar" layout="row" gap="4">
                <field name="search" grow="1" />
                <button name="go" text="Go" />
              </group>
              <list name="list" grow="1" items="{Lines}" selected="{Selected}" />
              <group name="footer" layout="row" justify="end" gap="4">
                <button name="ok" text="OK" />
                <button name="cancel" text="Cancel" />
              </group>
            </group>
            """, new Binding());

        Assert.Equal(new FlexRect(0f, 0f, 300f, 24f), Rect(Find(frame, "bar")));
        Assert.Equal(new FlexRect(0f, 0f, 258f, 24f), Rect(Find(frame, "search")));
        Assert.Equal(new FlexRect(262f, 0f, 38f, 24f), Rect(Find(frame, "go")));
        Assert.Equal(new FlexRect(0f, 24f, 300f, 152f), Rect(Find(frame, "list")));
        Assert.Equal(new FlexRect(0f, 176f, 300f, 24f), Rect(Find(frame, "footer")));
        Assert.Equal(new FlexRect(192f, 0f, 38f, 24f), Rect(Find(frame, "ok")));
        Assert.Equal(new FlexRect(234f, 0f, 66f, 24f), Rect(Find(frame, "cancel")));
    }

    [Fact]
    public void A_nested_flex_group_is_laid_out_by_its_root()
    {
        var (_, frame) = Mount("""
            <group name="root" x="0" y="0" w="100" h="200" layout="column" align="start">
              <group name="grid" layout="row" wrap="true" gap="4">
                <icon name="i0" did="1" /><icon name="i1" did="1" /><icon name="i2" did="1" />
              </group>
              <label name="after" text="A" />
            </group>
            """);

        var grid = (UiFlexGroup)Find(frame, "grid");
        Assert.False(grid.Flex.IsRoot);
        Assert.True(((UiFlexGroup)Find(frame, "root")).Flex.IsRoot);
        // Height-for-width across inner nodes: the one-line grid (104) is
        // capped at the column's 100, where two icons fit and the third wraps.
        Assert.Equal(new FlexRect(0f, 0f, 100f, 68f), Rect(grid));
        Assert.Equal(new FlexRect(0f, 36f, 32f, 32f), Rect(Find(frame, "i2")));
        Assert.Equal(new FlexRect(0f, 68f, 7f, 14f), Rect(Find(frame, "after")));
    }

    [Fact]
    public void An_absolute_group_in_flex_is_its_authored_size_and_its_children_follow_their_anchors()
    {
        var (_, frame) = Mount("""
            <group x="0" y="0" w="300" h="50" layout="row">
              <group name="abs" w="100" h="40" grow="1">
                <button name="pin" x="70" y="0" w="20" h="20" anchor="right top" />
              </group>
              <label text="AB" />
            </group>
            """);

        Assert.Equal(286f, Find(frame, "abs").Width);
        // Authored 10 points from the right edge of a 100-wide group; kept at 286.
        Assert.Equal(256f, Find(frame, "pin").Left);
    }

    [Fact]
    public void A_flex_group_in_an_absolute_container_is_placed_by_its_coordinates()
    {
        var (_, frame) = Mount("""
            <group name="abs" x="20" y="30" w="200" h="100">
              <group name="flex" x="10" y="10" w="80" h="50" anchor="left top right" layout="column">
                <button name="b" text="OK" />
              </group>
            </group>
            """);

        Assert.Equal(new FlexRect(10f, 10f, 80f, 50f), Rect(Find(frame, "flex")));
        Assert.Equal(new FlexRect(0f, 0f, 80f, 24f), Rect(Find(frame, "b")));
    }

    [Fact]
    public void Item_attributes_under_an_absolute_group_fail_the_build()
    {
        var ex = Assert.Throws<FormatException>(() => Mount("<group x=\"0\" y=\"0\" w=\"10\" h=\"10\"><label text=\"a\" grow=\"1\" /></group>"));
        Assert.Contains("grow", ex.Message);
    }

    [Fact]
    public void Coordinates_on_a_flex_item_fail_the_build()
    {
        var ex = Assert.Throws<FormatException>(() => Mount("<group x=\"0\" y=\"0\" w=\"10\" h=\"10\" layout=\"row\"><label x=\"3\" text=\"a\" /></group>"));
        Assert.Contains("x=\"3\"", ex.Message);
    }

    [Fact]
    public void A_hidden_item_takes_no_space_and_showing_it_reflows()
    {
        var binding = new Binding { Shown = false };
        var (root, frame) = Mount("""
            <group x="0" y="0" w="300" h="30" layout="row">
              <button name="first" text="OK" visible="{Shown}" />
              <button name="second" text="OK" />
            </group>
            """, binding);

        Assert.Equal(0f, Find(frame, "second").Left);

        binding.Shown = true;
        Frame(root);

        Assert.Equal(38f, Find(frame, "second").Left);
    }

    [Fact]
    public void A_longer_caption_reflows_its_row()
    {
        var binding = new Binding();
        var (root, frame) = Mount("""
            <group x="0" y="0" w="300" h="30" layout="row">
              <label name="caption" text="{Caption}" />
              <button name="after" text="OK" />
            </group>
            """, binding);

        Assert.Equal(14f, Find(frame, "after").Left);

        binding.Caption = "ABCDEF";
        Frame(root);

        Assert.Equal(42f, Find(frame, "caption").Width);
        Assert.Equal(42f, Find(frame, "after").Left);
    }

    [Fact]
    public void A_label_in_flex_keeps_the_size_it_is_given()
    {
        var (_, frame) = Mount("""
            <group x="0" y="0" w="300" h="30" layout="row">
              <label name="wide" text="AB" grow="1" />
            </group>
            <label name="loose" x="0" y="40" text="AB" />
            """);

        var wide = (UiLabel)Find(frame, "wide");
        Assert.True(wide.SizedByLayout);
        Assert.Equal(new FlexRect(0f, 0f, 300f, 30f), Rect(wide));
        // Outside flex a label still sizes itself to its text when drawn.
        Assert.False(((UiLabel)Find(frame, "loose")).SizedByLayout);
        Assert.Equal(14f, Find(frame, "loose").Width);
    }

    [Fact]
    public void A_label_in_flex_centres_its_line_in_the_height_it_is_given()
    {
        var glyphs = new Dictionary<char, FontCharDesc>
        {
            ['A'] = new FontCharDesc { Unicode = 'A', Width = 6, Height = 8 },
        };
        var font = new UiDatFont(
            fgTex: 7, fgW: 32, fgH: 32, bgTex: 0, bgW: 0, bgH: 0,
            lineHeight: 16f, baselineOffset: 12f, glyphs);

        float TextTop(string body)
        {
            UiNineSlicePanel frame = MarkupDocument.Build(
                $"<panel x=\"0\" y=\"0\" w=\"100\" h=\"100\">{body}</panel>", new object(), _ => (0u, 0, 0), datFont: font);
            var root = new UiRoot { Width = 200, Height = 200 };
            root.AddChild(frame);
            root.Tick(0, 0);
            var (renderer, ctx) = ThemeDrawCapture.Context(200, 200);
            root.Draw(ctx);
            return renderer.DebugSpriteSegmentVerts
                .Where(segment => segment.Texture == 7u)
                .SelectMany(segment => Enumerable.Range(0, segment.Verts.Count / TextRenderer.FloatsPerVertex)
                    .Select(i => segment.Verts[i * TextRenderer.FloatsPerVertex + 1]))
                .Min();
        }

        // Away from the frame's edge, so the outline drawn a point above the text is not clipped.
        float loose = TextTop("<label x=\"10\" y=\"40\" text=\"A\" />");
        float inRow = TextTop("<group x=\"10\" y=\"40\" w=\"80\" h=\"30\" layout=\"row\"><label text=\"A\" /></group>");

        Assert.Equal(7f, inRow - loose);   // (30 - 16) / 2
    }

    [Fact]
    public void A_theme_switch_reflows_items_whose_size_depends_on_it()
    {
        var themes = new PluginUiThemeSettings();
        var (root, frame) = Mount("""
            <group x="0" y="0" w="300" h="30" layout="row">
              <toggle name="t" text="Auto" checked="{Shown}" />
              <button name="after" text="OK" />
            </group>
            """, new Binding(), themes, panel: "w=\"300\" h=\"200\" theme=\"plugin\"");

        Assert.Equal(45f, Find(frame, "after").Left);

        themes.Theme = PluginUiTheme.Moss;
        Frame(root);

        Assert.Equal(63f, Find(frame, "after").Left);
    }

    [Fact]
    public void A_layout_with_nothing_changed_measures_nothing()
    {
        var (root, frame) = Mount("""
            <group name="g" x="0" y="0" w="300" h="30" layout="row">
              <label text="AB" /><button text="OK" /><field grow="1" />
            </group>
            """);
        var flex = ((UiFlexGroup)Find(frame, "g")).Flex;
        int measured = flex.MeasureCount;

        Frame(root);
        Frame(root);

        Assert.Equal(measured, flex.MeasureCount);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(200)]
    public void A_wrapping_grid_measures_each_item_once_per_layout(int count)
    {
        string icons = string.Concat(Enumerable.Repeat("<icon did=\"1\" />", count));
        var (_, frame) = Mount($"<group name=\"g\" x=\"0\" y=\"0\" w=\"300\" h=\"200\" layout=\"row\" wrap=\"true\">{icons}</group>");
        var group = (UiFlexGroup)Find(frame, "g");
        int before = group.Flex.MeasureCount;

        Assert.True(group.Flex.EnsureLayout(250f, 200f));

        Assert.Equal(count, group.Flex.MeasureCount - before);
    }

    [Fact]
    public void A_steady_state_relayout_allocates_nothing()
    {
        var (_, frame) = Mount("""
            <group name="g" x="0" y="0" w="300" h="60" layout="row" wrap="true" gap="2">
              <label text="AB" /><button text="OK" /><field grow="1" />
              <group layout="column"><toggle text="ABC" checked="{Shown}" /><meter /></group>
            </group>
            """, new Binding());
        UiFlexBox flex = ((UiFlexGroup)Find(frame, "g")).Flex;
        float width = 300f;

        long allocated = ZeroAllocationProbe.MeasureWarmed(() =>
        {
            width = width == 300f ? 280f : 300f;
            flex.EnsureLayout(width, 60f);
        });

        Assert.Equal(0, allocated);
    }
}
