# SVG Plugin Icons Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A plugin can give its dock buttons a one-colour SVG icon (`icon.svg`, or a per-window `IconFile`) that stays sharp at every display scale and takes its colour from the plugin theme.

**Architecture:** Pure parsing and geometry live in `AcDream.Core/Plugins` (`SvgOutline` for path data and shapes, `PluginSvgIcon` for the document subset, `SvgStroker` for stroke expansion). `AcDream.App` rasterizes with StbTrueType's outline rasterizer into a coverage texture per device size (`SvgIconRasterizer`), caches and reference-counts those (`PluginSvgIconCache`, through the existing `IPluginFontBackend` upload seam), and the dock button draws every icon through one `DrawIcon(ctx, x, y, extent, colour)` method. The launcher only allows `.svg` and checks its size.

**Tech Stack:** C# / .NET 10, xUnit, StbTrueTypeSharp 1.26.12 (already referenced by `AcDream.App`), `System.Xml.XmlReader`, the retained UI in `src/AcDream.App/UI`.

**Spec:** `docs/superpowers/specs/2026-10-02-svg-plugin-icons-design.md` (fork-only docs branch `docs/modern-theme-spec`). Read it with this plan.

## Global Constraints

- **Additive plugin API only:** one new optional `init` property, `PluginPanelDescriptor.IconFile`. Nothing else in `src/AcDream.Plugin.Abstractions` changes.
- **Never fail a plugin:** every parse, path, bake or upload problem falls back to the next icon (window SVG → plugin `icon.svg` → `icon.png` → `IconSurfaceId` → initials) and is logged once per file as `[UI] plugin icon '<pluginId>/<relative path>' ignored: <reason>`.
- **Mono-colour:** an icon is coverage only; any paint colour in the file means ink. The caller's tint gives the colour.
- **Limits** (constants in `PluginSvgIcon`, `SvgIconRasterizer`, `LauncherPluginIcon`): 16 KiB per file, 256 elements, depth 8, 4,096 path commands per file, 65,536 points per bake, bakes at most 128 device px, at most two baked sizes per icon.
- **XML safety:** `DtdProcessing.Prohibit`, `XmlResolver = null`, `MaxCharactersFromEntities = 0`, `MaxCharactersInDocument = 16384`, read from bytes already capped at 16 KiB.
- **Coordinates to stb** are device px × 64 as `short`, clamped to ±500 px first. (The spec says "−4..(size + 4)"; ±500 is used instead so a curve whose control point lies just outside the bitmap is not distorted. Both keep `short` from overflowing.)
- **Point limit is enforced at bake time** (`SvgIconRasterizer.Bake` returns null), since the flattened point count depends on the device size; parse time enforces the element, depth and command limits.
- **Current-dock tint** (this branch only, before the dock redesign lands): Moss/Brass `Text` closed and `Accent` open; Classic white closed and `VisibleBorder` (0.76, 0.64, 0.25) open. RGBA icons (PNG/DAT) are drawn exactly as before (same rectangle, `Vector4.One`). The dock redesign replaces `IconColor` with its own state colours.
- **Keep `PluginSidePanel.cs` changes minimal** (the internal `Add` overload and `PluginShelfButton` only), and put new dock tests in `PluginShelfSvgIconTests.cs`, so this branch and `modern-theme/dock` merge with few conflicts (see Task 10).
- **Branching:** code goes on `plugin-icons/svg` in `.worktrees/svg-icons`, made from fork `main` (it needs painter-v2 hidpi/fonts and the modern theme, which only fork main has). `docs/superpowers/` never goes on the code branch. No push or merge without the user's yes (Task 10).
- **Build env:** `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH`. Building rewrites `*.lock.json`; revert with `git checkout -- '*.lock.json'` before each commit.
- **Commit trailer:** end every commit message with `Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB`.

**User decisions (already made):** (from the spec's "Decisions (2026-10-02)")
- "Classic draws the SVG in the dock's Classic colours (it shares the new dock with Moss and Brass)." On this branch that means Classic's white/gold; the dock branch supplies `ClassicDock`.
- "`evenodd` rejects the file, with a clear log line."
- "Launcher validation only allows the `.svg` extension and checks the 16 KiB size. There is no second parser copy."
- "Size limit: 16 KiB."
- "Group opacity is applied to each child, an approximation documented in `plugin-ui-markup.md`."
- Mono-colour only; launcher plugin list and markup `<icon>` stay out of scope; fork first, spec stays on the fork-only docs branch.

Test command used throughout (project and filter vary):

```bash
cd .worktrees/svg-icons && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test tests/<Project>/<Project>.csproj --filter "FullyQualifiedName~<Name>"
```

---

### Task 0: Branch and worktree

**Goal:** A worktree `.worktrees/svg-icons` on a new branch `plugin-icons/svg`, made from fork `main`, that builds cleanly before any change.

**Files:**
- Create: worktree `.worktrees/svg-icons` (branch `plugin-icons/svg`)

**Acceptance Criteria:**
- [ ] `git -C .worktrees/svg-icons branch --show-current` prints `plugin-icons/svg`
- [ ] The branch's base is fork `main` at 16b0db10 or later
- [ ] `dotnet build AcDream.slnx` in the worktree reports `Build succeeded.` with 0 errors

**Verify:** `git -C .worktrees/svg-icons log -1 --format=%H main` equals `git -C .worktrees/svg-icons merge-base HEAD main`, and the build prints `Build succeeded.`

**Steps:**

- [ ] **Step 1: Create the worktree from fork main**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
git fetch origin
git worktree add .worktrees/svg-icons -b plugin-icons/svg main
cd .worktrees/svg-icons
git log --oneline -1   # 16b0db10 or a later fork-main commit
```

- [ ] **Step 2: Build once to prove the base is clean**

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet build AcDream.slnx 2>&1 | tail -3
git checkout -- '*.lock.json'
```

Expected: `Build succeeded.` and `0 Error(s)`. Nothing to commit.

```json:metadata
{"files": [], "verifyCommand": "cd .worktrees/svg-icons && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH && dotnet build AcDream.slnx", "modelTier": "mechanical", "acceptanceCriteria": ["`git -C .worktrees/svg-icons branch --show-current` prints `plugin-icons/svg`", "The branch's base is fork `main` at 16b0db10 or later", "`dotnet build AcDream.slnx` in the worktree reports `Build succeeded.` with 0 errors"]}
```

---

### Task 1: SVG geometry and path data (SvgOutline)

**Goal:** Pure geometry types (`SvgPoint`, `SvgMatrix`, `SvgSegment`, `SvgSubpath`) and `SvgOutline`, which parses SVG path data (all of `M L H V C S Q T A Z`, relative and absolute, implicit repeats, compact numbers, packed arc flags) and builds ellipses, rounded rects and polylines as lines, quadratics and cubics. Arcs become cubics of at most 90° (SVG 1.1 F.6.5/F.6.6).

**Files:**
- Create: `src/AcDream.Core/Plugins/SvgOutline.cs`
- Test: `tests/AcDream.Core.Tests/Plugins/SvgOutlineTests.cs`

**Acceptance Criteria:**
- [ ] `SvgOutlineTests` has 27 tests and all pass
- [ ] Arcs: endpoints exact, a semicircle passes through its top, sweep and large-arc flags pick the right arc, too-small radii are scaled up, zero radius is a line
- [ ] Malformed data (no leading moveto, missing numbers, bad flag, unknown command, a non-finite number) is rejected with a reason naming the offset
- [ ] The command counter includes implicit repeats and stops at the limit the caller passes

**Verify:** `dotnet test tests/AcDream.Core.Tests/AcDream.Core.Tests.csproj --filter "FullyQualifiedName~SvgOutlineTests"` → `Passed! - Failed: 0, Passed: 27`

**Steps:**

- [ ] **Step 1: Write the failing tests**

`tests/AcDream.Core.Tests/Plugins/SvgOutlineTests.cs`:

```csharp
using AcDream.Core.Plugins;

namespace AcDream.Core.Tests.Plugins;

public sealed class SvgOutlineTests
{
    private static List<SvgSubpath> Parse(string d)
    {
        var into = new List<SvgSubpath>();
        int commands = 0;
        Assert.True(SvgOutline.TryParsePath(d, into, ref commands, 4096, out string? reason), reason);
        return into;
    }

    private static string? Reject(string d, int maximumCommands = 4096)
    {
        int commands = 0;
        Assert.False(SvgOutline.TryParsePath(d, [], ref commands, maximumCommands, out string? reason));
        return reason;
    }

    private static void Near(SvgPoint expected, SvgPoint actual, double tolerance = 1e-6)
    {
        Assert.True(Math.Abs(expected.X - actual.X) <= tolerance && Math.Abs(expected.Y - actual.Y) <= tolerance,
            $"expected {expected}, got {actual}");
    }

    [Fact]
    public void AbsoluteAndRelativeLinesLandOnTheSamePoints()
    {
        SvgSubpath a = Assert.Single(Parse("M1 2 L4 6 H10 V0 Z"));
        SvgSubpath b = Assert.Single(Parse("m1 2 l3 4 h6 v-6 z"));
        Assert.Equal(a.Start, b.Start);
        Assert.Equal(a.Segments.Select(s => s.End), b.Segments.Select(s => s.End));
        Assert.True(a.Closed);
        Assert.Equal(new SvgPoint(10, 0), a.Segments[^1].End);
    }

    [Fact]
    public void ExtraPairsAfterAMovetoAreLinetos()
    {
        SvgSubpath p = Assert.Single(Parse("m1 1 2 0 0 2"));
        Assert.Equal(new SvgPoint(1, 1), p.Start);
        Assert.Equal([new SvgPoint(3, 1), new SvgPoint(3, 3)], p.Segments.Select(s => s.End));
        Assert.All(p.Segments, s => Assert.Equal(SvgSegmentKind.Line, s.Kind));
    }

    [Fact]
    public void CompactNumbersAndExponentsParse()
    {
        SvgSubpath p = Assert.Single(Parse("M1.5.5L-.5-1 1e1,2E-1"));
        Assert.Equal(new SvgPoint(1.5, 0.5), p.Start);
        Assert.Equal(new SvgPoint(-0.5, -1), p.Segments[0].End);
        Assert.Equal(new SvgPoint(10, 0.2), p.Segments[1].End);
    }

    [Fact]
    public void PackedArcFlagsParse()
    {
        SvgSubpath p = Assert.Single(Parse("M0 0a1 1 0 011 1"));
        Near(new SvgPoint(1, 1), p.Segments[^1].End);
        Assert.All(p.Segments, s => Assert.Equal(SvgSegmentKind.Cubic, s.Kind));
    }

    [Fact]
    public void SmoothCubicReflectsThePreviousControlPoint()
    {
        SvgSubpath p = Assert.Single(Parse("M0 0 C0 1 2 1 2 0 S4 -1 4 0"));
        Assert.Equal(new SvgPoint(2, -1), p.Segments[1].C1);
    }

    [Fact]
    public void SmoothCubicWithoutAPreviousCubicUsesTheCurrentPoint()
    {
        SvgSubpath p = Assert.Single(Parse("M0 0 L2 0 S4 -1 4 0"));
        Assert.Equal(new SvgPoint(2, 0), p.Segments[1].C1);
    }

    [Fact]
    public void SmoothQuadraticReflectsThePreviousControlPoint()
    {
        SvgSubpath p = Assert.Single(Parse("M0 0 Q1 1 2 0 T4 0"));
        Assert.Equal(SvgSegmentKind.Quadratic, p.Segments[1].Kind);
        Assert.Equal(new SvgPoint(3, -1), p.Segments[1].C1);
    }

    [Fact]
    public void ASemicircleArcEndsWhereItShouldAndPassesThroughTheTop()
    {
        // From (0,0) to (2,0) with radius 1, sweep=1: on screen (y down) it bulges upward.
        SvgSubpath p = Assert.Single(Parse("M0 0 A1 1 0 0 1 2 0"));
        Assert.Equal(2, p.Segments.Count);
        Near(new SvgPoint(1, -1), p.Segments[0].End);
        Near(new SvgPoint(2, 0), p.Segments[1].End);
    }

    [Fact]
    public void TheSweepFlagChoosesTheSide()
    {
        SvgSubpath p = Assert.Single(Parse("M0 0 A1 1 0 0 0 2 0"));
        Near(new SvgPoint(1, 1), p.Segments[0].End);
    }

    [Fact]
    public void TheLargeArcFlagTakesTheLongWayRound()
    {
        // Quarter-circle endpoints on a unit circle centred at (1,0)... small arc is 90°, large is 270°.
        SvgSubpath small = Assert.Single(Parse("M0 0 A1 1 0 0 1 1 -1"));
        SvgSubpath large = Assert.Single(Parse("M0 0 A1 1 0 1 1 1 -1"));
        Assert.Single(small.Segments);
        Assert.Equal(3, large.Segments.Count);
        Near(new SvgPoint(1, -1), large.Segments[^1].End);
    }

    [Fact]
    public void RadiiTooSmallAreScaledUp()
    {
        // Radius 0.5 cannot span 2 units, so it becomes 1: a semicircle through (1,-1).
        SvgSubpath p = Assert.Single(Parse("M0 0 A0.5 0.5 0 0 1 2 0"));
        Near(new SvgPoint(1, -1), p.Segments[0].End, 1e-9);
    }

    [Fact]
    public void AZeroRadiusArcIsALine()
    {
        SvgSubpath p = Assert.Single(Parse("M0 0 A0 3 0 0 1 2 0"));
        Assert.Equal(SvgSegmentKind.Line, Assert.Single(p.Segments).Kind);
    }

    [Fact]
    public void AnArcCubicStaysOnTheCircle()
    {
        SvgSubpath p = Assert.Single(Parse("M1 0 A1 1 0 0 1 0 1"));
        SvgSegment c = Assert.Single(p.Segments);
        // Midpoint of the cubic at t = 0.5.
        SvgPoint mid = (p.Start + c.C1 * 3 + c.C2 * 3 + c.End) * 0.125;
        Assert.InRange(mid.Length, 1 - 3e-4, 1 + 3e-4);
    }

    [Fact]
    public void AMovetoAfterCloseStartsANewSubpath()
    {
        List<SvgSubpath> paths = Parse("M0 0h2v2z m4 0h1");
        Assert.Equal(2, paths.Count);
        Assert.True(paths[0].Closed);
        Assert.Equal(new SvgPoint(4, 0), paths[1].Start);
        Assert.False(paths[1].Closed);
    }

    [Fact]
    public void DrawingAfterCloseWithoutAMovetoStartsAtTheSubpathStart()
    {
        List<SvgSubpath> paths = Parse("M1 1h2v2zl1 0");
        Assert.Equal(2, paths.Count);
        Assert.Equal(new SvgPoint(1, 1), paths[1].Start);
        Assert.Equal(new SvgPoint(2, 1), paths[1].Segments[0].End);
    }

    [Theory]
    [InlineData("L1 1")]
    [InlineData("M1")]
    [InlineData("M0 0 L1 x")]
    [InlineData("M0 0 A1 1 0 2 1 2 0")]
    [InlineData("M0 0 K1 1")]
    [InlineData("M0 0 L1e999 0")]
    public void MalformedPathDataIsRejected(string d)
    {
        Assert.NotNull(Reject(d));
    }

    [Fact]
    public void TheCommandLimitCountsImplicitRepeats()
    {
        string? reason = Reject("M0 0 1 1 2 2 3 3", maximumCommands: 3);
        Assert.Contains("more than 3 path commands", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyPathDataDrawsNothing()
    {
        Assert.Empty(Parse("  "));
    }

    [Fact]
    public void AnEllipseIsFourCubicsThroughItsExtremes()
    {
        SvgSubpath e = SvgOutline.Ellipse(5, 5, 4, 2);
        Assert.Equal(new SvgPoint(9, 5), e.Start);
        Assert.Equal([new SvgPoint(5, 7), new SvgPoint(1, 5), new SvgPoint(5, 3), new SvgPoint(9, 5)],
            e.Segments.Select(s => s.End));
        Assert.Equal(new SvgPoint(9, 5 + 2 * SvgOutline.Kappa), e.Segments[0].C1);
    }

    [Fact]
    public void ARoundedRectHasFourLinesAndFourCorners()
    {
        SvgSubpath r = SvgOutline.Rect(0, 0, 10, 6, 2, 2);
        Assert.Equal(4, r.Segments.Count(s => s.Kind == SvgSegmentKind.Line));
        Assert.Equal(4, r.Segments.Count(s => s.Kind == SvgSegmentKind.Cubic));
        Assert.Equal(new SvgPoint(2, 0), r.Start);
        Assert.Equal(r.Start, r.Segments[^1].End);
    }

    [Fact]
    public void MatrixCompositionAppliesTheInnerTransformFirst()
    {
        var translate = new SvgMatrix(1, 0, 0, 1, 10, 0);
        var scale = new SvgMatrix(2, 0, 0, 2, 0, 0);
        Assert.Equal(new SvgPoint(12, 2), translate.Then(scale).Apply(new SvgPoint(1, 1)));
        Assert.Equal(new SvgPoint(22, 2), scale.Then(translate).Apply(new SvgPoint(1, 1)));
    }

    [Fact]
    public void MaximumScaleIsTheLargerStretch()
    {
        Assert.Equal(3, new SvgMatrix(3, 0, 0, 0.5, 0, 0).MaximumScale, 9);
        Assert.Equal(2, new SvgMatrix(0, 2, -2, 0, 0, 0).MaximumScale, 9);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

```bash
cd .worktrees/svg-icons && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test tests/AcDream.Core.Tests/AcDream.Core.Tests.csproj --filter "FullyQualifiedName~SvgOutlineTests"
```

Expected: the build fails with `error CS0246: The type or namespace name 'SvgSubpath' could not be found`.

- [ ] **Step 3: Write `SvgOutline.cs`**

`src/AcDream.Core/Plugins/SvgOutline.cs`:

```csharp
using System.Globalization;

namespace AcDream.Core.Plugins;

/// <summary>A point in an icon's own user units, or in device pixels once transformed.</summary>
public readonly record struct SvgPoint(double X, double Y)
{
    public static SvgPoint operator +(SvgPoint a, SvgPoint b) => new(a.X + b.X, a.Y + b.Y);
    public static SvgPoint operator -(SvgPoint a, SvgPoint b) => new(a.X - b.X, a.Y - b.Y);
    public static SvgPoint operator *(SvgPoint a, double s) => new(a.X * s, a.Y * s);
    public double Length => Math.Sqrt(X * X + Y * Y);
}

/// <summary>
/// An SVG affine transform, <c>matrix(a b c d e f)</c>: x' = a·x + c·y + e,
/// y' = b·x + d·y + f.
/// </summary>
public readonly record struct SvgMatrix(double A, double B, double C, double D, double E, double F)
{
    public static SvgMatrix Identity { get; } = new(1, 0, 0, 1, 0, 0);

    /// <summary>This transform applied after <paramref name="inner"/>.</summary>
    public SvgMatrix Then(SvgMatrix inner) => new(
        A * inner.A + C * inner.B,
        B * inner.A + D * inner.B,
        A * inner.C + C * inner.D,
        B * inner.C + D * inner.D,
        A * inner.E + C * inner.F + E,
        B * inner.E + D * inner.F + F);

    public SvgPoint Apply(SvgPoint p) => new(A * p.X + C * p.Y + E, B * p.X + D * p.Y + F);

    public double Determinant => A * D - B * C;

    /// <summary>The largest factor this transform stretches any length by.</summary>
    public double MaximumScale
    {
        get
        {
            double p = A * A + B * B + C * C + D * D;
            double det = Determinant;
            double root = Math.Sqrt(Math.Max(0, p * p - 4 * det * det));
            return Math.Sqrt((p + root) / 2);
        }
    }
}

public enum SvgSegmentKind { Line, Quadratic, Cubic }

/// <summary>One piece of a subpath from the previous end point. A quadratic uses
/// <see cref="C1"/> only; a line uses neither control point.</summary>
public readonly record struct SvgSegment(SvgSegmentKind Kind, SvgPoint C1, SvgPoint C2, SvgPoint End)
{
    public static SvgSegment Line(SvgPoint end) => new(SvgSegmentKind.Line, default, default, end);
    public static SvgSegment Quadratic(SvgPoint c, SvgPoint end) => new(SvgSegmentKind.Quadratic, c, default, end);
    public static SvgSegment Cubic(SvgPoint c1, SvgPoint c2, SvgPoint end) => new(SvgSegmentKind.Cubic, c1, c2, end);
}

public sealed record SvgSubpath(SvgPoint Start, IReadOnlyList<SvgSegment> Segments, bool Closed);

/// <summary>
/// Turns SVG path data and the basic shapes into subpaths of lines,
/// quadratics and cubics. Arcs become cubics of at most 90° each (SVG 1.1
/// implementation notes F.6.5 and F.6.6). Pure: no I/O, no GL.
/// </summary>
public static class SvgOutline
{
    /// <summary>The cubic control distance for a quarter circle of radius 1.</summary>
    public const double Kappa = 0.5522847498307936;

    /// <summary>Parses <paramref name="d"/>. <paramref name="commands"/> counts every command,
    /// implicit repeats included, so a caller can hold a whole file to a limit.</summary>
    public static bool TryParsePath(string d, List<SvgSubpath> into, ref int commands, int maximumCommands, out string? reason)
    {
        var reader = new PathReader(d);
        var segments = new List<SvgSegment>();
        SvgPoint current = default, start = default, lastControl = default;
        char last = ' ';
        bool open = false;

        void Flush(bool closed)
        {
            if (open) into.Add(new SvgSubpath(start, segments.ToArray(), closed));
            segments.Clear();
            open = false;
        }

        void Begin()
        {
            if (open) return;
            start = current;
            open = true;
        }

        reader.SkipWhitespace();
        if (reader.AtEnd) { reason = null; return true; }
        if (!reader.TryCommand(out char command) || (command is not ('M' or 'm')))
        {
            reason = $"path data must start with a moveto, at offset {reader.Offset}";
            return false;
        }

        while (true)
        {
            bool relative = char.IsLower(command);
            char upper = char.ToUpperInvariant(command);
            bool first = true;
            do
            {
                if (++commands > maximumCommands)
                {
                    reason = $"has more than {maximumCommands} path commands";
                    return false;
                }

                SvgPoint origin = relative ? current : default;
                switch (upper)
                {
                    case 'M':
                    {
                        if (!reader.TryPoint(out SvgPoint p)) return Malformed(reader, out reason);
                        if (first)
                        {
                            Flush(false);
                            current = origin + p;
                            Begin();
                        }
                        else
                        {
                            Begin();
                            current = origin + p;
                            segments.Add(SvgSegment.Line(current));
                        }
                        lastControl = current;
                        break;
                    }
                    case 'L':
                    {
                        if (!reader.TryPoint(out SvgPoint p)) return Malformed(reader, out reason);
                        Begin();
                        current = origin + p;
                        segments.Add(SvgSegment.Line(current));
                        lastControl = current;
                        break;
                    }
                    case 'H':
                    {
                        if (!reader.TryNumber(out double x)) return Malformed(reader, out reason);
                        Begin();
                        current = new SvgPoint(relative ? current.X + x : x, current.Y);
                        segments.Add(SvgSegment.Line(current));
                        lastControl = current;
                        break;
                    }
                    case 'V':
                    {
                        if (!reader.TryNumber(out double y)) return Malformed(reader, out reason);
                        Begin();
                        current = new SvgPoint(current.X, relative ? current.Y + y : y);
                        segments.Add(SvgSegment.Line(current));
                        lastControl = current;
                        break;
                    }
                    case 'C':
                    {
                        if (!reader.TryPoint(out SvgPoint c1) || !reader.TryPoint(out SvgPoint c2)
                            || !reader.TryPoint(out SvgPoint p))
                            return Malformed(reader, out reason);
                        Begin();
                        segments.Add(SvgSegment.Cubic(origin + c1, origin + c2, origin + p));
                        lastControl = origin + c2;
                        current = origin + p;
                        break;
                    }
                    case 'S':
                    {
                        if (!reader.TryPoint(out SvgPoint c2) || !reader.TryPoint(out SvgPoint p))
                            return Malformed(reader, out reason);
                        Begin();
                        SvgPoint c1 = last is 'C' or 'S' ? current + (current - lastControl) : current;
                        segments.Add(SvgSegment.Cubic(c1, origin + c2, origin + p));
                        lastControl = origin + c2;
                        current = origin + p;
                        break;
                    }
                    case 'Q':
                    {
                        if (!reader.TryPoint(out SvgPoint c) || !reader.TryPoint(out SvgPoint p))
                            return Malformed(reader, out reason);
                        Begin();
                        segments.Add(SvgSegment.Quadratic(origin + c, origin + p));
                        lastControl = origin + c;
                        current = origin + p;
                        break;
                    }
                    case 'T':
                    {
                        if (!reader.TryPoint(out SvgPoint p)) return Malformed(reader, out reason);
                        Begin();
                        SvgPoint c = last is 'Q' or 'T' ? current + (current - lastControl) : current;
                        segments.Add(SvgSegment.Quadratic(c, origin + p));
                        lastControl = c;
                        current = origin + p;
                        break;
                    }
                    case 'A':
                    {
                        if (!reader.TryNumber(out double rx) || !reader.TryNumber(out double ry)
                            || !reader.TryNumber(out double rotation)
                            || !reader.TryFlag(out bool large) || !reader.TryFlag(out bool sweep)
                            || !reader.TryPoint(out SvgPoint p))
                            return Malformed(reader, out reason);
                        Begin();
                        SvgPoint end = origin + p;
                        AppendArc(segments, current, Math.Abs(rx), Math.Abs(ry), rotation, large, sweep, end);
                        current = end;
                        lastControl = current;
                        break;
                    }
                    case 'Z':
                    {
                        if (open) Flush(true);
                        current = start;
                        lastControl = current;
                        break;
                    }
                    default:
                        reason = $"path data has an unknown command '{command}' at offset {reader.Offset}";
                        return false;
                }

                last = upper;
                first = false;
                if (upper == 'M') last = 'L';
            }
            while (upper != 'Z' && reader.AtNumberStart);

            reader.SkipWhitespace();
            if (reader.AtEnd) break;
            if (!reader.TryCommand(out command))
                return Malformed(reader, out reason);
        }

        Flush(false);
        reason = null;
        return true;
    }

    private static bool Malformed(PathReader reader, out string? reason)
    {
        reason = $"path data is malformed at offset {reader.Offset}";
        return false;
    }

    /// <summary>An ellipse as four cubics, clockwise on screen from its rightmost point.</summary>
    public static SvgSubpath Ellipse(double cx, double cy, double rx, double ry)
    {
        double kx = rx * Kappa, ky = ry * Kappa;
        var right = new SvgPoint(cx + rx, cy);
        var bottom = new SvgPoint(cx, cy + ry);
        var left = new SvgPoint(cx - rx, cy);
        var top = new SvgPoint(cx, cy - ry);
        return new SvgSubpath(right,
        [
            SvgSegment.Cubic(new(cx + rx, cy + ky), new(cx + kx, cy + ry), bottom),
            SvgSegment.Cubic(new(cx - kx, cy + ry), new(cx - rx, cy + ky), left),
            SvgSegment.Cubic(new(cx - rx, cy - ky), new(cx - kx, cy - ry), top),
            SvgSegment.Cubic(new(cx + kx, cy - ry), new(cx + rx, cy - ky), right),
        ], true);
    }

    /// <summary>A rectangle, with corner radii already clamped to half its sides.</summary>
    public static SvgSubpath Rect(double x, double y, double w, double h, double rx, double ry)
    {
        if (rx <= 0 || ry <= 0)
        {
            return new SvgSubpath(new(x, y),
            [
                SvgSegment.Line(new(x + w, y)), SvgSegment.Line(new(x + w, y + h)),
                SvgSegment.Line(new(x, y + h)), SvgSegment.Line(new(x, y)),
            ], true);
        }

        double kx = rx * (1 - Kappa), ky = ry * (1 - Kappa);
        double r = x + w, b = y + h;
        return new SvgSubpath(new(x + rx, y),
        [
            SvgSegment.Line(new(r - rx, y)),
            SvgSegment.Cubic(new(r - kx, y), new(r, y + ky), new(r, y + ry)),
            SvgSegment.Line(new(r, b - ry)),
            SvgSegment.Cubic(new(r, b - ky), new(r - kx, b), new(r - rx, b)),
            SvgSegment.Line(new(x + rx, b)),
            SvgSegment.Cubic(new(x + kx, b), new(x, b - ky), new(x, b - ry)),
            SvgSegment.Line(new(x, y + ry)),
            SvgSegment.Cubic(new(x, y + ky), new(x + kx, y), new(x + rx, y)),
        ], true);
    }

    /// <summary>A polyline or polygon from its points.</summary>
    public static SvgSubpath Poly(IReadOnlyList<SvgPoint> points, bool closed)
    {
        var segments = new SvgSegment[points.Count - 1];
        for (int i = 1; i < points.Count; i++) segments[i - 1] = SvgSegment.Line(points[i]);
        return new SvgSubpath(points[0], segments, closed);
    }

    /// <summary>Parses a <c>points</c> list: pairs of numbers.</summary>
    public static bool TryParsePoints(string text, out List<SvgPoint> points)
    {
        points = [];
        var reader = new PathReader(text);
        reader.SkipWhitespace();
        while (!reader.AtEnd)
        {
            if (!reader.TryPoint(out SvgPoint p)) return false;
            points.Add(p);
            reader.SkipWhitespace();
        }
        return points.Count >= 2;
    }

    /// <summary>Parses a list of numbers separated by whitespace and/or commas.</summary>
    public static bool TryParseNumbers(string text, out List<double> numbers)
    {
        numbers = [];
        var reader = new PathReader(text);
        reader.SkipWhitespace();
        while (!reader.AtEnd)
        {
            if (!reader.TryNumber(out double n)) return false;
            numbers.Add(n);
            reader.SkipWhitespace();
        }
        return true;
    }

    internal static void AppendArc(
        List<SvgSegment> into, SvgPoint from, double rx, double ry, double rotationDegrees,
        bool large, bool sweep, SvgPoint to)
    {
        if (from == to) return;
        if (rx == 0 || ry == 0)
        {
            into.Add(SvgSegment.Line(to));
            return;
        }

        double phi = rotationDegrees * Math.PI / 180;
        double cos = Math.Cos(phi), sin = Math.Sin(phi);
        double dx = (from.X - to.X) / 2, dy = (from.Y - to.Y) / 2;
        double x1 = cos * dx + sin * dy, y1 = -sin * dx + cos * dy;

        double lambda = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);
        if (lambda > 1)
        {
            double s = Math.Sqrt(lambda);
            rx *= s;
            ry *= s;
        }

        double num = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1;
        double den = rx * rx * y1 * y1 + ry * ry * x1 * x1;
        double coef = Math.Sqrt(Math.Max(0, num / den)) * (large == sweep ? -1 : 1);
        double cx1 = coef * rx * y1 / ry, cy1 = -coef * ry * x1 / rx;
        double cx = cos * cx1 - sin * cy1 + (from.X + to.X) / 2;
        double cy = sin * cx1 + cos * cy1 + (from.Y + to.Y) / 2;

        double theta1 = Angle(1, 0, (x1 - cx1) / rx, (y1 - cy1) / ry);
        double delta = Angle((x1 - cx1) / rx, (y1 - cy1) / ry, (-x1 - cx1) / rx, (-y1 - cy1) / ry);
        if (!sweep && delta > 0) delta -= 2 * Math.PI;
        else if (sweep && delta < 0) delta += 2 * Math.PI;

        int pieces = Math.Max(1, (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2) - 1e-9));
        double step = delta / pieces;
        double t = 4.0 / 3.0 * Math.Tan(step / 4);
        for (int i = 0; i < pieces; i++)
        {
            double a1 = theta1 + i * step, a2 = a1 + step;
            double c1 = Math.Cos(a1), s1 = Math.Sin(a1), c2 = Math.Cos(a2), s2 = Math.Sin(a2);
            SvgPoint P(double ux, double uy) => new(
                cos * rx * ux - sin * ry * uy + cx,
                sin * rx * ux + cos * ry * uy + cy);
            SvgPoint end = i == pieces - 1 ? to : P(c2, s2);
            into.Add(SvgSegment.Cubic(P(c1 - t * s1, s1 + t * c1), P(c2 + t * s2, s2 - t * c2), end));
        }
    }

    private static double Angle(double ux, double uy, double vx, double vy)
    {
        double a = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
        return a;
    }

    /// <summary>A cursor over path data and number lists, with the SVG 1.1 number grammar.</summary>
    private sealed class PathReader(string text)
    {
        private int _i;

        public int Offset => _i;
        public bool AtEnd => _i >= text.Length;

        public bool AtNumberStart
        {
            get
            {
                SkipCommaWhitespace();
                if (AtEnd) return false;
                char c = text[_i];
                return char.IsAsciiDigit(c) || c is '.' or '-' or '+';
            }
        }

        public void SkipWhitespace()
        {
            while (!AtEnd && text[_i] is ' ' or '\t' or '\n' or '\r' or '\f') _i++;
        }

        private void SkipCommaWhitespace()
        {
            SkipWhitespace();
            if (!AtEnd && text[_i] == ',')
            {
                _i++;
                SkipWhitespace();
            }
        }

        public bool TryCommand(out char command)
        {
            SkipWhitespace();
            command = AtEnd ? '\0' : text[_i];
            if (!char.IsAsciiLetter(command) || command is 'e' or 'E') return false;
            _i++;
            return true;
        }

        public bool TryPoint(out SvgPoint p)
        {
            p = default;
            if (!TryNumber(out double x) || !TryNumber(out double y)) return false;
            p = new SvgPoint(x, y);
            return true;
        }

        public bool TryFlag(out bool flag)
        {
            SkipCommaWhitespace();
            flag = false;
            if (AtEnd || text[_i] is not ('0' or '1')) return false;
            flag = text[_i] == '1';
            _i++;
            return true;
        }

        public bool TryNumber(out double value)
        {
            SkipCommaWhitespace();
            value = 0;
            int begin = _i;
            if (!AtEnd && text[_i] is '+' or '-') _i++;
            int digits = 0;
            while (!AtEnd && char.IsAsciiDigit(text[_i])) { _i++; digits++; }
            if (!AtEnd && text[_i] == '.')
            {
                _i++;
                while (!AtEnd && char.IsAsciiDigit(text[_i])) { _i++; digits++; }
            }
            if (digits == 0)
            {
                _i = begin;
                return false;
            }
            if (!AtEnd && text[_i] is 'e' or 'E')
            {
                int mark = _i;
                _i++;
                if (!AtEnd && text[_i] is '+' or '-') _i++;
                int exponent = 0;
                while (!AtEnd && char.IsAsciiDigit(text[_i])) { _i++; exponent++; }
                if (exponent == 0) _i = mark;
            }
            return double.TryParse(text.AsSpan(begin, _i - begin), NumberStyles.Float,
                       CultureInfo.InvariantCulture, out value)
                   && double.IsFinite(value);
        }
    }
}
```

- [ ] **Step 4: Run the tests again**

```bash
dotnet test tests/AcDream.Core.Tests/AcDream.Core.Tests.csproj --filter "FullyQualifiedName~SvgOutlineTests"
```

Expected: `Passed! - Failed: 0, Passed: 27, Skipped: 0, Total: 27`.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.Core/Plugins/SvgOutline.cs \
  tests/AcDream.Core.Tests/Plugins/SvgOutlineTests.cs
git commit -m "feat: SVG path data and basic shapes as outlines" -m "Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.Core/Plugins/SvgOutline.cs", "tests/AcDream.Core.Tests/Plugins/SvgOutlineTests.cs"], "verifyCommand": "cd .worktrees/svg-icons && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH && dotnet test tests/AcDream.Core.Tests/AcDream.Core.Tests.csproj --filter \"FullyQualifiedName~SvgOutlineTests\"", "modelTier": "standard", "acceptanceCriteria": ["`SvgOutlineTests` has 27 tests and all pass", "Arcs: endpoints exact, a semicircle passes through its top, sweep and large-arc flags pick the right arc, too-small radii are scaled up, zero radius is a line", "Malformed data (no leading moveto, missing numbers, bad flag, unknown command, a non-finite number) is rejected with a reason naming the offset", "The command counter includes implicit repeats and stops at the limit the caller passes"]}
```

