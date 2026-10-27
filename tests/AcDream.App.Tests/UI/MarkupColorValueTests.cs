using AcDream.App.UI.Markup;

namespace AcDream.App.Tests.UI;

public class MarkupColorValueTests
{
    [Fact]
    public void Format_WritesUpperCaseHex()
    {
        Assert.Equal("#80FF0000", MarkupColorValue.Format(0x80FF0000u));
        Assert.Equal("#FF0000", MarkupColorValue.Format(0x80FF0000u, withAlpha: false));
        Assert.Equal("#00ABCDEF", MarkupColorValue.Format(0x00ABCDEFu));
    }

    [Theory]
    [InlineData("#FF0000", 0xFFFF0000u)]
    [InlineData("ff0000", 0xFFFF0000u)]
    [InlineData("  #80ff0000 ", 0x80FF0000u)]
    [InlineData("80FF0000", 0x80FF0000u)]
    public void Parse_AcceptsSixOrEightDigits(string text, uint expected) =>
        Assert.Equal(expected, MarkupColorValue.Parse(text));

    [Fact]
    public void Parse_SixDigitsTakeTheGivenAlpha() =>
        Assert.Equal(0x40123456u, MarkupColorValue.Parse("#123456", alphaForRgb: 0x40));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#123456789")]
    [InlineData("#GG0000")]
    [InlineData("##FF0000")]
    public void Parse_RejectsEverythingElse(string? text) => Assert.Null(MarkupColorValue.Parse(text));

    [Theory]
    [InlineData("#80ff0000", 0x80FF0000u)]
    [InlineData("#FFFFFFFF", 0xFFFFFFFFu)]
    public void ParseStrict_AcceptsOnlyHashAndEightDigits(string text, uint expected) =>
        Assert.Equal(expected, MarkupColorValue.ParseStrict(text));

    [Theory]
    [InlineData("FF000000")]
    [InlineData("#FF0000")]
    [InlineData(" #FF000000")]
    [InlineData(null)]
    public void ParseStrict_RejectsOtherForms(string? text) => Assert.Null(MarkupColorValue.ParseStrict(text));

    [Theory]
    [InlineData(0xFFFF0000u, 0f, 1f, 1f)]
    [InlineData(0xFF00FFFFu, 180f, 1f, 1f)]
    [InlineData(0xFF808080u, 0f, 0f, 0.502f)]
    [InlineData(0xFF804040u, 0f, 0.5f, 0.502f)]
    [InlineData(0xFF000000u, 0f, 0f, 0f)]
    public void ToHsv_KnownColours(uint argb, float h, float s, float v)
    {
        var (gotH, gotS, gotV) = MarkupColorValue.ToHsv(argb);
        Assert.Equal(h, gotH, 1);
        Assert.Equal(s, gotS, 2);
        Assert.Equal(v, gotV, 2);
    }

    [Theory]
    [InlineData(0f, 1f, 1f, 0xFFFF0000u)]
    [InlineData(180f, 1f, 1f, 0xFF00FFFFu)]
    [InlineData(0f, 0.5f, 0.5f, 0xFF804040u)]
    [InlineData(360f, 1f, 1f, 0xFFFF0000u)]
    [InlineData(120f, 1f, 1f, 0xFF00FF00u)]
    [InlineData(240f, 1f, 1f, 0xFF0000FFu)]
    public void FromHsv_KnownColours(float h, float s, float v, uint expected) =>
        Assert.Equal(expected, MarkupColorValue.FromHsv(h, s, v, 0xFF));

    [Fact]
    public void Hsv_RoundTripsEveryByteColourInASample()
    {
        for (uint i = 0; i < 4096; i++)
        {
            uint r = (i * 37u) & 0xFF, g = (i * 101u + 13u) & 0xFF, b = (i * 211u + 7u) & 0xFF;
            uint argb = 0xFF000000u | r << 16 | g << 8 | b;
            var (h, s, v) = MarkupColorValue.ToHsv(argb);
            Assert.Equal(argb, MarkupColorValue.FromHsv(h, s, v, 0xFF));
        }
    }

    [Fact]
    public void FromHsv_KeepsTheGivenAlpha() =>
        Assert.Equal(0x40FF0000u, MarkupColorValue.FromHsv(0f, 1f, 1f, 0x40));

    private sealed class Binding
    {
        public uint U { get; set; } = 0x80112233u;
        public int I { get; set; } = unchecked((int)0xFF445566u);
        public string? S { get; set; } = "#ff778899";
        public double D { get; set; }
        public uint? LastU;
        public int? LastI;
        public string? LastS;
        public Action<uint> SetU => v => LastU = v;
        public Action<int> SetI => v => LastI = v;
        public Action<string> SetS => v => LastS = v;
    }

    [Fact]
    public void BindRequiredColor_Uint()
    {
        var b = new Binding();
        var (read, write) = MarkupBindings.BindRequiredColor("{U}", "{SetU}", MarkupBindingSource.For(b), "color");
        Assert.Equal(0x80112233u, read());
        write(0x01020304u);
        Assert.Equal(0x01020304u, b.LastU);
    }

    [Fact]
    public void BindRequiredColor_IntIsTheSameBits()
    {
        var b = new Binding();
        var (read, write) = MarkupBindings.BindRequiredColor("{I}", "{SetI}", MarkupBindingSource.For(b), "color");
        Assert.Equal(0xFF445566u, read());
        write(0xFF000001u);
        Assert.Equal(unchecked((int)0xFF000001u), b.LastI);
    }

    [Fact]
    public void BindRequiredColor_StringReadsStrictlyAndWritesUpperCase()
    {
        var b = new Binding();
        var (read, write) = MarkupBindings.BindRequiredColor("{S}", "{SetS}", MarkupBindingSource.For(b), "color");
        Assert.Equal(0xFF778899u, read());
        b.S = "#778899";   // not #AARRGGBB: white, as a bound colour attribute
        Assert.Equal(0xFFFFFFFFu, read());
        b.S = null;
        Assert.Equal(0xFFFFFFFFu, read());
        write(0x0A0B0C0Du);
        Assert.Equal("#0A0B0C0D", b.LastS);
    }

    [Theory]
    [InlineData(null, "{SetU}", "color value")]
    [InlineData("#FF000000", "{SetU}", "color value")]
    [InlineData("{Missing}", "{SetU}", "color value")]
    [InlineData("{D}", "{SetU}", "color value")]
    [InlineData("{U}", null, "color onchange")]
    [InlineData("{U}", "{Missing}", "color onchange")]
    [InlineData("{U}", "{SetI}", "color onchange")]
    [InlineData("{I}", "{SetU}", "color onchange")]
    [InlineData("{S}", "{SetU}", "color onchange")]
    public void BindRequiredColor_BadBindingsThrow(string? value, string? onchange, string context)
    {
        var ex = Assert.Throws<FormatException>(
            () => MarkupBindings.BindRequiredColor(value, onchange, MarkupBindingSource.For(new Binding()), "color"));
        Assert.Contains(context, ex.Message);
    }
}
