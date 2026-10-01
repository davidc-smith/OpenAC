using AcDream.App.UI;
using AcDream.App.UI.Layout;

namespace AcDream.App.Tests.UI;

/// <summary>
/// Raising keeps an element inside its band: a window brought to the front
/// stays under the layer of canvases drawn above windows, screens sit over
/// that layer, dialogs and tooltips over screens, and pinned children are
/// never raised, counted or reached.
/// </summary>
public sealed class UiBandTests
{
    private static (UiRoot Root, UiPanel CanvasLayer) RootWithCanvasLayer()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var canvasLayer = new UiPanel { ZOrder = UiBands.CanvasesAboveWindows };
        root.AddChild(canvasLayer);
        return (root, canvasLayer);
    }

    private static UiPanel Child(UiRoot root, int zOrder = 0)
    {
        var child = new UiPanel { Width = 100, Height = 100, ZOrder = zOrder };
        root.AddChild(child);
        return child;
    }

    [Fact]
    public void TheBandsAreInOrderAndTheirBoundsAreWhereTheirNamesSay()
    {
        Assert.True(UiOverlayZOrder.BandCeiling < UiOverlayZOrder.WindowFloor);
        Assert.True(UiOverlayZOrder.WindowFloor < UiBands.WindowsCeiling);
        Assert.Equal(UiBands.WindowsCeiling, UiBands.CanvasesAboveWindows);
        Assert.True(UiBands.CanvasesAboveWindows < UiBands.ScreensFloor);
        Assert.True(UiBands.ScreensFloor < UiBands.DialogsFloor);
        Assert.True(UiBands.DialogsFloor < UiBands.Pinned);
        Assert.Equal(UiBands.CanvasesAboveWindows, UiBands.UpperRenderLayerFloor);

        Assert.Equal(UiBand.Windows, UiBands.Of(UiOverlayZOrder.SharedHostRoot));
        Assert.Equal(UiBand.Windows, UiBands.Of(UiBands.WindowsCeiling - 1));
        Assert.Null(UiBands.Of(UiBands.CanvasesAboveWindows));
        Assert.Equal(UiBand.Screens, UiBands.Of(UiBands.ScreensFloor));
        Assert.Equal(UiBand.Screens, UiBands.Of(UiBands.DialogsFloor - 1));
        Assert.Equal(UiBand.DialogsAndTooltips, UiBands.Of(UiBands.DialogsFloor));
        Assert.Equal(UiBand.DialogsAndTooltips, UiBands.Of(UiBands.Pinned - 1));
        Assert.Null(UiBands.Of(UiBands.Pinned));
    }

    [Fact]
    public void AWindowBroughtToTheFrontStaysUnderTheCanvasLayerAndEveryDialog()
    {
        (UiRoot root, UiPanel canvasLayer) = RootWithCanvasLayer();
        UiPanel a = Child(root, zOrder: 5);
        UiPanel b = Child(root, zOrder: 9);
        UiPanel dialog = Child(root);
        root.BringToFront(dialog, UiBand.DialogsAndTooltips);

        root.BringToFront(a);

        Assert.Equal(10, a.ZOrder);
        Assert.Equal(UiBands.CanvasesAboveWindows, canvasLayer.ZOrder);
        Assert.Equal(UiBands.DialogsFloor, dialog.ZOrder);
        Assert.True(b.ZOrder < a.ZOrder);
    }

    [Fact]
    public void DialogsAndTooltipsGoOverTheCanvasLayerAndStayInTheirBandWhenRaisedAgain()
    {
        (UiRoot root, UiPanel canvasLayer) = RootWithCanvasLayer();
        UiPanel window = Child(root, zOrder: 3);
        UiPanel first = Child(root);
        UiPanel tooltip = Child(root);

        root.BringToFront(first, UiBand.DialogsAndTooltips);
        root.BringToFront(tooltip, UiBand.DialogsAndTooltips);
        Assert.Equal(UiBands.DialogsFloor, first.ZOrder);
        Assert.Equal(UiBands.DialogsFloor + 1, tooltip.ZOrder);

        // The root raises a clicked dialog without naming a band.
        root.BringToFront(first);
        Assert.Equal(UiBands.DialogsFloor + 2, first.ZOrder);
        Assert.True(canvasLayer.ZOrder < tooltip.ZOrder);
        Assert.Equal(3, window.ZOrder);
    }

    [Fact]
    public void AScreenGoesOverTheCanvasLayerAndUnderDialogs()
    {
        (UiRoot root, UiPanel canvasLayer) = RootWithCanvasLayer();
        UiPanel window = Child(root, zOrder: 40);
        UiPanel screen = Child(root);
        UiPanel dialog = Child(root);
        root.BringToFront(dialog, UiBand.DialogsAndTooltips);

        root.BringToFront(screen, UiBand.Screens);
        root.BringToFront(window);

        Assert.Equal(UiBands.ScreensFloor, screen.ZOrder);
        Assert.True(canvasLayer.ZOrder < screen.ZOrder);
        Assert.True(screen.ZOrder < dialog.ZOrder);
        Assert.Equal(40, window.ZOrder);
    }

    [Fact]
    public void PinnedChildrenAreNeverRaisedCountedOrReached()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        UiPanel spew = Child(root, zOrder: UiBands.Pinned);
        UiPanel window = Child(root, zOrder: 9);
        UiPanel dialog = Child(root);

        root.BringToFront(window);
        root.BringToFront(dialog, UiBand.DialogsAndTooltips);
        root.BringToFront(spew);
        root.BringToFront(spew, UiBand.Windows);

        Assert.Equal(9, window.ZOrder);
        Assert.Equal(UiBands.DialogsFloor, dialog.ZOrder);
        Assert.Equal(UiBands.Pinned, spew.ZOrder);
    }

    [Fact]
    public void TheCanvasLayerIsNeverRaisedByAPlainRaise()
    {
        (UiRoot root, UiPanel canvasLayer) = RootWithCanvasLayer();
        Child(root, zOrder: 7);

        root.BringToFront(canvasLayer);

        Assert.Equal(UiBands.CanvasesAboveWindows, canvasLayer.ZOrder);
    }

    [Fact]
    public void AFullDialogBandIsRenumberedFromItsFloorInOrder()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        UiPanel spew = Child(root, zOrder: UiBands.Pinned);
        UiPanel back = Child(root, zOrder: UiBands.DialogsFloor + 5);
        UiPanel front = Child(root, zOrder: UiBands.Pinned - 1);

        root.BringToFront(back);

        Assert.Equal(UiBands.DialogsFloor, front.ZOrder);
        Assert.Equal(UiBands.DialogsFloor + 1, back.ZOrder);
        Assert.Equal(UiBands.Pinned, spew.ZOrder);
    }

    [Fact]
    public void AFullWindowBandIsRenumberedAndWhatSitsBelowTheFloorKeepsItsPlace()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        UiOverlayHost host = UiOverlayHost.Mount(root);
        UiPanel back = Child(root, zOrder: 3);
        UiPanel front = Child(root, zOrder: UiBands.WindowsCeiling - 1);

        root.BringToFront(back);

        Assert.Equal(0, front.ZOrder);
        Assert.Equal(1, back.ZOrder);
        Assert.Equal(UiOverlayZOrder.SharedHostRoot, host.Root.ZOrder);
    }

    [Fact]
    public void ClickingADialogKeepsItInItsBand()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var dialog = new UiPanel { Width = 100, Height = 100, Draggable = true };
        root.AddChild(dialog);
        root.BringToFront(dialog, UiBand.DialogsAndTooltips);

        root.OnMouseDown(UiMouseButton.Left, 50, 50);
        root.OnMouseUp(UiMouseButton.Left, 50, 50);

        Assert.Equal(UiBand.DialogsAndTooltips, UiBands.Of(dialog.ZOrder));
    }
}
