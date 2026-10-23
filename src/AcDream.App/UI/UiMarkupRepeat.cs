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
        public object? Key { get; } = key;
        public int Index { get; set; } = index;
        public MarkupBindingSource Source { get; } = source;
        public IReadOnlyList<UiElement> Elements { get; } = elements;
        public UiPluginMarkupPanel.ThemeActionScope? Scope { get; } = scope;
        public KeyedControlState? State { get; } = state;
    }

    private readonly XElement[] _template;
    private readonly MarkupRepeat.BuildArgs _args;
    private readonly MarkupRepeat.AddElement _add;
    private readonly ListReader _list;
    // Reused by the matching, so it allocates only when something changed.
    private readonly Dictionary<object, Copy> _byKey = new();
    private readonly HashSet<object> _seenKeys = new();
    private readonly HashSet<Copy> _kept = new();
    private readonly List<Copy?> _order = new();
    // The key paths of copies whose item left the list this tick, forgotten after matching.
    private readonly HashSet<string> _forget = new();
    private IMarkupControlState? _forgetIn;
    private bool _loggedDuplicates;
    private bool _released;

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
        _list = ListReader.For(itemType, key);
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

    internal object? KeyOf(object? item) =>
        KeyProperty is not null && item is not null ? KeyProperty.GetValue(item) : null;

    /// <summary>
    /// The build-time copies: one per item, or for an empty or null list a
    /// throwaway check build, so a misspelt name fails now (R3).
    /// </summary>
    internal void BuildInitialCopies()
    {
        _list.Read(ParentSource.Read(Items));
        for (int i = 0; i < _list.Count; i++)
            BuildCopy(_list.Item(i), i);
        if (_list.Count == 0)
            CheckBuild();
    }

    /// <summary>
    /// Reads the list and keys and, when they changed (R4), keeps, moves,
    /// adds and removes copies to match (R5). Copies built here tick from the
    /// next tick on (the parent ticks a snapshot of its children), and their
    /// bindings show their values from then.
    /// </summary>
    protected override void OnTick(double deltaSeconds)
    {
        // A removed marker still ticks once this tick, from its parent's
        // snapshot; it must not build into a tree that has left the window.
        if (_released || Parent is null)
            return;
        if (!_list.Read(ParentSource.Read(Items)))
            return;
        try
        {
            if (KeyProperty is not null && KeysUnique())
            {
                _loggedDuplicates = false;
                MatchByKey();
            }
            else
            {
                if (KeyProperty is not null && !_loggedDuplicates)
                {
                    Console.WriteLine($"markup <repeat items=\"{ItemsExpression}\">: duplicate or missing keys; rows are matched by position");
                    _loggedDuplicates = true;
                }
                MatchByIndex();
            }
        }
        catch
        {
            // Try again next tick, as a binding that throws does.
            _list.Forget();
            throw;
        }
        finally
        {
            ForgetRemovedKeys();
        }
    }

    /// <summary>
    /// Drops the remembered state of the copies whose item left the list, so
    /// short-lived keys (item or monster ids) don't pile up in the settings
    /// file. A copy that only leaves with its window keeps its state.
    /// </summary>
    private void ForgetRemovedKeys()
    {
        if (_forget.Count == 0)
            return;
        try
        {
            (_forgetIn as IForgetsKeyedState)?.ForgetKeyed(_forget);
        }
        finally
        {
            _forget.Clear();
            _forgetIn = null;
        }
    }

    /// <summary>Marks <paramref name="copy"/>'s remembered state to be forgotten after this tick's matching.</summary>
    private void ForgetLater(Copy copy)
    {
        if (copy.State is null)
            return;
        _forget.Add(copy.State.Path);
        _forgetIn = copy.State.Root;
    }

    /// <summary>Whether every key is set and different; a null key falls back to position matching like a duplicate.</summary>
    private bool KeysUnique()
    {
        _seenKeys.Clear();
        for (int i = 0; i < _list.Count; i++)
        {
            if (_list.Key(i) is not { } key || !_seenKeys.Add(key))
                return false;
        }
        return true;
    }

    private void MatchByKey()
    {
        _byKey.Clear();
        _kept.Clear();
        _order.Clear();
        foreach (Copy copy in Copies)
        {
            if (copy.Key is { } key)
                _byKey.TryAdd(key, copy);
        }
        for (int i = 0; i < _list.Count; i++)
        {
            if (_byKey.Remove(_list.Key(i)!, out Copy? copy))
            {
                copy.Source.Current = _list.Item(i);
                _kept.Add(copy);
            }
            _order.Add(copy);
        }
        foreach (Copy copy in Copies)
        {
            if (!_kept.Contains(copy))
            {
                ForgetLater(copy);
                Release(copy);
            }
        }

        Copies.Clear();
        int childAt = IndexOfChild(this) + 1;
        int itemAt = FlexIndexAfter(childAt - 1);
        int next = 0;
        try
        {
            for (; next < _order.Count; next++)
            {
                if (_order[next] is { } kept)
                {
                    kept.Index = next;
                    Copies.Add(kept);
                    Place(kept, ref childAt, ref itemAt);
                }
                else
                {
                    BuildCopy(_list.Item(next), next);
                    childAt = IndexOfChild(EndBefore(next + 1)) + 1;
                    itemAt = FlexIndexAfter(childAt - 1);
                }
            }
        }
        catch
        {
            // The kept copies not reached yet stay tracked; next tick puts them in order.
            for (int i = next + 1; i < _order.Count; i++)
            {
                if (_order[i] is { } kept)
                {
                    kept.Index = Copies.Count;
                    Copies.Add(kept);
                }
            }
            throw;
        }
        finally
        {
            _order.Clear();
            _kept.Clear();
            _byKey.Clear();
        }
    }

    private void MatchByIndex()
    {
        int count = _list.Count;
        for (int i = 0; i < Math.Min(count, Copies.Count); i++)
            Copies[i].Source.Current = _list.Item(i);
        for (int i = Copies.Count - 1; i >= count; i--)
        {
            // With duplicate keys a removed copy's key may still be in the list.
            if (Copies[i].State is not null && !ListHasKey(Copies[i].Key))
                ForgetLater(Copies[i]);
            Release(Copies[i]);
            Copies.RemoveAt(i);
        }
        for (int i = Copies.Count; i < count; i++)
            BuildCopy(_list.Item(i), i);
    }

    private bool ListHasKey(object? key)
    {
        for (int i = 0; i < _list.Count; i++)
            if (Equals(_list.Key(i), key)) return true;
        return false;
    }

    /// <summary>
    /// Moves a kept copy's elements (and its nested repeats' copies) to
    /// <paramref name="childAt"/> and its flex items to <paramref name="itemAt"/>,
    /// leaving whatever is already in place alone.
    /// </summary>
    private void Place(Copy copy, ref int childAt, ref int itemAt)
    {
        foreach (UiElement element in Span(copy))
        {
            if (!ReferenceEquals(Container.Children[childAt], element))
                Container.MoveChild(element, childAt);
            childAt++;
            if (element is UiMarkupRepeat)
                continue;   // markers have no flex item
            if (itemAt < Flex.Items.Count && ReferenceEquals(Flex.Items[itemAt].Element, element))
                itemAt++;
            else if (HasItem(element))
                Flex.Move(element, itemAt++);
        }
    }

    private bool HasItem(UiElement element)
    {
        foreach (UiFlexItem item in Flex.Items)
            if (ReferenceEquals(item.Element, element)) return true;
        return false;
    }

    /// <summary>
    /// Takes a copy out: its elements leave the container (the root drops
    /// focus, capture and popups inside them) and the flex box; nested
    /// repeats give back their copies, disposable elements are disposed, and
    /// its theme actions stop.
    /// </summary>
    private void Release(Copy copy)
    {
        foreach (UiElement element in copy.Elements)
        {
            Container.RemoveChild(element);
            Flex.Remove(element);
        }
        foreach (UiElement element in copy.Elements)
            ReleaseSubtree(element);
        copy.Scope?.Dispose();
    }

    /// <summary>Releases every copy, for a marker leaving with the copy it sits in; it does nothing after.</summary>
    private void ReleaseCopies()
    {
        _released = true;
        foreach (Copy copy in Copies)
            Release(copy);
        Copies.Clear();
    }

    private static void ReleaseSubtree(UiElement element)
    {
        if (element is UiMarkupRepeat repeat)
            repeat.ReleaseCopies();
        if (element is IDisposable disposable)
            disposable.Dispose();
        // A nested repeat takes its copies out of this list as it is walked.
        foreach (UiElement child in element.Children.ToArray())
        {
            if (ReferenceEquals(child.Parent, element))
                ReleaseSubtree(child);
        }
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
        UiPluginMarkupPanel.ThemeActionScope? scope;
        List<UiMarkupRepeat> pending;
        try
        {
            scope = BuildTemplate(Container, source, state ?? _args.ControlState, Flex, out pending);
        }
        catch
        {
            // A copy is built whole or not at all (BuildTemplate has dropped the scope).
            while (Container.Children.Count > firstChild)
            {
                UiElement built = Container.Children[^1];
                Container.RemoveChild(built);
                Flex.Remove(built);
                ReleaseSubtree(built);
            }
            throw;
        }
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
        try
        {
            BuildPending(pending);
        }
        catch
        {
            Copies.RemoveAt(index);
            for (int i = index; i < Copies.Count; i++)
                Copies[i].Index = i;
            Release(copy);
            throw;
        }
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

    /// <summary>
    /// The list and its keys as last read, in reused buffers, so a tick where
    /// nothing changed allocates nothing (for a list or array; another
    /// <c>IEnumerable&lt;T&gt;</c> costs its enumerator).
    /// </summary>
    private abstract class ListReader
    {
        public static ListReader For(Type itemType, PropertyInfo? key) =>
            (ListReader)Activator.CreateInstance(
                typeof(ListReader<,>).MakeGenericType(itemType, key?.PropertyType ?? typeof(object)), key)!;

        public abstract int Count { get; }

        public abstract object? Item(int index);

        /// <summary>The item's key; null without <c>key</c> or for a null item.</summary>
        public abstract object? Key(int index);

        /// <summary>Reads <paramref name="list"/>; false when its items and keys all match the last read.</summary>
        public abstract bool Read(object? list);

        /// <summary>Makes the next read count as a change.</summary>
        public abstract void Forget();
    }

    private sealed class ListReader<T, TKey> : ListReader
    {
        private static readonly bool ByReference = !typeof(T).IsValueType;
        // A struct's getter takes its item by reference, so it is bound as one.
        private delegate TKey StructGetter(ref T item);
        private readonly Func<T, TKey>? _key;
        private List<T> _items = new(), _lastItems = new();
        private List<TKey> _keys = new(), _lastKeys = new();
        private bool _forgot = true;

        public ListReader(PropertyInfo? key)
        {
            if (key is null)
                return;
            // Typed getters, so reading a key boxes neither the item nor the key.
            if (key.GetMethod is not { IsStatic: false } getter)
                _key = item => (TKey)key.GetValue(item)!;
            else if (ByReference)
                _key = getter.CreateDelegate<Func<T, TKey>>();
            else
            {
                StructGetter read = getter.CreateDelegate<StructGetter>();
                _key = item => read(ref item);
            }
        }

        public override int Count => _items.Count;

        public override object? Item(int index) => _items[index];

        public override object? Key(int index) =>
            _key is null || _items[index] is null ? null : _keys[index];

        public override void Forget() => _forgot = true;

        public override bool Read(object? list)
        {
            (_items, _lastItems) = (_lastItems, _items);
            (_keys, _lastKeys) = (_lastKeys, _keys);
            _items.Clear();
            _keys.Clear();
            switch (list)
            {
                case IReadOnlyList<T> readOnly:
                    for (int i = 0; i < readOnly.Count; i++) _items.Add(readOnly[i]);
                    break;
                case IList<T> writable:
                    for (int i = 0; i < writable.Count; i++) _items.Add(writable[i]);
                    break;
                case IEnumerable<T> sequence:
                    foreach (T item in sequence) _items.Add(item);
                    break;
            }
            if (_key is not null)
            {
                foreach (T item in _items)
                    _keys.Add(item is null ? default! : _key(item));
            }

            // The list instance does not count: a list built on every read with the same items is no change.
            bool changed = _forgot || _items.Count != _lastItems.Count;
            for (int i = 0; !changed && i < _items.Count; i++)
            {
                changed = ByReference
                    ? !ReferenceEquals(_items[i], _lastItems[i])
                    : !EqualityComparer<T>.Default.Equals(_items[i], _lastItems[i]);
            }
            for (int i = 0; !changed && i < _keys.Count; i++)
                changed = !EqualityComparer<TKey>.Default.Equals(_keys[i], _lastKeys[i]);
            _forgot = false;
            return changed;
        }
    }
}
