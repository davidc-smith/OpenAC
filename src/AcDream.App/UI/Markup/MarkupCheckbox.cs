using System.Xml.Linq;

namespace AcDream.App.UI.Markup;

internal static class MarkupCheckbox
{
    public static UiMarkupCheckbox Build(XElement el, object binding, MarkupBuildContext context)
    {
        string? caption = (string?)el.Attribute("text");
        var box = new UiMarkupCheckbox
        {
            AcceptsFocus = true,
            FocusOnMouseClick = false,
            TabStop = true,
            Left = MarkupDocument.F(el, "x"),
            Top = MarkupDocument.F(el, "y"),
            Width = MarkupDocument.F(el, "w"),
            Height = MarkupDocument.F(el, "h"),
            Text = caption ?? string.Empty,
            TextSource = MarkupDocument.BindString(caption, binding),
            CheckedSource = MarkupBindings.BindRequiredCheckState(
                (string?)el.Attribute("checked"), binding, "checkbox checked"),
            Changed = MarkupBindings.BindBoolAction(
                (string?)el.Attribute("onchange"), binding, "checkbox onchange"),
            DatFont = context.DatFont,
        };
        string? color = (string?)el.Attribute("color");
        if (color is not null)
            box.TextColor = MarkupDocument.Color(color);
        context.BindColor(color, value => box.TextColor = value, source => box.TextColorSource = source);
        MarkupDocument.ApplyCommon(box, el, binding);
        return box;
    }
}
