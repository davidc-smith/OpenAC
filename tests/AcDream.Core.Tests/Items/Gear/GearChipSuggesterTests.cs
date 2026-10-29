using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.Core.Tests.Items.Gear;

public sealed class GearChipSuggesterTests
{
    [Fact]
    public void Blank_GivesNothing()
    {
        Assert.Empty(GearChipSuggester.Suggest(""));
        Assert.Empty(GearChipSuggester.Suggest("   "));
    }

    [Fact]
    public void Steel_SuggestsMaterialFirst_NameLast()
    {
        var s = GearChipSuggester.Suggest("steel");
        Assert.Equal(GearChip.OfMaterial(64), s[0]);
        Assert.Equal(GearChip.NameContains("steel"), s[^1]);
    }

    [Fact]
    public void Salv_SuggestsSalvageGroup() =>
        Assert.Equal(GearChip.OfType(GearTypeGroup.Salvage), GearChipSuggester.Suggest("salv")[0]);

    [Theory]
    [InlineData("work 7", 7.0, null)]
    [InlineData("ws>=7.5", 7.5, null)]
    [InlineData("work > 7", 7.0, null)]
    [InlineData("value < 500", null, 500.0)]
    [InlineData("stack = 20", 20.0, 20.0)]
    public void Ranges(string typed, double? min, double? max)
    {
        GearChip c = GearChipSuggester.Suggest(typed)[0];
        Assert.True(c.IsRange);
        Assert.Equal((min, max), (c.Min, c.Max));
    }

    [Fact]
    public void RangeField_ByWord()
    {
        Assert.Equal(GearField.Workmanship, GearChipSuggester.Suggest("work 7")[0].Field);
        Assert.Equal(GearField.Value, GearChipSuggester.Suggest("val 7")[0].Field);
        Assert.Equal(GearField.Burden, GearChipSuggester.Suggest("bu 7")[0].Field);
        Assert.Equal(GearField.Stack, GearChipSuggester.Suggest("st 7")[0].Field);
    }

    [Fact]
    public void TwoLetters_OnlyWornAndName()
    {
        var s = GearChipSuggester.Suggest("wo");
        Assert.Equal([GearChip.WornOnly, GearChip.NameContains("wo")], s);
    }

    [Fact]
    public void AtMostFive_NameAlwaysLast()
    {
        // "sto" offers more than four chips (Mana stones by its alias, then the stone materials), so the cap bites.
        var s = GearChipSuggester.Suggest("sto");
        Assert.Equal(GearChipSuggester.MaxSuggestions, s.Count);
        Assert.Equal(GearChip.OfType(GearTypeGroup.ManaStones), s[0]);
        Assert.Equal(GearChip.NameContains("sto"), s[^1]);
        Assert.Equal(s.Count, s.Distinct().Count());
    }

    [Fact]
    public void Gem_SuggestsGroupThenMaterial()
    {
        var s = GearChipSuggester.Suggest("gem");
        Assert.Equal(GearChip.OfType(GearTypeGroup.Gems), s[0]);
        Assert.Contains(GearChip.OfMaterial(9), s);
    }
}
