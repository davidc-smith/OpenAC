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
        public Action<float> SetScale => v => Scale = v;
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
}
