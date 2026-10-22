using System.Globalization;
using System.Text.Json;
using AcDream.App.UI.Markup;

namespace AcDream.App.UI;

public sealed partial class UiMarkupList
{
    /// <summary>Indent per tree level, and the width of a row's arrow slot.</summary>
    public const float TreeIndent = 12f;

    /// <summary>The column the tree draws in (its first text column); −1 for a flat list.</summary>
    public int TreeColumn { get; set; } = -1;

    public bool IsTree => TreeColumn >= 0;

    /// <summary>Each plugin row's depth, 0 = top level.</summary>
    public Func<IReadOnlyList<int>>? TreeLevelsSource { get; set; }

    /// <summary>Each plugin row's stable key; a row past the end is "#&lt;row&gt;".</summary>
    public Func<IReadOnlyList<string>>? TreeKeysSource { get; set; }

    /// <summary>Bound expansion: the keys of expanded rows. When set, the plugin owns expansion.</summary>
    public Func<IReadOnlySet<string>>? ExpandedSource { get; set; }

    /// <summary>Called with a row's key and the state a user action asks for.</summary>
    public Action<string, bool>? ExpansionChanged { get; set; }

    /// <summary>Unbound: every row starts collapsed instead of expanded (<c>collapsed="true"</c>).</summary>
    public bool StartCollapsed { get; set; }

    internal RememberedValue? RememberedExpansion { get; set; }

    private readonly UiMarkupListTreeShape _tree = new();
    private IReadOnlyList<string> _treeKeys = Array.Empty<string>();

    // Unbound: the keys whose state differs from the default, as remembered.
    private HashSet<string>? _hostToggled;

    private string TreeKey(int row) =>
        row < _treeKeys.Count ? _treeKeys[row] : "#" + row.ToString(CultureInfo.InvariantCulture);

    private HashSet<string> HostToggled
    {
        get
        {
            if (RememberedExpansion is { } remembered && remembered.TryRefresh(out string? text))
                _hostToggled = DecodeKeys(text);
            return _hostToggled ??= new HashSet<string>(StringComparer.Ordinal);
        }
    }

    /// <summary>Reads the tree's levels, keys and expansion for this frame; null for a flat list.</summary>
    private UiMarkupListTreeShape? LoadTree(int rowCount)
    {
        if (!IsTree)
            return null;
        _tree.Load(TreeLevelsSource!(), rowCount);
        _treeKeys = TreeKeysSource!();
        IReadOnlySet<string>? bound = ExpandedSource?.Invoke();
        HashSet<string>? toggled = bound is null ? HostToggled : null;
        for (int row = 0; row < rowCount; row++)
        {
            if (!_tree.HasChildren(row))
                continue;
            string key = TreeKey(row);
            bool expanded = bound?.Contains(key) ?? toggled!.Contains(key) == StartCollapsed;
            _tree.SetCollapsed(row, !expanded);
        }
        return _tree;
    }

