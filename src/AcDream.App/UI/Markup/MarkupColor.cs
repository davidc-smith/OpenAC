using System.Xml.Linq;

namespace AcDream.App.UI.Markup;

internal static class MarkupColor
{
    public static UiMarkupColor Build(XElement el, MarkupBindingSource binding, MarkupBuildContext context)
    {
        var (read, write) = MarkupBindings.BindRequiredColor(
            (string?)el.Attribute("value"), (string?)el.Attribute("onchange"), binding, "color");
        var color = new UiMarkupColor
        {
            Left = MarkupDocument.F(el, "x"),
            Top = MarkupDocument.F(el, "y"),
            Width = MarkupDocument.F(el, "w"),
            Height = MarkupDocument.F(el, "h"),
            ValueSource = read,
            Changed = write,
            AllowAlpha = Alpha(el),
            DatFont = context.DatFont,
            Textures = context.Textures,
        };
        if (context.RecentColors is not null)
            color.Recent = context.RecentColors;
        MarkupDocument.ApplyCommon(color, el, binding);
        return color;
    }

    private static bool Alpha(XElement el) => (string?)el.Attribute("alpha") switch
    {
        null or "false" => false,
        "true" => true,
        var other => throw new FormatException($"<color alpha=\"{other}\"> must be true or false"),
    };
}
