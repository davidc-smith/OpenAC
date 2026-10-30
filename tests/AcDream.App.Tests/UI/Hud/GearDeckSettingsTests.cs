using AcDream.App.UI.Hud;
using AcDream.Core.Items.Gear;

namespace AcDream.App.Tests.UI.Hud;

public sealed class GearDeckSettingsTests
{
    [Fact]
    public void LocksRulesAndCombine_RoundTrip_AndUnreadableRulesAreKept()
    {
        string json = """
            {"version":1,"lockedIds":[7,3],"combineOnArrival":true,
             "packRules":[{"filter":"Salvage","pack":177,"arrival":true,"on":true},{"filter":5}],
             "future":"x"}
            """;
        GearDeckSettings s = GearDeckSettings.FromJson(json);
        Assert.Equal(new uint[] { 3, 7 }, s.LockedIds);
        Assert.True(s.CombineOnArrival);
        Assert.Equal(new[] { new GearPackRule("Salvage", 177u, true, true) }, s.PackRules);
        string back = s.ToJson();
        Assert.Contains("\"future\":\"x\"", back);
        Assert.Contains("{\"filter\":5}", back);
        Assert.Equal(s, GearDeckSettings.FromJson(back));
    }

    [Fact]
    public void WrongTypedLockedIds_ReadAsNone_AndTheRestStillLoads()
    {
        GearDeckSettings s = GearDeckSettings.FromJson("""{"lockedIds":"x","folded":true}""");
        Assert.Empty(s.LockedIds);
        Assert.True(s.Folded);
    }

    [Fact]
    public void WithLock_AddsAndRemovesSorted()
    {
        GearDeckSettings s = GearDeckSettings.Default.WithLock([9u, 2u], true).WithLock([9u], false);
        Assert.Equal(new uint[] { 2 }, s.LockedIds);
    }
}
