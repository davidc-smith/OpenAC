using AcDream.App.UI.Layout;
using AcDream.Core.Items.Gear;

namespace AcDream.App.UI.Hud;

/// <summary>What the Gear Deck needs from the retail salvage window: whether it is open and what it would take.</summary>
internal interface IGearSalvage
{
    /// <summary>The salvage window is showing with a tool loaded.</summary>
    bool IsOpen { get; }

    /// <summary>Changes whenever the panel's tool or item list does, so a cached count knows to recompute.</summary>
    int Revision { get; }

    /// <summary>The panel would take this item now (right material, owned, not already in).</summary>
    bool CanAdd(uint id);

    /// <summary>Puts the item in the panel; false when it refuses (it re-checks <see cref="CanAdd"/>).</summary>
    bool Add(uint id);

    /// <summary>One chat line about the hand-off.</summary>
    void Report(string text);
}

/// <summary>
/// <see cref="IGearSalvage"/> over the retail <see cref="SalvageUiController"/>. The controller is read
/// lazily because the salvage window mounts after the Gear Deck does.
/// </summary>
internal sealed class RuntimeGearSalvage(
    Func<SalvageUiController?> controller, Func<bool> windowVisible, Action<string> report) : IGearSalvage
{
    public bool IsOpen => windowVisible() && controller() is { ToolId: not 0u };

    public int Revision => controller() is { } c ? HashCode.Combine(c.ToolId, c.ItemCount) : 0;

    public bool CanAdd(uint id) => controller()?.CanAdd(id) ?? false;

    public bool Add(uint id) => controller()?.AddItem(id) ?? false;

    public void Report(string text) => report(text);
}

/// <summary>Picks what the deck hands to salvage and feeds it in, one place for every entry point.</summary>
internal static class GearSalvageHandOff
{
    /// <summary>The rows the panel would take, in order: not worn, not a pack, and addable.</summary>
    internal static List<GearItemView> Candidates(IReadOnlyList<GearItemView> rows, IGearSalvage salvage)
    {
        var kept = new List<GearItemView>();
        foreach (GearItemView row in rows)
            if (!row.Worn && !row.IsPack && salvage.CanAdd(row.Id)) kept.Add(row);
        return kept;
    }

    /// <summary>
    /// Adds each candidate in order and returns how many went in. The first item fixes the material, so
    /// later ones of another material are refused by <see cref="IGearSalvage.Add"/>; a shortfall is reported.
    /// </summary>
    internal static int AddAll(IReadOnlyList<GearItemView> candidates, IGearSalvage salvage)
    {
        int added = 0;
        foreach (GearItemView row in candidates)
            if (salvage.Add(row.Id)) added++;
        if (added == 0 && candidates.Count > 0)
            salvage.Report("None of those can be salvaged with that tool.");
        else if (added < candidates.Count)
            salvage.Report($"Added {added} of {candidates.Count} to salvage.");
        return added;
    }
}
