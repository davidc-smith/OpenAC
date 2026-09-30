using System.Diagnostics.CodeAnalysis;
using AcDream.App.UI;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Plugins;

/// <summary>
/// How many fonts one plugin may hold and how much memory its own fonts may
/// take. <paramref name="MaximumBytes"/> counts the font files the plugin
/// supplied and the atlases baked from them; the bundled font is shared and
/// costs nothing against it.
/// </summary>
internal readonly record struct PluginFontBudget(
    int MaximumCount,
    long MaximumBytes,
    int MaximumGlyphs,
    float MinimumPixelSize,
    float MaximumPixelSize)
{
    /// <summary>
    /// 16 fonts, 16 MB of the plugin's own fonts, 2048 characters a font,
    /// 6 to 64 pixels: starting points, like the image budget, not
    /// measurements.
    /// </summary>
    public static PluginFontBudget Default { get; } = new(16, 16L * 1024 * 1024, 2048, 6f, 64f);
}

/// <summary>The texture services a font table draws on: glyph atlases in, and back out.</summary>
internal interface IPluginFontBackend
{
    /// <summary>Uploads a single-channel atlas. The handle comes back through <see cref="ReleaseCoverage"/>.</summary>
    uint UploadCoverage(byte[] coverage, int width, int height, string debugName);

    /// <summary>Gives back an atlas from <see cref="UploadCoverage"/>.</summary>
    bool ReleaseCoverage(uint texture);
}

/// <summary>
/// One plugin's fonts: every font it has asked for, counted once per distinct
/// font, size and set of characters, with how many times it is held. Bound
/// to the interface's texture services once those exist and unbound when the
/// interface goes away; unbound, every request answers
/// <see cref="PluginFont.None"/>. Bound, requests must come from the
/// interface thread, like images.
/// </summary>
internal sealed class PluginFontTable : IDisposable
{
    private readonly record struct Key(bool Bundled, string Name, float PixelSize, string Ranges);

    private sealed class Entry(Key key, int id, CanvasFont font, long ownedBytes)
    {
        internal Key Key { get; } = key;
        internal int Id { get; } = id;
        internal CanvasFont Font { get; } = font;

        /// <summary>Zero for the shared bundled font; file plus atlas for the plugin's own.</summary>
        internal long OwnedBytes { get; } = ownedBytes;

        internal int RefCount { get; set; } = 1;

        internal PluginFont Handle => new(Id, Key.PixelSize, Font.LineHeight, Font.Ascent);
    }

    private readonly string _ownerId;
    private readonly Action<string> _report;
    private readonly Dictionary<Key, Entry> _byKey = [];
    private readonly Dictionary<int, Entry> _byId = [];
    private readonly HashSet<string> _reported = [];
    private IPluginFontBackend? _backend;
    private BundledCanvasFontCache? _bundled;
    private int _uiThreadId;
    private int _nextId;
    private long _ownedBytes;
    private bool _disposed;

    internal PluginFontTable(string ownerId, PluginFontBudget budget, Action<string>? report = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(budget.MaximumCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(budget.MaximumBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(budget.MaximumGlyphs);
        _ownerId = ownerId;
        Budget = budget;
        _report = report ?? (line => Serilog.Log.Warning("{Line}", line));
    }

    internal PluginFontBudget Budget { get; }

    internal int Count => _byId.Count;

    internal long OwnedBytes => _ownedBytes;

    internal bool IsBound => _backend is not null;

    internal void Bind(IPluginFontBackend backend, BundledCanvasFontCache bundled, int uiThreadId)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(bundled);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_backend is not null)
            throw new InvalidOperationException("The font table is already bound.");
        _backend = backend;
        _bundled = bundled;
        _uiThreadId = uiThreadId;
    }

    /// <summary>Lets go of every font and forgets the services; held handles stop resolving.</summary>
    internal void Unbind()
    {
        if (_backend is null) return;
        Clear();
        _backend = null;
        _bundled = null;
    }

    internal PluginFont AcquireBundled(float pixelSize)
    {
        if (!TryEnter(out _, out BundledCanvasFontCache? bundled) || !IsSizeAllowed(pixelSize))
            return PluginFont.None;
        var key = new Key(Bundled: true, Name: "", pixelSize, Ranges: "");
        if (TryHoldAgain(key, out PluginFont again))
            return again;
        if (!HasRoomForOneMore(FormattableString.Invariant($"the bundled font at {pixelSize} px")))
            return PluginFont.None;
        CanvasFont? font = bundled.Acquire(pixelSize, out string? failure);
        if (font is null)
        {
            ReportOnce(FormattableString.Invariant($"bundled:{pixelSize}"), FormattableString.Invariant($"the bundled font at {pixelSize} px could not be prepared: {failure}"));
            return PluginFont.None;
        }
        return Add(key, font, ownedBytes: 0L).Handle;
    }

    internal PluginFont AcquireStream(
        string name, Func<Stream> open, float pixelSize, IReadOnlyList<PluginCodepointRange>? ranges)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(open);
        if (!TryEnter(out IPluginFontBackend? backend, out _) || !IsSizeAllowed(pixelSize))
            return PluginFont.None;

        IReadOnlyList<(int First, int Last)> baked = ranges is null
            ? CanvasFontBaker.DefaultRanges
            : [.. ranges.Select(range => (range.First, range.Last))];
        var key = new Key(Bundled: false, name, pixelSize, string.Join(",", baked.Select(r => $"{r.First:X}-{r.Last:X}")));
        if (TryHoldAgain(key, out PluginFont again))
            return again;
        if (!HasRoomForOneMore(FormattableString.Invariant($"font '{name}'")))
            return PluginFont.None;

