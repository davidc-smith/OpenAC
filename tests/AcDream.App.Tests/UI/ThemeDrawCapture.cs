using System.Numerics;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

/// <summary>Draws interface elements into a recording renderer and reads back what was drawn.</summary>
internal static class ThemeDrawCapture
{
    private sealed class FrameSource : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame => null;
    }

    internal static (TextRenderer Renderer, UiRenderContext Context) Context(
        float width = 600f, float height = 600f, float pixelScale = 1f)
    {
        var renderer = new TextRenderer(new RecordingGpuDevice(), new FrameSource(), "unused");
        renderer.Begin(new Vector2(width, height));
        var context = new UiRenderContext(renderer, new Vector2(width, height));
        context.Begin(new Vector2(width, height), null, pixelScale);
        return (renderer, context);
    }

    /// <summary>Every vertex drawn, in order: position and colour (x, y, u, v, r, g, b, a per vertex).</summary>
    internal static List<(Vector2 Position, Vector4 Color)> Vertices(TextRenderer renderer)
    {
        var result = new List<(Vector2, Vector4)>();
        foreach (var segment in renderer.DebugSpriteSegmentVerts)
        {
            var f = segment.Verts;
            for (int i = 0; i + TextRenderer.FloatsPerVertex <= f.Count; i += TextRenderer.FloatsPerVertex)
                result.Add((new Vector2(f[i], f[i + 1]), new Vector4(f[i + 4], f[i + 5], f[i + 6], f[i + 7])));
        }
        return result;
    }

    /// <summary>Draws a root once and returns every float the renderer recorded, with texture ids.</summary>
    internal static float[] Draw(UiRoot root, float pixelScale = 1f)
    {
        (TextRenderer renderer, UiRenderContext context) = Context(root.Width, root.Height, pixelScale);
        root.Draw(context);
        return renderer.DebugSpriteSegmentVerts
            .SelectMany(s => s.Verts.Prepend((float)s.Texture))
            .ToArray();
    }

    /// <summary>Whether any opaque-ish vertex carries this colour's RGB (alpha is ignored).</summary>
    internal static bool HasColor(IEnumerable<(Vector2 Position, Vector4 Color)> vertices, Vector4 color,
        float tolerance = 0.004f) =>
        vertices.Any(v => v.Color.W > 0.05f
            && MathF.Abs(v.Color.X - color.X) <= tolerance
            && MathF.Abs(v.Color.Y - color.Y) <= tolerance
            && MathF.Abs(v.Color.Z - color.Z) <= tolerance);
}
