using System.Collections.Generic;
using AcDream.App.Plugins;

namespace AcDream.App.UI.Layout;

/// <summary>
/// Pairs plugin canvases registered with <c>InMarkup</c> with the
/// <c>&lt;canvas&gt;</c> elements of the plugins' mounted windows, by plugin
/// and canvas id. Either side may arrive first; the first element mounted
/// hosts a canvas, and when its window goes the canvas moves to the next one
/// or waits. A hosted canvas gets an element of its own each time it is
/// hosted, made by the interface the way a screen canvas's is, and gives its
/// targets back each time it leaves.
///
/// <para>What shows nothing says why once: a canvas waiting for its element,
/// an element whose canvas another window already shows, and an element
/// naming a canvas that was registered to sit on the screen.</para>
/// </summary>
internal sealed class PluginMarkupCanvases
{
    private sealed class Slot(string ownerId, UiMarkupCanvas element)
    {
        internal string OwnerId { get; } = ownerId;
        internal UiMarkupCanvas Element { get; } = element;
        internal bool Reported { get; set; }
    }

    private sealed class Entry(PluginCanvasRegistration registration)
    {
        internal PluginCanvasRegistration Registration { get; } = registration;
        internal Slot? Slot { get; set; }
        internal PluginCanvasElement? Element { get; set; }
        // Once per registration, however often it comes back to waiting.
        internal bool ReportedWaiting { get; set; }
    }

    private readonly Func<PluginCanvasRegistration, PluginCanvasElement> _createElement;
    private readonly Func<string, string, PluginCanvasRegistration?> _findCanvas;
    private readonly Action<string> _report;
    // Both in arrival order: the first element mounted is the first offered.
    private readonly List<Slot> _slots = [];
    private readonly List<Entry> _entries = [];

    internal PluginMarkupCanvases(
        Func<PluginCanvasRegistration, PluginCanvasElement> createElement,
        Func<string, string, PluginCanvasRegistration?> findCanvas,
        Action<string> report)
    {
        _createElement = createElement ?? throw new ArgumentNullException(nameof(createElement));
        _findCanvas = findCanvas ?? throw new ArgumentNullException(nameof(findCanvas));
        _report = report ?? throw new ArgumentNullException(nameof(report));
    }

    /// <summary>A canvas registered with <c>InMarkup</c>, drained by the interface; hosted at once if its element is mounted.</summary>
    internal void AddCanvas(PluginCanvasRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        _entries.Add(new Entry(registration));
        Pair();
    }

    /// <summary>The canvas was disposed or its interface is going away: its element is emptied.</summary>
    internal void RemoveCanvas(PluginCanvasRegistration registration)
    {
        Entry? entry = _entries.Find(e => ReferenceEquals(e.Registration, registration));
        if (entry is null) return;
        Unhost(entry);
        _entries.Remove(entry);
    }

    /// <summary>A window of plugin <paramref name="ownerId"/> mounted with these <c>&lt;canvas&gt;</c> elements.</summary>
    internal void AddWindow(string ownerId, IReadOnlyList<UiMarkupCanvas> elements)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentNullException.ThrowIfNull(elements);
        foreach (UiMarkupCanvas element in elements)
            _slots.Add(new Slot(ownerId, element));
        Pair();
        ReportIdle();
    }

    /// <summary>A window went: its canvases move to another of the plugin's windows, or wait.</summary>
    internal void RemoveWindow(IReadOnlyList<UiMarkupCanvas> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        foreach (UiMarkupCanvas element in elements)
        {
            Slot? slot = _slots.Find(s => ReferenceEquals(s.Element, element));
            if (slot is null) continue;
            foreach (Entry entry in _entries)
            {
                if (ReferenceEquals(entry.Slot, slot))
                    Unhost(entry);
            }
            _slots.Remove(slot);
        }
        Pair();
    }

    /// <summary>A press went down at interface (<paramref name="x"/>, <paramref name="y"/>): hosted canvases that asked hear of it.</summary>
    internal void ReportPress(UiMouseButton button, int x, int y)
    {
        foreach (Entry entry in _entries.ToArray())
        {
            if (entry.Element is { } element && entry.Registration.WantsOutsidePresses)
                element.ReportPress(button, x, y);
        }
    }

    private void Pair()
    {
        foreach (Entry entry in _entries)
        {
            if (entry.Slot is not null) continue;
            Slot? slot = _slots.Find(s => s.Element.Hosted is null && Matches(s, entry.Registration));
            if (slot is null)
            {
                if (!entry.ReportedWaiting)
                {
                    entry.ReportedWaiting = true;
                    _report(
                        $"Plugin canvas '{Name(entry.Registration)}' is registered InMarkup and is waiting for a "
                        + $"<canvas id=\"{entry.Registration.CanvasId}\"> in one of the plugin's windows; "
                        + "nothing is drawn until one is mounted.");
                }
                continue;
            }
            Host(entry, slot);
        }
    }

    private void Host(Entry entry, Slot slot)
    {
        PluginCanvasElement element = _createElement(entry.Registration);
        slot.Element.Host(element);
        entry.Slot = slot;
        entry.Element = element;
        entry.Registration.IsHosted = true;
        // A new element has nothing to show: paint it whether or not its size changed.
        entry.Registration.Invalidate();
    }

    private static void Unhost(Entry entry)
    {
        if (entry.Element is not { } element) return;
        entry.Registration.IsHosted = false;
        // Out of the tree first, so the root takes back a press or focus the
        // canvas holds, through the plugin's handlers; then the targets.
        entry.Slot!.Element.Unhost();
        element.ReleaseTargets();
        entry.Slot = null;
        entry.Element = null;
    }

    /// <summary>Says once, for each element still empty, why it shows nothing when the reason is known.</summary>
    private void ReportIdle()
    {
        foreach (Slot slot in _slots)
        {
            if (slot.Reported || slot.Element.Hosted is not null) continue;
            string where = $"<canvas id=\"{slot.Element.CanvasId}\"> of plugin '{slot.OwnerId}'";
            if (_entries.Exists(e => Matches(slot, e.Registration)))
            {
                slot.Reported = true;
                _report(
                    $"{where} shows nothing: another window of the plugin already shows that canvas. "
                    + "It takes over if that window goes away.");
            }
            else if (_findCanvas(slot.OwnerId, slot.Element.CanvasId) is { Descriptor.InMarkup: false })
            {
                slot.Reported = true;
                _report(
                    $"{where} shows nothing: the plugin registered canvas '{slot.Element.CanvasId}' without InMarkup, "
                    + "so it sits on the screen by itself.");
            }
        }
    }

    private static bool Matches(Slot slot, PluginCanvasRegistration registration) =>
        slot.OwnerId.Equals(registration.Owner.Id, StringComparison.Ordinal)
        && slot.Element.CanvasId.Equals(registration.CanvasId, StringComparison.Ordinal);

    private static string Name(PluginCanvasRegistration registration) =>
        $"{registration.Owner.Id}/{registration.CanvasId}";
}
