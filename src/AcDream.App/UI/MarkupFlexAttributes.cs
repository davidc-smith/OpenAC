using System.Globalization;
using System.Xml.Linq;
using AcDream.App.UI.Layout.Flex;

namespace AcDream.App.UI;

/// <summary>
/// Reads the flex attributes of plugin markup onto <see cref="FlexNode"/>s.
/// Inside flex (a container with <c>layout</c> and its items) every misuse
/// fails the build: placement attributes on a flex item, unknown keywords,
/// negative or non-finite numbers and inverted limits. Markup that does not
/// use flex was built before these attributes existed, when they were
/// ignored, so on it a stray one (container attributes without
/// <c>layout</c>, item attributes under an absolute container, an unknown
/// <c>layout</c> or <c>scroll</c>) is reported to <c>warnings</c> and ignored.
/// </summary>
internal static class MarkupFlexAttributes
{
    private static readonly string[] ContainerAttributes = ["gap", "padding", "justify", "align", "wrap"];
    private static readonly string[] ItemOnlyAttributes = ["grow", "shrink", "basis", "alignself"];
    private static readonly string[] LimitAttributes = ["minw", "maxw", "minh", "maxh"];
    private static readonly string[] PlacementAttributes = ["x", "y", "anchor"];

    /// <summary>
    /// Whether <paramref name="el"/> is a flex container. An unknown
    /// <c>layout</c> is reported and leaves it absolute.
    /// </summary>
    internal static bool IsContainer(XElement el, ICollection<string> warnings)
    {
        switch ((string?)el.Attribute("layout"))
        {
            case null: return false;
            case "row" or "column": return true;
            case string other:
                Warn(warnings, Ignored(el, "layout", other, "must be row or column"));
                return false;
        }
    }

    /// <summary>Reports container attributes on <paramref name="el"/>, which has no <c>layout</c>.</summary>
    internal static void RejectContainer(XElement el, ICollection<string> warnings)
    {
        foreach (string name in ContainerAttributes)
            if (el.Attribute(name) is { } a)
                Warn(warnings, Ignored(el, name, a.Value, "needs layout=\"row\" or layout=\"column\" on the same element"));
    }

    /// <summary>Reads the container attributes of <paramref name="el"/>, which has <c>layout</c>, onto <paramref name="node"/>.</summary>
    internal static void ReadContainer(XElement el, FlexNode node)
    {
        node.Direction = (string?)el.Attribute("layout") == "row" ? FlexDirection.Row : FlexDirection.Column;
        node.Gap = NonNegative(el, "gap") ?? 0f;
        node.Padding = Padding(el);
        node.Justify = (string?)el.Attribute("justify") switch
        {
            null or "start" => FlexJustify.Start,
            "center" => FlexJustify.Center,
            "end" => FlexJustify.End,
            "space-between" => FlexJustify.SpaceBetween,
            string other => throw Error(el, "justify", other, "must be start, center, end or space-between"),
        };
        node.Align = Align(el, "align") ?? FlexAlign.Stretch;
        node.Wrap = (string?)el.Attribute("wrap") switch
        {
            null or "false" => false,
            "true" => true,
            string other => throw Error(el, "wrap", other, "must be true or false"),
        };
        if (node.Wrap && node.Direction == FlexDirection.Column)
            throw Error(el, "wrap", "true", "is not supported on a column (only rows wrap in this version)");
    }

    /// <summary>
    /// Reads the item attributes of <paramref name="el"/>, a child of a flex
    /// container, onto <paramref name="node"/>. <c>x</c>, <c>y</c> and
    /// <c>anchor</c> are errors: the container places its items.
    /// </summary>
    internal static void ReadItem(XElement el, FlexNode node)
    {
        foreach (string name in PlacementAttributes)
            if (el.Attribute(name) is { } a)
                throw Error(el, name, a.Value, "cannot be used on a flex item; its container places it");

        node.Grow = NonNegative(el, "grow") ?? 0f;
        node.Shrink = NonNegative(el, "shrink") ?? 1f;
        node.Basis = (string?)el.Attribute("basis") is "auto" ? null : NonNegative(el, "basis");
        node.Width = NonNegative(el, "w");
        node.Height = NonNegative(el, "h");
        node.MinWidth = NonNegative(el, "minw");
        node.MaxWidth = NonNegative(el, "maxw");
        node.MinHeight = NonNegative(el, "minh");
        node.MaxHeight = NonNegative(el, "maxh");
        node.AlignSelf = Align(el, "alignself");
        CheckLimits(el, "minw", node.MinWidth, "maxw", node.MaxWidth);
        CheckLimits(el, "minh", node.MinHeight, "maxh", node.MaxHeight);
    }

