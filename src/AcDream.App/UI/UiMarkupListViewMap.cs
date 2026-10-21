namespace AcDream.App.UI;

/// <summary>
/// The order a markup list shows its rows in: view row (top to bottom) to
/// plugin row (the plugin's index) and back. Identity until sorted. A sort is
/// stable (ties keep the plugin's order) and puts rows without a key last,
/// ascending or descending. With a tree shape it shows the rows in pre-order,
/// sorts each set of siblings, and leaves out the descendants of collapsed rows.
/// </summary>
internal sealed class UiMarkupListViewMap
{
    private int[] _viewToPlugin = Array.Empty<int>();
    private int[] _pluginToView = Array.Empty<int>();
    private int[] _scratch = Array.Empty<int>();
    private bool _identity = true;
    private readonly KeyComparer _comparer = new();

    /// <summary>The rows shown, top to bottom.</summary>
    public int ViewCount { get; private set; }

    /// <summary>The plugin's rows, shown or not.</summary>
    public int PluginCount { get; private set; }

    /// <summary>The plugin row shown at a view row; −1 outside the rows.</summary>
    public int PluginRow(int view) =>
        (uint)view >= (uint)ViewCount ? -1 : _identity ? view : _viewToPlugin[view];

    /// <summary>Where a plugin row is shown; −1 outside the rows.</summary>
    public int ViewRow(int plugin) =>
        (uint)plugin >= (uint)PluginCount ? -1 : _identity ? plugin : _pluginToView[plugin];

    public void SetIdentity(int count, UiMarkupListTreeShape? tree = null)
    {
        if (tree is not null)
        {
            ArrangeTree(tree, order: null);
            return;
        }
        _identity = true;
        ViewCount = PluginCount = count;
    }

    /// <summary>Sorts by <paramref name="keys"/>[plugin row]; a null key sorts last. A tree sorts each set of siblings.</summary>
    public void SortByText(string?[] keys, int count, bool descending, UiMarkupListTreeShape? tree = null)
    {
        _comparer.Texts = keys;
        _comparer.Numbers = null;
        _comparer.Descending = descending;
        Apply(count, tree);
    }

    /// <summary>Sorts by <paramref name="keys"/>[plugin row]; a NaN key sorts last. A tree sorts each set of siblings.</summary>
    public void SortByNumber(double[] keys, int count, bool descending, UiMarkupListTreeShape? tree = null)
    {
        _comparer.Texts = null;
        _comparer.Numbers = keys;
        _comparer.Descending = descending;
        Apply(count, tree);
    }

    private void Apply(int count, UiMarkupListTreeShape? tree)
    {
        if (tree is not null)
        {
            ArrangeTree(tree, _comparer);
            _comparer.Texts = null;
            _comparer.Numbers = null;
            return;
        }
        if (_scratch.Length < count)
            _scratch = new int[count];
        for (int i = 0; i < count; i++)
            _scratch[i] = i;
        Array.Sort(_scratch, 0, count, _comparer);
        _comparer.Texts = null;
        _comparer.Numbers = null;

        (_viewToPlugin, _scratch) = (_scratch, _viewToPlugin);
        if (_pluginToView.Length < count)
            _pluginToView = new int[count];
        for (int view = 0; view < count; view++)
            _pluginToView[_viewToPlugin[view]] = view;
        _identity = false;
        ViewCount = PluginCount = count;
    }

    // Tree arrangement scratch: each parent's children grouped by a counting
    // sort (group g = parent + 1, so the top level is group 0), and an
    // explicit depth-first stack so a deep tree can't overflow the call stack.
    private int[] _groupEnd = Array.Empty<int>();
    private int[] _children = Array.Empty<int>();
    private int[] _stackNext = Array.Empty<int>();
    private int[] _stackEnd = Array.Empty<int>();

    private int GroupStart(int group) => group == 0 ? 0 : _groupEnd[group - 1];

    private void ArrangeTree(UiMarkupListTreeShape tree, IComparer<int>? order)
    {
        int count = tree.Count;
        if (_viewToPlugin.Length < count)
            _viewToPlugin = new int[count];
        if (_pluginToView.Length < count)
            _pluginToView = new int[count];
        if (_children.Length < count)
            _children = new int[count];
        if (_groupEnd.Length < count + 2)
            _groupEnd = new int[count + 2];
        if (_stackNext.Length < count + 1)
        {
            _stackNext = new int[count + 1];
            _stackEnd = new int[count + 1];
        }

        // Count each group's children at [group + 1], prefix-sum to starts,
        // then place rows in plugin order; each start ends as its group's end.
        Array.Clear(_groupEnd, 0, count + 2);
        for (int row = 0; row < count; row++)
            _groupEnd[tree.Parent(row) + 2]++;
        for (int g = 1; g < count + 2; g++)
            _groupEnd[g] += _groupEnd[g - 1];
        for (int row = 0; row < count; row++)
            _children[_groupEnd[tree.Parent(row) + 1]++] = row;

        if (order is not null)
        {
            for (int g = 0; g <= count; g++)
            {
                int start = GroupStart(g), length = _groupEnd[g] - start;
                if (length > 1)
                    Array.Sort(_children, start, length, order);
            }
        }

        Array.Fill(_pluginToView, -1, 0, count);
        int shown = 0, depth = 0;
        _stackNext[0] = 0;
        _stackEnd[0] = _groupEnd[0];
        while (depth >= 0)
        {
            if (_stackNext[depth] == _stackEnd[depth])
            {
                depth--;
                continue;
            }
            int row = _children[_stackNext[depth]++];
            _viewToPlugin[shown] = row;
            _pluginToView[row] = shown++;
            int group = row + 1;
            if (!tree.IsCollapsed(row) && GroupStart(group) < _groupEnd[group])
            {
                depth++;
                _stackNext[depth] = GroupStart(group);
                _stackEnd[depth] = _groupEnd[group];
            }
        }
        _identity = false;
        ViewCount = shown;
        PluginCount = count;
    }

    private sealed class KeyComparer : IComparer<int>
    {
        public string?[]? Texts;
        public double[]? Numbers;
        public bool Descending;

        public int Compare(int a, int b)
        {
            int order;
            if (Numbers is { } numbers)
            {
                bool aMissing = double.IsNaN(numbers[a]), bMissing = double.IsNaN(numbers[b]);
                if (aMissing != bMissing)
                    return aMissing ? 1 : -1;
                order = aMissing ? 0 : numbers[a].CompareTo(numbers[b]);
            }
            else
            {
                string? x = Texts![a], y = Texts[b];
                if ((x is null) != (y is null))
                    return x is null ? 1 : -1;
                order = x is null ? 0 : string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
            }
            if (order != 0)
                return Descending ? -order : order;
            return a.CompareTo(b);
        }
    }
}
