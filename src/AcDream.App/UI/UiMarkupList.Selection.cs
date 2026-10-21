using Silk.NET.Input;

namespace AcDream.App.UI;

/// <summary>The selection keys held during a pick: Add is Ctrl or Cmd (Super), Range is Shift.</summary>
internal readonly record struct UiSelectModifiers(bool Add, bool Range);

public sealed partial class UiMarkupList
{
    /// <summary><c>selectmode="multi"</c>: any number of rows, read from <see cref="SelectedRowsSource"/>.</summary>
    public bool MultiSelect { get; set; }

    /// <summary>The selected plugin rows in multi mode. The plugin owns them; the list keeps no selection.</summary>
    public Func<IReadOnlyList<int>> SelectedRowsSource { get; set; } = static () => Array.Empty<int>();

    /// <summary>Called in multi mode with the selection a user action asks for, plugin rows ascending.</summary>
    public Action<IReadOnlyList<int>>? SelectedRowsChanged { get; set; }

    /// <summary>The held selection keys; null reads the root's keyboard.</summary>
    internal Func<UiSelectModifiers>? ModifiersSource { get; set; }

    // The row a Shift range runs from (the last plain or Ctrl pick) and the
    // row keyboard steps move from (the last row picked or stepped to), as
    // plugin rows so they follow their rows across a sort; −1 for none.
    // Neither is a selection: what is selected is whatever the plugin returns.
    private int _anchorRow = -1, _leadRow = -1;

    // SelectedRowsSource as a per-plugin-row mask, read once per draw.
    private bool[] _selectedMask = Array.Empty<bool>();

    private UiSelectModifiers HeldModifiers()
    {
        if (ModifiersSource is { } source)
            return source();
        if (FindRoot()?.Keyboard is not { } keyboard)
            return default;
        return new UiSelectModifiers(
            Add: keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight)
                || keyboard.IsKeyPressed(Key.SuperLeft) || keyboard.IsKeyPressed(Key.SuperRight),
            Range: keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight));
    }

    /// <summary>A pointer pick of a plugin row: single mode reports the row, multi mode applies the click rules.</summary>
    private void PickRow(int pluginRow)
    {
        if (!MultiSelect)
        {
            SelectionChanged?.Invoke(pluginRow);
            return;
        }
        UiSelectModifiers keys = HeldModifiers();
        int[] next = UiMarkupListSelection.Click(
            SelectedRowsSource(), pluginRow, keys.Add, keys.Range, _anchorRow, _view);
        if (UiMarkupListSelection.MovesAnchor(keys.Range, _anchorRow, _view.PluginCount))
            _anchorRow = pluginRow;
        _leadRow = pluginRow;
        SelectedRowsChanged?.Invoke(next);
    }

    /// <summary>Reads the multi-mode selection into the mask the band is drawn from.</summary>
    private void RefreshSelectedMask(int rowCount)
    {
        if (_selectedMask.Length < rowCount)
            _selectedMask = new bool[rowCount];
        else
            Array.Clear(_selectedMask);
        IReadOnlyList<int> rows = SelectedRowsSource();
        for (int i = 0; i < rows.Count; i++)
        {
            int row = rows[i];
            if ((uint)row < (uint)rowCount)
                _selectedMask[row] = true;
        }
    }

    /// <summary>Whether a plugin row draws the selection band this frame.</summary>
    private bool RowBanded(int pluginRow, int selected) => MultiSelect
        ? (uint)pluginRow < (uint)_selectedMask.Length && _selectedMask[pluginRow]
        : pluginRow == selected && SelectionBandEnabled;

    private bool _selectionFocused;

    /// <summary>
    /// Multi-mode focus and selection keys: Up/Down step the lead (Shift
    /// extends from the anchor), Ctrl/Cmd+A selects every row; a tree list also takes Left/Right. Up and Down are
    /// always taken while focused; other keys fall through.
    /// </summary>
    private bool HandleSelectionEvent(in UiEvent e)
    {
        switch (e.Type)
        {
            case UiEventType.FocusGained when TabStop:
                _selectionFocused = true;
                return true;
            case UiEventType.FocusLost when TabStop:
                _selectionFocused = false;
                return true;
            case UiEventType.KeyDown when _selectionFocused && Enabled:
                break;
            default:
                return false;
        }

        UiSelectModifiers keys = HeldModifiers();
        var key = (Key)e.Data0;
        if (IsTree && key is Key.Left or Key.Right)
        {
            RefreshSelectionRows();
            HandleTreeKey(key == Key.Right);
            return true;
        }
        if (!MultiSelect)
            return false;
        if (key == Key.A && keys.Add)
        {
            RefreshSelectionRows();
            SelectedRowsChanged?.Invoke(UiMarkupListSelection.All(_view));
            return true;
        }
        int delta = key switch { Key.Up => -1, Key.Down => 1, _ => 0 };
        if (delta == 0)
            return false;

        RefreshSelectionRows();
        int lead = UiMarkupListSelection.Step(_leadRow, delta, _view);
        if (lead < 0)
            return true;
        int[] next;
        if (keys.Range && (uint)_anchorRow < (uint)_view.PluginCount)
        {
            next = UiMarkupListSelection.Range(_anchorRow, lead, _view);
        }
        else
        {
            next = [lead];
            _anchorRow = lead;
        }
        _leadRow = lead;
        SelectedRowsChanged?.Invoke(next);
        return true;
    }

    /// <summary>Reads the rows now, so keys act on the current data and order; returns the row count.</summary>
    private int RefreshSelectionRows()
    {
        if (Columns is { Count: > 0 } columns)
            return RefreshRows(columns);
        int count = ItemsSource().Count;
        _view.SetIdentity(count);
        return count;
    }

    /// <summary>The gold outline a focused multi list draws over its border.</summary>
    private void DrawSelectionFocus(UiRenderContext context)
    {
        if (_selectionFocused)
            context.DrawRectOutline(0f, 0f, Width, Height, UiMarkupExpanderHeader.FocusColor, 1f);
    }
}
