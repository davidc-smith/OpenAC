using System;
using System.Collections.Generic;

namespace AcDream.App.UI.Layout.Flex;

/// <summary>
/// A reduced CSS flexbox: direction, gap, padding, justify, align, wrap,
/// grow, shrink, basis, min/max and align-self, plus unbounded scrolling
/// axes. Pure: it reads and writes <see cref="FlexNode"/>s and knows nothing
/// of the UI tree.
///
/// <para><see cref="Measure"/> and <see cref="Arrange"/> each measure the
/// whole tree, bottom-up; <see cref="Arrange"/> then places every node
/// top-down. Each leaf's measure callback runs exactly once per call, and a
/// repeated arrange of the same tree allocates nothing once its scratch
/// buffers have grown to fit.</para>
///
/// <para>Sizing is height-for-width at any depth. A node whose extent along
/// one axis depends on the size it gets along the other (a wrapping
/// container, or any container holding one, that does not scroll) is
/// "size-dependent": its parent finds that extent by laying the node's
/// subtree out at the size it gives it, with the dependent axis unbounded,
/// rather than trusting a measurement made before any size was known. A
/// wrapping row inside a padded column inside a column is therefore as tall
/// as the lines it makes at the width it actually gets.</para>
///
/// <para>A size-dependent node's measured minimum is still its narrowest
/// layout (it drives the window minimum) but does not lift its preferred
/// size on the dependent axis. Greedy line breaking is not monotonic: more
/// room can make wider lines when items differ in cross size, and items that
/// prefer more than their minimum (nested wrapping containers) can make more
/// lines at a wider size than at the narrowest. A root wrapping container's
/// <see cref="FlexMeasurement.Minimum"/> cross size is therefore a close
/// estimate rather than a strict bound; nested containers are sized from
/// their real lines and are unaffected.</para>
///
/// <para>Each size-dependent node depends along one axis. A node that depends
/// both ways (a wrapping column holding a wrapping row) is resolved along its
/// own wrapping axis, or height-for-width when it does not wrap, and the
/// other way it is laid out from its preferred sizes; such a tree can
/// overflow its rect even at its minimum.</para>
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

    /// <summary>
    /// Measures the tree, then lays it out with <paramref name="root"/>
    /// occupying <paramref name="bounds"/> (stored unchanged as the root's
    /// <see cref="FlexNode.Rect"/>). Every descendant's rect is relative to
    /// its own parent.
    /// </summary>
    public static void Arrange(FlexNode root, FlexRect bounds)
    {
        MeasureNode(root);
        root.Rect = bounds;
        ArrangeChildren(root, Positive(bounds.Width), Positive(bounds.Height), descend: true);
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

        MarkSizeDependent(node);

        // A wrapping container's cross minimum is the lines it makes at its
        // narrowest, and a size-dependent container's minimum on its dependent
        // axis is its narrowest layout; neither lifts the preferred size, which
        // is the layout at its preferred size along the other axis (one line).
        float floorW = minW, floorH = minH;
        if (node.Measure is null && (node.Wrap || node.SizeDependent))
        {
            bool heightDepends = node.SizeDependent ? node.HeightForWidth : node.Direction == FlexDirection.Row;
            if (heightDepends) floorH = MathF.Min(node.MinHeight is { } eh ? Positive(eh) : 0f, maxH);
            else floorW = MathF.Min(node.MinWidth is { } ew ? Positive(ew) : 0f, maxW);
        }

        node.PreferredUnclamped = new FlexSize(prefW, prefH);
        node.MinimumSize = new FlexSize(minW, minH);
        node.MaximumSize = new FlexSize(maxW, maxH);
        node.Measured = new FlexMeasurement(
            new FlexSize(Math.Clamp(prefW, floorW, maxW), Math.Clamp(prefH, floorH, maxH)),
            new FlexSize(minW, minH));
    }

    /// <summary>
    /// Sets <see cref="FlexNode.SizeDependent"/> once the node's children are
    /// measured: a container that does not scroll depends on its size when it
    /// wraps (its cross axis depends on its main size, whatever its children
    /// do) or when any visible child does (along the same physical axis as
    /// that child; should children depend along different axes,
    /// height-for-width wins). A leaf never depends on its size in v1, and a
    /// scrolling container keeps its measured size.
    /// </summary>
    private static void MarkSizeDependent(FlexNode node)
    {
        node.SizeDependent = false;
        node.HeightForWidth = false;
        node.ScrollsDependentContent = false;
        if (node.Measure is not null) return;
        bool dependent = false, heightForWidth = false;
        if (node.Wrap)
        {
            dependent = true;
            heightForWidth = node.Direction == FlexDirection.Row;
        }
        else
        {
            for (int i = 0; i < node.Children.Count; i++)
            {
                FlexNode child = node.Children[i];
                if (child.Hidden || !child.SizeDependent) continue;
                dependent = true;
                heightForWidth |= child.HeightForWidth;
            }
        }
        node.HeightForWidth = heightForWidth;
        if (node.ScrollX || node.ScrollY) node.ScrollsDependentContent = dependent;
        else node.SizeDependent = dependent;
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
            // Each item counts at its hypothetical size, as the arrange pass
            // starts it: its base clamped to its limits.
            float childMinMain = MainOf(child.MinimumSize, row);
            prefMain += Math.Clamp(MeasuredBase(child, row), childMinMain, MathF.Max(childMinMain, MainOf(child.MaximumSize, row)));
            prefCross = MathF.Max(prefCross, CrossOf(child.Measured.Preferred, row));
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

    /// <summary>The cross size of the lines a wrapping container makes from its items' minimum sizes at <paramref name="available"/>.</summary>
    private static float WrappedMinimumCross(FlexNode node, bool row, float available, float gap) =>
        BreakLines(node, row, available, gap, LineSizes.Minimum, null);

    // ── Arrange ─────────────────────────────────────────────────────────

    // Per-container scratch, in sixths of FlexNode.Scratch for `count` children:
    // [0, c) resolved main size, [c, 2c) frozen flag, [2c, 3c) flex base size,
    // [3c, 4c) minimum main size, [4c, 5c) preferred cross size (for a child
    // sized across, the cross size it is given), [5c, 6c) cross size a
    // stretch never goes below.
    private const int TargetSlot = 0, FrozenSlot = 1, BaseSlot = 2, MinSlot = 3, CrossSlot = 4, CrossFloorSlot = 5;

    /// <summary>
    /// Lays out <paramref name="node"/>'s children inside a box of
    /// <paramref name="width"/> x <paramref name="height"/> and sets its
    /// <see cref="FlexNode.ContentSize"/>. A null size leaves that axis
    /// unbounded, as scrolling does: <see cref="Extent"/> uses that to find how
    /// far a size-dependent node's content reaches. With
    /// <paramref name="descend"/> false only this node's children are placed
    /// (enough for its content size); the real arrange that follows overwrites
    /// them and lays out the rest of the subtree.
    /// </summary>
    private static void ArrangeChildren(FlexNode node, float? width, float? height, bool descend)
    {
        node.ScrollbarX = false;
        node.ScrollbarY = false;
        if (node.Measure is null && (node.ScrollX || node.ScrollY) && Positive(node.ScrollbarSize) > 0f
            && width is { } w && height is { } h)
        {
            SettleScrollbars(node, w, h, descend);
            return;
        }
        node.Viewport = new FlexSize(width ?? 0f, height ?? 0f);
        ArrangeContent(node, width, height, descend);
    }

    /// <summary>
    /// Lays a scrolling container out with the bars its content needs. It
    /// starts with none, lays its items out in the viewport, adds every bar
    /// whose axis now overflows, and repeats in the smaller viewport until
    /// the bars stop changing. A vertical bar narrows the viewport, which can
    /// make the content overflow horizontally, and a horizontal bar does the
    /// reverse; bars are only ever added, so this ends after at most three
    /// layouts of the container's own items. Its subtree is then laid out
    /// once, in the settled viewport.
    /// </summary>
    private static void SettleScrollbars(FlexNode node, float width, float height, bool descend)
    {
        float bar = Positive(node.ScrollbarSize);
        bool barX = false, barY = false;
        float viewW = Positive(width), viewH = Positive(height);
        for (int pass = 0; pass < 3; pass++)
        {
            ArrangeContent(node, viewW, viewH, descend: false);
            bool needX = barX || (node.ScrollX && node.ContentSize.Width > viewW);
            bool needY = barY || (node.ScrollY && node.ContentSize.Height > viewH);
            if (needX == barX && needY == barY) break;
            barX = needX;
            barY = needY;
            viewW = Positive(width - (barY ? bar : 0f));
            viewH = Positive(height - (barX ? bar : 0f));
        }
        ArrangeContent(node, viewW, viewH, descend);
        node.ScrollbarX = barX;
        node.ScrollbarY = barY;
        node.Viewport = new FlexSize(viewW, viewH);
    }

    /// <summary>
    /// <see cref="ArrangeChildren"/> without the scrollbars: lays the items
    /// out in exactly <paramref name="width"/> x <paramref name="height"/>.
    /// </summary>
    private static void ArrangeContent(FlexNode node, float? width, float? height, bool descend)
    {
        if (node.Measure is not null)
        {
            node.ContentSize = new FlexSize(width ?? 0f, height ?? 0f);
            return;
        }

        bool row = node.Direction == FlexDirection.Row;
        FlexEdges p = node.Padding;
        float gap = Positive(node.Gap);
        bool mainScrolls = row ? node.ScrollX : node.ScrollY;
        bool crossScrolls = row ? node.ScrollY : node.ScrollX;
        float? mainSize = row ? width : height, crossSize = row ? height : width;
        float? availableMain = mainScrolls || mainSize is not { } ms ? null : Positive(ms - (row ? p.Horizontal : p.Vertical));
        float? availableCross = crossScrolls || crossSize is not { } cs ? null : Positive(cs - (row ? p.Vertical : p.Horizontal));
        float originMain = row ? p.Left : p.Top;
        float originCross = row ? p.Top : p.Left;
        bool singleLine = !node.Wrap || availableMain is null;

        int count = node.Children.Count;
        if (node.Scratch.Length < count * 6) node.Scratch = new float[count * 6];
        ResolveBases(node, row, count, availableCross, singleLine);

        node.LineEnds.Clear();
        if (!node.Wrap || availableMain is not { } available) node.LineEnds.Add(count);
        else BreakLines(node, row, available, gap, LineSizes.Hypothetical, node.LineEnds);

        int lineCount = node.LineEnds.Count;
        float linesCross = 0f;
        int lineStart = 0;
        for (int line = 0; line < lineCount; line++)
        {
            int lineEnd = node.LineEnds[line];
            ResolveFlexibleLengths(node, row, count, lineStart, lineEnd, availableMain, gap);
            ResolveAlongCross(node, row, count, lineStart, lineEnd);
            linesCross += LineCross(node, row, lineStart, lineEnd);
            lineStart = lineEnd;
        }

        // Spare cross space goes to the lines, as CSS's default align-content
        // does; a single line simply takes the whole cross size.
        float lineGaps = lineCount > 1 ? gap * (lineCount - 1) : 0f;
        float extraPerLine = 0f;
        if (availableCross is { } crossSpace && lineCount > 0)
        {
            extraPerLine = singleLine
                ? crossSpace - linesCross
                : MathF.Max(0f, (crossSpace - linesCross - lineGaps) / lineCount);
        }

        // Edges start at the leading padding, so an empty container still
        // reaches across its padding on both sides.
        float maxMainEdge = originMain, maxCrossEdge = originCross;
        float crossCursor = originCross;
        lineStart = 0;
        for (int line = 0; line < lineCount; line++)
        {
            int lineEnd = node.LineEnds[line];
            float lineCross = MathF.Max(0f, LineCross(node, row, lineStart, lineEnd) + extraPerLine);
            PlaceLine(node, row, count, lineStart, lineEnd, availableMain, gap, originMain, crossCursor, lineCross);
            for (int i = lineStart; i < lineEnd; i++)
            {
                FlexNode child = node.Children[i];
                if (child.Hidden) continue;
                FlexRect r = child.Rect;
                maxMainEdge = MathF.Max(maxMainEdge, row ? r.X + r.Width : r.Y + r.Height);
                maxCrossEdge = MathF.Max(maxCrossEdge, row ? r.Y + r.Height : r.X + r.Width);
            }
            crossCursor += lineCross + gap;
            lineStart = lineEnd;
        }

        for (int i = 0; descend && i < count; i++)
        {
            FlexNode child = node.Children[i];
            if (child.Hidden) { child.Rect = default; continue; }
            ArrangeChildren(child, child.Rect.Width, child.Rect.Height, descend: true);
        }

        float trailMain = row ? p.Right : p.Bottom;
        float trailCross = row ? p.Bottom : p.Right;
        node.ContentSize = Size(maxMainEdge + trailMain, maxCrossEdge + trailCross, row);
    }

    /// <summary>
    /// Fills each visible child's flex base size, minimum main size and cross
    /// sizes. Most children use their measurement; a size-dependent child
    /// whose dependent axis is this container's main axis gets its one cross
    /// size here, and its main size is the extent of its content laid out at
    /// that size. The place pass gives it exactly that cross size, so its rect
    /// always holds the content its extent was computed for.
    /// </summary>
    private static void ResolveBases(
        FlexNode node, bool row, int count, float? availableCross, bool singleLine)
    {
        float[] s = node.Scratch;
        for (int i = 0; i < count; i++)
        {
            FlexNode child = node.Children[i];
            if (child.Hidden) continue;

            if (!IsLaidAcross(node, child))
            {
                s[BaseSlot * count + i] = MeasuredBase(child, row);
                s[MinSlot * count + i] = MainOf(child.MinimumSize, row);
                s[CrossSlot * count + i] = CrossOf(child.Measured.Preferred, row);
                s[CrossFloorSlot * count + i] = CrossOf(child.MinimumSize, row);
                continue;
            }

            // The child's size along our main axis depends on its size across
            // it: find the cross size it gets (stretched over a non-wrapping
            // line, else its preferred size within the space), then the extent
            // its content reaches along our main axis at that size. A wrapping
            // line is not known yet, so it never stretches the child: a wider
            // line would leave the extent made for a narrower one.
            float? explicitCross = row ? child.Height : child.Width;
            float minCross = CrossOf(child.MinimumSize, row), maxCross = CrossOf(child.MaximumSize, row);
            FlexAlign align = child.AlignSelf ?? node.Align;
            bool stretched = singleLine && align == FlexAlign.Stretch && explicitCross is null;
            float cross = CrossOf(child.Measured.Preferred, row);
            if (availableCross is { } space) cross = stretched ? space : MathF.Min(cross, space);
            cross = Math.Clamp(cross, minCross, MathF.Max(minCross, maxCross));
            s[CrossSlot * count + i] = cross;
            s[CrossFloorSlot * count + i] = cross;

            float extent = Extent(child, widthGiven: !row, cross);
            float? explicitMain = row ? child.Width : child.Height;
            float? explicitMin = row ? child.MinWidth : child.MinHeight;
            float maxMain = MainOf(child.MaximumSize, row);
            // A scrolling child prefers its whole content but may shrink to its viewport minimum.
            float contentMin = child.SizeDependent ? extent : MainOf(child.MinimumSize, row);
            float autoMin = explicitMain is { } e ? MathF.Min(Positive(e), contentMin) : contentMin;
            s[BaseSlot * count + i] = child.Basis is { } basis ? Positive(basis) : explicitMain is { } em ? Positive(em) : extent;
            s[MinSlot * count + i] = MathF.Min(explicitMin is { } m ? Positive(m) : autoMin, maxMain);
        }
    }

    /// <summary>
    /// Whether <paramref name="child"/> is size-dependent along
    /// <paramref name="parent"/>'s main axis (a wrapping row, or a column
    /// holding one, inside a column), so its size along that axis depends on
    /// the cross size it gets.
    /// </summary>
    private static bool IsSizedAcross(FlexNode parent, FlexNode child) =>
        child.SizeDependent && child.HeightForWidth == (parent.Direction == FlexDirection.Column);

    /// <summary>
    /// Whether <paramref name="child"/> scrolls along <paramref name="parent"/>'s
    /// main axis (and not across it) over content that is size-dependent along
    /// that axis: a <c>scroll="y"</c> wrapping row in a column. It is sized like
    /// a child sized across, by the extent of its content at the cross size it
    /// gets, so it shows all of it when there is room; its minimum stays its
    /// scroll viewport, so it scrolls when there is not.
    /// </summary>
    private static bool IsScrolledAcross(FlexNode parent, FlexNode child)
    {
        if (!child.ScrollsDependentContent) return false;
        bool column = parent.Direction == FlexDirection.Column;
        if (child.HeightForWidth != column) return false;
        return column ? child.ScrollY && !child.ScrollX : child.ScrollX && !child.ScrollY;
    }

    /// <summary>Whether the child's main size is found by laying its content out at the cross size it gets.</summary>
    private static bool IsLaidAcross(FlexNode parent, FlexNode child) =>
        IsSizedAcross(parent, child) || IsScrolledAcross(parent, child);

    /// <summary>
    /// Whether <paramref name="child"/> is size-dependent along
    /// <paramref name="parent"/>'s cross axis (a wrapping row, or a column
    /// holding one, inside a row), so its cross size depends on the main size
    /// the line resolves for it.
    /// </summary>
    private static bool IsSizedAlong(FlexNode parent, FlexNode child) =>
        child.SizeDependent && child.HeightForWidth == (parent.Direction == FlexDirection.Row);

    /// <summary>
    /// How far a size-dependent <paramref name="node"/>'s content reaches
    /// along one axis when it is given <paramref name="given"/> along the
    /// other: its height at that width when <paramref name="widthGiven"/>,
    /// else its width at that height, padding included. The node will get the
    /// size snapped to whole points, which is the floor or the ceiling of
    /// <paramref name="given"/>. Greedy line breaking is not monotonic (more
    /// room can make wider lines when items differ in cross size), so the
    /// extent is the larger of the two layouts.
    /// </summary>
    private static float Extent(FlexNode node, bool widthGiven, float given)
    {
        float low = MathF.Floor(Positive(given)), high = MathF.Ceiling(Positive(given));
        float extent = LaidOutExtent(node, widthGiven, low);
        return high == low ? extent : MathF.Max(extent, LaidOutExtent(node, widthGiven, high));
    }

    /// <summary>
    /// Lays <paramref name="node"/>'s children out at <paramref name="size"/>
    /// on one axis with the other unbounded and reads how far they reach. The
    /// measure pass is not repeated, so leaves are still measured once per
    /// arrange; the node's real arrange later overwrites what this writes.
    /// </summary>
    private static float LaidOutExtent(FlexNode node, bool widthGiven, float size)
    {
        if (widthGiven)
        {
            ArrangeChildren(node, size, null, descend: false);
            return node.ContentSize.Height;
        }
        ArrangeChildren(node, null, size, descend: false);
        return node.ContentSize.Width;
    }

    /// <summary>Which sizes <see cref="BreakLines"/> reads for each item.</summary>
    private enum LineSizes
    {
        /// <summary>The measured minimum sizes: the measure pass's narrowest layout.</summary>
        Minimum,

        /// <summary>The arrange pass's hypothetical main sizes and cross sizes from the scratch.</summary>
        Hypothetical,
    }

    /// <summary>
    /// The one greedy line breaker, shared by both passes so they can never
    /// disagree on where lines break: an item starts a new line when, with
    /// the gap before it, it would take the line past
    /// <paramref name="available"/>. Fills <paramref name="ends"/>, when
    /// given, with each line's exclusive end index, and returns the lines'
    /// cross sizes (each its largest item) plus the gaps between them.
    /// </summary>
    private static float BreakLines(FlexNode node, bool row, float available, float gap, LineSizes sizes, List<int>? ends)
    {
        int count = node.Children.Count;
        bool minimum = sizes == LineSizes.Minimum;
        float total = 0f, lineMain = 0f, lineCross = 0f;
        int lines = 0, inLine = 0;
        for (int i = 0; i < count; i++)
        {
            FlexNode child = node.Children[i];
            if (child.Hidden) continue;
            float main = minimum ? MainOf(child.MinimumSize, row) : Hypothetical(node, child, row, count, i);
            float cross = minimum ? CrossOf(child.MinimumSize, row) : node.Scratch[CrossSlot * count + i];
            if (inLine > 0 && lineMain + gap + main > available)
            {
                ends?.Add(i);
                total += lineCross;
                lines++;
                lineMain = 0f; lineCross = 0f; inLine = 0;
            }
            lineMain += (inLine > 0 ? gap : 0f) + main;
            lineCross = MathF.Max(lineCross, cross);
            inLine++;
        }
        if (inLine > 0) { total += lineCross; lines++; }
        ends?.Add(count);
        return total + (lines > 1 ? gap * (lines - 1) : 0f);
    }

    /// <summary>
    /// CSS "resolve flexible lengths" for one line: writes each visible item's
    /// main size into the target slot. Items whose clamped size differs from
    /// their flexed size are frozen and the rest re-flexed until nothing changes.
    /// </summary>
    private static void ResolveFlexibleLengths(
        FlexNode node, bool row, int count, int start, int end, float? availableMain, float gap)
    {
        float[] s = node.Scratch;
        int target = TargetSlot * count, frozen = FrozenSlot * count, bases = BaseSlot * count, mins = MinSlot * count;

        int visible = 0;
        float hypotheticalSum = 0f;
        for (int i = start; i < end; i++)
        {
            FlexNode child = node.Children[i];
            if (child.Hidden) continue;
            s[target + i] = Hypothetical(node, child, row, count, i);
            hypotheticalSum += s[target + i];
            visible++;
        }
        if (availableMain is not { } available || visible == 0) return;

        float gaps = visible > 1 ? gap * (visible - 1) : 0f;
        bool growing = available - gaps - hypotheticalSum > 0f;
        for (int i = start; i < end; i++)
        {
            FlexNode child = node.Children[i];
            if (child.Hidden) continue;
            float factor = growing ? Positive(child.Grow) : Positive(child.Shrink);
            float baseSize = s[bases + i];
            bool isFrozen = factor == 0f
                || (growing && baseSize > s[target + i])
                || (!growing && baseSize < s[target + i]);
            s[frozen + i] = isFrozen ? 1f : 0f;
        }

        // CSS: the initial free space is taken once, after the initial freeze;
        // factors summing below 1 take that share of it whenever it is smaller
        // than the space actually left.
        float initialFree = 0f;
        for (int pass = 0; pass <= visible; pass++)
        {
            float used = gaps, factorSum = 0f, scaledShrinkSum = 0f;
            for (int i = start; i < end; i++)
            {
                FlexNode child = node.Children[i];
                if (child.Hidden) continue;
                if (s[frozen + i] != 0f) { used += s[target + i]; continue; }
                used += s[bases + i];
                factorSum += growing ? Positive(child.Grow) : Positive(child.Shrink);
                scaledShrinkSum += Positive(child.Shrink) * s[bases + i];
            }
            if (factorSum == 0f) break;

            float free = available - used;
            if (pass == 0) initialFree = free;
            if (factorSum < 1f && MathF.Abs(initialFree * factorSum) < MathF.Abs(free)) free = initialFree * factorSum;

            float violation = 0f;
            for (int i = start; i < end; i++)
            {
                FlexNode child = node.Children[i];
                if (child.Hidden || s[frozen + i] != 0f) continue;
                float baseSize = s[bases + i];
                float flexed;
                if (growing)
                    flexed = baseSize + free * Positive(child.Grow) / factorSum;
                else
                    flexed = scaledShrinkSum > 0f
                        ? baseSize + free * Positive(child.Shrink) * baseSize / scaledShrinkSum
                        : baseSize;
                float clamped = Math.Clamp(flexed, s[mins + i], MathF.Max(s[mins + i], MainOf(child.MaximumSize, row)));
                violation += clamped - flexed;
                s[target + i] = clamped;
            }

            if (violation == 0f) break;
            // Freeze the items that hit the side the total violation points to.
            for (int i = start; i < end; i++)
            {
                FlexNode child = node.Children[i];
                if (child.Hidden || s[frozen + i] != 0f) continue;
                bool atMin = s[target + i] <= s[mins + i];
                bool atMax = s[target + i] >= MainOf(child.MaximumSize, row);
                if ((violation > 0f && atMin) || (violation < 0f && atMax))
                    s[frozen + i] = 1f;
            }
        }
    }

    /// <summary>
    /// Once a line's main sizes are resolved, gives each size-dependent child
    /// whose dependent axis is this container's cross axis the cross extent
    /// of its content laid out at its resolved main size: that is its
    /// preferred cross size, and the floor a stretch never goes below (its
    /// measured minimum, its narrowest layout, would overflow a line it does
    /// not need).
    /// </summary>
    private static void ResolveAlongCross(FlexNode node, bool row, int count, int start, int end)
    {
        float[] s = node.Scratch;
        for (int i = start; i < end; i++)
        {
            FlexNode child = node.Children[i];
            if (child.Hidden || !IsSizedAlong(node, child)) continue;
            float extent = Extent(child, widthGiven: row, s[TargetSlot * count + i]);

            // Explicit cross sizes keep their meaning, as in the measure pass,
            // with the extent standing in for the content minimum.
            float? explicitCross = row ? child.Height : child.Width;
            float? explicitMin = row ? child.MinHeight : child.MinWidth;
            float maxCross = CrossOf(child.MaximumSize, row);
            float autoMin = explicitCross is { } e ? MathF.Min(Positive(e), extent) : extent;
            float minCross = MathF.Min(explicitMin is { } m ? Positive(m) : autoMin, maxCross);
            s[CrossFloorSlot * count + i] = minCross;
            s[CrossSlot * count + i] = Math.Clamp(explicitCross is { } ec ? Positive(ec) : extent, minCross, MathF.Max(minCross, maxCross));
        }
    }

    private static float LineCross(FlexNode node, bool row, int start, int end)
    {
        float cross = 0f;
        int crossSlot = CrossSlot * node.Children.Count;
        for (int i = start; i < end; i++)
        {
            FlexNode child = node.Children[i];
            if (child.Hidden) continue;
            cross = MathF.Max(cross, node.Scratch[crossSlot + i]);
        }
        return cross;
    }

    private static void PlaceLine(
        FlexNode node, bool row, int count, int start, int end, float? availableMain, float gap,
        float originMain, float lineCrossStart, float lineCross)
    {
        float[] s = node.Scratch;
        int visible = 0;
        float used = 0f;
        for (int i = start; i < end; i++)
        {
            if (node.Children[i].Hidden) continue;
            used += s[TargetSlot * count + i];
            visible++;
        }
        if (visible == 0) return;
        used += gap * (visible - 1);

        float free = availableMain is { } available ? MathF.Max(0f, available - used) : 0f;
        float offset = 0f, between = gap;
        switch (node.Justify)
        {
            case FlexJustify.Center: offset = free / 2f; break;
            case FlexJustify.End: offset = free; break;
            case FlexJustify.SpaceBetween when visible > 1: between = gap + free / (visible - 1); break;
        }

        // Positions accumulate unsnapped; each edge is rounded on its own, so
        // rounding error never piles up at the end of the line.
        float cursor = originMain + offset;
        for (int i = start; i < end; i++)
        {
            FlexNode child = node.Children[i];
            if (child.Hidden) continue;
            float mainStart = cursor, mainEnd = cursor + s[TargetSlot * count + i];
            cursor = mainEnd + between;

            // A child sized across already has the one cross size its extent
            // was made for; any other child stretches (never below its floor)
            // or keeps its preferred cross size.
            FlexAlign align = child.AlignSelf ?? node.Align;
            float? explicitCross = row ? child.Height : child.Width;
            float minCross = s[CrossFloorSlot * count + i], maxCross = CrossOf(child.MaximumSize, row);
            float cross = !IsLaidAcross(node, child) && align == FlexAlign.Stretch && explicitCross is null
                ? Math.Clamp(lineCross, minCross, MathF.Max(minCross, maxCross))
                : s[CrossSlot * count + i];
            float crossOffset = align switch
            {
                FlexAlign.Center => (lineCross - cross) / 2f,
                FlexAlign.End => lineCross - cross,
                _ => 0f,
            };
            float crossStart = lineCrossStart + crossOffset, crossEnd = crossStart + cross;

            float m0 = Snap(mainStart), m1 = Snap(mainEnd), c0 = Snap(crossStart), c1 = Snap(crossEnd);
            child.Rect = row
                ? new FlexRect(m0, c0, Positive(m1 - m0), Positive(c1 - c0))
                : new FlexRect(c0, m0, Positive(c1 - c0), Positive(m1 - m0));
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    /// <summary>The flex base size from the measurement: basis, else the main-axis preferred size (overrides applied, unclamped).</summary>
    private static float MeasuredBase(FlexNode child, bool row) =>
        child.Basis is { } basis ? Positive(basis) : MainOf(child.PreferredUnclamped, row);

    private static float Hypothetical(FlexNode node, FlexNode child, bool row, int count, int index)
    {
        float min = node.Scratch[MinSlot * count + index];
        return Math.Clamp(node.Scratch[BaseSlot * count + index], min, MathF.Max(min, MainOf(child.MaximumSize, row)));
    }

    private static float MainOf(FlexSize size, bool row) => row ? size.Width : size.Height;

    private static float CrossOf(FlexSize size, bool row) => row ? size.Height : size.Width;

    private static FlexSize Size(float main, float cross, bool row) =>
        row ? new FlexSize(main, cross) : new FlexSize(cross, main);

    private static float Snap(float value) => float.IsFinite(value) ? MathF.Floor(value + 0.5f) : 0f;

    /// <summary>Non-finite or negative computed values are defects; they clamp to 0 so layout never throws.</summary>
    private static float Positive(float value) => float.IsFinite(value) && value > 0f ? value : 0f;
}
