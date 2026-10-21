using System.Globalization;
using System.Numerics;
using System.Xml.Linq;

namespace AcDream.App.UI.Markup;

internal static class MarkupNumber
{
    public static UiMarkupNumber Build(XElement el, object binding, MarkupBuildContext context)
    {
        var (read, valueType) = MarkupBindings.BindRequiredNumberReader(
            (string?)el.Attribute("value"), binding, "number value");
        Action<decimal> changed = MarkupBindings.BindRequiredNumberAction(
            (string?)el.Attribute("onchange"), binding, valueType, "number onchange");

        bool integral = MarkupBindings.IsIntegralNumber(valueType);
        (decimal typeMin, decimal typeMax) = MarkupBindings.NumberRange(valueType);
        decimal min = Decimal(el, "min", typeMin);
        decimal max = Decimal(el, "max", typeMax);
        decimal step = Decimal(el, "step", 1m);
        int decimals = Decimals(el, integral ? 0 : 2);

        if (max < min)
            throw new FormatException($"<number min=\"{min}\" max=\"{max}\"> must have max >= min");
        if (min < typeMin || max > typeMax)
            throw new FormatException(
                $"<number min=\"{min}\" max=\"{max}\"> must fit a {valueType.Name} value");
        if (step <= 0m)
            throw new FormatException($"<number step=\"{step}\"> must be greater than 0");
        if (decimals is < 0 or > 10)
            throw new FormatException($"<number decimals=\"{decimals}\"> must be from 0 to 10");
        if (Math.Round(step, decimals) != step)
            throw new FormatException(
                $"<number step=\"{step}\"> must not have more decimal places than decimals=\"{decimals}\"");
        if (integral && decimals != 0)
            throw new FormatException(
                $"<number decimals=\"{decimals}\"> must be 0 for a {valueType.Name} value");

        var number = new UiMarkupNumber
        {
            Left = MarkupDocument.F(el, "x"),
            Top = MarkupDocument.F(el, "y"),
            Width = MarkupDocument.F(el, "w"),
            Height = MarkupDocument.F(el, "h"),
            Min = min,
            Max = max,
            Step = step,
            Decimals = decimals,
            ValueSource = read,
            Changed = changed,
        };
        UiField field = number.Field;
        field.DatFont = context.DatFont;
        string? background = (string?)el.Attribute("background");
        string? color = (string?)el.Attribute("color");
        field.BackgroundColor = background is null
            ? new Vector4(0f, 0f, 0f, 0.9f)
            : MarkupDocument.Color(background);
        field.TextColor = color is null
            ? new Vector4(0.91f, 0.87f, 0.76f, 1f)
            : MarkupDocument.Color(color);
        context.BindColor(background, value => field.BackgroundColor = value,
            source => field.BackgroundColorSource = source);
        context.BindColor(color, value => field.TextColor = value,
            source => number.TextColorSource = source);

        number.SyncField();
        field.SetText(number.Format(read()));
        MarkupDocument.ApplyCommon(number, el, binding);
        return number;
    }

    private static decimal Decimal(XElement el, string attribute, decimal fallback)
    {
        string? raw = (string?)el.Attribute(attribute);
        if (raw is null)
            return fallback;
        if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value))
            throw new FormatException($"<number {attribute}=\"{raw}\"> is not a number");
        return value;
    }

    private static int Decimals(XElement el, int fallback)
    {
        string? raw = (string?)el.Attribute("decimals");
        if (raw is null)
            return fallback;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            throw new FormatException($"<number decimals=\"{raw}\"> is not a whole number");
        return value;
    }
}
