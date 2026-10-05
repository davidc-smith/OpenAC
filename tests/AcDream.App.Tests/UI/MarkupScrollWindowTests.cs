using System.Xml.Linq;
using AcDream.App.UI;
using AcDream.App.UI.Layout.Flex;

namespace AcDream.App.Tests.UI;

/// <summary>
/// <c>scroll</c> on the root panel: the window's content area scrolls, for a
/// flex root and for an absolute window with the title bar; the window's
/// minimum along a scrolling axis is a small viewport; the attribute needs a
/// content area; and it enters the saved-size revision only when present.
/// Headless metrics: 24-point controls; chrome 5 points a side and a 24-point bar.
/// </summary>
public sealed class MarkupScrollWindowTests
{
    private const string Buttons =
        "<button text=\"One\" /><button text=\"Two\" /><button text=\"Three\" /><button text=\"Four\" />";

    private static MarkupWindow Window(string attrs, string body = Buttons) =>
        MarkupDocument.BuildWindow(
            $"<panel x=\"100\" y=\"50\" {attrs}>{body}</panel>", new object(), _ => (0u, 0, 0), fallbackTitle: "Demo");

    private static UiRoot Mount(MarkupWindow window)
    {
        var root = new UiRoot { Width = 1280, Height = 720 };
        root.AddChild(window.Frame);
        window.Register(root.WindowManager, "plugin:demo:main", new PluginWindowVisibilityController(null, startVisible: true));
        root.Tick(0, 0);
        ThemeDrawCapture.Draw(root);
        return root;
    }

    [Fact]
    public void A_scrolling_flex_root_scrolls_its_content_area()
    {
        MarkupWindow window = Window("layout=\"column\" w=\"200\" h=\"60\" scroll=\"y\"");
        UiRoot root = Mount(window);

        var content = (UiPluginContentHost)window.ContentRoot;
        UiScrollArea area = content.ScrollArea!;
        Assert.True(content.Flex!.Node.ScrollY);
        Assert.True(area.Vertical.Visible);
        Assert.Equal(96, area.Y.ContentHeight);
        Assert.Equal(184f, content.Flex.Items[0].Element.Width);

        root.OnMouseMove(150, 100);
        root.OnScroll(0, -1, shift: false);

        Assert.Equal(36, area.Y.ScrollY);   // 48 asked, clamped to 96 - 60
    }

    [Fact]
    public void A_scrolling_flex_root_can_shrink_to_a_small_viewport()
    {
        MarkupWindow window = Window("layout=\"column\" scroll=\"y\"");

        // Content minimum: a 40-point viewport tall, the widest button plus the bar wide.
        Assert.Equal(FlexLayout.MinimumScrollViewport + 29f, window.Frame.MinHeight);
    }

    [Fact]
    public void A_titled_absolute_window_scrolls_its_content_area()
    {
        MarkupWindow window = Window(
            "w=\"200\" h=\"100\" titlebar=\"true\" scroll=\"y\"",
            "<button x=\"0\" y=\"0\" w=\"50\" h=\"24\" text=\"Top\" /><button x=\"0\" y=\"180\" w=\"50\" h=\"24\" text=\"Far\" />");
        Mount(window);

        var content = (UiPluginContentHost)window.ContentRoot;
        Assert.Null(content.Flex);
        Assert.True(content.ScrollArea!.Vertical.Visible);
        Assert.Equal(204, content.ScrollArea.Y.ContentHeight);
    }

    [Fact]
    public void Scroll_on_a_root_without_a_content_area_is_a_build_error()
    {
        var error = Assert.Throws<FormatException>(() => Window("w=\"200\" h=\"100\" scroll=\"y\"", ""));
        Assert.Contains("needs layout=\"row|column\" or titlebar=\"true\"", error.Message);
    }

    [Fact]
    public void Scroll_enters_the_revision_only_when_present()
    {
        string?[] plain = PluginWindowChrome.AuthoredInputs(XElement.Parse("<panel layout=\"row\" />"));
        string?[] scrolling = PluginWindowChrome.AuthoredInputs(XElement.Parse("<panel layout=\"row\" scroll=\"y\" />"));

        Assert.Equal(9, plain.Length);
        Assert.Equal(plain, scrolling[..^1]);
        Assert.Equal("scroll=y", scrolling[^1]);
        Assert.NotEqual(
            Window("layout=\"column\"").AuthoredGeometryRevision,
            Window("layout=\"column\" scroll=\"y\"").AuthoredGeometryRevision);
    }
}
