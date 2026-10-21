using System.Globalization;
using System.Xml.Linq;

namespace AcDream.App.UI.Markup;

internal static class MarkupSplit
{
    public static UiMarkupSplit Build(XElement el, object binding, MarkupBuildContext context)
    {
        bool horizontal = (string?)el.Attribute("orientation") switch
        {
            null or "horizontal" => true,
            "vertical" => false,
            var other => throw new FormatException(
                $"<split orientation=\"{other}\"> must be horizontal or vertical"),
        };
        XElement[] panes = el.Elements().ToArray();
        if (panes.Length != 2 || panes.Any(pane => pane.Name.LocalName != "group"))
            throw new FormatException("<split> must contain exactly two <group> elements");

        var split = new UiMarkupSplit(horizontal)
        {
            Left = MarkupDocument.F(el, "x"),
            Top = MarkupDocument.F(el, "y"),
            Width = MarkupDocument.F(el, "w"),
            Height = MarkupDocument.F(el, "h"),
            Min = Length(el, "min") ?? 0f,
            Max = Length(el, "max"),
            Changed = MarkupBindings.BindFloatAction((string?)el.Attribute("onchange"), binding, "split onchange"),
        };
        if (split.Max < split.Min)
            throw new FormatException($"<split max=\"{split.Max}\"> must not be less than min=\"{split.Min}\"");
        float room = MathF.Max(0f, (horizontal ? split.Width : split.Height) - UiMarkupSplit.BarSize);
        split.DefaultPosition = Length(el, "at") ?? MathF.Floor(room / 2f);

        string? positionAttr = (string?)el.Attribute("position");
        if (positionAttr is not null)
            split.PositionSource = MarkupBindings.BindRequiredFloatReader(positionAttr, binding, "split position");

        MarkupDocument.ApplyCommon(split, el, binding);
        // The bar takes the hover, so it carries the tooltip's popup settings;
        // its text comes from the split (see UiMarkupSplitBar.GetTooltipText).
        UiMarkupSplitBar bar = split.Bar;
        bar.AuthoredTooltipEnabled = split.AuthoredTooltipEnabled;
        bar.AuthoredTooltipRootElementId = split.AuthoredTooltipRootElementId;
        bar.AuthoredTooltipLayoutDid = split.AuthoredTooltipLayoutDid;
        bar.AuthoredTooltipDelaySeconds = split.AuthoredTooltipDelaySeconds;
        if (split.PositionSource is null)
            split.Remembered = new RememberedValue(context.ControlState, split.Name, "position");

        if (context.AddChild is not { } addChild)
            throw new InvalidOperationException("<split> needs a build context that can add children");
        addChild(split, panes[0]);
        UiElement first = split.Children[^1];
        addChild(split, panes[1]);
        UiElement second = split.Children[^1];
        split.SetPanes(first, second);
        // Lay out at the authored start now, so the panes' children record their
        // anchors against the pane sizes the markup was written for, whatever
        // position is stored or bound.
        split.LayoutAtDefault();
        return split;
    }

    private static float? Length(XElement el, string name)
    {
        string? text = (string?)el.Attribute(name);
        if (text is null)
            return null;
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            || !float.IsFinite(value) || value < 0f)
        {
            throw new FormatException($"<split {name}=\"{text}\"> must be a number of pixels, 0 or more");
        }
        return value;
    }
}
