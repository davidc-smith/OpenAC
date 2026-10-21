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
        string? sortColumn = (string?)el.Attribute("sortcolumn");
        string? sortDescending = (string?)el.Attribute("sortdescending");
        if ((sortColumn is null) != (sortDescending is null))
            throw new FormatException("<list sortcolumn> and <list sortdescending> are bound together or not at all");
        if (sortColumn is not null)
        {
            list.SortColumnSource = MarkupDocument.BindRequiredIntReader(sortColumn, binding, "list sortcolumn");
            list.SortDescendingSource = MarkupDocument.BindRequiredBoolReader(sortDescending, binding, "list sortdescending");
        }
        list.SortChanged = MarkupBindings.BindIntBoolAction((string?)el.Attribute("onsort"), binding, "list onsort");
        if (list.SortColumnSource is null)
            list.RememberedSort = new RememberedValue(context.ControlState, list.Name, "sort");
        IReadOnlyList<UiMarkupListColumn> columns = list.Columns!;
        for (int i = 0; i < columns.Count; i++)
            ApplyColumn(columnEls[i], columns[i], i, binding);
    }

    private static void ApplyColumn(XElement columnEl, UiMarkupListColumn column, int index, object binding)
    {
        string? header = (string?)columnEl.Attribute("header");
        if (header is not null)
            column.HeaderSource = MarkupDocument.BindString(header, binding);
        UiMarkupListSortKind sort = (string?)columnEl.Attribute("sort") switch
        {
            null or "none" => UiMarkupListSortKind.None,
            "text" => UiMarkupListSortKind.Text,
            "number" => UiMarkupListSortKind.Number,
            "bool" => UiMarkupListSortKind.Bool,
            var other => throw new FormatException(
                $"column[{index}] sort=\"{other}\" must be text, number, bool or none"),
        };
        string? keys = (string?)columnEl.Attribute("sortkeys");
        if (keys is not null && sort is not (UiMarkupListSortKind.Text or UiMarkupListSortKind.Number))
            throw new FormatException($"column[{index}] sortkeys needs sort=\"text\" or sort=\"number\"");
        if (sort == UiMarkupListSortKind.Bool && column.Kind != UiMarkupListColumnKind.Check)
            throw new FormatException($"column[{index}] sort=\"bool\" needs a check or checkbox column");
        if (sort is UiMarkupListSortKind.Text or UiMarkupListSortKind.Number && keys is null
            && column.Kind is not (UiMarkupListColumnKind.Text or UiMarkupListColumnKind.Button))
        {
            throw new FormatException(
                $"column[{index}] sort=\"{columnEl.Attribute("sort")!.Value}\" on a {column.Kind} column needs sortkeys");
        }
        column.Sort = sort;
        if (keys is not null)
            (column.SortNumberKeys, column.SortTextKeys) =
                MarkupBindings.BindSortKeys(keys, binding, $"column[{index}] sortkeys");
    }

    internal static bool Bool(XElement el, string name) => (string?)el.Attribute(name) switch
    {
        null or "false" => false,
        "true" => true,
        var other => throw new FormatException($"<list {name}=\"{other}\"> must be true or false"),
    };
}
