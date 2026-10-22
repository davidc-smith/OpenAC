using System.Xml.Linq;

namespace AcDream.App.UI.Markup;

/// <summary>
/// Builds an element's <c>&lt;contextmenu&gt;</c> child, if it has one, into a
/// <see cref="UiMarkupContextMenu"/> on the element it was built into.
/// </summary>
internal static class MarkupContextMenu
{
    public static UiMarkupContextMenu? Attach(XElement ownerEl, UiElement owner, MarkupBindingSource binding, MarkupBuildContext context)
    {
        XElement[] menus = ownerEl.Elements("contextmenu").ToArray();
        if (menus.Length == 0)
            return null;
        if (menus.Length > 1)
            throw new FormatException($"<{ownerEl.Name.LocalName}> may have only one <contextmenu>");
        XElement el = menus[0];
        if (el.HasElements)
            throw new FormatException("<contextmenu> takes no children");

        Action<int>? opened = MarkupBindings.BindIntAction(
            (string?)el.Attribute("onopen"), binding, "contextmenu onopen");
        string? enabled = (string?)el.Attribute("enabled");
        var menu = new UiMarkupContextMenu
        {
            DatFont = context.DatFont,
            ItemsSource = Items((string?)el.Attribute("items"), binding),
            ItemsEnabledSource = enabled is null
                ? null
                : MarkupDocument.BindBoolList(enabled, binding, "contextmenu enabled"),
            Selected = MarkupDocument.BindRequiredIntAction(
                (string?)el.Attribute("onselect"), binding, "contextmenu onselect"),
            Opening = owner is UiMarkupList list
                ? (x, y) =>
                {
                    int row = list.PrepareContextMenu(x, y);
                    opened?.Invoke(row);
                }
                : opened is null ? null : (_, _) => opened(-1),
        };
        owner.AddChild(menu);
        owner.ContextMenu = menu;
        return menu;
    }

    /// <summary>A bound string list, or a literal list separated by <c>|</c>.</summary>
    private static Func<IReadOnlyList<string>> Items(string? expression, MarkupBindingSource binding)
    {
        if (string.IsNullOrEmpty(expression))
            throw new FormatException("contextmenu items must list at least one item");
        if (MarkupDocument.IsBinding(expression))
            return MarkupDocument.BindStringList(expression, binding, "contextmenu items");
        string[] items = expression.Split('|');
        if (Array.Exists(items, item => item.Length == 0))
            throw new FormatException("contextmenu items must not have an empty item");
        return () => items;
    }
}
