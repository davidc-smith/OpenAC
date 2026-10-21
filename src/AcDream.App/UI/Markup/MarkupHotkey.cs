using System.Xml.Linq;

namespace AcDream.App.UI.Markup;

internal static class MarkupHotkey
{
    public static UiMarkupHotkey Build(XElement el, object binding, MarkupBuildContext context)
    {
        var hotkey = new UiMarkupHotkey
        {
            Left = MarkupDocument.F(el, "x"),
            Top = MarkupDocument.F(el, "y"),
            Width = MarkupDocument.F(el, "w"),
            Height = MarkupDocument.F(el, "h"),
            ValueSource = MarkupBindings.BindRequiredStringReader(
                (string?)el.Attribute("value"), binding, "hotkey value"),
            Changed = MarkupBindings.BindRequiredStringAction(
                (string?)el.Attribute("onchange"), binding, "hotkey onchange"),
            DescribeKey = context.DescribeKey,
            DatFont = context.DatFont,
        };
        MarkupDocument.ApplyCommon(hotkey, el, binding);
        return hotkey;
    }
}
