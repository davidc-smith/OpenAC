using System;

namespace AcDream.App.UI.Layout.Flex;

/// <summary>
/// The smallest box a flex tree really fits in. <see cref="FlexLayout.Measure"/>
/// gives a minimum built from the items' own minimums, which is exact for
/// plain rows and columns but only an estimate once lines wrap (lines break
/// on the items' preferred sizes, and greedy breaking is not monotonic).
/// This lays the tree out at that estimate and grows each non-scrolling axis
/// until the content no longer reaches past the box, so a window held at its
/// minimum never clips its own content.
/// </summary>
public static class FlexFit
{
    /// <summary>Layouts tried before giving up on growing further; each settles one dependent axis.</summary>
    private const int MaxPasses = 4;

    /// <summary>
    /// The smallest whole-point size, starting from the measured minimum, at
    /// which <paramref name="root"/>'s content fits. The tree is left arranged
    /// at the last size tried; arrange it again before reading its rects.
    /// </summary>
    public static FlexSize Minimum(FlexNode root)
    {
        FlexSize measured = FlexLayout.Measure(root).Minimum;
        float width = MathF.Ceiling(measured.Width), height = MathF.Ceiling(measured.Height);
        for (int pass = 0; pass < MaxPasses; pass++)
        {
            FlexLayout.Arrange(root, new FlexRect(0f, 0f, width, height));
            float needWidth = root.ScrollX ? width : MathF.Ceiling(root.ContentSize.Width);
            float needHeight = root.ScrollY ? height : MathF.Ceiling(root.ContentSize.Height);
            if (needWidth <= width && needHeight <= height) break;
            width = MathF.Max(width, needWidth);
            height = MathF.Max(height, needHeight);
        }
        return new FlexSize(width, height);
    }
}
