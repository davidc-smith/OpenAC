using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.UI.ScriptWindows;

namespace AcDream.Rynth.Host.Tests;

public sealed class ScriptIconsTests : IDisposable
{
    public ScriptIconsTests() => ScriptIcons.Source = null;

    public void Dispose() => ScriptIcons.Source = null;

    [Fact]
    public void WithoutASourceThereIsNoIcon()
    {
        Assert.False(ScriptIcons.TryGet(ScriptIconKind.Icon, 0x06001234u, out IntPtr texture));
        Assert.Equal(IntPtr.Zero, texture);
    }

    [Theory]
    [InlineData((int)ScriptIconKind.Icon, 0x06001234u)]
    [InlineData((int)ScriptIconKind.Object, 0x80001234u)]
    [InlineData((int)ScriptIconKind.Spell, 1234u)]
    public void EachKindReachesTheSourceAsIs(int kindValue, uint id)
    {
        var kind = (ScriptIconKind)kindValue; // the enum is internal, so the public test method takes its value
        var asked = new List<(ScriptIconKind, uint)>();
        ScriptIcons.Source = (k, i) => { asked.Add((k, i)); return (IntPtr)42; };

        Assert.True(ScriptIcons.TryGet(kind, id, out IntPtr texture));
        Assert.Equal((IntPtr)42, texture);
        Assert.Equal(new[] { (kind, id) }, asked);
    }

    [Fact]
    public void UnknownKindsZeroIdsAndEmptyAnswersAreNoIcon()
    {
        int calls = 0;
        ScriptIcons.Source = (_, id) => { calls++; return id == 7 ? IntPtr.Zero : (IntPtr)9; };

        Assert.False(ScriptIcons.TryGet(ScriptIconKind.Unknown, 5, out _));
        Assert.False(ScriptIcons.TryGet(ScriptIconKind.Icon, 0, out _));
        Assert.Equal(0, calls);
        Assert.False(ScriptIcons.TryGet(ScriptIconKind.Icon, 7, out IntPtr texture));
        Assert.Equal(IntPtr.Zero, texture);
    }

    [Fact]
    public void ThePhosphorFontIsEmbedded()
    {
        byte[]? ttf = PhosphorFont.Load();
        Assert.NotNull(ttf);
        Assert.True(ttf!.Length > 10_000);
        Assert.Equal(new byte[] { 0, 1, 0, 0 }, ttf[..4]);
        Assert.Contains(PhosphorIcons.Sparkle, PhosphorIcons.Baked, StringComparison.Ordinal);
    }
}
