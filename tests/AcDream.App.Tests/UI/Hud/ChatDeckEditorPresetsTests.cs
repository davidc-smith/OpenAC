using AcDream.App.UI.Hud;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckEditorPresetsTests
{
    [Fact]
    public void Presets_GroupedAndToggled()
    {
        var (m, b) = ChatDeckEditorTabsTests.Make();
        Assert.Equal(ChatDeckPresets.GroupOrder, b.PresetGroups.Select(g => g.Name));
        ChatDeckEditorBinding.PresetRow row = b.PresetGroups[0].Presets[0];
        Assert.False(row.On);
        row.SetOn(true);
        Assert.True(row.On);
        Assert.Contains(m.Settings.Rules, r => r.Kind == ChatDeckMatchKind.Preset && r.PresetId == row.Id && r.Enabled);
        row.SetOn(false);
        Assert.False(row.On);
    }

    [Fact]
    public void Panes_SwitchAndClose()
    {
        var (_, b) = ChatDeckEditorTabsTests.Make();
        b.NewRule();
        b.ShowPresets();
        Assert.True(b.PresetsOpen);
        Assert.False(b.FormOpen);
        b.ShowTest();
        Assert.True(b.TestOpen);
        Assert.False(b.PresetsOpen);
        b.ClosePane();
        Assert.False(b.TestOpen);
    }

    [Fact]
    public void TestBox_TypedLine_UsesAppliedRules()
    {
        var (m, b) = ChatDeckEditorTabsTests.Make();
        m.Edit(s => ChatDeckSettingsEdits.AddRule(s,
            new ChatDeckRule { Id = "", Kind = ChatDeckMatchKind.Contains, Pattern = "spam", Action = ChatDeckRuleAction.Hide }, out _));
        b.ShowTest();
        b.SetTestChannel("General");
        b.SetTestSender("Bob");
        b.SetTestText("buy spam");
        Assert.Equal("Hidden by Rule 2 (Text contains \"spam\")", b.TestResult[0].Text);
        Assert.Equal(0xFF000000u | ChatDeckEditorBinding.ProblemColour, b.TestResult[0].Colour);
        b.SetTestText("hello");
        Assert.Equal("Goes to: All, Social", b.TestResult[0].Text);
    }

    [Fact]
    public void TestBox_RealLine_KeepsFactsUntilEdited()
    {
        var log = new ChatLog();
        var m = new ChatDeckModel(log, ChatDeckDefaults.Create(), "Davian");
        var b = new ChatDeckEditorBinding(() => m);
        log.OnTellReceived("Bob", "psst", 0x50000002u, 0u);
        m.Tick();
        ChatDeckLineFacts facts = m.FindTab("all")!.Snapshot()[^1].Facts;
        b.Open(new ChatDeckEditorRequest { Page = ChatDeckEditorPage.Rules, TestLine = facts });
        Assert.True(b.TestOpen);
        Assert.Equal("Tells received", b.TestChannel);
        Assert.Equal("Bob", b.TestSender);
        Assert.True(b.TestFromTranscript);
        Assert.Contains(b.TestResult, r => r.Text == "Alert: incoming tell");
        b.SetTestText("changed");
        Assert.False(b.TestFromTranscript);
    }
}
