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

    /// <summary>The panel takes one material only (the multiple-materials option is off): the first item in
    /// fixes the material, so <see cref="CanAdd"/> on an empty panel over-promises for mixed rows.</summary>
    bool OneMaterial { get; }

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

    public int Revision => controller()?.Revision ?? 0;

    public bool OneMaterial => controller() is { AllowsMultipleMaterials: false };

    public bool CanAdd(uint id) => controller()?.CanAdd(id) ?? false;

    public bool Add(uint id) => controller()?.AddItem(id) ?? false;

    public void Report(string text) => report(text);
}

/// <summary>Picks what the deck hands to salvage and feeds it in, one place for every entry point.</summary>
internal static class GearSalvageHandOff
{
    /// <summary>The rows the panel would take, in order: not worn, not a pack, and addable. When the panel takes
    /// <see cref="IGearSalvage.OneMaterial"/>, only rows of the first kept row's material: that one fixes the
    /// panel's material as it goes in, so "Add N" is what <see cref="AddAll"/> really adds.</summary>
    internal static List<GearItemView> Candidates(IReadOnlyList<GearItemView> rows, IGearSalvage salvage)
    {
        var kept = new List<GearItemView>();
        bool oneMaterial = salvage.OneMaterial;
        foreach (GearItemView row in rows)
        {
            if (row.Worn || row.IsPack || !salvage.CanAdd(row.Id)) continue;
            if (oneMaterial && kept.Count > 0 && row.Material != kept[0].Material) continue;
            kept.Add(row);
        }
        return kept;
    }

    /// <summary>
    /// Adds each candidate in order and returns how many went in. <see cref="Candidates"/> already holds
    /// to one material when the panel does; anything the panel still refuses is reported as a shortfall.
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
