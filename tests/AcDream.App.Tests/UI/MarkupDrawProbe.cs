using System.Numerics;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

/// <summary>Draws a widget headlessly and counts the solid quads it emitted.</summary>
internal static class MarkupDrawProbe
{
    public static TextRenderer Draw(UiElement element, float w = 400f, float h = 300f)
    {
        var renderer = new TextRenderer(new RecordingGpuDevice(), new NullFrameSource(), "unused");
        renderer.Begin(new Vector2(w, h));
        element.DrawSelfAndChildren(new UiRenderContext(renderer, new Vector2(w, h)));
        return renderer;
    }

    /// <summary>Untextured quads (6 vertices × 8 floats) whose colour matches within 0.01.</summary>
    public static int CountQuads(TextRenderer renderer, Vector4 color)
    {
        int count = 0;
        foreach (var s in renderer.DebugSpriteSegmentVerts)
        {
            if (s.Texture != 0u)
                continue;
            for (int q = 0; q + 48 <= s.Verts.Count; q += 48)
            {
                if (MathF.Abs(s.Verts[q + 4] - color.X) < 0.01f
                    && MathF.Abs(s.Verts[q + 5] - color.Y) < 0.01f
                    && MathF.Abs(s.Verts[q + 6] - color.Z) < 0.01f
                    && MathF.Abs(s.Verts[q + 7] - color.W) < 0.01f)
                {
                    count++;
                }
            }
        }
        return count;
    }

    private sealed class NullFrameSource : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame => null;
    }
}
