using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.App.UI.Layout;
using AcDream.Core.Items;
using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.App.Tests.UI.Hud;

/// <summary>
/// What <see cref="RetailUiRuntime"/> builds for the Gear Deck, headless: a plain
/// panel for the retail inventory, the real deck, the runtime's switch entry and
/// the window routing the runtime's public window calls go through.
/// </summary>
public sealed class GearDeckWiringTests
{
    private sealed class NoActions : IGearItemActions
    {
        public bool Ready() => true;
        public void Report(string text) { }
        public bool PutInContainer(uint item, uint container, int placement, bool fromGround) => true;
        public bool Merge(uint from, uint to) => true;
        public bool WieldFromPaperdoll(uint item, EquipMask mask) => true;
        public bool WieldSecondary(uint item) => true;
        public bool Activate(uint item) => true;
        public bool OpenPack(uint pack) => true;
    }

    private sealed class FakeStore : IGearDeckStore
    {
        public readonly Dictionary<string, string> Saved = [];
        public string? Load(string key) => Saved.GetValueOrDefault(key);
        public void Save(string key, string json) => Saved[key] = json;
    }

    private sealed class World
    {
        public readonly UiRoot Root = new() { Width = 1600, Height = 900 };
        public readonly UiPanel Inventory = new() { Width = 300, Height = 500, Visible = false };
        public readonly FakeStore Store = new();
        public readonly UiGearDeck Deck;
        public readonly ModernHudSwitch Switch;
        public int KeyReads;
        public int Closed;
        public bool Gameplay = true;

        public World(PaperdollClickMap? clickMap = null)
        {
            Root.AddChild(Inventory);
            Root.RegisterWindow(WindowNames.Inventory, Inventory, Inventory, null);

            var table = new ClientObjectTable();
            var session = new GearDeckSession(Store, () => { KeyReads++; return "Davian"; }, () => Gameplay, () => 0.0);
            Deck = new UiGearDeck(
                new GearDeckBindings(
                    Objects: () => table,
                    PlayerGuid: () => 0x50000001u,
                    Strength: () => 100,
                    ResolveIcon: (_, _, _, _, _) => 0u,
                    ResolveDragIcon: (_, _, _, _, _) => 0u,
                    DisplayName: o => o.Name,
                    Session: session,
                    Actions: new NoActions(),
                    IsPending: _ => false,
                    Close: () => Closed++,
                    ClickMap: clickMap),
                fonts: null, icons: null, clock: () => 0.0);
            RetailWindowFrame.Mount(Root, Deck, _ => (0u, 0, 0),
                UiGearDeck.MountOptions(RetailUiRuntime.GearDeckDefaultLeft(Root.Width), RetailUiRuntime.GearDeckDefaultTop));

            Switch = new ModernHudSwitch(
                Root.ShowWindow, Root.HideWindow, Root.IsWindowVisible, () => Gameplay,
                [RetailUiRuntime.GearDeckReplacement(() => Deck)],
                log: _ => { });
        }

        public bool Route(string name, RetailUiRuntime.WindowOp op) =>
            RetailUiRuntime.RouteWindow(Switch, name, op, n => op switch
            {
                RetailUiRuntime.WindowOp.Toggle => Root.ToggleWindow(n),
                RetailUiRuntime.WindowOp.Show => Root.ShowWindow(n),
                RetailUiRuntime.WindowOp.Hide => Root.HideWindow(n),
                _ => Root.IsWindowVisible(n),
            });

        public bool DeckVisible => Root.IsWindowVisible(WindowNames.GearDeck);

        public void Tick() => Root.Tick(0.016, 0);
    }

    [Fact]
    public void TheClickMap_ReachesTheDeckBindings()
    {
        var map = new PaperdollClickMap([0, 0, 0, 0xFF], 1, 1);
        Assert.Same(map, new World(map).Deck.Bindings.ClickMap);
        Assert.Null(new World().Deck.Bindings.ClickMap);
    }

