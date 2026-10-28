using AcDream.App.UI.Hud;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckEditorRulesTests
{
    private static ChatDeckRule R(string pattern, ChatDeckMatchKind kind = ChatDeckMatchKind.Contains) =>
        new() { Id = "", Kind = kind, Pattern = pattern, Action = ChatDeckRuleAction.Hide };

    private static (ChatDeckModel, ChatDeckEditorBinding) WithRules(params ChatDeckRule[] rules)
    {
        var (m, b) = ChatDeckEditorTabsTests.Make();
        foreach (ChatDeckRule r in rules) m.Edit(s => ChatDeckSettingsEdits.AddRule(s, r, out _));
        return (m, b);
    }

    [Fact]
    public void Rows_DescribeEachRule_ProblemsInRed()
    {
        var (_, b) = WithRules(R("loot"), R("(", ChatDeckMatchKind.Regex));
        Assert.Equal(["My name", "Text contains \"loot\"", "Text matches /(/"], b.RuleWhen);
        Assert.Equal([true, true, true], b.RuleOn);
        Assert.Equal("Hide", b.RuleDo[1]);
        Assert.Equal("Any channel", b.RuleFrom[1]);
        Assert.Equal(ChatDeckEditorBinding.ProblemColour, b.RuleColours[2]);
        Assert.NotEqual(ChatDeckEditorBinding.ProblemColour, b.RuleColours[1]);
        b.SelectRuleRow(2);
        Assert.StartsWith("Invalid: ", b.SelectedRuleStatus);
        Assert.Same(b.RuleWhen, b.RuleWhen);
    }

    [Fact]
    public void Toggle_FlipsEnabled()
    {
        var (m, b) = WithRules(R("loot"));
        b.ToggleRule(1);
        Assert.False(m.Settings.Rules[1].Enabled);
        b.ToggleRule(1);
        Assert.True(m.Settings.Rules[1].Enabled);
    }

    [Fact]
    public void RowMenu_DuplicateDeleteMove()
    {
        var (m, b) = WithRules(R("a"), R("b"));
        Assert.Equal(["Edit", "Duplicate", "Delete", "-", "Move up", "Move down"], b.RuleMenu);
        b.RuleMenuOpen(1);
        b.RuleMenuSelect(1);   // Duplicate
        Assert.Equal(["my-name", "rule-1", "rule-3", "rule-2"], m.Settings.Rules.Select(r => r.Id));
        b.RuleMenuOpen(3);
        b.RuleMenuSelect(4);   // Move up
        Assert.Equal("rule-2", m.Settings.Rules[2].Id);
        Assert.Equal(2, b.SelectedRuleIndex);
        b.RuleMenuOpen(2);
        b.RuleMenuSelect(2);   // Delete
        Assert.DoesNotContain(m.Settings.Rules, r => r.Id == "rule-2");
    }

    [Fact]
    public void Buttons_MoveTheSelection()
    {
        var (m, b) = WithRules(R("a"), R("b"));
        b.SelectRuleRow(2);
        b.MoveRuleUp();
        Assert.Equal("rule-2", m.Settings.Rules[1].Id);
        Assert.Equal(1, b.SelectedRuleIndex);
        b.MoveRuleDown();
        Assert.Equal(2, b.SelectedRuleIndex);
    }
}
