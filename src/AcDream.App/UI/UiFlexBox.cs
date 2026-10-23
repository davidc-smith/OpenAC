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
/// visibility, or a leaf's measured size. Each leaf keeps its last
/// measurement and measures again only when what it was measured from
/// (caption, font, theme) changed, so a bound caption that changes every
/// frame costs one measure, and no layout unless its size changed. Nothing
/// calls into it to invalidate; it compares at draw.</para>
/// </summary>
internal sealed class UiFlexBox
{
    private float _laidWidth = float.NaN, _laidHeight = float.NaN;
    private bool _laidOut;
    private bool _structureChanged;

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
    public UiFlexItem AddLeaf(UiElement element, FlexNode node, FlexMeasurement? authoredSize = null)
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
        // An item added after the first layout (a repeat's new copy) lays out again.
        _structureChanged = true;
    }

    /// <summary>Removes the item for <paramref name="element"/>; false when it has none.</summary>
    public bool Remove(UiElement element)
    {
        int index = IndexOf(element);
        if (index < 0) return false;
        Node.Children.RemoveAt(index);
        Items.RemoveAt(index);
        _structureChanged = true;
        return true;
    }

    /// <summary>Moves the item for <paramref name="element"/> to <paramref name="index"/>.</summary>
    public void Move(UiElement element, int index)
    {
        int old = IndexOf(element);
        if (old < 0) throw new ArgumentException("not an item of this container", nameof(element));
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Items.Count);
        if (old == index) return;
        UiFlexItem item = Items[old];
        Node.Children.RemoveAt(old);
        Items.RemoveAt(old);
        Node.Children.Insert(index, item.Node);
        Items.Insert(index, item);
        _structureChanged = true;
    }

    private int IndexOf(UiElement element)
    {
        for (int i = 0; i < Items.Count; i++)
            if (ReferenceEquals(Items[i].Element, element)) return i;
        return -1;
    }

    /// <summary>
    /// Measures the tree now, outside a draw: the window's first measurement
    /// at build time. Returns the preferred size and the fitted minimum.
    /// </summary>
    public FlexMeasurement MeasureNow()
    {
        Changed(this);
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
        // Changed runs even before the first layout: it brings the leaves
        // measured at build up to date.
        bool contentChanged = Changed(this) | !_laidOut;
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
        ClearStructure(this);
        _laidOut = true;
        _laidWidth = width;
        _laidHeight = height;
        return true;
    }

    /// <summary>
    /// Whether any item's visibility or any shown leaf's size changed, at any
    /// depth. Every shown leaf is brought up to date on the way, so the
    /// layout that follows measures nothing it already knows.
    /// </summary>
    private static bool Changed(UiFlexBox box)
    {
        // An added or removed item changes the layout; the leaves are still refreshed.
        bool changed = box._structureChanged;
        foreach (UiFlexItem item in box.Items)
        {
            bool hidden = !item.Element.Visible;
            if (hidden != item.Node.Hidden) changed = true;
            if (hidden) continue;
            changed |= item.Inner is { } inner ? Changed(inner) : item.Refresh();
        }
        return changed;
    }

    private static void ClearStructure(UiFlexBox box)
    {
        box._structureChanged = false;
        foreach (UiFlexItem item in box.Items)
            if (item.Inner is { } inner) ClearStructure(inner);
    }

    private static void SyncHidden(UiFlexBox box)
    {
        foreach (UiFlexItem item in box.Items)
        {
            item.Node.Hidden = !item.Element.Visible;
            item.SyncCollapse();
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
/// One child of a flex container: the element, its node, and for a leaf its
/// last measurement and the inputs it was made from, so a change of caption,
/// font or theme is noticed at the next draw and nothing else is measured twice.
/// </summary>
internal sealed class UiFlexItem
{
    private readonly UiFlexBox _owner;
    private readonly FlexMeasurement? _authoredSize;
    private FlexMeasurement _measured;
    private bool _hasMeasured;
    private string _text = string.Empty;
    private string[] _options = [];
    private UiDatFont? _font;
    private bool _themed;
    private bool _collapsed;

    internal UiFlexItem(UiFlexBox owner, UiElement element, FlexNode node, UiFlexBox? inner, FlexMeasurement? authoredSize)
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

    /// <summary>
    /// The leaf's measure callback: its content size. A leaf is measured the
    /// first time it is asked; after that this returns the last measurement,
    /// which <see cref="Refresh"/> keeps current.
    /// </summary>
    internal FlexMeasurement Measure(float? availableWidth)
    {
        if (_authoredSize is { } authored)
            return authored;
        if (!_hasMeasured) MeasureContent();
        return _measured;
    }

    /// <summary>
    /// Measures the leaf again if what it was measured from changed. Returns
    /// whether that changes the size it lays out at; a fixed-size leaf never does.
    /// </summary>
    internal bool Refresh()
    {
        if (!_hasMeasured) return false;
        if (_collapsed != MarkupContentSize.Collapsed(Element))
        {
            // An expander opening or closing changes its limits (SyncCollapse), not just its content.
            MeasureContent();
            return true;
        }
        if (IsFixed || !InputsChanged()) return false;
        FlexMeasurement before = FlexLayout.ResolveLeaf(Node, _measured);
        MeasureContent();
        return FlexLayout.ResolveLeaf(Node, _measured) != before;
    }

    /// <summary>
    /// Whether the leaf's size never depends on its content: an absolute group
    /// of its authored size, or a control whose markup states its width, height
    /// and both minimums.
    /// </summary>
    private bool IsFixed =>
        _authoredSize is not null
        || (Node.Width is not null && Node.Height is not null && Node.MinWidth is not null && Node.MinHeight is not null);

    private void MeasureContent()
    {
        _owner.MeasureCount++;
        _text = MarkupContentSize.Text(Element);
        _font = MarkupContentSize.Font(Element);
        _themed = MarkupContentSize.Themed(Element);
        _collapsed = MarkupContentSize.Collapsed(Element);
        if (Element is UiMarkupRadio radio) _options = [.. radio.Items];
        _measured = MarkupContentSize.Measure(Element, _text) ?? default;
        _hasMeasured = true;
    }

    /// <summary>An expander's authored <c>maxh</c>, applied only while it is expanded.</summary>
    internal float? ExpandedMaxHeight { get; set; }

    /// <summary>An expander's authored <c>minh</c>, applied only while it is expanded.</summary>
    internal float? ExpandedMinHeight { get; set; }

    /// <summary>
    /// Caps a collapsed expander at its header, so neither grow along a
    /// column nor stretch across a row opens it up.
    /// </summary>
    internal void SyncCollapse()
    {
        if (Element is UiMarkupExpander { FlexPlaced: true } expander)
        {
            bool open = expander.Expanded;
            Node.MaxHeight = open ? ExpandedMaxHeight : UiMarkupExpander.HeaderHeight;
            Node.MinHeight = open ? ExpandedMinHeight : null;
        }
    }

    /// <summary>Whether the caption, options, font or theme the leaf was measured from has changed.</summary>
    private bool InputsChanged() =>
        !ReferenceEquals(_font, MarkupContentSize.Font(Element))
        || _themed != MarkupContentSize.Themed(Element)
        || (Element is UiMarkupRadio radio && !SameOptions(radio.Items))
        || !string.Equals(_text, MarkupContentSize.Text(Element), StringComparison.Ordinal);

    private bool SameOptions(IReadOnlyList<string> items)
    {
        if (items.Count != _options.Length) return false;
        for (int i = 0; i < _options.Length; i++)
            if (!string.Equals(items[i], _options[i], StringComparison.Ordinal)) return false;
        return true;
    }
}
