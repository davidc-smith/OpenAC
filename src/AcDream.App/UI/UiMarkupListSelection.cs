namespace AcDream.App.UI;

/// <summary>
/// The multi-select rules of a markup list, as pure functions over plugin
/// rows. Ranges run in view order (what the player sees). Every result is
/// plugin rows in ascending order, without duplicates or rows outside
/// 0..view.Count-1.
/// </summary>
internal static class UiMarkupListSelection
{
    /// <summary>
    /// The selection after a click on <paramref name="row"/>: that row alone;
    /// toggled in or out with <paramref name="add"/>; the view range from
    /// <paramref name="anchor"/> with <paramref name="range"/>, added to the
    /// current selection when <paramref name="add"/> is held too. A range with
    /// no valid anchor acts as the same click without Shift.
    /// </summary>
    public static int[] Click(
        IReadOnlyList<int> current, int row, bool add, bool range, int anchor, UiMarkupListViewMap view)
    {
        int count = view.Count;
        if (range && IsRow(anchor, count))
        {
            int[] span = Range(anchor, row, view);
            return add ? Normalize(current.Concat(span), count) : span;
        }
        if (add)
        {
            return Normalize(
                current.Contains(row) ? current.Where(r => r != row) : current.Append(row), count);
        }
        return Normalize([row], count);
    }

    /// <summary>Whether a click moves the anchor to the clicked row: every click but a range that used it.</summary>
    public static bool MovesAnchor(bool range, int anchor, int count) => !(range && IsRow(anchor, count));

    /// <summary>The plugin rows shown from <paramref name="anchor"/>'s view row to <paramref name="lead"/>'s.</summary>
    public static int[] Range(int anchor, int lead, UiMarkupListViewMap view)
    {
        int from = view.ViewRow(anchor), to = view.ViewRow(lead);
        if (to < 0)
            return [];
        if (from < 0)
            return [lead];
        (int low, int high) = from <= to ? (from, to) : (to, from);
        var rows = new int[high - low + 1];
        for (int v = low; v <= high; v++)
            rows[v - low] = view.PluginRow(v);
        Array.Sort(rows);
        return rows;
    }

    /// <summary>Every row.</summary>
    public static int[] All(int count) => Enumerable.Range(0, Math.Max(0, count)).ToArray();

    /// <summary>
    /// The plugin row one view row from <paramref name="lead"/>, clamped to the
    /// rows; the first view row when the lead is not a row; −1 with no rows.
    /// </summary>
    public static int Step(int lead, int delta, UiMarkupListViewMap view)
    {
        if (view.Count == 0)
            return -1;
        int at = view.ViewRow(lead);
        return view.PluginRow(at < 0 ? 0 : Math.Clamp(at + delta, 0, view.Count - 1));
    }

    private static bool IsRow(int row, int count) => (uint)row < (uint)count;

    private static int[] Normalize(IEnumerable<int> rows, int count) =>
        rows.Where(r => IsRow(r, count)).Distinct().Order().ToArray();
}