    [Fact]
    public void TheReplacement_IsTheGearDeck_OnDemand_HoldingTheInventory()
    {
        ModernHudReplacement r = RetailUiRuntime.GearDeckReplacement(() => null);
        Assert.Equal("Gear Deck", r.Name);
        Assert.Equal(WindowNames.GearDeck, r.Window);
        Assert.Equal([WindowNames.Inventory], r.RetailWindows);
        Assert.True(r.HoldsRetailVisibility);
        Assert.True(r.OnDemand);
        Assert.False(r.Mounted());
    }

    [Fact]
    public void ModernHudOn_TheInventoryRedirectsToTheDeck_AndTogglingShowsIt()
    {
        var w = new World();
        w.Switch.Apply(true);
        Assert.Equal(WindowNames.GearDeck, w.Switch.RedirectTarget(WindowNames.Inventory));
        Assert.False(w.DeckVisible);   // on demand: not shown until opened

        Assert.True(w.Route(WindowNames.Inventory, RetailUiRuntime.WindowOp.Toggle));
        Assert.True(w.DeckVisible);
        Assert.False(w.Inventory.Visible);
        Assert.True(w.Route(WindowNames.Inventory, RetailUiRuntime.WindowOp.IsVisible));

        Assert.True(w.Route(WindowNames.Inventory, RetailUiRuntime.WindowOp.Toggle));
        Assert.False(w.DeckVisible);
        Assert.False(w.Route(WindowNames.Inventory, RetailUiRuntime.WindowOp.IsVisible));
    }

    [Fact]
    public void ModernHudOn_ShowAndHide_OpenAndCloseTheDeck()
    {
        var w = new World();
        w.Switch.Apply(true);
        Assert.True(w.Route(WindowNames.Inventory, RetailUiRuntime.WindowOp.Show));
        Assert.True(w.DeckVisible);
        Assert.True(w.Route(WindowNames.Inventory, RetailUiRuntime.WindowOp.Show));
        Assert.True(w.DeckVisible);
        Assert.True(w.Route(WindowNames.Inventory, RetailUiRuntime.WindowOp.Hide));
        Assert.False(w.DeckVisible);
        Assert.False(w.Inventory.Visible);
    }

    [Fact]
    public void ModernHudOff_TheDeckIsHidden_AndTheInventoryOpensAsUsual()
    {
        var w = new World();
        w.Switch.Apply(false);
        Assert.Null(w.Switch.RedirectTarget(WindowNames.Inventory));

        w.Route(WindowNames.Inventory, RetailUiRuntime.WindowOp.Toggle);
        w.Switch.Tick();
        Assert.True(w.Inventory.Visible);
        Assert.False(w.DeckVisible);
        Assert.True(w.Route(WindowNames.Inventory, RetailUiRuntime.WindowOp.IsVisible));
    }

    [Fact]
    public void TurningModernHudOff_HidesAnOpenDeck()
    {
        var w = new World();
        w.Switch.Apply(true);
        w.Route(WindowNames.Inventory, RetailUiRuntime.WindowOp.Show);
        Assert.True(w.DeckVisible);

        w.Switch.Apply(false);
        Assert.False(w.DeckVisible);
        Assert.False(w.Switch.IsOpen(WindowNames.GearDeck));
    }

    [Fact]
    public void ARetailInventoryShownDirectly_BecomesTheDeckOnTheNextTick()
    {
        var w = new World();
        w.Switch.Apply(true);
        w.Root.ShowWindow(WindowNames.Inventory);   // a controller that bypassed the redirect
        Assert.True(w.Inventory.Visible);

        w.Switch.Tick();
        Assert.False(w.Inventory.Visible);
        Assert.True(w.DeckVisible);
    }

    [Fact]
    public void CloseAllOnDemand_HidesTheDeckOnTheNextTick()
    {
        var w = new World();
        w.Switch.Apply(true);
        w.Route(WindowNames.Inventory, RetailUiRuntime.WindowOp.Show);
        w.Switch.CloseAllOnDemand();
        w.Switch.Tick();
        Assert.False(w.DeckVisible);
    }

