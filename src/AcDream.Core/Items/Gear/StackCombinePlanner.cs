namespace AcDream.Core.Items.Gear;

/// <summary>The merges that combine every partial stack of a kind: the emptiest into the fullest first.
/// A locked stack is never emptied (its id, and so its lock, would vanish); it can still be filled.</summary>
public static class StackCombinePlanner
{
    private sealed class Stack(GearItemView row) { public GearItemView Row = row; public int Count = row.Stack; }

    public static IReadOnlyList<GearMove> Plan(IEnumerable<GearItemView> rows, IReadOnlyCollection<uint>? only = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var moves = new List<GearMove>();
        IEnumerable<IGrouping<uint, GearItemView>> groups = rows
            .Where(r => !r.Worn && !r.IsPack && r.Weenie != 0u && r.StackMax > 1 && r.Stack > 0 && r.Stack < r.StackMax)
            .GroupBy(r => r.Weenie);
        foreach (IGrouping<uint, GearItemView> group in groups)
        {
            if (only is not null && !group.Any(r => only.Contains(r.Id))) continue;
            List<Stack> stacks = group.OrderByDescending(r => r.Stack).ThenBy(r => r.PackIndex).ThenBy(r => r.Slot)
                .Select(r => new Stack(r)).ToList();
            int t = 0, s = stacks.Count - 1;
            while (t < s)
            {
                Stack target = stacks[t], source = stacks[s];
                if (source.Row.Locked) { s--; continue; }
                int room = target.Row.StackMax - target.Count;
                if (room <= 0) { t++; continue; }
                int amount = Math.Min(room, source.Count);
                moves.Add(GearMove.Merge(source.Row.Id, target.Row.Id, amount));
                target.Count += amount;
                source.Count -= amount;
                if (source.Count == 0) s--;
            }
        }
        return moves;
    }
}