---

### Task 2: SVG icon document parser (PluginSvgIcon)

**Goal:** `PluginSvgIcon` reads an icon file into an `SvgIconDocument` (viewBox plus paint layers in document order) under the spec's subset, never throwing: XML without DTDs or entities, at most 16 KiB, 256 elements, depth 8 and 4,096 path commands; inherited fill/stroke style through `g` and `style=""`; transforms; and `TryResolvePath`, which keeps an `IconFile` inside the plugin folder after links are followed.

**Files:**
- Create: `src/AcDream.Core/Plugins/PluginSvgIcon.cs`
- Test: `tests/AcDream.Core.Tests/Plugins/PluginSvgIconTests.cs`

**Acceptance Criteria:**
- [ ] `PluginSvgIconTests` has 57 tests and all pass
- [ ] Every rejected element (`use image text style script mask clipPath linearGradient filter a foreignObject` and nested `svg`) and a non-empty `defs` is refused with a reason naming it
- [ ] `fill-rule="evenodd"`, a dash array, `url()` paint, `display="none"`, unknown attributes, a collapsing transform and a DTD are refused
- [ ] `TryResolvePath` refuses `..` escapes, rooted paths, drive letters, non-`.svg` files, missing files and a symlink pointing outside the folder
- [ ] `TryLoad` refuses a file over 16 KiB after reading at most 16 KiB + 1 bytes

**Verify:** `dotnet test tests/AcDream.Core.Tests/AcDream.Core.Tests.csproj --filter "FullyQualifiedName~PluginSvgIconTests"` → `Passed! - Failed: 0, Passed: 57`

**Steps:**

- [ ] **Step 1: Write the failing tests**

`tests/AcDream.Core.Tests/Plugins/PluginSvgIconTests.cs`:

```csharp
using System.Text;
using AcDream.Core.Plugins;

namespace AcDream.Core.Tests.Plugins;

public sealed class PluginSvgIconTests
{
    private const string Loot = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor"
             stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round">
          <path d="M5.5 8.5h13l-1 11h-11l-1-11Z"/>
          <path d="M9 8.5V7a3 3 0 0 1 6 0v1.5"/>
          <path d="M9.5 13h5"/>
        </svg>
        """;

    private static SvgIconDocument Accept(string svg)
    {
        Assert.True(PluginSvgIcon.TryParse(Encoding.UTF8.GetBytes(svg), out SvgIconDocument? document, out string? reason),
            reason);
        return document;
    }

    private static string Reject(string svg)
    {
        Assert.False(PluginSvgIcon.TryParse(Encoding.UTF8.GetBytes(svg), out _, out string? reason));
        return reason;
    }

    private static string Wrap(string body, string attributes = "") =>
        $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" {attributes}>{body}</svg>""";

    [Fact]
    public void AStrokedLucideIconParsesIntoStrokeLayers()
    {
        SvgIconDocument doc = Accept(Loot);
        Assert.Equal((0d, 0d, 24d, 24d), (doc.MinX, doc.MinY, doc.Width, doc.Height));
        Assert.Equal(3, doc.Layers.Count);
        Assert.All(doc.Layers, l =>
        {
            Assert.Equal(SvgPaintKind.Stroke, l.Kind);
            Assert.Equal(new SvgStrokeStyle(1.7, SvgLineCap.Round, SvgLineJoin.Round, 4), l.Stroke);
        });
    }

    [Fact]
    public void FillDefaultsToInkAndStrokeToNone()
    {
        SvgPaintLayer layer = Assert.Single(Accept(Wrap("""<circle cx="12" cy="12" r="5"/>""")).Layers);
        Assert.Equal(SvgPaintKind.Fill, layer.Kind);
    }

    [Fact]
    public void FillThenStrokeKeepDocumentOrder()
    {
        SvgIconDocument doc = Accept(Wrap("""<rect width="10" height="10" stroke="red"/><circle r="2"/>"""));
        Assert.Equal([SvgPaintKind.Fill, SvgPaintKind.Stroke, SvgPaintKind.Fill], doc.Layers.Select(l => l.Kind));
    }

    [Fact]
    public void StyleInheritsThroughGroupsAndTheStyleAttributeWins()
    {
        SvgIconDocument doc = Accept(Wrap("""
            <g stroke="#123" stroke-width="2" fill="none">
              <g style="stroke-linecap: square"><line x1="0" y1="0" x2="5" y2="5" stroke-width="9" style="stroke-width: 3"/></g>
            </g>
            """));
        SvgPaintLayer layer = Assert.Single(doc.Layers);
        Assert.Equal(3, layer.Stroke!.Width);
        Assert.Equal(SvgLineCap.Square, layer.Stroke.Cap);
    }

    [Fact]
    public void GroupOpacityMultipliesIntoEachChild()
    {
        SvgIconDocument doc = Accept(Wrap("""<g opacity="0.5"><path d="M0 0h4v4z" fill-opacity="0.5"/></g>"""));
        Assert.Equal(0.25, Assert.Single(doc.Layers).Opacity, 9);
    }

    [Fact]
    public void TransformsComposeFromTheRoot()
    {
        SvgIconDocument doc = Accept(Wrap("""<g transform="translate(10 0)"><rect width="1" height="1" transform="scale(2)"/></g>"""));
        SvgMatrix m = Assert.Single(doc.Layers).Transform;
        Assert.Equal(new SvgPoint(12, 2), m.Apply(new SvgPoint(1, 1)));
    }

    [Fact]
    public void RotateAboutAPointKeepsThatPointStill()
    {
        SvgIconDocument doc = Accept(Wrap("""<rect width="1" height="1" transform="rotate(90 12 12)"/>"""));
        SvgPoint p = Assert.Single(doc.Layers).Transform.Apply(new SvgPoint(12, 12));
        Assert.Equal(12, p.X, 9);
        Assert.Equal(12, p.Y, 9);
    }

    [Fact]
    public void WidthAndHeightStandInForAMissingViewBox()
    {
        SvgIconDocument doc = Accept("""<svg xmlns="http://www.w3.org/2000/svg" width="32px" height="16"><circle r="1"/></svg>""");
        Assert.Equal((32d, 16d), (doc.Width, doc.Height));
    }

    [Fact]
    public void TitleDescAndMetadataAreSkipped()
    {
        Accept(Wrap("""<title>Loot</title><desc>bag</desc><metadata><x/></metadata><defs/><path d="M0 0h1v1z"><title>t</title></path>"""));
    }

    [Fact]
    public void ANoNamespaceSvgIsAccepted()
    {
        Accept("""<svg viewBox="0 0 8 8"><rect width="8" height="8"/></svg>""");
    }

    [Theory]
    [InlineData("<use href=\"#a\"/>", "<use>")]
    [InlineData("<image href=\"a.png\"/>", "<image>")]
    [InlineData("<text>A</text>", "<text>")]
    [InlineData("<style>path{}</style>", "<style>")]
    [InlineData("<script>x</script>", "<script>")]
    [InlineData("<mask/>", "<mask>")]
    [InlineData("<clipPath/>", "<clipPath>")]
    [InlineData("<linearGradient/>", "<linearGradient>")]
    [InlineData("<filter/>", "<filter>")]
    [InlineData("<a><path d=\"M0 0h1v1z\"/></a>", "<a>")]
    [InlineData("<foreignObject/>", "<foreignObject>")]
    [InlineData("<svg/>", "<svg>")]
    public void UnsupportedElementsAreRejectedByName(string body, string named)
    {
        Assert.Contains(named, Reject(Wrap(body)), StringComparison.Ordinal);
    }

    [Fact]
    public void ADefsWithContentIsRejected()
    {
        Assert.Contains("<defs>", Reject(Wrap("""<defs><path id="a" d="M0 0h1v1z"/></defs>""")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("fill=\"url(#g)\"", "paint server")]
    [InlineData("fill-rule=\"evenodd\"", "nonzero")]
    [InlineData("style=\"fill-rule:evenodd\"", "nonzero")]
    [InlineData("stroke-dasharray=\"2 2\" stroke=\"red\"", "stroke-dasharray")]
    [InlineData("display=\"none\"", "display")]
    [InlineData("mask=\"url(#m)\"", "'mask'")]
    [InlineData("filter=\"url(#f)\"", "'filter'")]
    [InlineData("style=\"mix-blend-mode:multiply\"", "mix-blend-mode")]
    [InlineData("transform=\"scale(0)\"", "collapses")]
    [InlineData("transform=\"spin(3)\"", "transform")]
    [InlineData("xmlns:xlink=\"http://www.w3.org/1999/xlink\" xlink:href=\"#a\"", "xlink:href")]
    public void UnsupportedAttributesAreRejected(string attribute, string named)
    {
        Assert.Contains(named, Reject(Wrap($"""<path d="M0 0h1v1z" {attribute}/>""")), StringComparison.Ordinal);
    }

    [Fact]
    public void ADtdIsRejected()
    {
        string svg = """<?xml version="1.0"?><!DOCTYPE svg [<!ENTITY x "boom">]><svg viewBox="0 0 1 1"><title>&x;</title></svg>""";
        Assert.Contains("XML", Reject(svg), StringComparison.Ordinal);
    }

    [Fact]
    public void AFileOverTheSizeLimitIsRejected()
    {
        string svg = Wrap("<title>" + new string('a', PluginSvgIcon.MaximumBytes) + "</title>");
        Assert.Contains("16 KiB", Reject(svg), StringComparison.Ordinal);
    }

    [Fact]
    public void TooManyElementsAreRejected()
    {
        string body = string.Concat(Enumerable.Repeat("<g/>", PluginSvgIcon.MaximumElements));
        Assert.Contains("elements", Reject(Wrap(body)), StringComparison.Ordinal);
    }

    [Fact]
    public void NestingTooDeepIsRejected()
    {
        string body = string.Concat(Enumerable.Repeat("<g>", PluginSvgIcon.MaximumDepth))
            + string.Concat(Enumerable.Repeat("</g>", PluginSvgIcon.MaximumDepth));
        Assert.Contains("deep", Reject(Wrap(body)), StringComparison.Ordinal);
    }

    [Fact]
    public void TooManyPathCommandsAcrossTheFileAreRejected()
    {
        string d = "M0 0" + string.Concat(Enumerable.Repeat("h1", PluginSvgIcon.MaximumPathCommands / 2));
        Assert.Contains("path commands", Reject(Wrap($"""<path d="{d}"/><path d="{d}"/>""")), StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedPathDataNamesTheOffset()
    {
        Assert.Contains("offset", Reject(Wrap("""<path d="M0 0 L1 x"/>""")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""<svg viewBox="0 0 0 24"/>""")]
    [InlineData("""<svg viewBox="0 0 24"/>""")]
    [InlineData("""<svg/>""")]
    [InlineData("""<html/>""")]
    public void ADocumentWithoutAUsableViewBoxIsRejected(string svg)
    {
        Reject(svg);
    }

    [Fact]
    public void NotXmlIsRejected()
    {
        Assert.Contains("XML", Reject("<svg"), StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyShapesDrawNothing()
    {
        Assert.Empty(Accept(Wrap("""<circle r="0"/><rect width="0" height="4"/><path d=""/>""")).Layers);
    }

    [Fact]
    public void RectCornerRadiiFollowTheSvgRules()
    {
        SvgPaintLayer layer = Assert.Single(Accept(Wrap("""<rect width="10" height="4" rx="9"/>""")).Layers);
        SvgSubpath rect = Assert.Single(layer.Subpaths);
        // rx clamps to half the width, ry copies rx then clamps to half the height.
        Assert.Equal(new SvgPoint(5, 0), rect.Start);
        Assert.Equal(new SvgPoint(10, 2), rect.Segments[1].End);
    }

    [Fact]
    public void TryLoadReadsAFile()
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, PluginSvgIcon.FileName);
        File.WriteAllText(path, Loot);
        Assert.True(PluginSvgIcon.TryLoad(path, out SvgIconDocument? doc, out _));
        Assert.Equal(3, doc.Layers.Count);
    }

    [Fact]
    public void TryLoadRejectsAnOversizedFileWithoutReadingAllOfIt()
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, PluginSvgIcon.FileName);
        File.WriteAllBytes(path, new byte[PluginSvgIcon.MaximumBytes * 4]);
        Assert.False(PluginSvgIcon.TryLoad(path, out _, out string? reason));
        Assert.Contains("16 KiB", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolvePathAcceptsAFileInsideTheFolder()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, "icons"));
        File.WriteAllText(Path.Combine(directory.Path, "icons", "loot.svg"), Loot);
        Assert.True(PluginSvgIcon.TryResolvePath(directory.Path, @"icons\loot.svg", out string? full, out _));
        Assert.EndsWith("loot.svg", full, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../outside.svg")]
    [InlineData("icons/../../outside.svg")]
    [InlineData("/etc/icon.svg")]
    [InlineData("C:/icon.svg")]
    [InlineData("icons/loot.png")]
    [InlineData("missing.svg")]
    public void ResolvePathRejectsAnythingElse(string iconFile)
    {
        using var directory = new TemporaryDirectory();
        Assert.False(PluginSvgIcon.TryResolvePath(directory.Path, iconFile, out _, out string? reason));
        Assert.NotNull(reason);
    }

    [Fact]
    public void ResolvePathRejectsALinkThatLeavesTheFolder()
    {
        using var outside = new TemporaryDirectory();
        using var directory = new TemporaryDirectory();
        string target = Path.Combine(outside.Path, "real.svg");
        File.WriteAllText(target, Loot);
        string link = Path.Combine(directory.Path, "icon.svg");
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // Symlinks need a privilege on some Windows machines.
        }

        Assert.False(PluginSvgIcon.TryResolvePath(directory.Path, "icon.svg", out _, out string? reason));
        Assert.Contains("outside", reason, StringComparison.Ordinal);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"acdream-plugin-svg-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Run them to see them fail**

```bash
cd .worktrees/svg-icons && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test tests/AcDream.Core.Tests/AcDream.Core.Tests.csproj --filter "FullyQualifiedName~PluginSvgIconTests"
```

Expected: the build fails with `error CS0103: The name 'PluginSvgIcon' does not exist in the current context`.

- [ ] **Step 3: Write `PluginSvgIcon.cs`**

Notes for the implementer:

- Presentation attributes are read first and `style=""` declarations after them, so the style attribute wins, as in browsers. An element's own attribute beats a value inherited from its group.
- `opacity` is not inherited in SVG. A group's `opacity` is multiplied into each child's fill and stroke opacity (`GroupOpacity`), which is the approximation the spec accepts.
- A fill layer comes before a stroke layer for the same element (SVG's default paint order).

`src/AcDream.Core/Plugins/PluginSvgIcon.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Xml;

