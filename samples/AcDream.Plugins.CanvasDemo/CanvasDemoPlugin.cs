using AcDream.Plugin.Abstractions;

namespace AcDream.Plugins.CanvasDemo;

/// <summary>
/// One canvas in the middle of the screen with a sample of everything a
/// painter draws: the interface font, the bundled font at two sizes, shapes
/// with anti-aliased edges, hard-edged lines and an image. It exists to be
/// looked at -- for example to compare how sharp a canvas is on a
/// high-density display -- and does nothing else.
/// </summary>
public sealed class CanvasDemoPlugin : IAcDreamPlugin
{
    private const int CanvasWidth = 360;
    private const int CanvasHeight = 220;

    private static readonly PluginColor Panel = new(16, 20, 28, 220);
    private static readonly PluginColor Accent = new(90, 170, 255);
    private static readonly PluginColor Warm = new(255, 140, 60);

    private IPluginHost? _host;
    private IPluginCanvas? _canvas;
    private PluginFont _body = PluginFont.None;
    private PluginFont _title = PluginFont.None;
    private PluginImage _checker = PluginImage.None;

    public void Initialize(IPluginHost host) => _host = host;

    public void Enable()
    {
        if (_host is not { } host) return;
        _canvas = host.Ui.RegisterCanvas(
            new PluginCanvasDescriptor("demo", CanvasWidth, CanvasHeight) { Anchor = PluginCanvasAnchor.Center },
            Paint);
        host.Events.Tick += OnTick;
    }

    public void Disable()
    {
        if (_host is { } host)
            host.Events.Tick -= OnTick;
        _canvas?.Dispose();
        _canvas = null;
    }

    /// <summary>Fonts and images can only be asked for once the interface is up, and never while painting.</summary>
    private void OnTick(double deltaSeconds)
    {
        if (_host is not { } host) return;
        bool changed = false;
        if (!_title.IsValid && host.Ui.Fonts.IsAvailable)
        {
            _title = host.Ui.Fonts.Bundled(20);
            _body = host.Ui.Fonts.Bundled(13);
            changed = true;
        }
        if (!_checker.IsValid && host.Ui.Images.IsAvailable)
        {
            _checker = host.Ui.Images.FromStream("checker.bmp", () => new MemoryStream(Checkerboard()));
            changed = true;
        }
        if (changed)
            _canvas?.Invalidate();
    }

    private void Paint(IPluginPainter painter)
    {
        painter.Clear(PluginColor.Transparent);
        var bounds = new PluginRect(0, 0, painter.Width, painter.Height);
        painter.FillRoundedRect(bounds, PluginCornerRadii.Uniform(10), Panel);
        painter.StrokeRoundedRect(new PluginRect(0.5, 0.5, painter.Width - 1, painter.Height - 1), PluginCornerRadii.Uniform(10), Accent, 1f);

        painter.DrawText("Noto Sans 20 px: Sharp canvases", new PluginPoint(14, 10), PluginColor.White, _title);
        painter.DrawText("Noto Sans 13 px: The quick brown fox jumps over 0123456789", new PluginPoint(14, 38), PluginColor.White, _body);
        painter.DrawText("Interface font: The quick brown fox jumps", new PluginPoint(14, 60), PluginColor.White);

        // Shapes: anti-aliased edges.
        painter.FillCircle(new PluginPoint(34, 110), 18, Accent);
        painter.StrokeEllipse(new PluginRect(62, 92, 56, 36), PluginColor.White, 1f);
        painter.FillPolygon([new PluginPoint(134, 128), new PluginPoint(156, 90), new PluginPoint(178, 128)], Warm);
        painter.FillRoundedRect(new PluginRect(192, 92, 60, 36), PluginCornerRadii.Uniform(8), new PluginColor(60, 200, 90));
        painter.FillRectGradient(
            new PluginRect(14, 142, 238, 12), Accent, new PluginColor(Accent.R, Accent.G, Accent.B, 0), PluginGradientDirection.Horizontal);

        // Hard-edged lines, straight and slanted.
        painter.DrawLine(new PluginPoint(14, 168), new PluginPoint(252, 168), PluginColor.White, 1f);
        painter.DrawLine(new PluginPoint(14, 200), new PluginPoint(252, 176), PluginColor.White, 1f);

        // A hairline one screen pixel wide, whatever the display.
        double hairline = 1.0 / painter.PixelScale;
        painter.FillRect(new PluginRect(14, 206, 238, hairline), Accent);
        painter.DrawText(
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Pixel scale {painter.PixelScale:0.##}"),
            new PluginPoint(274, 190), new PluginColor(200, 200, 200), _body);

        // An 8 x 8 image shown at 64 x 64: how the host samples art.
        painter.DrawImage(_checker, new PluginRect(274, 92, 64, 64), PluginColor.White);
        painter.DrawText("8x8 at 64", new PluginPoint(274, 160), new PluginColor(200, 200, 200), _body);
    }

    /// <summary>An 8 x 8 checkerboard as a 32-bit BMP, so the sample ships no binary files.</summary>
    private static byte[] Checkerboard()
    {
        const int size = 8;
        const int pixelBytes = size * size * 4;
        const int headerBytes = 14 + 40;
        var bmp = new byte[headerBytes + pixelBytes];
        using var writer = new BinaryWriter(new MemoryStream(bmp));
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(bmp.Length);
        writer.Write(0);
        writer.Write(headerBytes);
        writer.Write(40);
        writer.Write(size);
        writer.Write(size);
        writer.Write((short)1);
        writer.Write((short)32);
        writer.Write(0);
        writer.Write(pixelBytes);
        writer.Write(2835);
        writer.Write(2835);
        writer.Write(0);
        writer.Write(0);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool light = (x + y) % 2 == 0;
                writer.Write(light ? (byte)230 : (byte)40); // blue
                writer.Write(light ? (byte)230 : (byte)40); // green
                writer.Write(light ? (byte)230 : (byte)200); // red
                writer.Write((byte)255);
            }
        }
        return bmp;
    }
}
