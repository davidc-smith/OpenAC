using System;
using System.Collections.Generic;
using AcDream.App.UI.Layout;
using AcDream.Plugin.Abstractions;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.UI;

/// <summary>A plugin canvas as the position persistence sees it.</summary>
internal interface IRememberedCanvas
{
    /// <summary>The canvas's key in the settings file: <c>"&lt;pluginId&gt;/&lt;canvasId&gt;"</c>.</summary>
    string Key { get; }

    PluginCanvasAnchor Anchor { get; set; }

    PluginPoint Offset { get; set; }

    (int Width, int Height) Size { get; }

    /// <summary>Whether the canvas holds a press right now, so a drag is still going.</summary>
    bool IsHoldingPointer { get; }
}

/// <summary>
/// Remembers where the player left each plugin canvas that asked for it
/// (<see cref="PluginCanvasDescriptor.RemembersPosition"/>), per character.
/// A plugin moves its canvas by setting its offset as often as it likes, so
/// a change is written only once the canvas has stayed put for
/// <see cref="SettleSeconds"/> with no press held on it; a canvas that goes
/// away, or a character switch, writes what is pending at once.
/// </summary>
internal sealed class PluginCanvasPositionPersistence : IDisposable
{
    internal const double SettleSeconds = 0.5;

    private sealed class Entry(IRememberedCanvas canvas)
    {
        public IRememberedCanvas Canvas { get; } = canvas;
        public (PluginCanvasAnchor Anchor, PluginPoint Offset) Baseline;
        public (PluginCanvasAnchor Anchor, PluginPoint Offset) Seen;
        public double ChangedAt;
    }

    private readonly SettingsStore? _store;
    private readonly Func<string> _characterKey;
    private readonly Func<(float Width, float Height)> _screenSize;
    private readonly List<Entry> _entries = [];
    private string? _restoredCharacter;
    private double _now;
    private bool _disposed;

    public PluginCanvasPositionPersistence(
        SettingsStore? store,
        Func<string> characterKey,
        Func<(float Width, float Height)> screenSize)
    {
        _store = store;
        _characterKey = characterKey ?? throw new ArgumentNullException(nameof(characterKey));
        _screenSize = screenSize ?? throw new ArgumentNullException(nameof(screenSize));
    }

    /// <summary>Whether a canvas registered with <paramref name="descriptor"/> is remembered.</summary>
    internal static bool Tracks(PluginCanvasDescriptor descriptor) =>
        descriptor.RemembersPosition && !descriptor.InMarkup;

    /// <summary>
    /// Starts remembering <paramref name="canvas"/>. Put back at once when a
    /// character's positions were restored and that character is still the
    /// one logged in; otherwise it waits for the next <see cref="RestoreAll"/>.
    /// </summary>
    public void Add(IRememberedCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (_disposed || _entries.Exists(e => ReferenceEquals(e.Canvas, canvas))) return;
        var entry = new Entry(canvas);
        Rebase(entry);
        _entries.Add(entry);
        if (_restoredCharacter is { } character && character == _characterKey())
            Restore(entry, character);
    }

    /// <summary>Stops remembering <paramref name="canvas"/>, writing a move not yet saved.</summary>
    public void Remove(IRememberedCanvas canvas)
    {
        int index = _entries.FindIndex(e => ReferenceEquals(e.Canvas, canvas));
        if (index < 0) return;
        Entry entry = _entries[index];
        _entries.RemoveAt(index);
        if (Current(entry) != entry.Baseline)
            Save(entry);
    }

    /// <summary>
    /// Puts back the logged-in character's saved positions, after writing
    /// what is still pending for the character before.
    /// </summary>
    public void RestoreAll()
    {
        if (_disposed) return;
        FlushPending();
        string character = _characterKey();
        _restoredCharacter = CanPersist(character) ? character : null;
        foreach (Entry entry in _entries)
        {
            if (_restoredCharacter is { } restored)
                Restore(entry, restored);
            else
                Rebase(entry);
        }
    }

    /// <summary>
    /// Once a frame: notices moves, and writes each one that has stayed put
    /// for <see cref="SettleSeconds"/> with no press held on the canvas.
    /// </summary>
    public void Tick(double deltaSeconds)
    {
        if (_disposed) return;
        _now += deltaSeconds;
        foreach (Entry entry in _entries)
        {
            var current = Current(entry);
            if (current != entry.Seen)
            {
                entry.Seen = current;
                entry.ChangedAt = _now;
            }
            if (current != entry.Baseline
                && _now - entry.ChangedAt >= SettleSeconds
                && !entry.Canvas.IsHoldingPointer)
                Save(entry);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        FlushPending();
        _entries.Clear();
        _disposed = true;
    }

    private void FlushPending()
    {
        foreach (Entry entry in _entries)
        {
            if (Current(entry) != entry.Baseline)
                Save(entry);
        }
    }

    private void Restore(Entry entry, string character)
    {
        PluginCanvasPosition? saved = null;
        try { saved = _store?.LoadPluginCanvasPosition(character, entry.Canvas.Key); }
        catch (Exception ex)
        {
            Console.WriteLine($"settings: plugin canvas position load failed [{entry.Canvas.Key}]: {ex.Message}");
        }
        if (saved is { } position
            && Enum.TryParse(position.Anchor, ignoreCase: false, out PluginCanvasAnchor anchor)
            && Enum.IsDefined(anchor))
        {
            (int width, int height) = entry.Canvas.Size;
            (float screenWidth, float screenHeight) = _screenSize();
            entry.Canvas.Anchor = anchor;
            entry.Canvas.Offset = PluginCanvasPlacement.ClampOnScreen(
                anchor, new PluginPoint(position.X, position.Y), width, height, screenWidth, screenHeight);
        }
        Rebase(entry);
    }

    private void Save(Entry entry)
    {
        var current = Current(entry);
        if (_restoredCharacter is not { } character || _store is null) return;
        try
        {
            _store.SavePluginCanvasPosition(character, entry.Canvas.Key,
                new PluginCanvasPosition(current.Anchor.ToString(), current.Offset.X, current.Offset.Y));
            entry.Baseline = current;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"settings: plugin canvas position save failed [{entry.Canvas.Key}]: {ex.Message}");
            entry.Baseline = current;
        }
    }

    private void Rebase(Entry entry)
    {
        entry.Baseline = Current(entry);
        entry.Seen = entry.Baseline;
        entry.ChangedAt = _now;
    }

    private static (PluginCanvasAnchor Anchor, PluginPoint Offset) Current(Entry entry) =>
        (entry.Canvas.Anchor, entry.Canvas.Offset);

    private static bool CanPersist(string key) =>
        !string.IsNullOrWhiteSpace(key)
        && !string.Equals(key, "default", StringComparison.OrdinalIgnoreCase);
}
