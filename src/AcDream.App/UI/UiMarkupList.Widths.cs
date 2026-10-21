using System.Globalization;
using AcDream.App.UI.Markup;

namespace AcDream.App.UI;

public sealed partial class UiMarkupList
{
    /// <summary>The narrowest a player can drag a column.</summary>
    public const float MinColumnWidth = 16f;

    // How far either side of a header boundary a press grabs it.
    private const float ResizeGrip = 3f;

    private const string ResizeCursorState = "resize";

    private static readonly IReadOnlyDictionary<string, UiCursorMedia> ResizeCursors = MakeResizeCursors();
    private static readonly IReadOnlyDictionary<string, UiCursorMedia> NoCursors =
        new Dictionary<string, UiCursorMedia>();

    private static IReadOnlyDictionary<string, UiCursorMedia> MakeResizeCursors()
    {
        RetailCursorCatalog.TryGetWindowControlCursor(CursorFeedbackKind.ResizeHorizontal, out UiCursorMedia cursor);
        return new Dictionary<string, UiCursorMedia> { [ResizeCursorState] = cursor };
    }

    /// <summary>Header boundaries can be dragged to resize the column to their left.</summary>
    public bool ColumnsResizable
    {
        get => _resizable;
        set
        {
            _resizable = value;
            SetStateCursors(value ? ResizeCursors : NoCursors);
        }
    }

    /// <summary>Bound widths, one per column (0 = automatic); when set, the plugin owns them.</summary>
    public Func<IReadOnlyList<float>>? WidthsSource { get; set; }

    /// <summary>Called with every column's width each time a drag or reset asks for a new one.</summary>
    public Action<IReadOnlyList<float>>? WidthsChanged { get; set; }

    internal RememberedValue? RememberedWidths { get; set; }

    private bool _resizable;

    // The markup's widths, one per column: 0 for automatic (`*`, and always the last).
    private float[] _markupWidths = Array.Empty<float>();
    private float[]? _hostWidths;
    private float[]? _askedWidths;
    private float[]? _resizeStartWidths;
    private int _resizeColumn = -1;
    private int _hoverBoundary = -1;
    private float _resizeStartWidth;
    private float _resizePressX;

    public override string ActiveCursorStateName =>
        Enabled && (_resizeColumn >= 0 || _hoverBoundary >= 0) ? ResizeCursorState : "";

    internal (float X, float W) ColumnLayoutForTest(int column) => _cachedLayout[column];

    /// <summary>Every column's width as asked for: bound, remembered or the markup's (0 = automatic).</summary>
    private IReadOnlyList<float> WidthState
    {
        get
        {
            if (WidthsSource is { } source)
            {
                IReadOnlyList<float> bound = source();
                return bound.Count == _markupWidths.Length ? bound : _markupWidths;
            }
            if (RememberedWidths is { } remembered && remembered.TryRefresh(out string? text))
                _hostWidths = DecodeWidths(text);
            return _hostWidths ?? (IReadOnlyList<float>)_markupWidths;
        }
    }

    private static string EncodeWidths(IReadOnlyList<float> widths) =>
        string.Join(",", widths.Select(w => ((int)MathF.Round(w)).ToString(CultureInfo.InvariantCulture)));

    private float[]? DecodeWidths(string? text)
    {
        if (text is null)
            return null;
        string[] parts = text.Split(',');
        if (parts.Length != _markupWidths.Length)
            return null;
        var widths = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out int width))
                return null;
            widths[i] = width;
        }
        return widths;
    }

    /// <summary>The boundary to the right of a column within grabbing distance; -1 for none.</summary>
    private int BoundaryAt(float x, float y)
    {
        if (!_resizable || !HeaderVisible || y < 0f || y >= HeaderHeight || _columns is not { } columns)
            return -1;
        for (int c = 0; c < columns.Count - 1; c++)
        {
            (float cellX, float cellW) = _cachedLayout[c];
            if (MathF.Abs(x - (cellX + cellW)) <= ResizeGrip)
                return c;
        }
        return -1;
    }

    private void BeginResize(int column, float x)
    {
        _resizeColumn = column;
        _resizePressX = x;
        _resizeStartWidth = _cachedLayout[column].w;
        _resizeStartWidths = WidthState.ToArray();
        _askedWidths = _resizeStartWidths;
    }

    private void DragResize(float x, float contentWidth)
    {
        (float cellX, _) = _cachedLayout[_resizeColumn];
        float max = MathF.Max(MinColumnWidth, contentWidth - cellX - MinColumnWidth);
        float width = Math.Clamp(MathF.Round(_resizeStartWidth + x - _resizePressX), MinColumnWidth, max);
        AskWidth(_resizeColumn, width);
    }

    /// <summary>Asks for one column's width; each distinct set of widths is reported once.</summary>
    private void AskWidth(int column, float width)
    {
        if (WidthsSource is not null && WidthsChanged is null)
            return;
        float[] next = (_askedWidths ?? WidthState).ToArray();
        if (next[column] == width)
            return;
        next[column] = width;
        _askedWidths = next;
        if (WidthsSource is null)
            _hostWidths = next;
        WidthsChanged?.Invoke(next);
    }

    private void EndResize()
    {
        if (_resizeColumn < 0)
            return;
        _resizeColumn = -1;
        CommitWidths(_resizeStartWidths);
        _askedWidths = null;
        _resizeStartWidths = null;
    }

    /// <summary>Remembers an unbound change once the player commits it.</summary>
    private void CommitWidths(float[]? before)
    {
        if (WidthsSource is not null || _askedWidths is null
            || (before is not null && before.AsSpan().SequenceEqual(_askedWidths)))
            return;
        string encoded = EncodeWidths(_askedWidths);
        RememberedWidths?.Remember(encoded == EncodeWidths(_markupWidths) ? null : encoded);
    }

    /// <summary>A double-click on a boundary: the column to its left goes back to its markup width.</summary>
    private void ResetWidth(int column)
    {
        float[] before = WidthState.ToArray();
        _askedWidths = before;
        AskWidth(column, _markupWidths[column]);
        CommitWidths(before);
        _askedWidths = null;
    }
}
