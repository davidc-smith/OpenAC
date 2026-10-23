using System.Linq;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

/// <summary>
/// A menu with <see cref="UiMenu.FitsLongLabels"/> (every plugin markup
/// <c>&lt;menu&gt;</c>) widens its popup to its longest item, keeps the
/// scrollbar beside the text, shifts left at an edge, and shortens a long
/// button label. With no font the menu measures 7 points per character.
/// </summary>
public sealed class UiMenuFitItemsTests
{
    private const float Border = 5f;   // RetailChromeSprites.Border
    private const float Indent = 6f;
    private static readonly string LongLabel = new('x', 40);   // 280 points

    private static UiMenu MakeMenu(float width, params string[] labels) => new()
    {
        Width = width, Height = 20f,
        ColumnWidth = width,
        FitsLongLabels = true,
        RetailButtonArt = false,
        OpenUpward = false,
        Scrollable = true,
        PopupScrollbarHideWhenDisabled = true,
        RowsPerColumn = 4,
        RowHeight = 18f,
        TextIndent = Indent,
        Items = labels.Select(static l => new UiMenu.MenuItem(l, l)).ToArray(),
    };

    private static void Click(UiRoot root, int x, int y)
    {
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
    }

    [Fact]
    public void LongItem_WidensThePopupToFitIt()
    {
        var menu = MakeMenu(100f, "Short", LongLabel);
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(menu);

        Click(root, 10, 10);

        Assert.True(menu.IsOpen);
        float textRight = Border + UiMenu.PlainPadding + LongLabel.Length * 7f;
        Assert.True(menu.PopupOuterWidth >= textRight + UiMenu.PlainPadding + Border,
            $"popup {menu.PopupOuterWidth} is narrower than the text ({textRight})");
    }

    [Fact]
    public void Scrollbar_SitsBesideTheLongestItem_NotOverIt()
    {
        var labels = Enumerable.Range(0, 10).Select(i => $"Item {i}").Append(LongLabel).ToArray();
        var menu = MakeMenu(100f, labels);
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(menu);

        Click(root, 10, 10);

        Assert.True(menu.IsPopupScrollbarPresentationVisible);
        float textRight = Border + UiMenu.PlainPadding + LongLabel.Length * 7f;
        Assert.True(menu.PopupScrollbarLeftForTest >= textRight,
            $"scrollbar at {menu.PopupScrollbarLeftForTest} overlaps text ending at {textRight}");
        Assert.Equal(menu.PopupScrollbarLeftForTest + menu.ScrollbarWidth + Border, menu.PopupOuterWidth);
    }

    [Fact]
    public void ShortItems_KeepThePopupAtTheButtonWidth()
    {
        var menu = MakeMenu(120f, "A", "B");
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(menu);

        Click(root, 10, 10);

        Assert.Equal(120f + 2 * Border, menu.PopupOuterWidth);
        Assert.True(menu.PopupOuterWidth >= menu.Width);
    }

    [Fact]
    public void WidenedPopup_ShiftsLeftAtTheWindowEdge_AndItsRowsStillPick()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var window = new UiPanel { Left = 100, Top = 50, Width = 500, Height = 300, Draggable = true };
        var menu = MakeMenu(100f, "Short", LongLabel);
        menu.Left = 380; menu.Top = 10;   // screen x 480: a 296-point popup would end at 776
        string? picked = null;
        menu.OnSelect = p => picked = p as string;
        window.AddChild(menu);
        root.AddChild(window);

        Click(root, 490, 70);

        Assert.True(menu.IsOpen);
        Assert.Equal(600f, menu.ScreenPosition.X + menu.PopupLeftForTest + menu.PopupOuterWidth);
        // Second row, left of the button: only reachable because the popup moved.
        Click(root, 340, 60 + 20 + (int)Border + 18 + 9);
        Assert.Equal(LongLabel, picked);
    }

    [Fact]
    public void OpenWidenedMenu_ButtonRowBesideTheButton_IsNotTheMenus()
    {
        var menu = MakeMenu(100f, "Short", LongLabel);
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(menu);

        Click(root, 10, 10);

        Assert.True(menu.IsOpen);
        Assert.True(menu.PopupOuterWidth > 200f);
        Assert.Same(menu, menu.HitTest(50f, 10f));     // the button
        Assert.Null(menu.HitTest(150f, 10f));          // beside it, over the popup's width
        Assert.Same(menu, menu.HitTest(150f, 30f));    // the popup under it
    }

    [Fact]
    public void ItemsReplacedWhileOpen_RemeasureThePopup()
    {
        var menu = MakeMenu(100f, "A", "B");
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(menu);
        Click(root, 10, 10);
        Assert.Equal(100f + 2 * Border, menu.PopupOuterWidth);

        menu.Items = new[] { new UiMenu.MenuItem(LongLabel, LongLabel) };

        float textRight = Border + UiMenu.PlainPadding + LongLabel.Length * 7f;
        Assert.True(menu.PopupOuterWidth >= textRight + UiMenu.PlainPadding + Border,
            $"popup {menu.PopupOuterWidth} did not grow for the new item ({textRight})");
    }

    [Fact]
    public void LongCaption_IsNotCutInsideASurrogatePair()
    {
        string label = string.Concat(Enumerable.Repeat("\U0001F600", 20));   // 40 chars
        var menu = MakeMenu(80f, label);
        menu.ButtonLabelProvider = () => label;

        string shown = menu.ButtonCaptionForTest;

        Assert.EndsWith("…", shown);
        string kept = shown[..^1];
        Assert.True(kept.Length > 0 && kept.Length % 2 == 0, $"cut at {kept.Length}");
        Assert.False(char.IsHighSurrogate(kept[^1]));
    }

    [Fact]
    public void LongSelectedLabel_EndsInAnEllipsisInsideTheButton()
    {
        var menu = MakeMenu(80f, LongLabel);
        menu.ButtonLabelProvider = () => LongLabel;

        string shown = menu.ButtonCaptionForTest;

        Assert.EndsWith("…", shown);
        Assert.True(UiMenu.PlainPadding + shown.Length * 7f <= 80f - 18f, $"'{shown}' is still too wide");
    }
}
