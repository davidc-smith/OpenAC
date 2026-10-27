using AcDream.App.Plugins;
using AcDream.Core.Plugins;

namespace AcDream.App.UI.Hud;

/// <summary>Names of the bundled HUD icons (embedded as <c>AcDream.Hud.&lt;name&gt;.svg</c>).</summary>
public static class HudIcons
{
    public const string Peace = "peace";
    public const string Melee = "melee";
    public const string Missile = "missile";
    public const string Magic = "magic";
    public const string Repeat = "repeat";
    public const string AutoTarget = "autotarget";
    public const string KeepInView = "keepinview";
    public const string Cast = "cast";
    public const string RecallLifestone = "recall-lifestone";
    public const string RecallMarketplace = "recall-marketplace";
    public const string RecallHouse = "recall-house";
    public const string RecallMansion = "recall-mansion";
    public const string RecallAllegiance = "recall-allegiance";
    public const string Search = "search";
    public const string ChevronDown = "chevron-down";

    public static IReadOnlyList<string> All { get; } =
    [
        Peace, Melee, Missile, Magic, Repeat, AutoTarget, KeepInView, Cast,
        RecallLifestone, RecallMarketplace, RecallHouse, RecallMansion, RecallAllegiance,
        Search, ChevronDown,
    ];
}

/// <summary>
/// Bundled SVG icons baked to single-channel coverage textures, one device
/// size per icon at a time (a new size gives the old texture back). Used from
/// the interface thread only. A missing or unusable icon draws nothing and is
/// logged once.
/// </summary>
internal sealed class EmbeddedSvgSpriteCache : IDisposable
{
    private readonly IPluginFontBackend _backend;
    private readonly Action<string> _report;
    private readonly Func<string, byte[]?> _load;
    private readonly Func<SvgIconDocument, int, byte[]?> _bake;
    private readonly Dictionary<string, SvgIconDocument?> _documents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Pixels, uint Texture)> _textures = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);

    public EmbeddedSvgSpriteCache(
        IPluginFontBackend backend,
        Action<string>? report = null,
        Func<string, byte[]?>? load = null,
        Func<SvgIconDocument, int, byte[]?>? bake = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _report = report ?? Console.WriteLine;
        _load = load ?? LoadFromAssembly;
        _bake = bake ?? SvgIconRasterizer.Bake;
    }

    public static byte[]? LoadFromAssembly(string name)
    {
        using Stream? stream = typeof(EmbeddedSvgSpriteCache).Assembly
            .GetManifestResourceStream($"AcDream.Hud.{name}.svg");
        if (stream is null) return null;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>The coverage texture for <paramref name="name"/> baked
    /// <paramref name="devicePixels"/> square, or 0 when it cannot be drawn.</summary>
    public uint Texture(string name, int devicePixels)
    {
        devicePixels = Math.Clamp(devicePixels, 1, SvgIconRasterizer.MaximumSize);
        if (_textures.TryGetValue(name, out var baked) && baked.Pixels == devicePixels)
            return baked.Texture;

        SvgIconDocument? document = Document(name);
        if (document is null) return 0;

        uint texture = 0;
        try
        {
            byte[]? coverage = _bake(document, devicePixels);
            if (coverage is not null)
                texture = _backend.UploadCoverage(coverage, devicePixels, devicePixels, "hud icon " + name);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Report(name, "could not be drawn: " + ex.Message);
            return 0;
        }
        if (texture == 0)
        {
            Report(name, "could not be drawn at this size");
            return 0;
        }

        if (_textures.TryGetValue(name, out var old))
            _backend.ReleaseCoverage(old.Texture);
        _textures[name] = (devicePixels, texture);
        return texture;
    }

    private SvgIconDocument? Document(string name)
    {
        if (_documents.TryGetValue(name, out SvgIconDocument? cached)) return cached;
        SvgIconDocument? document = null;
        byte[]? bytes = _load(name);
        if (bytes is null)
            Report(name, "is not bundled");
        else if (!PluginSvgIcon.TryParse(bytes, out document, out string? reason))
            Report(name, reason ?? "could not be parsed");
        _documents[name] = document;
        return document;
    }

    private void Report(string name, string reason)
    {
        if (_reported.Add(name))
            _report($"[UI] hud icon '{name}' ignored: {reason}");
    }

    public void Dispose()
    {
        foreach ((int _, uint texture) in _textures.Values)
            _backend.ReleaseCoverage(texture);
        _textures.Clear();
    }
}
