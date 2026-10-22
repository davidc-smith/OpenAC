using AcDream.App.UI.Layout.Flex;

namespace AcDream.App.UI;

/// <summary>
/// The flex side of a markup container (a <c>&lt;group layout&gt;</c>, or a
/// root panel's content area with <c>layout</c>): its <see cref="FlexNode"/>
/// and one <see cref="UiFlexItem"/> per child.
///
/// <para>One tree per flex root. A container whose parent is not a flex
/// container is a <see cref="IsRoot">root</see>; it owns the layout of every
/// flex container nested inside it, whose nodes are inner nodes of its tree
/// (height-for-width only works across inner nodes). A root lays its tree
/// out when it is drawn, and only when something changed: its size, a child's
/// visibility, or the inputs a child was measured from (caption, font,
/// theme). Nothing calls into it to invalidate; it compares at draw.</para>
/// </summary>
internal sealed class UiFlexBox
{
    private float _laidWidth = float.NaN, _laidHeight = float.NaN;
    private bool _laidOut;

    public FlexNode Node { get; } = new();

    public List<UiFlexItem> Items { get; } = new();

    /// <summary>Whether this container lays out its tree; false for a container nested in another.</summary>
    public bool IsRoot { get; set; } = true;

    /// <summary>
    /// Set for a window's content area: the smallest size its content fits
    /// in is recomputed whenever the content changes, and reported through
    /// <see cref="MinimumChanged"/>.
    /// </summary>
    public bool TracksMinimum { get; set; }

    /// <summary>The smallest size the content fits in, after the last layout that measured it.</summary>
    public FlexSize Minimum { get; private set; }

    /// <summary>Raised from <see cref="EnsureLayout"/> when <see cref="Minimum"/> changes.</summary>
    public event Action<FlexSize>? MinimumChanged;

    /// <summary>Leaf measurements made, for the cost tests.</summary>
    internal int MeasureCount { get; set; }

    /// <summary>Adds a control or an absolute group as a measured leaf.</summary>
    public UiFlexItem AddLeaf(UiElement element, FlexNode node, FlexSize? authoredSize = null)
    {
        var item = new UiFlexItem(this, element, node, inner: null, authoredSize);
        node.Measure = item.Measure;
        Add(item);
        return item;
    }

    /// <summary>Adds a nested flex container as an inner node of this tree.</summary>
    public UiFlexItem AddContainer(UiElement element, UiFlexBox inner)
    {
        inner.IsRoot = false;
        var item = new UiFlexItem(this, element, inner.Node, inner, authoredSize: null);
        Add(item);
        return item;
    }

    private void Add(UiFlexItem item)
    {
        Node.Children.Add(item.Node);
        Items.Add(item);
    }

    /// <summary>
    /// Measures the tree now, outside a draw: the window's first measurement
    /// at build time. Returns the preferred size and the fitted minimum.
    /// </summary>
    public FlexMeasurement MeasureNow()
    {
        SyncHidden(this);
        FlexSize minimum = FlexFit.Minimum(Node);
        Minimum = minimum;
        FlexSize preferred = FlexLayout.Measure(Node).Preferred;
        // A size-dependent root can prefer less than it needs on its dependent axis.
        return new FlexMeasurement(
            new FlexSize(MathF.Max(preferred.Width, minimum.Width), MathF.Max(preferred.Height, minimum.Height)),
            minimum);
    }

    /// <summary>
    /// Lays the tree out in <paramref name="width"/> x <paramref name="height"/>
    /// and places every element in it, when anything changed since the last
    /// layout. Returns whether it laid out. Only a root lays out.
    /// </summary>
    public bool EnsureLayout(float width, float height)
    {
        if (!IsRoot) return false;
        bool contentChanged = !_laidOut || Changed(this);
        if (!contentChanged && width == _laidWidth && height == _laidHeight) return false;

        SyncHidden(this);
        if (contentChanged && TracksMinimum)
        {
            FlexSize minimum = FlexFit.Minimum(Node);
            if (minimum != Minimum)
            {
                Minimum = minimum;
                MinimumChanged?.Invoke(minimum);
            }
        }
        FlexLayout.Arrange(Node, new FlexRect(0f, 0f, width, height));
        Place(this);
        _laidOut = true;
        _laidWidth = width;
        _laidHeight = height;
        return true;
    }

    /// <summary>Whether any shown item's visibility or measured inputs changed, at any depth.</summary>
    private static bool Changed(UiFlexBox box)
    {
        foreach (UiFlexItem item in box.Items)
        {
            bool hidden = !item.Element.Visible;
            if (hidden != item.Node.Hidden) return true;
            if (hidden) continue;
            if (item.Inner is { } inner ? Changed(inner) : item.InputsChanged()) return true;
        }
        return false;
    }

    private static void SyncHidden(UiFlexBox box)
    {
        foreach (UiFlexItem item in box.Items)
        {
            item.Node.Hidden = !item.Element.Visible;
            if (item.Inner is { } inner) SyncHidden(inner);
        }
    }

    private static void Place(UiFlexBox box)
    {
        foreach (UiFlexItem item in box.Items)
        {
            if (item.Node.Hidden) continue;
            FlexRect r = item.Node.Rect;
            UiElement e = item.Element;
            e.Left = r.X;
            e.Top = r.Y;
            e.Width = r.Width;
            e.Height = r.Height;
            if (item.Inner is { } inner) Place(inner);
        }
    }
}

/// <summary>
/// One child of a flex container: the element, its node, and the inputs its
/// size was last measured from, so a change of caption, font or theme is
/// noticed at the next draw.
/// </summary>
internal sealed class UiFlexItem
{
    private readonly UiFlexBox _owner;
    private readonly FlexSize? _authoredSize;
    private string _text = string.Empty;
    private UiDatFont? _font;
    private bool _themed;

    internal UiFlexItem(UiFlexBox owner, UiElement element, FlexNode node, UiFlexBox? inner, FlexSize? authoredSize)
    {
        _owner = owner;
        Element = element;
        Node = node;
        Inner = inner;
        _authoredSize = authoredSize;
    }

    public UiElement Element { get; }

    public FlexNode Node { get; }

    /// <summary>The nested container's box when this item is a flex group; null for a leaf.</summary>
    public UiFlexBox? Inner { get; }

    /// <summary>The leaf's measure callback: its content size, recording what it was measured from.</summary>
    internal FlexMeasurement Measure(float? availableWidth)
    {
        _owner.MeasureCount++;
        _text = MarkupContentSize.Text(Element);
        _font = MarkupContentSize.Font(Element);
        _themed = MarkupContentSize.Themed(Element);
        if (_authoredSize is { } authored)
            return new FlexMeasurement(authored, authored);
        return MarkupContentSize.Measure(Element, _text) ?? default;
    }

    /// <summary>Whether the caption, font or theme the leaf was measured from has changed.</summary>
    internal bool InputsChanged() =>
        !ReferenceEquals(_font, MarkupContentSize.Font(Element))
        || _themed != MarkupContentSize.Themed(Element)
        || !string.Equals(_text, MarkupContentSize.Text(Element), StringComparison.Ordinal);
}
