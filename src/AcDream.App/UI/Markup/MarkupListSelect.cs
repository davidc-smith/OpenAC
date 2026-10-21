using System.Xml.Linq;

namespace AcDream.App.UI.Markup;

/// <summary>A list's selection: <c>selectmode</c>, <c>selected</c>, <c>onchange</c> and <c>selectionband</c>.</summary>
internal static class MarkupListSelect
{
    public static void Apply(XElement el, UiMarkupList list, object binding)
    {
        string? mode = (string?)el.Attribute("selectmode");
        string? selected = (string?)el.Attribute("selected");
        string? onChange = (string?)el.Attribute("onchange");
        switch (mode ?? "single")
        {
            case "single":
                Action<int>? changed = MarkupDocument.BindIntAction(onChange, binding);
                if (onChange is not null && changed is null)
                {
                    throw new FormatException(
                        $"<list onchange=\"{onChange}\"> did not resolve to an "
                        + $"Action<int> property on {binding.GetType().Name}");
                }
                list.SelectedIndexSource = MarkupDocument.BindRequiredIntReader(selected, binding, "list selected");
                list.SelectionChanged = changed;
                list.SelectionBandEnabled = MarkupDocument.B(el, "selectionband", false);
                break;
            case "multi":
                if (!MarkupDocument.B(el, "selectionband", true))
                    throw new FormatException("<list selectmode=\"multi\"> always draws the selection band; remove selectionband=\"false\"");
                list.MultiSelect = true;
                list.SelectedRowsSource = MarkupBindings.BindRequiredIntListReader(selected, binding, "list selected");
                list.SelectedRowsChanged = MarkupBindings.BindIntListAction(onChange, binding, "list onchange");
                list.SelectionBandEnabled = true;
                break;
            default:
                throw new FormatException($"<list selectmode=\"{mode}\"> must be single or multi");
        }
    }
}
