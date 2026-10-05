using System.Numerics;
using AcDream.App.UI;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

public sealed class MarkupTitleBarTests
{
    private sealed class Binding
    {
        public int Clicks { get; private set; }
        public Action Go => () => Clicks++;
    }

    private static MarkupWindow Window(string attrs, string body = "", object? binding = null,
        string? fallback = null, PluginUiThemeSettings? themes = null, UiDatFont? font = null) =>
        MarkupDocument.BuildWindow(
            $"<panel x=\"100\" y=\"50\" w=\"300\" h=\"200\" {attrs}>{body}</panel>",
            binding ?? new object(), _ => (0u, 0, 0), datFont: font, themes: themes, fallbackTitle: fallback);

    private static UiRoot Mount(MarkupWindow window)
    {
        var root = new UiRoot { Width = 1280, Height = 720 };
        root.AddChild(window.Frame);
        root.Tick(0, 0);
        return root;
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("titlebar=\"false\"", false)]
    [InlineData("titlebar=\"true\"", true)]
    public void TheBarIsOptIn(string attrs, bool expected)
    {
        MarkupWindow window = Window(attrs, "<label x=\"0\" y=\"0\" text=\"a\" />");
        Assert.Equal(expected, window.TitleBar is not null);
        Assert.Equal(expected, window.Frame.Children.OfType<PluginTitleBar>().Any());
        Assert.Equal(expected, window.Frame.Children.OfType<UiPluginContentHost>().Any());
        if (!expected)
        {
            Assert.Same(window.Frame, window.ContentRoot);
            Assert.IsType<UiLabel>(Assert.Single(window.Frame.Children));
        }
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("True")]
    [InlineData("")]
    public void AnythingButTrueOrFalseIsABuildError(string value)
    {
        var ex = Assert.Throws<FormatException>(() => Window($"titlebar=\"{value}\""));
        Assert.Contains("titlebar", ex.Message);
        Assert.Contains($"\"{value}\"", ex.Message);
    }

    [Fact]
    public void WithABar_TheFrameIsTheContentAreaPlusChrome()
    {
        MarkupWindow window = Window("titlebar=\"true\" minw=\"250\"");
        UiNineSlicePanel frame = window.Frame;
        Assert.Equal((100f, 50f, 310f, 229f), (frame.Left, frame.Top, frame.Width, frame.Height));
        Assert.Equal((260f, 229f), (frame.MinWidth, frame.MinHeight));
        Assert.Collection(frame.Children,
            c => Assert.IsType<PluginTitleBar>(c),
            c => Assert.IsType<UiPluginContentHost>(c));
        UiElement host = window.ContentRoot;
        Assert.IsType<UiPluginContentHost>(host);
        Assert.Equal((5f, 24f, 300f, 200f), (host.Left, host.Top, host.Width, host.Height));
    }

    [Fact]
    public void ChildrenArePlacedFromTheContentAreaCorner_AndStillClick()
    {
        var binding = new Binding();
        MarkupWindow window = Window("titlebar=\"true\"",
            "<button x=\"0\" y=\"0\" w=\"80\" h=\"20\" text=\"Go\" onclick=\"{Go}\" />", binding);
        UiRoot root = Mount(window);
        var button = Assert.IsType<UiSimpleButton>(Assert.Single(window.ContentRoot.Children));
        Assert.Equal((0f, 0f), (button.Left, button.Top));
        Assert.Same(button, root.Pick(100 + 5 + 40, 50 + 24 + 10));
        root.OnMouseDown(UiMouseButton.Left, 145, 84);
        root.OnMouseUp(UiMouseButton.Left, 145, 84);
        Assert.Equal(1, binding.Clicks);
    }

    [Theory]
    [InlineData("title=\"Buff Bot\"", "Registered", "Buff Bot")]
    [InlineData("", "Registered", "Registered")]
    [InlineData("title=\"\"", "Registered", "Registered")]
    [InlineData("", null, "")]
    public void TheBarShowsTheMarkupTitleElseTheRegistrationTitle(string attrs, string? fallback, string expected)
    {
        MarkupWindow window = Window($"titlebar=\"true\" {attrs}", fallback: fallback);
        Assert.Equal(expected, window.TitleBar!.Title);
        Assert.Empty(window.ContentRoot.Children.OfType<UiLabel>());
        Assert.Empty(window.Frame.Children.OfType<UiLabel>());
    }

    [Fact]
    public void DraggingTheBarMovesTheWindow()
    {
        MarkupWindow window = Window("titlebar=\"true\" title=\"T\"");
        UiRoot root = Mount(window);
        root.OnMouseDown(UiMouseButton.Left, 160, 62);
        root.OnMouseMove(200, 92);
        root.OnMouseUp(UiMouseButton.Left, 200, 92);
        Assert.Equal((140f, 80f), (window.Frame.Left, window.Frame.Top));
    }