namespace AcDream.Core.Plugins;

public enum SvgPaintKind { Fill, Stroke }

public enum SvgLineCap { Butt, Round, Square }

public enum SvgLineJoin { Miter, Round, Bevel }

public sealed record SvgStrokeStyle(double Width, SvgLineCap Cap, SvgLineJoin Join, double MiterLimit);

/// <summary>One thing to paint, in document order: an element's fill or its stroke, in the
/// element's own user units, with the transform from those units to the icon's viewBox.</summary>
public sealed record SvgPaintLayer(
    SvgPaintKind Kind,
    double Opacity,
    IReadOnlyList<SvgSubpath> Subpaths,
    SvgStrokeStyle? Stroke,
    SvgMatrix Transform);

/// <summary>A parsed icon: its viewBox and what to paint. Coverage only; it has no colour.</summary>
public sealed record SvgIconDocument(
    double MinX, double MinY, double Width, double Height, IReadOnlyList<SvgPaintLayer> Layers);

/// <summary>
/// Reads a plugin's SVG icon: a small, safe subset of SVG 1.1 (see the plugin UI
/// markup docs). A file is accepted only if everything in it that could change
/// what is drawn is understood, so an icon is never drawn wrong. Never throws:
/// every rejection is a reason string, and the caller falls back to the next
/// icon.
/// </summary>
public static class PluginSvgIcon
{
    public const string FileName = "icon.svg";
    public const int MaximumBytes = 16 * 1024;
    public const int MaximumElements = 256;
    public const int MaximumDepth = 8;
    public const int MaximumPathCommands = 4096;

    private const string SvgNamespace = "http://www.w3.org/2000/svg";

    private static readonly HashSet<string> Drawn =
        ["path", "circle", "ellipse", "rect", "line", "polyline", "polygon"];

    private static readonly HashSet<string> Skipped = ["title", "desc", "metadata"];

    /// <summary>Attributes that never change what is drawn.</summary>
    private static readonly HashSet<string> Ignored =
    [
        "id", "class", "version", "vector-effect", "color", "shape-rendering", "overflow",
        "enable-background", "color-interpolation", "color-interpolation-filters", "baseProfile",
    ];

    /// <summary>Loads and parses the file at <paramref name="path"/>.</summary>
    public static bool TryLoad(
        string path, [NotNullWhen(true)] out SvgIconDocument? document, [NotNullWhen(false)] out string? reason)
    {
        document = null;
        try
        {
            using FileStream stream = File.OpenRead(path);
            var buffer = new byte[MaximumBytes + 1];
            int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            if (read > MaximumBytes)
            {
                reason = $"is larger than {MaximumBytes / 1024} KiB";
                return false;
            }
            return TryParse(buffer.AsSpan(0, read).ToArray(), out document, out reason);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            reason = "could not be read: " + ex.Message;
            return false;
        }
    }

    /// <summary>Parses an icon from its bytes.</summary>
    public static bool TryParse(
        byte[] bytes, [NotNullWhen(true)] out SvgIconDocument? document, [NotNullWhen(false)] out string? reason)
    {
        document = null;
        if (bytes.Length > MaximumBytes)
        {
            reason = $"is larger than {MaximumBytes / 1024} KiB";
            return false;
        }

        try
        {
            var parser = new Parser();
            document = parser.Parse(bytes);
            reason = parser.Reason;
            return document is not null;
        }
        catch (XmlException ex)
        {
            reason = "is not well-formed XML: " + ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Resolves a descriptor's <c>IconFile</c> against the plugin folder. The path must be
    /// relative, end in <c>.svg</c>, and stay inside the folder after links are followed.
    /// </summary>
    public static bool TryResolvePath(
        string pluginDirectory, string iconFile,
        [NotNullWhen(true)] out string? fullPath, [NotNullWhen(false)] out string? reason)
    {
        fullPath = null;
        string relative = iconFile.Replace('\\', '/');
        if (relative.Length == 0 || relative.StartsWith('/') || Path.IsPathRooted(relative)
            || (relative.Length >= 2 && relative[1] == ':'))
        {
            reason = "points outside the plugin folder";
            return false;
        }
        if (!relative.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            reason = "is not an .svg file";
            return false;
        }

        try
        {
            string root = Path.GetFullPath(pluginDirectory);
            string candidate = Path.GetFullPath(Path.Combine(root, relative));
            if (!IsInside(root, candidate))
            {
                reason = "points outside the plugin folder";
                return false;
            }
            if (!File.Exists(candidate))
            {
                reason = "does not exist";
                return false;
            }

            string resolvedRoot = ResolveLinks(root);
            string resolved = ResolveLinks(candidate);
            if (!IsInside(resolvedRoot, resolved))
            {
                reason = "points outside the plugin folder";
                return false;
            }
            fullPath = resolved;
            reason = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException)
        {
            reason = "could not be resolved: " + ex.Message;
            return false;
        }
    }

    private static bool IsInside(string root, string path)
    {
        string prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return path.StartsWith(prefix, comparison);
    }

    /// <summary>Follows every link along <paramref name="path"/>, directories included.</summary>
    private static string ResolveLinks(string path)
    {
        string full = Path.GetFullPath(path);
        string? parent = Path.GetDirectoryName(full);
        string resolvedParent = parent is null || parent == full ? full : ResolveLinks(parent);
        string joined = parent is null || parent == full ? full : Path.Combine(resolvedParent, Path.GetFileName(full));
        FileSystemInfo info = Directory.Exists(joined) ? new DirectoryInfo(joined) : new FileInfo(joined);
        FileSystemInfo? target = info.LinkTarget is null ? null : info.ResolveLinkTarget(returnFinalTarget: true);
        return target is null ? joined : Path.GetFullPath(target.FullName);
    }

    private readonly record struct Style(
        bool Fill, bool Stroke, double FillOpacity, double StrokeOpacity, double GroupOpacity,
        double StrokeWidth, SvgLineCap Cap, SvgLineJoin Join, double MiterLimit);

    private sealed class Parser
    {
        private int _elements;
        private int _commands;
        private readonly List<SvgPaintLayer> _layers = [];

        public string? Reason { get; private set; }

        private SvgIconDocument? Fail(string reason)
        {
            Reason = reason;
            return null;
        }

        public SvgIconDocument? Parse(byte[] bytes)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersFromEntities = 0,
                MaxCharactersInDocument = MaximumBytes,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true,
            };
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = XmlReader.Create(stream, settings);
            if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != "svg" || !IsSvgNamespace(reader))
                return Fail("is not an SVG document");

            var style = new Style(true, false, 1, 1, 1, 1, SvgLineCap.Butt, SvgLineJoin.Miter, 4);
            Dictionary<string, string> attributes = ReadAttributes(reader);
            if (Reason is not null) return null;
            if (!TryViewBox(attributes, out double minX, out double minY, out double width, out double height))
                return Fail(Reason ?? "has no usable viewBox, width or height");
            attributes.Remove("viewBox");
            attributes.Remove("width");
            attributes.Remove("height");
            attributes.Remove("preserveAspectRatio");
            attributes.Remove("x");
            attributes.Remove("y");
            if (!TryStyle(attributes, ref style, out SvgMatrix transform, out double opacity)) return null;
            if (!NoneLeft(attributes, "svg")) return null;
            style = style with { GroupOpacity = style.GroupOpacity * opacity };

            _elements = 1;
            if (!reader.IsEmptyElement && !ReadChildren(reader, style, transform, depth: 1)) return null;
            return new SvgIconDocument(minX, minY, width, height, _layers);
        }

        private static bool IsSvgNamespace(XmlReader reader) =>
            reader.NamespaceURI.Length == 0 || reader.NamespaceURI == SvgNamespace;

        private bool ReadChildren(XmlReader reader, Style inherited, SvgMatrix ctm, int depth)
        {
            int parentDepth = reader.Depth;
            reader.Read();
            while (!reader.EOF && reader.Depth > parentDepth)
            {
                if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA)
                {
                    reader.Read();
                    continue;
                }
                if (reader.NodeType != XmlNodeType.Element)
                {
                    reader.Read();
                    continue;
                }
                if (!ReadElement(reader, inherited, ctm, depth)) return false;
            }
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == parentDepth) reader.Read();
            return true;
        }

        private bool ReadElement(XmlReader reader, Style inherited, SvgMatrix ctm, int depth)
        {
            string name = reader.LocalName;
            if (!IsSvgNamespace(reader))
            {
                Reason = $"uses <{reader.Name}>, which plugin icons do not support";
                return false;
            }
            if (++_elements > MaximumElements)
            {
                Reason = $"has more than {MaximumElements} elements";
                return false;
            }
            if (depth >= MaximumDepth)
            {
                Reason = $"nests elements more than {MaximumDepth} deep";
                return false;
            }

            if (Skipped.Contains(name))
            {
                reader.Skip();
                return true;
            }
            if (name == "defs")
            {
                if (!reader.IsEmptyElement)
                {
                    using XmlReader inner = reader.ReadSubtree();
                    inner.Read();
                    while (inner.Read())
                    {
                        if (inner.NodeType == XmlNodeType.Element)
                        {
                            Reason = "uses <defs> with content, which plugin icons do not support";
                            return false;
                        }
                    }
                }
                reader.Skip();
                return true;
            }
            if (name != "g" && !Drawn.Contains(name))
            {
                Reason = $"uses <{name}>, which plugin icons do not support";
                return false;
            }

            bool empty = reader.IsEmptyElement;
            Dictionary<string, string> attributes = ReadAttributes(reader);
            if (Reason is not null) return false;
            Style style = inherited;
            if (!TryStyle(attributes, ref style, out SvgMatrix own, out double opacity)) return false;
            SvgMatrix transform = ctm.Then(own);
            style = style with { GroupOpacity = style.GroupOpacity * opacity };

            if (name == "g")
            {
                if (!NoneLeft(attributes, "g")) return false;
                if (empty)
                {
                    reader.Read();
                    return true;
                }
                return ReadChildren(reader, style, transform, depth + 1);
            }

            var subpaths = new List<SvgSubpath>();
            if (!TryGeometry(name, attributes, subpaths)) return false;
            if (!NoneLeft(attributes, name)) return false;
            if (subpaths.Count > 0)
            {
                double fillOpacity = style.FillOpacity * style.GroupOpacity;
                double strokeOpacity = style.StrokeOpacity * style.GroupOpacity;
                if (style.Fill && fillOpacity > 0)
                    _layers.Add(new SvgPaintLayer(SvgPaintKind.Fill, fillOpacity, subpaths, null, transform));
                if (style.Stroke && strokeOpacity > 0 && style.StrokeWidth > 0)
                {
                    _layers.Add(new SvgPaintLayer(SvgPaintKind.Stroke, strokeOpacity, subpaths,
                        new SvgStrokeStyle(style.StrokeWidth, style.Cap, style.Join, style.MiterLimit), transform));
                }
            }

            if (empty)
            {
                reader.Read();
                return true;
            }
            // A drawn element may hold only title/desc text; anything else is refused.
            int parentDepth = reader.Depth;
            reader.Read();
            while (!reader.EOF && reader.Depth > parentDepth)
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (!Skipped.Contains(reader.LocalName))
                    {
                        Reason = $"puts <{reader.LocalName}> inside <{name}>, which plugin icons do not support";
                        return false;
                    }
                    reader.Skip();
                    continue;
                }
                reader.Read();
            }
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == parentDepth) reader.Read();
            return true;
        }

        private bool NoneLeft(Dictionary<string, string> attributes, string element)
        {
            if (attributes.Count == 0) return true;
            Reason = $"uses the attribute '{attributes.Keys.First()}' on <{element}>, which plugin icons do not support";
            return false;
        }

        private Dictionary<string, string> ReadAttributes(XmlReader reader)
        {
            var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
            if (reader.MoveToFirstAttribute())
            {
                do
                {
                    string name = reader.Name;
                    if (name == "xmlns" || name.StartsWith("xmlns:", StringComparison.Ordinal)
                        || name.StartsWith("xml:", StringComparison.Ordinal)
                        || name.StartsWith("aria-", StringComparison.Ordinal)
                        || name.StartsWith("data-", StringComparison.Ordinal)
                        || name.StartsWith("sodipodi:", StringComparison.Ordinal)
                        || name.StartsWith("inkscape:", StringComparison.Ordinal)
                        || Ignored.Contains(name))
                        continue;
                    if (name.Contains(':'))
                    {
                        Reason = $"uses the attribute '{name}', which plugin icons do not support";
                        break;
                    }
                    attributes[name] = reader.Value;
                }
                while (reader.MoveToNextAttribute());
                reader.MoveToElement();
            }
            return attributes;
        }

        private bool TryViewBox(
            Dictionary<string, string> attributes, out double minX, out double minY, out double width, out double height)
        {
            minX = minY = width = height = 0;
            if (attributes.TryGetValue("viewBox", out string? viewBox))
            {
                if (!SvgOutline.TryParseNumbers(viewBox, out List<double> n) || n.Count != 4 || !(n[2] > 0) || !(n[3] > 0))
                {
                    Reason = "has a malformed viewBox";
                    return false;
                }
                (minX, minY, width, height) = (n[0], n[1], n[2], n[3]);
                return true;
            }
            if (attributes.TryGetValue("width", out string? w) && attributes.TryGetValue("height", out string? h)
                && TryLength(w, out width) && TryLength(h, out height) && width > 0 && height > 0)
                return true;
            return false;
        }

        private bool TryStyle(Dictionary<string, string> attributes, ref Style style, out SvgMatrix transform, out double opacity)
        {
            transform = SvgMatrix.Identity;
            opacity = 1;
            var properties = new List<(string Name, string Value)>();
            foreach (string name in PresentationNames)
            {
                if (attributes.Remove(name, out string? value)) properties.Add((name, value));
            }
            if (attributes.Remove("style", out string? css))
            {
                foreach (string declaration in css.Split(';'))
                {
                    if (string.IsNullOrWhiteSpace(declaration)) continue;
                    int colon = declaration.IndexOf(':');
                    if (colon <= 0)
                    {
                        Reason = "has a malformed style attribute";
                        return false;
                    }
                    string name = declaration[..colon].Trim();
                    string value = declaration[(colon + 1)..].Trim();
                    if (Ignored.Contains(name)) continue;
                    if (!PresentationNames.Contains(name) || name == "transform")
                    {
                        Reason = $"uses the style property '{name}', which plugin icons do not support";
                        return false;
                    }
                    properties.Add((name, value));
                }
            }

            foreach ((string name, string raw) in properties)
            {
                string value = raw.Trim();
                if (value == "inherit") continue;
                switch (name)
                {
                    case "fill":
                    case "stroke":
                        if (!TryPaint(value, out bool ink)) return false;
                        style = name == "fill" ? style with { Fill = ink } : style with { Stroke = ink };
                        break;
                    case "fill-opacity":
                    case "stroke-opacity":
                    case "opacity":
                        if (!TryOpacity(value, out double o)) return Bad(name);
                        if (name == "fill-opacity") style = style with { FillOpacity = o };
                        else if (name == "stroke-opacity") style = style with { StrokeOpacity = o };
                        else opacity = o;
                        break;
                    case "stroke-width":
                        if (!TryLength(value, out double width) || width < 0) return Bad(name);
                        style = style with { StrokeWidth = width };
                        break;
                    case "stroke-linecap":
                        SvgLineCap? cap = value switch
                        {
                            "butt" => SvgLineCap.Butt, "round" => SvgLineCap.Round, "square" => SvgLineCap.Square, _ => null,
                        };
                        if (cap is null) return Bad(name);
                        style = style with { Cap = cap.Value };
                        break;
                    case "stroke-linejoin":
                        SvgLineJoin? join = value switch
                        {
                            "miter" => SvgLineJoin.Miter, "round" => SvgLineJoin.Round, "bevel" => SvgLineJoin.Bevel, _ => null,
                        };
                        if (join is null) return Bad(name);
                        style = style with { Join = join.Value };
                        break;
                    case "stroke-miterlimit":
                        if (!TryNumber(value, out double limit) || limit < 1) return Bad(name);
                        style = style with { MiterLimit = limit };
                        break;
                    case "fill-rule":
                    case "clip-rule":
                        if (value != "nonzero")
                        {
                            Reason = $"uses {name}=\"{value}\"; plugin icons support only nonzero";
                            return false;
                        }
                        break;
                    case "stroke-dasharray":
                        if (value != "none")
                        {
                            Reason = "uses stroke-dasharray, which plugin icons do not support";
                            return false;
                        }
                        break;
                    case "stroke-dashoffset":
                        break;
                    case "display":
                        if (value is not ("inline" or "block"))
                        {
                            Reason = $"uses display=\"{value}\", which plugin icons do not support";
                            return false;
                        }
                        break;
                    case "visibility":
                        if (value != "visible")
                        {
                            Reason = $"uses visibility=\"{value}\", which plugin icons do not support";
                            return false;
                        }
                        break;
                    case "transform":
                        if (!TryTransform(value, out transform))
                        {
                            Reason ??= "has a malformed transform";
                            return false;
                        }
                        break;
                }
            }
            return true;
        }

        private bool Bad(string name)
        {
            Reason = $"has a malformed {name}";
            return false;
        }

        private static readonly HashSet<string> PresentationNames =
        [
            "fill", "stroke", "fill-opacity", "stroke-opacity", "opacity", "stroke-width",
            "stroke-linecap", "stroke-linejoin", "stroke-miterlimit", "fill-rule", "clip-rule",
            "stroke-dasharray", "stroke-dashoffset", "display", "visibility", "transform",
        ];

        private bool TryPaint(string value, out bool ink)
        {
            ink = false;
            if (value is "none" or "transparent") return true;
            if (value.StartsWith("url(", StringComparison.Ordinal))
            {
                Reason = "uses a paint server (url(...)), which plugin icons do not support";
                return false;
            }
            if (value == "currentColor" || value.StartsWith('#') || value.StartsWith("rgb", StringComparison.Ordinal)
                || value.StartsWith("hsl", StringComparison.Ordinal) || value.All(char.IsAsciiLetter))
            {
                ink = true;
                return true;
            }
            Reason = $"has an unknown paint '{value}'";
            return false;
        }

        private static bool TryOpacity(string value, out double opacity)
        {
            bool percent = value.EndsWith('%');
            bool ok = TryNumber(percent ? value[..^1] : value, out opacity);
            if (percent) opacity /= 100;
            opacity = Math.Clamp(opacity, 0, 1);
            return ok;
        }

        private static bool TryNumber(string value, out double number) =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number);

        private static bool TryLength(string value, out double number)
        {
            string trimmed = value.Trim();
            if (trimmed.EndsWith("px", StringComparison.Ordinal)) trimmed = trimmed[..^2];
            return TryNumber(trimmed, out number);
        }

        private bool TryTransform(string text, out SvgMatrix matrix)
        {
            matrix = SvgMatrix.Identity;
            int i = 0;
            while (true)
            {
                while (i < text.Length && (char.IsWhiteSpace(text[i]) || text[i] == ',')) i++;
                if (i >= text.Length) break;
                int nameStart = i;
                while (i < text.Length && char.IsAsciiLetter(text[i])) i++;
                string name = text[nameStart..i];
                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                if (i >= text.Length || text[i] != '(') return false;
                int close = text.IndexOf(')', i);
                if (close < 0) return false;
                if (!SvgOutline.TryParseNumbers(text[(i + 1)..close], out List<double> n)) return false;
                i = close + 1;
                SvgMatrix? step = (name, n.Count) switch
                {
                    ("matrix", 6) => new SvgMatrix(n[0], n[1], n[2], n[3], n[4], n[5]),
                    ("translate", 1) => new SvgMatrix(1, 0, 0, 1, n[0], 0),
                    ("translate", 2) => new SvgMatrix(1, 0, 0, 1, n[0], n[1]),
                    ("scale", 1) => new SvgMatrix(n[0], 0, 0, n[0], 0, 0),
                    ("scale", 2) => new SvgMatrix(n[0], 0, 0, n[1], 0, 0),
                    ("rotate", 1) => Rotate(n[0], 0, 0),
                    ("rotate", 3) => Rotate(n[0], n[1], n[2]),
                    ("skewX", 1) => new SvgMatrix(1, 0, Math.Tan(n[0] * Math.PI / 180), 1, 0, 0),
                    ("skewY", 1) => new SvgMatrix(1, Math.Tan(n[0] * Math.PI / 180), 0, 1, 0, 0),
                    _ => null,
                };
                if (step is null) return false;
                matrix = matrix.Then(step.Value);
            }
            if (!double.IsFinite(matrix.Determinant) || Math.Abs(matrix.Determinant) < 1e-12)
            {
                Reason = "has a transform that collapses the shape";
                return false;
            }
            return true;
        }

        private static SvgMatrix Rotate(double degrees, double cx, double cy)
        {
            double a = degrees * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
            return new SvgMatrix(1, 0, 0, 1, cx, cy)
                .Then(new SvgMatrix(cos, sin, -sin, cos, 0, 0))
                .Then(new SvgMatrix(1, 0, 0, 1, -cx, -cy));
        }

        private bool TryGeometry(string name, Dictionary<string, string> a, List<SvgSubpath> into)
        {
            double Number(string key, double fallback = 0)
            {
                if (!a.Remove(key, out string? text)) return fallback;
                if (TryLength(text, out double value)) return value;
                Reason ??= $"has a malformed {key} on <{name}>";
                return double.NaN;
            }

            switch (name)
            {
                case "path":
                    if (!a.Remove("d", out string? d)) return true;
                    if (!SvgOutline.TryParsePath(d, into, ref _commands, MaximumPathCommands, out string? why))
                    {
                        Reason = why;
                        return false;
                    }
                    return true;
                case "circle":
                {
                    double cx = Number("cx"), cy = Number("cy"), r = Number("r");
                    if (Reason is not null) return false;
                    if (r < 0) return Bad("r");
                    if (r > 0) into.Add(SvgOutline.Ellipse(cx, cy, r, r));
                    return true;
                }
                case "ellipse":
                {
                    double cx = Number("cx"), cy = Number("cy"), rx = Number("rx"), ry = Number("ry");
                    if (Reason is not null) return false;
                    if (rx < 0 || ry < 0) return Bad("rx");
                    if (rx > 0 && ry > 0) into.Add(SvgOutline.Ellipse(cx, cy, rx, ry));
                    return true;
                }
                case "rect":
                {
                    bool hasRx = a.ContainsKey("rx"), hasRy = a.ContainsKey("ry");
                    double x = Number("x"), y = Number("y"), w = Number("width"), h = Number("height");
                    double rx = Number("rx"), ry = Number("ry");
                    if (Reason is not null) return false;
                    if (w < 0 || h < 0 || rx < 0 || ry < 0) return Bad("rect size");
                    if (hasRx && !hasRy) ry = rx;
                    if (hasRy && !hasRx) rx = ry;
                    rx = Math.Min(rx, w / 2);
                    ry = Math.Min(ry, h / 2);
                    if (w > 0 && h > 0) into.Add(SvgOutline.Rect(x, y, w, h, rx, ry));
                    return true;
                }
                case "line":
                {
                    double x1 = Number("x1"), y1 = Number("y1"), x2 = Number("x2"), y2 = Number("y2");
                    if (Reason is not null) return false;
                    into.Add(SvgOutline.Poly([new(x1, y1), new(x2, y2)], closed: false));
                    return true;
                }
                default: // polyline, polygon
                {
                    if (!a.Remove("points", out string? text)) return true;
                    if (!SvgOutline.TryParsePoints(text, out List<SvgPoint> points)) return Bad("points");
                    into.Add(SvgOutline.Poly(points, closed: name == "polygon"));
                    return true;
                }
            }
        }
    }
}
```

- [ ] **Step 4: Run the tests again**

```bash
dotnet test tests/AcDream.Core.Tests/AcDream.Core.Tests.csproj --filter "FullyQualifiedName~PluginSvgIconTests|FullyQualifiedName~SvgOutlineTests"
```

Expected: `Passed! - Failed: 0, Passed: 84` (57 new plus Task 1's 27).

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.Core/Plugins/PluginSvgIcon.cs \
  tests/AcDream.Core.Tests/Plugins/PluginSvgIconTests.cs
git commit -m "feat: parse plugin SVG icons under a small, safe subset" -m "Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.Core/Plugins/PluginSvgIcon.cs", "tests/AcDream.Core.Tests/Plugins/PluginSvgIconTests.cs"], "verifyCommand": "cd .worktrees/svg-icons && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH && dotnet test tests/AcDream.Core.Tests/AcDream.Core.Tests.csproj --filter \"FullyQualifiedName~PluginSvgIconTests\"", "modelTier": "standard", "acceptanceCriteria": ["`PluginSvgIconTests` has 57 tests and all pass", "Every rejected element (`use image text style script mask clipPath linearGradient filter a foreignObject` and nested `svg`) and a non-empty `defs` is refused with a reason naming it", "`fill-rule=\"evenodd\"`, a dash array, `url()` paint, `display=\"none\"`, unknown attributes, a collapsing transform and a DTD are refused", "`TryResolvePath` refuses `..` escapes, rooted paths, drive letters, non-`.svg` files, missing files and a symlink pointing outside the folder", "`TryLoad` refuses a file over 16 KiB after reading at most 16 KiB + 1 bytes"]}
```

---

### Task 3: Stroke expansion (SvgStroker)

**Goal:** `SvgStroker.Expand` turns a stroke layer into closed polygons in device space: one rectangle per flattened edge, miter/round/bevel joins, butt/round/square caps, dots for zero-length subpaths, all built in the element's own units and then transformed, each turned to positive area so they union under the nonzero rule. `Flatten` cuts curves into chords within a device-pixel tolerance.

**Files:**
- Create: `src/AcDream.Core/Plugins/SvgStroker.cs`
- Test: `tests/AcDream.Core.Tests/Plugins/SvgStrokerTests.cs`

**Acceptance Criteria:**
- [ ] `SvgStrokerTests` has 15 tests and all pass
- [ ] Every piece has positive signed area for every join × cap, and after a reflecting transform
- [ ] A 90° miter reaches the corner (11, −1) for a width-2 stroke; a 10° spike falls back to a bevel under the default limit 4
- [ ] A non-uniform scale (1, 3) makes a width-2 horizontal stroke 6 units tall
- [ ] Flattening a radius-10 circle at tolerance 0.05 keeps every chord midpoint within [9.94, 10.03]

**Verify:** `dotnet test tests/AcDream.Core.Tests/AcDream.Core.Tests.csproj --filter "FullyQualifiedName~SvgStrokerTests"` → `Passed! - Failed: 0, Passed: 15`

**Steps:**

- [ ] **Step 1: Write the failing tests**

`tests/AcDream.Core.Tests/Plugins/SvgStrokerTests.cs`:

```csharp
using AcDream.Core.Plugins;

