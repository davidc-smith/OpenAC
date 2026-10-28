using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;

namespace AcDream.Core.Tests.Chat.Deck;

public sealed class ChatDeckModelTests
{
    private static (ChatLog Log, ChatDeckModel Model) Create(ChatDeckSettings? settings = null)
    {
        var log = new ChatLog();
        return (log, new ChatDeckModel(log, settings ?? ChatDeckDefaults.Create(), "Davian"));
    }

    private static void Say(ChatLog log, string text, string sender = "Bob") =>
        log.OnLocalSpeech(sender, text, 0x50000002u, isRanged: false, logTextType: 0x02u);

    [Fact]
    public void Entries_AppearOnTick_FromAnyThread()
    {
        var (log, model) = Create();
        var delivered = new List<ChatDeckDelivery>();
        model.LinesDelivered += batch => delivered.AddRange(batch);

        var thread = new Thread(() => Say(log, "hello"));
        thread.Start();
        thread.Join();
        Assert.Equal(0, model.FindTab("local")!.Count);

        model.Tick();
        Assert.Equal(1, model.FindTab("local")!.Count);
        Assert.Equal(1, model.FindTab("all")!.Count);
        Assert.Same(model.FindTab("local")![0], model.FindTab("all")![0]);
        ChatDeckDelivery d = Assert.Single(delivered);
        Assert.Equal(["all", "local"], d.TabIds);
    }

    [Fact]
    public void ReadState_FollowsSelectionAndView()
    {
        var (log, model) = Create();
        model.SelectTab("local");
        model.SetView(visible: true, atBottom: true);
        Say(log, "seen");
        model.Tick();
        Assert.Equal(0, model.FindTab("local")!.Unread);
        Assert.Equal(1, model.FindTab("all")!.Unread);

        model.SetView(visible: true, atBottom: false);
        Say(log, "scrolled up");
        model.Tick();
        Assert.Equal(1, model.FindTab("local")!.Unread);

        model.SetView(visible: true, atBottom: true);
        Assert.Equal(0, model.FindTab("local")!.Unread);

        model.SetView(visible: false, atBottom: true);
        Say(log, "hidden");
        model.Tick();
        Assert.Equal(1, model.FindTab("local")!.Unread);

        model.SelectTab("all");
        Assert.Equal(0, model.FindTab("all")!.Unread);
        model.MarkAllRead();
        Assert.All(model.Tabs, t => Assert.Equal(0, t.Unread));
    }

    [Fact]
    public void IncomingTell_SetsAlert()
    {
        var (log, model) = Create();
        log.OnTellReceived("Bob", "psst", 0x50000002u, 0x03u);
        model.Tick();
        Assert.True(model.FindTab("local")!.Alert);
    }

    [Fact]
    public void Clear_EmptiesTabsInOrder()
    {
        var (log, model) = Create();
        var events = new List<string>();
        model.LinesDelivered += batch => events.Add("lines:" + string.Join(",", batch.Select(d => d.Line.Entry.Text)));
        model.Cleared += () => events.Add("cleared");
        Say(log, "before");
        log.Clear();
        Say(log, "after");
        model.Tick();

        Assert.Equal(["lines:before", "cleared", "lines:after"], events);
        ChatDeckTabState local = model.FindTab("local")!;
        Assert.Equal(1, local.Count);
        Assert.Equal("after", local[0].Entry.Text);
    }

    [Fact]
    public void ApplySettings_KeepsLastTabInSyncWithSelection()
    {
        var (_, model) = Create();
        model.SelectTab("group");
        var s = model.Settings;
        model.ApplySettings(s with { Layout = s.Layout with { LastTabId = "social" } });
        Assert.Equal("group", model.SelectedTabId);
        Assert.Equal("group", model.Settings.Layout.LastTabId);
    }

    [Fact]
    public void ApplySettings_KeepsSurvivingHistory()
    {
        var (log, model) = Create();
        model.SelectTab("combat");
        Say(log, "hello");
        model.Tick();

        var s = model.Settings;
        var newTab = new ChatDeckTab { Id = "loot", Name = "Loot", Channels = new HashSet<ChatDeckChannel> { ChatDeckChannel.Say } };
        model.ApplySettings(s with { Tabs = [.. s.Tabs.Where(t => t.Id != "combat"), newTab] });

        Assert.Equal(1, model.FindTab("local")!.Count);
        Assert.Equal(0, model.FindTab("loot")!.Count);
        Assert.Null(model.FindTab("combat"));
        Assert.Equal("all", model.SelectedTabId);

        Say(log, "again");
        model.Tick();
        Assert.Equal(1, model.FindTab("loot")!.Count);
    }

