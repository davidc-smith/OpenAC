using System.Linq;
using AcDream.App.UI;
using AcDream.App.UI.Markup;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

public class MarkupNumberTests
{
    private sealed class Binding
    {
        public int Count { get; set; } = 10;
        public int Calls { get; private set; }
        public Action<int> SetCount => v => { Count = v; Calls++; };
        public double Ratio { get; set; } = 0.5;
        public Action<double> SetRatio => v => Ratio = v;
        public long Big { get; set; } = 5;
        public Action<long> SetBig => v => Big = v;
        public decimal Money { get; set; } = decimal.MaxValue - 0.5m;
        public Action<decimal> SetMoney => v => Money = v;
        public float Scale { get; set; } = 1.5f;
        public int ScaleCalls { get; private set; }
        public Action<float> SetScale => v => { Scale = v; ScaleCalls++; };
        public bool On { get; set; } = true;
        public string NotNumber { get; set; } = "";
    }

    [Fact]
    public void NumberReader_ReadsEveryNumericTypeAsDecimal()
    {
        var b = new Binding();
        var (count, countType) = MarkupBindings.BindRequiredNumberReader("{Count}", b, "ctx");
        Assert.Equal(10m, count());
        Assert.Equal(typeof(int), countType);
        Assert.Equal(0.5m, MarkupBindings.BindRequiredNumberReader("{Ratio}", b, "ctx").Read());
        Assert.Equal(5m, MarkupBindings.BindRequiredNumberReader("{Big}", b, "ctx").Read());
        Assert.Equal(1.5m, MarkupBindings.BindRequiredNumberReader("{Scale}", b, "ctx").Read());
        b.Scale = 0.1f;
        Assert.Equal(0.1m, MarkupBindings.BindRequiredNumberReader("{Scale}", b, "ctx").Read());
        Assert.Equal(b.Money, MarkupBindings.BindRequiredNumberReader("{Money}", b, "ctx").Read());
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 1)]
    [InlineData(double.NegativeInfinity, -1)]
    [InlineData(1e30, 1)]
    public void NumberReader_ClampsUnrepresentableDoubles(double value, int expected)
    {
        var b = new Binding { Ratio = value };
        decimal read = MarkupBindings.BindRequiredNumberReader("{Ratio}", b, "ctx").Read();
        Assert.Equal(expected switch { 1 => decimal.MaxValue, -1 => decimal.MinValue, _ => 0m }, read);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("10")]
    [InlineData("{NotNumber}")]
    [InlineData("{Missing}")]
    public void NumberReader_RejectsNonNumbers(string? expression)
    {
        var ex = Assert.Throws<FormatException>(
            () => MarkupBindings.BindRequiredNumberReader(expression, new Binding(), "number value"));
        Assert.Contains("number value", ex.Message);
    }

    [Fact]
    public void NumberAction_ConvertsToTheBoundType()
    {
        var b = new Binding();
        MarkupBindings.BindRequiredNumberAction("{SetCount}", b, typeof(int), "ctx")(42m);
        MarkupBindings.BindRequiredNumberAction("{SetRatio}", b, typeof(double), "ctx")(0.25m);
        MarkupBindings.BindRequiredNumberAction("{SetBig}", b, typeof(long), "ctx")(9_000_000_000m);
        MarkupBindings.BindRequiredNumberAction("{SetScale}", b, typeof(float), "ctx")(2.5m);
        MarkupBindings.BindRequiredNumberAction("{SetMoney}", b, typeof(decimal), "ctx")(1.25m);
        Assert.Equal(42, b.Count);
        Assert.Equal(0.25, b.Ratio);
        Assert.Equal(9_000_000_000L, b.Big);
        Assert.Equal(2.5f, b.Scale);
        Assert.Equal(1.25m, b.Money);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("SetCount")]
    [InlineData("{SetBig}")]
    [InlineData("{Missing}")]
    public void NumberAction_RequiresActionOfTheValueType(string? attribute)
    {
        var ex = Assert.Throws<FormatException>(
            () => MarkupBindings.BindRequiredNumberAction(attribute, new Binding(), typeof(int), "number onchange"));
        Assert.Contains("number onchange", ex.Message);
    }

    [Fact]
    public void NumberRange_AndIntegral()
    {
        Assert.Equal(((decimal)int.MinValue, (decimal)int.MaxValue), MarkupBindings.NumberRange(typeof(int)));
        Assert.Equal(((decimal)long.MinValue, (decimal)long.MaxValue), MarkupBindings.NumberRange(typeof(long)));
        Assert.Equal((decimal.MinValue, decimal.MaxValue), MarkupBindings.NumberRange(typeof(double)));
        Assert.True(MarkupBindings.IsIntegralNumber(typeof(int)));
        Assert.True(MarkupBindings.IsIntegralNumber(typeof(long)));
        Assert.False(MarkupBindings.IsIntegralNumber(typeof(float)));
    }

    private const string CountAttrs =
        "value=\"{Count}\" min=\"0\" max=\"100\" step=\"5\" onchange=\"{SetCount}\"";

    // The number sits at (8, 8), 80×20: spin buttons span x 74-88; up is y 8-18, down y 18-28.
    private const int SpinX = 81, UpY = 11, DownY = 24;

    private static (UiRoot Root, UiMarkupNumber Number) Mount(string attributes, Binding binding)
    {
        var panel = MarkupDocument.Build(
            $"<panel x=\"0\" y=\"0\" w=\"300\" h=\"100\"><number x=\"8\" y=\"8\" w=\"80\" h=\"20\" {attributes}/></panel>",
            binding, _ => (1u, 16, 16));
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        return (root, Assert.IsType<UiMarkupNumber>(panel.Children[0]));
    }

    private static void TypeText(UiRoot root, UiMarkupNumber number, string text)
    {
        root.SetKeyboardFocus(number.Field);
        number.Field.SetText(text);
    }

    private static void Press(UiRoot root, int x, int y) => root.OnMouseDown(UiMouseButton.Left, x, y);
    private static void Release(UiRoot root, int x, int y) => root.OnMouseUp(UiMouseButton.Left, x, y);

    [Fact]
    public void Number_ShowsTheBoundValueFormatted()
    {
        var (_, count) = Mount(CountAttrs, new Binding());
        Assert.Equal("10", count.Field.Text);
        var (_, ratio) = Mount("value=\"{Ratio}\" onchange=\"{SetRatio}\"", new Binding());
        Assert.Equal("0.50", ratio.Field.Text);
    }

    [Theory]
    [InlineData("onchange=\"{SetCount}\"")]
    [InlineData("value=\"{NotNumber}\" onchange=\"{SetCount}\"")]
    [InlineData("value=\"{Count}\"")]
    [InlineData("value=\"{Count}\" onchange=\"{SetBig}\"")]
    [InlineData("value=\"{Count}\" onchange=\"{SetCount}\" min=\"10\" max=\"5\"")]
    [InlineData("value=\"{Count}\" onchange=\"{SetCount}\" step=\"0\"")]
    [InlineData("value=\"{Count}\" onchange=\"{SetCount}\" decimals=\"2\"")]
    [InlineData("value=\"{Ratio}\" onchange=\"{SetRatio}\" decimals=\"11\"")]
    [InlineData("value=\"{Count}\" onchange=\"{SetCount}\" max=\"3000000000\"")]
    [InlineData("value=\"{Count}\" onchange=\"{SetCount}\" step=\"0.5\"")]
    [InlineData("value=\"{Ratio}\" onchange=\"{SetRatio}\" decimals=\"1\" step=\"0.25\"")]
    [InlineData("value=\"{Count}\" onchange=\"{SetCount}\" min=\"low\"")]
    [InlineData("value=\"{Ratio}\" onchange=\"{SetRatio}\" decimals=\"two\"")]
    public void Number_BadAttributesThrowAtBuild(string attributes)
    {
        Assert.Throws<FormatException>(() => Mount(attributes, new Binding()));
    }

    [Fact]
    public void Number_FocusLossCommitsTheTypedValue()
    {
        var b = new Binding();
        var (root, number) = Mount(CountAttrs, b);
        TypeText(root, number, "42");
        root.SetKeyboardFocus(null);
        Assert.Equal(42, b.Count);
        Assert.Equal("42", number.Field.Text);
    }

    [Fact]
    public void Number_EnterCommits()
    {
        var b = new Binding();
        var (root, number) = Mount(CountAttrs, b);
        TypeText(root, number, "42");
        root.OnKeyDown((int)Key.Enter);
        Assert.Equal(42, b.Count);
        Assert.Null(root.KeyboardFocus);
    }

    [Fact]
    public void Number_CommitClampsAndRounds()
    {
        var b = new Binding();
        var (root, count) = Mount(CountAttrs, b);
        TypeText(root, count, "250");
        root.SetKeyboardFocus(null);
        Assert.Equal(100, b.Count);

        var (root2, ratio) = Mount("value=\"{Ratio}\" min=\"-1\" max=\"1\" decimals=\"1\" onchange=\"{SetRatio}\"", b);
        TypeText(root2, ratio, "0.25");
        root2.SetKeyboardFocus(null);
        Assert.Equal(0.3, b.Ratio, 10);
        Assert.Equal("0.3", ratio.Field.Text);
    }

    [Theory]
    [InlineData("-")]
    [InlineData("")]
    [InlineData("1-2")]
    public void Number_UnparseableTextRestores(string text)
    {
        var b = new Binding();
        var (root, number) = Mount(CountAttrs, b);
        TypeText(root, number, text);
        root.SetKeyboardFocus(null);
        Assert.Equal(0, b.Calls);
        Assert.Equal("10", number.Field.Text);
    }

    [Fact]
    public void Number_EscapeRestoresWithoutReporting()
    {
        var b = new Binding();
        var (root, number) = Mount(CountAttrs, b);
        TypeText(root, number, "77");
        root.OnKeyDown((int)Key.Escape);
        Assert.Equal(0, b.Calls);
        Assert.Equal(10, b.Count);
        Assert.Equal("10", number.Field.Text);
        Assert.Null(root.KeyboardFocus);
    }

    [Fact]
    public void Number_SameValueIsNotReported()
    {
        var b = new Binding();
        var (root, number) = Mount(CountAttrs, b);
        TypeText(root, number, "10");
        root.SetKeyboardFocus(null);
        Assert.Equal(0, b.Calls);
    }

    [Fact]
    public void Number_UpDownStepAndShiftStepsTen()
    {
        var b = new Binding();
        var (root, number) = Mount(CountAttrs, b);
        root.SetKeyboardFocus(number.Field);
        root.OnKeyDown((int)Key.Up);
        Assert.Equal(15, b.Count);
        root.OnKeyDown((int)Key.Down);
        Assert.Equal(10, b.Count);
        number.ShiftHeld = () => true;
        root.OnKeyDown((int)Key.Up);
        Assert.Equal(60, b.Count);
        Assert.Equal("60", number.Field.Text);
    }

    [Fact]
    public void Number_WheelStepsOnlyWhileFocused()
    {
        var b = new Binding();
        var (root, number) = Mount(CountAttrs, b);
        root.OnMouseMove(30, 15);
        root.OnScroll(1);
        Assert.Equal(10, b.Count);
        root.SetKeyboardFocus(number.Field);
        root.OnScroll(1);
        Assert.Equal(15, b.Count);
        root.OnScroll(-1);
        Assert.Equal(10, b.Count);
    }

    [Fact]
    public void Number_ShiftSidewaysTickStepsTenWhenNothingScrollsSideways()
    {
        var b = new Binding();
        var (root, number) = Mount(CountAttrs, b);
        root.OnMouseMove(30, 15);
        root.SetKeyboardFocus(number.Field);
        number.ShiftHeld = () => true;
        root.OnScroll(1, 0, shift: true);   // macOS: Shift+wheel arrives as a sideways tick
        Assert.Equal(60, b.Count);
        root.OnScroll(1, 0, shift: false);  // a plain sideways swipe steps nothing
        Assert.Equal(60, b.Count);
    }

    [Fact]
    public void Number_SpinButtonsStepAndRepeatWhileHeld()
    {
        var b = new Binding();
        var (root, _) = Mount(CountAttrs, b);
        Press(root, SpinX, UpY);
        Assert.Equal(15, b.Count);
        Assert.Null(root.KeyboardFocus);
        root.Tick(0.39, 0);
        Assert.Equal(15, b.Count);
        root.Tick(0.02, 0);
        Assert.Equal(20, b.Count);
        root.Tick(0.06, 0);
        Assert.Equal(25, b.Count);
        Release(root, SpinX, UpY);
        root.Tick(1.0, 0);
        Assert.Equal(25, b.Count);

        Press(root, SpinX, DownY);
        Release(root, SpinX, DownY);
        Assert.Equal(20, b.Count);
    }

    [Fact]
    public void Number_SpinClampsAtTheLimitWithoutReporting()
    {
        var b = new Binding { Count = 100 };
        var (root, _) = Mount(CountAttrs, b);
        Press(root, SpinX, UpY);
        Release(root, SpinX, UpY);
        Assert.Equal(100, b.Count);
        Assert.Equal(0, b.Calls);
    }

    [Fact]
    public void Number_SteppingPastDecimalLimitsClamps()
    {
        var b = new Binding();
        var (root, number) = Mount("value=\"{Money}\" decimals=\"1\" onchange=\"{SetMoney}\"", b);
        root.SetKeyboardFocus(number.Field);
        number.ShiftHeld = () => true;
        root.OnKeyDown((int)Key.Up);
        Assert.Equal(decimal.MaxValue, b.Money);
    }

    [Fact]
    public void Number_FollowsTheBoundValueWhileNotEditing()
    {
        var b = new Binding();
        var (root, number) = Mount(CountAttrs, b);
        b.Count = 30;
        root.Tick(0.016, 0);
        Assert.Equal("30", number.Field.Text);
    }

    [Fact]
    public void Number_CharacterFilterFollowsRangeAndDecimals()
    {
        var (_, count) = Mount(CountAttrs, new Binding());
        Assert.True(count.Field.CharacterFilter!('5'));
        Assert.False(count.Field.CharacterFilter!('a'));
        Assert.False(count.Field.CharacterFilter!('-'));
        Assert.False(count.Field.CharacterFilter!('.'));
        var (_, ratio) = Mount("value=\"{Ratio}\" min=\"-1\" max=\"1\" decimals=\"1\" onchange=\"{SetRatio}\"", new Binding());
        Assert.True(ratio.Field.CharacterFilter!('-'));
        Assert.True(ratio.Field.CharacterFilter!('.'));
    }

    [Fact]
    public void Number_InvalidTextDrawsInTheErrorColour()
    {
        var (root, number) = Mount(CountAttrs, new Binding());
        TypeText(root, number, "250");
        Assert.Equal(UiMarkupNumber.ErrorColor, number.Field.TextColorSource!());
        number.Field.SetText("50");
        Assert.NotEqual(UiMarkupNumber.ErrorColor, number.Field.TextColorSource!());
    }

    [Fact]
    public void Number_DisabledIsReadOnlyAndSpinsDoNothing()
    {
        var b = new Binding();
        var (root, number) = Mount(CountAttrs + " enabled=\"false\"", b);
        Assert.False(number.Field.Editable);   // read-only from build, before any tick
        root.Tick(0.016, 0);
        Assert.False(number.Field.Editable);
        Press(root, SpinX, UpY);
        Release(root, SpinX, UpY);
        Assert.Equal(0, b.Calls);
    }

    [Fact]
    public void Number_FloatStepAtItsMaxReportsNothing()
    {
        var b = new Binding { Scale = 1.1f };
        var (root, _) = Mount("value=\"{Scale}\" max=\"1.1\" onchange=\"{SetScale}\"", b);
        Press(root, SpinX, UpY);
        Release(root, SpinX, UpY);
        Assert.Equal(0, b.ScaleCalls);
    }

    [Fact]
    public void Number_FloatRetypedUnchangedReportsNothing()
    {
        var b = new Binding { Scale = 0.1f };
        var (root, number) = Mount("value=\"{Scale}\" decimals=\"2\" onchange=\"{SetScale}\"", b);
        Assert.Equal("0.10", number.Field.Text);
        TypeText(root, number, "0.1");
        root.SetKeyboardFocus(null);
        Assert.Equal(0, b.ScaleCalls);
    }

    [Fact]
    public void Number_LongAndFloatStepAsTheirTypes()
    {
        var b = new Binding();
        var (rootLong, _) = Mount("value=\"{Big}\" step=\"2\" onchange=\"{SetBig}\"", b);
        Press(rootLong, SpinX, UpY);
        Assert.Equal(7L, b.Big);
        var (rootFloat, _) = Mount("value=\"{Scale}\" step=\"0.25\" onchange=\"{SetScale}\"", b);
        Press(rootFloat, SpinX, UpY);
        Assert.Equal(1.75f, b.Scale);
    }

    [Fact]
    public void Number_FocusAndBlurWithoutEditingDoesNotReport()
    {
        var b = new Binding { Scale = 1.1f, Ratio = 0.123 };
        var (root, _) = Mount("value=\"{Scale}\" onchange=\"{SetScale}\"", b);
        root.SetKeyboardFocus(((UiMarkupNumber)root.Children[0].Children[0]).Field);
        root.SetKeyboardFocus(null);
        Assert.Equal(0, b.ScaleCalls);

        var (root2, number2) = Mount("value=\"{Ratio}\" onchange=\"{SetRatio}\"", b);
        root2.SetKeyboardFocus(number2.Field);
        root2.SetKeyboardFocus(null);
        Assert.Equal(0.123, b.Ratio);
    }

    [Fact]
    public void Number_KeyStepCommitsTypedTextFirst()
    {
        var b = new Binding();
        var (root, number) = Mount(CountAttrs, b);
        TypeText(root, number, "42");
        root.OnKeyDown((int)Key.Up);
        Assert.Equal(47, b.Count);
        Assert.Equal(1, b.Calls);
    }

    [Fact]
    public void Number_WheelStepCommitsTypedTextFirst()
    {
        var b = new Binding();
        var (root, number) = Mount(CountAttrs, b);
        root.OnMouseMove(30, 15);
        TypeText(root, number, "42");
        root.OnScroll(1);
        Assert.Equal(47, b.Count);
        Assert.Equal(1, b.Calls);
    }

    [Fact]
    public void Number_DisablingWhileEditingRestoresInsteadOfCommitting()
    {
        var b = new Binding();
        var (root, number) = Mount(CountAttrs + " enabled=\"{On}\"", b);
        TypeText(root, number, "42");
        b.On = false;
        root.Tick(0.016, 0);
        Assert.Equal(10, b.Count);
        Assert.Equal(0, b.Calls);
    }

    [Fact]
    public void Number_LongHitchFiresOneRepeatStep()
    {
        var b = new Binding();
        var (root, _) = Mount(CountAttrs, b);
        Press(root, SpinX, UpY);
        Assert.Equal(15, b.Count);
        root.Tick(2.0, 0);
        Assert.Equal(20, b.Count);
    }

    private static UiMarkupNumber MountThemed(Binding binding)
    {
        var themes = new PluginUiThemeSettings { Theme = PluginUiTheme.Moss };
        var panel = MarkupDocument.Build(
            "<panel theme=\"plugin\" x=\"0\" y=\"0\" w=\"300\" h=\"100\">"
            + $"<number x=\"8\" y=\"8\" w=\"80\" h=\"20\" {CountAttrs}/></panel>",
            binding, _ => (1u, 16, 16), themes: themes);
        return panel.Children.OfType<UiMarkupNumber>().Single();
    }

    [Fact]
    public void Number_PluginThemeReachesTheInnerField()
    {
        var number = MountThemed(new Binding());
        Assert.Same(PluginUiPalette.Moss, number.ThemePalette);
        Assert.Equal(PluginUiPalette.Moss.Field, number.Field.BackgroundColor);
    }

    [Fact]
    public void Number_ClassicDrawsTwoArrows()
    {
        var (_, number) = Mount(CountAttrs, new Binding());
        var renderer = MarkupDrawProbe.Draw(number);
        Assert.Equal(6, MarkupDrawProbe.CountQuads(renderer, UiMarkupNumber.ClassicSpinArrow));
        Assert.Equal(0, MarkupDrawProbe.CountQuads(renderer, UiMarkupNumber.ClassicSpinPressed));
    }

    [Fact]
    public void Number_HeldSpinDrawsPressed()
    {
        var (root, number) = Mount(CountAttrs, new Binding());
        Press(root, SpinX, UpY);
        var renderer = MarkupDrawProbe.Draw(number);
        Assert.Equal(1, MarkupDrawProbe.CountQuads(renderer, UiMarkupNumber.ClassicSpinPressed));
    }

    [Fact]
    public void Number_DisabledDrawsFaded()
    {
        var (_, number) = Mount(CountAttrs + " enabled=\"false\"", new Binding());
        var renderer = MarkupDrawProbe.Draw(number);
        var faded = UiMarkupNumber.ClassicSpinArrow with { W = UiMarkupToggle.DisabledAlpha };
        Assert.Equal(6, MarkupDrawProbe.CountQuads(renderer, faded));
        Assert.Equal(0, MarkupDrawProbe.CountQuads(renderer, UiMarkupNumber.ClassicSpinArrow));
    }

    [Fact]
    public void Number_ThemedDrawUsesThePalette()
    {
        var number = MountThemed(new Binding());
        var renderer = MarkupDrawProbe.Draw(number);
        Assert.Equal(0, MarkupDrawProbe.CountQuads(renderer, UiMarkupNumber.ClassicSpinArrow));
        Assert.True(MarkupDrawProbe.CountQuads(renderer, PluginUiPalette.Moss.Text) >= 6);
    }
}
