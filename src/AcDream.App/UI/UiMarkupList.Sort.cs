using System.Globalization;
using AcDream.App.UI.Markup;

namespace AcDream.App.UI;

public sealed partial class UiMarkupList
{
    /// <summary>Bound sort column (−1 none); when set, the plugin owns the sort.</summary>
    public Func<int>? SortColumnSource { get; set; }

    /// <summary>Bound sort direction, read with <see cref="SortColumnSource"/>.</summary>
    public Func<bool>? SortDescendingSource { get; set; }

    /// <summary>Called with the sort a header click asks for: column (−1 none) and descending.</summary>
    public Action<int, bool>? SortChanged { get; set; }

    internal RememberedValue? RememberedSort { get; set; }

    private (int Column, bool Descending)? _hostSort;
    private string?[] _textKeys = Array.Empty<string?>();
    private double[] _numberKeys = Array.Empty<double>();
    // The cells a number column's keys were parsed from, so an unchanged cell
    // isn't parsed again; only the first _parsedRows are this column's.
    private string?[] _numberCells = Array.Empty<string?>();
    private int _parsedRows;

    // What the view order was last built from; the order is rebuilt only when one of these or a key changes.
    private bool _orderStale = true;
    private int _orderColumn;
    private bool _orderDescending;
    private int _orderRowCount;
    private bool _orderTree;

    /// <summary>How many times the row order has been built; for tests.</summary>
    internal int OrderBuilds { get; private set; }

    /// <summary>The sort in force (bound, remembered or none); an unsortable or missing column is none.</summary>
    public (int Column, bool Descending) CurrentSort
    {
        get
        {
            (int column, bool descending) = SortColumnSource is { } source
                ? (source(), SortDescendingSource?.Invoke() ?? false)
                : HostSort;
            return IsSortable(column) ? (column, descending) : (-1, false);
        }
    }

    private (int Column, bool Descending) HostSort
    {
        get
        {
            if (RememberedSort is { } remembered && remembered.TryRefresh(out string? text))
                _hostSort = DecodeSort(text) ?? (-1, false);
            return _hostSort ??= (-1, false);
        }
    }

    private bool IsSortable(int column) =>
        _columns is { } columns && column >= 0 && column < columns.Count
        && columns[column].Sort != UiMarkupListSortKind.None;

    private static (int, bool)? DecodeSort(string? text)
    {
        if (text is null)
            return null;
        int colon = text.IndexOf(':');
        if (colon <= 0
            || !int.TryParse(text.AsSpan(0, colon), NumberStyles.None, CultureInfo.InvariantCulture, out int column))
            return null;
        return text[(colon + 1)..] switch
        {
            "asc" => (column, false),
            "desc" => (column, true),
            _ => null,
        };
    }

    private static string EncodeSort(int column, bool descending) =>
        column.ToString(CultureInfo.InvariantCulture) + (descending ? ":desc" : ":asc");

    /// <summary>A click on a sortable header: ascending, then descending, then no sort.</summary>
    private void CycleSort(int column)
    {
        if (!IsSortable(column))
            return;
        (int current, bool descending) = CurrentSort;
        (int Column, bool Descending) next = current != column
            ? (column, false)
            : !descending ? (column, true) : (-1, false);
        if (SortColumnSource is not null)
        {
            SortChanged?.Invoke(next.Column, next.Descending);
            return;
        }
        _hostSort = next;
        RememberedSort?.Remember(next.Column < 0 ? null : EncodeSort(next.Column, next.Descending));
        SortChanged?.Invoke(next.Column, next.Descending);
    }

