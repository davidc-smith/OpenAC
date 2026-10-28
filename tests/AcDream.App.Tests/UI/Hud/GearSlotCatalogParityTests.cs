using AcDream.App.UI.Layout;
using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.App.Tests.UI.Hud;

public sealed class GearSlotCatalogParityTests
{
    [Fact]
    public void SameMasksAsRetailPaperdoll()
    {
        var retail = PaperdollSlotBackgrounds.Definitions.Select(d => d.Mask).ToHashSet();
        var gear = GearSlotCatalog.Slots.Select(s => s.Mask).ToHashSet();
        Assert.Equal(retail, gear);
    }
}
