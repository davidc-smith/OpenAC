using RynthCore.Engine.ImGuiBackend;

namespace AcDream.Rynth.Host.Tests;

/// <summary>An in-memory IPanelLayout that records every call.</summary>
internal sealed class FakePanelLayout : IPanelLayout
{
    public Dictionary<string, PanelPlacement> Saved { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(string Key, PanelRect Rect)> RectCalls { get; } = new();
    public List<(string Key, bool Open)> OpenCalls { get; } = new();

    public PanelPlacement? Get(string key) => Saved.TryGetValue(key, out PanelPlacement p) ? p : null;

    public void SetRect(string key, PanelRect rect)
    {
        RectCalls.Add((key, rect));
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