    /// <summary>
    /// Orders the view by the sorted column's keys (the cells, or its
    /// sortkeys). Runs every frame the rows are read, but builds the order
    /// again only when the sort, the row count, a key or the tree's shape
    /// has changed since it was last built.
    /// </summary>
    private void SortView(IReadOnlyList<UiMarkupListColumn> columns, int rowCount)
    {
        UiMarkupListTreeShape? tree = LoadTree(rowCount);
        (int sorted, bool descending) = CurrentSort;
        bool sameSort = !_orderStale && sorted == _orderColumn;
        bool changed = !sameSort || descending != _orderDescending || rowCount != _orderRowCount
            || (tree is not null) != _orderTree || tree?.Changed == true;
        if (sorted >= 0)
            changed |= LoadSortKeys(columns[sorted], sorted, rowCount, reuseParsed: sameSort);
        if (!changed)
            return;

        _orderStale = false;
        _orderColumn = sorted;
        _orderDescending = descending;
        _orderRowCount = rowCount;
        _orderTree = tree is not null;
        OrderBuilds++;
        tree?.MarkBuilt();
        if (sorted < 0)
            _view.SetIdentity(rowCount, tree);
        else if (UsesNumberKeys(columns[sorted]))
            _view.SortByNumber(_numberKeys, rowCount, descending, tree);
        else
            _view.SortByText(_textKeys, rowCount, descending, tree);
    }

    private static bool UsesNumberKeys(UiMarkupListColumn column) =>
        column.SortNumberKeys is not null || column.Sort is UiMarkupListSortKind.Bool or UiMarkupListSortKind.Number;

    /// <summary>Reads the sorted column's keys for this frame; true when any differs from last frame's.</summary>
    private bool LoadSortKeys(UiMarkupListColumn column, int sorted, int rowCount, bool reuseParsed)
    {
        bool changed = false;
        if (_textKeys.Length < rowCount)
        {
            _textKeys = new string?[rowCount];
            _numberKeys = new double[rowCount];
            _numberCells = new string?[rowCount];
            changed = true;
            reuseParsed = false;
        }

        if (column.SortNumberKeys is { } numbers)
        {
            IReadOnlyList<double> keys = numbers();
            for (int row = 0; row < rowCount; row++)
                changed |= SetNumberKey(row, row < keys.Count ? keys[row] : double.NaN);
            return changed;
        }
        if (column.Sort == UiMarkupListSortKind.Bool)
        {
            IReadOnlyList<bool>? flags = _cachedCheckRows[sorted];
            for (int row = 0; row < rowCount; row++)
                changed |= SetNumberKey(row, flags is not null && row < flags.Count ? (flags[row] ? 1d : 0d) : double.NaN);
            return changed;
        }
        IReadOnlyList<string>? cells = column.SortTextKeys?.Invoke() ?? _cachedTextRows[sorted];
        if (column.Sort == UiMarkupListSortKind.Number)
        {
            int parsed = reuseParsed ? _parsedRows : 0;
            _parsedRows = rowCount;
            for (int row = 0; row < rowCount; row++)
            {
                string? cell = cells is not null && row < cells.Count ? cells[row] : null;
                if (row < parsed && string.Equals(cell, _numberCells[row], StringComparison.Ordinal))
                    continue;
                _numberCells[row] = cell;
                changed |= SetNumberKey(row,
                    double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                        ? value
                        : double.NaN);
            }
            return changed;
        }
        for (int row = 0; row < rowCount; row++)
        {
            string? cell = cells is not null && row < cells.Count ? cells[row] : null;
            if (!string.Equals(cell, _textKeys[row], StringComparison.Ordinal))
            {
                _textKeys[row] = cell;
                changed = true;
            }
        }
        return changed;
    }

    private bool SetNumberKey(int row, double key)
    {
        if (_numberKeys[row].Equals(key))
            return false;
        _numberKeys[row] = key;
        return true;
    }

    /// <summary>▲ (ascending) or ▼ at the right of a sorted header cell.</summary>
    private void DrawSortGlyph(UiRenderContext context, bool descending, float cellRight)
    {
        float left = cellRight - 10f;
        float top = MathF.Floor((HeaderHeight - 4f) * 0.5f);
        for (int r = 0; r < 4; r++)
        {
            float width = 1f + 2f * (descending ? 3 - r : r);
            context.DrawFill(left + (7f - width) * 0.5f, top + r, width, 1f, TextColor);
        }
    }
}
