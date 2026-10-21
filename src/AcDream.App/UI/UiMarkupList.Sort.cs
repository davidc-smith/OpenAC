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

    /// <summary>Orders the view by the sorted column's keys (the cells, or its sortkeys).</summary>
    private void SortView(IReadOnlyList<UiMarkupListColumn> columns, int rowCount)
    {
        (int sorted, bool descending) = CurrentSort;
        if (sorted < 0)
        {
            _view.SetIdentity(rowCount);
            return;
        }
        if (_textKeys.Length < rowCount)
            _textKeys = new string?[rowCount];
        if (_numberKeys.Length < rowCount)
            _numberKeys = new double[rowCount];

        UiMarkupListColumn column = columns[sorted];
        if (column.SortNumberKeys is { } numbers)
        {
            IReadOnlyList<double> keys = numbers();
            for (int row = 0; row < rowCount; row++)
                _numberKeys[row] = row < keys.Count ? keys[row] : double.NaN;
            _view.SortByNumber(_numberKeys, rowCount, descending);
            return;
        }
        if (column.Sort == UiMarkupListSortKind.Bool)
        {
            IReadOnlyList<bool>? flags = _cachedCheckRows[sorted];
            for (int row = 0; row < rowCount; row++)
                _numberKeys[row] = flags is not null && row < flags.Count ? (flags[row] ? 1d : 0d) : double.NaN;
            _view.SortByNumber(_numberKeys, rowCount, descending);
            return;
        }
        IReadOnlyList<string>? cells = column.SortTextKeys?.Invoke() ?? _cachedTextRows[sorted];
        if (column.Sort == UiMarkupListSortKind.Number)
        {
            for (int row = 0; row < rowCount; row++)
            {
                _numberKeys[row] = cells is not null && row < cells.Count
                    && double.TryParse(cells[row], NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                        ? value
                        : double.NaN;
            }
            _view.SortByNumber(_numberKeys, rowCount, descending);
            return;
        }
        for (int row = 0; row < rowCount; row++)
            _textKeys[row] = cells is not null && row < cells.Count ? cells[row] : null;
        _view.SortByText(_textKeys, rowCount, descending);
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
