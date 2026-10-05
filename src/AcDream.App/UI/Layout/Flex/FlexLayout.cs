using System;

namespace AcDream.App.UI.Layout.Flex;

/// <summary>
/// A reduced CSS flexbox: direction, gap, padding, justify, align, wrap,
/// grow, shrink, basis, min/max and align-self, plus unbounded scrolling
/// axes. Pure: it reads and writes <see cref="FlexNode"/>s and knows nothing
/// of the UI tree.
///
/// <para>One <see cref="Arrange"/> measures every node once, bottom-up, then
/// places every node top-down. Each leaf's measure callback runs exactly once
/// per arrange, and a repeated arrange of the same tree allocates nothing
/// once its scratch buffers have grown to fit.</para>
///
/// <para>A wrapping container placed across its parent's main axis (a
/// wrapping row inside a column) is sized height-for-width: its extent along
/// the parent's main axis is the lines it makes at the width it is given,
/// not the lines it would make at its narrowest.</para>
/// </summary>
public static class FlexLayout
{
    /// <summary>Thickness of a scrollbar, reserved in a scrolling container's minimum size.</summary>
    public const float ScrollbarThickness = 16f;

    /// <summary>The smallest viewport a scrolling container shrinks to along an axis it scrolls.</summary>
    public const float MinimumScrollViewport = 40f;

    /// <summary>Measures <paramref name="root"/> and every node under it, and returns the root's measurement.</summary>
    public static FlexMeasurement Measure(FlexNode root)
    {
        MeasureNode(root);
        return root.Measured;
    }

    // ── Measure ─────────────────────────────────────────────────────────

    private static void MeasureNode(FlexNode node)
    {
        FlexMeasurement content = node.Measure is { } measure
            ? measure(null)
            : MeasureChildren(node);

        float prefW = Positive(node.Width ?? content.Preferred.Width);
        float prefH = Positive(node.Height ?? content.Preferred.Height);

        // CSS min-width:auto: the content minimum, but never more than an
        // explicit preferred size, and never more than the maximum.
        float autoMinW = node.Width is { } w ? MathF.Min(Positive(w), Positive(content.Minimum.Width)) : Positive(content.Minimum.Width);
        float autoMinH = node.Height is { } h ? MathF.Min(Positive(h), Positive(content.Minimum.Height)) : Positive(content.Minimum.Height);
        float maxW = node.MaxWidth is { } mw ? Positive(mw) : float.MaxValue;
        float maxH = node.MaxHeight is { } mh ? Positive(mh) : float.MaxValue;
        float minW = MathF.Min(node.MinWidth is { } nw ? Positive(nw) : autoMinW, maxW);
        float minH = MathF.Min(node.MinHeight is { } nh ? Positive(nh) : autoMinH, maxH);

        // A wrapping container's cross minimum is the lines it makes at its
        // narrowest; that must not lift its preferred cross size, which is one line.
        float floorW = minW, floorH = minH;
        if (node.Measure is null && node.Wrap)
        {
            if (node.Direction == FlexDirection.Row) floorH = MathF.Min(node.MinHeight is { } eh ? Positive(eh) : 0f, maxH);
            else floorW = MathF.Min(node.MinWidth is { } ew ? Positive(ew) : 0f, maxW);
        }

        node.PreferredUnclamped = new FlexSize(prefW, prefH);
        node.MinimumSize = new FlexSize(minW, minH);
        node.MaximumSize = new FlexSize(maxW, maxH);
        node.Measured = new FlexMeasurement(
            new FlexSize(Math.Clamp(prefW, floorW, maxW), Math.Clamp(prefH, floorH, maxH)),
            new FlexSize(minW, minH));
    }

    private static FlexMeasurement MeasureChildren(FlexNode node)
    {
        bool row = node.Direction == FlexDirection.Row;
        float gap = Positive(node.Gap);
        float prefMain = 0f, prefCross = 0f, minMainSum = 0f, minMainMax = 0f, minCross = 0f;
        int visible = 0;

        for (int i = 0; i < node.Children.Count; i++)
        {
            FlexNode child = node.Children[i];
            if (child.Hidden) continue;
            MeasureNode(child);
            prefMain += MeasuredBase(child, row);
            prefCross = MathF.Max(prefCross, CrossOf(child.Measured.Preferred, row));
            float childMinMain = MainOf(child.MinimumSize, row);
            minMainSum += childMinMain;
            minMainMax = MathF.Max(minMainMax, childMinMain);
            minCross = MathF.Max(minCross, CrossOf(child.MinimumSize, row));
            visible++;
        }

        float gaps = visible > 1 ? gap * (visible - 1) : 0f;
        prefMain += gaps;

        float minMain;
        if (node.Wrap && visible > 0)
        {
            // At its minimum main size a wrapping container is as narrow as its
            // widest item; its minimum cross size is then the lines that makes.
            minMain = minMainMax;
            minCross = WrappedMinimumCross(node, row, minMain, gap);
        }
        else
        {
            minMain = minMainSum + gaps;
        }

        FlexEdges p = node.Padding;
        float padMain = row ? p.Horizontal : p.Vertical;
        float padCross = row ? p.Vertical : p.Horizontal;
        FlexSize preferred = Size(prefMain + padMain, prefCross + padCross, row);
        FlexSize minimum = Size(minMain + padMain, minCross + padCross, row);

        // A scrolling axis only needs a small viewport; the bar for that axis
        // takes room on the other axis.
        float minW = minimum.Width, minH = minimum.Height;
        if (node.ScrollX) minW = MinimumScrollViewport;
        if (node.ScrollY) minH = MinimumScrollViewport;
        if (node.ScrollY) minW += ScrollbarThickness;
        if (node.ScrollX) minH += ScrollbarThickness;
        return new FlexMeasurement(preferred, new FlexSize(minW, minH));
    }

    private static float WrappedMinimumCross(FlexNode node, bool row, float available, float gap)
    {
        float total = 0f, lineMain = 0f, lineCross = 0f;
        int lines = 0, inLine = 0;
        for (int i = 0; i < node.Children.Count; i++)
        {
            FlexNode child = node.Children[i];
            if (child.Hidden) continue;
            float main = MainOf(child.MinimumSize, row);
            if (inLine > 0 && lineMain + gap + main > available)
            {
                total += lineCross;
                lines++;
                lineMain = 0f; lineCross = 0f; inLine = 0;
            }
            lineMain += (inLine > 0 ? gap : 0f) + main;
            lineCross = MathF.Max(lineCross, CrossOf(child.MinimumSize, row));
            inLine++;
        }
        if (inLine > 0) { total += lineCross; lines++; }
        return total + (lines > 1 ? gap * (lines - 1) : 0f);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    /// <summary>The flex base size from the measurement: basis, else the main-axis preferred size (overrides applied, unclamped).</summary>
    private static float MeasuredBase(FlexNode child, bool row) =>
        child.Basis is { } basis ? Positive(basis) : MainOf(child.PreferredUnclamped, row);

    private static float MainOf(FlexSize size, bool row) => row ? size.Width : size.Height;

    private static float CrossOf(FlexSize size, bool row) => row ? size.Height : size.Width;

    private static FlexSize Size(float main, float cross, bool row) =>
        row ? new FlexSize(main, cross) : new FlexSize(cross, main);

    /// <summary>Non-finite or negative computed values are defects; they clamp to 0 so layout never throws.</summary>
    private static float Positive(float value) => float.IsFinite(value) && value > 0f ? value : 0f;
}