    [Fact]
    public void TheFirstOpen_LoadsTheCharacterSettings_BeforeTheDeckDraws()
    {
        var w = new World();
        w.Store.Saved["Davian"] = """{"version":1,"folded":true,"view":"list","selectedPack":0}""";
        w.Switch.Apply(true);
        w.Route(WindowNames.Inventory, RetailUiRuntime.WindowOp.Toggle);
        // No UI tick yet: a draw this frame must already see the character's settings.
        Assert.True(w.Deck.Bindings.Session.Settings.Folded);
        Assert.Equal(GearPackView.List, w.Deck.Bindings.Session.Settings.View);
    }

    [Fact]
    public void TheSessionTicks_OnlyWhileTheDeckIsShown()
    {
        var w = new World();
        w.Switch.Apply(true);
        w.Tick();
        w.Tick();
        Assert.Equal(0, w.KeyReads);

        w.Route(WindowNames.Inventory, RetailUiRuntime.WindowOp.Show);
        int afterShow = w.KeyReads;
        w.Tick();
        Assert.Equal(afterShow + 1, w.KeyReads);

        w.Route(WindowNames.Inventory, RetailUiRuntime.WindowOp.Hide);
        int afterHide = w.KeyReads;
        w.Tick();
        Assert.Equal(afterHide, w.KeyReads);
    }

    [Fact]
    public void OtherWindows_AreNotRedirected()
    {
        var w = new World();
        w.Switch.Apply(true);
        var calls = new List<string>();
        bool result = RetailUiRuntime.RouteWindow(w.Switch, WindowNames.Character, RetailUiRuntime.WindowOp.Toggle,
            n => { calls.Add(n); return true; });
        Assert.True(result);
        Assert.Equal([WindowNames.Character], calls);
        Assert.False(w.DeckVisible);
    }

    [Fact]
    public void NoSwitch_GoesStraightToTheRawOperation()
    {
        var calls = new List<string>();
        Assert.False(RetailUiRuntime.RouteWindow(null, WindowNames.Inventory, RetailUiRuntime.WindowOp.IsVisible,
            n => { calls.Add(n); return false; }));
        Assert.Equal([WindowNames.Inventory], calls);
    }

    [Fact]
    public void TheToolbarInventoryButton_FollowsTheGearDeck()
    {
        Assert.True(RetailUiRuntime.TryGetToolbarPanel(WindowNames.GearDeck, out uint deckPanel));
        Assert.Equal(RetailPanelCatalog.Inventory, deckPanel);
        Assert.True(RetailUiRuntime.TryGetToolbarPanel(WindowNames.Inventory, out uint inventoryPanel));
        Assert.Equal(RetailPanelCatalog.Inventory, inventoryPanel);
        Assert.False(RetailUiRuntime.TryGetToolbarPanel(WindowNames.ChatDeck, out _));
    }

    [Fact]
    public void TheIndicatorBar_TogglesCataloguedPanelsByWindowName()
    {
        var windows = new List<string>();
        var panels = new List<uint>();
        RetailUiRuntime.ToggleIndicatorPanel(RetailPanelCatalog.Inventory,
            n => { windows.Add(n); return true; }, p => { panels.Add(p); return true; });
        RetailUiRuntime.ToggleIndicatorPanel(9999u,
            n => { windows.Add(n); return true; }, p => { panels.Add(p); return true; });
        Assert.Equal([WindowNames.Inventory], windows);
        Assert.Equal([9999u], panels);
    }

    [Fact]
    public void TheDefaultPlace_IsTheRightEdge_UnderTheRadar()
    {
        var w = new World();
        Assert.Equal(1600f - GearDeckGeometry.Width - 10f, w.Deck.Left);
        Assert.Equal(120f, w.Deck.Top);
        Assert.Equal(0f, RetailUiRuntime.GearDeckDefaultLeft(100f));
    }

    [Fact]
    public void TheDeckIsMountedAsADraggableWindow()
    {
        var w = new World();
        Assert.True(w.Deck.Draggable);   // empty frame space drags it
        Assert.Same(w.Root, w.Deck.Parent);
    }

