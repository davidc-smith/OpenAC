using AcDream.App.UI.Markup;

namespace AcDream.App.Tests.UI;

public class MarkupColorSupportTests
{
    private static byte At(byte[] rgba, int w, int x, int y, int channel) => rgba[(y * w + x) * 4 + channel];

    [Fact]
    public void WhiteToClear_FadesLeftToRight()
    {
        byte[] p = MarkupColorRamps.WhiteToClear(128, 4);
        Assert.Equal(128 * 4 * 4, p.Length);
        Assert.Equal(255, At(p, 128, 0, 2, 3));
        Assert.Equal(0, At(p, 128, 127, 2, 3));
        Assert.Equal(255, At(p, 128, 64, 0, 0));
        Assert.Equal(255, At(p, 128, 64, 0, 2));
    }

    [Fact]
    public void ClearToBlack_DarkensTopToBottom()
    {
        byte[] p = MarkupColorRamps.ClearToBlack(4, 128);
        Assert.Equal(0, At(p, 4, 1, 0, 3));
        Assert.Equal(255, At(p, 4, 1, 127, 3));
        Assert.Equal(0, At(p, 4, 1, 64, 0));
    }

    [Fact]
    public void Hue_RunsFromRedThroughCyan()
    {
        byte[] p = MarkupColorRamps.Hue(16, 128);
        Assert.Equal((255, 0, 255), (At(p, 16, 0, 0, 0), At(p, 16, 0, 0, 1), At(p, 16, 0, 0, 3)));
        Assert.InRange(At(p, 16, 8, 64, 0), 0, 2);
        Assert.InRange(At(p, 16, 8, 64, 1), 253, 255);
        Assert.InRange(At(p, 16, 8, 64, 2), 253, 255);
    }

    [Fact]
    public void AlphaRamp_IsOpaqueAtTheTop()
    {
        byte[] p = MarkupColorRamps.AlphaRamp(16, 128);
        Assert.Equal(255, At(p, 16, 3, 0, 3));
        Assert.Equal(0, At(p, 16, 3, 127, 3));
        Assert.Equal(255, At(p, 16, 3, 127, 1));
    }

    private sealed class FakeState : IMarkupControlState
    {
        public Dictionary<(string, string), string> Values { get; } = new();
        public int Generation { get; set; } = 1;
        public int Sets { get; private set; }
        public string? Get(string control, string kind) => Values.GetValueOrDefault((control, kind));
        public void Set(string control, string kind, string value) { Sets++; Values[(control, kind)] = value; }
        public void Remove(string control, string kind) => Values.Remove((control, kind));
    }

    [Fact]
    public void Recent_NewestFirstNoRepeatsAtMostEight()
    {
        var recent = new MarkupRecentColors(null);
        for (uint i = 1; i <= 10; i++)
            recent.Add(0xFF000000u | i);
        recent.Add(0xFF000005u);
        Assert.Equal(
            new uint[] { 0xFF000005, 0xFF00000A, 0xFF000009, 0xFF000008, 0xFF000007, 0xFF000006, 0xFF000004, 0xFF000003 },
            recent.Colors);
    }

    [Fact]
    public void Recent_StoresCommaTextInTheSharedState()
    {
        var state = new FakeState();
        var recent = new MarkupRecentColors(state);
        recent.Add(0x80FF0000u);
        recent.Add(0xFF00FF00u);
        Assert.Equal("#FF00FF00,#80FF0000", state.Values[("color", "recent")]);
    }

    [Fact]
    public void Recent_ReadsTheStoredListAndSkipsJunk()
    {
        var state = new FakeState();
        state.Values[("color", "recent")] = "#FF0000FF,junk,#ff0000ff,#12,#80112233";
        var recent = new MarkupRecentColors(state);
        Assert.Equal(new uint[] { 0xFF0000FF, 0x80112233 }, recent.Colors);
    }

    [Fact]
    public void Recent_RereadsAfterACharacterSwitch()
    {
        var state = new FakeState();
        var recent = new MarkupRecentColors(state);
        recent.Add(0xFF111111u);
        state.Values[("color", "recent")] = "#FF222222";
        state.Generation++;
        Assert.Equal(new uint[] { 0xFF222222 }, recent.Colors);
        recent.Add(0xFF333333u);
        Assert.Equal("#FF333333,#FF222222", state.Values[("color", "recent")]);
    }

    [Fact]
    public void Recent_KeepsAtMostEightFromStoredText()
    {
        var state = new FakeState();
        state.Values[("color", "recent")] = string.Join(",", Enumerable.Range(1, 12).Select(i => $"#FF0000{i:X2}"));
        Assert.Equal(MarkupRecentColors.Capacity, new MarkupRecentColors(state).Colors.Count);
    }

    [Fact]
    public void Store_SharedUsesTheReservedWindowKey()
    {
        var store = new MarkupControlStateStore(null, () => "Alice");
        store.Shared.Set("color", "recent", "#FF000000");
        Assert.Equal("#FF000000", store.ForWindow(MarkupControlStateStore.SharedWindow).Get("color", "recent"));
        Assert.Null(store.ForWindow("other").Get("color", "recent"));
    }
}
