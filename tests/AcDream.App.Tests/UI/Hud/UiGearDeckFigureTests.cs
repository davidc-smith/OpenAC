using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.App.UI.Layout;
using AcDream.Core.Items;
using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiGearDeckFigureTests
{
    // Player, Bag, Helm (HeadWear), Sword (MeleeWeapon) and loose items as in UiGearDeckInputTests,
    // plus a loose cap (HeadWear) and a loose shirt (ChestWear) for the fit and drop tests.
    private const uint Player = 0x50000001, Bag = 0xB1, Helm = 0xC1, Sword = 0xC2, Cap = 0xD1, Shirt = 0xD2, First = 0xA00;

    /// <summary>A 100 × 214 map: rows 0-39 head (blue), rows 60-119 chest (green), the rest unmapped.</summary>
    internal static PaperdollClickMap TestMap()
    {
        var rgba = new byte[100 * 214 * 4];
        for (int y = 0; y < 214; y++)
            for (int x = 0; x < 100; x++)
            {
                int p = (y * 100 + x) * 4;
                (byte r, byte g, byte b) = y < 40 ? ((byte)0, (byte)0, (byte)0xFF)
                    : y >= 60 && y < 120 ? ((byte)0, (byte)0xFF, (byte)0) : ((byte)0xFF, (byte)0xFF, (byte)0xFF);
                (rgba[p], rgba[p + 1], rgba[p + 2], rgba[p + 3]) = (r, g, b, 0xFF);
            }
        return new PaperdollClickMap(rgba, 100, 214);
    }

    private sealed class Fake : IGearItemActions
    {
        public readonly List<string> Calls = [];
        public bool Ready() => true;
        public void Report(string text) => Calls.Add($"report {text}");
        public bool PutInContainer(uint item, uint container, int placement, bool fromGround) { Calls.Add($"put {item:X} {container:X} {placement}"); return true; }
        public bool Merge(uint from, uint to) { Calls.Add($"merge {from:X} {to:X}"); return true; }
        public bool WieldFromPaperdoll(uint item, EquipMask mask) { Calls.Add($"wield {item:X} {mask}"); return true; }
        public bool WieldSecondary(uint item) { Calls.Add($"secondary {item:X}"); return true; }
        public bool Activate(uint item) { Calls.Add($"use {item:X}"); return true; }
        public bool OpenPack(uint pack) { Calls.Add($"open {pack:X}"); return true; }
    }

    private static void Place(ClientObjectTable t, ClientObject o, uint container, int slot)
    {
        t.AddOrUpdate(o);
        Assert.True(t.MoveItem(o.ObjectId, container, slot));
    }

    private static void Wear(ClientObjectTable t, ClientObject o, EquipMask mask)
    {
        t.AddOrUpdate(o);
        Assert.True(t.MoveItem(o.ObjectId, Player, -1, mask));
    }

    /// <summary>A player with one bag, a cap and a shirt (both auto-wear), four junk items, a worn helm and sword.</summary>
    private static ClientObjectTable World()
    {
        var t = new ClientObjectTable();
        t.AddOrUpdate(new ClientObject { ObjectId = Player, Type = ItemType.Creature, ItemsCapacity = 102, ContainersCapacity = 7 });
        Place(t, new ClientObject { ObjectId = Bag, Type = ItemType.Container, ItemsCapacity = 24, Name = "Pack" }, Player, 0);
        Place(t, new ClientObject { ObjectId = Cap, Name = "Cap", Type = ItemType.Clothing, ValidLocations = EquipMask.HeadWear }, Player, 0);
        Place(t, new ClientObject { ObjectId = Shirt, Name = "Shirt", Type = ItemType.Clothing, ValidLocations = EquipMask.ChestWear }, Player, 1);
        for (int i = 0; i < 4; i++)
            Place(t, new ClientObject { ObjectId = First + (uint)i, Name = $"Item {i}", Type = ItemType.Misc }, Player, 2 + i);
        Wear(t, new ClientObject { ObjectId = Helm, WielderId = Player, Name = "Helm", ValidLocations = EquipMask.HeadWear }, EquipMask.HeadWear);
        Wear(t, new ClientObject { ObjectId = Sword, WielderId = Player, Name = "Sword", ValidLocations = EquipMask.MeleeWeapon }, EquipMask.MeleeWeapon);
        return t;
    }

    private sealed class Rig
    {
        public required UiGearDeck Deck;
        public required Fake Actions;
        public required GearDeckSession Session;
        public double Now = 0;   // the clock later tasks advance for double-clicks
    }

    private static Rig Make(bool attach = true, Func<GearDeckSettings, GearDeckSettings>? settings = null,
        float scale = 1f, List<string>? log = null, GearDeckSession? session = null)
    {
        ClientObjectTable table = World();
        session ??= new GearDeckSession(null, () => "", () => false);
        if (settings is not null) session.Update(settings);
        var actions = new Fake();
        Rig rig = null!;
        var b = new GearDeckBindings(
            Objects: () => table,
            PlayerGuid: () => Player,
            Strength: () => 100,
            ResolveIcon: (_, _, _, _, _) => 0u,
            ResolveDragIcon: (_, _, _, _, _) => 77u,
            DisplayName: o => o.Name,
            Session: session,
            Actions: actions,
            IsPending: _ => false,
            Close: () => { },
            Log: log is null ? null : log.Add,
            ClickMap: TestMap());
        var deck = new UiGearDeck(b, null, null, () => rig.Now);
        rig = new Rig { Deck = deck, Actions = actions, Session = session };
        if (attach) deck.AttachFigure();
        if (scale != 1f) deck.SetScale(scale);
        deck.Height = 900f * scale;
        deck.Refresh();
        return rig;
    }

    /// <summary>The figure's padlock shut: a press turns it and the wheel zooms it.</summary>
    private static GearDeckSettings Locked(GearDeckSettings s) => s with { FigureLocked = true };

    private static (float X, float Y) Centre(DeckRect r) => (r.X + r.W / 2f, r.Y + r.H / 2f);

    private static void Send(UiGearDeck deck, int type, DeckRect at, object? payload = null, int data0 = 0)
    {
        var (x, y) = Centre(at);
        deck.HitTestLocal(x, y);
        deck.OnEvent(new UiEvent(0, deck, type, Data0: data0, Data1: (int)x, Data2: (int)y, Payload: payload));
    }

    private static void Click(UiGearDeck deck, DeckRect at)
    {
        Send(deck, UiEventType.MouseDown, at);
        Send(deck, UiEventType.Click, at);
    }

    /// <summary>A point on the figure, in deck coordinates, at click-map pixel (mx, my).</summary>
    private static (float X, float Y) MapPoint(UiGearDeck deck, int mx, int my)
    {
        DeckRect m = deck.Geometry.ClickMap;
        float s = deck.Scale;
        return (m.X + (mx + 0.5f) * s, m.Y + (my + 0.5f) * s);
    }

    [Fact]
    public void Attached_DefaultsToFigure_AndTheStudsSwitch()
    {
        Rig r = Make();
        Assert.True(r.Deck.Geometry.Input.Figure);
        Click(r.Deck, r.Deck.Geometry.SlotsStud);
        Assert.Equal(GearEquipView.Slots, r.Session.Settings.Equip);
        Assert.False(r.Deck.Geometry.Input.Figure);
        Click(r.Deck, r.Deck.Geometry.FigureStud);
        Assert.Equal(GearEquipView.Figure, r.Session.Settings.Equip);
        Assert.True(r.Deck.Geometry.Input.Figure);
    }

    [Fact]
    public void TheFigureStud_Unfolds()
    {
        Rig r = Make(settings: s => s with { Folded = true, Equip = GearEquipView.Slots });
        Click(r.Deck, r.Deck.Geometry.FigureStud);
        Assert.False(r.Session.Settings.Folded);
        Assert.True(r.Deck.Geometry.Input.Figure);
    }

    [Fact]
    public void Detached_ShowsSlots_KeepsTheSetting_AndLogsOnce()
    {
        var log = new List<string>();
        Rig r = Make(attach: false, log: log);
        Assert.False(r.Deck.Geometry.Input.Figure);
        Click(r.Deck, r.Deck.Geometry.FigureStud);
        Assert.Equal(GearEquipView.Figure, r.Session.Settings.Equip);
        r.Deck.OnShown();
        r.Deck.OnShown();
        Assert.Single(log, l => l.Contains("figure view unavailable"));
    }

    [Fact]
    public void FigureSize_OnlyWhileShown()
    {
        var parent = new UiPanel { Width = 2000, Height = 2000 };   // UiElement is abstract
        Rig r = Make();
        parent.AddChild(r.Deck);
        Assert.True(r.Deck.TryGetFigureSize(out int w, out int h));
        Assert.Equal((140, 214), (w, h));
        parent.Visible = false;
        Assert.False(r.Deck.TryGetFigureSize(out _, out _));
        parent.Visible = true;
        Click(r.Deck, r.Deck.Geometry.FoldStud);
        Assert.False(r.Deck.TryGetFigureSize(out _, out _));
    }

    [Fact]
    public void FigureSize_FollowsTheScale()
    {
        Rig r = Make(scale: 1.5f);
        Assert.True(r.Deck.TryGetFigureSize(out int w, out int h));
        Assert.Equal((210, 321), (w, h));
    }

    [Fact]
    public void TheFrameView_RoutesTextureAndPose()
    {
        Rig r = Make();
        var view = new GearDeckPaperdollFrameView(r.Deck);
        view.SetTextureHandle(5u);
        Assert.True(r.Deck.FigureTexture.IsAssigned);
        view.ClearTextureHandle();
        Assert.False(r.Deck.FigureTexture.IsAssigned);
        Assert.Equal(r.Deck.FigurePose, view.Pose);
    }

    [Fact]
    public void Detach_ClearsTheTexture_AndFallsBackToSlots()
    {
        Rig r = Make();
        r.Deck.SetFigureTexture(new GpuTextureSlot(3));
        r.Deck.DetachFigure();
        r.Deck.Refresh();
        Assert.False(r.Deck.FigureTexture.IsAssigned);
        Assert.False(r.Deck.Geometry.Input.Figure);
    }

    private static void SendAt(UiGearDeck deck, int type, (float X, float Y) at, int data0 = 0, object? payload = null)
    {
        deck.HitTestLocal(at.X, at.Y);
        deck.OnEvent(new UiEvent(0, deck, type, Data0: data0, Data1: (int)at.X, Data2: (int)at.Y, Payload: payload));
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void TheHead_IsTheHelm_AtAnyScale(float scale)
    {
        Rig r = Make(scale: scale);
        var head = MapPoint(r.Deck, 50, 10);
        SendAt(r.Deck, UiEventType.MouseMove, head);
        Assert.Equal(GearZone.Figure, r.Deck.Hover.Zone);
        Assert.Equal((int)EquipMask.HeadWear, r.Deck.Hover.Index);
        r.Now = 10;
        Assert.Equal("Helm", r.Deck.VisibleTooltip?.Title);
    }

    [Fact]
    public void ABareRegion_AndUnmappedSpace_NameNothingWorn()
    {
        Rig r = Make();
        var chest = MapPoint(r.Deck, 50, 80);     // mapped, nothing worn there
        SendAt(r.Deck, UiEventType.MouseMove, chest);
        Assert.Equal((int)(EquipMask.ChestWear | EquipMask.ChestArmor), r.Deck.Hover.Index);
        r.Now = 10;
        Assert.Null(r.Deck.VisibleTooltip);
        var gap = MapPoint(r.Deck, 50, 50);       // white: unmapped
        SendAt(r.Deck, UiEventType.MouseMove, gap);
        Assert.Equal(0, r.Deck.Hover.Index);
    }

    [Fact]
    public void APressOnTheHelm_LiftsIt()
    {
        var log = new List<string>();
        Rig r = Make(log: log);
        var head = MapPoint(r.Deck, 50, 10);
        Assert.True(r.Deck.HitTestLocal(head.X, head.Y));
        Assert.True(r.Deck.IsDragSource);
        SendAt(r.Deck, UiEventType.MouseDown, head);
        var p = Assert.IsType<ItemDragPayload>(r.Deck.GetDragPayload());
        Assert.Equal((Helm, ItemDragSource.Equipment, -1), (p.ObjId, p.SourceKind, p.SourceSlot));
        SendAt(r.Deck, UiEventType.DragBegin, head, payload: p);
        Assert.Single(log, l => l.StartsWith($"[gear-deck] drag 0x{Helm:X8} from Figure"));
    }

    [Fact]
    public void DoubleClickTheHelm_TakesItOff()
    {
        Rig r = Make();
        var head = MapPoint(r.Deck, 50, 10);
        SendAt(r.Deck, UiEventType.MouseDown, head);
        SendAt(r.Deck, UiEventType.Click, head);
        SendAt(r.Deck, UiEventType.MouseDown, head);
        SendAt(r.Deck, UiEventType.Click, head);
        SendAt(r.Deck, UiEventType.DoubleClick, head);
        Assert.Contains(r.Actions.Calls, c => c.StartsWith($"put {Helm:X} "));
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void Locked_ADrag_Turns_AndTheMapGoesQuiet(float scale)
    {
        Rig r = Make(settings: Locked, scale: scale);
        var chest = MapPoint(r.Deck, 50, 80);
        SendAt(r.Deck, UiEventType.MouseMove, chest);
        GearDeckHit hovered = r.Deck.Hover;
        Assert.True(r.Deck.HitTestLocal(chest.X, chest.Y));
        Assert.False(r.Deck.IsDragSource);
        SendAt(r.Deck, UiEventType.MouseDown, chest);
        SendAt(r.Deck, UiEventType.MouseMove, (chest.X + 2f, chest.Y));
        Assert.True(r.Deck.FigurePose.IsDefault);       // inside the dead zone
        // Past the dead zone the turn counts the whole way from the press: 50 points, in scale-1 points.
        SendAt(r.Deck, UiEventType.MouseMove, (chest.X + 50f, chest.Y));
        float yaw = 50f / scale * UiGearDeck.TurnPerPoint;
        Assert.Equal(yaw, r.Deck.FigurePose.Yaw, 3);
        Assert.Equal(hovered, r.Deck.Hover);            // a turn's moves leave the hover alone
        SendAt(r.Deck, UiEventType.MouseMove, (chest.X + 30f, chest.Y));
        Assert.Equal(30f / scale * UiGearDeck.TurnPerPoint, r.Deck.FigurePose.Yaw, 3);   // then by deltas, back inside too
        SendAt(r.Deck, UiEventType.MouseMove, (chest.X + 50f, chest.Y));
        SendAt(r.Deck, UiEventType.MouseUp, (chest.X + 50f, chest.Y));
        SendAt(r.Deck, UiEventType.MouseMove, (chest.X + 90f, chest.Y));
        Assert.Equal(yaw, r.Deck.FigurePose.Yaw, 3);    // released: no more turning

        var head = MapPoint(r.Deck, 50, 10);
        SendAt(r.Deck, UiEventType.MouseMove, head);
        Assert.Equal(0, r.Deck.Hover.Index);            // turned: no region
        Assert.True(r.Deck.HitTestLocal(head.X, head.Y));
        Assert.False(r.Deck.IsDragSource);
    }

    [Fact]
    public void ReleasingAfterATurn_CatchesTheHoverUp()
    {
        Rig r = Make(settings: Locked);
        var chest = MapPoint(r.Deck, 50, 80);
        SendAt(r.Deck, UiEventType.MouseMove, chest);
        SendAt(r.Deck, UiEventType.MouseDown, chest);
        SendAt(r.Deck, UiEventType.MouseMove, (chest.X + 50f, chest.Y));
        Assert.False(r.Deck.FigurePose.IsDefault);
        var head = MapPoint(r.Deck, 50, 10);
        SendAt(r.Deck, UiEventType.MouseUp, head);
        Assert.Equal(GearZone.Figure, r.Deck.Hover.Zone);
        Assert.Equal(0, r.Deck.Hover.Index);            // turned: no region
        Assert.Equal(EquipMask.None, r.Deck.HoverFitMask);
    }

    [Fact]
    public void AClickWobble_DoesNotTurn()
    {
        Rig r = Make(settings: Locked);
        var chest = MapPoint(r.Deck, 50, 80);
        SendAt(r.Deck, UiEventType.MouseDown, chest);
        SendAt(r.Deck, UiEventType.MouseMove, (chest.X + 2f, chest.Y));
        SendAt(r.Deck, UiEventType.MouseUp, chest);   // the root sends it before the Click
        SendAt(r.Deck, UiEventType.Click, chest);
        Assert.True(r.Deck.FigurePose.IsDefault);
        var head = MapPoint(r.Deck, 50, 10);
        SendAt(r.Deck, UiEventType.MouseMove, head);
        Assert.Equal((int)EquipMask.HeadWear, r.Deck.Hover.Index);   // the map still names regions
    }

    [Fact]
    public void TheWheel_ZoomsTheFigure_AndDoubleClickResets()
    {
        Rig r = Make(settings: Locked);
        var chest = MapPoint(r.Deck, 50, 80);
        SendAt(r.Deck, UiEventType.Scroll, chest, data0: 2);
        Assert.Equal(1.12f * 1.12f, r.Deck.FigurePose.Zoom, 3);
        SendAt(r.Deck, UiEventType.MouseDown, chest);
        SendAt(r.Deck, UiEventType.Click, chest);
        SendAt(r.Deck, UiEventType.MouseDown, chest);
        SendAt(r.Deck, UiEventType.Click, chest);
        SendAt(r.Deck, UiEventType.DoubleClick, chest);
        Assert.True(r.Deck.FigurePose.IsDefault);
    }

    [Fact]
    public void HidingOrFolding_StandsTheFigureUp()
    {
        Rig r = Make(settings: Locked);
        var chest = MapPoint(r.Deck, 50, 80);
        SendAt(r.Deck, UiEventType.Scroll, chest, data0: 1);
        r.Deck.OnHidden();
        Assert.True(r.Deck.FigurePose.IsDefault);
        SendAt(r.Deck, UiEventType.Scroll, chest, data0: 1);
        Click(r.Deck, r.Deck.Geometry.FoldStud);
        Assert.True(r.Deck.FigurePose.IsDefault);
    }

    [Fact]
    public void ASessionEnd_StandsTheFigureUp()
    {
        Rig r = Make(settings: Locked);
        SendAt(r.Deck, UiEventType.Scroll, MapPoint(r.Deck, 50, 80), data0: 1);
        Assert.False(r.Deck.FigurePose.IsDefault);
        r.Session.EndSession();
        Assert.True(r.Deck.FigurePose.IsDefault);
    }

    [Fact]
    public void SwitchingCharacter_StandsTheFigureUp()
    {
        string key = "Davian";
        var session = new GearDeckSession(null, () => key, () => true, () => 0);
        session.Tick();
        Rig r = Make(settings: Locked, session: session);
        SendAt(r.Deck, UiEventType.Scroll, MapPoint(r.Deck, 50, 80), data0: 1);
        Assert.False(r.Deck.FigurePose.IsDefault);
        key = "Bob";
        session.Tick();
        Assert.True(r.Deck.FigurePose.IsDefault);
    }

    [Fact]
    public void TheSlotsStud_StandsTheFigureUp()
    {
        Rig r = Make(settings: Locked);
        var chest = MapPoint(r.Deck, 50, 80);
        SendAt(r.Deck, UiEventType.Scroll, chest, data0: 1);
        Assert.False(r.Deck.FigurePose.IsDefault);
        Click(r.Deck, r.Deck.Geometry.SlotsStud);
        Assert.True(r.Deck.FigurePose.IsDefault);
    }

    [Fact]
    public void DroppingTheCapOnTheHead_WearsIt_AndTheHighlightAccepts()
    {
        Rig r = Make();
        var head = MapPoint(r.Deck, 50, 10);
        var payload = new ItemDragPayload(Cap, ItemDragSource.Inventory, 0, null);
        SendAt(r.Deck, UiEventType.DragEnter, head, payload: payload);
        var drop = Assert.NotNull(r.Deck.DropHighlight);
        Assert.Equal(GearZone.Figure, drop.Hit.Zone);
        Assert.True(drop.Accept);
        SendAt(r.Deck, UiEventType.DropReleased, head, payload: payload);
        Assert.Contains(r.Actions.Calls, c => c.StartsWith($"wield {Cap:X} "));
    }

    [Fact]
    public void ALooseMiscItem_IsRefusedByTheFigure()
    {
        Rig r = Make();
        var chest = MapPoint(r.Deck, 50, 80);
        var payload = new ItemDragPayload(First, ItemDragSource.Inventory, 0, null);
        SendAt(r.Deck, UiEventType.DragEnter, chest, payload: payload);
        Assert.False(r.Deck.DropHighlight?.Accept);
        SendAt(r.Deck, UiEventType.DropReleased, chest, payload: payload);
        Assert.DoesNotContain(r.Actions.Calls, c => c.StartsWith("wield"));
    }

    // ---- the padlock ----

    [Fact]
    public void Unlocked_ABareFigureDrag_DoesNotTurn()
    {
        Rig r = Make();
        Assert.False(r.Deck.FigureLocked);
        var chest = MapPoint(r.Deck, 50, 80);
        SendAt(r.Deck, UiEventType.MouseDown, chest);
        SendAt(r.Deck, UiEventType.MouseMove, (chest.X + 50f, chest.Y));
        SendAt(r.Deck, UiEventType.MouseUp, (chest.X + 50f, chest.Y));
        Assert.True(r.Deck.FigurePose.IsDefault);
    }

    [Fact]
    public void Unlocked_TheWheel_DoesNotZoom_AndFallsThrough()
    {
        Rig r = Make();
        var chest = MapPoint(r.Deck, 50, 80);
        r.Deck.HitTestLocal(chest.X, chest.Y);
        bool handled = r.Deck.OnEvent(new UiEvent(0, r.Deck, UiEventType.Scroll, Data0: 2, Data1: (int)chest.X, Data2: (int)chest.Y));
        Assert.False(handled);
        Assert.True(r.Deck.FigurePose.IsDefault);
    }

    [Fact]
    public void Unlocked_TheHelmStillLifts_AndADoubleClickTakesItOff()
    {
        Rig r = Make();
        var head = MapPoint(r.Deck, 50, 10);
        Assert.True(r.Deck.HitTestLocal(head.X, head.Y));
        Assert.True(r.Deck.IsDragSource);
        DoubleClickAt(r.Deck, head);
        Assert.Contains(r.Actions.Calls, c => c.StartsWith($"put {Helm:X} "));
    }

    [Fact]
    public void Unlocked_ADoubleClickOnBareFigure_DoesNothing()
    {
        Rig r = Make();
        var chest = MapPoint(r.Deck, 50, 80);
        DoubleClickAt(r.Deck, chest);
        Assert.True(r.Deck.FigurePose.IsDefault);
        Assert.Empty(r.Actions.Calls);
    }

    [Fact]
    public void Locked_APressOnTheHelm_TurnsTheFigure_AndLiftsNothing()
    {
        Rig r = Make(settings: Locked);
        var head = MapPoint(r.Deck, 50, 10);
        Assert.True(r.Deck.HitTestLocal(head.X, head.Y));
        Assert.False(r.Deck.IsDragSource);
        Assert.True(r.Deck.CapturesPointerDrag);   // the window does not drag either
        SendAt(r.Deck, UiEventType.MouseDown, head);
        Assert.Null(r.Deck.GetDragPayload());
        SendAt(r.Deck, UiEventType.MouseMove, (head.X + 50f, head.Y));
        Assert.Equal(0.6f, r.Deck.FigurePose.Yaw, 3);
    }

    [Fact]
    public void Locked_TheWheelZooms()
    {
        Rig r = Make(settings: Locked);
        SendAt(r.Deck, UiEventType.Scroll, MapPoint(r.Deck, 50, 80), data0: 1);
        Assert.Equal(1.12f, r.Deck.FigurePose.Zoom, 3);
    }

    [Fact]
    public void Locked_ADoubleClickOnTheHelm_ResetsThePose_AndTakesNothingOff()
    {
        Rig r = Make(settings: Locked);
        var head = MapPoint(r.Deck, 50, 10);
        SendAt(r.Deck, UiEventType.Scroll, head, data0: 1);
        Assert.False(r.Deck.FigurePose.IsDefault);
        DoubleClickAt(r.Deck, head);   // zoomed, the map names no region there
        Assert.True(r.Deck.FigurePose.IsDefault);
        DoubleClickAt(r.Deck, head);   // at rest the press names the helm itself
        Assert.True(r.Deck.FigurePose.IsDefault);
        Assert.DoesNotContain(r.Actions.Calls, c => c.StartsWith($"put {Helm:X} "));
    }

    [Fact]
    public void Locked_DroppingTheCapOnTheHead_StillWearsIt()
    {
        Rig r = Make(settings: Locked);
        var head = MapPoint(r.Deck, 50, 10);
        var payload = new ItemDragPayload(Cap, ItemDragSource.Inventory, 0, null);
        SendAt(r.Deck, UiEventType.DragEnter, head, payload: payload);
        Assert.True(r.Deck.DropHighlight?.Accept);
        SendAt(r.Deck, UiEventType.DropReleased, head, payload: payload);
        Assert.Contains(r.Actions.Calls, c => c.StartsWith($"wield {Cap:X} "));
    }

    [Fact]
    public void ClickingTheLock_FlipsTheSetting_AndUnlockingStandsTheFigureUp()
    {
        Rig r = Make();
        Assert.Equal(GearZone.FigureLock, r.Deck.HitAt(Centre(r.Deck.Geometry.FigureLock).X, Centre(r.Deck.Geometry.FigureLock).Y).Zone);
        Click(r.Deck, r.Deck.Geometry.FigureLock);
        Assert.True(r.Session.Settings.FigureLocked);
        var chest = MapPoint(r.Deck, 50, 80);
        SendAt(r.Deck, UiEventType.MouseDown, chest);
        SendAt(r.Deck, UiEventType.MouseMove, (chest.X + 50f, chest.Y));
        SendAt(r.Deck, UiEventType.MouseUp, (chest.X + 50f, chest.Y));
        Assert.False(r.Deck.FigurePose.IsDefault);
        Click(r.Deck, r.Deck.Geometry.FigureLock);
        Assert.False(r.Session.Settings.FigureLocked);
        Assert.True(r.Deck.FigurePose.IsDefault);
    }

    [Fact]
    public void TheLockTooltip_NamesTheState()
    {
        Rig r = Make();
        DeckRect lockStud = r.Deck.Geometry.FigureLock;
        Send(r.Deck, UiEventType.MouseMove, lockStud);
        r.Now = 10;
        Assert.Equal("Unlocked: drag gear off", r.Deck.VisibleTooltip?.Title);
        Click(r.Deck, lockStud);
        Send(r.Deck, UiEventType.MouseMove, r.Deck.Geometry.SlotsStud);
        Send(r.Deck, UiEventType.MouseMove, lockStud);
        r.Now = 20;
        Assert.Equal("Locked: drag to turn", r.Deck.VisibleTooltip?.Title);
    }

    private static void DoubleClickAt(UiGearDeck deck, (float X, float Y) at)
    {
        SendAt(deck, UiEventType.MouseDown, at);
        SendAt(deck, UiEventType.MouseUp, at);
        SendAt(deck, UiEventType.Click, at);
        SendAt(deck, UiEventType.MouseDown, at);
        SendAt(deck, UiEventType.MouseUp, at);
        SendAt(deck, UiEventType.Click, at);
        SendAt(deck, UiEventType.DoubleClick, at);
    }

    private static int SlotIndex(EquipMask mask)
    {
        for (int i = 0; i < GearSlotCatalog.Slots.Count; i++)
            if (GearSlotCatalog.Slots[i].Mask == mask) return i;
        throw new InvalidOperationException();
    }

    [Fact]
    public void HoveringTheHeadSlot_HighlightsTheCap_NotTheShirt()
    {
        Rig r = Make();
        DeckRect head = r.Deck.Geometry.EquipSlot(SlotIndex(EquipMask.HeadWear));
        Send(r.Deck, UiEventType.MouseMove, head);
        Assert.Equal(EquipMask.HeadWear, r.Deck.HoverFitMask);
        Assert.True(r.Deck.HoverFits(Cap));
        Assert.False(r.Deck.HoverFits(Shirt));
        Assert.False(r.Deck.HoverFits(Helm));   // worn, not a pack item
    }

    [Fact]
    public void HoveringTheChest_OnTheFigure_HighlightsTheShirt()
    {
        Rig r = Make();
        SendAt(r.Deck, UiEventType.MouseMove, MapPoint(r.Deck, 50, 80));
        Assert.True(r.Deck.HoverFits(Shirt));
        Assert.False(r.Deck.HoverFits(Cap));
    }

    [Fact]
    public void SlotsView_HighlightsToo_AndLeavingClears()
    {
        Rig r = Make(attach: false);
        DeckRect head = r.Deck.Geometry.EquipSlot(SlotIndex(EquipMask.HeadWear));
        Send(r.Deck, UiEventType.MouseMove, head);
        Assert.True(r.Deck.HoverFits(Cap));
        r.Deck.OnEvent(new UiEvent(0, r.Deck, UiEventType.HoverLeave));
        Assert.Equal(EquipMask.None, r.Deck.HoverFitMask);
        Assert.False(r.Deck.HoverFits(Cap));
    }
}
