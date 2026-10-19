using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using RynthCore.Engine.ImGuiBackend;

namespace AcDream.Rynth.Host;

/// <summary>
/// The script windows' and the bar's saved layout: one JSON file per install
/// (<see cref="FileName"/> in the folder the client gives the extension).
/// Without a folder the layout lives in memory only. Every change is written
/// at once, since changes come once per finished drag, resize, open or close.
/// A disk error is logged and never thrown. Window rectangles and the bar's spot
/// are kept per display size (the client's size in points), so a window left at
/// the bottom of the game's display isn't moved by the smaller character list.
/// </summary>
internal sealed class RynthLayoutStore : IPanelLayout
{
    internal const string FileName = "rynth-layout.json";
    private const int Version = 1;
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly object _gate = new();
    private readonly string? _path;
    private readonly Action<string> _log;
    private readonly Dictionary<string, PanelPlacement> _windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, PanelRect>> _rectsByDisplay = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Vector2> _barByDisplay = new(StringComparer.Ordinal);
    private Vector2? _bar;

    public RynthLayoutStore(string? directory, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
        if (string.IsNullOrWhiteSpace(directory))
            return;
        _path = Path.Combine(directory, FileName);
        Load();
    }

    /// <summary>Where the player last left the bar, on any display, or null.</summary>
    public Vector2? BarPosition
    {
        get { lock (_gate) return _bar; }
    }

    /// <summary>Where the player left the bar on a display of this size, else <see cref="BarPosition"/>.</summary>
    public Vector2? BarPositionFor(Vector2 display)
    {
        lock (_gate) return _barByDisplay.TryGetValue(DisplayKey(display), out Vector2 pos) ? pos : _bar;
    }

    public void SetBarPosition(Vector2 pos)
    {
        lock (_gate)
        {
            if (!IsFinite(pos) || _bar == pos)
                return;
            _bar = pos;
            SaveLocked();
        }
    }

    public void SetBarPosition(Vector2 pos, Vector2 display)
    {
        lock (_gate)
        {
            if (!IsFinite(pos) || !IsFinite(display))
                return;
            string size = DisplayKey(display);
            if (_bar == pos && _barByDisplay.TryGetValue(size, out Vector2 prior) && prior == pos)
                return;
            _bar = pos;
            _barByDisplay[size] = pos;
            SaveLocked();
        }
    }

    public PanelRect? GetRect(string key, Vector2 display)
    {
        lock (_gate)
        {
            if (_rectsByDisplay.TryGetValue(key, out var sizes) && sizes.TryGetValue(DisplayKey(display), out PanelRect rect))
                return rect;
            return _windows.TryGetValue(key, out PanelPlacement placement) ? placement.Rect : null;
        }
    }

    public void SetRect(string key, PanelRect rect, Vector2 display)
    {
        lock (_gate)
        {
            if (!IsFinite(rect.Pos) || !IsFinite(rect.Size) || !IsFinite(display))
                return;
            string size = DisplayKey(display);
            if (!_rectsByDisplay.TryGetValue(key, out var sizes))
                _rectsByDisplay[key] = sizes = new Dictionary<string, PanelRect>(StringComparer.Ordinal);
            bool known = _windows.TryGetValue(key, out PanelPlacement prior);
            if (known && prior.Rect == rect && sizes.TryGetValue(size, out PanelRect old) && old == rect)
                return;
            sizes[size] = rect;
            _windows[key] = prior with { Rect = rect };
            SaveLocked();
        }
    }

    /// <summary>"2560x1377": a display size in whole points.</summary>
    private static string DisplayKey(Vector2 display) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{MathF.Round(display.X)}x{MathF.Round(display.Y)}");

    public PanelPlacement? Get(string key)
    {
        lock (_gate) return _windows.TryGetValue(key, out PanelPlacement placement) ? placement : null;
    }

    public void SetRect(string key, PanelRect rect)
    {
        lock (_gate)
        {
            if (!IsFinite(rect.Pos) || !IsFinite(rect.Size))
                return;
            bool known = _windows.TryGetValue(key, out PanelPlacement prior);
            if (known && prior.Rect == rect)
                return;
            _windows[key] = prior with { Rect = rect };
            SaveLocked();
        }
    }

    public void SetOpen(string key, bool open)
    {
        lock (_gate)
        {
            bool known = _windows.TryGetValue(key, out PanelPlacement prior);
            if (known && prior.Open == open)
                return;
            _windows[key] = prior with { Open = open };
            SaveLocked();
        }
    }

