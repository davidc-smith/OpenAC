using System;
using System.Collections.Generic;
using AcDream.App.UI;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

public sealed class UiMarkupContextMenuTests
{
    private sealed class Owner : UiElement
    {
        public int MouseDowns;
        public override bool HandlesClick => true;
        public override bool OnEvent(in UiEvent e)
        {
            if (e.Type == UiEventType.MouseDown && ReferenceEquals(e.Target, this)) MouseDowns++;
            return e.Type == UiEventType.MouseDown;
        }
    }

    private sealed class Focusable : UiElement
    {
        public Focusable() { AcceptsFocus = true; }
    }

    private sealed class Mounted
    {
        public required UiRoot Root { get; init; }
        public required Owner Owner { get; init; }
        public required UiMarkupContextMenu Menu { get; init; }
        public List<int> Picks { get; } = new();
    }

    // The owner covers the whole root so any point opens the menu.
    private static Mounted Mount(string[] items, bool[]? enabled = null, int width = 800, int height = 600)
    {
        var root = new UiRoot { Width = width, Height = height };
        var owner = new Owner { Left = 0, Top = 0, Width = width, Height = height };
        var menu = new UiMarkupContextMenu
        {
            ItemsSource = () => items,
            ItemsEnabledSource = enabled is null ? null : () => enabled,
        };
        var mounted = new Mounted { Root = root, Owner = owner, Menu = menu };
        menu.Selected = mounted.Picks.Add;
        owner.AddChild(menu);
        owner.ContextMenu = menu;
        root.AddChild(owner);
        return mounted;
    }

    private static readonly string[] CopyPaste = { "Copy", "-", "Paste" };

    private static void RightClick(UiRoot root, int x, int y)
    {
        root.OnMouseDown(UiMouseButton.Right, x, y);
        root.OnMouseUp(UiMouseButton.Right, x, y);
    }

    private static void Click(UiRoot root, int x, int y)
    {
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
    }

    [Fact]
    public void RightClick_OpensAtTheCursor()
    {
        var m = Mount(CopyPaste);
        RightClick(m.Root, 150, 120);
        Assert.True(m.Menu.IsOpen);
        Assert.Equal((150f, 120f, 80f, 49f), m.Menu.PopupScreenRect);   // 3 + 18 + 7 + 18 + 3
    }

    [Fact]
    public void NearTheRightAndBottomEdges_Flips()
    {
        var m = Mount(CopyPaste);
        RightClick(m.Root, 790, 590);
        Assert.Equal((710f, 541f, 80f, 49f), m.Menu.PopupScreenRect);
    }

    [Fact]
    public void TallerThanTheScreen_ShiftsOntoIt()
    {
        var m = Mount(CopyPaste, height: 40);
        RightClick(m.Root, 10, 30);
        Assert.Equal(0f, m.Menu.PopupScreenRect.Y);
    }

    [Fact]
    public void ClickingAnItem_ClosesAndReportsItsIndex()
    {
        var m = Mount(CopyPaste);
        int indexWhenPicked = -2;
        m.Menu.Selected = i => { indexWhenPicked = m.Menu.IsOpen ? -1 : i; };
        RightClick(m.Root, 100, 100);
        Click(m.Root, 110, 135);          // Paste: 100 + 3 + 18 + 7 = 128 .. 146
        Assert.Equal(2, indexWhenPicked);  // closed before the callback
        Assert.False(m.Menu.IsOpen);
    }

    [Fact]
    public void SeparatorAndDisabledItems_DoNothingAndStayOpen()
    {
        var m = Mount(CopyPaste, enabled: new[] { true, true, false });
        RightClick(m.Root, 100, 100);
        Click(m.Root, 110, 124);          // separator 121 .. 128
        Click(m.Root, 110, 135);          // disabled Paste
        Assert.Empty(m.Picks);
        Assert.True(m.Menu.IsOpen);
    }

    [Fact]
    public void PressOutside_ClosesAndIsSwallowed()
    {
        var m = Mount(CopyPaste);
        RightClick(m.Root, 100, 100);
        Click(m.Root, 500, 500);
        Assert.False(m.Menu.IsOpen);
        Assert.Equal(0, m.Owner.MouseDowns);
    }

