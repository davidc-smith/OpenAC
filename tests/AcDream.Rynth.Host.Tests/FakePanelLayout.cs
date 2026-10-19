using System.Numerics;
using RynthCore.Engine.ImGuiBackend;

namespace AcDream.Rynth.Host.Tests;

/// <summary>An in-memory IPanelLayout that records every call.</summary>
internal sealed class FakePanelLayout : IPanelLayout
{
    public Dictionary<string, PanelPlacement> Saved { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<(string Key, Vector2 Display), PanelRect> SavedFor { get; } = new();
    public List<(string Key, PanelRect Rect, Vector2 Display)> RectCalls { get; } = new();
    public List<(string Key, bool Open)> OpenCalls { get; } = new();

    public PanelPlacement? Get(string key) => Saved.TryGetValue(key, out PanelPlacement p) ? p : null;

    public PanelRect? GetRect(string key, Vector2 display) =>
        SavedFor.TryGetValue((key, display), out PanelRect rect) ? rect : Get(key)?.Rect;

    public void SetRect(string key, PanelRect rect, Vector2 display)
    {
        RectCalls.Add((key, rect, display));
        SavedFor[(key, display)] = rect;
        Saved.TryGetValue(key, out PanelPlacement prior);
        Saved[key] = prior with { Rect = rect };
    }

    public void SetOpen(string key, bool open)
    {
        OpenCalls.Add((key, open));
        Saved.TryGetValue(key, out PanelPlacement prior);
        Saved[key] = prior with { Open = open };
    }
}
