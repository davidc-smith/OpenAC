using AcDream.App.UI.Hud;
using AcDream.UI.Abstractions.Input;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI.Hud;

public sealed class SpellDeckKeyLabelTests
{
    [Fact]
    public void Labels_FromTheFirstChord()
    {
        var b = new KeyBindings();
        b.Add(new(new KeyChord(Key.Number4, ModifierMask.None), InputAction.UseSpellSlot_4, Scope: InputScope.MagicCombat));
        b.Add(new(new KeyChord(Key.Number4, ModifierMask.Shift), InputAction.UseSpellRow2Slot4, Scope: InputScope.MagicCombat));
        b.Add(new(new KeyChord(Key.Minus, ModifierMask.Ctrl), InputAction.UseSpellRow3Slot11, Scope: InputScope.MagicCombat));
        b.Add(new(new KeyChord(Key.F5, ModifierMask.Alt), InputAction.CastItemSpell, Scope: InputScope.MagicCombat));
        Assert.Equal("4", SpellDeckKeyLabel.For(b, InputAction.UseSpellSlot_4));
        Assert.Equal("⇧4", SpellDeckKeyLabel.For(b, InputAction.UseSpellRow2Slot4));
        Assert.Equal("^-", SpellDeckKeyLabel.For(b, InputAction.UseSpellRow3Slot11));
        Assert.Equal("⌥F5", SpellDeckKeyLabel.For(b, InputAction.CastItemSpell));
        Assert.Equal("", SpellDeckKeyLabel.For(b, InputAction.UseSpellRow4Slot1));
    }
}
