using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.UI.Hud;

/// <summary>A character's Spell Deck rows and attachment, validated. <paramref name="DetachedRows"/>
/// is the row count the stored detached placement goes with (its height is that many rows at
/// its scale); null when not recorded, which means the current row count.</summary>
public sealed record SpellDeckSettings(IReadOnlyList<SpellDeckRow> Rows, bool Attached, int? DetachedRows = null)
{
    public static SpellDeckSettings Default { get; } =
        new([new(1, 0), new(2, 1), new(3, 2)], Attached: true);

    public int NextRowId => Rows.Count == 0 ? 1 : Rows.Max(r => r.Id) + 1;

    public SpellDeckSettings Normalize()
    {
        int? detachedRows = DetachedRows is int n
            ? Math.Clamp(n, SpellDeckGeometry.MinRows, SpellDeckGeometry.MaxRows)
            : null;
        if (Rows.Count == 0) return Default with { Attached = Attached, DetachedRows = detachedRows };
        var seen = new HashSet<int>();
        int next = Math.Max(1, Rows.Max(r => r.Id) + 1);
        var rows = new List<SpellDeckRow>();
        foreach (SpellDeckRow row in Rows.Take(SpellDeckGeometry.MaxRows))
        {
            int id = row.Id > 0 && seen.Add(row.Id) ? row.Id : next++;
            seen.Add(id);
            rows.Add(new(id, row.Tab is >= 0 and < SpellDeckModel.Tabs ? row.Tab : 0));
        }
        return new(rows, Attached, detachedRows);
    }

    public static SpellDeckSettings Load(SettingsStore? store, string toonKey) =>
        store?.LoadSpellDeck(toonKey) is { } s
            ? new SpellDeckSettings(s.Rows.Select(r => new SpellDeckRow(r.Id, r.Tab)).ToArray(), s.Attached, s.DetachedRows).Normalize()
            : Default;

    public void Save(SettingsStore? store, string toonKey) =>
        store?.SaveSpellDeck(toonKey, new StoredSpellDeck(Attached, Rows.Select(r => (r.Id, r.Tab)).ToArray(), DetachedRows));
}
