using System.Linq;
using AcDream.Core.Items;

namespace AcDream.App.Spells;

/// <summary>A wielded item that carries a castable spell.</summary>
public readonly record struct ItemSpell(uint ItemId, uint SpellId);

/// <summary>
/// Finds the spell on the player's wielded caster: the retail spell bar's
/// endowment slot and the Spell Deck's item slot both read it from here.
/// </summary>
public static class ItemSpellSource
{
    public static ItemSpell? Find(ClientObjectTable objects, uint playerGuid)
    {
        ClientObject? item = objects.GetEquippedBy(playerGuid)
            .FirstOrDefault(o =>
                (o.CurrentlyEquippedLocation & EquipMask.Held) != 0
                && (o.Type & ItemType.Caster) != 0
                && o.SpellId.GetValueOrDefault() != 0u);
        return item is null ? null : new ItemSpell(item.ObjectId, item.SpellId!.Value);
    }
}
