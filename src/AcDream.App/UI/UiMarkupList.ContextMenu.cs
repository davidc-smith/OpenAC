namespace AcDream.App.UI;

public sealed partial class UiMarkupList
{
    /// <summary>
    /// A right click about to open the list's context menu, at a point in list
    /// coordinates: selects the row under it, unless the row is already in a
    /// multi selection, and returns its plugin row; −1 over the header, the
    /// scrollbar or empty space, where the selection is left alone.
    /// </summary>
    internal int PrepareContextMenu(float x, float y)
    {
        int row = ContextMenuRowAt(x, y);
        if (row < 0 || !Enabled)
            return row;
        if (MultiSelect)
        {
            if (!SelectedRowsSource().Contains(row))
            {
                _anchorRow = _leadRow = row;
                SelectedRowsChanged?.Invoke(new[] { row });
            }
        }
        else if (SelectedIndexSource() != row)
        {
            SelectionChanged?.Invoke(row);
        }
        return row;
    }

    /// <summary>The plugin row under a point in list coordinates, from the rows as they are now; −1 for none.</summary>
    private int ContextMenuRowAt(float x, float y)
    {
        RefreshSelectionRows();
        int rowCount = _view.ViewCount;
        int visibleRows = VisibleRows;
        float contentWidth = rowCount > visibleRows ? MathF.Max(0f, Width - ScrollbarWidth) : Width;
        if (x < 0f || x >= contentWidth || y < RowsTop)
            return -1;
        int row = (int)MathF.Floor((y - RowsTop) / MathF.Max(1f, RowHeight));
        int index = _topRow + row;
        if (row < 0 || row >= visibleRows || index < 0 || index >= rowCount)
            return -1;
        return _view.PluginRow(index);
    }
}
