using AcDream.App.Plugins;
using AcDream.Core.Plugins;

namespace AcDream.App.UI;

/// <summary>
/// The interface's SVG plugin icons: each file parsed once, baked lazily per
/// device size on first draw, shared by every dock button that shows it and
/// given back when the last one goes. Keyed by resolved path, length and
/// write time, so a hot reload that changes the file loads it again. Used
/// from the interface thread only.
/// </summary>
internal sealed class PluginSvgIconCache : IDisposable
{
    internal readonly record struct Key(string Path, long Length, DateTime WrittenUtc);

    private readonly IPluginFontBackend _backend;
    private readonly Action<string> _report;
    private readonly Func<SvgIconDocument, int, byte[]?> _bake;
    private readonly Dictionary<Key, PluginSvgIconEntry> _entries = [];
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private long _clock;

    internal PluginSvgIconCache(
        IPluginFontBackend backend,
        Action<string>? report = null,
        Func<SvgIconDocument, int, byte[]?>? bake = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _report = report ?? Console.WriteLine;
        _bake = bake ?? SvgIconRasterizer.Bake;
    }

    /// <summary>Device pixels for an icon <paramref name="extentPoints"/> wide at
    /// <paramref name="pixelScale"/>, capped at the largest bake.</summary>
    internal static int DevicePixels(float extentPoints, float pixelScale) =>
        DevicePixelsChecked(extentPoints * pixelScale);

    private static int DevicePixelsChecked(float pixels)
    {
        if (float.IsNaN(pixels) || pixels <= 0f) return 1;
        if (pixels >= SvgIconRasterizer.MaximumSize) return SvgIconRasterizer.MaximumSize;
        return Math.Clamp((int)MathF.Ceiling(pixels - 0.001f), 1, SvgIconRasterizer.MaximumSize);
    }

    internal int EntryCount => _entries.Count;

    /// <summary>
    /// The icon at <paramref name="fullPath"/>, with one more holder, or null when it
    /// cannot be used; the reason is logged once under <paramref name="label"/>
    /// (<c>pluginId/relative/path.svg</c>).
    /// </summary>
    internal PluginSvgIconEntry? Acquire(string label, string fullPath)
    {
        Key key;
        try
        {
            var info = new FileInfo(fullPath);
            key = new Key(info.FullName, info.Length, info.LastWriteTimeUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Reject(label, fullPath, "could not be read: " + ex.Message);
            return null;
        }

        if (!_entries.TryGetValue(key, out PluginSvgIconEntry? entry))
        {
            SvgIconDocument? document;
            string? reason;
            bool loaded;
            try
            {
                loaded = PluginSvgIcon.TryLoad(fullPath, out document, out reason);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                document = null;
                reason = "could not be parsed: " + ex.Message;
                loaded = false;
            }
            if (!loaded || document is null)
            {
                Reject(label, fullPath, reason ?? "could not be loaded");
                return null;
            }
            entry = new PluginSvgIconEntry(this, key, label, document);
            _entries.Add(key, entry);
        }
        entry.Holders++;
        return entry;
    }

    /// <summary>Logs, once per file, why an icon was not used.</summary>
    internal void Reject(string label, string path, string reason)
    {
        if (_reported.Add(path))
            _report($"[UI] plugin icon '{label}' ignored: {reason}");
    }

    public void Dispose()
    {
        foreach (PluginSvgIconEntry entry in _entries.Values) entry.ReleaseTextures();
        _entries.Clear();
    }

    /// <summary>Gives a texture back; a backend that throws is reported once per icon and
    /// never lets the caller fail.</summary>
    private void TryRelease(string label, string path, uint texture)
    {
        try
        {
            _backend.ReleaseCoverage(texture);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (_reported.Add(path + "\0release"))
                _report($"[UI] plugin icon '{label}' ignored: could not release a texture: " + ex.Message);
        }
    }

    internal sealed class PluginSvgIconEntry
    {
        private const int MaximumSizes = 2;

        private readonly PluginSvgIconCache _owner;
        private readonly Key _key;
        private readonly string _label;
        private readonly SvgIconDocument _document;
        private readonly Dictionary<int, (uint Texture, long Used)> _textures = [];

        internal PluginSvgIconEntry(PluginSvgIconCache owner, Key key, string label, SvgIconDocument document)
        {
            _owner = owner;
            _key = key;
            _label = label;
            _document = document;
        }

        internal int Holders { get; set; }

        /// <summary>Set once a bake or upload failed; the icon is never tried again.</summary>
        internal bool Failed { get; private set; }

        internal int BakedSizes => _textures.Count;

        /// <summary>The coverage texture for a <paramref name="devicePixels"/>² bake, baking it on
        /// first use; 0 once the icon has failed.</summary>
        internal uint TextureFor(int devicePixels)
        {
            if (Failed) return 0;
            devicePixels = Math.Clamp(devicePixels, 1, SvgIconRasterizer.MaximumSize);
            long now = ++_owner._clock;
            if (_textures.TryGetValue(devicePixels, out (uint Texture, long Used) baked))
            {
                _textures[devicePixels] = (baked.Texture, now);
                return baked.Texture;
            }

            uint texture = 0;
            string reason = "could not be drawn at this size";
            try
            {
                byte[]? coverage = _owner._bake(_document, devicePixels);
                if (coverage is not null)
                {
                    if (!coverage.AsSpan().ContainsAnyExcept((byte)0))
                    {
                        reason = "draws nothing at this size";
                    }
                    else
                    {
                        reason = "could not be uploaded";
                        texture = _owner._backend.UploadCoverage(coverage, devicePixels, devicePixels, "plugin icon " + _label);
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                reason += ": " + ex.Message;
                texture = 0;
            }
            if (texture == 0)
            {
                Failed = true;
                _owner._report($"[UI] plugin icon '{_label}' ignored: " + reason);
                ReleaseTextures();
                return 0;
            }

            if (_textures.Count >= MaximumSizes)
            {
                int oldest = _textures.MinBy(pair => pair.Value.Used).Key;
                uint evicted = _textures[oldest].Texture;
                _textures.Remove(oldest);
                _owner.TryRelease(_label, _key.Path, evicted);
            }
            _textures[devicePixels] = (texture, now);
            return texture;
        }

        /// <summary>Drops one holder; the last one gives every texture back.</summary>
        internal void Release()
        {
            if (Holders <= 0) return;
            if (--Holders > 0) return;
            ReleaseTextures();
            _owner._entries.Remove(_key);
        }

        internal void ReleaseTextures()
        {
            (uint Texture, long Used)[] held = [.. _textures.Values];
            _textures.Clear();
            foreach ((uint texture, _) in held) _owner.TryRelease(_label, _key.Path, texture);
        }
    }
}