    [Fact]
    public void EmptyItems_DoNotOpen()
    {
        var m = Mount(Array.Empty<string>());
        Assert.False(m.Menu.TryOpen(100, 100));
        Assert.False(m.Menu.IsOpen);
    }

    [Fact]
    public void Opening_GetsTheOwnerPoint_BeforeTheItemsAreRead()
    {
        string[] items = Array.Empty<string>();
        var root = new UiRoot { Width = 800, Height = 600 };
        var owner = new Owner { Left = 100, Top = 100, Width = 200, Height = 50 };
        var menu = new UiMarkupContextMenu { ItemsSource = () => items };
        (float X, float Y) point = default;
        menu.Opening = (x, y) => { point = (x, y); items = new[] { "Only" }; };
        owner.AddChild(menu);
        owner.ContextMenu = menu;
        root.AddChild(owner);
        RightClick(root, 150, 120);
        Assert.Equal((50f, 20f), point);
        Assert.True(menu.IsOpen);
    }

    [Fact]
    public void Keys_SkipSeparatorsAndDisabledItems_AndWrap()
    {
        var m = Mount(new[] { "A", "-", "B", "C" }, enabled: new[] { true, true, false, true });
        RightClick(m.Root, 100, 100);
        m.Root.OnKeyDown((int)Key.Down);
        Assert.Equal(0, m.Menu.HighlightForTest);
        m.Root.OnKeyDown((int)Key.Down);
        Assert.Equal(3, m.Menu.HighlightForTest);
        m.Root.OnKeyDown((int)Key.Down);
        Assert.Equal(0, m.Menu.HighlightForTest);
        m.Root.OnKeyDown((int)Key.Up);
        Assert.Equal(3, m.Menu.HighlightForTest);
        m.Root.OnKeyDown((int)Key.Enter);
        Assert.Equal(new[] { 3 }, m.Picks);
        Assert.False(m.Menu.IsOpen);
    }

    [Fact]
    public void UpWithNothingHighlighted_GoesToTheLastEnabledItem()
    {
        var m = Mount(new[] { "A", "B", "C" }, enabled: new[] { true, true, false });
        RightClick(m.Root, 100, 100);
        m.Root.OnKeyDown((int)Key.Up);
        Assert.Equal(1, m.Menu.HighlightForTest);
    }

    [Fact]
    public void Escape_Closes_AndGivesTheFocusBack()
    {
        var m = Mount(CopyPaste);
        var field = new Focusable { Left = 0, Top = 0, Width = 10, Height = 10 };
        m.Root.AddChild(field);
        m.Root.SetKeyboardFocus(field);
        RightClick(m.Root, 100, 100);
        Assert.Same(m.Menu, m.Root.KeyboardFocus);
        m.Root.OnKeyDown((int)Key.Escape);
        Assert.False(m.Menu.IsOpen);
        Assert.Same(field, m.Root.KeyboardFocus);
    }

    [Fact]
    public void WhileOpen_KeysDoNotReachTheGame_AndAPickLeavesNoFocus()
    {
        var m = Mount(CopyPaste);
        int fell = 0;
        m.Root.WorldKeyFallThrough += (_, _) => fell++;
        Assert.Null(m.Root.KeyboardFocus);
        RightClick(m.Root, 100, 100);
        m.Root.OnKeyDown((int)Key.W);
        Assert.Equal(0, fell);
        Click(m.Root, 110, 110);          // Copy
        Assert.Equal(new[] { 0 }, m.Picks);
        Assert.Null(m.Root.KeyboardFocus);
        m.Root.OnKeyDown((int)Key.W);
        Assert.Equal(1, fell);
    }

    [Fact]
    public void HidingTheOwner_ClosesIt()
    {
        var m = Mount(CopyPaste);
        RightClick(m.Root, 100, 100);
        m.Owner.Visible = false;
        Assert.False(m.Menu.IsOpen);
        m.Owner.Visible = true;
        Assert.False(m.Menu.IsOpen);
    }

    [Fact]
    public void DisablingTheOwner_ClosesItOnTheNextTick()
    {
        var m = Mount(CopyPaste);
        RightClick(m.Root, 100, 100);
        m.Owner.Enabled = false;
        m.Root.Tick(0.016, 1000);
        Assert.False(m.Menu.IsOpen);
    }

