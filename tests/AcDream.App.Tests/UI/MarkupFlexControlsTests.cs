using AcDream.App.UI;
using AcDream.App.UI.Layout.Flex;

namespace AcDream.App.Tests.UI;

/// <summary>
/// The markup controls added by the markup-controls series as flex items:
/// their content sizes, separators following the axis, and context menus
/// staying out of the layout. Headless metrics: 7 points a character,
/// 14-point lines, 24-point controls.
/// </summary>
public sealed class MarkupFlexControlsTests
{
    private sealed class Binding
    {
        public bool On { get; set; } = true;
        public Action<bool> SetOn => v => On = v;
        public string Caption { get; set; } = "AB";
        public int Choice { get; set; }
        public Action<int> Choose => v => Choice = v;
        public int Count { get; set; } = 3;
        public Action<int> SetCount => v => Count = v;
        public string Key { get; set; } = string.Empty;
        public Action<string> SetKey => v => Key = v;
        public uint Colour { get; set; } = 0xFF336699;
        public Action<uint> SetColour => v => Colour = v;
        public Action<int> Pick => _ => { };
    }

    private static (UiRoot Root, UiNineSlicePanel Frame) Mount(string body, object binding)
    {
        UiNineSlicePanel frame = MarkupDocument.Build(
            $"<panel x=\"0\" y=\"0\" w=\"400\" h=\"500\">{body}</panel>", binding, _ => (0u, 0, 0));
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

    private static FlexRect Rect(UiElement e) => new(e.Left, e.Top, e.Width, e.Height);

    [Fact]
    public void New_controls_take_their_content_size_in_a_column()
    {
        var (_, frame) = Mount("""
            <group name="g" x="0" y="0" w="300" h="480" layout="column" align="start">
              <checkbox name="c" text="AB" checked="{On}" onchange="{SetOn}" />
              <radio name="r" items="A|BB" selected="{Choice}" onchange="{Choose}" />
              <number name="n" value="{Count}" onchange="{SetCount}" />
              <hotkey name="k" value="{Key}" onchange="{SetKey}" />
              <color name="o" value="{Colour}" onchange="{SetColour}" />
              <split name="p" w="200" h="100">
                <group />
                <group />
              </split>
            </group>
            """, new Binding());

        Assert.Equal(new FlexRect(0f, 0f, 31f, 20f), Rect(Find(frame, "c")));
        Assert.Equal(new FlexRect(0f, 20f, 39f, 40f), Rect(Find(frame, "r")));
        Assert.Equal(new FlexRect(0f, 60f, 120f, 24f), Rect(Find(frame, "n")));
        Assert.Equal(new FlexRect(0f, 84f, 120f, 24f), Rect(Find(frame, "k")));
        Assert.Equal(new FlexRect(0f, 108f, 40f, 24f), Rect(Find(frame, "o")));
        Assert.Equal(new FlexRect(0f, 132f, 200f, 100f), Rect(Find(frame, "p")));
    }

    [Fact]
    public void A_number_shrinks_no_narrower_than_a_field_and_its_spin()
    {
        var (_, frame) = Mount("""
            <group name="g" x="0" y="0" w="30" h="40" layout="row">
              <number name="n" value="{Count}" onchange="{SetCount}" />
            </group>
            """, new Binding());

        Assert.Equal(54f, Find(frame, "n").Width);
    }

    [Fact]
    public void A_separator_runs_across_a_column_and_down_a_row()
    {
        var (_, frame) = Mount("""
            <group x="0" y="0" w="200" h="100" layout="column">
              <label text="AB" />
              <separator name="h" />
            </group>
            <group x="0" y="120" w="200" h="40" layout="row">
              <label text="AB" />
              <separator name="v" />
            </group>
            """, new Binding());

        var h = (UiMarkupSeparator)Find(frame, "h");
        var v = (UiMarkupSeparator)Find(frame, "v");
        Assert.Equal(new FlexRect(0f, 14f, 200f, 9f), Rect(h));
        Assert.False(h.Vertical);
        Assert.Equal(new FlexRect(14f, 0f, 9f, 40f), Rect(v));
        Assert.True(v.Vertical);
    }

    [Fact]
    public void A_context_menu_is_never_a_flex_item()
    {
        var (_, frame) = Mount("""
            <group name="g" x="0" y="0" w="200" h="40" layout="row">
              <contextmenu items="A|B" onselect="{Pick}" />
              <button name="b" text="OK">
                <contextmenu items="C|D" onselect="{Pick}" />
              </button>
            </group>
            """, new Binding());

        var group = (UiFlexGroup)Find(frame, "g");
        Assert.Single(group.Flex.Items);
        Assert.Same(Find(frame, "b"), group.Flex.Items[0].Element);
        Assert.Contains(group.Children, c => c is UiMarkupContextMenu);
        Assert.Equal(new FlexRect(0f, 0f, 38f, 40f), Rect(Find(frame, "b")));
    }

    [Fact]
    public void A_longer_checkbox_caption_reflows_its_row()
    {
        var binding = new Binding();
        var (root, frame) = Mount("""
            <group x="0" y="0" w="300" h="20" layout="row">
              <checkbox name="c" text="{Caption}" checked="{On}" onchange="{SetOn}" />
              <button name="b" text="OK" />
            </group>
            """, binding);
        Assert.Equal(31f, Find(frame, "b").Left);

        binding.Caption = "ABCD";
        Frame(root);

        Assert.Equal(45f, Find(frame, "b").Left);
    }
}
