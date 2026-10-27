using System.Numerics;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

/// <summary>
/// <see cref="UiElement.ContentViewport"/> and <see cref="UiElement.ContentOffset"/>:
/// while a viewport is set, every child but a <see cref="UiElement.ScrollChrome"/>
/// one is moved by the offset and clipped to the viewport, in drawing,
/// hit-testing and screen positions alike. Without one nothing changes.
/// </summary>
public sealed class UiContentOffsetTests
{
    private static readonly Vector4 Red = new(1f, 0f, 0f, 1f);
    private static readonly Vector4 Blue = new(0f, 0f, 1f, 1f);

    private static (UiRoot Root, UiPanel Host, UiPanel Content, UiPanel Bar) Tree()
    {
        var root = new UiRoot { Width = 400, Height = 300 };
        var host = new UiPanel { Left = 10, Top = 20, Width = 100, Height = 50, BackgroundColor = Vector4.Zero, BorderColor = Vector4.Zero };
        var content = new UiPanel { Left = 0, Top = 60, Width = 40, Height = 30, BackgroundColor = Red, BorderColor = Vector4.Zero };
        var bar = new UiPanel { Left = 84, Top = 0, Width = 16, Height = 50, BackgroundColor = Blue, BorderColor = Vector4.Zero, ScrollChrome = true };
        host.AddChild(content);
        host.AddChild(bar);
        root.AddChild(host);
        return (root, host, content, bar);
    }

    [Fact]
    public void Without_a_viewport_the_offset_moves_nothing()
    {
        var (_, host, content, _) = Tree();
        host.ContentOffset = new Vector2(0f, 30f);

        Assert.Equal(new Vector2(10f, 80f), content.ScreenPosition);
    }

    [Fact]
    public void A_viewport_moves_content_but_not_chrome_in_screen_positions()
    {
        var (_, host, content, bar) = Tree();
        host.ContentViewport = new Vector2(84f, 50f);
        host.ContentOffset = new Vector2(0f, 30f);

        Assert.Equal(new Vector2(10f, 50f), content.ScreenPosition);
        Assert.Equal(new Vector2(94f, 20f), bar.ScreenPosition);
    }

    [Fact]
    public void Hit_testing_follows_the_offset_and_stops_at_the_viewport()
    {
        var (root, host, content, bar) = Tree();
        host.ContentViewport = new Vector2(84f, 50f);
        host.ContentOffset = new Vector2(0f, 30f);

        Assert.Same(content, root.Pick(15, 65));
        Assert.Same(bar, root.Pick(95, 30));
        // Content is on screen at x 10..50 only; x 88 is in the bar's column.
        Assert.NotSame(content, root.Pick(98, 65));
    }

    [Fact]
    public void Content_scrolled_out_of_the_viewport_is_not_hit()
    {
        var (root, host, content, _) = Tree();
        host.ContentViewport = new Vector2(84f, 50f);

        // Unscrolled, the content sits at y 80..110, below the 50-point viewport.
        Assert.NotSame(content, root.Pick(15, 85));
    }

    [Fact]
    public void Drawing_offsets_and_clips_content_but_not_chrome()
    {
        var (root, host, _, _) = Tree();
        host.ContentViewport = new Vector2(84f, 50f);
        host.ContentOffset = new Vector2(0f, 30f);

        (var renderer, var context) = ThemeDrawCapture.Context(root.Width, root.Height);
        root.Draw(context);
        var vertices = ThemeDrawCapture.Vertices(renderer);
        var red = vertices.Where(v => v.Color == Red).Select(v => v.Position.Y).ToList();
        var blue = vertices.Where(v => v.Color == Blue).Select(v => v.Position.Y).ToList();

        // Content drawn from y 50 (80 - 30), cut at the viewport's bottom (70).
        Assert.Equal(50f, red.Min(), 2);
        Assert.Equal(70f, red.Max(), 2);
        Assert.Equal(20f, blue.Min(), 2);
        Assert.Equal(70f, blue.Max(), 2);
    }
}
