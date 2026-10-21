using System.Xml.Linq;

namespace AcDream.App.UI.Markup;

/// <summary>The header, sort and width attributes of a column <c>&lt;list&gt;</c>.</summary>
internal static class MarkupListHeader
{
    private static readonly string[] ListAttributes =
        ["header", "resizable", "sortcolumn", "sortdescending", "onsort", "widths", "onresize"];

    /// <summary>The single-column form has no header; its attributes there are mistakes.</summary>
    public static void RejectWithoutColumns(XElement el)
    {
        foreach (string name in ListAttributes)
        {
            if (el.Attribute(name) is not null)
                throw new FormatException($"<list {name}> needs <column> children");
        }
    }

    public static void Apply(
        XElement el, IReadOnlyList<XElement> columnEls, UiMarkupList list, object binding,
        MarkupBuildContext context)
    {
        list.ShowHeader = Bool(el, "header");
        IReadOnlyList<UiMarkupListColumn> columns = list.Columns!;
        for (int i = 0; i < columns.Count; i++)
            ApplyColumn(columnEls[i], columns[i], i, binding);
    }

    private static void ApplyColumn(XElement columnEl, UiMarkupListColumn column, int index, object binding)
    {
        string? header = (string?)columnEl.Attribute("header");
        if (header is not null)
            column.HeaderSource = MarkupDocument.BindString(header, binding);
    }

    internal static bool Bool(XElement el, string name) => (string?)el.Attribute(name) switch
    {
        null or "false" => false,
        "true" => true,
        var other => throw new FormatException($"<list {name}=\"{other}\"> must be true or false"),
    };
}
