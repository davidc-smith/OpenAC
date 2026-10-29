using AcDream.Core.Items;
using Xunit;

namespace AcDream.Core.Tests.Items;

public sealed class MaterialNamesTests
{
    [Theory]
    [InlineData(1u, "Ceramic")]
    [InlineData(64u, "Steel")]
    [InlineData(77u, "Teak")]
    [InlineData(0u, "Unknown")]
    [InlineData(78u, "Unknown")]
    public void Name_ByRetailId(uint id, string name) => Assert.Equal(name, MaterialNames.Name(id));

    [Fact]
    public void All_IsOneToSeventySevenInOrder()
    {
        var all = MaterialNames.All.ToList();
        Assert.Equal(77, all.Count);
        Assert.Equal((1u, "Ceramic"), all[0]);
        Assert.Equal((77u, "Teak"), all[^1]);
    }
}