namespace AcDream.Core.Tests.Plugins;

public sealed class SvgStrokerTests
{
    private static SvgPaintLayer Stroke(
        SvgSubpath path, double width = 2, SvgLineCap cap = SvgLineCap.Butt, SvgLineJoin join = SvgLineJoin.Miter,
        double miterLimit = 4, SvgMatrix? transform = null) =>
        new(SvgPaintKind.Stroke, 1, [path], new SvgStrokeStyle(width, cap, join, miterLimit), transform ?? SvgMatrix.Identity);

    private static SvgSubpath Open(params SvgPoint[] points) => SvgOutline.Poly(points, closed: false);

    private static List<SvgPoint[]> Expand(SvgPaintLayer layer) => SvgStroker.Expand(layer, SvgMatrix.Identity, 0.05);

    [Fact]
    public void EveryPieceHasPositiveArea()
    {
        SvgSubpath zigzag = Open(new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(5, 3));
        foreach (SvgLineJoin join in Enum.GetValues<SvgLineJoin>())
        foreach (SvgLineCap cap in Enum.GetValues<SvgLineCap>())
        {
            Assert.All(Expand(Stroke(zigzag, cap: cap, join: join)),
                piece => Assert.True(SvgStroker.SignedArea(piece) > 0));
        }
    }

    [Fact]
    public void AReflectingTransformStillGivesPositiveArea()
    {
        var flip = new SvgMatrix(-1, 0, 0, 1, 24, 0);
        Assert.All(Expand(Stroke(Open(new(0, 0), new(10, 0), new(10, 10)), transform: flip)),
            piece => Assert.True(SvgStroker.SignedArea(piece) > 0));
    }

    [Fact]
    public void AnOpenPolylineWithRoundJoinsAndCapsHasRectsJoinsAndCaps()
    {
        List<SvgPoint[]> pieces = Expand(Stroke(Open(new(0, 0), new(10, 0), new(10, 10)),
            cap: SvgLineCap.Round, join: SvgLineJoin.Round));
        Assert.Equal(2 + 1 + 2, pieces.Count);
    }

    [Fact]
    public void ButtCapsAddNothing()
    {
        Assert.Single(Expand(Stroke(Open(new(0, 0), new(10, 0)))));
    }

    [Fact]
    public void SquareCapsExtendTheEndsByHalfTheWidth()
    {
        SvgPoint[] rect = Assert.Single(Expand(Stroke(Open(new(0, 0), new(10, 0)), width: 2, cap: SvgLineCap.Square)));
        Assert.Equal(-1, rect.Min(p => p.X), 9);
        Assert.Equal(11, rect.Max(p => p.X), 9);
        Assert.Equal(-1, rect.Min(p => p.Y), 9);
        Assert.Equal(1, rect.Max(p => p.Y), 9);
    }

    [Fact]
    public void ARightAngleMiterReachesTheCorner()
    {
        List<SvgPoint[]> pieces = Expand(Stroke(Open(new(0, 0), new(10, 0), new(10, 10)), width: 2));
        SvgPoint[] miter = Assert.Single(pieces, p => p.Length == 4 && p.Contains(new SvgPoint(10, 0)));
        Assert.Contains(miter, p => Math.Abs(p.X - 11) < 1e-9 && Math.Abs(p.Y + 1) < 1e-9);
    }

    [Fact]
    public void ASharpMiterPastTheLimitFallsBackToABevel()
    {
        // A 10° spike needs a miter ratio of about 11.5, over the default limit of 4.
        double angle = 10 * Math.PI / 180;
        var tip = new SvgPoint(10, 0);
        var back = new SvgPoint(10 - 10 * Math.Cos(angle), 10 * Math.Sin(angle));
        List<SvgPoint[]> pieces = Expand(Stroke(Open(new(0, 0), tip, back)));
        Assert.Contains(pieces, p => p.Length == 3);
        Assert.DoesNotContain(pieces, p => p.Length == 4 && p.Contains(tip));
    }

    [Fact]
    public void ABevelJoinIsATriangle()
    {
        List<SvgPoint[]> pieces = Expand(Stroke(Open(new(0, 0), new(10, 0), new(10, 10)), join: SvgLineJoin.Bevel));
        Assert.Equal(3, pieces.Count);
        Assert.Single(pieces, p => p.Length == 3);
    }

    [Fact]
    public void AClosedSquareHasAJoinAtEveryCornerAndNoCaps()
    {
        SvgSubpath square = SvgOutline.Rect(0, 0, 10, 10, 0, 0);
        List<SvgPoint[]> pieces = Expand(Stroke(square, cap: SvgLineCap.Round));
        Assert.Equal(4 + 4, pieces.Count);
        Assert.DoesNotContain(pieces, p => p.Length > 4);
    }

    [Theory]
    [InlineData(SvgLineCap.Round, 1)]
    [InlineData(SvgLineCap.Square, 1)]
    [InlineData(SvgLineCap.Butt, 0)]
    public void AZeroLengthSubpathIsADotOnlyWithRoundOrSquareCaps(SvgLineCap cap, int expected)
    {
        Assert.Equal(expected, Expand(Stroke(Open(new(3, 3), new(3, 3)), cap: cap)).Count);
    }

    [Fact]
    public void ANonUniformScaleStretchesTheStroke()
    {
        var stretch = new SvgMatrix(1, 0, 0, 3, 0, 0);
        SvgPoint[] rect = Assert.Single(SvgStroker.Expand(Stroke(Open(new(0, 0), new(10, 0)), width: 2, transform: stretch),
            SvgMatrix.Identity, 0.05));
        Assert.Equal(6, rect.Max(p => p.Y) - rect.Min(p => p.Y), 9);
    }

    [Fact]
    public void FlatteningStaysWithinTolerance()
    {
        SvgSubpath circle = SvgOutline.Ellipse(0, 0, 10, 10);
        List<SvgPoint> points = SvgStroker.Flatten(circle, 0.05);
        for (int i = 1; i < points.Count; i++)
        {
            SvgPoint mid = (points[i - 1] + points[i]) * 0.5;
            Assert.InRange(mid.Length, 10 - 0.06, 10 + 0.03);
        }
    }

    [Fact]
    public void ToleranceIsMeasuredInDeviceUnits()
    {
        SvgSubpath circle = SvgOutline.Ellipse(0, 0, 1, 1);
        var big = new SvgMatrix(50, 0, 0, 50, 0, 0);
        int small = Expand(Stroke(circle, width: 0.1, join: SvgLineJoin.Bevel)).Count;
        int large = SvgStroker.Expand(Stroke(circle, width: 0.1, join: SvgLineJoin.Bevel), big, 0.05).Count;
        Assert.True(large > small, $"{large} pieces at 50x should exceed {small} at 1x");
    }
}
```

- [ ] **Step 2: Run them to see them fail**

```bash
cd .worktrees/svg-icons && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test tests/AcDream.Core.Tests/AcDream.Core.Tests.csproj --filter "FullyQualifiedName~SvgStrokerTests"
```

Expected: the build fails with `error CS0103: The name 'SvgStroker' does not exist in the current context`.

- [ ] **Step 3: Write `SvgStroker.cs`**

The flattening bounds: a quadratic's chord error with n even steps is at most |p0 − 2c + p2| / (4n²), and a cubic's at most 0.75 · max(|p0 − 2c1 + c2|, |c1 − 2c2 + p3|) / n². The local tolerance is the device tolerance divided by the transform's largest stretch (`SvgMatrix.MaximumScale`), so a stroke drawn at 2× gets twice the chords.

`src/AcDream.Core/Plugins/SvgStroker.cs`:

```csharp
namespace AcDream.Core.Plugins;

/// <summary>
/// Turns a stroke into closed polygons that a nonzero fill draws as the
/// stroke: one rectangle per flattened edge, plus joins and caps. The pieces
/// overlap, and every one is turned to the same winding, so they add up
/// instead of cancelling. Work happens in the element's own units and is
/// transformed afterwards, so a non-uniform scale or a skew gives the stroke
/// SVG would draw. Pure: no I/O, no GL.
/// </summary>
public static class SvgStroker
{
    private const double Epsilon = 1e-9;

    /// <summary>Expands <paramref name="layer"/>'s stroke into polygons in the space
    /// <paramref name="toDevice"/> maps the icon's viewBox to, flattened to within
    /// <paramref name="tolerance"/> of that space's units.</summary>
    public static List<SvgPoint[]> Expand(SvgPaintLayer layer, SvgMatrix toDevice, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(layer.Stroke);
        SvgStrokeStyle style = layer.Stroke;
        SvgMatrix m = toDevice.Then(layer.Transform);
        double local = tolerance / Math.Max(m.MaximumScale, Epsilon);
        double h = style.Width / 2;
        var pieces = new List<SvgPoint[]>();

        foreach (SvgSubpath subpath in layer.Subpaths)
        {
            List<SvgPoint> points = Distinct(Flatten(subpath, local), subpath.Closed);
            if (points.Count == 1)
            {
                SvgPoint p = points[0];
                if (style.Cap == SvgLineCap.Round) pieces.Add(Circle(p, h, local));
                else if (style.Cap == SvgLineCap.Square)
                    pieces.Add([new(p.X - h, p.Y - h), new(p.X + h, p.Y - h), new(p.X + h, p.Y + h), new(p.X - h, p.Y + h)]);
                continue;
            }

            bool closed = subpath.Closed && points.Count > 2;
            int edgeCount = closed ? points.Count : points.Count - 1;
            for (int i = 0; i < edgeCount; i++)
            {
                SvgPoint a = points[i], b = points[(i + 1) % points.Count];
                if (!closed && style.Cap == SvgLineCap.Square)
                {
                    if (i == 0) a -= Unit(b - a) * h;
                    if (i == edgeCount - 1) b += Unit(b - a) * h;
                }
                SvgPoint n = Normal(b - a) * h;
                pieces.Add([a + n, b + n, b - n, a - n]);
            }

            int joins = closed ? points.Count : points.Count - 2;
            for (int j = 0; j < joins; j++)
            {
                int at = closed ? j : j + 1;
                SvgPoint v = points[at];
                SvgPoint before = points[(at - 1 + points.Count) % points.Count];
                SvgPoint after = points[(at + 1) % points.Count];
                if (Join(v, before, after, h, style, local) is { } join) pieces.Add(join);
            }

            if (!closed && style.Cap == SvgLineCap.Round)
            {
                pieces.Add(Circle(points[0], h, local));
                pieces.Add(Circle(points[^1], h, local));
            }
        }

        var result = new List<SvgPoint[]>(pieces.Count);
        foreach (SvgPoint[] piece in pieces)
        {
            for (int i = 0; i < piece.Length; i++) piece[i] = m.Apply(piece[i]);
            double area = SignedArea(piece);
            if (Math.Abs(area) < Epsilon) continue;
            if (area < 0) Array.Reverse(piece);
            result.Add(piece);
        }
        return result;
    }

    /// <summary>A subpath as points, each curve cut into chords no further than
    /// <paramref name="tolerance"/> from it.</summary>
    public static List<SvgPoint> Flatten(SvgSubpath subpath, double tolerance)
    {
        var points = new List<SvgPoint> { subpath.Start };
        SvgPoint current = subpath.Start;
        foreach (SvgSegment s in subpath.Segments)
        {
            switch (s.Kind)
            {
                case SvgSegmentKind.Line:
                    points.Add(s.End);
                    break;
                case SvgSegmentKind.Quadratic:
                {
                    double dd = (current - s.C1 * 2 + s.End).Length;
                    int n = Steps(dd / 4, tolerance);
                    for (int i = 1; i <= n; i++)
                    {
                        double t = (double)i / n, u = 1 - t;
                        points.Add(current * (u * u) + s.C1 * (2 * u * t) + s.End * (t * t));
                    }
                    break;
                }
                case SvgSegmentKind.Cubic:
                {
                    double dd = Math.Max((current - s.C1 * 2 + s.C2).Length, (s.C1 - s.C2 * 2 + s.End).Length);
                    int n = Steps(dd * 0.75, tolerance);
                    for (int i = 1; i <= n; i++)
                    {
                        double t = (double)i / n, u = 1 - t;
                        points.Add(current * (u * u * u) + s.C1 * (3 * u * u * t) + s.C2 * (3 * u * t * t) + s.End * (t * t * t));
                    }
                    break;
                }
            }
            current = s.End;
        }
        return points;
    }

