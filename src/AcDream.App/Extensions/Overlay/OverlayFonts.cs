using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;

namespace AcDream.App.Extensions.Overlay;

/// <summary>
/// Fonts extensions asked to merge into the overlay's font (icon fonts). The
/// atlas reads a font's bytes and glyph ranges whenever it is rebuilt, so
/// both are kept as native copies until the overlay is disposed. A disposed
/// list takes no more fonts (the overlay doesn't restart after Dispose).
/// </summary>
internal sealed unsafe class OverlayFonts : IDisposable
{
    private readonly Action<string> _log;
    private readonly List<Entry> _entries = new();
    private readonly List<float> _sizes = new();
    private readonly Dictionary<float, IntPtr> _baked = new();
    private bool _disposed;

    public OverlayFonts(Action<string> log) => _log = log ?? throw new ArgumentNullException(nameof(log));

    /// <summary>Bumped on every font added; the renderer rebuilds its atlas when it changes.</summary>
    public int Version { get; private set; }

    /// <summary>How many fonts are kept (their native copies are live).</summary>
    internal int Count => _entries.Count;

    public void Add(byte[] ttf, string glyphs, float scale)
    {
        if (_disposed)
        {
            _log("[ImGui] merged font ignored: the overlay is disposed.");
            return;
        }

        ushort[] ranges = string.IsNullOrEmpty(glyphs) ? [0] : Ranges(glyphs);
        if (ttf is null || ttf.Length == 0 || ranges.Length < 3 || !float.IsFinite(scale) || scale <= 0)
        {
            _log("[ImGui] merged font ignored: it needs font bytes, glyphs and a positive scale.");
            return;
        }

        if (_entries.Any(entry => entry.Matches(ttf, ranges, scale)))
        {
            _log("[ImGui] merged font ignored: the same font, glyphs and scale are already merged.");
            return;
        }

        void* data = NativeMemory.Alloc((nuint)ttf.Length);
        ttf.AsSpan().CopyTo(new Span<byte>(data, ttf.Length));
        ushort* nativeRanges = (ushort*)NativeMemory.Alloc((nuint)(ranges.Length * sizeof(ushort)));
        ranges.AsSpan().CopyTo(new Span<ushort>(nativeRanges, ranges.Length));
        _entries.Add(new Entry((IntPtr)data, ttf.Length, (IntPtr)nativeRanges, ranges.Length, scale));
        Version++;
    }

    /// <summary>The extra point sizes asked for through <see cref="Sized"/>, in order.</summary>
    public IReadOnlyList<float> SizesAsked => _sizes;

    /// <summary>
    /// The overlay's font at <paramref name="points"/> (whole points, 4-72) in the atlas
    /// as last built, or zero. A size not built yet is asked for, and the next build has it.
    /// </summary>
    public IntPtr Sized(float points)
    {
        if (!float.IsFinite(points) || points < MinPoints || points > MaxPoints || _disposed)
            return IntPtr.Zero;
        points = MathF.Round(points);
        if (_baked.TryGetValue(points, out IntPtr font))
            return font;
        if (!_sizes.Contains(points))
        {
            _sizes.Add(points);
            Version++;
        }

        return IntPtr.Zero;
    }

    private const float MinPoints = 4f;
    private const float MaxPoints = 72f;

    /// <summary>The atlas is being rebuilt: its fonts are gone.</summary>
    internal void ClearBaked() => _baked.Clear();

    /// <summary>The atlas now has the font at <paramref name="points"/>; the first one built at a size is kept.</summary>
    internal unsafe void Baked(float points, ImFontPtr font) => _baked.TryAdd(points, (IntPtr)font.NativePtr);

    /// <summary>
    /// Merges every font into the atlas's last added font, at <paramref name="basePoints"/> times
    /// its scale, rasterised at <paramref name="density"/> pixels per point.
    /// </summary>
    public void MergeInto(ImFontAtlasPtr atlas, float basePoints, float density = 1f)
    {
        foreach (Entry entry in _entries)
        {
            // Whole pixels at the rasterised density, given back in points.
            float pixels = MathF.Max(1f, MathF.Round(basePoints * entry.Scale * density));
            float points = pixels / density;
            ImFontConfigPtr config = ImGuiNative.ImFontConfig_ImFontConfig();
            try
            {
                config.MergeMode = true;
                config.FontDataOwnedByAtlas = false;
                config.PixelSnapH = true;
                config.OversampleH = 1;
                config.OversampleV = 1;
                config.RasterizerDensity = density;
                config.GlyphMinAdvanceX = points;
                // Icon glyphs sit a quarter of their size above the text's middle; this lines them up with capitals.
                config.GlyphOffset = new Vector2(0, MathF.Round(pixels * 0.25f) / density);
                atlas.AddFontFromMemoryTTF(entry.Data, entry.Length, points, config, entry.Ranges);
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
        _disposed = true;
        foreach (Entry entry in _entries)
        {
            NativeMemory.Free((void*)entry.Data);
            NativeMemory.Free((void*)entry.Ranges);
        }

        _entries.Clear();
        _sizes.Clear();
        _baked.Clear();
    }

    private readonly record struct Entry(IntPtr Data, int Length, IntPtr Ranges, int RangesLength, float Scale)
    {
        public bool Matches(byte[] ttf, ushort[] ranges, float scale) =>
            scale == Scale && ttf.Length == Length && ranges.Length == RangesLength
            && new ReadOnlySpan<ushort>((void*)Ranges, RangesLength).SequenceEqual(ranges)
            && new ReadOnlySpan<byte>((void*)Data, Length).SequenceEqual(ttf);
    }
}
