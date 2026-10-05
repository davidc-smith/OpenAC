using AcDream.App.UI;
using AcDream.App.UI.Layout;
using AcDream.Core.Items;
using AcDream.Core.Selection;

namespace AcDream.App.Tests.UI.Layout;

[Trait("Lane", "InstalledDat")]
public sealed class InventoryPackScrollingInstalledDatTests
{
    [Fact]
    public void AuthoredPackColumn_routesWheelInputAndRevealsEighthSlot()
    {
        string datDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Documents", "Asheron's Call");
        using var dats = new BoundedTestDatCollection(datDir);
        ElementInfo info = LayoutImporter.ImportInfos(dats, 0x21000023u)!;
        ImportedLayout layout = LayoutImporter.Build(info, resolve: _ => (1u, 16, 16), datFont: null);
        const uint player = 0x50000001u;
        var objects = new ClientObjectTable();
        objects.AddOrUpdate(new ClientObject { ObjectId = player, ContainersCapacity = 8 });
        InventoryController.Bind(layout, objects, () => player,
            iconIds: (_, _, _, _, _) => 0u, strength: () => 100,
            selection: new SelectionState(), datFont: null,
            resolveAppropriateName: ItemTooltipCaptionNames.Resolve);
        var root = new UiRoot { Width = 1920, Height = 1080 };
        root.AddChild(layout.Root);
        var list = Assert.IsType<UiItemList>(layout.FindElement(0x100001CAu));
        Assert.Equal(8, list.GetNumUIItems());
        UiItemSlot seventh = list.GetItem(6)!;
        var p = seventh.ScreenPosition;
        int x = (int)(p.X + seventh.Width / 2), y = (int)(p.Y + seventh.Height / 2);
        Assert.Same(seventh, root.Pick(x, y));
        root.OnMouseMove(x, y);
        root.OnScroll(-1);
        list.LayoutCells();
        Assert.Equal(36, list.Scroll.ScrollY);
        Assert.True(list.GetItem(7)!.Visible);
    }
}