    private static (World W, UiPanel Character, RetailPanelUiController PanelUi) WithCharacterPanel()
    {
        var w = new World();
        var character = new UiPanel { Width = 300, Height = 500 };
        w.Root.AddChild(character);
        w.Root.RegisterWindow(WindowNames.Character, character, character, null);
        var panelUi = new RetailPanelUiController(w.Root.IsWindowVisible, w.Root.ShowWindow, w.Root.HideWindow);
        panelUi.Register(RetailPanelCatalog.Character, WindowNames.Character);
        panelUi.Register(RetailPanelCatalog.Inventory, WindowNames.Inventory);
        panelUi.SetPanelVisibility(RetailPanelCatalog.Character, visible: true);
        return (w, character, panelUi);
    }

    [Fact]
    public void ThePacksShow_UnderModernHud_OpensTheDeck_AndLeavesTheCharacterPanelOpen()
    {
        var (w, character, panelUi) = WithCharacterPanel();
        w.Switch.Apply(true);

        RetailUiRuntime.ShowInventoryForController(w.Switch, panelUi, n => w.Route(n, RetailUiRuntime.WindowOp.Show));
        w.Switch.Tick();

        Assert.True(character.Visible);
        Assert.False(w.Inventory.Visible);
        Assert.True(w.DeckVisible);
    }

    [Fact]
    public void ThePacksShow_ModernHudOff_ShowsTheRetailPanelInTheSharedSlot()
    {
        var (w, character, panelUi) = WithCharacterPanel();
        w.Switch.Apply(false);
        var routed = new List<string>();

        RetailUiRuntime.ShowInventoryForController(w.Switch, panelUi, n => { routed.Add(n); return true; });

        Assert.Empty(routed);
        Assert.True(w.Inventory.Visible);
        Assert.False(character.Visible);   // the retail panels take turns in the main slot
        Assert.False(w.DeckVisible);
    }

    [Fact]
    public void TurningModernHudOff_FromTheOptionsPanel_LeavesItOpen_AndTheInventoryClosed()
    {
        var w = new World();
        var options = new UiPanel { Width = 300, Height = 500 };
        w.Root.AddChild(options);
        w.Root.RegisterWindow(WindowNames.Options, options, options, null);
        var panelUi = new RetailPanelUiController(w.Root.IsWindowVisible, w.Root.ShowWindow, w.Root.HideWindow);
        panelUi.Register(RetailPanelCatalog.Options, WindowNames.Options);
        panelUi.Register(RetailPanelCatalog.Inventory, WindowNames.Inventory);
        panelUi.SetPanelVisibility(RetailPanelCatalog.Inventory, visible: true);

        // The runtime's raw operations: a retail panel goes through the shared main-panel slot.
        var held = new Dictionary<string, bool>();
        var released = new List<string>();
        var hud = new ModernHudSwitch(
            n => RetailPanelCatalog.TryGetPanelId(n, out uint id) ? panelUi.SetPanelVisibility(id, true) : w.Root.ShowWindow(n),
            n => RetailPanelCatalog.TryGetPanelId(n, out uint id) ? panelUi.SetPanelVisibility(id, false) : w.Root.HideWindow(n),
            n => RetailPanelCatalog.TryGetPanelId(n, out uint id) ? panelUi.IsPanelVisible(id) : w.Root.IsWindowVisible(n),
            () => w.Gameplay,
            [RetailUiRuntime.GearDeckReplacement(() => w.Deck)],
            holdVisibility: n => held.TryAdd(n, w.Inventory.Visible),
            releaseVisibility: n => { released.Add(n); return held.Remove(n, out bool v) && v; },
            log: _ => { });

        hud.Apply(true);                                   // holds "inventory was open"
        Assert.False(w.Inventory.Visible);
        panelUi.SetPanelVisibility(RetailPanelCatalog.Options, visible: true);
        Assert.True(options.Visible);

        hud.Apply(false);

        Assert.True(options.Visible);
        Assert.False(w.Inventory.Visible);
        Assert.Equal([WindowNames.Inventory], released);
        Assert.False(w.DeckVisible);
    }

    [Fact]
    public void RuntimeGearSalvage_WithNoController_IsNotOpenAndTakesNothing()
    {
        var salvage = new RuntimeGearSalvage(() => null, () => true, _ => { });
        Assert.False(salvage.IsOpen);
        Assert.False(salvage.CanAdd(1));
        Assert.False(salvage.Add(1));
        Assert.Equal(0, salvage.Revision);
    }
}
