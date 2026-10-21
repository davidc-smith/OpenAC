using System.Runtime.CompilerServices;
using AcDream.App.UI.Markup;

namespace AcDream.App.UI;

/// <summary>
/// A markup expander: a header that folds away the content below it. When it
/// collapses, the siblings at or below its bottom move up by the height it
/// lost (those stretched between top and bottom grow instead), and come back
/// when it expands. Each moved element re-records its
/// anchor baseline, so the per-frame anchor pass keeps the new layout.
/// </summary>
public sealed class UiMarkupExpander : UiElement
{
    public const float HeaderHeight = 18f;

    public UiMarkupExpanderHeader Header { get; }

    /// <summary>Holds the authored children; y = 0 is the first pixel under the header.</summary>
    public UiPanel Content { get; }

    /// <summary>Bound state: when set, the plugin owns it and the layout follows it.</summary>
    public Func<bool>? ExpandedSource { get; set; }

    /// <summary>Called with the state a click or key press asks for.</summary>
    public Action<bool>? Changed { get; set; }

    /// <summary>The markup's <c>open</c>: where unbound state starts, and what isn't remembered.</summary>
    public bool OpenByDefault { get; set; } = true;

    internal RememberedValue? Remembered { get; set; }

    public bool IsRemembered => Remembered?.IsActive == true;

    private bool? _hostExpanded;
    private bool _laidOutExpanded = true;

    /// <summary>The state the layout is currently in (what the header arrow shows).</summary>
    internal bool LaidOutExpanded => _laidOutExpanded;
    // The height to restore on expand (an expander above can change it while collapsed).
    private float _collapseDelta;
    private AnchorEdges? _expandedAnchors;
    private float _parentHeightAtCollapse;
    // What collapsing moved, and by how much: that is what expanding gives back,
    // even if _collapseDelta has changed since.
    private readonly List<(UiElement Element, bool Grew, float Delta)> _moved = new();

    public UiMarkupExpander()
    {
        ClickThrough = true;
        Header = new UiMarkupExpanderHeader(this)
        {
            AcceptsFocus = true,
            FocusOnMouseClick = false,
            TabStop = true,
            Height = HeaderHeight,
            Anchors = AnchorEdges.Left | AnchorEdges.Right | AnchorEdges.Top,
        };
        Content = new UiPanel
        {
            Top = HeaderHeight,
            ClickThrough = true,
            BackgroundColor = System.Numerics.Vector4.Zero,
            BorderColor = System.Numerics.Vector4.Zero,
            Anchors = AnchorEdges.Left | AnchorEdges.Right | AnchorEdges.Top | AnchorEdges.Bottom,
        };
        AddChild(Header);
        AddChild(Content);
    }

    /// <summary>Sizes the header and content to the expander's authored box.</summary>
    internal void LayoutParts()
    {
        Header.Left = 0f;
        Header.Top = 0f;
        Header.Width = Width;
        Content.Left = 0f;
        Content.Top = HeaderHeight;
        Content.Width = Width;
        Content.Height = MathF.Max(0f, Height - HeaderHeight);
    }

    public bool Expanded => ExpandedSource?.Invoke() ?? HostExpanded;

    private bool HostExpanded
    {
        get
        {
            if (Remembered is { } remembered && remembered.TryRefresh(out string? text))
                _hostExpanded = Parse(text) ?? OpenByDefault;
            return _hostExpanded ??= OpenByDefault;
        }
    }

    private static bool? Parse(string? text) => text switch
    {
        "true" => true,
        "false" => false,
        _ => null,
    };

    /// <summary>What a header click or Enter/Space does.</summary>
    internal void Toggle()
    {
        if (!Enabled)
            return;
        bool next = !Expanded;
        if (ExpandedSource is not null)
        {
            Changed?.Invoke(next);
            return;
        }
        _hostExpanded = next;
        Remembered?.Remember(next == OpenByDefault ? null : next ? "true" : "false");
        ApplyLayout(next);
        Changed?.Invoke(next);
    }

