using System.Collections.Generic;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public sealed class UiContextMenuRoutingTests
{
    private sealed class RecordingMenu : IUiContextMenu
    {
        public bool Result { get; set; } = true;
        public List<(int X, int Y)> Opens { get; } = new();
        public bool TryOpen(int screenX, int screenY)
        {
            Opens.Add((screenX, screenY));
            return Result;
        }
    }

    private sealed class Box : UiElement
    {
        public bool HandlesRight { get; set; }
        public override bool HandlesClick => true;
        public override bool OnEvent(in UiEvent e) =>
            HandlesRight && e.Type == UiEventType.RightClick;
    }

    // outer (0,0 200x200) > inner (50,50 50x50)
    private static (UiRoot Root, Box Outer, Box Inner) Tree()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var outer = new Box { Left = 0, Top = 0, Width = 200, Height = 200 };
        var inner = new Box { Left = 50, Top = 50, Width = 50, Height = 50 };
        outer.AddChild(inner);
        root.AddChild(outer);
        return (root, outer, inner);
    }

    private static void RightClick(UiRoot root, int x, int y, int releaseX = -1)
    {
        root.OnMouseDown(UiMouseButton.Right, x, y);
        root.OnMouseUp(UiMouseButton.Right, releaseX < 0 ? x : releaseX, y);
    }

    [Fact]
    public void RightClick_OpensTheInnermostMenu()
    {
        var (root, outer, inner) = Tree();
        var outerMenu = new RecordingMenu();
        var innerMenu = new RecordingMenu();
        outer.ContextMenu = outerMenu;
        inner.ContextMenu = innerMenu;
        RightClick(root, 60, 70);
        Assert.Equal(new[] { (60, 70) }, innerMenu.Opens);
        Assert.Empty(outerMenu.Opens);
    }

    [Fact]
    public void ChildWithoutAMenu_PassesToItsParentsMenu()
    {
        var (root, outer, _) = Tree();
        var outerMenu = new RecordingMenu();
        outer.ContextMenu = outerMenu;
        RightClick(root, 60, 70);
        Assert.Equal(new[] { (60, 70) }, outerMenu.Opens);
    }

    [Fact]
    public void ElementHandlingTheRightClick_OpensNoMenu()
    {
        var (root, outer, inner) = Tree();
        var outerMenu = new RecordingMenu();
        outer.ContextMenu = outerMenu;
        inner.HandlesRight = true;
        RightClick(root, 60, 70);
        Assert.Empty(outerMenu.Opens);
    }

    [Fact]
    public void MenuWithNothingToShow_PassesToTheNextMenuOut()
    {
        var (root, outer, inner) = Tree();
        var outerMenu = new RecordingMenu();
        var innerMenu = new RecordingMenu { Result = false };
        outer.ContextMenu = outerMenu;
        inner.ContextMenu = innerMenu;
        RightClick(root, 60, 70);
        Assert.Single(innerMenu.Opens);
        Assert.Single(outerMenu.Opens);
    }

    [Fact]
    public void RightDragAndLeftClick_OpenNothing()
    {
        var (root, outer, _) = Tree();
        var menu = new RecordingMenu();
        outer.ContextMenu = menu;
        RightClick(root, 10, 10, releaseX: 30);
        root.OnMouseDown(UiMouseButton.Left, 10, 10);
        root.OnMouseUp(UiMouseButton.Left, 10, 10);
        Assert.Empty(menu.Opens);
    }

    [Fact]
    public void HidingAnOpenPopupsOwner_DismissesThePopup()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var panel = new UiPanel { Left = 0, Top = 0, Width = 300, Height = 300 };
        var menu = new UiMenu
        {
            Left = 10, Top = 20, Width = 46, Height = 20, OpenUpward = false,
            Items = new[] { new UiMenu.MenuItem("A", "a") },
        };
        panel.AddChild(menu);
        root.AddChild(panel);
        root.OnMouseDown(UiMouseButton.Left, 30, 30);
        root.OnMouseUp(UiMouseButton.Left, 30, 30);
        Assert.True(menu.IsOpen);

        panel.Visible = false;
        Assert.False(menu.IsOpen);
        panel.Visible = true;
        Assert.False(menu.IsOpen);
    }
}