    /// <summary>Twice the shoelace sum, halved: positive for clockwise on screen (y down).</summary>
    public static double SignedArea(IReadOnlyList<SvgPoint> polygon)
    {
        double sum = 0;
        for (int i = 0; i < polygon.Count; i++)
        {
            SvgPoint a = polygon[i], b = polygon[(i + 1) % polygon.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }
        return sum / 2;
    }

    private static int Steps(double bound, double tolerance) =>
        Math.Clamp((int)Math.Ceiling(Math.Sqrt(bound / Math.Max(tolerance, Epsilon))), 1, 512);

    private static List<SvgPoint> Distinct(List<SvgPoint> points, bool closed)
    {
        var result = new List<SvgPoint>(points.Count);
        foreach (SvgPoint p in points)
        {
            if (result.Count == 0 || (p - result[^1]).Length > Epsilon) result.Add(p);
        }
        if (closed && result.Count > 1 && (result[0] - result[^1]).Length <= Epsilon) result.RemoveAt(result.Count - 1);
        return result;
    }

    private static SvgPoint Unit(SvgPoint d) => d * (1 / d.Length);

    /// <summary>The unit normal to the left of <paramref name="d"/>, in y-up terms.</summary>
    private static SvgPoint Normal(SvgPoint d)
    {
        SvgPoint u = Unit(d);
        return new SvgPoint(-u.Y, u.X);
    }

    private static SvgPoint[]? Join(SvgPoint v, SvgPoint before, SvgPoint after, double h, SvgStrokeStyle style, double tolerance)
    {
        SvgPoint d1 = Unit(v - before), d2 = Unit(after - v);
        double cross = d1.X * d2.Y - d1.Y * d2.X;
        double dot = d1.X * d2.X + d1.Y * d2.Y;
        if (Math.Abs(cross) < 1e-12 && dot > 0) return null; // straight on: the rectangles already meet

        if (style.Join == SvgLineJoin.Round) return Circle(v, h, tolerance);
        if (Math.Abs(cross) < 1e-12) return null; // a full reversal: nothing to bevel

        double side = cross > 0 ? -1 : 1;
        SvgPoint n1 = Normal(d1), n2 = Normal(d2);
        SvgPoint o1 = v + n1 * (h * side), o2 = v + n2 * (h * side);
        if (style.Join == SvgLineJoin.Miter)
        {
            SvgPoint sum = n1 + n2;
            double ratio = 2 / sum.Length; // 1 / sin(θ/2), θ the angle between the segments
            if (ratio <= style.MiterLimit)
                return [v, o1, v + sum * (h * side * 2 / (sum.X * sum.X + sum.Y * sum.Y)), o2];
        }
        return [v, o1, o2];
    }

    private static SvgPoint[] Circle(SvgPoint c, double r, double tolerance)
    {
        double ratio = Math.Clamp(1 - tolerance / Math.Max(r, Epsilon), -1, 1);
        int n = Math.Clamp((int)Math.Ceiling(Math.PI / Math.Acos(ratio)), 8, 128);
        var points = new SvgPoint[n];
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n;
            points[i] = new SvgPoint(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
        }
        return points;
    }
}
```

- [ ] **Step 4: Run the tests again**

```bash
dotnet test tests/AcDream.Core.Tests/AcDream.Core.Tests.csproj --filter "FullyQualifiedName~SvgStrokerTests"
```

Expected: `Passed! - Failed: 0, Passed: 15, Skipped: 0, Total: 15`.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.Core/Plugins/SvgStroker.cs \
  tests/AcDream.Core.Tests/Plugins/SvgStrokerTests.cs
git commit -m "feat: expand SVG strokes into nonzero-safe polygons" -m "Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.Core/Plugins/SvgStroker.cs", "tests/AcDream.Core.Tests/Plugins/SvgStrokerTests.cs"], "verifyCommand": "cd .worktrees/svg-icons && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH && dotnet test tests/AcDream.Core.Tests/AcDream.Core.Tests.csproj --filter \"FullyQualifiedName~SvgStrokerTests\"", "modelTier": "standard", "acceptanceCriteria": ["`SvgStrokerTests` has 15 tests and all pass", "Every piece has positive signed area for every join \u00d7 cap, and after a reflecting transform", "A 90\u00b0 miter reaches the corner (11, \u22121) for a width-2 stroke; a 10\u00b0 spike falls back to a bevel under the default limit 4", "A non-uniform scale (1, 3) makes a width-2 horizontal stroke 6 units tall", "Flattening a radius-10 circle at tolerance 0.05 keeps every chord midpoint within [9.94, 10.03]"]}
```

---

### Task 4: Rasterizer (SvgIconRasterizer)

**Goal:** `SvgIconRasterizer.Bake(document, size)` fits the icon into a square (`xMidYMid meet`), rasterizes each paint layer separately with `StbTrueType.stbtt_Rasterize` (fills as quadratics/cubics, strokes as `SvgStroker` polygons, Int16 coordinates in 1/64 px), and composites coverage source-over by opacity. It returns null past 65,536 points.

**Files:**
- Create: `src/AcDream.App/UI/SvgIconRasterizer.cs`
- Test: `tests/AcDream.App.Tests/UI/SvgIconRasterizerTests.cs`
- Create (generated by the test, then reviewed): `tests/AcDream.App.Tests/UI/SvgIconRasterizerTests.x24.pgm`

**Acceptance Criteria:**
- [ ] `SvgIconRasterizerTests` has 10 tests and all pass
- [ ] A rect on pixel edges is exactly 255 inside and 0 outside; a half-pixel edge is 126–129
- [ ] An opposite-winding inner contour is a hole (0) while a stroke drawn across that hole is 255
- [ ] Two 50% layers composite to 190–192
- [ ] The golden `x24.pgm` is checked in, symmetric in both axes, and matches the picture in Step 4

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~SvgIconRasterizerTests"` → `Passed! - Failed: 0, Passed: 10`

**Steps:**

- [ ] **Step 1: Write the failing tests**

`tests/AcDream.App.Tests/UI/SvgIconRasterizerTests.cs`:

```csharp
using System.Runtime.CompilerServices;
using System.Text;
using AcDream.App.UI;
using AcDream.Core.Plugins;

namespace AcDream.App.Tests.UI;

public sealed class SvgIconRasterizerTests
{
    private const string GoldenFile = "SvgIconRasterizerTests.x24.pgm";

    private static SvgIconDocument Parse(string body, string viewBox = "0 0 16 16")
    {
        string svg = $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="{viewBox}">{body}</svg>""";
        Assert.True(PluginSvgIcon.TryParse(Encoding.UTF8.GetBytes(svg), out SvgIconDocument? doc, out string? reason), reason);
        return doc;
    }

    private static byte At(byte[] coverage, int size, int x, int y) => coverage[y * size + x];

    [Fact]
    public void AFilledSquareOnPixelEdgesIsSolidInsideAndEmptyOutside()
    {
        byte[] c = SvgIconRasterizer.Bake(Parse("""<rect x="4" y="4" width="8" height="8"/>"""), 16)!;
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            bool inside = x is >= 4 and < 12 && y is >= 4 and < 12;
            Assert.Equal(inside ? 255 : 0, At(c, 16, x, y));
        }
    }

    [Fact]
    public void AHalfPixelEdgeIsHalfCovered()
    {
        byte[] c = SvgIconRasterizer.Bake(Parse("""<rect x="4.5" y="4" width="8" height="8"/>"""), 16)!;
        Assert.InRange(At(c, 16, 4, 8), 126, 129);
        Assert.Equal(255, At(c, 16, 5, 8));
        Assert.InRange(At(c, 16, 12, 8), 126, 129);
    }

    [Fact]
    public void AnOppositeWindingInnerContourIsAHole()
    {
        byte[] c = SvgIconRasterizer.Bake(Parse("""<path d="M2 2H14V14H2Z M6 6V10H10V6Z"/>"""), 16)!;
        Assert.Equal(255, At(c, 16, 3, 8));
        Assert.Equal(0, At(c, 16, 8, 8));
    }

    [Fact]
    public void AStrokeAcrossAFillsHoleIsStillDrawn()
    {
        byte[] c = SvgIconRasterizer.Bake(Parse("""
            <path d="M2 2H14V14H2Z M6 6V10H10V6Z"/>
            <line x1="0" y1="8.5" x2="16" y2="8.5" stroke="black" stroke-width="1"/>
            """), 16)!;
        Assert.Equal(255, At(c, 16, 8, 8));
        Assert.Equal(0, At(c, 16, 8, 6));
    }

    [Fact]
    public void OpacityScalesCoverage()
    {
        byte[] c = SvgIconRasterizer.Bake(Parse("""<rect width="16" height="16" fill-opacity="0.5"/>"""), 16)!;
        Assert.InRange(At(c, 16, 8, 8), 127, 128);
    }

    [Fact]
    public void LayersCompositeSourceOver()
    {
        byte[] c = SvgIconRasterizer.Bake(Parse("""
            <rect width="16" height="16" fill-opacity="0.5"/><rect width="16" height="16" fill-opacity="0.5"/>
            """), 16)!;
        Assert.InRange(At(c, 16, 8, 8), 190, 192);
    }

    [Fact]
    public void ANonSquareViewBoxIsCentred()
    {
        byte[] c = SvgIconRasterizer.Bake(Parse("""<rect width="32" height="16"/>""", viewBox: "0 0 32 16"), 16)!;
        Assert.Equal(0, At(c, 16, 8, 3));
        Assert.Equal(255, At(c, 16, 8, 4));
        Assert.Equal(255, At(c, 16, 8, 11));
        Assert.Equal(0, At(c, 16, 8, 12));
    }

    [Fact]
    public void TheSameIconCoversTheSameShareAtEverySize()
    {
        SvgIconDocument doc = Parse("""<circle cx="8" cy="8" r="6" fill="none" stroke="black" stroke-width="1.5"/>""");
        double Share(int size) => SvgIconRasterizer.Bake(doc, size)!.Sum(b => b / 255.0) / (size * size);
        double reference = Share(48);
        Assert.InRange(Share(16), reference * 0.93, reference * 1.07);
        Assert.InRange(Share(24), reference * 0.95, reference * 1.05);
    }

    [Fact]
    public void TooManyPointsFailsTheBake()
    {
        SvgIconDocument doc = Parse("""<circle cx="8" cy="8" r="6" fill="none" stroke="black"/>""");
        var heavy = doc with { Layers = Enumerable.Repeat(doc.Layers[0], 2000).ToArray() };
        Assert.Null(SvgIconRasterizer.Bake(heavy, 128));
    }

    [Fact]
    public void AStrokedXWithRoundCapsMatchesItsGolden()
    {
        SvgIconDocument doc = Parse(
            """<path d="M6 6l12 12M18 6L6 18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>""",
            viewBox: "0 0 24 24");
        byte[] c = SvgIconRasterizer.Bake(doc, 24)!;
        CompareWithGolden(c, 24);
    }

    private static void CompareWithGolden(byte[] coverage, int size, [CallerFilePath] string source = "")
    {
        string path = Path.Combine(Path.GetDirectoryName(source)!, GoldenFile);
        var text = new StringBuilder($"P2\n{size} {size}\n255\n");
        for (int y = 0; y < size; y++)
            text.AppendLine(string.Join(' ', Enumerable.Range(0, size).Select(x => coverage[y * size + x].ToString("D3"))));
        bool update = Environment.GetEnvironmentVariable("ACDREAM_UPDATE_SVG_GOLDEN") == "1";
        if (!File.Exists(path) || update)
        {
            File.WriteAllText(path, text.ToString());
            Assert.True(update, $"There was no golden, so it was written to {path}; review it and run again.");
            return;
        }

        int[] expected = File.ReadAllText(path).Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Skip(4).Select(int.Parse).ToArray();
        Assert.Equal(size * size, expected.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.True(Math.Abs(expected[i] - coverage[i]) <= 2,
                $"pixel ({i % size},{i / size}) is {coverage[i]}, golden {expected[i]}; "
                + "if the change is meant, rewrite it with ACDREAM_UPDATE_SVG_GOLDEN=1");
        }
    }
}
```

- [ ] **Step 2: Run them to see them fail**

```bash
cd .worktrees/svg-icons && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~SvgIconRasterizerTests"
```

Expected: the build fails with `error CS0103: The name 'SvgIconRasterizer' does not exist in the current context`.

- [ ] **Step 3: Write `SvgIconRasterizer.cs`**

Facts the code relies on (checked 2026-10-02 against StbTrueTypeSharp 1.26.12): `stbtt_Rasterize` is public; the caller allocates `stbtt__bitmap.pixels`; vertex types are move 1, line 2, quadratic 3, cubic 4; coordinates are `short`; `stbtt_setvertex` does not set `cx1/cy1`, so cubic vertices are written field by field; `invert = 0` is y-down; coverage is |winding| clamped to 255 (nonzero), and contours close themselves.

`src/AcDream.App/UI/SvgIconRasterizer.cs`:

```csharp
using AcDream.Core.Plugins;
using StbTrueTypeSharp;

namespace AcDream.App.UI;

/// <summary>
/// Bakes a parsed SVG icon into a square single-channel coverage bitmap with
/// StbTrueType's outline rasterizer, the one the canvas fonts use. Each paint
/// layer is rasterized on its own and composited in document order, so a
/// fill's hole cannot cancel a stroke that crosses it. Coordinates go to stb
/// as Int16 in 1/64 device pixels.
/// </summary>
internal static class SvgIconRasterizer
{
    internal const int MaximumSize = 128;
    internal const int MaximumPoints = 65_536;
    private const float Flatness = 0.35f;
    private const double StrokeTolerance = 0.2;
    private const double SubPixels = 64;
    private const double Clamp = 500; // device px; ±500 × 64 stays inside Int16

    /// <summary>The icon fitted into <paramref name="size"/>² device pixels, centred
    /// (<c>xMidYMid meet</c>), or null when it needs more than
    /// <see cref="MaximumPoints"/> points.</summary>
    internal static byte[]? Bake(SvgIconDocument document, int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, MaximumSize);

        double scale = size / Math.Max(document.Width, document.Height);
        var fit = new SvgMatrix(scale, 0, 0, scale,
            (size - document.Width * scale) / 2 - document.MinX * scale,
            (size - document.Height * scale) / 2 - document.MinY * scale);

        var result = new byte[size * size];
        var layer = new byte[size * size];
        int budget = MaximumPoints;
        foreach (SvgPaintLayer paint in document.Layers)
        {
            List<Vertex> vertices = paint.Kind == SvgPaintKind.Fill
                ? FillVertices(paint, fit)
                : StrokeVertices(paint, fit);
            budget -= vertices.Count;
            if (budget < 0) return null;
            if (vertices.Count == 0) continue;

            Array.Clear(layer);
            Rasterize(vertices, layer, size);
            double opacity = Math.Clamp(paint.Opacity, 0, 1);
            for (int i = 0; i < result.Length; i++)
            {
                if (layer[i] == 0) continue;
                double c = result[i] / 255.0, a = layer[i] / 255.0 * opacity;
                result[i] = (byte)Math.Round((c + a * (1 - c)) * 255);
            }
        }
        return result;
    }

    /// <summary>One stb vertex before it is pinned: kind, end point and controls, in device pixels.</summary>
    private readonly record struct Vertex(byte Type, SvgPoint P, SvgPoint C, SvgPoint C1);

    private const byte VMove = 1, VLine = 2, VCurve = 3, VCubic = 4;

    private static List<Vertex> FillVertices(SvgPaintLayer paint, SvgMatrix fit)
    {
        SvgMatrix m = fit.Then(paint.Transform);
        var vertices = new List<Vertex>();
        foreach (SvgSubpath path in paint.Subpaths)
        {
            if (path.Segments.Count == 0) continue;
            vertices.Add(new(VMove, m.Apply(path.Start), default, default));
            foreach (SvgSegment s in path.Segments)
            {
                vertices.Add(s.Kind switch
                {
                    SvgSegmentKind.Line => new(VLine, m.Apply(s.End), default, default),
                    SvgSegmentKind.Quadratic => new(VCurve, m.Apply(s.End), m.Apply(s.C1), default),
                    _ => new(VCubic, m.Apply(s.End), m.Apply(s.C1), m.Apply(s.C2)),
                });
            }
        }
        return vertices;
    }

    private static List<Vertex> StrokeVertices(SvgPaintLayer paint, SvgMatrix fit)
    {
        var vertices = new List<Vertex>();
        foreach (SvgPoint[] polygon in SvgStroker.Expand(paint, fit, StrokeTolerance))
        {
            vertices.Add(new(VMove, polygon[0], default, default));
            for (int i = 1; i < polygon.Length; i++) vertices.Add(new(VLine, polygon[i], default, default));
        }
        return vertices;
    }

    private static short Fixed(double v) => (short)Math.Round(Math.Clamp(v, -Clamp, Clamp) * SubPixels);

    private static unsafe void Rasterize(List<Vertex> vertices, byte[] into, int size)
    {
        var pinned = new StbTrueType.stbtt_vertex[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
        {
            Vertex v = vertices[i];
            pinned[i].type = v.Type;
            pinned[i].x = Fixed(v.P.X);
            pinned[i].y = Fixed(v.P.Y);
            pinned[i].cx = Fixed(v.C.X);
            pinned[i].cy = Fixed(v.C.Y);
            pinned[i].cx1 = Fixed(v.C1.X);
            pinned[i].cy1 = Fixed(v.C1.Y);
        }

        fixed (StbTrueType.stbtt_vertex* vp = pinned)
        fixed (byte* pixels = into)
        {
            StbTrueType.stbtt__bitmap bitmap;
            bitmap.w = size;
            bitmap.h = size;
            bitmap.stride = size;
            bitmap.pixels = pixels;
            StbTrueType.stbtt_Rasterize(&bitmap, Flatness, vp, pinned.Length,
                (float)(1 / SubPixels), (float)(1 / SubPixels), 0, 0, 0, 0, 0, null, false);
        }
    }
}
```

- [ ] **Step 4: Record and review the golden**

```bash
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~SvgIconRasterizerTests"
```

Expected on the first run: 9 pass and `AStrokedXWithRoundCapsMatchesItsGolden` fails with `There was no golden, so it was written to …/SvgIconRasterizerTests.x24.pgm; review it and run again.`

Look at it as ASCII (`#` ≥ 230, `+` ≥ 100, `.` > 0):

```bash
python3 -c "
v=[int(x) for x in open('tests/AcDream.App.Tests/UI/SvgIconRasterizerTests.x24.pgm').read().split()[4:]]
for y in range(24): print(''.join(' ' if v[y*24+x]==0 else '#' if v[y*24+x]>=230 else '+' if v[y*24+x]>=100 else '.' for x in range(24)))
print('mirror', all(v[y*24+x]==v[y*24+23-x] for y in range(24) for x in range(24)))
"
```

It must show rows 5–18 as below (rows 0–4 and 19–23 empty) and print `mirror True`:

```text
     +#.        .#+
     ##+.      .+##
     .+#+.    .+#+.
      .+#+.  .+#+.
       .+#+..+#+.
        .+####+.
         .####.
         .####.
        .+####+.
       .+#+..+#+.
      .+#+.  .+#+.
     .+#+.    .+#+.
     ##+.      .+##
     +#.        .#+
```

Then run again: `Passed! - Failed: 0, Passed: 10`.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/SvgIconRasterizer.cs \
  tests/AcDream.App.Tests/UI/SvgIconRasterizerTests.cs \
  tests/AcDream.App.Tests/UI/SvgIconRasterizerTests.x24.pgm
git commit -m "feat: bake SVG icons to coverage with the stb rasterizer" -m "Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/UI/SvgIconRasterizer.cs", "tests/AcDream.App.Tests/UI/SvgIconRasterizerTests.cs", "tests/AcDream.App.Tests/UI/SvgIconRasterizerTests.x24.pgm"], "verifyCommand": "cd .worktrees/svg-icons && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH && dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter \"FullyQualifiedName~SvgIconRasterizerTests\"", "modelTier": "standard", "acceptanceCriteria": ["`SvgIconRasterizerTests` has 10 tests and all pass", "A rect on pixel edges is exactly 255 inside and 0 outside; a half-pixel edge is 126\u2013129", "An opposite-winding inner contour is a hole (0) while a stroke drawn across that hole is 255", "Two 50% layers composite to 190\u2013192", "The golden `x24.pgm` is checked in, symmetric in both axes, and matches the picture in Step 4"]}
```

---

### Task 5: Icon cache (PluginSvgIconCache)

**Goal:** `PluginSvgIconCache` parses each file once (keyed by resolved path, length and write time), hands out reference-counted `PluginSvgIconEntry` holds, bakes lazily per device size through the existing `IPluginFontBackend.UploadCoverage` seam, keeps at most two sizes per icon, gives textures back on the last release or on dispose, and logs each unusable file once.

**Files:**
- Create: `src/AcDream.App/UI/PluginSvgIconCache.cs`
- Test: `tests/AcDream.App.Tests/UI/PluginSvgIconCacheTests.cs`

**Acceptance Criteria:**
- [ ] `PluginSvgIconCacheTests` has 13 tests and all pass
- [ ] One bake per size; a third size releases the least recently drawn
- [ ] The last holder's `Release` gives back every texture and removes the entry
- [ ] Rewriting the file with a new length or time gives a new entry
- [ ] A failed upload marks the entry failed, is never retried, and logs `could not be uploaded` once
- [ ] A rejected file logs exactly `[UI] plugin icon '<label>' ignored: <reason>` once
- [ ] `DevicePixels(24, 1.5)` = 36, `(22, 1.25)` = 28, `(100, 4)` = 128 (the cap)

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~PluginSvgIconCacheTests"` → `Passed! - Failed: 0, Passed: 13`

**Steps:**

- [ ] **Step 1: Write the failing tests**

`tests/AcDream.App.Tests/UI/PluginSvgIconCacheTests.cs`:

```csharp
using AcDream.App.Plugins;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public sealed class PluginSvgIconCacheTests : IDisposable
{
    private const string Icon = """<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="8"/></svg>""";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"acdream-svg-cache-{Guid.NewGuid():N}");
    private readonly Backend _backend = new();
    private readonly List<string> _reports = [];

    public PluginSvgIconCacheTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class Backend : IPluginFontBackend
    {
        private uint _next = 700;
        public bool Refuse { get; set; }
        public List<int> UploadedSizes { get; } = [];
        public List<uint> Released { get; } = [];

        public uint UploadCoverage(byte[] coverage, int width, int height, string debugName)
        {
            if (Refuse) return 0;
            UploadedSizes.Add(width);
            return _next++;
        }

        public bool ReleaseCoverage(uint texture)
        {
            Released.Add(texture);
            return true;
        }
    }

    private PluginSvgIconCache Cache() => new(_backend, _reports.Add);

    private string Write(string name, string content = Icon)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void EachSizeIsBakedOnce()
    {
        using PluginSvgIconCache cache = Cache();
        PluginSvgIconCache.PluginSvgIconEntry entry = cache.Acquire("p/icon.svg", Write("icon.svg"))!;
        uint first = entry.TextureFor(48);
        Assert.Equal(first, entry.TextureFor(48));
        Assert.Equal([48], _backend.UploadedSizes);
    }

    [Fact]
    public void TwoButtonsShowingOneFileShareOneEntry()
    {
        using PluginSvgIconCache cache = Cache();
        string path = Write("icon.svg");
        Assert.Same(cache.Acquire("p/icon.svg", path), cache.Acquire("p/icon.svg", path));
        Assert.Equal(1, cache.EntryCount);
    }

    [Fact]
    public void AThirdSizeReleasesTheLeastRecentlyDrawn()
    {
        using PluginSvgIconCache cache = Cache();
        PluginSvgIconCache.PluginSvgIconEntry entry = cache.Acquire("p/icon.svg", Write("icon.svg"))!;
        uint at24 = entry.TextureFor(24);
        uint at48 = entry.TextureFor(48);
        entry.TextureFor(24);
        entry.TextureFor(36);
        Assert.Equal([at48], _backend.Released);
        Assert.Equal(2, entry.BakedSizes);
        Assert.Equal(at24, entry.TextureFor(24));
    }

    [Fact]
    public void TheLastHolderGivesEveryTextureBack()
    {
        using PluginSvgIconCache cache = Cache();
        string path = Write("icon.svg");
        PluginSvgIconCache.PluginSvgIconEntry entry = cache.Acquire("p/icon.svg", path)!;
        cache.Acquire("p/icon.svg", path);
        uint a = entry.TextureFor(24), b = entry.TextureFor(48);

        entry.Release();
        Assert.Empty(_backend.Released);
        entry.Release();
        Assert.Equal([a, b], _backend.Released.Order());
        Assert.Equal(0, cache.EntryCount);
    }

    [Fact]
    public void AChangedFileLoadsAgain()
    {
        using PluginSvgIconCache cache = Cache();
        string path = Write("icon.svg");
        PluginSvgIconCache.PluginSvgIconEntry before = cache.Acquire("p/icon.svg", path)!;
        File.WriteAllText(path, Icon.Replace("r=\"8\"", "r=\"10\"", StringComparison.Ordinal));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        Assert.NotSame(before, cache.Acquire("p/icon.svg", path));
    }

    [Fact]
    public void AFailedUploadFallsBackAndIsNotRetried()
    {
        using PluginSvgIconCache cache = Cache();
        PluginSvgIconCache.PluginSvgIconEntry entry = cache.Acquire("p/icon.svg", Write("icon.svg"))!;
        _backend.Refuse = true;
        Assert.Equal(0u, entry.TextureFor(24));
        _backend.Refuse = false;
        Assert.Equal(0u, entry.TextureFor(24));
        Assert.True(entry.Failed);
        Assert.Empty(_backend.UploadedSizes);
        Assert.Single(_reports, r => r.Contains("could not be uploaded", StringComparison.Ordinal));
    }

    [Fact]
    public void ARejectedFileIsLoggedOnceAndGivesNull()
    {
        using PluginSvgIconCache cache = Cache();
        string path = Write("icon.svg", """<svg viewBox="0 0 1 1"><mask/></svg>""");
        Assert.Null(cache.Acquire("acdream.mosstank/icon.svg", path));
        Assert.Null(cache.Acquire("acdream.mosstank/icon.svg", path));
        string report = Assert.Single(_reports);
        Assert.Equal("[UI] plugin icon 'acdream.mosstank/icon.svg' ignored: uses <mask>, which plugin icons do not support", report);
    }

    [Theory]
    [InlineData(24f, 1f, 24)]
    [InlineData(24f, 1.5f, 36)]
    [InlineData(24f, 2f, 48)]
    [InlineData(22f, 1.25f, 28)]
    [InlineData(100f, 4f, 128)]
    public void DevicePixelsRoundUpAndCap(float extent, float scale, int expected)
    {
        Assert.Equal(expected, PluginSvgIconCache.DevicePixels(extent, scale));
    }

    [Fact]
    public void DisposeReleasesEverything()
    {
        PluginSvgIconCache cache = Cache();
        PluginSvgIconCache.PluginSvgIconEntry entry = cache.Acquire("p/icon.svg", Write("icon.svg"))!;
        uint t = entry.TextureFor(24);
        cache.Dispose();
        Assert.Equal([t], _backend.Released);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

```bash
cd .worktrees/svg-icons && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~PluginSvgIconCacheTests"
```

Expected: the build fails with `error CS0246: The type or namespace name 'PluginSvgIconCache' could not be found`.

- [ ] **Step 3: Write `PluginSvgIconCache.cs`**

`src/AcDream.App/UI/PluginSvgIconCache.cs`:

```csharp
using AcDream.App.Plugins;
using AcDream.Core.Plugins;

namespace AcDream.App.UI;

/// <summary>
/// The interface's SVG plugin icons: each file parsed once, baked lazily per
/// device size on first draw, shared by every dock button that shows it and
/// given back when the last one goes. Keyed by resolved path, length and
/// write time, so a hot reload that changes the file loads it again. Used
/// from the interface thread only.
/// </summary>
internal sealed class PluginSvgIconCache : IDisposable
{
    internal readonly record struct Key(string Path, long Length, DateTime WrittenUtc);

    private readonly IPluginFontBackend _backend;
    private readonly Action<string> _report;
    private readonly Func<SvgIconDocument, int, byte[]?> _bake;
    private readonly Dictionary<Key, PluginSvgIconEntry> _entries = [];
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private long _clock;

    internal PluginSvgIconCache(
        IPluginFontBackend backend,
        Action<string>? report = null,
        Func<SvgIconDocument, int, byte[]?>? bake = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _report = report ?? Console.WriteLine;
        _bake = bake ?? SvgIconRasterizer.Bake;
    }

    /// <summary>Device pixels for an icon <paramref name="extentPoints"/> wide at
    /// <paramref name="pixelScale"/>, capped at the largest bake.</summary>
    internal static int DevicePixels(float extentPoints, float pixelScale) =>
        Math.Clamp((int)MathF.Ceiling(extentPoints * pixelScale - 0.001f), 1, SvgIconRasterizer.MaximumSize);

    internal int EntryCount => _entries.Count;

