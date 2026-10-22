using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using AcDream.App.UI.Markup;

namespace AcDream.App.UI;

/// <summary>
/// Where a <c>&lt;repeat&gt;</c> sits in its flex container: a zero-size,
/// click-through element with no flex item. Its copies, one per list item,
/// are its siblings, placed in list order straight after it.
/// </summary>
internal sealed class UiMarkupRepeat : UiElement
{
    /// <summary>
    /// One copy of the template: its item's key (null without <c>key</c>),
    /// its place in the list, the source its bindings read, the top-level
    /// elements it built into <see cref="Container"/>, the theme scope that
    /// holds its theme actions, and its per-key remembered state.
    /// </summary>
    internal sealed class Copy(
        object? key, int index, MarkupBindingSource source, IReadOnlyList<UiElement> elements,
        UiPluginMarkupPanel.ThemeActionScope? scope, KeyedControlState? state)
    {
        public object? Key { get; set; } = key;
        public int Index { get; set; } = index;
        public MarkupBindingSource Source { get; } = source;
        public IReadOnlyList<UiElement> Elements { get; } = elements;
        public UiPluginMarkupPanel.ThemeActionScope? Scope { get; } = scope;
        public KeyedControlState? State { get; } = state;
    }

    private readonly XElement[] _template;
    private readonly MarkupRepeat.BuildArgs _args;
    private readonly MarkupRepeat.AddElement _add;

    internal UiMarkupRepeat(
        XElement[] template, string itemsExpression, MarkupBindingSource parentSource, PropertyInfo items,
        Type itemType, PropertyInfo? key, UiElement container, UiFlexBox flex,
        MarkupRepeat.BuildArgs args, MarkupRepeat.AddElement add)
    {
        _template = template;
        ItemsExpression = itemsExpression;
        ParentSource = parentSource;
        Items = items;
        ItemType = itemType;
        KeyProperty = key;
        Container = container;
        Flex = flex;
        _args = args;
        _add = add;
        ClickThrough = true;
        Anchors = AnchorEdges.None;
    }

    /// <summary>The <c>items</c> attribute as written, for messages.</summary>
    internal string ItemsExpression { get; }

    /// <summary>The source <c>items</c> is read through: the window's, or the enclosing copy's.</summary>
    internal MarkupBindingSource ParentSource { get; }

    internal PropertyInfo Items { get; }

    /// <summary>The list's declared element type, which every copy's names resolve on.</summary>
    internal Type ItemType { get; }

    internal PropertyInfo? KeyProperty { get; }

    /// <summary>The flex container's element the marker and its copies are children of.</summary>
    internal UiElement Container { get; }

    internal UiFlexBox Flex { get; }

    /// <summary>The copies in list order.</summary>
    internal List<Copy> Copies { get; } = new();

    /// <summary>The list as it reads now; null when the property is null.</summary>
    internal IEnumerable? ReadList() => ParentSource.Read(Items) as IEnumerable;

    internal object? KeyOf(object? item) =>
        KeyProperty is not null && item is not null ? KeyProperty.GetValue(item) : null;

    /// <summary>
    /// The build-time copies: one per item, or for an empty or null list a
    /// throwaway check build, so a misspelt name fails now (R3).
    /// </summary>
    internal void BuildInitialCopies()
    {
        int index = 0;
        if (ReadList() is { } list)
        {
            foreach (object? item in list)
                BuildCopy(item, index++);
        }
        if (index == 0)
            CheckBuild();
    }