    [Fact]
    public void TheCloseButtonComesFirstInTabOrder()
    {
        MarkupWindow window = Window("titlebar=\"true\"",
            "<button x=\"0\" y=\"0\" w=\"80\" h=\"20\" text=\"Go\" />");
        UiRoot root = Mount(window);
        var button = Assert.IsType<UiSimpleButton>(Assert.Single(window.ContentRoot.Children));
        root.SetKeyboardFocus(button);
        root.OnKeyDown((int)Key.Tab);
        Assert.Same(window.TitleBar!.Close, root.KeyboardFocus);
        root.OnKeyDown((int)Key.Tab);
        Assert.Same(button, root.KeyboardFocus);
    }

    [Fact]
    public void TheRevisionIsAuthoredInputsWithABar_AndTodaysHashWithout()
    {
        MarkupWindow bar = Window("titlebar=\"true\"");
        Assert.Equal(
            RetailWindowManager.ComputeAuthoredGeometryRevision(
                ["300", "200", null, null, null, null, null, "true", "1"]),
            bar.AuthoredGeometryRevision);

        MarkupWindow plain = Window("");
        UiNineSlicePanel f = plain.Frame;
        Assert.Equal(
            RetailWindowManager.ComputeAuthoredGeometryRevision(f.Width, f.Height, f.MinWidth, f.MinHeight, f.Resizable),
            plain.AuthoredGeometryRevision);
    }

    [Fact]
    public void ResizingKeepsTheInsets_AndAnchoredContentFollows()
    {
        MarkupWindow window = Window("titlebar=\"true\" resizable=\"true\"",
            "<button x=\"210\" y=\"170\" w=\"80\" h=\"20\" text=\"OK\" anchor=\"right bottom\" />");
        UiRoot root = Mount(window);
        RetailWindowHandle handle = root.RegisterWindow("w", window.Frame, window.ContentRoot);
        handle.ResizeTo(400f, 300f);
        var (renderer, ctx) = ThemeDrawCapture.Context(1280, 720);
        root.Draw(ctx);
        UiElement host = window.ContentRoot;
        Assert.Equal((5f, 24f, 390f, 271f), (host.Left, host.Top, host.Width, host.Height));
        var ok = Assert.Single(host.Children);
        Assert.Equal((300f, 241f), (ok.Left, ok.Top));
    }

    [Fact]
    public void Themed_TheBarFollowsTheThemeAndBack()
    {
        var classic = BundledUiFont.Bake(12).CreateFont(1);
        var bold = BundledUiFont.Bake(16, BundledUiFontWeight.SemiBold).CreateFont(3);
        var settings = new PluginUiThemeSettings(modernTitleFont: new(() => bold));
        MarkupWindow window = Window("titlebar=\"true\" title=\"T\" theme=\"plugin\"", themes: settings, font: classic);
        UiRoot root = Mount(window);
        var panel = Assert.IsType<UiPluginMarkupPanel>(window.Frame);
        PluginTitleBar bar = window.TitleBar!;
        Assert.True(panel.HasTitle);
        Assert.Null(bar.ThemePalette);
        Assert.Same(classic, bar.DatFont);
        Assert.True(bar.Outline);
        Vector4 classicColor = bar.TextColor;

        settings.Theme = PluginUiTheme.Brass;
        root.Tick(0.016, 1);
        Assert.Same(PluginUiPalette.Brass, bar.ThemePalette);
        Assert.Same(bold, bar.DatFont);
        Assert.Equal(PluginUiPalette.Brass.Text, bar.TextColor);
        Assert.False(bar.Outline);

        settings.Theme = PluginUiTheme.Classic;
        root.Tick(0.016, 2);
        Assert.Null(bar.ThemePalette);
        Assert.Same(classic, bar.DatFont);
        Assert.Equal(classicColor, bar.TextColor);
        Assert.True(bar.Outline);
    }

    [Fact]
    public void Themed_TheCloseButtonFollowsALiveThemeSwitchAfterBuild()
    {
        var settings = new PluginUiThemeSettings();
        MarkupWindow window = Window("titlebar=\"true\" title=\"T\" theme=\"plugin\"", themes: settings);
        UiRoot root = Mount(window);
        PluginCloseButton close = window.TitleBar!.Close;
        void Frame(int n)
        {
            root.Tick(0.016, n);
            var (_, ctx) = ThemeDrawCapture.Context(1280, 720);
            root.Draw(ctx);
        }
        Frame(1);
        float left = close.Left;
        Assert.Equal(5f, close.Top);

        settings.Theme = PluginUiTheme.Moss;
        Frame(2);
        Assert.Equal(3f, close.Top);
        Assert.Equal(left, close.Left);

        settings.Theme = PluginUiTheme.Classic;
        Frame(3);
        Assert.Equal(5f, close.Top);
    }
}
