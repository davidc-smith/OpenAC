using System.Reflection;
using System.Xml.Linq;
using AcDream.UI.Abstractions.Input;

namespace AcDream.App.UI.Markup;

/// <summary>
/// Builds <c>&lt;repeat items="{List}" key="{Id}"&gt;</c>: checks it, places
/// its <see cref="UiMarkupRepeat"/> marker in the surrounding flex container
/// and builds the first copies of its template.
/// </summary>
internal static class MarkupRepeat
{
    /// <summary>What the enclosing <c>AddElement</c> got, which every copy is built with.</summary>
    internal sealed record BuildArgs(
        Func<uint, (uint, int, int)> Resolve,
        UiDatFont? DatFont,
        IMarkupIconResolver? Icons,
        UiPluginMarkupPanel? ThemedPanel,
        PluginUiThemeSettings? Themes,
        IMarkupControlState? ControlState,
        Func<KeyChord, string>? DescribeKey,
        IMarkupTextureService? Textures,
        MarkupRecentColors RecentColors);

    /// <summary>Builds one element into a container, as the document builds any element.</summary>
    internal delegate void AddElement(
        UiElement parent, XElement el, MarkupBindingSource source, IMarkupControlState? controlState, UiFlexBox flex);

    public static UiMarkupRepeat Build(
        UiElement parent, XElement el, MarkupBindingSource binding, UiFlexBox? flex, BuildArgs args, AddElement add)
    {
        if (flex is null)
            throw new FormatException("<repeat> goes in a flex container (a window or <group> with layout)");
        foreach (XAttribute attribute in el.Attributes())
        {
            if (attribute.Name.LocalName is not ("items" or "key"))
                throw new FormatException(
                    $"<repeat> takes only items= and key=, not {attribute.Name.LocalName}=; "
                    + "put layout attributes on the template's elements");
        }
        if (el.Element("contextmenu") is not null)
            throw new FormatException("<contextmenu> goes inside a <repeat>'s template element, not directly in <repeat>");

        string? itemsExpression = (string?)el.Attribute("items");
        if (itemsExpression is null || !MarkupDocument.IsBinding(itemsExpression))
            throw new FormatException("<repeat items=\"…\"> must be a {binding} to a list");
        PropertyInfo items = binding.Property(itemsExpression[1..^1])
            ?? throw new FormatException($"<repeat items=\"{itemsExpression}\"> did not resolve to a property on {binding.TypeName}");
        Type itemType = ItemType(items.PropertyType)
            ?? throw new FormatException(
                $"<repeat items=\"{itemsExpression}\"> must be an IReadOnlyList<T>, IList<T> or IEnumerable<T> "
                + $"property, not {items.PropertyType.Name}");

        PropertyInfo? key = null;
        string? keyExpression = (string?)el.Attribute("key");
        if (keyExpression is not null)
        {
            if (!MarkupDocument.IsBinding(keyExpression))
                throw new FormatException($"<repeat key=\"{keyExpression}\"> must be a {{binding}} to a property of the item");
            key = itemType.GetProperty(keyExpression[1..^1])
                ?? throw new FormatException($"<repeat key=\"{keyExpression}\"> did not resolve to a property on {itemType.Name}");
        }

        var marker = new UiMarkupRepeat(el.Elements().ToArray(), itemsExpression, binding, items, itemType, key,
            parent, flex, args, add);
        parent.AddChild(marker);
        if (binding.PendingRepeats is { } pending)
            pending.Add(marker);
        else
            marker.BuildInitialCopies();
        return marker;
    }

    /// <summary>
    /// The <c>T</c> of a list property: the declared type when it is
    /// <c>IReadOnlyList&lt;T&gt;</c>, <c>IList&lt;T&gt;</c> or <c>IEnumerable&lt;T&gt;</c>,
    /// else the single <c>IEnumerable&lt;T&gt;</c> it implements. Null for a
    /// string, or for a type with none or several.
    /// </summary>
    internal static Type? ItemType(Type declared)
    {
        if (declared == typeof(string))
            return null;
        if (declared.IsGenericType)
        {
            Type definition = declared.GetGenericTypeDefinition();
            if (definition == typeof(IReadOnlyList<>) || definition == typeof(IList<>) || definition == typeof(IEnumerable<>))
                return declared.GetGenericArguments()[0];
        }
        Type[] enumerables = declared.GetInterfaces()
            .Where(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            .ToArray();
        return enumerables.Length == 1 ? enumerables[0].GetGenericArguments()[0] : null;
    }

    /// <summary>
    /// Throws when an element with a remembered name (<c>name</c>, else <c>id</c>)
    /// sits inside a <c>&lt;repeat&gt;</c> without <c>key</c> (R8): its
    /// remembered state would have no per-item name.
    /// </summary>
    internal static void RejectUnkeyedNames(XElement root)
    {
        foreach (XElement el in root.Descendants())
        {
            if ((el.Attribute("name") ?? el.Attribute("id")) is not { } name)
                continue;
            if (el.Ancestors("repeat").Any(static repeat => repeat.Attribute("key") is null))
                throw new FormatException(
                    $"<{el.Name.LocalName} {name.Name.LocalName}=\"{name.Value}\"> inside a <repeat> needs key= on every enclosing repeat");
        }
    }
}