    [Fact]
    public void TimedOutRule_IsFaultedAndReported()
    {
        var d = ChatDeckDefaults.Create();
        var slow = new ChatDeckRule { Id = "slow", Kind = ChatDeckMatchKind.Regex, Pattern = @"^(\w+\s?)*\1$", Action = ChatDeckRuleAction.Hide };
        var (log, model) = Create(d with { Rules = [.. d.Rules, slow] });
        var changes = new List<ChatDeckSettings>();
        model.SettingsChanged += changes.Add;

        Say(log, new string('a', 1990) + "!");
        Say(log, new string('a', 1990) + "?");
        Say(log, new string('a', 1990) + ".");
        Say(log, new string('a', 1990) + ",");
        model.Tick();

        Assert.True(model.Settings.Rules.Single(r => r.Id == "slow").Faulted);
        Assert.Single(changes);
        Assert.Equal(4, model.FindTab("local")!.Count);
        Assert.Equal(ChatDeckRuleStatus.Faulted, model.CompiledRules.Single(r => r.Rule.Id == "slow").Status);
        Assert.Empty(model.Test(ChatDeckMatcherTests.Facts(new string('a', 1990) + "#")).TimedOutRuleIds);
    }

    [Fact]
    public void TimedOutRule_IsNotFaultedBeforeThreeTimeoutsInARow()
    {
        // A CPU spike can push one match past the timeout; only a rule that keeps timing out is turned off.
        var d = ChatDeckDefaults.Create();
        var slow = new ChatDeckRule { Id = "slow", Kind = ChatDeckMatchKind.Regex, Pattern = @"^(\w+\s?)*\1$", Action = ChatDeckRuleAction.Hide };
        var (log, model) = Create(d with { Rules = [.. d.Rules, slow] });
        var changes = new List<ChatDeckSettings>();
        model.SettingsChanged += changes.Add;

        Say(log, new string('a', 1990) + "!");
        Say(log, new string('a', 1990) + "?");
        Say(log, "hi there");
        Say(log, new string('a', 1990) + ".");
        Say(log, new string('a', 1990) + ",");
        model.Tick();

        Assert.False(model.Settings.Rules.Single(r => r.Id == "slow").Faulted);
        Assert.Empty(changes);
        Assert.Equal(ChatDeckRuleStatus.Ok, model.CompiledRules.Single(r => r.Rule.Id == "slow").Status);
    }

    [Fact]
    public void CompiledRules_ReportInvalidPatterns()
    {
        var d = ChatDeckDefaults.Create();
        var (_, model) = Create();
        model.ApplySettings(d with { Rules = [.. d.Rules, new ChatDeckRule { Id = "bad", Kind = ChatDeckMatchKind.Regex, Pattern = "(", Action = ChatDeckRuleAction.Hide }] });
        Assert.Equal(ChatDeckRuleStatus.Invalid, model.CompiledRules.Single(r => r.Rule.Id == "bad").Status);
        Assert.Equal("Davian", model.CharacterName);
    }

    [Fact]
    public void OwnLinesUnderTheCharactersName_AreOwnAndDoNotAlert()
    {
        var (log, model) = Create();
        var delivered = new List<ChatDeckDelivery>();
        model.LinesDelivered += batch => delivered.AddRange(batch);
        log.OnLocalSpeech("Davian", "Davian is here", 0x50000002u, isRanged: true, logTextType: 0x02u);
        model.Tick();
        ChatDeckLine line = Assert.Single(delivered).Line;
        Assert.True(line.Facts.IsOwn);
        Assert.False(line.Alert);
    }

    [Fact]
    public void SelectTab_RecordsLastTab()
    {
        var (_, model) = Create();
        ChatDeckSettings? changed = null;
        model.SettingsChanged += s => changed = s;
        model.SelectTab("group");
        Assert.Equal("group", model.Settings.Layout.LastTabId);
        Assert.Equal("group", changed!.Layout.LastTabId);
    }

    [Fact]
    public void StartsOnLastTab()
    {
        var d = ChatDeckDefaults.Create();
        var (_, model) = Create(d with { Layout = d.Layout with { LastTabId = "social" } });
        Assert.Equal("social", model.SelectedTabId);
    }