    [Fact]
    public void Hover_HighlightsEnabledItemsOnly()
    {
        var m = Mount(CopyPaste, enabled: new[] { true, true, false });
        RightClick(m.Root, 100, 100);
        m.Root.OnMouseMove(110, 110);     // Copy
        Assert.Equal(0, m.Menu.HighlightForTest);
        m.Root.OnMouseMove(110, 135);     // disabled Paste
        Assert.Equal(-1, m.Menu.HighlightForTest);
    }

    [Fact]
    public void OpeningHidesTheOwner_DoesNotOpenOrTakeTheFocus()
    {
        var m = Mount(CopyPaste);
        int fell = 0;
        m.Root.WorldKeyFallThrough += (_, _) => fell++;
        m.Menu.Opening = (_, _) => m.Owner.Visible = false;
        Assert.False(m.Menu.TryOpen(100, 100));
        Assert.False(m.Menu.IsOpen);
        Assert.Null(m.Root.KeyboardFocus);
        m.Root.OnKeyDown((int)Key.W);
        Assert.Equal(1, fell);
    }

    [Fact]
    public void OpeningRemovesTheOwner_DoesNotOpenOrTakeTheFocus()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var box = new UiPanel { Left = 0, Top = 0, Width = 800, Height = 600 };
        var owner = new Owner { Left = 0, Top = 0, Width = 800, Height = 600 };
        var menu = new UiMarkupContextMenu { ItemsSource = () => CopyPaste };
        var field = new Focusable { Left = 0, Top = 0, Width = 10, Height = 10 };
        owner.AddChild(menu);
        owner.ContextMenu = menu;
        box.AddChild(owner);
        root.AddChild(box);
        root.AddChild(field);
        root.SetKeyboardFocus(field);
        menu.Opening = (_, _) => box.RemoveChild(owner);
        Assert.False(menu.TryOpen(100, 100));
        Assert.False(menu.IsOpen);
        Assert.Same(field, root.KeyboardFocus);
    }

    [Fact]
    public void OpeningTwice_StillGivesTheFocusBackToTheFirstHolder()
    {
        var m = Mount(CopyPaste);
        var field = new Focusable { Left = 0, Top = 0, Width = 10, Height = 10 };
        m.Root.AddChild(field);
        m.Root.SetKeyboardFocus(field);
        Assert.True(m.Menu.TryOpen(100, 100));
        Assert.True(m.Menu.TryOpen(120, 120));
        m.Root.OnKeyDown((int)Key.Escape);
        Assert.False(m.Menu.IsOpen);
        Assert.Same(field, m.Root.KeyboardFocus);
    }

    [Fact]
    public void RemovingTheOwnerWhileOpen_ClosesAndLeavesNoFocusOnTheMenu()
    {
        var m = Mount(CopyPaste);
        RightClick(m.Root, 100, 100);
        m.Root.RemoveChild(m.Owner);
        Assert.False(m.Menu.IsOpen);
        Assert.NotSame(m.Menu, m.Root.KeyboardFocus);
    }

    [Fact]
    public void Draw_UsesClassicColours_OrThePalette()
    {
        var m = Mount(CopyPaste);
        RightClick(m.Root, 100, 100);
        var classic = MarkupDrawProbe.DrawOverlays(m.Root);
        Assert.Equal(1, MarkupDrawProbe.CountQuads(classic, UiMarkupContextMenu.ClassicBackground));
        Assert.Equal(1, MarkupDrawProbe.CountQuads(classic, UiMarkupContextMenu.ClassicSeparator));
        Assert.Equal(0, MarkupDrawProbe.CountQuads(classic, UiMarkupContextMenu.ClassicHighlight));

        m.Root.OnKeyDown((int)Key.Down);
        var keyed = MarkupDrawProbe.DrawOverlays(m.Root);
        Assert.Equal(1, MarkupDrawProbe.CountQuads(keyed, UiMarkupContextMenu.ClassicHighlight));

        m.Menu.ThemePalette = PluginUiPalette.Moss;
        var themed = MarkupDrawProbe.DrawOverlays(m.Root);
        Assert.Equal(0, MarkupDrawProbe.CountQuads(themed, UiMarkupContextMenu.ClassicBackground));
        Assert.True(MarkupDrawProbe.CountQuads(themed, PluginUiPalette.Moss.Field) >= 1);
    }
}