        long room = Budget.MaximumBytes - _ownedBytes;
        byte[]? bytes;
        try
        {
            using Stream stream = open()
                ?? throw new InvalidOperationException("the stream factory returned null");
            bytes = ReadAtMost(stream, room);
        }
        catch (Exception failure)
        {
            ReportOnce($"read:{name}", $"font '{name}' could not be read: {failure.Message}");
            return PluginFont.None;
        }
        if (bytes is null)
        {
            ReportOnce($"bytes:{name}", FormattableString.Invariant($"font '{name}' would take the plugin's own fonts past the budget of {Budget.MaximumBytes:N0} bytes"));
            return PluginFont.None;
        }

        if (!CanvasFontBaker.TryBake(bytes, pixelSize, baked, Budget.MaximumGlyphs, out CanvasFontBake? bake, out string? why))
        {
            ReportOnce($"bake:{key}", FormattableString.Invariant($"font '{name}' at {pixelSize} px could not be prepared: {why}"));
            return PluginFont.None;
        }
        long owned = bytes.LongLength + (long)bake.AtlasWidth * bake.AtlasHeight;
        if (_ownedBytes + owned > Budget.MaximumBytes)
        {
            ReportOnce($"bytes:{key}", FormattableString.Invariant($"font '{name}' at {pixelSize} px would take the plugin's own fonts to {_ownedBytes + owned:N0} bytes; the budget is {Budget.MaximumBytes:N0}"));
            return PluginFont.None;
        }

        uint texture = backend.UploadCoverage(
            bake.Coverage, bake.AtlasWidth, bake.AtlasHeight, $"plugin-{_ownerId}-font-{name}-{pixelSize}px");
        _ownedBytes += owned;
        return Add(key, new CanvasFont(bake, texture, bytes), owned).Handle;
    }

    /// <summary>
    /// Lets go of one hold; on the last, the plugin's own atlas goes back to
    /// the backend and a bundled hold goes back to the shared cache. False for
    /// a font this table did not issue or has already let go of completely.
    /// </summary>
    internal bool Release(PluginFont font)
    {
        if (!font.IsValid || _disposed) return false;
        ThrowIfWrongThread();
        if (!_byId.TryGetValue(font.Handle, out Entry? entry))
            return false;
        if (--entry.RefCount > 0)
            return true;
        Remove(entry);
        return true;
    }

    internal bool TryResolve(PluginFont font, [NotNullWhen(true)] out CanvasFont? resolved)
    {
        if (font.IsValid && !_disposed && _byId.TryGetValue(font.Handle, out Entry? entry))
        {
            resolved = entry.Font;
            return true;
        }
        resolved = null;
        return false;
    }

    internal void Clear()
    {
        foreach (Entry entry in _byId.Values.ToList())
            Remove(entry);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Clear();
        _backend = null;
        _bundled = null;
        _disposed = true;
    }

    private static byte[]? ReadAtMost(Stream stream, long limit)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[81_920];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > limit)
                return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private bool TryEnter(
        [NotNullWhen(true)] out IPluginFontBackend? backend,
        [NotNullWhen(true)] out BundledCanvasFontCache? bundled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        backend = _backend;
        bundled = _bundled;
        if (backend is null || bundled is null)
            return false;
        ThrowIfWrongThread();
        return true;
    }

    private void ThrowIfWrongThread()
    {
        if (_backend is not null && Environment.CurrentManagedThreadId != _uiThreadId)
        {
            throw new InvalidOperationException(
                "Plugin fonts may only be acquired and released from the interface thread, "
                + "the thread the plugin's own callbacks run on.");
        }
    }

    private bool IsSizeAllowed(float pixelSize)
    {
        if (float.IsFinite(pixelSize)
            && pixelSize >= Budget.MinimumPixelSize
            && pixelSize <= Budget.MaximumPixelSize)
            return true;
        ReportOnce(
            FormattableString.Invariant($"size:{pixelSize}"),
            FormattableString.Invariant($"a font size of {pixelSize} px was refused; sizes run from {Budget.MinimumPixelSize:0.##} to {Budget.MaximumPixelSize:0.##} px"));
        return false;
    }

    private bool TryHoldAgain(Key key, out PluginFont handle)
    {
        if (_byKey.TryGetValue(key, out Entry? held))
        {
            held.RefCount++;
            handle = held.Handle;
            return true;
        }
        handle = PluginFont.None;
        return false;
    }

    private bool HasRoomForOneMore(string what)
    {
        if (_byId.Count < Budget.MaximumCount)
            return true;
        ReportOnce("count", FormattableString.Invariant($"{what} refused: the plugin already holds {Budget.MaximumCount} fonts, which is the most it may"));
        return false;
    }

    private Entry Add(Key key, CanvasFont font, long ownedBytes)
    {
        var entry = new Entry(key, checked(++_nextId), font, ownedBytes);
        _byKey.Add(key, entry);
        _byId.Add(entry.Id, entry);
        return entry;
    }

    private void Remove(Entry entry)
    {
        _byKey.Remove(entry.Key);
        _byId.Remove(entry.Id);
        if (entry.Key.Bundled)
        {
            _bundled?.Release(entry.Font);
            return;
        }
        _ownedBytes -= entry.OwnedBytes;
        _backend?.ReleaseCoverage(entry.Font.AtlasTexture);
        entry.Font.Dispose();
    }

    private void ReportOnce(string key, string message)
    {
        if (_reported.Add(key))
            _report($"Plugin '{_ownerId}': {message}.");
    }
}
