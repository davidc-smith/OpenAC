using System.Runtime.InteropServices;
using ImGuiNET;

namespace AcDream.App.Extensions.Overlay;

/// <summary>
/// Fonts extensions asked to merge into the overlay's font (icon fonts). The
/// atlas reads a font's bytes and glyph ranges whenever it is rebuilt, so
/// both are kept as native copies until the overlay is disposed.
/// </summary>
internal sealed unsafe class OverlayFonts : IDisposable
{
    private readonly Action<string> _log;
    private readonly List<Entry> _entries = new();

    public OverlayFonts(Action<string> log) => _log = log ?? throw new ArgumentNullException(nameof(log));

    /// <summary>Bumped on every font added; the renderer rebuilds its atlas when it changes.</summary>
    public int Version { get; private set; }

    public void Add(byte[] ttf, string glyphs, float scale)
    {
        if (ttf is null || ttf.Length == 0 || string.IsNullOrEmpty(glyphs) || !float.IsFinite(scale) || scale <= 0)
        {
            _log("[ImGui] merged font ignored: it needs font bytes, glyphs and a positive scale.");
            return;
        }

        void* data = NativeMemory.Alloc((nuint)ttf.Length);
        ttf.AsSpan().CopyTo(new Span<byte>(data, ttf.Length));
        ushort[] ranges = Ranges(glyphs);
        ushort* nativeRanges = (ushort*)NativeMemory.Alloc((nuint)(ranges.Length * sizeof(ushort)));
        ranges.AsSpan().CopyTo(new Span<ushort>(nativeRanges, ranges.Length));
        _entries.Add(new Entry((IntPtr)data, ttf.Length, (IntPtr)nativeRanges, scale));
        Version++;
    }

    /// <summary>Merges every font into the atlas's last added font, at <paramref name="basePixels"/> times its scale.</summary>
    public void MergeInto(ImFontAtlasPtr atlas, float basePixels)
    {
        foreach (Entry entry in _entries)
        {
            float pixels = MathF.Max(1f, MathF.Round(basePixels * entry.Scale));
            ImFontConfigPtr config = ImGuiNative.ImFontConfig_ImFontConfig();
            try
            {
                config.MergeMode = true;
                config.FontDataOwnedByAtlas = false;
                config.PixelSnapH = true;
                config.OversampleH = 1;
                config.OversampleV = 1;
                config.GlyphMinAdvanceX = pixels;
                atlas.AddFontFromMemoryTTF(entry.Data, entry.Length, pixels, config, entry.Ranges);
            }
            catch (Exception error)
            {
                _log($"[ImGui] merged font skipped: {error.GetType().Name}: {error.Message}");
            }
            finally
            {
                config.Destroy();
            }
        }
    }

    /// <summary>ImGui glyph ranges for the code points in <paramref name="glyphs"/>: pairs, consecutive ones joined, 0-terminated.</summary>
    internal static ushort[] Ranges(string glyphs)
    {
        var points = glyphs.Select(c => (ushort)c).Where(c => c != 0).Distinct().Order().ToArray();
        var ranges = new List<ushort>();
        for (int i = 0; i < points.Length; i++)
        {
            ushort start = points[i];
            while (i + 1 < points.Length && points[i + 1] == points[i] + 1)
                i++;
            ranges.Add(start);
            ranges.Add(points[i]);
        }

        ranges.Add(0);
        return ranges.ToArray();
    }

    public void Dispose()
    {
        foreach (Entry entry in _entries)
        {
            NativeMemory.Free((void*)entry.Data);
            NativeMemory.Free((void*)entry.Ranges);
        }

        _entries.Clear();
    }

    private readonly record struct Entry(IntPtr Data, int Length, IntPtr Ranges, float Scale);
}