    private static HashSet<string> DecodeKeys(string? text)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (text is null)
            return keys;
        try
        {
            if (JsonSerializer.Deserialize<string?[]>(text) is { } values)
            {
                foreach (string? value in values)
                {
                    if (value is not null)
                        keys.Add(value);
                }
            }
        }
        catch (JsonException)
        {
        }
        return keys;
    }

    private static string EncodeKeys(HashSet<string> keys) =>
        JsonSerializer.Serialize(keys.Order(StringComparer.Ordinal).ToArray());

    /// <summary>The tree column's cell: indent, ▸/▾ for a parent, then the text after the arrow slot.</summary>
    private void DrawTreeCell(
        UiRenderContext context, IReadOnlyList<string>? texts, IReadOnlyList<uint>? colors,
        int row, float cellX, float y)
    {
        float slotX = TreeSlotX(row, cellX);
        if (_tree.HasChildren(row))
            DrawTreeArrow(context, slotX, y, expanded: !_tree.IsCollapsed(row));
        DrawTextCell(context, texts, colors, row, slotX + TreeIndent - Padding, y);
    }

    private float TreeSlotX(int row, float cellX) => cellX + Padding + _tree.Level(row) * TreeIndent;

    /// <summary>▾ (expanded: four rows narrowing) or ▸ (collapsed: four columns narrowing) in the arrow slot.</summary>
    private void DrawTreeArrow(UiRenderContext context, float slotX, float rowY, bool expanded)
    {
        float top = rowY + MathF.Floor((RowHeight - 7f) * 0.5f);
        for (int i = 0; i < 4; i++)
        {
            float length = 7f - 2f * i;
            if (expanded)
                context.DrawFill(slotX + 2f + i, top + 1f + i, length, 1f, TextColor);
            else
                context.DrawFill(slotX + 3f + i, top + i, 1f, length, TextColor);
        }
    }

    /// <summary>Whether a press at <paramref name="localX"/> lands in a parent row's arrow slot.</summary>
    private bool OnTreeArrow(int row, float cellX, float localX)
    {
        if (!_tree.HasChildren(row))
            return false;
        float slotX = TreeSlotX(row, cellX);
        return localX >= slotX && localX < slotX + TreeIndent;
    }

    /// <summary>
    /// A user collapse or expand of a parent row. Unbound, the host applies and
    /// remembers it; bound, the plugin is asked. A collapse drops the row's
    /// descendants from a multi selection.
    /// </summary>
    private void ToggleExpanded(int row)
    {
        if (!_tree.HasChildren(row))
            return;
        string key = TreeKey(row);
        bool expand = _tree.IsCollapsed(row);
        if (ExpandedSource is null)
        {
            HashSet<string> toggled = HostToggled;
            if (!toggled.Remove(key))
                toggled.Add(key);
            RememberedExpansion?.Remember(toggled.Count == 0 ? null : EncodeKeys(toggled));
            _tree.SetCollapsed(row, !expand);
        }
        ExpansionChanged?.Invoke(key, expand);
        if (!expand)
            DeselectHidden(row);
    }

    /// <summary>A collapse hides the row's descendants: a multi selection drops them, and an anchor or lead among them moves up to the row.</summary>
    private void DeselectHidden(int row)
    {
        if (!MultiSelect)
            return;
        int end = _tree.SubtreeEnd(row);
        if (_anchorRow > row && _anchorRow < end)
            _anchorRow = row;
        if (_leadRow > row && _leadRow < end)
            _leadRow = row;
        IReadOnlyList<int> current = SelectedRowsSource();
        var kept = new List<int>(current.Count);
        for (int i = 0; i < current.Count; i++)
        {
            int selected = current[i];
            if (selected <= row || selected >= end)
                kept.Add(selected);
        }
        if (kept.Count != current.Count)
            SelectedRowsChanged?.Invoke(kept.Distinct().Order().ToArray());
    }

    /// <summary>
    /// Right expands a collapsed parent or moves to an expanded one's first
    /// child; Left collapses an expanded parent or moves to the row's parent.
    /// Acts on the selected row (single) or the lead (multi). A single-select
    /// row hidden inside a collapsed parent acts as its nearest shown
    /// ancestor, so Right expands that parent and Left moves up from it; a
    /// multi lead is never hidden. With nothing selected in a single-select
    /// tree, either key selects the first shown row.
    /// </summary>
    private void HandleTreeKey(bool right)
    {
        int row = MultiSelect ? _leadRow : SelectedIndexSource();
        if (!MultiSelect && row < 0)
        {
            // Nothing selected yet: start at the first shown row.
            if (_view.ViewCount > 0)
                SelectionChanged?.Invoke(_view.PluginRow(0));
            return;
        }
        row = ShownRowFor(row);
        int view = _view.ViewRow(row);
        if (view < 0)
            return;
        bool parent = _tree.HasChildren(row);
        if (right)
        {
            if (!parent)
                return;
            if (_tree.IsCollapsed(row))
                ToggleExpanded(row);
            else
                MoveTreeSelection(_view.PluginRow(view + 1));
            return;
        }
        if (parent && !_tree.IsCollapsed(row))
            ToggleExpanded(row);
        else if (_tree.Parent(row) >= 0)
            MoveTreeSelection(_tree.Parent(row));
    }

    /// <summary>The row itself when shown, else its nearest shown ancestor; -1 when none or out of range.</summary>
    private int ShownRowFor(int row)
    {
        while (row >= 0 && _view.ViewRow(row) < 0)
            row = _tree.Parent(row);
        return row;
    }

    private void MoveTreeSelection(int row)
    {
        if (row < 0)
            return;
        if (MultiSelect)
        {
            _anchorRow = _leadRow = row;
            SelectedRowsChanged?.Invoke([row]);
        }
        else
        {
            SelectionChanged?.Invoke(row);
        }
    }
}
