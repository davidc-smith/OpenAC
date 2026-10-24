using System.Xml.Linq;

namespace AcDream.App.UI.Markup;

internal static class MarkupSeparator
{
    public static UiMarkupSeparator Build(XElement el, MarkupBindingSource binding, MarkupBuildContext context)
    {
        var separator = new UiMarkupSeparator
        {
            Left = MarkupDocument.F(el, "x"),
            Top = MarkupDocument.F(el, "y"),
            Width = MarkupDocument.F(el, "w"),
            Height = MarkupDocument.F(el, "h"),
        };
        string? color = (string?)el.Attribute("color");
        if (color is not null)
            separator.Color = MarkupDocument.Color(color);
        context.BindColor(color, value => separator.Color = value, source => separator.ColorSource = source);
        MarkupDocument.ApplyCommon(separator, el, binding);
        return separator;
    }
}