    [Fact]
    public void Dispose_StopsListening()
    {
        var (log, model) = Create();
        model.Dispose();
        model.Dispose();
        Say(log, "late");
        model.Tick();
        Assert.Equal(0, model.FindTab("all")!.Count);
    }

    [Fact]
    public void SendChannel_OverrideThenTabThenSay()
    {
        var (_, model) = Create();
        Assert.Equal(ChatDeckSendChannel.Fellowship, model.SendChannelFor("group", null));
        Assert.Equal(ChatDeckSendChannel.Trade, model.SendChannelFor("group", ChatDeckSendChannel.Trade));
        Assert.Equal(ChatDeckSendChannel.Say, model.SendChannelFor("missing", null));
    }

    [Fact]
    public void Test_EvaluatesWithoutSideEffects()
    {
        var (_, model) = Create();
        var verdict = model.Test(ChatDeckMatcherTests.Facts("Davian, heal"));
        Assert.True(verdict.Alert);
        Assert.Equal(0, model.FindTab("all")!.Count);
    }

    [Fact]
    public void SetLayout_SavesPinnedAndEdge_OncePerChange()
    {
        var model = new ChatDeckModel(new ChatLog(), ChatDeckDefaults.Create(), "Davian");
        var saved = new List<ChatDeckSettings>();
        model.SettingsChanged += saved.Add;

        model.SetLayout(pinned: false, ChatDeckEdge.Right);
        model.SetLayout(pinned: false, ChatDeckEdge.Right);

        Assert.Single(saved);
        Assert.False(model.Settings.Layout.Pinned);
        Assert.Equal(ChatDeckEdge.Right, model.Settings.Layout.Edge);
    }

    [Fact]
    public void SetLayout_KeepsTheLastTab()
    {
        var model = new ChatDeckModel(new ChatLog(), ChatDeckDefaults.Create(), "Davian");
        model.SelectTab("combat");
        model.SetLayout(pinned: false, ChatDeckEdge.Left);
        Assert.Equal("combat", model.Settings.Layout.LastTabId);
    }

    [Fact]
    public void SetAppearance_Normalizes_AndRaisesOnce()
    {
        var model = new ChatDeckModel(new ChatLog(), ChatDeckDefaults.Create(), "Davian");
        int raised = 0;
        model.SettingsChanged += _ => raised++;
        model.SetAppearance(model.Settings.Appearance with { FontSize = 99 });
        Assert.Equal(20, model.Settings.Appearance.FontSize);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void SetAppearance_IgnoresNoChange()
    {
        var model = new ChatDeckModel(new ChatLog(), ChatDeckDefaults.Create(), "Davian");
        int raised = 0;
        model.SettingsChanged += _ => raised++;
        model.SetAppearance(model.Settings.Appearance with { });
        Assert.Equal(0, raised);
        model.SetAppearance(model.Settings.Appearance with { BackgroundOpacity = 0.3f });
        Assert.Equal(1, raised);
    }

    [Fact]
    public void SetKeys_Stores_AndIgnoresNoChange()
    {
        var model = new ChatDeckModel(new ChatLog(), ChatDeckDefaults.Create(), "Davian");
        int raised = 0;
        model.SettingsChanged += _ => raised++;
        var keys = new ChatDeckKeys { NextTab = "Ctrl+Alt+N" };
        model.SetKeys(keys);
        model.SetKeys(keys with { });
        Assert.Equal("Ctrl+Alt+N", model.Settings.Keys.NextTab);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Edit_AppliesAndRaisesOnce_NoChangeRaisesNothing()
    {
        var model = new ChatDeckModel(new ChatLog(), ChatDeckDefaults.Create(), "Davian");
        int raised = 0;
        model.SettingsChanged += _ => raised++;
        Assert.False(model.Edit(s => s));
        Assert.Equal(0, raised);
        Assert.True(model.Edit(s => ChatDeckSettingsEdits.AddTab(s, "Loot", out _)));
        Assert.Equal(1, raised);
        Assert.NotNull(model.FindTab("tab-1"));
    }

    [Fact]
    public void Edit_RebuildsRules()
    {
        var model = new ChatDeckModel(new ChatLog(), ChatDeckDefaults.Create(), "Davian");
        model.Edit(s => ChatDeckSettingsEdits.AddRule(s,
            new ChatDeckRule { Id = "", Kind = ChatDeckMatchKind.Regex, Pattern = "(" }, out _));
        Assert.Equal(ChatDeckRuleStatus.Invalid, model.CompiledRules[^1].Status);
    }
}
