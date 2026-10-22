using System.Xml.Linq;
using AcDream.App.UI;
using AcDream.App.UI.Layout.Flex;

namespace AcDream.App.Tests.UI;

/// <summary>
/// Reading flex attributes from markup, and every build error of spec
/// section 5: unknown keywords, container attributes without layout, item
/// attributes under an absolute container, placement on a flex item,
/// negative and non-finite numbers, and inverted limits.
/// </summary>
public sealed class MarkupFlexAttributesTests
{
    private static XElement El(string xml) => XElement.Parse(xml);

    private static FlexNode Container(string attrs)
    {
        var node = new FlexNode();
        MarkupFlexAttributes.ReadContainer(El($"<group {attrs} />"), node);
        return node;
    }

    private static FlexNode Item(string attrs)
    {
        var node = new FlexNode();
        MarkupFlexAttributes.ReadItem(El($"<label {attrs} />"), node);
        return node;
    }

    [Fact]
    public void Container_defaults_are_css_defaults()
    {
        FlexNode node = Container("layout=\"column\"");

        Assert.Equal(FlexDirection.Column, node.Direction);
        Assert.Equal(0f, node.Gap);
        Assert.Equal(default, node.Padding);
        Assert.Equal(FlexJustify.Start, node.Justify);
        Assert.Equal(FlexAlign.Stretch, node.Align);
        Assert.False(node.Wrap);
    }

    [Fact]
    public void Container_attributes_are_read()
    {
        FlexNode node = Container("layout=\"row\" gap=\"6\" justify=\"space-between\" align=\"center\" wrap=\"true\"");

        Assert.Equal(FlexDirection.Row, node.Direction);
        Assert.Equal(6f, node.Gap);
        Assert.Equal(FlexJustify.SpaceBetween, node.Justify);
        Assert.Equal(FlexAlign.Center, node.Align);
        Assert.True(node.Wrap);
    }

    [Theory]
    [InlineData("8", 8f, 8f, 8f, 8f)]
    [InlineData("4 10", 4f, 10f, 4f, 10f)]
    [InlineData("1 2 3 4", 1f, 2f, 3f, 4f)]
    [InlineData("1,2,3,4", 1f, 2f, 3f, 4f)]
    public void Padding_takes_one_two_or_four_numbers_in_css_order(
        string padding, float top, float right, float bottom, float left)
    {
        Assert.Equal(new FlexEdges(top, right, bottom, left), Container($"layout=\"row\" padding=\"{padding}\"").Padding);
    }

    [Fact]
    public void Item_defaults_are_css_defaults()
    {
        FlexNode node = Item("");

        Assert.Equal(0f, node.Grow);
        Assert.Equal(1f, node.Shrink);
        Assert.Null(node.Basis);
        Assert.Null(node.Width);
        Assert.Null(node.Height);
        Assert.Null(node.MinWidth);
        Assert.Null(node.MaxWidth);
        Assert.Null(node.MinHeight);
        Assert.Null(node.MaxHeight);
        Assert.Null(node.AlignSelf);
    }

    [Fact]
    public void Item_attributes_are_read()
    {
        FlexNode node = Item(
            "grow=\"2\" shrink=\"0\" basis=\"40\" w=\"50\" h=\"20\" minw=\"10\" maxw=\"90\" minh=\"5\" maxh=\"30\" alignself=\"end\"");

        Assert.Equal(2f, node.Grow);
        Assert.Equal(0f, node.Shrink);
        Assert.Equal(40f, node.Basis);
        Assert.Equal(50f, node.Width);
        Assert.Equal(20f, node.Height);
        Assert.Equal(10f, node.MinWidth);
        Assert.Equal(90f, node.MaxWidth);
        Assert.Equal(5f, node.MinHeight);
        Assert.Equal(30f, node.MaxHeight);
        Assert.Equal(FlexAlign.End, node.AlignSelf);
    }

    [Fact]
    public void Basis_auto_is_the_default()
    {
        Assert.Null(Item("basis=\"auto\"").Basis);
    }

