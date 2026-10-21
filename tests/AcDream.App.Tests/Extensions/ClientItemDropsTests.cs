using System.Numerics;
using AcDream.App.Extensions;
using AcDream.App.UI;

namespace AcDream.App.Tests.Extensions;

public sealed class ClientItemDropsTests
{
    private object? _payload;
    private bool _overImGui;
    private readonly List<string> _log = new();
    private readonly ClientItemDrops _drops;

    public ClientItemDropsTests() => _drops = new ClientItemDrops(() => _payload, () => _overImGui, _log.Add);

    private static ItemDragPayload Item(ItemDragSource source) => new(0x5001u, source, 0, null);

    [Theory]
    [InlineData(ItemDragSource.Inventory, 0x5001u)]
    [InlineData(ItemDragSource.Equipment, 0u)]
    [InlineData(ItemDragSource.ShortcutBar, 0u)]
    [InlineData(ItemDragSource.Ground, 0u)]
    public void OnlyAnInventoryDragIsTheDraggedItem(ItemDragSource source, uint expected)
    {
        _payload = Item(source);
        Assert.Equal(expected, _drops.DraggedItem);
        _payload = "not an item";
        Assert.Equal(0u, _drops.DraggedItem);
        _payload = null;
        Assert.Equal(0u, _drops.DraggedItem);
    }

    [Fact]
    public void OffImGuiTheReleaseIsLeftAlone()
    {
        int calls = 0;
        _drops.SetDropClaimer((_, _) => { calls++; return true; });
        Assert.False(_drops.InterceptRelease(Item(ItemDragSource.Inventory), new Vector2(5, 5)));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OverImGuiAnInventoryDragIsOfferedAndAlwaysConsumed(bool claimed)
    {
        _overImGui = true;
        (uint Item, Vector2 Point)? offered = null;
        _drops.SetDropClaimer((item, point) => { offered = (item, point); return claimed; });

        Assert.True(_drops.InterceptRelease(Item(ItemDragSource.Inventory), new Vector2(150, 160)));
        Assert.Equal((0x5001u, new Vector2(150, 160)), offered);
    }

    [Fact]
    public void OverImGuiAnEquipmentDragIsCancelledNotOffered()
    {
        _overImGui = true;
        int calls = 0;
        _drops.SetDropClaimer((_, _) => { calls++; return true; });
        Assert.True(_drops.InterceptRelease(Item(ItemDragSource.Equipment), Vector2.Zero));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void AThrowingClaimerIsLoggedAndTheDropStillCancelled()
    {
        _overImGui = true;
        _drops.SetDropClaimer((_, _) => throw new InvalidOperationException("boom"));
        Assert.True(_drops.InterceptRelease(Item(ItemDragSource.Inventory), Vector2.Zero));
        Assert.Contains(_log, line => line.Contains("boom", StringComparison.Ordinal));
    }
}
