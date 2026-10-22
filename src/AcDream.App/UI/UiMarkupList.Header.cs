using System.Numerics;

namespace AcDream.App.UI;

public sealed partial class UiMarkupList
{
    /// <summary>The height of the header row a <c>header="true"</c> list draws above its rows.</summary>
    public const float HeaderHeight = 18f;

    /// <summary>Draws a row of column captions above the rows (the column form only).</summary>
    public bool ShowHeader { get; set; }

    private bool HeaderVisible => ShowHeader && _columns is { Count: > 0 };

    /// <summary>Where the rows and the scrollbar start: under the header when there is one.</summary>
    private float RowsTop => HeaderVisible ? HeaderHeight : 0f;

    private float RowsHeight => MathF.Max(0f, Height - RowsTop);

    // The header cell the left button went down on; -1 for none.
    private int _headerPressColumn = -1;

    /// <summary>
    /// The header: Classic draws the list's own dark background under the
    /// captions, the plugin theme its field colour; both draw a
    /// border-coloured underline and a line between cells.
    /// </summary>
    private void DrawHeader(UiRenderContext context, IReadOnlyList<UiMarkupListColumn> columns)
    {
        context.DrawFill(0f, 0f, Width, HeaderHeight, ThemePalette is { } palette ? palette.Field : BackgroundColor);
        context.DrawFill(0f, HeaderHeight - 1f, Width, 1f, BorderColor);

        (int sorted, bool descending) = CurrentSort;
        for (int c = 0; c < columns.Count; c++)
        {
            (float cellX, float cellW) = _cachedLayout[c];
            if (cellW <= 0f)
                continue;
            if (c < columns.Count - 1)
                context.DrawFill(cellX + cellW - 1f, 2f, 1f, HeaderHeight - 4f, BorderColor);
            DrawHeaderCell(context, columns[c], c, cellX, cellW, c == sorted, descending);
        }
    }

    private void DrawHeaderCell(
        UiRenderContext context, UiMarkupListColumn column, int c, float cellX, float cellW,
        bool sorted, bool descending)
    {
        if (column.SelectAll)
        {
            if (column.SelectAllChanged is not null)
            {
                UiCheckBoxGlyph.Draw(context,
                    cellX + 1f + MathF.Max(0f, cellW - 2f - UiCheckBoxGlyph.Size) * 0.5f,
                    MathF.Max(1f, (HeaderHeight - UiCheckBoxGlyph.Size) * 0.5f),
                    SelectAllState(c), ThemePalette);
            }
            return;
        }
        string caption = column.HeaderSource?.Invoke() ?? string.Empty;
        context.PushClip(cellX, 0f, MathF.Max(0f, cellW - (sorted ? 12f : 0f)), HeaderHeight);
        try
        {
            DrawCaption(context, caption, cellX + Padding, 0f, HeaderHeight, TextColor);
        }
        finally
        {
            context.PopClip();
        }
        if (sorted)
            DrawSortGlyph(context, descending, cellX + cellW);
    }

    private void DrawCaption(UiRenderContext context, string text, float x, float top, float height, Vector4 color)
    {
        float textY = top + MathF.Max(0f, (height - (DatFont?.LineHeight ?? 14f)) * 0.5f);
        if (DatFont is { } font)
            context.DrawStringDat(font, text, x, textY, color, ThemePalette is null);
        else
            context.DrawString(text, x, textY, color);
    }

    private int HeaderColumnAt(float x)
    {
        if (_columns is not { } columns)
            return -1;
        for (int c = 0; c < columns.Count; c++)
        {
            (float cellX, float cellW) = _cachedLayout[c];
            if (x >= cellX && x < cellX + cellW)
                return c;
        }
        return -1;
    }

    /// <summary>
    /// Pointer input over the header. Returns true when it is consumed; moves,
    /// hovers and presses that began in the rows carry on to the rows.
    /// </summary>
    private bool HandleHeaderEvent(in UiEvent e, float contentWidth)
    {
        if (e.Type == UiEventType.MouseUp)
            NoteRelease();
        if (_resizeColumn >= 0)
        {
            switch (e.Type)
            {
                case UiEventType.MouseMove:
                    DragResize(e.Data1, contentWidth);
                    return true;
                case UiEventType.MouseUp:
                    EndResize();
                    return true;
                case UiEventType.CaptureChanged:
                    EndResize();
                    break;
            }
        }
        if (!HeaderVisible)
        {
            _hoverBoundary = -1;
            return false;
        }
        bool inHeader = e.Data2 >= 0 && e.Data2 < HeaderHeight;
        switch (e.Type)
        {
            case UiEventType.HoverEnter:
            case UiEventType.MouseMove:
                _hoverBoundary = BoundaryAt(e.Data1, e.Data2);
                return false;
            case UiEventType.HoverLeave:
                _hoverBoundary = -1;
                return false;
            case UiEventType.MouseDown:
                if (!inHeader)
                    return false;
                if (Enabled && BoundaryAt(e.Data1, e.Data2) is var boundary and >= 0)
                {
                    BeginResize(boundary, e.Data1);
                    return true;
                }
                _headerPressColumn = Enabled ? HeaderColumnAt(e.Data1) : -1;
                return true;
            case UiEventType.MouseUp:
            {
                if (_headerPressColumn < 0)
                    return false;
                int pressed = _headerPressColumn;
                _headerPressColumn = -1;
                if (inHeader && Enabled && HeaderColumnAt(e.Data1) == pressed)
                    HeaderClicked(pressed);
                return true;
            }
            case UiEventType.CaptureChanged:
                _headerPressColumn = -1;
                return false;
            case UiEventType.Click:
                return inHeader;
            case UiEventType.DoubleClick:
                if (inHeader && Enabled && DoubleClickWasAtRest
                    && BoundaryAt(e.Data1, e.Data2) is var reset and >= 0)
                    ResetWidth(reset);
                return inHeader;
        }
        return false;
    }

    /// <summary>The select-all box: ticked when every row is, clear when none is, else indeterminate.</summary>
    private bool? SelectAllState(int column)
    {
        IReadOnlyList<bool>? flags = _cachedCheckRows[column];
        int ticked = 0;
        for (int row = 0; row < _cachedRowCount; row++)
        {
            if (flags is not null && row < flags.Count && flags[row])
                ticked++;
        }
        return ticked == 0 ? false : ticked == _cachedRowCount ? true : null;
    }

    /// <summary>A press and release on the same header cell.</summary>
    private void HeaderClicked(int column)
    {
        if (_columns is { } columns && columns[column].SelectAll)
        {
            RefreshRows(columns);
            columns[column].SelectAllChanged?.Invoke(SelectAllState(column) != true);
            return;
        }
        CycleSort(column);
    }
}
