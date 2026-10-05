using System.Xml.Linq;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public sealed class PluginWindowChromeTests
{
    [Fact]
    public void TheChromeInsetsAreTheClassicBorderAndTheHeaderBand()
    {
        Assert.Equal(5f, PluginWindowChrome.Border);
        Assert.Equal(24f, PluginWindowChrome.TitleBarHeight);
        Assert.Equal(19f, PluginWindowChrome.CloseButtonSize);
        Assert.Equal(10f, PluginWindowChrome.HorizontalInsets);
        Assert.Equal(29f, PluginWindowChrome.VerticalInsets);
    }

    [Fact]
    public void TheRevisionIsFnv1aOverTheAuthoredInputs()
    {
        // Pinned: computed independently. A per-process string hash would fail here.
        Assert.Equal(1338326359, RetailWindowManager.ComputeAuthoredGeometryRevision(
            ["300", "200", null, null, null, null, null, "true", "1"]));
    }

    [Fact]
    public void AnAbsentInputIsNotAnEmptyOne()
    {
        Assert.NotEqual(
            RetailWindowManager.ComputeAuthoredGeometryRevision([null, "1"]),
            RetailWindowManager.ComputeAuthoredGeometryRevision(["", "1"]));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
    public void ChangingAnyInputChangesTheRevision(int index)
    {
        string?[] inputs = ["300", "200", "250", "150", "true", "x", null, "true", "1"];
        int before = RetailWindowManager.ComputeAuthoredGeometryRevision(inputs);
        inputs[index] = inputs[index] is null ? "row" : inputs[index] + "0";
        int after = RetailWindowManager.ComputeAuthoredGeometryRevision(inputs);
        Assert.NotEqual(before, after);
        Assert.True(after >= 0);
    }

    [Fact]
    public void TheAuthoredInputsAreTheGeometryAttributesThenTheChromeVersion()
    {
        var root = XElement.Parse(
            "<panel x=\"9\" title=\"T\" theme=\"plugin\" w=\"300\" h=\"200\" minw=\"250\" "
            + "resizable=\"true\" resize=\"x\" titlebar=\"true\" />");
        Assert.Equal(
            new string?[] { "300", "200", "250", null, "true", "x", null, "true", "1" },
            PluginWindowChrome.AuthoredInputs(root));
    }

    private static float Measure(string s) => s.Length * 10f;

    [Theory]
    [InlineData("Buffs", 50f, "Buffs")]
    [InlineData("Buff Bot", 70f, "Buff...")]
    [InlineData("Buff Bot", 60f, "Buf...")]
    [InlineData("Buff Bot", 30f, "...")]
    [InlineData("Buff Bot", 29f, "")]
    public void LongTitlesAreShortenedWithDots(string text, float width, string expected) =>
        Assert.Equal(expected, PluginWindowChrome.Ellipsize(text, Measure, width));
}
