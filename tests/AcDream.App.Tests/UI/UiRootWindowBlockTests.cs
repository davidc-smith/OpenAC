using System.Numerics;
using AcDream.App.UI;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

/// <summary>
/// A window-modal dialog blocks only its owner window: presses on the owner
/// go nowhere but flash the dialog, the owner draws dimmed, and everything
/// else keeps working.
/// </summary>
public sealed class UiRootWindowBlockTests
{
    private sealed class Recorder : UiElement
    {
        public List<int> Types { get; } = [];

        public override bool OnEvent(in UiEvent e)
        {
            Types.Add(e.Type);
            return true;
        }
    }

    private sealed class Fixture
    {
        public UiRoot Root { get; } = new() { Width = 800f, Height = 600f };
        public UiPanel Owner { get; } = new() { Left = 10f, Top = 10f, Width = 200f, Height = 150f };
        public UiSimpleButton OwnerButton { get; } = new()
        {
            Left = 10f, Top = 10f, Width = 60f, Height = 20f, AcceptsFocus = true, TabStop = true,
        };
        public Recorder OwnerScroller { get; } = new() { Left = 100f, Top = 50f, Width = 50f, Height = 50f };
        public UiPanel Other { get; } = new() { Left = 400f, Top = 10f, Width = 200f, Height = 150f };
        public UiSimpleButton OtherButton { get; } = new() { Left = 10f, Top = 10f, Width = 60f, Height = 20f };
        public UiPanel Dialog { get; } = new() { Left = 50f, Top = 300f, Width = 120f, Height = 60f, AcceptsFocus = true };
        public int OwnerClicks;
        public int OtherClicks;
        public int WorldMouse;
        public int WorldScroll;

        public Fixture()
        {
            Owner.AddChild(OwnerButton);
            Owner.AddChild(OwnerScroller);
            Other.AddChild(OtherButton);
            Root.AddChild(Owner);
            Root.AddChild(Other);
            Root.AddChild(Dialog);
            OwnerButton.Click += () => OwnerClicks++;
            OtherButton.Click += () => OtherClicks++;
            Root.WorldMouseFallThrough += (_, _, _, _) => WorldMouse++;
            Root.WorldScrollFallThrough += _ => WorldScroll++;
        }

        public void Click(int x, int y, UiMouseButton button = UiMouseButton.Left)
        {
            Root.OnMouseDown(button, x, y);
            Root.OnMouseUp(button, x, y);
        }
    }

    // OwnerButton is at (20,20)-(80,40) on screen; OtherButton at (410,20)-(470,40).

    [Fact]
    public void APressOnABlockedWindowIsSwallowedAndFlashesTheDialog()
    {
        var f = new Fixture();
        f.Root.BlockWindow(f.Owner, f.Dialog);

        f.Click(30, 30);

        Assert.Equal(0, f.OwnerClicks);
        Assert.Equal(0, f.WorldMouse);
        Assert.Equal(UiElement.ModalFlashDuration, f.Dialog.ModalFlashSeconds);
        Assert.True(f.Dialog.ZOrder > f.Owner.ZOrder && f.Dialog.ZOrder > f.Other.ZOrder);
        Assert.Same(f.Dialog, f.Root.KeyboardFocus);
    }

    [Fact]
    public void ARightPressOnABlockedWindowIsSwallowedToo()
    {
        var f = new Fixture();
        f.Root.BlockWindow(f.Owner, f.Dialog);

        f.Click(30, 30, UiMouseButton.Right);

        Assert.Equal(0, f.WorldMouse);
        Assert.Equal(UiElement.ModalFlashDuration, f.Dialog.ModalFlashSeconds);
    }

    [Fact]
    public void OtherWindowsAndTheWorldStillTakePresses()
    {
        var f = new Fixture();
        f.Root.BlockWindow(f.Owner, f.Dialog);

        f.Click(420, 30);
        f.Click(700, 500);

        Assert.Equal(1, f.OtherClicks);
        Assert.Equal(2, f.WorldMouse); // the empty-space press and its release
        Assert.Equal(0f, f.Dialog.ModalFlashSeconds);
    }

    [Fact]
    public void AScrollOverABlockedWindowGoesNowhere()
    {
        var f = new Fixture();
        f.Root.OnMouseMove(130, 80);
        f.Root.OnScroll(1);
        Assert.Contains(UiEventType.Scroll, f.OwnerScroller.Types);
        f.OwnerScroller.Types.Clear();

        f.Root.BlockWindow(f.Owner, f.Dialog);
        f.Root.OnScroll(1);

        Assert.DoesNotContain(UiEventType.Scroll, f.OwnerScroller.Types);
        Assert.Equal(0, f.WorldScroll);
    }

    [Fact]
    public void ABlockedWindowCannotTakeTheKeyboardFocus()
    {
        var f = new Fixture();
        f.Root.BlockWindow(f.Owner, f.Dialog);

        f.Root.SetKeyboardFocus(f.OwnerButton);

        Assert.Null(f.Root.KeyboardFocus);
        Assert.False(f.Root.CanFocus(f.OwnerButton));
        Assert.True(f.Root.CanFocus(f.Dialog));
    }

