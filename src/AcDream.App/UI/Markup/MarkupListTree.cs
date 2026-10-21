using System.Xml.Linq;

namespace AcDream.App.UI.Markup;

/// <summary>A tree list's attributes: <c>tree levels keys expanded onexpand collapsed</c>.</summary>
internal static class MarkupListTree
{
    private static readonly string[] TreeAttributes = ["levels", "keys", "expanded", "onexpand", "collapsed"];

    public static void Apply(XElement el, UiMarkupList list, object binding, MarkupBuildContext context)
    {
        if (!MarkupDocument.B(el, "tree", false))
        {
            foreach (string name in TreeAttributes)
            {
                if (el.Attribute(name) is not null)
                    throw new FormatException($"<list {name}> needs tree=\"true\"");
            }
            return;
        }
        if (list.Columns is not { Count: > 0 } columns)
            throw new FormatException("<list tree=\"true\"> needs <column> children");
        int treeColumn = -1;
        for (int i = 0; i < columns.Count; i++)
        {
            if (columns[i].Kind == UiMarkupListColumnKind.Text)
            {
                treeColumn = i;
                break;
            }
        }
        if (treeColumn < 0)
            throw new FormatException("<list tree=\"true\"> needs a text column to draw the tree in");

        list.TreeColumn = treeColumn;
        list.TreeLevelsSource = MarkupBindings.BindRequiredIntListReader(
            (string?)el.Attribute("levels"), binding, "list levels");
        list.TreeKeysSource = MarkupDocument.BindStringList((string?)el.Attribute("keys"), binding, "list keys");
        string? expanded = (string?)el.Attribute("expanded");
        if (expanded is not null)
        {
            if (el.Attribute("collapsed") is not null)
                throw new FormatException("<list collapsed> only applies when expanded is not bound");
            list.ExpandedSource = MarkupBindings.BindRequiredStringSetReader(expanded, binding, "list expanded");
        }
        else
        {
            list.StartCollapsed = MarkupDocument.B(el, "collapsed", false);
            list.RememberedExpansion = new RememberedValue(context.ControlState, list.Name, "expanded");
        }
        list.ExpansionChanged = MarkupBindings.BindStringBoolAction(
            (string?)el.Attribute("onexpand"), binding, "list onexpand");
    }
}
