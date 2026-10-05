using AcDream.App.UI;
using AcDream.App.UI.Layout.Flex;

namespace AcDream.App.Tests.UI;

/// <summary>
/// A root panel with <c>layout</c>: the title bar by default, the content
/// area as the flex root, the first measurement at build time (size and
/// minimum), the authored-inputs revision, and the minimum following the
/// content afterwards. Headless metrics: 7 points a character, 14-point
/// lines, 24-point controls; chrome 5 points a side and a 24-point bar.
/// </summary>
public sealed class MarkupFlexWindowTests
{
    private sealed class Binding
    {
        public string Caption { get; set; } = "AB";
    }

    private const string Body = "<button text=\"OK\" /><label text=\"ABCD\" />";

    private static MarkupWindow Window(string attrs, string body = Body, object? binding = null) =>
        MarkupDocument.BuildWindow(
            $"<panel x=\"100\" y=\"50\" {attrs}>{body}</panel>", binding ?? new object(), _ => (0u, 0, 0),
            fallbackTitle: "Demo");

    private static UiRoot Mount(MarkupWindow window, string? name = null)
    {
        var root = new UiRoot { Width = 1280, Height = 720 };
        root.AddChild(window.Frame);
        if (name is not null)
            window.Register(root.WindowManager, name, new PluginWindowVisibilityController(null, startVisible: true));
        Frame(root);
        return root;
    }

    private static void Frame(UiRoot root)
    {
        root.Tick(0, 0);
        ThemeDrawCapture.Draw(root);
    }

    [Theory]
    [InlineData("layout=\"column\"", true)]
    [InlineData("layout=\"column\" titlebar=\"true\"", true)]
    [InlineData("layout=\"column\" titlebar=\"false\"", false)]
    public void A_layout_root_has_the_title_bar_unless_it_says_otherwise(string attrs, bool bar)
    {
        MarkupWindow window = Window(attrs);

        Assert.Equal(bar, window.TitleBar is not null);
        var content = Assert.IsType<UiPluginContentHost>(window.ContentRoot);
        Assert.NotNull(content.Flex);
        Assert.Equal(PluginWindowChrome.Border, content.Left);
        Assert.Equal(bar ? PluginWindowChrome.TitleBarHeight : PluginWindowChrome.Border, content.Top);
    }

    [Fact]
    public void A_root_without_a_size_opens_at_its_content_size_plus_chrome()
    {
        MarkupWindow window = Window("layout=\"column\" gap=\"4\" padding=\"8\"");

        // Content: max(38, 28) + 16 wide; 24 + 4 + 14 + 16 tall.
        var content = (UiPluginContentHost)window.ContentRoot;
        Assert.Equal((54f, 58f), (content.Width, content.Height));
        Assert.Equal((64f, 87f), (window.Frame.Width, window.Frame.Height));
        Assert.Equal((64f, 87f), (window.Frame.MinWidth, window.Frame.MinHeight));
        Assert.Equal(64f, window.TitleBar!.Width);
        Assert.Equal(64f - PluginWindowChrome.TitleBarHeight, window.TitleBar.Close.Left);
    }

    [Fact]
    public void Without_the_bar_the_chrome_is_the_border_alone()
    {
        MarkupWindow window = Window("layout=\"column\" titlebar=\"false\" gap=\"4\" padding=\"8\"");

        Assert.Equal((64f, 68f), (window.Frame.Width, window.Frame.Height));
    }

    [Fact]
    public void An_authored_size_is_kept_and_children_are_laid_out_in_the_content_area()
    {
        MarkupWindow window = Window("layout=\"row\" w=\"300\" h=\"100\" padding=\"8\"");
        Mount(window);

        Assert.Equal((310f, 129f), (window.Frame.Width, window.Frame.Height));
        UiElement ok = window.ContentRoot.Children[0];
        Assert.Equal((8f, 8f, 38f, 84f), (ok.Left, ok.Top, ok.Width, ok.Height));
        Assert.Equal(new System.Numerics.Vector2(100f + 5f + 8f, 50f + 24f + 8f), ok.ScreenPosition);
    }

