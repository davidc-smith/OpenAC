using System.Xml.Linq;

namespace AcDream.App.UI.Markup;

internal static class MarkupExpander
{
    public static UiMarkupExpander Build(XElement el, MarkupBindingSource binding, MarkupBuildContext context)
    {
        float height = MarkupDocument.F(el, "h");
        if (height < UiMarkupExpander.HeaderHeight)
        {
            throw new FormatException(
                $"<expander h=\"{height}\"> must be at least {UiMarkupExpander.HeaderHeight} (its header)");
        }

        var expander = new UiMarkupExpander
        {
            Left = MarkupDocument.F(el, "x"),
            Top = MarkupDocument.F(el, "y"),
            Width = MarkupDocument.F(el, "w"),
            Height = height,
            OpenByDefault = Open(el),
            Changed = MarkupBindings.BindBoolAction(
                (string?)el.Attribute("onchange"), binding, "expander onchange"),
        };
        expander.LayoutParts();

        string? expandedAttr = (string?)el.Attribute("expanded");
        if (expandedAttr is not null)
            expander.ExpandedSource = MarkupDocument.BindRequiredBoolReader(expandedAttr, binding, "expander expanded");

        UiMarkupExpanderHeader header = expander.Header;
        string? caption = (string?)el.Attribute("text");
        header.Text = caption ?? string.Empty;
        header.TextSource = MarkupDocument.BindString(caption, binding);
        header.DatFont = context.DatFont;
        string? color = (string?)el.Attribute("color");
        if (color is not null)
            header.TextColor = MarkupDocument.Color(color);
        context.BindColor(color, value => header.TextColor = value, source => header.TextColorSource = source);

        MarkupDocument.ApplyCommon(expander, el, binding);
        // The header takes the hover, so it carries the tooltip's popup settings;
        // its text comes from the expander (see UiMarkupExpanderHeader.GetTooltipText).
        header.AuthoredTooltipEnabled = expander.AuthoredTooltipEnabled;
        header.AuthoredTooltipRootElementId = expander.AuthoredTooltipRootElementId;
        header.AuthoredTooltipLayoutDid = expander.AuthoredTooltipLayoutDid;
        header.AuthoredTooltipDelaySeconds = expander.AuthoredTooltipDelaySeconds;
        if (expander.ExpandedSource is null)
            expander.Remembered = new RememberedValue(context.ControlState, expander.Name, "expanded");

        if (context.AddChild is { } addChild)
        {
            foreach (XElement child in el.Elements())
                addChild(expander.Content, child);
        }
        return expander;
    }

    private static bool Open(XElement el) => (string?)el.Attribute("open") switch
    {
        null or "true" => true,
        "false" => false,
        var other => throw new FormatException($"<expander open=\"{other}\"> must be true or false"),
    };
}