    /// <summary>
    /// The icon at <paramref name="fullPath"/>, with one more holder, or null when it
    /// cannot be used; the reason is logged once under <paramref name="label"/>
    /// (<c>pluginId/relative/path.svg</c>).
    /// </summary>
    internal PluginSvgIconEntry? Acquire(string label, string fullPath)
    {
        Key key;
        try
        {
            var info = new FileInfo(fullPath);
            key = new Key(info.FullName, info.Length, info.LastWriteTimeUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Reject(label, fullPath, "could not be read: " + ex.Message);
            return null;
        }

        if (!_entries.TryGetValue(key, out PluginSvgIconEntry? entry))
        {
            if (!PluginSvgIcon.TryLoad(fullPath, out SvgIconDocument? document, out string? reason))
            {
                Reject(label, fullPath, reason);
                return null;
            }
            entry = new PluginSvgIconEntry(this, key, label, document);
            _entries.Add(key, entry);
        }
        entry.Holders++;
        return entry;
    }

    /// <summary>Logs, once per file, why an icon was not used.</summary>
    internal void Reject(string label, string path, string reason)
    {
        if (_reported.Add(path))
            _report($"[UI] plugin icon '{label}' ignored: {reason}");
    }

    public void Dispose()
    {
        foreach (PluginSvgIconEntry entry in _entries.Values) entry.ReleaseTextures();
        _entries.Clear();
    }

    internal sealed class PluginSvgIconEntry
    {
        private const int MaximumSizes = 2;

        private readonly PluginSvgIconCache _owner;
        private readonly Key _key;
        private readonly string _label;
        private readonly SvgIconDocument _document;
        private readonly Dictionary<int, (uint Texture, long Used)> _textures = [];

        internal PluginSvgIconEntry(PluginSvgIconCache owner, Key key, string label, SvgIconDocument document)
        {
            _owner = owner;
            _key = key;
            _label = label;
            _document = document;
        }

        internal int Holders { get; set; }

        /// <summary>Set once a bake or upload failed; the icon is never tried again.</summary>
        internal bool Failed { get; private set; }

        internal int BakedSizes => _textures.Count;

        /// <summary>The coverage texture for a <paramref name="devicePixels"/>² bake, baking it on
        /// first use; 0 once the icon has failed.</summary>
        internal uint TextureFor(int devicePixels)
        {
            if (Failed) return 0;
            long now = ++_owner._clock;
            if (_textures.TryGetValue(devicePixels, out (uint Texture, long Used) baked))
            {
                _textures[devicePixels] = (baked.Texture, now);
                return baked.Texture;
            }

            byte[]? coverage = _owner._bake(_document, devicePixels);
            uint texture = coverage is null
                ? 0
                : _owner._backend.UploadCoverage(coverage, devicePixels, devicePixels, "plugin icon " + _label);
            if (texture == 0)
            {
                Failed = true;
                _owner._report($"[UI] plugin icon '{_label}' ignored: "
                    + (coverage is null ? "needs too many points to draw" : "could not be uploaded"));
                ReleaseTextures();
                return 0;
            }

            if (_textures.Count >= MaximumSizes)
            {
                int oldest = _textures.MinBy(pair => pair.Value.Used).Key;
                _owner._backend.ReleaseCoverage(_textures[oldest].Texture);
                _textures.Remove(oldest);
            }
            _textures[devicePixels] = (texture, now);
            return texture;
        }

        /// <summary>Drops one holder; the last one gives every texture back.</summary>
        internal void Release()
        {
            if (Holders <= 0) return;
            if (--Holders > 0) return;
            ReleaseTextures();
            _owner._entries.Remove(_key);
        }

        internal void ReleaseTextures()
        {
            foreach ((uint texture, _) in _textures.Values) _owner._backend.ReleaseCoverage(texture);
            _textures.Clear();
        }
    }
}
```

- [ ] **Step 4: Run the tests again**

```bash
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~PluginSvgIconCacheTests"
```

Expected: `Passed! - Failed: 0, Passed: 13, Skipped: 0, Total: 13`.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/PluginSvgIconCache.cs \
  tests/AcDream.App.Tests/UI/PluginSvgIconCacheTests.cs
git commit -m "feat: cache plugin SVG icons per file and device size" -m "Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/UI/PluginSvgIconCache.cs", "tests/AcDream.App.Tests/UI/PluginSvgIconCacheTests.cs"], "verifyCommand": "cd .worktrees/svg-icons && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH && dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter \"FullyQualifiedName~PluginSvgIconCacheTests\"", "modelTier": "standard", "acceptanceCriteria": ["`PluginSvgIconCacheTests` has 13 tests and all pass", "One bake per size; a third size releases the least recently drawn", "The last holder's `Release` gives back every texture and removes the entry", "Rewriting the file with a new length or time gives a new entry", "A failed upload marks the entry failed, is never retried, and logs `could not be uploaded` once", "A rejected file logs exactly `[UI] plugin icon '<label>' ignored: <reason>` once", "`DevicePixels(24, 1.5)` = 36, `(22, 1.25)` = 28, `(100, 4)` = 128 (the cap)"]}
```

---

### Task 6: IconFile, the DrawIcon seam and runtime wiring

**Goal:** `PluginPanelDescriptor.IconFile` exists; the dock button draws every icon through `PluginShelfButton.DrawIcon(ctx, x, y, extent, colour)`, with an SVG first (tinted, snapped to the device grid through the new `UiRenderContext.DrawCoverageIcon`) and the PNG/DAT path unchanged after it; `RetailUiRuntime` resolves `IconFile` then `icon.svg` per window and disposes the cache after the dock.

**Files:**
- Modify: `src/AcDream.Plugin.Abstractions/IUiRegistry.cs` (new `IconFile` after `IconSurfaceId`)
- Modify: `src/AcDream.App/UI/UiRenderContext.cs` (new `DrawCoverageIcon` before `DrawCoverageSpriteAbsolute`)
- Modify: `src/AcDream.App/UI/PluginSidePanel.cs` (internal `Add` overload; `PluginShelfButton`: `_svgIcon`, `DrawIcon`, `IconColor`, release in `DisposeSubscriptions`)
- Modify: `src/AcDream.App/UI/RetailUiRuntime.cs` (`_pluginSvgIcons`, `ResolvePluginSvgIcon`, `AcquirePluginSvgIcon`, the `Add` call in the plugin mount, `Dispose`)
- Test: `tests/AcDream.App.Tests/UI/PluginShelfSvgIconTests.cs` (a new file, so the dock branch's rewrite of `PluginSidePanelTests` does not collide with it)

**Acceptance Criteria:**
- [ ] `PluginShelfSvgIconTests` has 11 tests and all pass
- [ ] With an SVG, a PNG and a DAT id all present, only the SVG is drawn and the DAT resolver is never called
- [ ] At pixel scale 2 a Moss tile's 20-point icon box bakes at 40 device pixels
- [ ] Moss tints `Text` when closed and `Accent` when open; Classic tints white when closed and (0.76, 0.64, 0.25) when open
- [ ] A failed upload falls back to `icon.png` if there is one, else the initials, and gives its hold back
- [ ] Unregistering the window, disposing the dock, and a duplicate `Add` each give the hold back
- [ ] The existing `PluginSidePanel*` and `PluginShelfIcon*` tests have the same results as on `main` (DAT-dependent tests fail only when the DATs are absent)
- [ ] `dotnet build AcDream.slnx` succeeds with 0 warnings

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~PluginShelfSvgIconTests"` → `Passed! - Failed: 0, Passed: 11`

**Steps:**

- [ ] **Step 1: Write the failing tests**

`tests/AcDream.App.Tests/UI/PluginShelfSvgIconTests.cs`:

```csharp
using System.Numerics;
using AcDream.App.Plugins;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.UI;

/// <summary>A dock button with an SVG icon: it wins over every other icon, is tinted by
/// theme and state, falls back when it cannot be drawn, and is given back with its window.</summary>
public sealed class PluginShelfSvgIconTests : IDisposable
{
    private const string Icon = """<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="8"/></svg>""";

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"acdream-shelf-svg-{Guid.NewGuid():N}.svg");
    private readonly Backend _backend = new();
    private readonly PluginSvgIconCache _cache;

    public PluginShelfSvgIconTests()
    {
        File.WriteAllText(_path, Icon);
        _cache = new PluginSvgIconCache(_backend, _ => { });
    }

    public void Dispose()
    {
        _cache.Dispose();
        File.Delete(_path);
    }

    private sealed class Backend : IPluginFontBackend
    {
        private uint _next = 500;
        public bool Refuse { get; set; }
        public List<(uint Texture, int Size)> Uploaded { get; } = [];

        public uint UploadCoverage(byte[] coverage, int width, int height, string debugName)
        {
            if (Refuse) return 0;
            uint texture = _next++;
            Uploaded.Add((texture, width));
            return texture;
        }

        public bool ReleaseCoverage(uint texture) => true;
    }

    private sealed class NullFrames : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame => null;
    }

    private sealed record Rig(
        UiRoot Root, PluginSidePanel Shelf, RetailWindowHandle Handle, UiPanel Frame,
        PluginSidePanel.PluginShelfButton Button);

    private Rig Build(PluginUiThemeSettings? themes = null, bool visible = false, (uint, int, int)? fileIcon = null,
        uint surface = 0)
    {
        var root = new UiRoot { Width = 800f, Height = 600f };
        var shelf = new PluginSidePanel(
            root.WindowManager,
            _ => throw new InvalidOperationException("the DAT resolver must not run"),
            font: null,
            themes);
        root.AddChild(shelf);
        var frame = new UiPanel { Width = 200f, Height = 100f, Visible = visible };
        root.AddChild(frame);
        RetailWindowHandle handle = root.WindowManager.Register("plugin:acdream.test:main", frame);
        shelf.Add(
            new PluginUiOwner("acdream.test", "Test Plugin"),
            new PluginPanelDescriptor("main", "Test Plugin") { IconText = "TP", IconSurfaceId = surface },
            handle,
            fileIcon,
            _cache.Acquire("acdream.test/icon.svg", _path));
        return new Rig(root, shelf, handle, frame,
            Assert.Single(shelf.Children.OfType<PluginSidePanel.PluginShelfButton>()));
    }

    private static (TextRenderer Renderer, UiRenderContext Context) Context(float pixelScale = 1f)
    {
        var renderer = new TextRenderer(new RecordingGpuDevice(), new NullFrames(), "unused");
        renderer.Begin(new Vector2(200f, 200f));
        var context = new UiRenderContext(renderer, new Vector2(200f, 200f));
        context.Begin(new Vector2(200f, 200f), null, pixelScale);
        return (renderer, context);
    }

    private static Vector4 CoverageColour(TextRenderer renderer, uint texture)
    {
        int index = renderer.DebugSpriteSegmentCoverage.ToList().IndexOf(texture);
        Assert.True(index >= 0, $"no coverage sprite drew texture {texture}");
        IReadOnlyList<float> v = renderer.DebugSpriteSegmentVerts[index].Verts;
        return new Vector4(v[4], v[5], v[6], v[7]);
    }

    [Fact]
    public void AnSvgWinsOverTheFileIconAndTheDatSurface()
    {
        Rig rig = Build(fileIcon: (7u, 64, 64), surface: 0x165u);
        Assert.Equal(string.Empty, rig.Button.Text);
        (TextRenderer renderer, UiRenderContext context) = Context();

        rig.Button.DrawSelfAndChildren(context);

        (uint texture, _) = Assert.Single(_backend.Uploaded);
        Assert.Contains(texture, renderer.DebugSpriteSegmentCoverage);
        Assert.DoesNotContain(renderer.DebugSpriteSegmentVerts, seg => seg.Texture == 7u);
    }

    [Fact]
    public void AtTwiceTheScaleTheBakeIsInDevicePixels()
    {
        Rig rig = Build(new PluginUiThemeSettings { Theme = PluginUiTheme.Moss });
        rig.Button.DrawSelfAndChildren(Context(pixelScale: 2f).Context);
        // A Moss tile's icon box is 22 - 2 = 20 points: 40 device pixels at 2x.
        Assert.Equal(40, Assert.Single(_backend.Uploaded).Size);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MossTintsTextWhenClosedAndAccentWhenOpen(bool open)
    {
        var themes = new PluginUiThemeSettings { Theme = PluginUiTheme.Moss };
        Rig rig = Build(themes, visible: open);
        (TextRenderer renderer, UiRenderContext context) = Context();
        rig.Button.DrawSelfAndChildren(context);
        Vector4 expected = open ? PluginUiPalette.Moss.Accent : PluginUiPalette.Moss.Text;
        Assert.Equal(expected, CoverageColour(renderer, _backend.Uploaded[0].Texture));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClassicTintsWhiteWhenClosedAndGoldWhenOpen(bool open)
    {
        Rig rig = Build(visible: open);
        (TextRenderer renderer, UiRenderContext context) = Context();
        rig.Button.DrawSelfAndChildren(context);
        Vector4 expected = open ? new Vector4(0.76f, 0.64f, 0.25f, 1f) : Vector4.One;
        Assert.Equal(expected, CoverageColour(renderer, _backend.Uploaded[0].Texture));
    }

    [Fact]
    public void AnIconThatCannotBeUploadedFallsBackToTheInitialsAndIsGivenBack()
    {
        _backend.Refuse = true;
        Rig rig = Build();
        rig.Button.DrawSelfAndChildren(Context().Context);
        Assert.Equal("TP", rig.Button.Text);
        Assert.Equal(0, _cache.EntryCount);
    }

    [Fact]
    public void AnIconThatCannotBeUploadedFallsBackToTheFileIcon()
    {
        _backend.Refuse = true;
        Rig rig = Build(fileIcon: (7u, 64, 64));
        (TextRenderer renderer, UiRenderContext context) = Context();
        rig.Button.DrawSelfAndChildren(context);
        Assert.Equal(string.Empty, rig.Button.Text);
        Assert.Contains(renderer.DebugSpriteSegmentVerts, seg => seg.Texture == 7u);
    }

    [Fact]
    public void UnregisteringTheWindowGivesTheIconBack()
    {
        Rig rig = Build();
        Assert.Equal(1, _cache.EntryCount);
        rig.Root.WindowManager.Unregister("plugin:acdream.test:main");
        Assert.Equal(0, _cache.EntryCount);
        rig.Shelf.Dispose();
    }

    [Fact]
    public void DisposingTheDockGivesTheIconBack()
    {
        Rig rig = Build();
        rig.Shelf.Dispose();
        Assert.Equal(0, _cache.EntryCount);
    }

    [Fact]
    public void AddingTheSameWindowTwiceGivesTheSecondHoldBack()
    {
        Rig rig = Build();
        PluginSvgIconCache.PluginSvgIconEntry entry = _cache.Acquire("acdream.test/icon.svg", _path)!;
        Assert.Equal(2, entry.Holders);
        rig.Shelf.Add(
            new PluginUiOwner("acdream.test", "Test Plugin"),
            new PluginPanelDescriptor("main", "Test Plugin"),
            rig.Handle,
            null,
            entry);
        Assert.Equal(1, entry.Holders);
        rig.Shelf.Dispose();
    }
}
```

- [ ] **Step 2: Run them to see them fail**

```bash
cd .worktrees/svg-icons && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~PluginShelfSvgIconTests"
```

Expected: the build fails with `error CS1501: No overload for method 'Add' takes 5 arguments`.

- [ ] **Step 3: Add `IconFile` to the descriptor**

```diff
diff --git a/src/AcDream.Plugin.Abstractions/IUiRegistry.cs b/src/AcDream.Plugin.Abstractions/IUiRegistry.cs
index 4ef48837..9d409db6 100644
--- a/src/AcDream.Plugin.Abstractions/IUiRegistry.cs
+++ b/src/AcDream.Plugin.Abstractions/IUiRegistry.cs
@@ -24,6 +24,15 @@ public sealed record PluginPanelDescriptor(string WindowId, string Title)
     /// </summary>
     public uint IconSurfaceId { get; init; }
 
+    /// <summary>
+    /// An SVG icon for this window's dock button: a path relative to the
+    /// plugin's install folder, such as <c>"icons/loot.svg"</c>. Null uses the
+    /// plugin's own <c>icon.svg</c>, if it ships one. A file that is missing or
+    /// outside the supported subset falls back to the next icon. The icon is
+    /// drawn in one colour that follows the plugin theme.
+    /// </summary>
+    public string? IconFile { get; init; }
+
     /// <summary>Whether the window is open as soon as it is registered.</summary>
     public bool StartVisible { get; init; } = true;
 
```

- [ ] **Step 4: Add `DrawCoverageIcon` to the render context**

```diff
diff --git a/src/AcDream.App/UI/UiRenderContext.cs b/src/AcDream.App/UI/UiRenderContext.cs
index 4d291910..54af9859 100644
--- a/src/AcDream.App/UI/UiRenderContext.cs
+++ b/src/AcDream.App/UI/UiRenderContext.cs
@@ -214,6 +214,19 @@ public sealed class UiRenderContext
         float u0, float v0, float u1, float v1, Vector4 color) =>
         DrawCoverageSpriteAbsolute(coverageTexture, x + _current.X, y + _current.Y, w, h, u0, v0, u1, v1, color);
 
+    /// <summary>
+    /// Draws a coverage icon baked at <paramref name="devicePixels"/>² device
+    /// pixels with its top-left corner at (<paramref name="x"/>, <paramref name="y"/>),
+    /// snapped to the device grid so each texel lands on one device pixel.
+    /// </summary>
+    internal void DrawCoverageIcon(uint texture, float x, float y, int devicePixels, Vector4 color)
+    {
+        float extent = devicePixels / PixelScale;
+        float ax = MathF.Round((_current.X + x) * PixelScale) / PixelScale;
+        float ay = MathF.Round((_current.Y + y) * PixelScale) / PixelScale;
+        DrawCoverageSpriteAbsolute(texture, ax, ay, extent, extent, 0f, 0f, 1f, 1f, color);
+    }
+
     private void DrawCoverageSpriteAbsolute(uint coverageTexture, float x, float y, float w, float h,
         float u0, float v0, float u1, float v1, Vector4 color)
     {
```

- [ ] **Step 5: Give the dock button its SVG and the `DrawIcon` seam**

Keep the change this small: the dock redesign rewrites this file's layout and calls `DrawIcon` with its own state colours, so everything outside `DrawIcon`/`IconColor` should stay as it is.

```diff
diff --git a/src/AcDream.App/UI/PluginSidePanel.cs b/src/AcDream.App/UI/PluginSidePanel.cs
index 0a0ef986..02a13369 100644
--- a/src/AcDream.App/UI/PluginSidePanel.cs
+++ b/src/AcDream.App/UI/PluginSidePanel.cs
@@ -119,14 +119,29 @@ public sealed class PluginSidePanel : UiPanel, IDisposable, IRetainedWindowState
         PluginUiOwner owner,
         PluginPanelDescriptor descriptor,
         RetailWindowHandle handle,
-        (uint Texture, int Width, int Height)? fileIcon = null)
+        (uint Texture, int Width, int Height)? fileIcon = null) =>
+        Add(owner, descriptor, handle, fileIcon, svgIcon: null);
+
+    /// <summary>
+    /// The same, with an SVG icon. The caller's hold on <paramref name="svgIcon"/>
+    /// passes to the dock, which gives it back when the window goes.
+    /// </summary>
+    internal void Add(
+        PluginUiOwner owner,
+        PluginPanelDescriptor descriptor,
+        RetailWindowHandle handle,
+        (uint Texture, int Width, int Height)? fileIcon,
+        PluginSvgIconCache.PluginSvgIconEntry? svgIcon)
     {
         ObjectDisposedException.ThrowIf(_disposed, this);
         ArgumentException.ThrowIfNullOrWhiteSpace(owner.Id);
         ArgumentNullException.ThrowIfNull(descriptor);
         ArgumentNullException.ThrowIfNull(handle);
         if (_entries.ContainsKey(handle))
+        {
+            svgIcon?.Release();
             return;
+        }
 
         handle.OuterFrame.ConstrainResizeToParent = true;
         KeepWindowReachable(handle);
@@ -138,7 +153,8 @@ public sealed class PluginSidePanel : UiPanel, IDisposable, IRetainedWindowState
             _resolve,
             _font,
             fileIcon,
-            _themes)
+            _themes,
+            svgIcon)
         {
             Width = ButtonExtent,
             Height = ButtonExtent,
@@ -535,6 +551,7 @@ public sealed class PluginSidePanel : UiPanel, IDisposable, IRetainedWindowState
 
         private bool _iconResolveAttempted;
         private bool _iconAvailable;
+        private PluginSvgIconCache.PluginSvgIconEntry? _svgIcon;
 
         internal PluginShelfButton(
             PluginPanelDescriptor descriptor,
@@ -543,9 +560,11 @@ public sealed class PluginSidePanel : UiPanel, IDisposable, IRetainedWindowState
             Func<uint, (uint tex, int width, int height)> resolve,
             UiDatFont? font,
             (uint Texture, int Width, int Height)? fileIcon = null,
-            PluginUiThemeSettings? themes = null)
+            PluginUiThemeSettings? themes = null,
+            PluginSvgIconCache.PluginSvgIconEntry? svgIcon = null)
         {
             _themes = themes;
+            _svgIcon = svgIcon;
             _handle = handle;
             _resolve = resolve;
             _fileIcon = fileIcon;
@@ -555,7 +574,7 @@ public sealed class PluginSidePanel : UiPanel, IDisposable, IRetainedWindowState
                 ? descriptor.Title
                 : $"{ownerDisplayName} — {descriptor.Title}";
             _initialsFallback = Initials(descriptor.IconText, descriptor.Title);
-            Text = _fileIcon is null && _iconSurfaceId == 0 ? _initialsFallback : string.Empty;
+            Text = _svgIcon is null && _fileIcon is null && _iconSurfaceId == 0 ? _initialsFallback : string.Empty;
             DatFont = font;
             Outline = true;
             BorderThickness = 1f;
@@ -586,6 +605,36 @@ public sealed class PluginSidePanel : UiPanel, IDisposable, IRetainedWindowState
 
             base.OnDraw(ctx);
 
+            float inset = (_themes?.Theme ?? PluginUiTheme.Classic) == PluginUiTheme.Classic ? 6f : 2f;
+            float extent = MathF.Min(Width - inset, Height - inset);
+            DrawIcon(ctx, (Width - extent) * 0.5f, (Height - extent) * 0.5f, extent, IconColor());
+        }
+
+        /// <summary>
+        /// Draws this window's icon in the square at (<paramref name="x"/>, <paramref name="y"/>),
+        /// <paramref name="extent"/> points wide: its SVG tinted <paramref name="colour"/>, else its
+        /// <c>icon.png</c> or DAT surface (full colour, <paramref name="colour"/>'s alpha). False
+        /// when it has none, so the caller draws the initials.
+        /// </summary>
+        internal bool DrawIcon(UiRenderContext ctx, float x, float y, float extent, Vector4 colour)
+        {
+            if (_svgIcon is { } svg)
+            {
+                int pixels = PluginSvgIconCache.DevicePixels(extent, ctx.PixelScale);
+                uint coverage = svg.TextureFor(pixels);
+                if (coverage != 0)
+                {
+                    float drawn = pixels / ctx.PixelScale;
+                    float offset = (extent - drawn) * 0.5f;
+                    ctx.DrawCoverageIcon(coverage, x + offset, y + offset, pixels, colour);
+                    return true;
+                }
+                svg.Release();
+                _svgIcon = null;
+                if (_fileIcon is null && _iconSurfaceId == 0)
+                    Text = _initialsFallback;
+            }
+
             uint texture;
             int width;
             int height;
@@ -594,29 +643,26 @@ public sealed class PluginSidePanel : UiPanel, IDisposable, IRetainedWindowState
             else if (_iconSurfaceId != 0 && _iconAvailable)
                 (texture, width, height) = _resolve(_iconSurfaceId);
             else
-                return;
+                return false;
 
             if (texture == 0 || width <= 0 || height <= 0)
-                return;
-            float inset = (_themes?.Theme ?? PluginUiTheme.Classic) == PluginUiTheme.Classic ? 6f : 2f;
-            float extent = MathF.Min(Width - inset, Height - inset);
-            ctx.DrawSprite(
-                texture,
-                (Width - extent) * 0.5f,
-                (Height - extent) * 0.5f,
-                extent,
-                extent,
-                0f,
-                0f,
-                1f,
-                1f,
-                Vector4.One);
+                return false;
+            ctx.DrawSprite(texture, x, y, extent, extent, 0f, 0f, 1f, 1f, new Vector4(1f, 1f, 1f, colour.W));
+            return true;
         }
 
+        /// <summary>The tint for a one-colour icon: the theme's text colour, its accent while the
+        /// window is open; Classic's white, its gold while open.</summary>
+        private Vector4 IconColor() => _themes?.Palette is { } palette
+            ? (_handle.IsVisible ? palette.Accent : palette.Text)
+            : (_handle.IsVisible ? VisibleBorder : Vector4.One);
+
         internal void DisposeSubscriptions()
         {
             _handle.Shown -= OnVisibilityChanged;
             _handle.Hidden -= OnVisibilityChanged;
+            _svgIcon?.Release();
+            _svgIcon = null;
         }
 
         private void OnVisibilityChanged(RetailWindowHandle _) =>
```

- [ ] **Step 6: Resolve the icon at mount and dispose the cache**

```diff
diff --git a/src/AcDream.App/UI/RetailUiRuntime.cs b/src/AcDream.App/UI/RetailUiRuntime.cs
index 4f3f23f2..2a43cd77 100644
--- a/src/AcDream.App/UI/RetailUiRuntime.cs
+++ b/src/AcDream.App/UI/RetailUiRuntime.cs
@@ -408,6 +408,7 @@ public sealed class RetailUiRuntime : IDisposable
     private PluginUiThemeSettings? _pluginThemes;
     private UiNineSlicePanel? _pluginAppearance;
     private readonly Dictionary<string, (uint Texture, int Width, int Height)?> _pluginIcons = [];
+    private PluginSvgIconCache? _pluginSvgIcons;
     private bool _pluginsMounted;
     private IDisposable? _characterSheetSubscription;
     private Layout.CharacterTitlesController? _characterTitlesController;
@@ -4144,7 +4145,8 @@ public sealed class RetailUiRuntime : IDisposable
                         panel.Owner,
                         panel.Descriptor,
                         handle,
-                        ResolvePluginFileIcon(panel.Owner.Id, panel.PluginDirectory));
+                        ResolvePluginFileIcon(panel.Owner.Id, panel.PluginDirectory),
+                        ResolvePluginSvgIcon(panel.Owner.Id, panel.Descriptor, panel.PluginDirectory));
                 }
 
                 Console.WriteLine(
@@ -4279,6 +4281,40 @@ public sealed class RetailUiRuntime : IDisposable
         return icon;
     }
 
+    /// <summary>
+    /// The window's SVG icon with one hold taken, or null: the descriptor's
+    /// <c>IconFile</c> first, then the plugin's own <c>icon.svg</c>. Any file that
+    /// cannot be used is logged once and the next icon is tried.
+    /// </summary>
+    private PluginSvgIconCache.PluginSvgIconEntry? ResolvePluginSvgIcon(
+        string pluginId,
+        PluginPanelDescriptor descriptor,
+        string? pluginDirectory)
+    {
+        if (pluginDirectory is null)
+            return null;
+        _pluginSvgIcons ??= new PluginSvgIconCache(new RetailPluginFontBackend(_bindings.Assets.TextureCache));
+
+        if (descriptor.IconFile is { } iconFile
+            && AcquirePluginSvgIcon(pluginId, pluginDirectory, iconFile) is { } own)
+            return own;
+        return File.Exists(Path.Combine(pluginDirectory, PluginSvgIcon.FileName))
+            ? AcquirePluginSvgIcon(pluginId, pluginDirectory, PluginSvgIcon.FileName)
+            : null;
+    }
+
+    private PluginSvgIconCache.PluginSvgIconEntry? AcquirePluginSvgIcon(
+        string pluginId, string pluginDirectory, string relative)
+    {
+        string label = $"{pluginId}/{relative.Replace('\\', '/')}";
+        if (!PluginSvgIcon.TryResolvePath(pluginDirectory, relative, out string? fullPath, out string? reason))
+        {
+            _pluginSvgIcons!.Reject(label, Path.Combine(pluginDirectory, relative), reason);
+            return null;
+        }
+        return _pluginSvgIcons!.Acquire(label, fullPath);
+    }
+
     private void MountInventory()
     {
         ImportedLayout? layout = Import(0x21000023u);
@@ -5154,6 +5190,9 @@ public sealed class RetailUiRuntime : IDisposable
                 _characterSheetSubscription?.Dispose();
                 _characterTitlesController?.Dispose();
                 _pluginSidePanel?.Dispose();
+                // After the dock has given back its holds, while the texture cache is still here.
+                _pluginSvgIcons?.Dispose();
+                _pluginSvgIcons = null;
                 Host.WindowManager.WindowVisibilityChanged -= OnWindowVisibilityChanged;
                 WindowLockPresentation.Dispose();
                 WindowOpacity.Dispose();
```

- [ ] **Step 7: Run the new tests and the existing dock tests**

```bash
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~PluginShelfSvgIconTests"
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --no-build --filter "FullyQualifiedName~PluginSidePanel|FullyQualifiedName~PluginShelfIcon"
dotnet build AcDream.slnx 2>&1 | grep -E "warning|error|Build succeeded" | tail -3
```

Expected: `Passed! - Failed: 0, Passed: 11`. The second run matches `main`: on a machine without DATs at the default path, `PluginShelfIconInstalledDatTests.AShelfIconId_ResolvesToARealInstalledRenderSurface` and two `PluginSidePanelToggleGlyphClipTests` fail with `FileNotFoundException … client_cell_1.dat`; everything else passes. The build prints `Build succeeded.`

- [ ] **Step 8: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.Plugin.Abstractions/IUiRegistry.cs \
  src/AcDream.App/UI/UiRenderContext.cs \
  src/AcDream.App/UI/PluginSidePanel.cs \
  src/AcDream.App/UI/RetailUiRuntime.cs \
  tests/AcDream.App.Tests/UI/PluginShelfSvgIconTests.cs
git commit -m "feat: SVG icons on dock buttons through one DrawIcon seam" -m "Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.Plugin.Abstractions/IUiRegistry.cs", "src/AcDream.App/UI/UiRenderContext.cs", "src/AcDream.App/UI/PluginSidePanel.cs", "src/AcDream.App/UI/RetailUiRuntime.cs", "tests/AcDream.App.Tests/UI/PluginShelfSvgIconTests.cs"], "verifyCommand": "cd .worktrees/svg-icons && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH && dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter \"FullyQualifiedName~PluginShelfSvgIconTests\"", "modelTier": "standard", "acceptanceCriteria": ["`PluginShelfSvgIconTests` has 11 tests and all pass", "With an SVG, a PNG and a DAT id all present, only the SVG is drawn and the DAT resolver is never called", "At pixel scale 2 a Moss tile's 20-point icon box bakes at 40 device pixels", "Moss tints `Text` when closed and `Accent` when open; Classic tints white when closed and (0.76, 0.64, 0.25) when open", "A failed upload falls back to `icon.png` if there is one, else the initials, and gives its hold back", "Unregistering the window, disposing the dock, and a duplicate `Add` each give the hold back", "The existing `PluginSidePanel*` and `PluginShelfIcon*` tests have the same results as on `main` (DAT-dependent tests fail only when the DATs are absent)", "`dotnet build AcDream.slnx` succeeds with 0 warnings"]}
```

---

### Task 7: Launcher allows .svg; PluginCheck reports icon.svg

**Goal:** Plugin zips and hand-installed folders may carry `.svg` files of at most 16 KiB (`LauncherPluginIcon.SvgMaximumBytes`, pinned to the client's `PluginSvgIcon.MaximumBytes` by a parity test); PluginCheck adds a `plugin svg icon` line in both modes: PASS with the size when `icon.svg` is at the root, SKIP when it is not.

**Files:**
- Modify: `src/AcDream.Launcher.Core/Plugins/LauncherPluginIcon.cs` (`SvgFileName`, `SvgMaximumBytes`)
- Modify: `src/AcDream.Launcher.Core/Plugins/PluginContentPolicy.cs` (`.svg` in `AllowedExtensions`; size check)
- Modify: `src/AcDream.PluginCheck/PluginCheckRunner.cs` (`CheckSvgIcon`, called in directory and zip mode)
- Test: `tests/AcDream.Launcher.Core.Tests/Plugins/PluginContentPolicyTests.cs`
- Test: `tests/AcDream.PluginCheck.Tests/PluginCheckRunnerTests.cs`
- Test: `tests/AcDream.Headless.Tests/Plugins/PluginIconParityTests.cs`

**Acceptance Criteria:**
- [ ] `PluginContentPolicyTests`: 18 pass, including `.svg` accepted at the root and in a folder at exactly 16 KiB, and rejected at 16 KiB + 1 with `16 KiB` in the message
- [ ] `PluginCheckRunnerTests`: 16 pass, including a 24-byte `icon.svg` reported as `icon.svg present, 24 bytes`, an oversized one failing `plugin content policy`, and SKIP without one
- [ ] `PluginIconParityTests`: 25 pass, including `LauncherAndClientShareTheSvgIconLimit`

**Verify:** the three test commands in Step 4 → 18, 16 and 25 passed, 0 failed

**Steps:**

- [ ] **Step 1: Write the failing tests**

`tests/AcDream.Launcher.Core.Tests/Plugins/PluginContentPolicyTests.cs`:

```diff
diff --git a/tests/AcDream.Launcher.Core.Tests/Plugins/PluginContentPolicyTests.cs b/tests/AcDream.Launcher.Core.Tests/Plugins/PluginContentPolicyTests.cs
index 6d1ed000..57fa0e01 100644
--- a/tests/AcDream.Launcher.Core.Tests/Plugins/PluginContentPolicyTests.cs
+++ b/tests/AcDream.Launcher.Core.Tests/Plugins/PluginContentPolicyTests.cs
@@ -89,5 +89,22 @@ public sealed class PluginContentPolicyTests
             "Hello.dll");
     }
 
