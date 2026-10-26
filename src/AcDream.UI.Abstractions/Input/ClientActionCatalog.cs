namespace AcDream.UI.Abstractions.Input;

/// <summary>A client-only action shown on the keyboard page. The retail
/// action map has no row for it, so it carries its own identity: the magic
/// combat input map (so conflicts are checked against the spell keys) and an
/// action id in a range retail never uses.</summary>
public sealed record ClientActionEntry(
    InputAction Action, uint InputMapId, uint ActionId, string Label, string Group);

public static class ClientActionCatalog
{
    public const uint MagicCombatInputMap = 0x10000005u;
    public const uint FirstClientActionId = 0xC0000000u;
    public const string SpellBarGroup = "Spell bar";

    public static IReadOnlyList<ClientActionEntry> Entries { get; } = Build();

    private static List<ClientActionEntry> Build()
    {
        var list = new List<ClientActionEntry>();
        uint id = FirstClientActionId;
        for (int row = 1; row < SpellBarActions.Rows; row++)
            for (int slot = 0; slot < SpellBarActions.SlotsPerRow; slot++)
                list.Add(new(SpellBarActions.ForSlot(row, slot), MagicCombatInputMap, id++,
                    $"Spell bar row {row + 1}, slot {slot + 1}", SpellBarGroup));
        list.Add(new(InputAction.CastItemSpell, MagicCombatInputMap, id, "Cast item spell", SpellBarGroup));
        return list;
    }
}
