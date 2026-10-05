namespace AcDream.App.UI.Layout.Flex;

/// <summary>The main axis of a flex container.</summary>
public enum FlexDirection { Row, Column }

/// <summary>Where a line's items sit along the main axis when they leave space over.</summary>
public enum FlexJustify { Start, Center, End, SpaceBetween }

/// <summary>Where an item sits across its line.</summary>
public enum FlexAlign { Start, Center, End, Stretch }

/// <summary>A size in points.</summary>
public readonly record struct FlexSize(float Width, float Height);

/// <summary>A rectangle in points, relative to the parent node's top-left corner.</summary>
public readonly record struct FlexRect(float X, float Y, float Width, float Height);

/// <summary>Space between a container's edge and its items, in points.</summary>
public readonly record struct FlexEdges(float Top, float Right, float Bottom, float Left)
{
    public static FlexEdges All(float value) => new(value, value, value, value);

    public float Horizontal => Left + Right;

    public float Vertical => Top + Bottom;
}

/// <summary>
/// What a node needs: the size it would like, and the smallest size it can
/// be given without its content breaking. Both include a container's padding.
/// </summary>
public readonly record struct FlexMeasurement(FlexSize Preferred, FlexSize Minimum);

/// <summary>
/// Measures a leaf's content. <paramref name="availableWidth"/> is the width
/// the leaf may wrap to, or null when it is unconstrained; v1 controls do not
/// wrap and are always asked with null.
/// </summary>
public delegate FlexMeasurement FlexMeasure(float? availableWidth);
