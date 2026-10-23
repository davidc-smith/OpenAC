using System.Xml.Linq;

namespace AcDream.App.UI.Markup;

internal static class MarkupRadio
{
    public static UiMarkupRadio Build(XElement el, object binding, MarkupBuildContext context)
    {
        var radio = new UiMarkupRadio
        {
            AcceptsFocus = true,
            FocusOnMouseClick = false,
            TabStop = true,
            Left = MarkupDocument.F(el, "x"),
            Top = MarkupDocument.F(el, "y"),
            Width = MarkupDocument.F(el, "w"),
            Height = MarkupDocument.F(el, "h"),
            ItemsSource = Items((string?)el.Attribute("items"), binding),
            SelectedSource = MarkupDocument.BindRequiredIntReader(
                (string?)el.Attribute("selected"), binding, "radio selected"),
            Changed = MarkupBindings.BindIntAction(
                (string?)el.Attribute("onchange"), binding, "radio onchange"),
            Horizontal = Horizontal(el),
            Segmented = Segmented(el),
            DatFont = context.DatFont,
        };
        string? color = (string?)el.Attribute("color");
        if (color is not null)
            radio.TextColor = MarkupDocument.Color(color);
        context.BindColor(color, value => radio.TextColor = value, source => radio.TextColorSource = source);
        MarkupDocument.ApplyCommon(radio, el, binding);
        return radio;
    }

    /// <summary>A bound string list, or a literal list separated by <c>|</c>.</summary>
    private static Func<IReadOnlyList<string>> Items(string? expression, object binding)
    {
        if (string.IsNullOrEmpty(expression))
            throw new FormatException("radio items must list at least one option");
        if (MarkupDocument.IsBinding(expression))
            return MarkupDocument.BindStringList(expression, binding, "radio items", preserveSnapshot: true);
        string[] options = expression.Split('|');
        if (Array.Exists(options, o => o.Length == 0))
            throw new FormatException("radio items must not have an empty option");
        return () => options;
    }

    private static bool Horizontal(XElement el) => (string?)el.Attribute("orientation") switch
    {
        null or "vertical" => false,
        "horizontal" => true,
        var other => throw new FormatException(
            $"<radio orientation=\"{other}\"> must be vertical or horizontal"),
    };

    private static bool Segmented(XElement el) => (string?)el.Attribute("style") switch
    {
        null or "radio" => false,
        "segmented" => true,
        var other => throw new FormatException($"<radio style=\"{other}\"> must be radio or segmented"),
    };
}
