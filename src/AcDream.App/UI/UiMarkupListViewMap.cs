namespace AcDream.App.UI;

/// <summary>
/// The order a markup list shows its rows in: view row (top to bottom) to
/// plugin row (the plugin's index) and back. Identity until sorted. A sort is
/// stable (ties keep the plugin's order) and puts rows without a key last,
/// ascending or descending.
/// </summary>
internal sealed class UiMarkupListViewMap
{
    private int[] _viewToPlugin = Array.Empty<int>();
    private int[] _pluginToView = Array.Empty<int>();
    private int[] _scratch = Array.Empty<int>();
    private bool _identity = true;
    private readonly KeyComparer _comparer = new();

    public int Count { get; private set; }

    /// <summary>The plugin row shown at a view row; −1 outside the rows.</summary>
    public int PluginRow(int view) =>
        (uint)view >= (uint)Count ? -1 : _identity ? view : _viewToPlugin[view];

    /// <summary>Where a plugin row is shown; −1 outside the rows.</summary>
    public int ViewRow(int plugin) =>
        (uint)plugin >= (uint)Count ? -1 : _identity ? plugin : _pluginToView[plugin];

    public void SetIdentity(int count)
    {
        _identity = true;
        Count = count;
    }

    /// <summary>Sorts by <paramref name="keys"/>[plugin row]; a null key sorts last.</summary>
    public void SortByText(string?[] keys, int count, bool descending)
    {
        _comparer.Texts = keys;
        _comparer.Numbers = null;
        _comparer.Descending = descending;
        Apply(count);
    }

    /// <summary>Sorts by <paramref name="keys"/>[plugin row]; a NaN key sorts last.</summary>
    public void SortByNumber(double[] keys, int count, bool descending)
    {
        _comparer.Texts = null;
        _comparer.Numbers = keys;
        _comparer.Descending = descending;
        Apply(count);
    }

    private void Apply(int count)
    {
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
        Count = count;
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
