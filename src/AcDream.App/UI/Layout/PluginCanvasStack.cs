using System.Collections.Generic;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.UI.Layout;

/// <summary>
/// One of the two layers plugin canvases are drawn in (see
/// <see cref="PluginCanvasLayer"/>), holding one group per plugin. Groups
/// are stacked in the order their plugins first mounted a canvas here,
/// first lowest, so no plugin can lift its canvases over another's. Inside
/// a group the canvases are stacked by the plugin's
/// <see cref="IPluginCanvas.ZOrder"/>, then by registration.
/// </summary>
internal sealed class PluginCanvasStack
{
    private readonly UiOverlayLayer _layer;
    private readonly Dictionary<string, PluginCanvasGroup> _groups = new(StringComparer.Ordinal);

    /// <summary>Takes over <paramref name="layer"/>, which the overlay host keeps equal to the viewport, and shows it.</summary>
    internal PluginCanvasStack(UiOverlayLayer layer)
    {
        _layer = layer ?? throw new ArgumentNullException(nameof(layer));
        _layer.Visible = true;
    }

    internal UiOverlayLayer Layer => _layer;

    /// <summary>Adds <paramref name="element"/> to its plugin's group, making the group on the plugin's first canvas here.</summary>
    internal void Add(PluginCanvasElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        string ownerId = element.Registration.Owner.Id;
        if (!_groups.TryGetValue(ownerId, out PluginCanvasGroup? group))
        {
            // Groups are never removed, so the count is a unique, rising rank.
            group = new PluginCanvasGroup { Name = $"PluginCanvases:{ownerId}", ZOrder = _groups.Count };
            _groups.Add(ownerId, group);
            _layer.AddChild(group);
            group.FollowParent();
        }
        group.Add(element);
    }

    /// <summary>
    /// Tells every canvas here that asked for outside presses about a press
    /// at (<paramref name="x"/>, <paramref name="y"/>) in interface
    /// coordinates; each canvas decides whether it was outside. A handler
    /// may hide, add or remove canvases, so the canvases are listed first.
    /// </summary>
    internal void ReportPress(UiMouseButton button, int x, int y)
    {
        var canvases = new List<PluginCanvasElement>();
        foreach (PluginCanvasGroup group in _groups.Values)
            group.CollectOutsidePressListeners(canvases);
        foreach (PluginCanvasElement canvas in canvases)
            canvas.ReportPress(button, x, y);
    }

    /// <summary>Takes <paramref name="element"/> out of its group; does nothing if it is not in one.</summary>
    internal static void Remove(PluginCanvasElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.Parent?.RemoveChild(element);
    }
}

/// <summary>
/// One plugin's canvases in one layer: a click-through panel the size of
/// the layer, whose children are ranked every tick by
/// <c>(ZOrder, registration)</c>. Each canvas's interface z-order is its
/// rank, so no two are ever equal and the order never depends on how the
/// sort treats ties.
/// </summary>
internal sealed class PluginCanvasGroup : UiOverlayLayer
{
    private readonly List<PluginCanvasElement> _canvases = [];

    internal void Add(PluginCanvasElement element)
    {
        AddChild(element, takesInput: element.Registration.AcceptsPointerInput);
        _canvases.Add(element);
        Rank();
    }

    public override bool RemoveChild(UiElement child)
    {
        if (!base.RemoveChild(child)) return false;
        if (child is PluginCanvasElement canvas)
        {
            _canvases.Remove(canvas);
            Rank();
        }
        return true;
    }

    internal void CollectOutsidePressListeners(List<PluginCanvasElement> into)
    {
        foreach (PluginCanvasElement canvas in _canvases)
        {
            if (canvas.Registration.WantsOutsidePresses)
                into.Add(canvas);
        }
    }

    /// <summary>Before the canvases tick: follow the layer's size, then pick up any change of a plugin's order.</summary>
    protected override void OnTick(double deltaSeconds)
    {
        FollowParent();
        Rank();
    }

    internal void FollowParent()
    {
        Left = 0f;
        Top = 0f;
        Width = Parent?.Width ?? 0f;
        Height = Parent?.Height ?? 0f;
    }

    private void Rank()
    {
        _canvases.Sort(static (a, b) =>
        {
            int byOrder = a.Registration.ZOrder.CompareTo(b.Registration.ZOrder);
            return byOrder != 0 ? byOrder : a.Registration.Id.CompareTo(b.Registration.Id);
        });
        for (int rank = 0; rank < _canvases.Count; rank++)
            _canvases[rank].ZOrder = rank;
    }
}