    /// <summary>Reports item attributes on <paramref name="el"/>, a child of an absolute container.</summary>
    internal static void RejectItem(XElement el, ICollection<string> warnings)
    {
        foreach (string name in ItemOnlyAttributes.Concat(LimitAttributes))
            if (el.Attribute(name) is { } a)
                Warn(warnings, Ignored(el, name, a.Value, "only applies to a child of a layout=\"row\" or layout=\"column\" container"));
    }

    /// <summary>
    /// The axes <c>scroll</c> asks <paramref name="el"/> (a group or the root
    /// panel) to scroll on; neither when it is absent. An unknown value is
    /// reported and scrolls neither.
    /// </summary>
    internal static (bool X, bool Y) Scroll(XElement el, ICollection<string> warnings)
    {
        switch ((string?)el.Attribute("scroll"))
        {
            case null: return (false, false);
            case "x": return (true, false);
            case "y": return (false, true);
            case "both": return (true, true);
            case string other:
                Warn(warnings, Ignored(el, "scroll", other, "must be x, y or both"));
                return (false, false);
        }
    }

    /// <summary>
    /// A root panel's authored content-area size or limit, checked like a
    /// flex number: null when absent.
    /// </summary>
    internal static float? RootSize(XElement root, string name) => NonNegative(root, name);

    private static FlexAlign? Align(XElement el, string name) => (string?)el.Attribute(name) switch
    {
        null => null,
        "start" => FlexAlign.Start,
        "center" => FlexAlign.Center,
        "end" => FlexAlign.End,
        "stretch" => FlexAlign.Stretch,
        string other => throw Error(el, name, other, "must be start, center, end or stretch"),
    };

    private static FlexEdges Padding(XElement el)
    {
        if ((string?)el.Attribute("padding") is not { } raw) return default;
        string[] parts = raw.Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var values = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            values[i] = Number(el, "padding", parts[i], raw);
        foreach (float v in values)
            if (v < 0f) throw Error(el, "padding", raw, "must not be negative");
        return values.Length switch
        {
            1 => FlexEdges.All(values[0]),
            2 => new FlexEdges(values[0], values[1], values[0], values[1]),
            4 => new FlexEdges(values[0], values[1], values[2], values[3]),
            _ => throw Error(el, "padding", raw, "must be 1, 2 or 4 numbers (all; vertical horizontal; top right bottom left)"),
        };
    }

    private static float? NonNegative(XElement el, string name)
    {
        if ((string?)el.Attribute(name) is not { } raw) return null;
        float value = Number(el, name, raw, raw);
        if (value < 0f) throw Error(el, name, raw, "must not be negative");
        return value;
    }

    private static float Number(XElement el, string name, string text, string raw)
    {
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            throw Error(el, name, raw, "is not a number");
        if (!float.IsFinite(value))
            throw Error(el, name, raw, "must be a finite number");
        return value;
    }

    private static void CheckLimits(XElement el, string minName, float? min, string maxName, float? max)
    {
        if (min is { } lo && max is { } hi && lo > hi)
            throw new FormatException(
                $"{Identity(el)} {minName}=\"{Format(lo)}\" is greater than {maxName}=\"{Format(hi)}\"");
    }

    private static FormatException Error(XElement el, string name, string value, string problem) =>
        new($"{Identity(el)} {name}=\"{value}\" {problem}");

    /// <summary>
    /// Adds <paramref name="message"/> unless it is already there: every
    /// <c>&lt;repeat&gt;</c> row is built from the same template, so its stray
    /// attribute is reported once, however many rows there are or come later.
    /// </summary>
    internal static void Warn(ICollection<string> warnings, string message)
    {
        if (!warnings.Contains(message)) warnings.Add(message);
    }

    /// <summary>The warning for an attribute the build ignores.</summary>
    internal static string Ignored(XElement el, string name, string value, string problem) =>
        $"{Identity(el)} {name}=\"{value}\" {problem}; ignored";

    private static string Format(float value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Identity(XElement el)
    {
        string? name = (string?)el.Attribute("name") ?? (string?)el.Attribute("id");
        return name is null ? $"<{el.Name.LocalName}>" : $"<{el.Name.LocalName} name=\"{name}\">";
    }
}
