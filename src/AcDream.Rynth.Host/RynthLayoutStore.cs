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
/// A disk error is logged and never thrown.
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

    /// <summary>Where the player left the bar, or null.</summary>
    public Vector2? BarPosition
    {
        get { lock (_gate) return _bar; }
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
                }
            }
        }
        catch (Exception error)
        {
            // Anything unreadable (bad JSON, duplicate keys, wrong types, IO) is an empty layout.
            _windows.Clear();
            _bar = null;
            _log($"[Rynth] layout file unreadable ({_path}): {error.Message}");
        }
    }

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
                windows[key] = w;
            }

            var root = new JsonObject { ["version"] = Version };
            if (_bar is { } bar)
                root["bar"] = new JsonObject { ["x"] = bar.X, ["y"] = bar.Y };
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
