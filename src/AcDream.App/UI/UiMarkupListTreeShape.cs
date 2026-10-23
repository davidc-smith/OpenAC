namespace AcDream.App.UI;

/// <summary>
/// A tree list's shape for one frame. The plugin's rows are the tree
/// flattened in pre-order, each with a depth; a row's descendants are the
/// rows straight after it that are deeper. Levels are clamped as they load: a
/// negative level, or a row past the end of the levels, is level 0, and a row
/// more than one deeper than the row before it is one deeper.
/// </summary>
internal sealed class UiMarkupListTreeShape
{
    private int[] _levels = Array.Empty<int>();
    private int[] _parents = Array.Empty<int>();
    private bool[] _collapsed = Array.Empty<bool>();
    private int[] _path = Array.Empty<int>();

    // The shape when the view order was last built from it.
    private int[] _builtLevels = Array.Empty<int>();
    private bool[] _builtCollapsed = Array.Empty<bool>();
    private int _builtCount = -1;

    public int Count { get; private set; }

    /// <summary>Loads <paramref name="count"/> rows' levels; every row starts expanded.</summary>
    public void Load(IReadOnlyList<int> levels, int count)
    {
        if (_levels.Length < count)
        {
            _levels = new int[count];
            _parents = new int[count];
            _collapsed = new bool[count];
            _path = new int[count + 1];
        }
        else
        {
            Array.Clear(_collapsed, 0, count);
        }
        int previous = -1;
        for (int row = 0; row < count; row++)
        {
            int level = row < levels.Count ? Math.Max(0, levels[row]) : 0;
            level = Math.Min(level, previous + 1);
            _levels[row] = level;
            _parents[row] = level == 0 ? -1 : _path[level - 1];
            _path[level] = row;
            previous = level;
        }
        Count = count;
    }

    public int Level(int row) => (uint)row < (uint)Count ? _levels[row] : 0;

    /// <summary>The row's parent; −1 for a top-level row.</summary>
    public int Parent(int row) => (uint)row < (uint)Count ? _parents[row] : -1;

    public bool HasChildren(int row) =>
        (uint)row < (uint)Count && row + 1 < Count && _levels[row + 1] > _levels[row];

    /// <summary>The first row after <paramref name="row"/>'s descendants.</summary>
    public int SubtreeEnd(int row)
    {
        int end = row + 1;
        while (end < Count && _levels[end] > _levels[row])
            end++;
        return end;
    }

    public bool IsCollapsed(int row) => (uint)row < (uint)Count && _collapsed[row];

    public void SetCollapsed(int row, bool collapsed)
    {
        if ((uint)row < (uint)Count)
            _collapsed[row] = collapsed;
    }

    /// <summary>Whether the levels or the collapsed rows differ from when <see cref="MarkBuilt"/> was last called.</summary>
    public bool Changed
    {
        get
        {
            if (Count != _builtCount)
                return true;
            for (int row = 0; row < Count; row++)
            {
                if (_levels[row] != _builtLevels[row] || _collapsed[row] != _builtCollapsed[row])
                    return true;
            }
            return false;
        }
    }

    /// <summary>Notes the shape a view order has just been built from.</summary>
    public void MarkBuilt()
    {
        if (_builtLevels.Length < Count)
        {
            _builtLevels = new int[Count];
            _builtCollapsed = new bool[Count];
        }
        Array.Copy(_levels, _builtLevels, Count);
        Array.Copy(_collapsed, _builtCollapsed, Count);
        _builtCount = Count;
    }
}
