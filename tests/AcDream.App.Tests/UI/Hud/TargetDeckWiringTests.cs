using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.App.UI.Layout;
using AcDream.Core.Targeting;
using AcDream.Plugin.Abstractions;
using static AcDream.App.Tests.UI.Hud.TargetDeckModels;

namespace AcDream.App.Tests.UI.Hud;

/// <summary>
/// What <see cref="RetailUiRuntime"/> builds for the Target Deck, headless: plain
/// panels for the retail windows, the real deck, the runtime's switch entry and actions.
/// </summary>
public sealed class TargetDeckWiringTests
{
    private sealed class World
    {
        public readonly UiRoot Root = new() { Width = 1600, Height = 900 };
        public readonly UiPanel Vitals = new() { Width = 160, Height = 58 };
        public readonly UiPanel Chat = new() { Width = 300, Height = 200 };
        public readonly UiPanel CommandDeck = new() { Width = 420, Height = 150, Visible = false };
        public readonly UiTargetDeck Deck;
        public readonly ModernHudSwitch Switch;
        public int Ticks;
        public int Shown;
        public bool DeckMounted = true;

        public World()
        {
            Root.AddChild(Vitals);
            Root.RegisterWindow(WindowNames.Vitals, Vitals, Vitals, null);
            Root.AddChild(Chat);
            Root.RegisterWindow(WindowNames.Chat, Chat, Chat, null);
            Root.AddChild(CommandDeck);
            Root.RegisterWindow(WindowNames.CommandDeck, CommandDeck, CommandDeck, null);

            Deck = new UiTargetDeck(() => null, new NoActions(), icons: null, clock: () => 0.0);
            Deck.Ticked += () => Ticks++;
            RetailWindowFrame.Mount(Root, Deck, _ => (0u, 0, 0),
                UiTargetDeck.MountOptions(RetailUiRuntime.TargetDeckDefaultLeft(Root.Width), RetailUiRuntime.TargetDeckDefaultTop));

            Switch = new ModernHudSwitch(
                Root.ShowWindow, Root.HideWindow, Root.IsWindowVisible, () => true,
                [
                    new ModernHudReplacement("Command Deck", WindowNames.CommandDeck, ModernHudSwitch.CommandDeckRetailWindows,
                        () => true, OnShown: () => { }, OnHidden: () => { }),
                    RetailUiRuntime.TargetDeckReplacement(() => DeckMounted ? Deck : null, () => Shown++),
                ],
                log: _ => { });
        }

        public void Tick() => Root.Tick(0.016, 0);
    }

    [Fact]
    public void TheReplacement_IsTheTargetDeck_WithNoRetailWindows()
    {
        ModernHudReplacement r = RetailUiRuntime.TargetDeckReplacement(() => null);
        Assert.Equal("Target Deck", r.Name);
        Assert.Equal(WindowNames.TargetDeck, r.Window);
        Assert.Empty(r.RetailWindows);
        Assert.False(r.Mounted());
    }

    [Fact]
    public void On_ShowsTheDeck_Off_HidesIt_AndNoRetailWindowChanges()
    {
        var w = new World();
        w.Switch.Apply(true);
        Assert.True(w.Root.IsWindowVisible(WindowNames.TargetDeck));
        Assert.True(w.Chat.Visible);

        w.Switch.Apply(false);
        Assert.False(w.Root.IsWindowVisible(WindowNames.TargetDeck));
        Assert.True(w.Chat.Visible);
    }

    [Fact]
    public void ShowingTheDeck_RunsItsOnShown()
    {
        var w = new World();
        w.Switch.Apply(true);
        Assert.Equal(1, w.Shown);
        w.Switch.Apply(false);
        Assert.Equal(1, w.Shown);
    }

    [Fact]
    public void OnItsOwn_TheSwitchTouchesNoRetailWindow()
    {
        var root = new UiRoot { Width = 1600, Height = 900 };
        var retail = new Dictionary<string, UiPanel>();
        foreach (string name in ModernHudSwitch.CommandDeckRetailWindows.Concat(ModernHudSwitch.ChatDeckRetailWindows))
        {
            var panel = new UiPanel { Width = 100, Height = 50, Visible = name != WindowNames.SideVitals };
            root.AddChild(panel);
            root.RegisterWindow(name, panel, panel, null);
            retail[name] = panel;
        }
        var deck = new UiTargetDeck(() => null, new NoActions(), icons: null, clock: () => 0.0);
        RetailWindowFrame.Mount(root, deck, _ => (0u, 0, 0), UiTargetDeck.MountOptions(0f, 12f));
        var hud = new ModernHudSwitch(root.ShowWindow, root.HideWindow, root.IsWindowVisible, () => true,
            [RetailUiRuntime.TargetDeckReplacement(() => deck)], log: _ => { });
        var before = retail.ToDictionary(p => p.Key, p => p.Value.Visible);

        hud.Apply(true);
        Assert.True(root.IsWindowVisible(WindowNames.TargetDeck));
        Assert.Equal(before, retail.ToDictionary(p => p.Key, p => p.Value.Visible));

        hud.Apply(false);
        Assert.False(root.IsWindowVisible(WindowNames.TargetDeck));
        Assert.Equal(before, retail.ToDictionary(p => p.Key, p => p.Value.Visible));
    }

