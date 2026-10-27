using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.UI.Hud;

/// <summary>A character's Spell Deck, validated: the one tab the whole deck shows (0-7 server
/// tabs, 8+ local tabs), how many rows continue it, attachment, and the local tabs' spell ids.
/// <paramref name="DetachedRows"/> is the row count the stored detached placement goes with (its
/// height is that many rows at its scale); null when not recorded, which means the current row count.</summary>
public sealed record SpellDeckSettings(
    int Tab,
    int RowCount,
    bool Attached,
    int? DetachedRows,
    IReadOnlyList<IReadOnlyList<uint>> LocalTabs)
{
    public const int MaxLocalTabs = 8;

    public static SpellDeckSettings Default { get; } = new(0, 3, true, null, []);

    public SpellDeckSettings Normalize()
    {
        int? detachedRows = DetachedRows is int n
            ? Math.Clamp(n, SpellDeckGeometry.MinRows, SpellDeckGeometry.MaxRows)
            : null;
        var local = new List<IReadOnlyList<uint>>();
        foreach (IReadOnlyList<uint>? tab in (LocalTabs ?? []).Take(MaxLocalTabs))
        {
            var seen = new HashSet<uint>();
            local.Add([.. (tab ?? []).Where(id => id != 0 && seen.Add(id))]);
        }
        int tabCount = SpellDeckModel.Tabs + local.Count;
        return new(
            Tab >= 0 && Tab < tabCount ? Tab : 0,
            Math.Clamp(RowCount, SpellDeckGeometry.MinRows, SpellDeckGeometry.MaxRows),
            Attached,
            detachedRows,
            local);
    }

    public static SpellDeckSettings Load(SettingsStore? store, string toonKey) =>
        store?.LoadSpellDeck(toonKey) is { } s
            ? new SpellDeckSettings(s.Tab, s.RowCount, s.Attached, s.DetachedRows, s.LocalTabs).Normalize()
            : Default;

    public void Save(SettingsStore? store, string toonKey) =>
        store?.SaveSpellDeck(toonKey, new StoredSpellDeck(Tab, RowCount, Attached, DetachedRows, LocalTabs));
}
