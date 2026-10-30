using System.Text.Json.Nodes;
using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.Core.Tests.Items.Gear;

public sealed class GearQueryJsonTests
{
    private static readonly SavedFilter Full = new("Best steel", new GearQuery(
        [GearChip.OfMaterial(64), GearChip.OfType(GearTypeGroup.Salvage), GearChip.Range(GearField.Workmanship, 7, null),
         GearChip.Range(GearField.Value, 10, 500), GearChip.NameContains("bag"), GearChip.InPack(0xB1), GearChip.WornOnly],
        "x", new GearSort(GearSortField.Workmanship, true)));

    [Fact]
    public void RoundTrip()
    {
        JsonObject o = GearQueryJson.Write(Full);
        Assert.True(GearQueryJson.TryRead(JsonNode.Parse(o.ToJsonString()), out SavedFilter? back));
        Assert.Equal(Full, back);
    }

    [Fact]
    public void Shape()
    {
        JsonObject o = GearQueryJson.Write(Full);
        Assert.Equal("work", (string?)o["sort"]);
        Assert.True((bool)o["desc"]!);
        JsonObject work = o["chips"]![2]!.AsObject();
        Assert.Equal("work", (string?)work["f"]);
        Assert.False(work.ContainsKey("max"));
        Assert.Equal("Salvage", (string?)o["chips"]![1]!["group"]);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"chips\":[]}")]
    [InlineData("{\"name\":\" \",\"chips\":[]}")]
    [InlineData("{\"name\":\"a\"}")]
    [InlineData("{\"name\":\"a\",\"chips\":[{\"f\":\"armourLevel\",\"min\":300}]}")]
    [InlineData("{\"name\":\"a\",\"chips\":[{\"f\":\"type\",\"group\":\"3\"}]}")]
    [InlineData("{\"name\":\"a\",\"chips\":[{\"f\":\"type\",\"group\":\" 3\"}]}")]          // Enum.TryParse trims it to 3
    [InlineData("{\"name\":\"a\",\"chips\":[{\"f\":\"type\",\"group\":\"Salvage, Food\"}]}")]  // 0 | 3 parses as Food
    [InlineData("{\"name\":\"a\",\"chips\":[{\"f\":\"type\",\"group\":3}]}")]
    [InlineData("{\"name\":\"a\",\"chips\":[null]}")]
    [InlineData("{\"name\":\"a\",\"chips\":[],\"sort\":\"name\",\"desc\":\"yes\"}")]
    [InlineData("{\"name\":\"a\",\"chips\":[{\"f\":\"type\",\"group\":\"Shoes\"}]}")]
    [InlineData("{\"name\":\"a\",\"chips\":[],\"sort\":\"damage\"}")]
    [InlineData("{\"name\":\"a\",\"chips\":[{\"f\":\"material\",\"id\":\"steel\"}]}")]
    public void Unreadable(string json) => Assert.False(GearQueryJson.TryRead(JsonNode.Parse(json), out _));

    [Fact]
    public void Write_OfAFieldItHasNoKeyFor_SaysWhich()
    {
        var filter = new SavedFilter("a", GearQuery.Empty with { Sort = new GearSort((GearSortField)99, false) });
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => GearQueryJson.Write(filter));
        Assert.Contains("99", ex.Message);
    }

    [Fact]
    public void LockedChip_RoundTrips_AsTheLockedKey()
    {
        var filter = new SavedFilter("a", GearQuery.Empty.With(GearChip.LockedOnly));
        JsonObject o = GearQueryJson.Write(filter);
        Assert.Contains("\"f\":\"locked\"", o.ToJsonString());
        Assert.True(GearQueryJson.TryRead(JsonNode.Parse(o.ToJsonString()), out SavedFilter? back));
        Assert.Equal(filter, back);
    }

    [Fact]
    public void PackOrder_RoundTrips_AsThePackKey()
    {
        var filter = new SavedFilter("a", GearQuery.Empty.With(GearChip.WornOnly));
        JsonObject o = GearQueryJson.Write(filter);
        Assert.Equal("pack", (string?)o["sort"]);
        Assert.True(GearQueryJson.TryRead(JsonNode.Parse(o.ToJsonString()), out SavedFilter? back));
        Assert.Equal(filter, back);
    }

    [Theory]
    [InlineData("name", GearSortField.Name)]
    [InlineData("type", GearSortField.Type)]
    [InlineData("work", GearSortField.Workmanship)]
    [InlineData("stack", GearSortField.Stack)]
    public void AFilterSavedBeforePackOrder_KeepsItsSort(string key, GearSortField field)
    {
        string json = $"{{\"name\":\"Old\",\"chips\":[{{\"f\":\"worn\"}}],\"text\":\"\",\"sort\":\"{key}\",\"desc\":false}}";
        Assert.True(GearQueryJson.TryRead(JsonNode.Parse(json), out SavedFilter? f));
        Assert.Equal(new GearSort(field, false), f!.Query.Sort);
    }

    [Fact]
    public void MissingTextAndSort_Default()
    {
        Assert.True(GearQueryJson.TryRead(JsonNode.Parse("{\"name\":\"a\",\"chips\":[{\"f\":\"worn\"}]}"), out SavedFilter? f));
        Assert.Equal(GearQuery.Empty with { Chips = [GearChip.WornOnly] }, f!.Query);
    }
}