    [Fact]
    public void An_authored_size_below_the_content_minimum_opens_at_the_minimum()
    {
        MarkupWindow window = Window("layout=\"row\" w=\"20\" h=\"10\"");

        // Content minimum: 38 + 28 wide, 24 tall.
        Assert.Equal((76f, 53f), (window.Frame.Width, window.Frame.Height));
        Assert.Equal((76f, 53f), (window.Frame.MinWidth, window.Frame.MinHeight));
    }

    [Fact]
    public void Authored_minimums_win_over_the_content_minimum()
    {
        MarkupWindow window = Window("layout=\"row\" w=\"300\" h=\"100\" minw=\"30\" minh=\"200\"");

        Assert.Equal((40f, 229f), (window.Frame.MinWidth, window.Frame.MinHeight));
        Assert.Equal((310f, 229f), (window.Frame.Width, window.Frame.Height));
    }

    [Fact]
    public void A_bad_root_size_fails_the_build()
    {
        var ex = Assert.Throws<FormatException>(() => Window("layout=\"row\" w=\"NaN\""));
        Assert.Contains("w=\"NaN\"", ex.Message);
    }

    [Fact]
    public void Container_attributes_on_a_root_without_layout_fail_the_build()
    {
        var ex = Assert.Throws<FormatException>(() => Window("w=\"10\" h=\"10\" gap=\"4\"", body: ""));
        Assert.Contains("gap=\"4\"", ex.Message);
    }

    [Fact]
    public void The_revision_comes_from_the_markup_not_the_content()
    {
        int Revision(string attrs, string caption) =>
            Window(attrs, "<label text=\"{Caption}\" />", new Binding { Caption = caption }).AuthoredGeometryRevision;

        Assert.Equal(Revision("layout=\"row\"", "AB"), Revision("layout=\"row\"", "ABCDEFGH"));
        Assert.NotEqual(Revision("layout=\"row\"", "AB"), Revision("layout=\"column\"", "AB"));
        Assert.NotEqual(Revision("layout=\"row\"", "AB"), Revision("layout=\"row\" w=\"200\"", "AB"));
        Assert.Equal(
            RetailWindowManager.ComputeAuthoredGeometryRevision(
                PluginWindowChrome.AuthoredInputs(System.Xml.Linq.XElement.Parse("<panel layout=\"row\" />"))),
            Revision("layout=\"row\"", "AB"));
    }

    [Fact]
    public void A_longer_caption_raises_the_minimum_and_grows_a_registered_window()
    {
        var binding = new Binding();
        MarkupWindow window = Window("layout=\"row\" w=\"50\" h=\"40\"", "<label text=\"{Caption}\" />", binding);
        UiRoot root = Mount(window, "plugin:demo:main");
        var resized = new List<string>();
        Assert.True(root.WindowManager.TryGet("plugin:demo:main", out RetailWindowHandle handle));
        handle.Resized += _ => resized.Add("resized");
        Assert.Equal(60f, window.Frame.Width);

        binding.Caption = "ABCDEFGHIJ";   // 70 points
        Frame(root);                       // lays out, finds the new minimum
        Frame(root);                       // the tick reports it; the window grows

        Assert.Equal(80f, window.Frame.MinWidth);
        Assert.Equal(80f, window.Frame.Width);
        Assert.Equal(["resized"], resized);
        Frame(root);
        Assert.Equal(70f, window.ContentRoot.Width);
    }

    [Fact]
    public void Chrome_is_outside_the_flex_tree()
    {
        MarkupWindow window = Window("layout=\"column\"");
        var content = (UiPluginContentHost)window.ContentRoot;

        Assert.Equal(2, content.Flex!.Items.Count);
        Assert.All(content.Flex.Items, item => Assert.Same(content, item.Element.Parent));
        Assert.Same(window.Frame, window.TitleBar!.Parent);
    }
}
