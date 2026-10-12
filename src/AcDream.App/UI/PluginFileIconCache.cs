using System.Diagnostics.CodeAnalysis;
using AcDream.Core.Plugins;
using AcDream.Core.Textures;

namespace AcDream.App.UI;

/// <summary>
/// The PNG and JPEG images plugin markup names by path (<c>&lt;icon file="..."/&gt;</c>):
/// each file decoded and uploaded once, shared by every element that shows it, and given
/// back when the interface goes. A file that changes on disk (a hot reload) is loaded again
/// and its old texture given back. A plugin may hold at most a fixed number of images.
/// Used from the interface thread only.
/// </summary>
internal sealed class PluginFileIconCache : IDisposable
{
    /// <summary>One loaded image. <see cref="Texture"/> drops to 0 once it is given back,
    /// so an element still holding the entry draws nothing instead of a dead texture.</summary>
    internal sealed class Entry(uint texture, int width, int height, long length, DateTime writtenUtc)
    {
        internal uint Texture { get; private set; } = texture;
        internal int Width { get; } = width;
        internal int Height { get; } = height;
        internal long Length { get; } = length;
        internal DateTime WrittenUtc { get; } = writtenUtc;

        internal void Drop() => Texture = 0u;
    }

    private readonly record struct Key(string PluginId, string FullPath);

    internal delegate bool PathResolver(
        string pluginDirectory, string relativePath,
        [NotNullWhen(true)] out string? fullPath, [NotNullWhen(false)] out string? reason);

    internal delegate bool ImageLoader(
        string fullPath, [NotNullWhen(true)] out DecodedTexture? texture, [NotNullWhen(false)] out string? reason);

    /// <summary>Which files a cache takes and what it calls them in the log.</summary>
    internal sealed record Rules(string Noun, PathResolver Resolve, ImageLoader Load)
    {
        /// <summary>Markup images: PNG or JPEG, up to 512 pixels a side and 1 MiB.</summary>
        internal static Rules MarkupImages { get; } =
            new("image", PluginImageFile.TryResolvePath, PluginImageFile.TryLoad);

        /// <summary>A window's own dock icon: the <c>icon.png</c> rules, at any path.</summary>
        internal static Rules DockIcons { get; } =
            new("icon", PluginIconFile.TryResolvePath, PluginIconFile.TryLoadFile);
    }

    public const int DefaultMaximumPerPlugin = 256;

    private readonly Func<byte[], int, int, string, uint> _upload;
    private readonly Func<uint, bool> _release;
    private readonly Action<string> _report;
    private readonly int _maximumPerPlugin;
    private readonly Rules _rules;
    private readonly Dictionary<Key, Entry> _entries = [];
    private readonly Dictionary<string, int> _countByPlugin = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private bool _disposed;

    internal PluginFileIconCache(
        Func<byte[], int, int, string, uint> upload,
        Func<uint, bool> release,
        Action<string>? report = null,
        int maximumPerPlugin = DefaultMaximumPerPlugin,
        Rules? rules = null)
    {
        _upload = upload ?? throw new ArgumentNullException(nameof(upload));
        _release = release ?? throw new ArgumentNullException(nameof(release));
        _report = report ?? Console.WriteLine;
        _maximumPerPlugin = maximumPerPlugin;
        _rules = rules ?? Rules.MarkupImages;
    }

    /// <summary>
    /// The image at <paramref name="relativePath"/> in <paramref name="pluginDirectory"/>, or
    /// null when it cannot be used; the reason is logged once per file and reason.
    /// </summary>
    internal Entry? Acquire(string pluginId, string pluginDirectory, string relativePath)
    {
        if (_disposed) return null;
        string label = $"{pluginId}/{relativePath.Replace('\\', '/')}";
        if (!_rules.Resolve(pluginDirectory, relativePath, out string? fullPath, out string? reason))
        {
            Reject(label, reason);
            return null;
        }

        long length;
        DateTime writtenUtc;
        try
        {
            var info = new FileInfo(fullPath);
            (length, writtenUtc) = (info.Length, info.LastWriteTimeUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException)
        {
            Reject(label, "could not be read: " + ex.Message);
            return null;
        }

        var key = new Key(pluginId, fullPath);
        if (_entries.TryGetValue(key, out Entry? held))
        {
            if (held.Length == length && held.WrittenUtc == writtenUtc)
                return held;
            Remove(key, held);
        }

        if (_countByPlugin.GetValueOrDefault(pluginId) >= _maximumPerPlugin)
        {
            Reject($"{pluginId}/*", $"the plugin already holds {_maximumPerPlugin} {_rules.Noun}s, the most it may");
            return null;
        }

        if (!_rules.Load(fullPath, out DecodedTexture? decoded, out reason))
        {
            Reject(label, reason);
            return null;
        }

        uint texture;
        try
        {
            texture = _upload(decoded.Rgba8, decoded.Width, decoded.Height, $"plugin-{label}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Reject(label, "could not be uploaded: " + ex.Message);
            return null;
        }
        if (texture == 0u)
        {
            Reject(label, "could not be uploaded");
            return null;
        }

        var entry = new Entry(texture, decoded.Width, decoded.Height, length, writtenUtc);
        _entries[key] = entry;
        _countByPlugin[pluginId] = _countByPlugin.GetValueOrDefault(pluginId) + 1;
        return entry;
    }

    /// <summary>Logs <paramref name="reason"/> for <paramref name="label"/>, once.</summary>
    internal void Reject(string label, string reason)
    {
        if (_reported.Add(label + "\n" + reason))
            _report($"[UI] plugin {_rules.Noun} '{label}' ignored: {reason}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach ((Key key, Entry entry) in _entries.ToArray())
            Remove(key, entry);
    }

    private void Remove(Key key, Entry entry)
    {
        _entries.Remove(key);
        _countByPlugin[key.PluginId]--;
        uint texture = entry.Texture;
        entry.Drop();
        try
        {
            _release(texture);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _report($"[UI] plugin {_rules.Noun} texture {texture} could not be released: {ex.Message}");
        }
    }
}
