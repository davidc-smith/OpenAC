using System.Linq;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

/// <summary>A menu opens on the side of its button with room for the list, and shortens the list when neither side has it.</summary>
public sealed class UiMenuFlipTests
{
    private static (UiRoot root, UiMenu menu) Make(float rootH, float top, bool preferUp, out string? picked, int items = 4)
    {
        string? got = null;
        var menu = new UiMenu
        {
            Left = 0, Top = top, Width = 100f, Height = 20f, ColumnWidth = 100f,
            FitsLongLabels = true, RetailButtonArt = false, Scrollable = true,
            PopupScrollbarHideWhenDisabled = true,
            OpenUpward = preferUp, RowsPerColumn = 4, RowHeight = 18f, TextIndent = 6f,
            Items = Enumerable.Range(0, items).Select(i => new UiMenu.MenuItem($"I{i}", $"I{i}")).ToArray(),
        };
        string?[] box = new string?[1];
        menu.OnSelect = p => box[0] = p as string;
        var root = new UiRoot { Width = 800, Height = rootH };
        root.AddChild(menu);
        picked = null;
        _ = got;
        _box = box;
        return (root, menu);
    }
    private static string?[] _box = new string?[1];

    private static void Click(UiRoot root, int x, int y)
    {
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
    }

    [Fact]
    public void NearBottom_OpensUpward()
    {
        var (root, menu) = Make(200, 170, preferUp: false, out _);
        Click(root, 10, 175);
        Assert.True(menu.IsOpen);
        Assert.True(menu.OpensUpward);
    }

    [Fact]
    public void WithRoom_OpensDownwardAsBefore()
    {
        var (root, menu) = Make(200, 10, preferUp: false, out _);
        Click(root, 10, 15);
        Assert.True(menu.IsOpen);
        Assert.False(menu.OpensUpward);
    }

    [Fact]
    public void PreferredUp_FlipsDownWhenNoRoomAbove()
    {
        var (root, menu) = Make(200, 10, preferUp: true, out _);
        Click(root, 10, 15);
        Assert.False(menu.OpensUpward);
    }

    [Fact]
    public void TooLittleRoomEitherWay_ClampsToTheLargerSide()
    {
        // Below the button: 30. Above: 50. The 4-row list needs 82.
        var (root, menu) = Make(100, 50, preferUp: false, out _);
        Click(root, 10, 55);
        Assert.True(menu.OpensUpward);
        Assert.True(menu.PopupOuterHeight <= 50f, $"popup {menu.PopupOuterHeight} overruns 50");
        Assert.True(menu.PopupOuterHeight >= 28f);
    }

    [Fact]
    public void TooLittleRoomEitherWay_LargerSideBelow()
    {
        var (root, menu) = Make(100, 20, preferUp: true, out _);
        Click(root, 10, 25);
        Assert.False(menu.OpensUpward);
        Assert.True(menu.PopupOuterHeight <= 60f);
    }

    [Fact]
    public void ClickOnItemInFlippedList_Selects()
    {
        var (root, menu) = Make(200, 170, preferUp: false, out _);
        Click(root, 10, 175);
        // outer 82; popup spans 88..170; item 1 is at 88 + 5 + 18 .. +18.
        Click(root, 10, 88 + 5 + 18 + 9);
        Assert.Equal("I1", _box[0]);
        Assert.False(menu.IsOpen);
    }
}
