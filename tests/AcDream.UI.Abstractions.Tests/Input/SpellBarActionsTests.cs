using AcDream.UI.Abstractions.Input;
using Silk.NET.Input;

namespace AcDream.UI.Abstractions.Tests.Input;

public sealed class SpellBarActionsTests
{
    [Fact]
    public void Row0_IsTheRetailSpellSlots()
    {
        Assert.Equal(InputAction.UseSpellSlot_1, SpellBarActions.ForSlot(0, 0));
        Assert.Equal(InputAction.UseSpellSlot_10, SpellBarActions.ForSlot(0, 9));
        Assert.Equal(InputAction.UseSpellSlot_12, SpellBarActions.ForSlot(0, 11));
    }

    [Fact]
    public void LaterRows_AreTheNewActions_AndParseBack()
    {
        Assert.Equal(InputAction.UseSpellRow2Slot1, SpellBarActions.ForSlot(1, 0));
        Assert.Equal(InputAction.UseSpellRow10Slot12, SpellBarActions.ForSlot(9, 11));
        for (int row = 0; row < SpellBarActions.Rows; row++)
            for (int slot = 0; slot < SpellBarActions.SlotsPerRow; slot++)
            {
                Assert.True(SpellBarActions.TryParse(SpellBarActions.ForSlot(row, slot), out int r, out int s));
                Assert.Equal((row, slot), (r, s));
            }
        Assert.False(SpellBarActions.TryParse(InputAction.CastItemSpell, out _, out _));
    }

    [Fact]
    public void Defaults_Row2Shift_Row3Ctrl_InMagicScope()
    {
        KeyBindings b = KeyBindings.RetailDefaults();
        Binding row2 = Assert.Single(b.ForAction(InputAction.UseSpellRow2Slot4));
        Assert.Equal(new KeyChord(Key.Number4, ModifierMask.Shift), row2.Chord);
        Assert.Equal(InputScope.MagicCombat, row2.Scope);
        Binding row3 = Assert.Single(b.ForAction(InputAction.UseSpellRow3Slot9));
        Assert.Equal(new KeyChord(Key.Number9, ModifierMask.Ctrl), row3.Chord);
        Assert.Empty(b.ForAction(InputAction.UseSpellRow3Slot10));
        Assert.Empty(b.ForAction(InputAction.UseSpellRow4Slot1));
        Assert.Empty(b.ForAction(InputAction.CastItemSpell));
        Assert.Contains(b.ForAction(InputAction.CreateShortcut),
            x => x.Chord == new KeyChord(Key.Number0, ModifierMask.Ctrl));
    }

    [Fact]
    public void Catalog_ListsEveryClientAction_WithUniqueIds()
    {
        IReadOnlyList<ClientActionEntry> e = ClientActionCatalog.Entries;
        Assert.Equal(108 + 1, e.Count);
        Assert.All(e, x => Assert.Equal(0x10000005u, x.InputMapId));
        Assert.All(e, x => Assert.True(x.ActionId >= 0xC0000000u));
        Assert.Equal(e.Count, e.Select(x => x.ActionId).Distinct().Count());
        Assert.Contains(e, x => x.Action == InputAction.UseSpellRow2Slot4 && x.Label == "Spell bar row 2, slot 4");
        Assert.Contains(e, x => x.Action == InputAction.CastItemSpell && x.Label == "Cast item spell");
        Assert.All(e, x => Assert.Equal("Spell bar", x.Group));
    }

    [Fact]
    public void Rebinding_ANewAction_SurvivesSaveAndLoad()
    {
        string path = Path.Combine(Path.GetTempPath(), $"kb-{Guid.NewGuid():N}.json");
        try
        {
            KeyBindings b = KeyBindings.RetailDefaults();
            b.Add(new Binding(new KeyChord(Key.F7, ModifierMask.None), InputAction.CastItemSpell,
                Scope: InputScope.MagicCombat));
            b.SaveToFile(path);
            KeyBindings loaded = KeyBindings.LoadOrDefault(path);
            Binding cast = Assert.Single(loaded.ForAction(InputAction.CastItemSpell));
            Assert.Equal(Key.F7, cast.Chord.Key);
            Assert.Equal(InputScope.MagicCombat, cast.Scope);
        }
        finally { File.Delete(path); }
    }
}
