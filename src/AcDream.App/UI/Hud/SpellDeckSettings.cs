using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.UI.Hud;

/// <summary>A character's Spell Deck, validated: the one tab the whole deck shows (0-7 server
/// tabs, 8+ local tabs), how many rows show it, attachment, the local tabs and each tab's slot layout.
/// <paramref name="DetachedRows"/> is the row count the stored detached placement goes with (its
/// height is that many rows at its scale); null when not recorded, which means the current row count.
/// <paramref name="LocalTabs"/> gives how many local tabs exist (and, saved, each one's spells once in
/// reading order). <paramref name="Layouts"/> is each tab's slot layout by tab number; null for data saved
/// before layouts existed (v2), whose local tabs <see cref="LayoutsAt"/> lays out in reading order.</summary>
public sealed record SpellDeckSettings(
    int Tab,
    int RowCount,
    bool Attached,
    int? DetachedRows,
    IReadOnlyList<IReadOnlyList<uint>> LocalTabs,
    IReadOnlyDictionary<int, SpellDeckLayout>? Layouts = null)
{
    public const int MaxLocalTabs = 8;

    /// <summary>Saved slots in a column at or past this are dropped on load. Well past the widest deck
    /// (<see cref="SpellDeckGeometry.MaxCapacity"/>), so columns saved by a wider deck (v2 held 27) keep
    /// their spells: they stay unshown, and the deck widens as far as it can to show them.</summary>
    public const int MaxSavedColumns = 64;

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
        int tabCount = SpellDeckTabs.ServerTabs + local.Count;
        Dictionary<int, SpellDeckLayout>? layouts = null;
        if (Layouts is not null)
        {
            layouts = [];
            foreach ((int tab, SpellDeckLayout? layout) in Layouts)
            {
                if (tab < 0 || tab >= tabCount || layout is null) continue;
                if (tab >= SpellDeckTabs.ServerTabs)
                {
                    // A local tab is only its layout: nothing may be hidden or dropped.
                    SpellDeckLayout fitted = FitLocal(layout);
                    if (!fitted.IsEmpty) layouts[tab] = fitted;
                    continue;
                }
                // Slots far past the deck's limits are junk; a server tab re-places those spells on reconcile.
                SpellDeckLayout kept = SpellDeckLayout.FromSlots(layout.Filled
                    .Where(s => s.Row < SpellDeckGeometry.MaxRows && s.Col < MaxSavedColumns)
                    .Select(s => (s.Row, s.Col, s.SpellId)));
                if (!kept.IsEmpty) layouts[tab] = kept;
            }
        }
        return new(
            Tab >= 0 && Tab < tabCount ? Tab : 0,
            Math.Clamp(RowCount, SpellDeckGeometry.MinRows, SpellDeckGeometry.MaxRows),
            Attached,
            detachedRows,
            local,
            layouts);
    }

    /// <summary>Each tab's layout (non-empty ones only). For v2 data (no <see cref="Layouts"/>) each local tab's
    /// list is laid out in reading order at <c>max(perRow, ceil(count / MaxRows))</c> slots per row (capped at
    /// <see cref="SpellDeckGeometry.MaxCapacity"/>), so it fits the rows; server tabs get none and fill from the
    /// server on reconcile.</summary>
    public IReadOnlyDictionary<int, SpellDeckLayout> LayoutsAt(int perRow)
    {
        if (Layouts is not null)
            return Layouts.Where(kv => !kv.Value.IsEmpty).ToDictionary(kv => kv.Key, kv => kv.Value);
        var migrated = new Dictionary<int, SpellDeckLayout>();
        for (int i = 0; i < LocalTabs.Count; i++)
            if (LocalTabs[i].Count > 0)
            {
                int count = LocalTabs[i].Count;
                int cols = Math.Clamp(
                    Math.Max(perRow, (count + SpellDeckGeometry.MaxRows - 1) / SpellDeckGeometry.MaxRows),
                    1, SpellDeckGeometry.MaxCapacity);
                migrated[SpellDeckTabs.ServerTabs + i] = FitLocal(SpellDeckLayout.FromList(LocalTabs[i], cols));
            }
        return migrated;
    }

    /// <summary>A local tab's layout with every slot outside the usable grid (row at or past
    /// <see cref="SpellDeckGeometry.MaxRows"/>, column at or past <see cref="SpellDeckGeometry.MaxCapacity"/>)
    /// re-placed, in reading order, into the first empty slot inside it. Only a full grid can leave a spell out.</summary>
    public static SpellDeckLayout FitLocal(SpellDeckLayout layout)
    {
        static bool Inside(SpellDeckSlotSpell s) => s.Row < SpellDeckGeometry.MaxRows && s.Col < SpellDeckGeometry.MaxCapacity;
        if (layout.Filled.All(Inside)) return layout;
        SpellDeckLayout fitted = SpellDeckLayout.FromSlots(layout.Filled.Where(Inside).Select(s => (s.Row, s.Col, s.SpellId)));
        foreach (SpellDeckSlotSpell s in layout.Filled.Where(s => !Inside(s)))
            if (fitted.FirstEmpty(SpellDeckGeometry.MaxRows, SpellDeckGeometry.MaxCapacity) is { } empty)
                fitted = fitted.Place(empty.Row, empty.Col, s.SpellId);
        return fitted;
    }

    public static SpellDeckSettings Load(SettingsStore? store, string toonKey) =>
        store?.LoadSpellDeck(toonKey) is { } s
            ? new SpellDeckSettings(s.Tab, s.RowCount, s.Attached, s.DetachedRows, s.LocalTabs, FromStored(s.Layouts)).Normalize()
            : Default;

    public void Save(SettingsStore? store, string toonKey) =>
        store?.SaveSpellDeck(toonKey, new StoredSpellDeck(Tab, RowCount, Attached, DetachedRows, LocalTabs, ToStored(Layouts)));

    private static Dictionary<int, SpellDeckLayout>? FromStored(IReadOnlyDictionary<int, IReadOnlyList<StoredSpellDeckSlot>>? stored)
    {
        if (stored is null) return null;
        var layouts = new Dictionary<int, SpellDeckLayout>();
        foreach ((int tab, IReadOnlyList<StoredSpellDeckSlot> slots) in stored)
            layouts[tab] = SpellDeckLayout.FromSlots(slots
                .Where(s => s.Row >= 0 && s.Col >= 0)
                .Select(s => (s.Row, s.Col, s.Spell)));
        return layouts;
    }

    private static Dictionary<int, IReadOnlyList<StoredSpellDeckSlot>>? ToStored(IReadOnlyDictionary<int, SpellDeckLayout>? layouts) =>
        layouts?.Where(kv => !kv.Value.IsEmpty).ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<StoredSpellDeckSlot>)[.. kv.Value.Filled.Select(s => new StoredSpellDeckSlot(s.Row, s.Col, s.SpellId))]);
}