    [Fact]
    public void NotMounted_TheSwitchLeavesItOut()
    {
        var w = new World { DeckMounted = false };
        w.Switch.Apply(true);
        Assert.False(w.Root.IsWindowVisible(WindowNames.TargetDeck));
        Assert.True(w.CommandDeck.Visible);
    }

    [Fact]
    public void TheDeckTicks_OnlyWhileModernHudShowsIt()
    {
        var w = new World();
        w.Tick();
        Assert.Equal(0, w.Ticks);

        w.Switch.Apply(true);
        w.Tick();
        w.Tick();
        Assert.Equal(2, w.Ticks);

        w.Switch.Apply(false);
        w.Tick();
        Assert.Equal(2, w.Ticks);
    }

    [Fact]
    public void TheDefaultPlace_IsCentredAtTheTop()
    {
        var w = new World();
        Assert.Equal((1600f - TargetDeckGeometry.Width) * 0.5f, w.Deck.Left);
        Assert.Equal(12f, w.Deck.Top);
        Assert.Equal(0f, RetailUiRuntime.TargetDeckDefaultLeft(100f));
    }

    [Fact]
    public void PluginTargetKind_MatchesTargetKindMemberForMember()
    {
        Assert.Equal(Enum.GetNames<TargetKind>(), Enum.GetNames<PluginTargetKind>());
        Assert.Equal(
            Enum.GetValues<TargetKind>().Select(k => (int)k),
            Enum.GetValues<PluginTargetKind>().Select(k => (int)k));
    }

    private sealed class Routes
    {
        public readonly List<string> Calls = [];
        public uint PackItem;

        public RetailUiRuntime.TargetDeckActions Actions() => new(
            examine: id => Calls.Add($"examine {id}"),
            use: id => Calls.Add($"use {id}"),
            packItem: () => PackItem,
            useItemOnTarget: id => Calls.Add($"use-on {id}"),
            give: (item, id) => Calls.Add($"give {item} {id}"),
            trade: id => Calls.Add($"trade {id}"),
            fellowshipRecruit: id => Calls.Add($"recruit {id}"),
            allegianceSwear: id => Calls.Add($"swear {id}"),
            composeChat: text => Calls.Add($"compose {text}"),
            follow: id => Calls.Add($"follow {id}"),
            toggleCharacter: () => Calls.Add("character"),
            copyText: text => Calls.Add($"copy {text}"),
            wield: id => Calls.Add($"wield {id}"),
            deselect: () => Calls.Add("deselect"),
            runPluginAction: (plugin, id, action) => Calls.Add($"plugin {plugin} {id} {action}"));
    }

    [Theory]
    [InlineData(TargetAction.Assess, "examine 7")]
    [InlineData(TargetAction.Examine, "examine 7")]
    [InlineData(TargetAction.Use, "use 7")]
    [InlineData(TargetAction.UseOrTalk, "use 7")]
    [InlineData(TargetAction.PickUpOrUse, "use 7")]
    [InlineData(TargetAction.Open, "use 7")]
    [InlineData(TargetAction.UsePortal, "use 7")]
    [InlineData(TargetAction.UseItemOn, "use-on 7")]
    [InlineData(TargetAction.UseKey, "use-on 7")]
    [InlineData(TargetAction.Give, "give 99 7")]
    [InlineData(TargetAction.Trade, "trade 7")]
    [InlineData(TargetAction.FellowshipInvite, "recruit 7")]
    [InlineData(TargetAction.SwearAllegiance, "swear 7")]
    [InlineData(TargetAction.Tell, "compose /tell Drudge Ravener, ")]
    [InlineData(TargetAction.Follow, "follow 7")]
    [InlineData(TargetAction.CharacterSheet, "character")]
    [InlineData(TargetAction.CopyName, "copy Drudge Ravener")]
    [InlineData(TargetAction.Wield, "wield 7")]
    [InlineData(TargetAction.Deselect, "deselect")]
    public void EachAction_TakesItsPath(TargetAction action, string expected)
    {
        var r = new Routes { PackItem = 99 };
        r.Actions().Run(action, Build(Monster()));
        Assert.Equal([expected], r.Calls);
    }

    [Theory]
    [InlineData(TargetAction.None)]
    [InlineData(TargetAction.More)]
    public void NoneAndMore_DoNothing(TargetAction action)
    {
        var r = new Routes { PackItem = 99 };
        r.Actions().Run(action, Build(Monster()));
        Assert.Empty(r.Calls);
    }

    [Theory]
    [InlineData(TargetAction.UseItemOn)]
    [InlineData(TargetAction.UseKey)]
    [InlineData(TargetAction.Give)]
    public void PackItemActions_WithNoPackItem_DoNothing(TargetAction action)
    {
        var r = new Routes();
        r.Actions().Run(action, Build(Monster()));
        Assert.Empty(r.Calls);
    }

    [Fact]
    public void APluginAction_GoesToThePluginStore()
    {
        var r = new Routes();
        r.Actions().RunPluginAction("aethrix", 7u, "mark");
        Assert.Equal(["plugin aethrix 7 mark"], r.Calls);
    }
}
