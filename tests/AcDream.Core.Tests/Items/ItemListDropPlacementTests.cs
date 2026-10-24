using AcDream.Core.Items;

namespace AcDream.Core.Tests.Items;

public sealed class ItemListDropPlacementTests
{
    [Theory]
    [InlineData(0, 4, 3, 0)]    // moving earlier takes the hovered slot
    [InlineData(1, 4, 3, 1)]
    [InlineData(3, 5, 0, 2)]    // moving later asks for one less: lands left of the target
    [InlineData(2, 4, 0, 1)]
    [InlineData(99, 4, 0, 3)]   // past the last item appends
    [InlineData(99, 4, 2, 3)]
    public void WholeItemReorder(int drop, int count, int source, int expected)
        => Assert.Equal(expected, ItemListDropPlacement.Resolve(drop, count, source, wholeStack: true));

    [Theory]
    [InlineData(2, 4, 2)]       // dropped on itself
    [InlineData(3, 4, 2)]       // dropped on its right neighbour
    [InlineData(99, 4, 3)]      // the last item dropped past the end
    public void DropThatLeavesTheItemInPlace_SendsNothing(int drop, int count, int source)
        => Assert.Null(ItemListDropPlacement.Resolve(drop, count, source, wholeStack: true));

    [Fact]
    public void SplitKeepsTheSourceInPlace_SoNothingIsTakenOff()
    {
        Assert.Equal(3, ItemListDropPlacement.Resolve(3, 4, 2, wholeStack: false));
        Assert.Equal(4, ItemListDropPlacement.Resolve(99, 4, 0, wholeStack: false));
        Assert.Null(ItemListDropPlacement.Resolve(2, 4, 2, wholeStack: false));
    }

    [Theory]
    [InlineData(0, 3, 0)]
    [InlineData(2, 3, 2)]
    [InlineData(7, 3, 3)]       // an empty cell past the last item appends
    [InlineData(0, 0, 0)]
    public void ArrivalFromElsewhere_TakesTheHoveredSlot(int drop, int count, int expected)
        => Assert.Equal(expected, ItemListDropPlacement.Resolve(drop, count, -1, wholeStack: true));
}
