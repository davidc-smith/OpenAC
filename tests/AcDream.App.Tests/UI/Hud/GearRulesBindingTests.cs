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

    // ── the Filters tab ──

    [Fact]
    public void TheTabs_SwitchBetweenRulesAndFilters()
    {
        var (b, _) = Make();
        Assert.True(b.OnRulesPage);
        b.ShowFiltersPage();
        Assert.True(b.OnFiltersPage);
        Assert.False(b.OnRulesPage);
        b.ShowRulesPage();
        Assert.True(b.OnRulesPage);
    }

    [Fact]
    public void TheList_ShowsEachFiltersNameAndSummary()
    {
        var (b, s) = Make(filters: false);
        var query = new GearQuery(
            [GearChip.OfType(GearTypeGroup.Salvage), GearChip.Range(GearField.Workmanship, 7, null), GearChip.InPack(Bag)],
            "steel", GearSort.DefaultFor(GearSortField.Value));
        s.Update(x => x.WithSaved(new SavedFilter("Salvage", query)));
        Assert.Equal(new[] { "Salvage" }, b.SavedNames);
        Assert.Equal(new[] { "Salvage, Work >= 7, Pack 2, \"steel\", sorted by Value" }, b.SavedSummaries);
    }

    [Fact]
    public void Summary_SaysWhenTheSortIsReversed_AndLeavesOutPackOrder()
    {
        GearQuery worn = GearQuery.Empty.With(GearChip.WornOnly);
        Assert.Equal("Worn", GearRules.Summary(worn, Packs));
        Assert.Equal("Worn, sorted by Name (reversed)", GearRules.Summary(worn with { Sort = new GearSort(GearSortField.Name, true) }, Packs));
        Assert.Equal("(missing)", GearRules.Summary(GearQuery.Empty.With(GearChip.InPack(0xDEAD)), Packs));
    }

    [Fact]
    public void TypingInTheQueryField_ListsTheDecksSuggestions()
    {
        var (b, _) = Make();
        Assert.Empty(b.SuggestionTexts);
        b.SetDraftText("salv");
        Assert.Equal(GearChipSuggester.Suggest("salv").Select(GearChipText.Suggestion), b.SuggestionTexts);
        Assert.Contains("Type: Salvage", b.SuggestionTexts);
        Assert.Equal("Name contains \"salv\"", b.SuggestionTexts[^1]);
        Assert.Equal(-1, b.NoSuggestionSelected);
    }

    [Fact]
    public void PickingASuggestion_AddsItsChipAndClearsTheText()
    {
        var (b, _) = Make();
        b.SetDraftText("salv");
        int salvage = b.SuggestionTexts.ToList().IndexOf("Type: Salvage");
        b.PickSuggestion(salvage);
        Assert.Equal(new[] { "Salvage" }, b.DraftChipLabels);
        Assert.Equal("", b.DraftText);
        Assert.Empty(b.SuggestionTexts);
        b.SetDraftText("work 7");
        b.SubmitDraftText("work 7");      // Enter takes the first suggestion
        Assert.Equal(new[] { "Salvage", "Work >= 7" }, b.DraftChipLabels);
        b.SetDraftText("salv");
        b.PickSuggestion(salvage);        // a chip already there is not added twice
        Assert.Equal(2, b.DraftChipLabels.Count);
    }

    [Fact]
    public void RemoveChip_TakesTheSelectedChipOut()
    {
        var (b, _) = Make();
        b.SetDraftText("worn");
        b.PickSuggestion(0);
        b.SetDraftText("locked");
        b.PickSuggestion(0);
        Assert.Equal(new[] { "Worn", "Locked" }, b.DraftChipLabels);
        Assert.False(b.HasSelectedChip);
        b.SelectChipRow(0);
        b.RemoveChip();
        Assert.Equal(new[] { "Locked" }, b.DraftChipLabels);
        Assert.False(b.HasSelectedChip);
        b.SelectChipRow(0);
        b.ChipMenuSelect(0);              // the list's right-click "Remove chip"
        Assert.Empty(b.DraftChipLabels);
    }

    [Fact]
    public void Save_IsOffWithoutANameOrAQuery()
    {
        var (b, _) = Make(filters: false);
        Assert.False(b.CanSaveFilter);
        b.SetDraftName("Gems");
        Assert.False(b.CanSaveFilter);    // no query
        b.SetDraftText("   ");
        Assert.False(b.CanSaveFilter);    // blank text is no query
        b.SetDraftText("gem");
        Assert.True(b.CanSaveFilter);     // free text alone is a query
        b.SetDraftName("  ");
        Assert.False(b.CanSaveFilter);
    }

    [Fact]
    public void Save_AddsAFilter_WithTheLeftoverTextAsItsNameContainsText()
    {
        var (b, s) = Make(filters: false);
        b.SetDraftName(" Steel ");
        b.SetDraftText("steel");
        b.PickSuggestion(0);              // Material: Steel
        b.SetDraftText(" bar ");
        b.SaveFilter();
        SavedFilter saved = Assert.Single(s.Settings.SavedFilters);
        Assert.Equal("Steel", saved.Name);
        Assert.Equal(new[] { GearChip.OfMaterial(saved.Query.Chips[0].Id) }, saved.Query.Chips);
        Assert.Equal(GearField.Material, saved.Query.Chips[0].Field);
        Assert.Equal("bar", saved.Query.Text);
        Assert.Equal(0, b.SelectedFilterIndex);
        // the rules see it at once: their pickers read the same settings
        Assert.True(b.HasFilters);
        Assert.Equal(new[] { "Steel" }, b.FilterNames);
    }

    [Fact]
    public void Save_ReplacesASameNamedFilter_IgnoringCase()
    {
        var (b, s) = Make();
        b.SetDraftName("salvage");
        b.SetDraftText("worn");
        b.PickSuggestion(0);
        b.SaveFilter();
        Assert.Equal(new[] { "salvage", "Gems" }, s.Settings.SavedFilters.Select(f => f.Name));
        Assert.Equal(GearQuery.Empty.With(GearChip.WornOnly), s.Settings.SavedFilters[0].Query);
        Assert.Equal(0, b.SelectedFilterIndex);
    }

    [Fact]
    public void Save_IsOffForANewNameOnceTheListIsFull()
    {
        var (b, s) = Make(filters: false);
        for (int i = 0; i < SavedFilter.MaxCount; i++) s.Update(x => x.WithSaved(new SavedFilter($"F{i}", Named("x"))));
        b.SetDraftName("Another");
        b.SetDraftText("gem");
        Assert.False(b.CanSaveFilter);
        Assert.NotEqual("", b.SaveHint);
        b.SetDraftName("f3");             // replacing one still works
        Assert.True(b.CanSaveFilter);
        Assert.Equal("", b.SaveHint);
    }

    [Fact]
    public void SelectingAFilter_LoadsItIntoTheEditor_AndSaveKeepsItsSort()
    {
        var (b, s) = Make(filters: false);
        var query = new GearQuery([GearChip.WornOnly], "ring", new GearSort(GearSortField.Value, true));
        s.Update(x => x.WithSaved(new SavedFilter("Rings", query)));
        b.SelectFilterRow(0);
        Assert.Equal("Rings", b.DraftName);
        Assert.Equal("ring", b.DraftText);
        Assert.Equal(new[] { "Worn" }, b.DraftChipLabels);
        Assert.StartsWith("Edit filter", b.EditorTitle);
        b.SetDraftText("rin");
        b.SaveFilter();
        Assert.Equal(query with { Text = "rin" }, s.Settings.SavedFilters[0].Query);
        b.NewFilter();
        Assert.Equal("", b.DraftName);
        Assert.Empty(b.DraftChipLabels);
        Assert.Equal("New filter", b.EditorTitle);
        Assert.False(b.HasSelectedFilter);
        b.SelectFilterRow(0);
        b.SetDraftName("Changed");
        b.EditFilter();                   // Edit loads the selected filter again
        Assert.Equal("Rings", b.DraftName);
    }

    [Fact]
    public void Save_RenamesALoadedFilterInPlace_AndItsRulesFollow()
    {
        var (b, s) = Make();
        b.AddRule();                      // Salvage -> Main
        b.SelectFilterRow(0);
        b.SetDraftName("Scrap");
        Assert.Equal("Edit filter \"Salvage\" (Save renames it)", b.EditorTitle);
        Assert.True(b.CanSaveFilter);
        b.SaveFilter();
        Assert.Equal(new[] { "Scrap", "Gems" }, s.Settings.SavedFilters.Select(f => f.Name));
        Assert.Equal(Named("salvage"), s.Settings.SavedFilters[0].Query);
        Assert.Equal(new[] { new GearPackRule("Scrap", Main, false) }, s.Settings.PackRules);
        Assert.Equal(new[] { "Scrap" }, b.RuleFilter);
        Assert.Equal(0, b.SelectedFilterIndex);
        b.SetDraftName("Junk");           // the renamed filter is still the loaded one
        b.SaveFilter();
        Assert.Equal(new[] { "Junk", "Gems" }, s.Settings.SavedFilters.Select(f => f.Name));
    }

    [Fact]
    public void Save_RefusesARenameOntoAnotherFiltersName()
    {
        var (b, s) = Make();
        b.SelectFilterRow(0);
        b.SetDraftName("gems");
        Assert.False(b.CanSaveFilter);
        Assert.False(b.CanSaveAsNew);
        Assert.Equal("A filter named \"gems\" already exists.", b.SaveHint);
        b.SaveFilter();
        Assert.Equal(new[] { "Salvage", "Gems" }, s.Settings.SavedFilters.Select(f => f.Name));
        b.SetDraftName("SALVAGE");        // a change of case only is a plain save
        Assert.True(b.CanSaveFilter);
        b.SaveFilter();
        Assert.Equal(new[] { "SALVAGE", "Gems" }, s.Settings.SavedFilters.Select(f => f.Name));
    }

    [Fact]
    public void SaveAsNew_AddsACopyUnderTheNewName_AndKeepsTheOriginal()
    {
        var (b, s) = Make();
        b.AddRule();
        Assert.False(b.CanSaveAsNew);     // nothing loaded
        b.SelectFilterRow(0);
        Assert.False(b.CanSaveAsNew);     // name unchanged
        b.SetDraftName("Salvage 2");
        Assert.True(b.CanSaveAsNew);
        b.SaveFilterAsNew();
        Assert.Equal(new[] { "Salvage", "Gems", "Salvage 2" }, s.Settings.SavedFilters.Select(f => f.Name));
        Assert.Equal(new[] { new GearPackRule("Salvage", Main, false) }, s.Settings.PackRules);
        Assert.Equal(2, b.SelectedFilterIndex);
        Assert.Equal("Salvage 2", b.DraftName);
    }

    [Fact]
    public void Delete_RemovesTheFilter_AndARuleNamingItShowsMissing()
    {
        var (b, s) = Make();
        b.AddRule();                      // Salvage -> Main
        b.SelectFilterRow(0);
        b.DeleteFilter();
        Assert.Equal(new[] { "Gems" }, s.Settings.SavedFilters.Select(f => f.Name));
        Assert.Equal(new[] { "(missing)" }, b.RuleFilter);
        Assert.False(b.HasSelectedFilter);
        Assert.Equal("", b.DraftName);    // the editor held the deleted filter
        b.FilterMenuOpen(0);
        b.FilterMenuSelect(1);            // the list's right-click Delete
        Assert.Empty(s.Settings.SavedFilters);
        Assert.True(b.NoFilters);
    }

    [Fact]
    public void ANewSession_ClearsTheEditor()
    {
        var first = new GearDeckSession(null, () => "Toon", () => true);
        GearDeckSession current = first;
        var b = new GearRulesBinding(() => current, () => Packs);
        b.SetDraftName("Half");
        b.SetDraftText("ge");
        current = new GearDeckSession(null, () => "Other", () => true);
        Assert.Equal("", b.DraftName);
        Assert.Equal("", b.DraftText);
    }

    [Fact]
    public void Markup_BuildsWithBothTabs_AndTheFieldsFeedTheEditor()
    {
        var (b, _) = Make();
        b.Close = () => { };
        UiElement window = MarkupDocument.Build(GearRulesBinding.Markup, b, _ => (0u, 0, 0));
        List<UiField> fields = [];
        void Walk(UiElement e)
        {
            if (e is UiField f) fields.Add(f);
            foreach (UiElement c in e.Children) Walk(c);
        }
        Walk(window);
        Assert.Equal(2, fields.Count);    // Name and Query
        fields[0].SetText("Mine");
        fields[1].SetText("gem");
        Assert.Equal("Mine", b.DraftName);
        Assert.Contains("Type: Gems", b.SuggestionTexts);
    }

    [Fact]
    public void TheSelectedFilter_FollowsItsName_WhenTheDeckDeletesAnother()
    {
        var (b, s) = Make();
        b.SelectFilterRow(1);             // Gems
        s.Update(x => x.WithoutSaved("Salvage"));   // the deck's right-click delete
        Assert.Equal(0, b.SelectedFilterIndex);
        Assert.Equal(new[] { "Gems" }, b.SavedNames);
    }
}