-    private static ExtractedFileRecord File(string path) => new(path, new string('a', 64), 1, 0x1A4);
+    [Fact]
+    public void AcceptsSvgIcons()
+    {
+        PluginContentPolicy.Validate(
+            [File("plugin.json"), File("Hello.dll"), File("icon.svg"), File("icons/Loot.SVG", 16 * 1024)],
+            "Hello.dll");
+    }
+
+    [Fact]
+    public void RejectsAnSvgOverTheIconLimit()
+    {
+        var ex = Assert.Throws<LauncherUpdateException>(() => PluginContentPolicy.Validate(
+            [File("plugin.json"), File("Hello.dll"), File("icons/loot.svg", 16 * 1024 + 1)],
+            "Hello.dll"));
+        Assert.Contains("16 KiB", ex.Message, StringComparison.Ordinal);
+    }
+
+    private static ExtractedFileRecord File(string path, long size = 1) => new(path, new string('a', 64), size, 0x1A4);
 }
```

`tests/AcDream.PluginCheck.Tests/PluginCheckRunnerTests.cs`:

```diff
diff --git a/tests/AcDream.PluginCheck.Tests/PluginCheckRunnerTests.cs b/tests/AcDream.PluginCheck.Tests/PluginCheckRunnerTests.cs
index 0945db6b..27f5f7c6 100644
--- a/tests/AcDream.PluginCheck.Tests/PluginCheckRunnerTests.cs
+++ b/tests/AcDream.PluginCheck.Tests/PluginCheckRunnerTests.cs
@@ -92,6 +92,48 @@ public sealed class PluginCheckRunnerTests : IDisposable
         Assert.Contains("SemVer", check.Message, StringComparison.Ordinal);
     }
 
+    [Fact]
+    public async Task AnSvgIconIsReportedWithItsSize()
+    {
+        string directory = WriteValidPluginFolder("svg-icon");
+        File.WriteAllText(Path.Combine(directory, "icon.svg"), "<svg viewBox=\"0 0 1 1\"/>");
+
+        PluginCheckReport report = await PluginCheckRunner.RunAsync(directory);
+
+        Assert.Equal(PluginCheckVerdict.WouldInstall, report.Verdict);
+        PluginCheckItem check = Assert.Single(report.Checks, c => c.Check == "plugin svg icon");
+        Assert.Equal(PluginCheckStatus.Pass, check.Status);
+        Assert.Contains("24 bytes", check.Message, StringComparison.Ordinal);
+    }
+
+    [Fact]
+    public async Task AnOversizedSvgFailsTheContentPolicyInZipMode()
+    {
+        Directory.CreateDirectory(_root);
+        string zipPath = Path.Combine(_root, "big-svg.zip");
+        using (FileStream stream = File.Create(zipPath))
+        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
+        {
+            AddEntry(archive, "plugin.json", Encoding.UTF8.GetBytes(ValidManifestJson()));
+            AddEntry(archive, EntryDll, [1, 2, 3]);
+            AddEntry(archive, "icon.svg", new byte[16 * 1024 + 1]);
+        }
+
+        PluginCheckReport report = await PluginCheckRunner.RunAsync(zipPath);
+
+        Assert.Equal(PluginCheckVerdict.WouldBeRefused, report.Verdict);
+        PluginCheckItem check = Assert.Single(report.Checks, c => c.Check == "plugin content policy");
+        Assert.Equal(PluginCheckStatus.Fail, check.Status);
+        Assert.Contains("SVG", check.Message, StringComparison.Ordinal);
+    }
+
+    [Fact]
+    public async Task NoSvgIconIsSkipped()
+    {
+        PluginCheckReport report = await PluginCheckRunner.RunAsync(WriteValidPluginFolder("no-svg"));
+        Assert.Equal(PluginCheckStatus.Skip, Assert.Single(report.Checks, c => c.Check == "plugin svg icon").Status);
+    }
+
     [Fact]
     public async Task AnOversizedIconFailsInDirectoryMode()
     {
```

`tests/AcDream.Headless.Tests/Plugins/PluginIconParityTests.cs`:

```diff
diff --git a/tests/AcDream.Headless.Tests/Plugins/PluginIconParityTests.cs b/tests/AcDream.Headless.Tests/Plugins/PluginIconParityTests.cs
index e25791e9..a5894501 100644
--- a/tests/AcDream.Headless.Tests/Plugins/PluginIconParityTests.cs
+++ b/tests/AcDream.Headless.Tests/Plugins/PluginIconParityTests.cs
@@ -41,6 +41,13 @@ public sealed class PluginIconParityTests
         return data;
     }
 
+    [Fact]
+    public void LauncherAndClientShareTheSvgIconLimit()
+    {
+        Assert.Equal(PluginSvgIcon.MaximumBytes, LauncherPluginIcon.SvgMaximumBytes);
+        Assert.Equal(PluginSvgIcon.FileName, LauncherPluginIcon.SvgFileName);
+    }
+
     [Theory]
     [MemberData(nameof(Cases))]
     public void LauncherAndClientAgreeOnEveryRule(string _, byte[] bytes)
```

- [ ] **Step 2: Run them to see them fail**

```bash
cd .worktrees/svg-icons && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test tests/AcDream.Launcher.Core.Tests/AcDream.Launcher.Core.Tests.csproj --filter "FullyQualifiedName~PluginContentPolicyTests"
```

Expected: `AcceptsSvgIcons` fails with `Plugin file 'icon.svg' has a disallowed extension.` and `RejectsAnSvgOverTheIconLimit` fails for the same reason (its message lacks `16 KiB`). The Headless and PluginCheck projects fail to build (`'LauncherPluginIcon' does not contain a definition for 'SvgMaximumBytes'`) or fail their SVG tests.

- [ ] **Step 3: Implement**

`src/AcDream.Launcher.Core/Plugins/LauncherPluginIcon.cs`:

```diff
diff --git a/src/AcDream.Launcher.Core/Plugins/LauncherPluginIcon.cs b/src/AcDream.Launcher.Core/Plugins/LauncherPluginIcon.cs
index e95b1038..6fec104d 100644
--- a/src/AcDream.Launcher.Core/Plugins/LauncherPluginIcon.cs
+++ b/src/AcDream.Launcher.Core/Plugins/LauncherPluginIcon.cs
@@ -14,6 +14,13 @@ public static class LauncherPluginIcon
     public const int Extent = 64;
     public const int MaximumBytes = 64 * 1024;
 
+    /// <summary>The plugin's optional <c>icon.svg</c>. The launcher never draws it, so it checks
+    /// only the size; the client parses it and falls back to the next icon if it cannot.</summary>
+    public const string SvgFileName = "icon.svg";
+
+    /// <summary>The largest <c>.svg</c> file a plugin may ship, matching the client's limit.</summary>
+    public const int SvgMaximumBytes = 16 * 1024;
+
     private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
 
     public static void Validate(ReadOnlySpan<byte> bytes)
```

`src/AcDream.Launcher.Core/Plugins/PluginContentPolicy.cs`:

```diff
diff --git a/src/AcDream.Launcher.Core/Plugins/PluginContentPolicy.cs b/src/AcDream.Launcher.Core/Plugins/PluginContentPolicy.cs
index 473b2998..d19115c7 100644
--- a/src/AcDream.Launcher.Core/Plugins/PluginContentPolicy.cs
+++ b/src/AcDream.Launcher.Core/Plugins/PluginContentPolicy.cs
@@ -9,7 +9,7 @@ public static class PluginContentPolicy
 {
     private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
     {
-        ".dll", ".pdb", ".json", ".xml", ".txt", ".md", ".png", ".jpg", ".jpeg", ".ttf", ".otf",
+        ".dll", ".pdb", ".json", ".xml", ".txt", ".md", ".png", ".jpg", ".jpeg", ".ttf", ".otf", ".svg",
     };
 
     public static void Validate(IReadOnlyList<ExtractedFileRecord> files, string entryDll)
@@ -27,6 +27,14 @@ public static class PluginContentPolicy
                     $"Plugin file '{file.Path}' has a disallowed extension.");
             }
 
+            if (string.Equals(Path.GetExtension(file.Path), ".svg", StringComparison.OrdinalIgnoreCase)
+                && file.Size > LauncherPluginIcon.SvgMaximumBytes)
+            {
+                throw new LauncherUpdateException(
+                    $"Plugin file '{file.Path}' is larger than {LauncherPluginIcon.SvgMaximumBytes / 1024} KiB, "
+                    + "the limit for SVG icons.");
+            }
+
             if (file.Path.Split('/').Any(segment =>
                     string.Equals(segment, "runtimes", StringComparison.OrdinalIgnoreCase)))
             {
```

`src/AcDream.PluginCheck/PluginCheckRunner.cs`:

```diff
diff --git a/src/AcDream.PluginCheck/PluginCheckRunner.cs b/src/AcDream.PluginCheck/PluginCheckRunner.cs
index f9d06bfc..ae61db1d 100644
--- a/src/AcDream.PluginCheck/PluginCheckRunner.cs
+++ b/src/AcDream.PluginCheck/PluginCheckRunner.cs
@@ -72,6 +72,7 @@ internal static class PluginCheckRunner
                 "folder passes the direct-install checks",
                 refusal,
                 "This is the exact refusal a hand-unzipped install would show; fix it and re-run."));
+        checks.Add(CheckSvgIcon(directory));
 
         return Finish(PluginCheckMode.Directory, directory, checks);
     }
@@ -161,6 +162,7 @@ internal static class PluginCheckRunner
             }
 
             checks.Add(CheckIcon(stagingDirectory, extracted));
+            checks.Add(CheckSvgIcon(stagingDirectory));
             checks.Add(CheckSha256Sidecar(zipPath));
 
             return Finish(PluginCheckMode.Zip, zipPath, checks);
@@ -200,6 +202,18 @@ internal static class PluginCheckRunner
         }
     }
 
+    /// <summary>Only reported: the size limit is already part of the content policy, and the
+    /// client parses the file itself, falling back to the next icon if it cannot use it.</summary>
+    private static PluginCheckItem CheckSvgIcon(string root)
+    {
+        var file = new FileInfo(Path.Combine(root, LauncherPluginIcon.SvgFileName));
+        return file.Exists
+            ? Pass(
+                "plugin svg icon",
+                $"icon.svg present, {file.Length} bytes. The client checks the rest when it draws it.")
+            : Skip("plugin svg icon", "No icon.svg at the root; it is optional.");
+    }
+
     /// <summary>The <c>.sha256</c> sidecar is a release asset, not part of the zip itself, so this
     /// looks beside the given zip path rather than inside it. Absent locally, it only matters once a
     /// release is published, so a missing sidecar is not refused.</summary>
```

- [ ] **Step 4: Run the tests again**

```bash
dotnet test tests/AcDream.Launcher.Core.Tests/AcDream.Launcher.Core.Tests.csproj --filter "FullyQualifiedName~PluginContentPolicyTests"
dotnet test tests/AcDream.PluginCheck.Tests/AcDream.PluginCheck.Tests.csproj --filter "FullyQualifiedName~PluginCheckRunnerTests"
dotnet test tests/AcDream.Headless.Tests/AcDream.Headless.Tests.csproj --filter "FullyQualifiedName~PluginIconParityTests"
```

Expected: `Passed: 18`, `Passed: 16` and `Passed: 25`, each with `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.Launcher.Core/Plugins/LauncherPluginIcon.cs \
  src/AcDream.Launcher.Core/Plugins/PluginContentPolicy.cs \
  src/AcDream.PluginCheck/PluginCheckRunner.cs \
  tests/AcDream.Launcher.Core.Tests/Plugins/PluginContentPolicyTests.cs \
  tests/AcDream.PluginCheck.Tests/PluginCheckRunnerTests.cs \
  tests/AcDream.Headless.Tests/Plugins/PluginIconParityTests.cs
git commit -m "feat: allow .svg icons in plugin installs and report them in PluginCheck" -m "Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.Launcher.Core/Plugins/LauncherPluginIcon.cs", "src/AcDream.Launcher.Core/Plugins/PluginContentPolicy.cs", "src/AcDream.PluginCheck/PluginCheckRunner.cs", "tests/AcDream.Launcher.Core.Tests/Plugins/PluginContentPolicyTests.cs", "tests/AcDream.PluginCheck.Tests/PluginCheckRunnerTests.cs", "tests/AcDream.Headless.Tests/Plugins/PluginIconParityTests.cs"], "verifyCommand": "cd .worktrees/svg-icons && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH && dotnet test tests/AcDream.Launcher.Core.Tests/AcDream.Launcher.Core.Tests.csproj --filter \"FullyQualifiedName~PluginContentPolicyTests\" && dotnet test tests/AcDream.PluginCheck.Tests/AcDream.PluginCheck.Tests.csproj --filter \"FullyQualifiedName~PluginCheckRunnerTests\" && dotnet test tests/AcDream.Headless.Tests/AcDream.Headless.Tests.csproj --filter \"FullyQualifiedName~PluginIconParityTests\"", "modelTier": "mechanical", "acceptanceCriteria": ["`PluginContentPolicyTests`: 18 pass, including `.svg` accepted at the root and in a folder at exactly 16 KiB, and rejected at 16 KiB + 1 with `16 KiB` in the message", "`PluginCheckRunnerTests`: 16 pass, including a 24-byte `icon.svg` reported as `icon.svg present, 24 bytes`, an oversized one failing `plugin content policy`, and SKIP without one", "`PluginIconParityTests`: 25 pass, including `LauncherAndClientShareTheSvgIconLimit`"]}
```

---

### Task 8: Docs and the ThemeGallery sample

**Goal:** The plugin docs describe the new icon order, `IconFile`, the supported subset, the log line, the `.svg` allowance and its 16 KiB limit; the ThemeGallery sample ships a stroked `icon.svg` and a second "Swatches" window whose `IconFile` is a filled, part-transparent `icons/swatch.svg`.

**Files:**
- Modify: `docs/plugin-ui-markup.md` (example, icon order, new "SVG icons" section, the modern shelf paragraph)
- Modify: `docs/plugin-manifest.md` (allowlist adds `.svg`, 16 KiB)
- Modify: `docs/plugin-development.md` (release rules bullet; sample PluginCheck output gains `[SKIP] plugin svg icon`)
- Modify: `samples/AcDream.Plugins.ThemeGallery/AcDream.Plugins.ThemeGallery.csproj`
- Modify: `samples/AcDream.Plugins.ThemeGallery/ThemeGalleryPlugin.cs`
- Create: `samples/AcDream.Plugins.ThemeGallery/icon.svg`
- Create: `samples/AcDream.Plugins.ThemeGallery/icons/swatch.svg`

**Acceptance Criteria:**
- [ ] The sample builds and its output folder holds `icon.svg` and `icons/swatch.svg`
- [ ] Both sample SVGs parse (`PluginSvgIcon.TryLoad` → true), checked by the one-off script in Step 4
- [ ] `docs/plugin-ui-markup.md` has a `## SVG icons` heading, and `plugin-manifest.md`/`plugin-development.md` link to `plugin-ui-markup.md#svg-icons`

**Verify:** `dotnet build samples/AcDream.Plugins.ThemeGallery/AcDream.Plugins.ThemeGallery.csproj` → `Build succeeded.`; `ls samples/AcDream.Plugins.ThemeGallery/bin/Debug/net10.0/icons/swatch.svg` exists

**Steps:**

- [ ] **Step 1: Docs**

`docs/plugin-ui-markup.md`:

