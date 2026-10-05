using System.Collections.Generic;

namespace AcDream.App.UI.Layout.Flex;

/// <summary>
/// One box in a flex layout: a container that lays out <see cref="Children"/>,
/// a leaf measured by <see cref="Measure"/>, or both settings at once (a
/// container is also an item of its own parent). A node with a
/// <see cref="Measure"/> callback is a leaf: its children, if any, are not
/// laid out by the engine.
///
/// <para>Unbounded is always null, never infinity: a null maximum has no
/// limit, and a scrolling axis lays out with no available size.</para>
/// </summary>
public sealed class FlexNode
{
    // ── Container settings ──────────────────────────────────────────────
    public FlexDirection Direction { get; set; }

    /// <summary>Points between items on a line, and between wrapped lines.</summary>
    public float Gap { get; set; }

    public FlexEdges Padding { get; set; }

    public FlexJustify Justify { get; set; }

    public FlexAlign Align { get; set; } = FlexAlign.Stretch;

    public bool Wrap { get; set; }

    /// <summary>The container scrolls horizontally: its items are laid out with no width limit.</summary>
    public bool ScrollX { get; set; }

    /// <summary>The container scrolls vertically: its items are laid out with no height limit.</summary>
    public bool ScrollY { get; set; }

    public List<FlexNode> Children { get; } = new();

    // ── Item settings ───────────────────────────────────────────────────
    public float Grow { get; set; }

    public float Shrink { get; set; } = 1f;

    /// <summary>The main-axis starting size; null is <c>auto</c>.</summary>
    public float? Basis { get; set; }

    /// <summary>Preferred width, overriding the measured one.</summary>
    public float? Width { get; set; }

    /// <summary>Preferred height, overriding the measured one.</summary>
    public float? Height { get; set; }

    /// <summary>Null: the content minimum.</summary>
    public float? MinWidth { get; set; }

    /// <summary>Null: no limit.</summary>
    public float? MaxWidth { get; set; }

    public float? MinHeight { get; set; }

    public float? MaxHeight { get; set; }

    /// <summary>Null: the parent's <see cref="Align"/>.</summary>
    public FlexAlign? AlignSelf { get; set; }

    /// <summary>A hidden node takes no space and is not laid out.</summary>
    public bool Hidden { get; set; }

    /// <summary>Set for a leaf; null for a container measured from its children.</summary>
    public FlexMeasure? Measure { get; set; }

    // ── Results ─────────────────────────────────────────────────────────
    /// <summary>Where the last arrange put this node, relative to its parent's top-left corner.</summary>
    public FlexRect Rect { get; internal set; }

    /// <summary>The last measurement: preferred size clamped to the limits, and minimum size.</summary>
    public FlexMeasurement Measured { get; internal set; }

    /// <summary>
    /// After an arrange, how far this container's items reach: the furthest
    /// item edge plus the trailing padding, on each axis. A scrolling
    /// container scrolls over this extent.
    /// </summary>
    public FlexSize ContentSize { get; internal set; }

    // Measure-pass results the arrange pass reads: preferred size with the
    // Width/Height overrides applied but not clamped, and resolved limits.
    internal FlexSize PreferredUnclamped;
    internal FlexSize MinimumSize;
    internal FlexSize MaximumSize;

    // Arrange scratch, reused so a steady-state layout allocates nothing.
    internal readonly List<int> LineEnds = new();
    internal float[] Scratch = System.Array.Empty<float>();
}
