using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.App.Tests.UI.Hud;

public sealed class GearRulesBindingTests
{
    private const uint Main = 0x50000001, Bag = 0xB1;
    private static readonly GearPack[] Packs = [new(Main, 102, true), new(Bag, 24, false)];

    private static GearQuery Named(string text) => GearQuery.Empty.With(GearChip.NameContains(text));

    private static (GearRulesBinding B, GearDeckSession S) Make(bool filters = true)
    {
        var session = new GearDeckSession(null, () => "Toon", () => true);
        if (filters)
            session.Update(s => s.WithSaved(new SavedFilter("Salvage", Named("salvage"))).WithSaved(new SavedFilter("Gems", Named("gem"))));
        return (new GearRulesBinding(() => session, () => Packs), session);
    }

    [Fact]
    public void Add_MakesTheFirstFilterToTheMainPack_AndSelectsIt()
    {
        var (b, s) = Make();
        Assert.True(b.HasFilters);
        Assert.False(b.HasSelectedRule);
        b.AddRule();
        Assert.Equal(new[] { new GearPackRule("Salvage", Main, false) }, s.Settings.PackRules);
        Assert.Equal(0, b.SelectedRuleIndex);
        Assert.Equal(new[] { "Salvage" }, b.RuleFilter);
        Assert.Equal(new[] { "Main" }, b.RulePack);
        Assert.Equal(new[] { true }, b.RuleOn);
        Assert.Equal(new[] { false }, b.RuleArrival);
    }

    [Fact]
    public void ThePickers_SetTheSelectedRulesFilterAndPack()
    {
        var (b, s) = Make();
        b.AddRule();
        Assert.Equal(new[] { "Main", "Pack 2" }, b.PackNames);
        Assert.Equal(new[] { "Salvage", "Gems" }, b.FilterNames);
        b.SelectPack(b.PackNames[1]);
        Assert.Equal(Bag, s.Settings.PackRules[0].PackId);
        Assert.Equal("Pack 2", b.SelectedPack);
        b.SelectFilter("Gems");
        Assert.Equal("Gems", s.Settings.PackRules[0].Filter);
        Assert.Equal("Gems", b.SelectedFilter);
    }

    [Fact]
    public void UpAndDown_Reorder_AndFollowTheSelection()
    {
        var (b, s) = Make();
        b.AddRule();
        b.AddRule();
        b.SelectFilter("Gems");            // the second rule is selected
        b.MoveUp();
        Assert.Equal(new[] { "Gems", "Salvage" }, s.Settings.PackRules.Select(r => r.Filter));
        Assert.Equal(0, b.SelectedRuleIndex);
        b.MoveDown();
        Assert.Equal(new[] { "Salvage", "Gems" }, s.Settings.PackRules.Select(r => r.Filter));
        Assert.Equal(1, b.SelectedRuleIndex);
    }

    [Fact]
    public void Delete_RemovesTheSelectedRule()
    {
        var (b, s) = Make();
        b.AddRule();
        b.AddRule();
        b.SelectFilter("Gems");
        b.SelectRuleRow(0);
        b.DeleteRule();
        Assert.Equal(new[] { "Gems" }, s.Settings.PackRules.Select(r => r.Filter));
        Assert.False(b.HasSelectedRule);
    }

    [Fact]
    public void TheCheckboxes_UpdateTheSettings()
    {
        var (b, s) = Make();
        b.AddRule();
        b.ToggleArrival(0);
        Assert.True(s.Settings.PackRules[0].OnArrival);
        b.ToggleRule(0);
        Assert.False(s.Settings.PackRules[0].Enabled);
        Assert.False(b.CombineOnArrival);
        b.SetCombineOnArrival(true);
        Assert.True(s.Settings.CombineOnArrival);
        Assert.True(b.CombineOnArrival);
    }

    [Fact]
    public void ARuleWhoseFilterOrPackIsGone_ShowsMissing()
    {
        var (b, s) = Make();
        s.Update(x => x.WithRules([new GearPackRule("Old", 0xDEAD, false)]));
        Assert.Equal(new[] { "(missing)" }, b.RuleFilter);
        Assert.Equal(new[] { "(missing)" }, b.RulePack);
    }

    [Fact]
    public void WithoutSavedFilters_AddIsOff()
    {
        var (b, s) = Make(filters: false);
        Assert.False(b.HasFilters);
        Assert.True(b.NoFilters);
        b.AddRule();
        Assert.Empty(s.Settings.PackRules);
    }

    [Fact]
    public void Resolve_PairsEachRuleWithItsFiltersQuery_AndAMissingOneWithTheEmptyQuery()
    {
        var (_, s) = Make();
        s.Update(x => x.WithRules([new GearPackRule("gems", Bag, true), new GearPackRule("Old", Bag, false)]));
        var resolved = GearRules.Resolve(s.Settings);
        Assert.Equal(Named("gem"), resolved[0].Query);
        Assert.Same(GearQuery.Empty, resolved[1].Query);
        Assert.Single(GearRules.Resolve(s.Settings, r => r.OnArrival));
    }

    [Fact]
    public void Markup_Builds()
    {
        var (b, _) = Make();
        b.Close = () => { };
        UiElement window = MarkupDocument.Build(GearRulesBinding.Markup, b, _ => (0u, 0, 0));
        Assert.NotNull(window);
    }
}