    private static bool IsFinite(Vector2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
                return;
            JsonNode? root = JsonNode.Parse(File.ReadAllText(_path));
            if (root?["bar"] is JsonObject bar
                && TryRead(bar, "x", out float bx) && TryRead(bar, "y", out float by))
                _bar = new Vector2(bx, by);
            if (root?["bar"]?["displays"] is JsonObject barSizes)
            {
                foreach ((string size, JsonNode? node) in barSizes)
                {
                    if (node is JsonObject b && TryRead(b, "x", out float x) && TryRead(b, "y", out float y))
                        _barByDisplay[size] = new Vector2(x, y);
                }
            }
            if (root?["windows"] is JsonObject windows)
            {
                foreach ((string key, JsonNode? node) in windows)
                {
                    if (node is not JsonObject w)
                        continue;
                    PanelRect? rect = TryRead(w, "x", out float x) && TryRead(w, "y", out float y)
                        && TryRead(w, "w", out float width) && TryRead(w, "h", out float height)
                        ? new PanelRect(new Vector2(x, y), new Vector2(width, height))
                        : null;
                    _windows[key] = new PanelPlacement(rect, w["open"]?.GetValue<bool>() ?? false);
                    if (w["displays"] is JsonObject sizes)
                    {
                        var bySize = new Dictionary<string, PanelRect>(StringComparer.Ordinal);
                        foreach ((string size, JsonNode? sizeNode) in sizes)
                        {
                            if (sizeNode is JsonObject r && ReadRect(r) is { } sized)
                                bySize[size] = sized;
                        }

                        _rectsByDisplay[key] = bySize;
                    }
                }
            }
        }
        catch (Exception error)
        {
            // Anything unreadable (bad JSON, duplicate keys, wrong types, IO) is an empty layout.
            _windows.Clear();
            _rectsByDisplay.Clear();
            _barByDisplay.Clear();
            _bar = null;
            _log($"[Rynth] layout file unreadable ({_path}): {error.Message}");
        }
    }

    private static PanelRect? ReadRect(JsonObject node) =>
        TryRead(node, "x", out float x) && TryRead(node, "y", out float y)
        && TryRead(node, "w", out float width) && TryRead(node, "h", out float height)
            ? new PanelRect(new Vector2(x, y), new Vector2(width, height))
            : null;

    private static JsonObject WriteRect(PanelRect rect) =>
        new() { ["x"] = rect.Pos.X, ["y"] = rect.Pos.Y, ["w"] = rect.Size.X, ["h"] = rect.Size.Y };

    /// <summary>Reads a finite number; a missing or non-finite value fails.</summary>
    private static bool TryRead(JsonObject node, string name, out float value)
    {
        value = 0f;
        if (node[name] is not JsonValue json)
            return false;
        value = json.GetValue<float>();
        return float.IsFinite(value);
    }

    private void SaveLocked()
    {
        if (_path is null)
            return;
        string tmp = _path + ".tmp";
        try
        {
            var windows = new JsonObject();
            foreach ((string key, PanelPlacement placement) in _windows)
            {
                var w = new JsonObject();
                if (placement.Rect is { } rect)
                {
                    w["x"] = rect.Pos.X;
                    w["y"] = rect.Pos.Y;
                    w["w"] = rect.Size.X;
                    w["h"] = rect.Size.Y;
                }

                w["open"] = placement.Open;
                if (_rectsByDisplay.TryGetValue(key, out var sizes) && sizes.Count > 0)
                {
                    var bySize = new JsonObject();
                    foreach ((string size, PanelRect sized) in sizes)
                        bySize[size] = WriteRect(sized);
                    w["displays"] = bySize;
                }

                windows[key] = w;
            }

            var root = new JsonObject { ["version"] = Version };
            if (_bar is { } bar)
            {
                var b = new JsonObject { ["x"] = bar.X, ["y"] = bar.Y };
                if (_barByDisplay.Count > 0)
                {
                    var bySize = new JsonObject();
                    foreach ((string size, Vector2 pos) in _barByDisplay)
                        bySize[size] = new JsonObject { ["x"] = pos.X, ["y"] = pos.Y };
                    b["displays"] = bySize;
                }

                root["bar"] = b;
            }
            root["windows"] = windows;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(tmp, root.ToJsonString(Indented));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception error)
        {
            _log($"[Rynth] layout not saved ({_path}): {error.Message}");
            try { File.Delete(tmp); }
            catch (Exception) { /* a leftover temp file is harmless */ }
        }
    }
}
