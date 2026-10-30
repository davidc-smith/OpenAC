namespace AcDream.Core.Items.Gear;

/// <summary>The puts that reorder one pack by type group, then name, then workmanship (best first).
/// Each put takes the next item to its place; places before it are already right, so it only moves up.</summary>
public static class PackTidyPlanner
{
    public static IReadOnlyList<GearMove> Plan(uint packId, IReadOnlyList<GearItemView> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        List<GearItemView> order = rows.OrderBy(r => (int)r.Group)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(r => r.Workmanship)
            .ThenBy(r => r.Slot).ToList();
        var now = rows.ToList();
        var moves = new List<GearMove>();
        for (int i = 0; i < order.Count; i++)
        {
            int at = now.IndexOf(order[i]);
            if (at == i) continue;
            moves.Add(GearMove.Put(order[i].Id, packId, i));
            now.RemoveAt(at);
            now.Insert(i, order[i]);
        }
        return moves;
    }
}
