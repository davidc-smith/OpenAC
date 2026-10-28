using AcDream.App.UI.Hud;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckEditorRuleFormTests
{
    private static (ChatLog Log, ChatDeckModel Model, ChatDeckEditorBinding Binding) Make()
    {
        var log = new ChatLog();
        var model = new ChatDeckModel(log, ChatDeckDefaults.Create(), "Davian");
        return (log, model, new ChatDeckEditorBinding(() => model));
    }

    private static void System(ChatLog log, ChatDeckModel model, string text)
    {
        log.OnSystemMessage(text, (uint)RetailLogTextType.System);
        model.Tick();
    }

    [Fact]
    public void NewRule_AddsOnlyOnAdd()
    {
        var (_, m, b) = Make();
        int before = m.Settings.Rules.Count;
        b.NewRule();
        Assert.True(b.FormOpen);
        Assert.Equal("Add", b.SaveText);
        Assert.False(b.CanSave);
        b.SetPattern("loot");
        b.SetDraftAction(0);
        Assert.Equal(before, m.Settings.Rules.Count);
        Assert.True(b.CanSave);
        b.SaveRule();
        Assert.Equal(before + 1, m.Settings.Rules.Count);
        Assert.Equal("loot", m.Settings.Rules[^1].Pattern);
        Assert.False(b.FormOpen);
        Assert.Equal(m.Settings.Rules.Count - 1, b.SelectedRuleIndex);
    }

    [Fact]
    public void EditRule_SavesInPlace_Cancel_ChangesNothing()
    {
        var (_, m, b) = Make();
        m.Edit(s => ChatDeckSettingsEdits.AddRule(s, new ChatDeckRule { Id = "", Kind = ChatDeckMatchKind.Contains, Pattern = "a" }, out _));
        m.Edit(s => s with { Rules = s.Rules.Select(r => r.Id == "rule-1" ? r with { Faulted = true } : r).ToList() });
        b.SelectRuleRow(1);
        b.EditRule();
        Assert.Equal("Save", b.SaveText);
        b.SetPattern("b");
        b.CancelRule();
        Assert.Equal("a", m.Settings.Rules[1].Pattern);

        b.EditRule();
        b.SetPattern("b");
        b.SaveRule();
        Assert.Equal(("rule-1", "b", false), (m.Settings.Rules[1].Id, m.Settings.Rules[1].Pattern, m.Settings.Rules[1].Faulted));
    }

    [Fact]
    public void MoveNeedsATarget_ControlsFollowTheAction()
    {
        var (_, m, b) = Make();
        b.NewRule();
        b.SetPattern("x");
        b.SetDraftAction((int)ChatDeckRuleAction.MoveTo);
        Assert.True(b.ShowsTarget);
        Assert.False(b.ShowsColour);
        Assert.DoesNotContain("All", b.TargetNames);
        Assert.False(b.CanSave);
        b.SetTarget("Combat");
        Assert.True(b.CanSave);
        b.SetDraftAction((int)ChatDeckRuleAction.ColourLine);
        Assert.True(b.ShowsColour);
        Assert.False(b.ShowsTarget);
        b.SetDraftKind(2);   // My name
        Assert.False(b.PatternEditable);
    }

    [Fact]
    public void FormChannels_EditTheDraft()
    {
        var (_, m, b) = Make();
        b.NewRule();
        b.DraftChannelGroups.Single(g => g.Name == "Combat").SetAll(true);
        b.SetPattern("x");
        b.SaveRule();
        Assert.True(ChatDeckChannels.Group("Combat").All(m.Settings.Rules[^1].Channels.Contains));
    }

    [Fact]
    public void Preview_CountsHistory_AndWarns()
    {
        var (log, m, b) = Make();
        System(log, m, "You receive 5 Pyreals.");
        System(log, m, "Something else.");
        b.NewRule();
        b.SetPattern("Pyreals");
        Assert.Equal("Matches 1 of the last 2 lines.", b.PreviewText);
        Assert.Single(b.PreviewSamples);
        b.SetDraftKind(1);   // Regex
        b.SetPattern("(");
        Assert.StartsWith("Invalid: ", b.PreviewText);
        b.SetPattern("z*");
        Assert.Contains("matches every line", b.PreviewText);
    }

    [Fact]
    public void FromLine_GeneraliseRewritesTheDraft()
    {
        var (log, m, b) = Make();
        System(log, m, "You receive 5 Pyreals.");
        ChatDeckLineFacts facts = m.FindTab("all")!.Snapshot()[0].Facts;
        b.Open(new ChatDeckEditorRequest
        {
            Page = ChatDeckEditorPage.Rules,
            NewRule = ChatDeckRuleDraft.FromLine(facts, ChatDeckRuleAction.Hide),
            FromLine = facts,
        });
        Assert.True(b.OnRulesPage);
        Assert.True(b.FromLine);
        Assert.False(b.CanAnySender);
        Assert.True(b.CanAnyNumber);
        b.SetAnyNumber(true);
        Assert.Equal("You receive [any number] Pyreals.", b.PatternPreview);
        Assert.Equal(1, b.DraftKind);   // Regex
        System(log, m, "You receive 12 Pyreals.");
        Assert.Equal("Matches 2 of the last 2 lines.", b.PreviewText);
    }

    [Fact]
    public void ModelSwap_ClosesTheForm_WithoutThrowing()
    {
        var log = new ChatLog();
        var m1 = new ChatDeckModel(log, ChatDeckDefaults.Create(), "Davian");
        var m2 = new ChatDeckModel(log, ChatDeckDefaults.Create(), "Other");
        var current = m1;
        var b = new ChatDeckEditorBinding(() => current);
        b.NewRule();
        b.SetPattern("x");
        Assert.True(b.CanSave);
        current = m2;
        Assert.Equal("", b.PreviewText);
        Assert.False(b.CanSave);
        Assert.False(b.FormOpen);
    }

    [Fact]
    public void AtRuleLimit_NewRuleCannotSave_FormStaysOpen()
    {
        var (_, m, b) = Make();
        while (m.Settings.Rules.Count < ChatDeckLimits.MaxRules)
            m.Edit(s => ChatDeckSettingsEdits.AddRule(s, new ChatDeckRule { Id = "", Kind = ChatDeckMatchKind.Contains, Pattern = "a" }, out _));
        b.NewRule();
        b.SetPattern("x");
        Assert.False(b.CanSave);
        b.SaveRule();
        Assert.True(b.FormOpen);
        Assert.Equal(ChatDeckLimits.MaxRules, m.Settings.Rules.Count);
    }
}