    protected override void OnTick(double deltaSeconds) => ApplyLayout(Expanded);

    // How far expanders above have moved an element up. Siblings are chosen by
    // their authored position (current top plus this shift), so two columns of
    // expanders never drag each other's neighbours along.
    private static readonly ConditionalWeakTable<UiElement, StrongBox<float>> Shifts = new();

    private static float ShiftOf(UiElement e) => Shifts.TryGetValue(e, out var box) ? box.Value : 0f;

    private static void AddShift(UiElement e, float delta) => Shifts.GetOrCreateValue(e).Value += delta;

    private static bool StretchesWhenExpanded(UiElement e)
    {
        AnchorEdges anchors = e is UiMarkupExpander { _expandedAnchors: { } expanded } ? expanded : e.Anchors;
        return (anchors & (AnchorEdges.Top | AnchorEdges.Bottom)) == (AnchorEdges.Top | AnchorEdges.Bottom);
    }

    // A collapsed stretched expander is pinned at its header, so the height it
    // gains or loses goes to what it restores on expand instead.
    private static void Grow(UiElement e, float delta)
    {
        if (e is UiMarkupExpander { _expandedAnchors: not null } collapsed)
            collapsed._collapseDelta = MathF.Max(0f, collapsed._collapseDelta + delta);
        else
            e.Height = MathF.Max(0f, e.Height + delta);
    }

    private void ApplyLayout(bool expanded)
    {
        if (expanded == _laidOutExpanded)
            return;
        _laidOutExpanded = expanded;
        UiElement? parent = Parent;

        // Bring every rect up to the current parent size before changing it, so
        // re-recording a baseline never absorbs a resize that has not been drawn yet.
        if (parent is not null)
        {
            foreach (UiElement child in parent.Children)
                child.ApplyAnchor(parent.Width, parent.Height);
        }

        if (!expanded)
        {
            _collapseDelta = Height - HeaderHeight;
            float authoredBottom = Top + ShiftOf(this) + Height;
            _moved.Clear();
            if (parent is not null)
            {
                foreach (UiElement sibling in parent.Children)
                {
                    if (ReferenceEquals(sibling, this)
                        || sibling.Top + ShiftOf(sibling) < authoredBottom - 0.5f)
                        continue;
                    // A sibling stretched between top and bottom grows into the space
                    // (its bottom margin stays as authored); everything else moves up.
                    bool grows = StretchesWhenExpanded(sibling);
                    sibling.Top -= _collapseDelta;
                    if (grows)
                        Grow(sibling, _collapseDelta);
                    AddShift(sibling, _collapseDelta);
                    sibling.CaptureCurrentAnchorBaseline();
                    _moved.Add((sibling, grows, _collapseDelta));
                }
            }
            Content.Visible = false;
            if (parent is not null && (Anchors & (AnchorEdges.Top | AnchorEdges.Bottom)) == (AnchorEdges.Top | AnchorEdges.Bottom))
            {
                // A bottom-anchored header would stretch (or clamp to 0) with the parent;
                // pin it at 18 while collapsed and remember how to restore.
                _expandedAnchors = Anchors;
                _parentHeightAtCollapse = parent.Height;
                Anchors &= ~AnchorEdges.Bottom;
            }
            Height = HeaderHeight;
            CaptureCurrentAnchorBaseline();
        }
        else
        {
            if (_expandedAnchors is { } anchors && parent is not null)
            {
                Height = HeaderHeight + _collapseDelta + (parent.Height - _parentHeightAtCollapse);
                Anchors = anchors;
                _expandedAnchors = null;
            }
            else
            {
                Height += _collapseDelta;
            }
            CaptureCurrentAnchorBaseline();
            Content.Visible = true;
            foreach (var (sibling, grew, delta) in _moved)
            {
                if (!ReferenceEquals(sibling.Parent, parent))
                    continue;
                sibling.Top += delta;
                if (grew)
                    Grow(sibling, -delta);
                AddShift(sibling, -delta);
                sibling.CaptureCurrentAnchorBaseline();
            }
            _moved.Clear();
        }
    }
}