    [Fact]
    public void BlockingTakesFocusCaptureAndAPopupOutOfTheWindow()
    {
        var f = new Fixture();
        f.Root.SetKeyboardFocus(f.OwnerButton);
        f.Root.SetCapture(f.OwnerScroller);
        bool dismissed = false;
        f.Root.SetActivePopup(f.OwnerScroller, () => dismissed = true);

        f.Root.BlockWindow(f.Owner, f.Dialog);

        Assert.Null(f.Root.KeyboardFocus);
        Assert.Null(f.Root.Captured);
        Assert.True(dismissed);
    }

    [Fact]
    public void ABlockedWindowDrawsDimmedUntilTheBlockEnds()
    {
        var f = new Fixture();
        IDisposable block = f.Root.BlockWindow(f.Owner, f.Dialog);

        Assert.Equal(1, MarkupDrawProbe.CountQuads(MarkupDrawProbe.Draw(f.Owner), UiElement.ModalDimColor));

        block.Dispose();
        Assert.Equal(0, MarkupDrawProbe.CountQuads(MarkupDrawProbe.Draw(f.Owner), UiElement.ModalDimColor));
        Assert.Equal(0, f.Owner.ModalBlockCount);
    }

    [Fact]
    public void TheFlashBlinksAndFades()
    {
        var f = new Fixture();
        f.Root.BlockWindow(f.Owner, f.Dialog);
        f.Click(30, 30);

        Assert.True(MarkupDrawProbe.CountQuads(MarkupDrawProbe.Draw(f.Dialog), UiElement.ModalFlashColor) > 0);

        f.Root.Tick(0.25d, 250L); // 0.35 s left: an odd tenth, hidden
        Assert.Equal(0, MarkupDrawProbe.CountQuads(MarkupDrawProbe.Draw(f.Dialog), UiElement.ModalFlashColor));

        f.Root.Tick(1d, 1250L);
        Assert.Equal(0f, f.Dialog.ModalFlashSeconds);
        Assert.Equal(0, MarkupDrawProbe.CountQuads(MarkupDrawProbe.Draw(f.Dialog), UiElement.ModalFlashColor));
    }

    [Fact]
    public void DisposingTheBlockLetsPressesThroughAgain()
    {
        var f = new Fixture();
        IDisposable block = f.Root.BlockWindow(f.Owner, f.Dialog);

        block.Dispose();
        block.Dispose();
        f.Click(30, 30);

        Assert.Equal(1, f.OwnerClicks);
        Assert.Equal(0, f.Owner.ModalBlockCount);
    }

    [Fact]
    public void TheNewestOfTwoBlockersFlashes()
    {
        var f = new Fixture();
        var second = new UiPanel { Left = 300f, Top = 300f, Width = 50f, Height = 50f };
        f.Root.AddChild(second);
        f.Root.BlockWindow(f.Owner, f.Dialog);
        IDisposable newest = f.Root.BlockWindow(f.Owner, second);
        Assert.Equal(2, f.Owner.ModalBlockCount);

        f.Click(30, 30);
        Assert.Equal(UiElement.ModalFlashDuration, second.ModalFlashSeconds);
        Assert.Equal(0f, f.Dialog.ModalFlashSeconds);

        newest.Dispose();
        f.Click(30, 30);
        Assert.Equal(UiElement.ModalFlashDuration, f.Dialog.ModalFlashSeconds);
        Assert.Equal(1, f.Owner.ModalBlockCount);
    }

    [Fact]
    public void OnlyATopLevelWindowCanBeBlocked()
    {
        var f = new Fixture();

        Assert.Throws<ArgumentException>(() => f.Root.BlockWindow(f.OwnerButton, f.Dialog));
    }

    [Fact]
    public void EachButtonKeepsItsOwnSwallowedRelease()
    {
        var f = new Fixture();
        f.Root.BlockWindow(f.Owner, f.Dialog);

        f.Root.OnMouseDown(UiMouseButton.Right, 30, 30);
        f.Root.OnMouseDown(UiMouseButton.Left, 30, 30);
        f.Root.OnMouseUp(UiMouseButton.Right, 30, 30);
        f.Root.OnMouseUp(UiMouseButton.Left, 30, 30);

        Assert.Equal(0, f.WorldMouse);
    }

    [Fact]
    public void ALostSwallowedReleaseDoesNotEatTheNextClick()
    {
        var f = new Fixture();
        IDisposable block = f.Root.BlockWindow(f.Owner, f.Dialog);
        f.Root.OnMouseDown(UiMouseButton.Left, 30, 30); // release never arrives
        block.Dispose();

        f.Click(30, 30);

        Assert.Equal(1, f.OwnerClicks);
    }

    [Fact]
    public void ADropOntoABlockedWindowGoesNowhere()
    {
        var f = new Fixture();
        var slot = new UiItemSlot { Left = 0f, Top = 0f, Width = 32f, Height = 32f };
        slot.SetItem(0x50000A01u, 0x99u);
        f.Other.AddChild(slot);
        int outside = 0;
        f.Root.DragReleasedOutsideUi += (_, _, _) => outside++;
        f.Root.BlockWindow(f.Owner, f.Dialog);

        f.Root.OnMouseDown(UiMouseButton.Left, 405, 15);
        f.Root.OnMouseMove(420, 30);
        f.Root.OnMouseMove(130, 80);
        Assert.Same(slot, f.Root.DragSource);
        f.Root.OnMouseUp(UiMouseButton.Left, 130, 80);

        Assert.DoesNotContain(UiEventType.DropReleased, f.OwnerScroller.Types);
        Assert.Equal(0, outside);
        Assert.Null(f.Root.DragSource);
    }
}