````diff
diff --git a/docs/plugin-ui-markup.md b/docs/plugin-ui-markup.md
index 18b3b39e..6380d474 100644
--- a/docs/plugin-ui-markup.md
+++ b/docs/plugin-ui-markup.md
@@ -14,6 +14,7 @@ host.Ui.AddPanel(
     {
         IconText = "MP",             // shelf button initials, the last resort
         IconSurfaceId = 0x06002C41,  // an icon id (see "Icon ids")
+        IconFile = "icons/main.svg", // a one-colour SVG (see "SVG icons")
         StartVisible = true,
         ShowInSidePanel = true,      // default: a button in the plugin shelf
     },
@@ -27,9 +28,60 @@ host.Ui.AddPanel(
   removes the window on its own.
 - `RegisterPanelContent` takes the markup as a string instead of a file path.
 
-The plugin shelf picks a button's icon in order: the plugin's own `icon.png`
-(one per plugin, at the root of its install folder), else `IconSurfaceId`,
-else the initials from `IconText`.
+The plugin shelf picks a button's icon in order:
+
+1. the window's own `IconFile`, an SVG path relative to the install folder;
+2. the plugin's `icon.svg`, at the root of its install folder;
+3. the plugin's `icon.png`, at the root of its install folder;
+4. `IconSurfaceId`;
+5. the initials from `IconText`.
+
+An icon that cannot be used is skipped, and the next one in the list is
+drawn.
+
+## SVG icons
+
+An SVG icon stays sharp at every display scale and takes its colour from the
+plugin theme: muted or white while the window is closed, the theme's accent
+while it is open. The file supplies only the shape. Any colour in it means
+"draw here", and its hue is ignored. Icon sets drawn on a 24-unit grid with a
+stroke, such as Lucide or Tabler, work as they are.
+
+```xml
+<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none"
+     stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round">
+  <path d="M5.5 8.5h13l-1 11h-11l-1-11Z"/>
+  <path d="M9 8.5V7a3 3 0 0 1 6 0v1.5"/>
+</svg>
+```
+
+The client reads a small, safe part of SVG. A file that uses anything else
+is not drawn at all, so an icon is never drawn wrong:
+
+- **Size:** at most 16 KiB, 256 elements nested at most 8 deep, and 4,096
+  path commands.
+- **Shapes:** `path` (every command, `M L H V C S Q T A Z`), `circle`,
+  `ellipse`, `rect` (with `rx`/`ry`), `line`, `polyline`, `polygon`, grouped
+  with `g`. `title`, `desc` and `metadata` are skipped, as is an empty
+  `defs`.
+- **Style**, as attributes or in `style="..."`, inherited through `g`:
+  `fill`, `stroke`, `fill-opacity`, `stroke-opacity`, `opacity`,
+  `stroke-width`, `stroke-linecap`, `stroke-linejoin`, `stroke-miterlimit`.
+  `opacity` on a `g` is applied to each child separately, so where two
+  children overlap they look slightly darker than a browser would draw them.
+- **Transforms:** `matrix`, `translate`, `scale`, `rotate`, `skewX`,
+  `skewY`.
+- **The icon is fitted** into a square and centred, whatever its `viewBox`
+  shape.
+- **Not supported:** `fill-rule="evenodd"`, dashed strokes, gradients,
+  patterns, masks, clip paths, filters, text, images, `<use>`, `<style>`,
+  scripts and DTDs.
+
+When a file is skipped, the client log says why, once per file:
+
+```
+[UI] plugin icon 'acme.hello/icons/main.svg' ignored: uses <mask>, which plugin icons do not support
+```
 
 Every window gets drag, an optional resize, the global UI lock, and a
 persisted position keyed `plugin:{pluginId}:{windowId}`. Hiding a window never
@@ -49,7 +101,8 @@ opted-in plugin windows. The atlas covers Latin, Greek, Cyrillic and common
 punctuation; unsupported characters display a question mark.
 
 Modern shelves are 24 pixels wide; wheel scrolling reaches
-entries that do not fit vertically. Icons keep their full-color composition.
+entries that do not fit vertically. PNG and DAT icons keep their full-color
+composition; SVG icons take the theme's colours.
 
 Opted-in windows keep their authored layout. In the modern themes they draw
 in a softer style: the window has rounded corners, a soft shadow and a header
````

`docs/plugin-manifest.md`:

```diff
diff --git a/docs/plugin-manifest.md b/docs/plugin-manifest.md
index f55dac09..d5d7705f 100644
--- a/docs/plugin-manifest.md
+++ b/docs/plugin-manifest.md
@@ -74,8 +74,9 @@ install. `icon.jpg` and `icon.jpeg` are never accepted at the zip root, in any c
 subfolder is untouched by this rule.
 
 **Managed code only, by allowlist.** Every file in the zip must end in one of: `.dll`, `.pdb`,
-`.json`, `.xml`, `.txt`, `.md`, `.png`, `.jpg`, `.jpeg`, `.ttf`, `.otf`. A `runtimes/` folder is
-rejected.
+`.json`, `.xml`, `.txt`, `.md`, `.png`, `.jpg`, `.jpeg`, `.ttf`, `.otf`, `.svg`. A `runtimes/`
+folder is rejected. An `.svg` file may be at most 16 KiB; the launcher checks only that, and the
+client decides whether it can draw it (see [SVG icons](plugin-ui-markup.md#svg-icons)).
 
 **Caps:** zip at most 64 MiB. Extraction: 2,000 entries, 64 MiB per entry, 256 MiB total,
 compression ratio 200. A hand-installed folder is limited the same way, counting files rather than
```

`docs/plugin-development.md`:

```diff
diff --git a/docs/plugin-development.md b/docs/plugin-development.md
index c905564e..619dfdab 100644
--- a/docs/plugin-development.md
+++ b/docs/plugin-development.md
@@ -254,6 +254,7 @@ Mode: plugin .zip (a release asset)
 [PASS] manifest satisfies install rules
 [PASS] plugin content policy
 [PASS] plugin icon
+[SKIP] plugin svg icon
 [PASS] sha256 sidecar
 
 Verdict: this plugin would install.
@@ -299,6 +300,9 @@ loads locally. The headlines:
 - Managed files only, by extension allowlist; no `runtimes/` folder.
 - An optional `icon.png` at the zip root: PNG, exactly 64x64, at most 64 KiB,
   not animated. No icon is fine; a broken one refuses the whole install.
+- Optional SVG icons (`icon.svg` at the root, or any path a window's
+  `IconFile` names): at most 16 KiB each. See
+  [SVG icons](plugin-ui-markup.md#svg-icons) for what the client draws.
 - Declare [capabilities](plugin-manifest.md#capabilities) for anything the
   player would want to know about — network access, chat, input automation.
   The launcher shows them before installing and asks again when an update
```

- [ ] **Step 2: Sample icons**

`samples/AcDream.Plugins.ThemeGallery/icon.svg`:

```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor"
     stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round">
  <title>Theme Gallery</title>
  <rect x="3.5" y="4.5" width="17" height="15" rx="3"/>
  <path d="M3.5 9h17"/>
  <circle cx="9" cy="14.5" r="2"/>
  <path d="M13.5 13h4M13.5 16h3"/>
</svg>
```

`samples/AcDream.Plugins.ThemeGallery/icons/swatch.svg`:

```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
  <title>Swatches</title>
  <rect x="4" y="4" width="7" height="7" rx="2"/>
  <rect x="13" y="4" width="7" height="7" rx="2" opacity="0.7"/>
  <rect x="4" y="13" width="7" height="7" rx="2" opacity="0.45"/>
  <circle cx="16.5" cy="16.5" r="3.5" fill="none" stroke="black" stroke-width="1.7"/>
</svg>
```

- [ ] **Step 3: Sample project and plugin**

`samples/AcDream.Plugins.ThemeGallery/AcDream.Plugins.ThemeGallery.csproj`:

```diff
diff --git a/samples/AcDream.Plugins.ThemeGallery/AcDream.Plugins.ThemeGallery.csproj b/samples/AcDream.Plugins.ThemeGallery/AcDream.Plugins.ThemeGallery.csproj
index f07e86b6..135b16aa 100644
--- a/samples/AcDream.Plugins.ThemeGallery/AcDream.Plugins.ThemeGallery.csproj
+++ b/samples/AcDream.Plugins.ThemeGallery/AcDream.Plugins.ThemeGallery.csproj
@@ -16,5 +16,7 @@
   <ItemGroup>
     <None Include="plugin.json" CopyToOutputDirectory="PreserveNewest" />
     <None Include="gallery.xml" CopyToOutputDirectory="PreserveNewest" />
+    <None Include="icon.svg" CopyToOutputDirectory="PreserveNewest" />
+    <None Include="icons\swatch.svg" CopyToOutputDirectory="PreserveNewest" />
   </ItemGroup>
 </Project>
```

`samples/AcDream.Plugins.ThemeGallery/ThemeGalleryPlugin.cs`:

```diff
diff --git a/samples/AcDream.Plugins.ThemeGallery/ThemeGalleryPlugin.cs b/samples/AcDream.Plugins.ThemeGallery/ThemeGalleryPlugin.cs
index 25cc859f..dd16e8d5 100644
--- a/samples/AcDream.Plugins.ThemeGallery/ThemeGalleryPlugin.cs
+++ b/samples/AcDream.Plugins.ThemeGallery/ThemeGalleryPlugin.cs
@@ -11,6 +11,7 @@ public sealed class ThemeGalleryPlugin : IAcDreamPlugin
 {
     private IPluginHost? _host;
     private IDisposable? _window;
+    private IDisposable? _swatches;
     private readonly GalleryBinding _binding = new();
 
     public void Initialize(IPluginHost host) => _host = host;
@@ -24,12 +25,24 @@ public sealed class ThemeGalleryPlugin : IAcDreamPlugin
         _window = host.Ui.RegisterPanel(
             new PluginPanelDescriptor("gallery", "Theme Gallery") { IconText = "TG", StartVisible = true },
             markup, _binding);
+        // A second window with its own SVG, so the dock shows both a plugin icon.svg
+        // and a per-window IconFile side by side.
+        _swatches = host.Ui.RegisterPanelContent(
+            new PluginPanelDescriptor("swatches", "Swatches") { IconText = "SW", IconFile = "icons/swatch.svg", StartVisible = false },
+            """
+            <panel x="420" y="120" w="220" h="96" title="Swatches" theme="plugin">
+              <label x="12" y="36" text="This window's dock icon is icons/swatch.svg." />
+            </panel>
+            """,
+            _binding);
     }
 
     public void Disable()
     {
         _window?.Dispose();
         _window = null;
+        _swatches?.Dispose();
+        _swatches = null;
     }
 
     /// <summary>Plain properties the markup binds to; every action only changes what is shown.</summary>
```

- [ ] **Step 4: Build the sample and check both icons parse**

```bash
cd .worktrees/svg-icons && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet build samples/AcDream.Plugins.ThemeGallery/AcDream.Plugins.ThemeGallery.csproj 2>&1 | tail -2
ls samples/AcDream.Plugins.ThemeGallery/bin/Debug/net10.0/icon.svg samples/AcDream.Plugins.ThemeGallery/bin/Debug/net10.0/icons/swatch.svg
# One-off parse check (not committed): a throwaway test, run once, then deleted.
cat > tests/AcDream.Core.Tests/Plugins/SampleSvgOnceTests.cs <<'EOF'
using AcDream.Core.Plugins;
namespace AcDream.Core.Tests.Plugins;
public sealed class SampleSvgOnceTests
{
    [Theory]
    [InlineData("icon.svg")]
    [InlineData("icons/swatch.svg")]
    public void SampleIconParses(string name)
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../samples/AcDream.Plugins.ThemeGallery"));
        Assert.True(PluginSvgIcon.TryLoad(Path.Combine(root, name), out _, out string? reason), reason);
    }
}
EOF
dotnet test tests/AcDream.Core.Tests/AcDream.Core.Tests.csproj --filter "FullyQualifiedName~SampleSvgOnceTests"
rm tests/AcDream.Core.Tests/Plugins/SampleSvgOnceTests.cs
```

Expected: `Build succeeded.`, both paths listed, and `Passed! - Failed: 0, Passed: 2`.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add docs/plugin-ui-markup.md \
  docs/plugin-manifest.md \
  docs/plugin-development.md \
  samples/AcDream.Plugins.ThemeGallery/AcDream.Plugins.ThemeGallery.csproj \
  samples/AcDream.Plugins.ThemeGallery/ThemeGalleryPlugin.cs \
  samples/AcDream.Plugins.ThemeGallery/icon.svg \
  samples/AcDream.Plugins.ThemeGallery/icons/swatch.svg
git commit -m "docs: SVG plugin icons; ThemeGallery ships icon.svg and a per-window IconFile" -m "Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["docs/plugin-ui-markup.md", "docs/plugin-manifest.md", "docs/plugin-development.md", "samples/AcDream.Plugins.ThemeGallery/AcDream.Plugins.ThemeGallery.csproj", "samples/AcDream.Plugins.ThemeGallery/ThemeGalleryPlugin.cs", "samples/AcDream.Plugins.ThemeGallery/icon.svg", "samples/AcDream.Plugins.ThemeGallery/icons/swatch.svg"], "verifyCommand": "cd .worktrees/svg-icons && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH && dotnet build samples/AcDream.Plugins.ThemeGallery/AcDream.Plugins.ThemeGallery.csproj && ls samples/AcDream.Plugins.ThemeGallery/bin/Debug/net10.0/icons/swatch.svg", "modelTier": "mechanical", "acceptanceCriteria": ["The sample builds and its output folder holds `icon.svg` and `icons/swatch.svg`", "Both sample SVGs parse (`PluginSvgIcon.TryLoad` \u2192 true), checked by the one-off script in Step 4", "`docs/plugin-ui-markup.md` has a `## SVG icons` heading, and `plugin-manifest.md`/`plugin-development.md` link to `plugin-ui-markup.md#svg-icons`"]}
```

---

### Task 9: Visual gate: 2× captures of the dock in all three themes

**Goal:** Real 2× backbuffer captures of the dock with the ThemeGallery sample's two SVG icons (Theme Gallery open, Swatches closed) in Classic, Moss and Brass, which the user looks at and accepts.

> **USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in `acceptanceCriteria` has been re-validated independently, with output captured.

**Files:**
- Output (not committed): `dock-svg-<theme>-2x.png` for theme in classic, moss, brass, in the session scratchpad
- Modify (only if a defect is found): files from Tasks 4–6, each fix with a failing test first

**Acceptance Criteria:**
- [ ] Three PNG captures exist, each 2× (its width is twice the window's width in points)
- [ ] In each capture the Theme Gallery button shows the stroked gallery icon in the open colour and the Swatches button shows the swatch icon in the closed colour: Moss accent `#97BE81` / text `#E0E8E2`, Brass accent `#C9A665` / text `#E9E2D5`, Classic gold (0.76, 0.64, 0.25) / white
- [ ] Icon edges are crisp at 2× (no blur from resampling): the 1-device-pixel antialiased edge is visible when zoomed
- [ ] The user was shown the three captures and said they accept them; their words are recorded in the task notes

**Verify:** the three PNGs are listed with `ls -l` in the scratchpad, and the user's acceptance is quoted in the task notes

**Steps:**

- [ ] **Step 1: Make a capture build and a scratch root**

Set `SP` to your session scratchpad directory (your system prompt names it). The capture build is a throwaway detached worktree of this branch with one patch: the screenshot must ask for the framebuffer's size, not the window's size in points, or a 2× capture fails with `retained capture is 1600x1200; 800x600 was requested` (memory `retina-capture-workflow`). **Never commit this patch.**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
git worktree add --detach $SP/svg-capture plugin-icons/svg
cd $SP/svg-capture
```

In `src/AcDream.App/Rendering/GameWindow.cs`, where the frame is rendered, give `RenderFrameInput` the framebuffer size:

```diff
             _frameGraphs.Render(
                 new AcDream.App.Rendering.RenderFrameInput(
                     deltaSeconds,
-                    size.X,
-                    size.Y),
+                    _window!.FramebufferSize.X,
+                    _window!.FramebufferSize.Y),
                 out outcome);
```

```bash
dotnet build AcDream.slnx -c Release 2>&1 | tail -2
mkdir -p $SP/svg-root/plugins/sample.theme-gallery $SP/svg-root/settings $SP/svg-run $SP/svg-shots
cp -R samples/AcDream.Plugins.ThemeGallery/bin/Release/net10.0/. $SP/svg-root/plugins/sample.theme-gallery/
```

- [ ] **Step 2: Capture each theme at 2×**

For each theme, write the theme into the scratch settings, then run the client offline from `$SP/svg-run` (the client writes `Asheron's Call/acdream.keymap` relative to its working directory) with a probe script. During the 45-second sleep, ask the user to drag the window onto the built-in Retina display, and give them a countdown.

```bash
export DYLD_LIBRARY_PATH="$(brew --prefix)/lib" VK_DRIVER_FILES="$(brew --prefix)/etc/vulkan/icd.d/MoltenVK_icd.json"
for theme in Classic Moss Brass; do
  printf '{"pluginUi":{"theme":"%s"}}\n' $theme > $SP/svg-root/settings/settings.json
  lower=$(echo $theme | tr A-Z a-z)
  printf 'sleep 45000\nscreenshot dock-svg-%s-2x\nclose-client\n' $lower > $SP/svg-run/probe.txt
  (cd $SP/svg-run && ACDREAM_ROOT_DIR=$SP/svg-root ACDREAM_DAT_DIR=$HOME/AsheronsCall \
     ACDREAM_PAK_PATH="$HOME/Library/Application Support/OpenAC/data/pak/acdream.pak" \
     ACDREAM_UI_PROBE_SCRIPT=$SP/svg-run/probe.txt ACDREAM_AUTOMATION_ARTIFACT_DIR=$SP/svg-shots \
     dotnet $SP/svg-capture/src/AcDream.App/bin/Release/net10.0/AcDream.App.dll)
done
ls -l $SP/svg-shots
```

The PNGs keep framebuffer alpha; flatten them onto black before judging colours (memory `modern-plugin-theme`). When done: `git worktree remove --force $SP/svg-capture`.

- [ ] **Step 3: Check the criteria yourself**

Crop the dock from each capture, zoom 4×, and check the icon colours against the palette values in the criteria and the edge sharpness. A defect that can be written as a test (wrong tint, wrong bake size, offset off the device grid) gets a failing test first, then the fix, committed as `fix: …`.

- [ ] **Step 4: Show the user and record their verdict**

Send the three captures (SendUserFile) and ask whether the SVG dock icons look right in all three themes. Quote their answer in the task notes. Do not close this task without it.

```json:metadata
{"files": [], "verifyCommand": "ls -l $SP/svg-shots/dock-svg-*-2x.png", "modelTier": "frontier", "userGate": true, "tags": ["user-gate"], "requiresUserSpecification": false, "acceptanceCriteria": ["Three PNG captures exist, each 2\u00d7 (its width is twice the window's width in points)", "In each capture the Theme Gallery button shows the stroked gallery icon in the open colour and the Swatches button shows the swatch icon in the closed colour: Moss accent `#97BE81` / text `#E0E8E2`, Brass accent `#C9A665` / text `#E9E2D5`, Classic gold (0.76, 0.64, 0.25) / white", "Icon edges are crisp at 2\u00d7 (no blur from resampling): the 1-device-pixel antialiased edge is visible when zoomed", "The user was shown the three captures and said they accept them; their words are recorded in the task notes"]}
```

---

### Task 10: Final verification, review and merge decision

**Goal:** The branch builds with 0 warnings, the affected test suites show no new failures against `main`, a final code review is done and acted on, and the user decides about pushing `plugin-icons/svg` and merging it into fork `main`.

**Files:**
- No new files

**Acceptance Criteria:**
- [ ] `dotnet build AcDream.slnx -c Release` → `0 Warning(s)`, `0 Error(s)`
- [ ] Core, App, Launcher.Core, PluginCheck, Headless and Plugin test suites fail exactly the same test names as `main` (on 2026-10-02 the prototype matched main name for name: Core 173, App 166, Launcher.Core 10 environment failures; Headless live tests flaky on both)
- [ ] `git diff --stat main...plugin-icons/svg -- docs/superpowers '*.lock.json'` is empty
- [ ] A final review (superpowers-extended-cc:requesting-code-review over `main...plugin-icons/svg`) is done and every finding is fixed or answered
- [ ] The user said yes before any push or merge; nothing is pushed otherwise

**Verify:** `git log --oneline main..plugin-icons/svg` lists Tasks 1–8's commits (plus any fixes) and the review's verdict is recorded

**Steps:**

- [ ] **Step 1: Full build and test runs**

Set `SP` to your session scratchpad directory (your system prompt names it).

```bash
cd .worktrees/svg-icons && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet build AcDream.slnx -c Release 2>&1 | tail -3
for t in AcDream.Core.Tests AcDream.App.Tests AcDream.Launcher.Core.Tests AcDream.PluginCheck.Tests AcDream.Headless.Tests AcDream.Plugin.Tests; do
  dotnet test tests/$t/$t.csproj -c Release --no-build 2>&1 | grep -E '^\s+Failed [A-Za-z]' | sed 's/^ *Failed //; s/ \[.*//' | sort > $SP/fails-branch-$t.txt
done
git checkout -- '*.lock.json'
```

Run the same loop in a detached worktree of `main` (writing `fails-main-$t.txt`), then `comm -13 fails-main-$t.txt fails-branch-$t.txt` for each suite must print nothing. A name that only fails on the branch is either a known flaky class (memory `local-dotnet-and-vulkan-env`; rerun it alone) or a regression to fix.

- [ ] **Step 2: Final review**

Run superpowers-extended-cc:requesting-code-review over `main...plugin-icons/svg`. Ask it to check: the parser never throws on hostile input (fuzz a handful of truncated files), every texture is released on every path (unregister, dispose, failed bake, duplicate add, hot reload), the `IconFile` path check on Windows paths, and that Classic draws a PNG/DAT icon exactly as before (same rectangle, `Vector4.One`).

- [ ] **Step 3: Merge-conflict notes for the dock branch**

Record in the task notes the conflicts `modern-theme/dock` should expect, so whichever branch lands second resolves them quickly:

- `src/AcDream.App/UI/PluginSidePanel.cs`: the `Add` overloads, `PluginShelfButton`'s constructor, `OnDraw`, `DrawIcon`, `IconColor` and `DisposeSubscriptions`. Keep the dock's layout and keep `DrawIcon`'s body from this branch. The dock's state colours (closed `Muted`, hover `Text`, open `Accent`, RGBA at 85% alpha when closed, Classic from `PluginUiPalette.ClassicDock`) replace this branch's `IconColor`.
- `src/AcDream.App/UI/RetailUiRuntime.cs`: the `_pluginSidePanel.Add(...)` call in the plugin mount (keep the fifth argument) and `Dispose` (keep `_pluginSvgIcons?.Dispose()` right after `_pluginSidePanel?.Dispose()`).
- `docs/plugin-ui-markup.md`: the icon order list and the "Modern shelves are 24 pixels wide" paragraph, which the dock rewrites; keep the dock's paragraph and this branch's icon list and SVG section.

- [ ] **Step 4: Ask the user**

Ask whether to push `plugin-icons/svg` to `origin` and merge it into fork `main` with `--no-ff` (as for painter v2 and the modern theme), and whether it goes before or after the dock branch. Do nothing until they answer. On a yes: `git push -u origin plugin-icons/svg`, then in the main checkout `git merge --no-ff plugin-icons/svg` and `git push origin main`.

- [ ] **Step 5: Update memory**

Update the `plugin-dock-redesign` memory with the branch head, merge commit (if any) and status.

```json:metadata
{"files": [], "verifyCommand": "cd .worktrees/svg-icons && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH && dotnet build AcDream.slnx -c Release && git diff --stat main...plugin-icons/svg -- docs/superpowers '*.lock.json'", "modelTier": "standard", "acceptanceCriteria": ["`dotnet build AcDream.slnx -c Release` \u2192 `0 Warning(s)`, `0 Error(s)`", "Core, App, Launcher.Core, PluginCheck, Headless and Plugin test suites fail exactly the same test names as `main` (on 2026-10-02 the prototype matched main name for name: Core 173, App 166, Launcher.Core 10 environment failures; Headless live tests flaky on both)", "`git diff --stat main...plugin-icons/svg -- docs/superpowers '*.lock.json'` is empty", "A final review (superpowers-extended-cc:requesting-code-review over `main...plugin-icons/svg`) is done and every finding is fixed or answered", "The user said yes before any push or merge; nothing is pushed otherwise"]}
```

---
