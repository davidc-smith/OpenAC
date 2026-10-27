using System.Numerics;
using AcDream.App.Rendering;
using AcDream.App.Spells;
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.Core.Spells;
using AcDream.UI.Abstractions.Input;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiSpellDeckTests
{
    private sealed class Game : ISpellDeckActions
    {
        public readonly List<uint>[] Tabs = Enumerable.Range(0, 8).Select(_ => new List<uint>()).ToArray();
        public readonly List<string> Calls = [];
        public ItemSpell? Item;
        public void CastSpell(uint id) => Calls.Add($"cast {id}");
        public void CastItemSpell(uint itemId) => Calls.Add($"item {itemId}");
        public void SetFavorite(int tab, int pos, uint id) => Calls.Add($"set {tab} {pos} {id}");
        public void RemoveFavorite(int tab, uint id) => Calls.Add($"remove {tab} {id}");
        public void ExamineSpell(uint id) => Calls.Add($"examine {id}");
        public void ShowMessage(string text) => Calls.Add("msg " + text);
    }

    private sealed class Rig
    {
        public required UiSpellDeck Deck;
        public required SpellDeckController Controller;
        public required Game Game;
        public double Now = 1.0;
        public uint? Pending;
    }

    private static uint Icon(uint spellId) => 0x0600_0000u + spellId;

    private static SpellMetadata Spell(uint id, string name, string school) => new(
        id, name, school, 0u, Icon(id), string.Empty, 0f, 0, false, false, string.Empty,
        0, 0, 0u, 0, false, false, false, 0f, 0u, 0u, 0u, 0);

    private static readonly Dictionary<uint, SpellMetadata> Spells = new()
    {
        [10] = Spell(10, "Flame Bolt I", "War Magic"),
        [11] = Spell(11, "Frost Bolt I", "War Magic"),
        [12] = Spell(12, "Heal Self I", "Life Magic"),
        [20] = Spell(20, "Strength Self I", "Creature Enchantment"),
        [30] = Spell(30, "Blade Blessing", "Item Enchantment"),
    };

    private static Rig Make(bool clickCasts = false, Func<uint, string?>? itemName = null)
    {
        var g = new Game();
        g.Tabs[0].AddRange([10u, 11u, 12u]);
        g.Tabs[1].AddRange([20u]);
        var c = new SpellDeckController(
            g, favorites: tab => g.Tabs[tab], itemSpell: () => g.Item,
            capacity: () => 12, settings: SpellDeckSettings.Default, save: _ => { },
            clickCasts: () => clickCasts, canAddRow: () => true);
        Rig? rig = null;
        var deck = new UiSpellDeck(
            c,
            spellIcon: Icon,
            spellInfo: id => Spells.GetValueOrDefault(id),
            keyLabel: a => a == InputAction.UseSpellSlot_1 ? "1" : string.Empty,
            pendingSpell: () => rig!.Pending,
            clock: () => rig!.Now,
            isLocked: () => false,
            toggleClickCasts: () => { },
            setAttached: _ => { },
            itemName: itemName);
        rig = new Rig { Deck = deck, Controller = c, Game = g };
        deck.Refresh();
        return rig;
    }

    private static void Send(UiSpellDeck d, int type, DeckRect r) =>
        d.OnEvent(new UiEvent(0, d, type, Data1: (int)(r.X + r.W / 2), Data2: (int)(r.Y + r.H / 2)));

    [Fact]
    public void GeometryFollowsTheControllersRows()
    {
        Rig r = Make();
        Assert.Equal(3, r.Deck.Geometry.Rows);
        Assert.Equal(r.Deck.Geometry.Width, r.Deck.Width);
        Assert.Equal(r.Deck.Geometry.Height, r.Deck.Height);

        r.Controller.AddRow();
        r.Deck.Refresh();
        Assert.Equal(4, r.Deck.Geometry.Rows);
        Assert.Equal(SpellDeckGeometry.Create(1f, 1f, 4).Height, r.Deck.Height);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void OnePhysicalDoubleClickCastsByClickMode(bool clickCasts, int casts)
    {
        Rig r = Make(clickCasts);
        DeckRect slot = r.Deck.Geometry.Slot(0, 1);
        Send(r.Deck, UiEventType.Click, slot);
        Send(r.Deck, UiEventType.Click, slot);
        Send(r.Deck, UiEventType.DoubleClick, slot);
        Assert.Equal(Enumerable.Repeat("cast 11", casts), r.Game.Calls);
        Assert.Equal(SpellDeckMark.Spell(0, 11u), r.Controller.Outline);
    }

    [Fact]
    public void ItemClickOutlinesAndDoubleClickCasts()
    {
        Rig r = Make();
        r.Game.Item = new ItemSpell(0x5000_0001u, 30u);
        r.Deck.Refresh();
        DeckRect item = r.Deck.Geometry.ItemSlot;
        Send(r.Deck, UiEventType.Click, item);
        Assert.Equal(SpellDeckMark.Item, r.Controller.Outline);
        Assert.Empty(r.Game.Calls);
        Send(r.Deck, UiEventType.DoubleClick, item);
        Assert.Equal([$"item {0x5000_0001u}"], r.Game.Calls);
    }

    private static DeckRect Mark(Rig r, int tab) => r.Deck.Geometry.Bookmark(tab, r.Controller.TabCount);
    private static DeckRect Plus(Rig r) => r.Deck.Geometry.Plus(r.Controller.TabCount);

    [Fact]
    public void ABookmarkClickShowsThatTab()
    {
        Rig r = Make();
        Send(r.Deck, UiEventType.Click, Mark(r, 1));
        Assert.Equal(1, r.Controller.Tab);
        Send(r.Deck, UiEventType.Click, Mark(r, 1));
        Assert.Equal(1, r.Controller.Tab);   // not a step: that tab again
        Send(r.Deck, UiEventType.DoubleClick, Mark(r, 1));
        Send(r.Deck, UiEventType.Click, Mark(r, 7));
        Assert.Equal(7, r.Controller.Tab);
        Assert.Empty(r.Game.Calls);
    }

    [Fact]
    public void APlusClickAddsALocalTabAndShowsIt()
    {
        Rig r = Make();
        Send(r.Deck, UiEventType.Click, Plus(r));
        Assert.Equal(9, r.Controller.TabCount);
        Assert.Equal(8, r.Controller.Tab);
        Assert.Empty(r.Game.Calls);
    }

    [Fact]
    public void TheStripFollowsTheTabCount_AndThePlusGoesAt16()
    {
        Rig r = Make();
        for (int i = 0; i < 8; i++) Send(r.Deck, UiEventType.Click, Plus(r));
        Assert.Equal(16, r.Controller.TabCount);
        DeckRect plus = r.Deck.Geometry.Plus(16);
        Assert.False(r.Deck.OnEvent(new UiEvent(0, r.Deck, UiEventType.Click,
            Data1: (int)(plus.X + plus.W / 2), Data2: (int)(plus.Y + plus.H / 2))));
        Assert.Equal(16, r.Controller.TabCount);
        Send(r.Deck, UiEventType.Click, Mark(r, 15));
        Assert.Equal(15, r.Controller.Tab);
        Send(r.Deck, UiEventType.Click, Mark(r, 3));
        Assert.Equal(3, r.Controller.Tab);
    }

    [Fact]
    public void ClicksOnTheFrameDoNothing()
    {
        Rig r = Make();
        DeckRect divider = r.Deck.Geometry.Divider;
        Assert.False(r.Deck.OnEvent(new UiEvent(0, r.Deck, UiEventType.Click, Data1: (int)divider.X, Data2: (int)(divider.Y + 4))));
        Assert.Empty(r.Game.Calls);
    }

    [Fact]
    public void HandlesClickOnlyOverControls()
    {
        Rig r = Make();
        SpellDeckGeometry g = r.Deck.Geometry;
        DeckRect s = g.Slot(0, 0), mark = g.Bookmark(2, 8), plus = g.Plus(8), item = g.ItemSlot;
        Assert.True(r.Deck.HitTestLocal(s.X + 2, s.Y + 2));
        Assert.True(r.Deck.HandlesClick);
        Assert.True(r.Deck.HitTestLocal(mark.X + 2, mark.Y + 2));
        Assert.True(r.Deck.HandlesClick);
        Assert.True(r.Deck.HitTestLocal(plus.X + 2, plus.Y + 2));
        Assert.True(r.Deck.HandlesClick);
        Assert.True(r.Deck.HitTestLocal(item.X + item.W / 2, item.Y + item.H / 2));
        Assert.True(r.Deck.HandlesClick);
        Assert.True(r.Deck.HitTestLocal(g.Divider.X, g.Divider.Y + 4));
        Assert.False(r.Deck.HandlesClick);
        Assert.False(r.Deck.CapturesPointerDrag);
        Assert.False(r.Deck.HitTestLocal(g.Width + 5, 4));
    }

    [Fact]
    public void SlotTooltipShowsNameSchoolAndKeyAfterTheDwell()
    {
        Rig r = Make();
        Send(r.Deck, UiEventType.MouseMove, r.Deck.Geometry.Slot(0, 0));
        Assert.Null(r.Deck.VisibleTooltip);
        r.Now += UiCommandDeck.TooltipDwell + 0.01;
        HudTooltipContent? tip = r.Deck.VisibleTooltip;
        Assert.NotNull(tip);
        Assert.Equal("Flame Bolt I", tip.Title);
        Assert.Equal("1", tip.State);
        Assert.Equal("War Magic", tip.Detail);

        Send(r.Deck, UiEventType.MouseDown, r.Deck.Geometry.Slot(0, 0));
        Assert.Null(r.Deck.VisibleTooltip);
        r.Deck.OnEvent(new UiEvent(0, r.Deck, UiEventType.HoverLeave));
        Assert.Null(r.Deck.VisibleTooltip);
    }

    [Fact]
    public void BookmarkPlusAndItemTooltips()
    {
        Rig r = Make();
        r.Game.Item = new ItemSpell(0x5000_0001u, 30u);
        r.Deck.Refresh();
        Send(r.Deck, UiEventType.MouseMove, Mark(r, 0));
        r.Now += 1;
        Assert.Equal(new HudTooltipContent("Tab 1", null, "3 spells", null), r.Deck.VisibleTooltip);

        Send(r.Deck, UiEventType.MouseMove, Mark(r, 1));
        r.Now += 1;
        Assert.Equal(new HudTooltipContent("Tab 2", null, "1 spell", "Click to show"), r.Deck.VisibleTooltip);

        Send(r.Deck, UiEventType.MouseMove, Mark(r, 5));
        r.Now += 1;
        Assert.Equal(new HudTooltipContent("Tab 6", null, "No spells", "Click to show"), r.Deck.VisibleTooltip);

        Send(r.Deck, UiEventType.MouseMove, Plus(r));
        r.Now += 1;
        Assert.Equal(new HudTooltipContent("Add a tab", null, "Tabs 9-16 are saved on this PC only.", null),
            r.Deck.VisibleTooltip);

        r.Controller.AddLocalTab();
        r.Controller.ShowTab(0);
        Send(r.Deck, UiEventType.MouseMove, Mark(r, 8));
        r.Now += 1;
        Assert.Equal(new HudTooltipContent("Tab 9", "Local tab", "No spells", "Click to show, right-click to remove"),
            r.Deck.VisibleTooltip);

        Send(r.Deck, UiEventType.MouseMove, r.Deck.Geometry.ItemSlot);
        r.Now += 1;
        HudTooltipContent? tip = r.Deck.VisibleTooltip;
        Assert.NotNull(tip);
        Assert.Equal("Item spell", tip.Title);
        Assert.Equal("Blade Blessing", tip.Detail);
    }

    [Fact]
    public void ItemTooltip_NamesTheItemAndItsSpell()
    {
        Rig r = Make(itemName: id => id == 0x5000_0001u ? "Ivory Wand" : null);
        r.Game.Item = new ItemSpell(0x5000_0001u, 30u);
        r.Deck.Refresh();
        Send(r.Deck, UiEventType.MouseMove, r.Deck.Geometry.ItemSlot);
        r.Now += 1;
        Assert.Equal(new HudTooltipContent("Ivory Wand (Blade Blessing)", null, null, "Double-click to cast"),
            r.Deck.VisibleTooltip);
    }

    [Fact]
    public void DrawsOneSpritePerVisibleSpell()
    {
        Rig r = Make();
        TextRenderer renderer = MarkupDrawProbe.Draw(r.Deck, r.Deck.Width, r.Deck.Height);

        var sprites = new Dictionary<uint, int>();
        foreach (var s in renderer.DebugSpriteSegmentVerts)
            if (s.Texture != 0u)
                sprites[s.Texture] = sprites.GetValueOrDefault(s.Texture) + s.Verts.Count / 48;
        // Every row shows more of tab 0: row 0 holds all three of its spells; tab 1 is not shown.
        Assert.Equal(new Dictionary<uint, int> { [Icon(10)] = 1, [Icon(11)] = 1, [Icon(12)] = 1 }, sprites);
    }

    [Fact]
    public void DrawsTheGoldOutlineAroundTheOutlinedSlot()
    {
        Rig r = Make();
        r.Controller.Click(0, 2);
        TextRenderer renderer = MarkupDrawProbe.Draw(r.Deck, r.Deck.Width, r.Deck.Height);

        var b = Bounds(renderer, DeckChrome.Handle);
        Assert.NotNull(b);
        DeckRect s = r.Deck.Geometry.Slot(0, 2);
        AssertAround(b.Value, s, inside: 1f, outside: 4f);
    }

    [Fact]
    public void DrawsTheBlueGlowOnTheCastingSlot()
    {
        Rig r = Make();
        Assert.Null(Bounds(MarkupDrawProbe.Draw(r.Deck, r.Deck.Width, r.Deck.Height), DeckChrome.CastLit));

        r.Game.Tabs[0].AddRange(Enumerable.Range(100, 10).Select(i => (uint)i));   // the 13th spell, 109, is row 1's first
        r.Pending = 109u;
        var b = Bounds(MarkupDrawProbe.Draw(r.Deck, r.Deck.Width, r.Deck.Height), DeckChrome.CastLit);
        Assert.NotNull(b);
        AssertAround(b.Value, r.Deck.Geometry.Slot(1, 0), inside: 3f, outside: 6f);
    }

    [Theory]
    [InlineData(0.75f, 1f)]
    [InlineData(1f, 1f)]
    [InlineData(1.5f, 2f)]
    [InlineData(1f, 2f)]
    public void DrawsTheBookmarks_TheCurrentOneLit_ThenThePlus(float scale, float stretch)
    {
        Rig r = Make();
        r.Controller.ShowTab(3);
        r.Deck.ApplyGeometry(SpellDeckGeometry.Create(scale, stretch, 3));
        TextRenderer renderer = MarkupDrawProbe.Draw(r.Deck, r.Deck.Width, r.Deck.Height);
        SpellDeckGeometry g = r.Deck.Geometry;

        var lit = Bounds(renderer, UiSpellDeck.TabLit);
        Assert.NotNull(lit);
        AssertAround(lit.Value, g.Bookmark(3, 8), inside: -1f, outside: 1f);

        var off = Bounds(renderer, UiSpellDeck.TabOff);
        Assert.NotNull(off);
        DeckRect first = g.Bookmark(0, 8), plus = g.Plus(8);
        Assert.InRange(off.Value.X0, first.X - 1f, first.X + 1f);
        Assert.InRange(off.Value.X1, plus.X + plus.W - 1f, plus.X + plus.W + 1f);
        Assert.True(off.Value.Y0 >= first.Y - 1f && off.Value.Y1 <= first.Y + first.H + 1f);

        var cross = Bounds(renderer, UiSpellDeck.PlusInk);
        Assert.NotNull(cross);
        Assert.True(cross.Value.X0 > plus.X && cross.Value.X1 < plus.X + plus.W);
        Assert.True(cross.Value.Y0 > plus.Y && cross.Value.Y1 < plus.Y + plus.H);
    }

    [Fact]
    public void ThePlusIsNotDrawnAt16Tabs()
    {
        Rig r = Make();
        for (int i = 0; i < 8; i++) r.Controller.AddLocalTab();
        TextRenderer renderer = MarkupDrawProbe.Draw(r.Deck, r.Deck.Width, r.Deck.Height);
        Assert.Null(Bounds(renderer, UiSpellDeck.PlusInk));
        var lit = Bounds(renderer, UiSpellDeck.TabLit);
        Assert.NotNull(lit);
        AssertAround(lit.Value, r.Deck.Geometry.Bookmark(15, 16), inside: -1f, outside: 1f);
    }

    [Theory]
    [InlineData(0.75f)]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void AttachedDrawsSquareBottomCorners_DetachedRounded(float scale)
    {
        Rig r = Make();
        r.Deck.ApplyGeometry(SpellDeckGeometry.Create(scale, 1f, 2));
        Assert.True(r.Controller.Attached);
        Assert.True(r.Deck.SquareBottomCorners);
        Assert.True(CornerFilled(r, bottom: true));
        Assert.False(CornerFilled(r, bottom: false));

        r.Controller.SetAttached(false);
        Assert.False(r.Deck.SquareBottomCorners);
        Assert.False(CornerFilled(r, bottom: true));
    }

    /// <summary>Whether the frame's dark edge reaches into the very corner (bottom-left or top-left).
    /// The draw is clipped to the deck, so a square corner leaves a vertex on the corner itself.</summary>
    private static bool CornerFilled(Rig r, bool bottom)
    {
        TextRenderer renderer = MarkupDrawProbe.Draw(r.Deck, r.Deck.Width, r.Deck.Height);
        float h = r.Deck.Height;
        Vector4 c = DeckChrome.FrameEdge;
        foreach (var s in renderer.DebugSpriteSegmentVerts)
        {
            if (s.Texture != 0u) continue;
            for (int i = 0; i + 8 <= s.Verts.Count; i += 8)
            {
                if (s.Verts[i + 7] <= 0f || MathF.Abs(s.Verts[i + 4] - c.X) > 0.01f
                    || MathF.Abs(s.Verts[i + 5] - c.Y) > 0.01f || MathF.Abs(s.Verts[i + 6] - c.Z) > 0.01f)
                    continue;
                float x = s.Verts[i], y = s.Verts[i + 1];
                if (x < 0.25f && (bottom ? y > h - 0.25f : y < 0.25f)) return true;
            }
        }
        return false;
    }

    [Theory]
    [InlineData(0.75f, 1f)]
    [InlineData(1f, 1f)]
    [InlineData(1.5f, 2f)]
    public void DrawsInsideTheDeckAtEveryScale(float scale, float stretch)
    {
        Rig r = Make();
        r.Game.Item = new ItemSpell(1u, 30u);
        r.Pending = 11u;
        r.Controller.Click(0, 0);
        r.Deck.Refresh();
        r.Deck.ApplyGeometry(SpellDeckGeometry.Create(scale, stretch, 3));
        TextRenderer renderer = MarkupDrawProbe.Draw(r.Deck, r.Deck.Width, r.Deck.Height);
        int quads = 0;
        foreach (var s in renderer.DebugSpriteSegmentVerts)
            for (int i = 0; i + 8 <= s.Verts.Count; i += 8)
            {
                quads++;
                Assert.InRange(s.Verts[i], -3.01f, r.Deck.Width + 3.01f);
                Assert.InRange(s.Verts[i + 1], -3.01f, r.Deck.Height + 3.01f);
            }
        Assert.True(quads > 0);
    }

    [Fact]
    public void ARestoredNarrowSizeIsWidenedSoTheRowsHoldTheFullestTab()
    {
        Rig r = Make();
        r.Game.Tabs[1].AddRange(Enumerable.Range(100, 47).Select(i => (uint)i));   // tab 1 (not shown) holds 48: 16 a row
        float min = SpellDeckGeometry.StretchFor(16);
        Assert.True(min > 1f);

        // A restore puts back a size saved before the row grew: stretch 1.05, scale 1.
        SpellDeckGeometry saved = SpellDeckGeometry.Create(1f, 1.05f, 3);
        Assert.True(saved.Stretch < min);
        r.Deck.Width = saved.Width;
        r.Deck.Height = saved.Height;
        r.Deck.Refresh();

        Assert.Equal(min, r.Deck.Geometry.Stretch, 4);
        Assert.Equal(1f, r.Deck.Geometry.Scale, 4);
        Assert.Equal(r.Deck.Geometry.Width, r.Deck.Width);
        Assert.True(r.Deck.Geometry.SlotsPerRow >= 16);
    }

    [Fact]
    public void MountOptionsCoverEverySize()
    {
        var o = UiSpellDeck.MountOptions(10f, 20f);
        Assert.Equal(WindowNames.SpellDeck, o.WindowName);
        Assert.Equal(ResizeEdges.Right, o.ResizableEdges);
        Assert.False(o.Visible);
        Assert.False(o.ContentClickThrough);
        foreach (int rows in new[] { 1, 10 })
            foreach (float scale in new[] { 0.75f, 1.5f })
                foreach (float stretch in new[] { 1f, 2f })
                {
                    var g = SpellDeckGeometry.Create(scale, stretch, rows);
                    Assert.InRange(g.Width, o.MinWidth ?? float.NaN, o.MaxWidth ?? float.NaN);
                    Assert.InRange(g.Height, o.MinHeight ?? float.NaN, o.MaxHeight ?? float.NaN);
                }
    }

    /// <summary>The bounds of every untextured vertex drawn in <paramref name="color"/>'s RGB (any alpha above zero).</summary>
    private static (float X0, float Y0, float X1, float Y1)? Bounds(TextRenderer renderer, Vector4 color)
    {
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        bool any = false;
        foreach (var s in renderer.DebugSpriteSegmentVerts)
        {
            if (s.Texture != 0u) continue;
            for (int i = 0; i + 8 <= s.Verts.Count; i += 8)
            {
                if (s.Verts[i + 7] <= 0f
                    || MathF.Abs(s.Verts[i + 4] - color.X) > 0.01f
                    || MathF.Abs(s.Verts[i + 5] - color.Y) > 0.01f
                    || MathF.Abs(s.Verts[i + 6] - color.Z) > 0.01f)
                    continue;
                any = true;
                x0 = MathF.Min(x0, s.Verts[i]); x1 = MathF.Max(x1, s.Verts[i]);
                y0 = MathF.Min(y0, s.Verts[i + 1]); y1 = MathF.Max(y1, s.Verts[i + 1]);
            }
        }
        return any ? (x0, y0, x1, y1) : null;
    }

    /// <summary>Bounds lie between the slot grown by <paramref name="inside"/> and by <paramref name="outside"/>.</summary>
    private static void AssertAround((float X0, float Y0, float X1, float Y1) b, DeckRect s, float inside, float outside)
    {
        Assert.InRange(b.X0, s.X - outside, s.X - inside);
        Assert.InRange(b.Y0, s.Y - outside, s.Y - inside);
        Assert.InRange(b.X1, s.X + s.W + inside, s.X + s.W + outside);
        Assert.InRange(b.Y1, s.Y + s.H + inside, s.Y + s.H + outside);
    }
}