    /// <summary>
    /// Builds <paramref name="item"/>'s copy as <see cref="Copies"/>[<paramref name="index"/>],
    /// placed in the container after the copy before it, or after the marker.
    /// </summary>
    internal Copy BuildCopy(object? item, int index)
    {
        object? key = KeyOf(item);
        KeyedControlState? state = KeyProperty is not null && _args.ControlState is { } outer
            ? new KeyedControlState(outer, Convert.ToString(key, CultureInfo.InvariantCulture) ?? string.Empty)
            : null;
        var source = new MarkupBindingSource(ItemType) { Current = item };
        int firstChild = Container.Children.Count, firstItem = Flex.Items.Count;
        var scope = BuildTemplate(Container, source, state ?? _args.ControlState, Flex, out var pending);
        scope?.Close();

        var elements = new UiElement[Container.Children.Count - firstChild];
        for (int i = 0; i < elements.Length; i++)
        {
            elements[i] = Container.Children[firstChild + i];
            elements[i].CaptureAuthoredAnchorBaselines();
        }
        int newItems = Flex.Items.Count - firstItem;

        UiElement after = EndBefore(index);
        int childAt = IndexOfChild(after) + 1;
        int itemAt = FlexIndexAfter(childAt - 1);
        foreach (UiElement element in elements)
            Container.MoveChild(element, childAt++);
        for (int i = 0; i < newItems; i++)
            Flex.Move(Flex.Items[firstItem + i].Element, itemAt++);

        var copy = new Copy(key, index, source, elements, scope, state);
        Copies.Insert(index, copy);
        for (int i = index + 1; i < Copies.Count; i++)
            Copies[i].Index = i;
        BuildPending(pending);
        return copy;
    }

    /// <summary>
    /// Builds the template against a null item into a detached scratch
    /// container, so its names are checked against the item type, then drops
    /// it and its theme actions.
    /// </summary>
    private void CheckBuild()
    {
        var source = new MarkupBindingSource(ItemType);
        BuildTemplate(new UiPanel(), source, controlState: null, new UiFlexBox(), out var pending)?.Dispose();
        // Nested repeats open their own scopes, so only once this one is gone.
        BuildPending(pending);
    }

    /// <summary>
    /// Builds every template child, with a theme scope open around it when
    /// the window is themed. Repeats met inside wait in <paramref name="pending"/>
    /// until the caller has closed the scope.
    /// </summary>
    private UiPluginMarkupPanel.ThemeActionScope? BuildTemplate(
        UiElement parent, MarkupBindingSource source, IMarkupControlState? controlState, UiFlexBox flex,
        out List<UiMarkupRepeat> pending)
    {
        pending = new List<UiMarkupRepeat>();
        source.PendingRepeats = pending;
        var scope = _args.ThemedPanel?.BeginThemeScope();
        try
        {
            foreach (XElement child in _template)
                _add(parent, child, source, controlState, flex);
        }
        catch
        {
            scope?.Dispose();
            throw;
        }
        finally
        {
            source.PendingRepeats = null;
        }
        return scope;
    }

    private static void BuildPending(List<UiMarkupRepeat> pending)
    {
        foreach (UiMarkupRepeat repeat in pending)
            repeat.BuildInitialCopies();
    }

    /// <summary>
    /// The last element in the container belonging to this repeat's copies
    /// before <paramref name="index"/>, counting the copies of a repeat a copy
    /// ends with; the marker when they built nothing.
    /// </summary>
    private UiElement EndBefore(int index)
    {
        for (int i = index - 1; i >= 0; i--)
        {
            IReadOnlyList<UiElement> elements = Copies[i].Elements;
            if (elements.Count > 0)
                return elements[^1] is UiMarkupRepeat nested ? nested.EndBefore(nested.Copies.Count) : elements[^1];
        }
        return this;
    }

    /// <summary>
    /// Every element a copy has in the container, in order: its own, and the
    /// copies of any repeat directly inside it after that repeat's marker.
    /// </summary>
    internal IEnumerable<UiElement> Span(Copy copy)
    {
        foreach (UiElement element in copy.Elements)
        {
            yield return element;
            if (element is UiMarkupRepeat nested)
                foreach (Copy inner in nested.Copies)
                    foreach (UiElement spanned in nested.Span(inner))
                        yield return spanned;
        }
    }

    private int IndexOfChild(UiElement element)
    {
        for (int i = 0; i < Container.Children.Count; i++)
            if (ReferenceEquals(Container.Children[i], element)) return i;
        throw new InvalidOperationException("not a child of the repeat's container");
    }

    /// <summary>The flex index just after the item of the last element at or before <paramref name="childIndex"/> that has one.</summary>
    private int FlexIndexAfter(int childIndex)
    {
        for (int i = childIndex; i >= 0; i--)
        {
            UiElement child = Container.Children[i];
            int item = Flex.Items.FindIndex(it => ReferenceEquals(it.Element, child));
            if (item >= 0) return item + 1;
        }
        return 0;
    }
}
