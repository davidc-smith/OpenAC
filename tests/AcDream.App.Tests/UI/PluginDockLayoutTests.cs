using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public sealed class PluginDockLayoutTests
{
    private static PluginDockLayout Floating(string[] owners, float maxHeight = float.PositiveInfinity, int firstRow = 0) =>
        PluginDockLayout.Compute(PluginDockMode.Floating, collapsed: false, owners, maxHeight, firstRow);

    [Fact]
    public void OneSlot_IsHandleRowSlotDividerGearAndPadding()
    {
        PluginDockLayout layout = Floating(["a"]);

        Assert.Equal(48f, layout.Width);
        // 16 (handle row) + 36 (slot) + 9 (divider) + 36 (gear) + 6 (padding)
        Assert.Equal(103f, layout.Height);
        Assert.Equal(new DockRect(6f, 16f, 36f, 36f), layout.Slots[0]);
        Assert.Equal([56.5f], layout.Dividers);
        Assert.Equal(new DockRect(6f, 61f, 36f, 36f), layout.Gear);
        Assert.Equal(new DockRect(0f, 0f, 30f, 16f), layout.Handle);
        Assert.Equal(new DockRect(30f, 2f, 12f, 12f), layout.Toggle);
    }

    [Fact]
    public void SameOwnerSlotsAreGapped_AndADifferentOwnerGetsADivider()
    {
        PluginDockLayout layout = Floating(["moss", "moss", "goarrow"]);

        Assert.Equal(16f, layout.Slots[0]!.Value.Y);
        Assert.Equal(56f, layout.Slots[1]!.Value.Y);   // 16 + 36 + 4
        Assert.Equal(101f, layout.Slots[2]!.Value.Y);  // 56 + 36 + 9
        Assert.Equal([96.5f, 141.5f], layout.Dividers);
        Assert.Equal(146f, layout.Gear.Y);
        Assert.Equal(188f, layout.Height);
    }

    [Fact]
    public void Overflow_ShowsWhatFits_AndScrollingIsClampedToTheLastFullPage()
    {
        string[] owners = Enumerable.Range(0, 12).Select(i => $"p{i}").ToArray();
        // Room for 16 + 3 slots (36 + 9 + 36 + 9 + 36) + 9 + 36 + 6 = 193.
        PluginDockLayout top = Floating(owners, maxHeight: 200f);

        Assert.Equal(3, top.VisibleCount);
        Assert.Equal(0, top.FirstRow);
        Assert.Equal(9, top.MaxFirstRow);
        Assert.False(top.FadeTop);
        Assert.True(top.FadeBottom);
        Assert.True(top.Height <= 200f);
        Assert.NotNull(top.Slots[2]);
        Assert.Null(top.Slots[3]);

        PluginDockLayout end = Floating(owners, maxHeight: 200f, firstRow: 50);
        Assert.Equal(9, end.FirstRow);
        Assert.True(end.FadeTop);
        Assert.False(end.FadeBottom);
        Assert.Null(end.Slots[8]);
        Assert.Equal(16f, end.Slots[9]!.Value.Y);
        Assert.NotNull(end.Slots[11]);
    }

    [Fact]
    public void TooLittleRoom_StillShowsOneSlot()
    {
        PluginDockLayout layout = Floating(["a", "b"], maxHeight: 10f);
        Assert.Equal(1, layout.VisibleCount);
        Assert.NotNull(layout.Slots[0]);
    }

    [Theory]
    [InlineData(PluginDockMode.Left)]
    [InlineData(PluginDockMode.Right)]
    public void Rail_IsFullWidthSlots_WithTheSamePitchAsFloating(PluginDockMode mode)
    {
        PluginDockLayout rail = PluginDockLayout.Compute(mode, false, ["a", "a"], float.PositiveInfinity, 0);
        PluginDockLayout floating = Floating(["a", "a"]);

        Assert.Equal(46f, rail.Width);
        Assert.Equal(new DockRect(0f, 16f, 46f, 38f), rail.Slots[0]);
        Assert.Equal(56f, rail.Slots[1]!.Value.Y);   // 16 + 38 + 2
        // Slot pitch is 40 in both (36 + 4, 38 + 2); the last slot and the gear are 2pt taller each.
        Assert.Equal(floating.Height + 4f, rail.Height);
        Assert.Equal(new DockRect(28f, 2f, 12f, 12f), rail.Toggle);
    }

    [Fact]
    public void Collapsed_FloatingIsAHandlePill_AndARailIsAnEdgeTab()
    {
        PluginDockLayout pill = PluginDockLayout.Compute(PluginDockMode.Floating, true, ["a"], 500f, 0);
        Assert.Equal((48f, 24f), (pill.Width, pill.Height));
        Assert.Equal(new DockRect(0f, 0f, 30f, 24f), pill.Handle);
        Assert.Equal(new DockRect(30f, 0f, 12f, 24f), pill.Toggle);
        Assert.All(pill.Slots, s => Assert.Null(s));
        Assert.Empty(pill.Dividers);

        PluginDockLayout tab = PluginDockLayout.Compute(PluginDockMode.Left, true, ["a"], 500f, 0);
        Assert.Equal((12f, 44f), (tab.Width, tab.Height));
        Assert.Equal(new DockRect(0f, 0f, 12f, 14f), tab.Handle);
        Assert.Equal(new DockRect(0f, 14f, 12f, 30f), tab.Toggle);
    }

    [Fact]
    public void NoEntries_HasNoDividerBeforeTheGear()
    {
        PluginDockLayout layout = Floating([]);
        Assert.Empty(layout.Dividers);
        Assert.Equal(16f, layout.Gear.Y);
    }
}