    [Theory]
    [InlineData("<group layout=\"grid\" />", "layout=\"grid\"")]
    [InlineData("<group layout=\"row\" justify=\"around\" />", "justify=\"around\"")]
    [InlineData("<group layout=\"row\" align=\"baseline\" />", "align=\"baseline\"")]
    [InlineData("<group layout=\"row\" wrap=\"yes\" />", "wrap=\"yes\"")]
    [InlineData("<group layout=\"column\" wrap=\"true\" />", "wrap=\"true\"")]
    [InlineData("<group layout=\"row\" gap=\"-1\" />", "gap=\"-1\"")]
    [InlineData("<group layout=\"row\" gap=\"NaN\" />", "gap=\"NaN\"")]
    [InlineData("<group layout=\"row\" gap=\"Infinity\" />", "gap=\"Infinity\"")]
    [InlineData("<group layout=\"row\" gap=\"wide\" />", "gap=\"wide\"")]
    [InlineData("<group layout=\"row\" padding=\"1 2 3\" />", "padding=\"1 2 3\"")]
    [InlineData("<group layout=\"row\" padding=\"1 x\" />", "padding=\"1 x\"")]
    [InlineData("<group layout=\"row\" padding=\"-2\" />", "padding=\"-2\"")]
    [InlineData("<group layout=\"row\" padding=\"NaN\" />", "padding=\"NaN\"")]
    public void Bad_container_values_name_the_attribute_and_value(string xml, string expected)
    {
        XElement el = El(xml);
        var ex = Assert.Throws<FormatException>(() =>
        {
            if (MarkupFlexAttributes.IsContainer(el)) MarkupFlexAttributes.ReadContainer(el, new FlexNode());
        });
        Assert.Contains("<group>", ex.Message);
        Assert.Contains(expected, ex.Message);
    }

    [Theory]
    [InlineData("gap=\"4\"")]
    [InlineData("padding=\"4\"")]
    [InlineData("justify=\"end\"")]
    [InlineData("align=\"center\"")]
    [InlineData("wrap=\"true\"")]
    public void Container_attributes_without_layout_are_errors(string attr)
    {
        var ex = Assert.Throws<FormatException>(() => MarkupFlexAttributes.RejectContainer(El($"<group name=\"g\" {attr} />")));
        Assert.Contains("<group name=\"g\">", ex.Message);
        Assert.Contains(attr, ex.Message);
        Assert.Contains("layout", ex.Message);
    }

    [Theory]
    [InlineData("grow=\"1\"")]
    [InlineData("shrink=\"0\"")]
    [InlineData("basis=\"20\"")]
    [InlineData("alignself=\"end\"")]
    [InlineData("minw=\"5\"")]
    [InlineData("maxw=\"5\"")]
    [InlineData("minh=\"5\"")]
    [InlineData("maxh=\"5\"")]
    public void Item_attributes_under_an_absolute_container_are_errors(string attr)
    {
        var ex = Assert.Throws<FormatException>(() => MarkupFlexAttributes.RejectItem(El($"<label {attr} />")));
        Assert.Contains(attr, ex.Message);
    }

    [Theory]
    [InlineData("x=\"4\"")]
    [InlineData("y=\"4\"")]
    [InlineData("anchor=\"right\"")]
    public void Placement_on_a_flex_item_is_an_error(string attr)
    {
        var ex = Assert.Throws<FormatException>(() => Item(attr));
        Assert.Contains(attr, ex.Message);
        Assert.Contains("flex item", ex.Message);
    }

    [Theory]
    [InlineData("grow=\"-1\"")]
    [InlineData("shrink=\"-0.5\"")]
    [InlineData("basis=\"-3\"")]
    [InlineData("w=\"-3\"")]
    [InlineData("grow=\"NaN\"")]
    [InlineData("shrink=\"Infinity\"")]
    [InlineData("basis=\"-Infinity\"")]
    [InlineData("w=\"NaN\"")]
    [InlineData("h=\"Infinity\"")]
    [InlineData("minw=\"NaN\"")]
    [InlineData("maxw=\"Infinity\"")]
    [InlineData("minh=\"NaN\"")]
    [InlineData("maxh=\"NaN\"")]
    [InlineData("alignself=\"middle\"")]
    [InlineData("basis=\"big\"")]
    public void Bad_item_values_name_the_attribute_and_value(string attr)
    {
        var ex = Assert.Throws<FormatException>(() => Item(attr));
        Assert.Contains("<label>", ex.Message);
        Assert.Contains(attr, ex.Message);
    }

    [Theory]
    [InlineData("minw=\"50\" maxw=\"40\"", "minw=\"50\" is greater than maxw=\"40\"")]
    [InlineData("minh=\"9\" maxh=\"8.5\"", "minh=\"9\" is greater than maxh=\"8.5\"")]
    public void A_minimum_above_its_maximum_is_an_error(string attrs, string expected)
    {
        var ex = Assert.Throws<FormatException>(() => Item(attrs));
        Assert.Contains(expected, ex.Message);
    }
}
