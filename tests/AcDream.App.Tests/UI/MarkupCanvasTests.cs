using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

/// <summary>
/// <c>&lt;canvas&gt;</c> through the markup builder: an empty, id-carrying leaf
/// placed by <c>x</c>/<c>y</c>/<c>w</c>/<c>h</c> or by flex, listed on the
/// window so the interface can host the plugin's canvas in it, with a missing
/// or repeated id refused. Headless metrics: 14-point lines.
/// </summary>
public sealed class MarkupCanvasTests
{
    private sealed class Binding
    {
        public bool Shown { get; set; } = true;
    }

    private static MarkupWindow Build(string body, object? binding = null) =>
        MarkupDocument.BuildWindow(
            $"<panel x=\"0\" y=\"0\" w=\"300\" h=\"200\">{body}</panel>", binding ?? new object(), _ => (0u, 0, 0));

    private static MarkupWindow Mount(string body, object? binding = null)
    {
        MarkupWindow window = Build(body, binding);
        var root = new UiRoot { Width = 1280, Height = 720 };
        root.AddChild(window.Frame);
        root.Tick(0, 0);
        ThemeDrawCapture.Draw(root);
        return window;
    }

    private static (float, float, float, float) Rect(UiElement e) => (e.Left, e.Top, e.Width, e.Height);

    [Fact]
    public void An_absolute_canvas_is_an_empty_leaf_at_its_authored_rectangle()
    {
        MarkupWindow window = Mount("""<canvas id="map" x="10" y="20" w="100" h="60" />""");

        UiMarkupCanvas canvas = Assert.Single(window.Canvases);
        Assert.Equal("map", canvas.CanvasId);
        Assert.Equal("map", canvas.Name);
        Assert.Equal((10f, 20f, 100f, 60f), Rect(canvas));
        Assert.Null(canvas.Hosted);
        Assert.Empty(canvas.Children);
    }

    [Fact]
    public void Name_works_like_id_and_canvases_are_listed_in_document_order()
    {
        MarkupWindow window = Build("""
            <canvas name="a" x="0" y="0" w="10" h="10" />
            <group x="0" y="20" w="100" h="100">
              <canvas id="b" x="0" y="0" w="10" h="10" />
            </group>
            """);

        Assert.Equal(["a", "b"], window.Canvases.Select(c => c.CanvasId));
    }

    [Fact]
    public void Grow_fills_what_a_column_leaves()
    {
        MarkupWindow window = Mount("""
            <group x="0" y="0" w="300" h="200" layout="column">
              <label text="Map" />
              <canvas id="map" grow="1" />
            </group>
            """);

        Assert.Equal((0f, 14f, 300f, 186f), Rect(Assert.Single(window.Canvases)));
    }

    [Fact]
    public void W_and_h_size_it_in_a_row()
    {
        MarkupWindow window = Mount("""
            <group x="0" y="0" w="300" h="40" layout="row" align="start">
              <canvas id="map" w="80" h="40" />
            </group>
            """);

        Assert.Equal((0f, 0f, 80f, 40f), Rect(Assert.Single(window.Canvases)));
    }

    [Fact]
    public void A_canvas_without_an_id_is_refused()
    {
        var error = Assert.Throws<FormatException>(() => Build("""<canvas x="0" y="0" w="10" h="10" />"""));
        Assert.Contains("<canvas> needs an id", error.Message);
    }

    [Fact]
    public void The_same_id_twice_in_one_window_is_refused()
    {
        var error = Assert.Throws<FormatException>(() => Build("""
            <canvas id="map" x="0" y="0" w="10" h="10" />
            <group x="0" y="20" w="100" h="100"><canvas id="map" x="0" y="0" w="10" h="10" /></group>
            """));
        Assert.Contains("more than one <canvas id=\"map\">", error.Message);
    }

    [Fact]
    public void Visible_binds_as_on_every_element()
    {
        var binding = new Binding { Shown = false };
        MarkupWindow window = Mount("""<canvas id="map" x="0" y="0" w="10" h="10" visible="{Shown}" />""", binding);

        Assert.False(Assert.Single(window.Canvases).Visible);
    }
}
