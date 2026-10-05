using AcDream.App.UI.Layout.Flex;

namespace AcDream.App.Tests.UI.Layout.Flex;

/// <summary>Builders for flex trees in tests: leaves with a fixed measurement, and containers.</summary>
internal static class FlexTestNodes
{
    public static FlexNode Leaf(float width, float height, float? minWidth = null, float? minHeight = null)
    {
        var measurement = new FlexMeasurement(
            new FlexSize(width, height),
            new FlexSize(minWidth ?? width, minHeight ?? height));
        return new FlexNode { Measure = _ => measurement };
    }

    public static FlexNode Row(params FlexNode[] children) => Container(FlexDirection.Row, children);

    public static FlexNode Column(params FlexNode[] children) => Container(FlexDirection.Column, children);

    private static FlexNode Container(FlexDirection direction, FlexNode[] children)
    {
        var node = new FlexNode { Direction = direction };
        node.Children.AddRange(children);
        return node;
    }
}
