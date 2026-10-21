# Markup controls, plan 6: `<split>` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `<split>`, two panes with a draggable bar between them, whose position is plugin-owned when bound and remembered per character when not.

**Architecture:**
- **Shape.** `UiMarkupSplit` is a click-through container holding the two authored `<group>` panes and a `UiMarkupSplitBar` (5 px). It lays all three out itself from one number, the first pane's size, every tick and at the start of its own draw (after its parent's anchor pass has sized it, before the panes' children are anchored). The panes and the bar have `Anchors = None`, so the anchor pass never fights the split; children inside the panes anchor to their pane as usual.
- **Position.** `Position` = the bound `float` when `position` is bound, else the host's value (remembered via `RememberedValue`, kind `"position"`, or the markup default `at`, or half the room). The laid-out value is always clamped to `min`/`max` and to the room (`main size − 5`) and rounded to whole pixels. When the split resizes, the first pane keeps its size and the second takes the rest.
- **Input.** The bar captures the pointer (`CapturesPointerDrag`), shows the retail resize cursor (state cursor `""`), highlights on hover and while dragging, and is the split's one tab stop: arrow keys along its axis move it 8 px. Clicking doesn't take keyboard focus.
- **Callbacks.** `onchange(float)` fires once per distinct whole-pixel position during a drag or per key press. Unbound, the host value moves with it and is remembered once, at drag end (or per key press), removed when back at the default. Bound, the host only asks; the layout follows the property.

**Status:** DONE 2026-10-21 — feat 13aa1206..5561afcb (7 commits incl. 3 final-review fix commits; each builds; pushed). Final review also fixed the expander move-back regression from 13aa1206 (per-entry move delta). Parked: grown stretched expander's authoredBottom misses siblings below (pre-existing, transient); disabled ancestor doesn't fade the bar (same as all controls).

**Tech Stack:** C# / .NET 10, xUnit, headless `UiRoot` + `RecordingGpuDevice` tests.

**Spec:** `docs/superpowers/specs/2026-10-21-markup-controls-design.md` (branch `docs/markup-controls-spec`), "Shared rules" and section 7. Plans 4-5 built the store, `RememberedValue` and the `AddChild` build context.

## Global Constraints

- **Workspace.** Worktree `/Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls`, branch `feat/markup-controls` (HEAD 13aa1206, from upstream 391e709a). Never merge fork `main` in; never add `docs/superpowers/` files there. Push only when the controller says so.
- **Build hygiene.** Every shell runs `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH` first. After building, `git checkout -- '*.lock.json'`; never commit lock files.
- **Commits.** Every commit must build and pass its own tests. Commit messages end with `Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W`.
- **Helper conventions.** `BindRequired*` throw when absent or mistyped; other `Bind*` return null when absent and throw when mistyped; `context` is `"<element> <attribute>"`. Builders return exactly one element (so `PluginMarkupTheme.Register` still fires for it).
- **Remembered-state contract** (enforced by `RememberedValue`): re-read when `Generation` moves; build new values from a fresh read; write only on a user commit (drag end, key press), never per drag frame; unnamed controls and bound state never touch the store; remove the entry when the encoded value equals the encoded markup default. Encoding: whole pixels, invariant-culture integer (`"150"`); undecodable or negative = absent (use the default); the read value is clamped at layout, not on read.
- **`<split>` contract.**
  - Attributes: `name x y w h orientation position onchange at min max` plus the common ones (`visible enabled tooltip anchor`).
  - `orientation` is `horizontal` (default: panes side by side, the bar vertical) or `vertical` (stacked); anything else throws.
  - Exactly two children, both `<group>`, else build throws. The split sizes its groups: their `x y w h anchor` are ignored.
  - `at`, `min`, `max` are pixel lengths ≥ 0 (else throw); `max < min` throws. Defaults: `min` 0, `max` the room, `at` half the authored room, rounded down.
  - `position` is a required-if-present `float` binding; when present the plugin owns the position. `onchange` is an optional `Action<float>`.
  - Bar 5 px (`UiMarkupSplit.BarSize`); arrow keys step 8 px (`UiMarkupSplit.KeyStep`): Left/Right for horizontal, Up/Down for vertical; other keys fall through.
  - Disabled: no drag, no keys, no resize cursor, bar drawn at `UiMarkupToggle.DisabledAlpha`.

**User decisions (already made):**
- One branch, one commit series per control, upstream-portable.
- Remembered state is "both": unbound state is host-remembered per character/window/control; bound state is the plugin's.
- Split panes are the split's own children, so a split never moves its siblings (plan-5 review note).

---

## File Structure

| File | Responsibility |
|---|---|
| `src/AcDream.App/UI/Markup/MarkupBindings.cs` | `BindRequiredFloatReader`, `BindFloatAction` |
| `src/AcDream.App/UI/UiMarkupSplit.cs` (new) | The split: position, clamping, layout, move/commit, remembered value |
| `src/AcDream.App/UI/UiMarkupSplitBar.cs` (new) | The bar: pointer drag, keys, focus, hover, cursor, drawing |
| `src/AcDream.App/UI/Markup/MarkupSplit.cs` (new) | `<split>` builder |
| `src/AcDream.App/UI/MarkupDocument.cs` | `case "split"` |
| `src/AcDream.App/UI/PluginMarkupTheme.cs` | Bar palette |
| `tests/AcDream.App.Tests/UI/MarkupSplitTests.cs` (new) | Tests |
| `docs/plugin-ui-markup.md` | `<split>` docs |

---

### Task 1: Float bindings

**Goal:** Markup builders can bind a required `float` property and an optional `Action<float>` with the shared helper conventions.

**Files:**
- Modify: `src/AcDream.App/UI/Markup/MarkupBindings.cs`
- Test: `tests/AcDream.App.Tests/UI/MarkupSplitTests.cs` (new; binding tests only in this task)

**Acceptance Criteria:**
- [ ] `BindRequiredFloatReader` returns a live reader for a `float` property; absent, literal, missing or non-`float` (e.g. `int`) throws `FormatException` naming the context and `float`.
- [ ] `BindFloatAction` returns null when absent, invokes a bound `Action<float>`, and throws `FormatException` naming the context and `Action<float>` for a literal, missing or wrongly typed property.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupSplitTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — create `tests/AcDream.App.Tests/UI/MarkupSplitTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using AcDream.App.UI;
using AcDream.App.UI.Markup;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

public sealed class MarkupSplitTests
{
    private sealed class Binding
    {
        public float At { get; set; } = 120f;
        public List<float> Changes { get; } = new();
        public Action<float> SetAt => v => { At = v; Changes.Add(v); };
        public Action<float> Notify => v => Changes.Add(v);
        public int NotFloat { get; set; }
        public Action<int> WrongAction => _ => { };
        public int LeftClicks { get; private set; }
        public int RightClicks { get; private set; }
        public Action LeftClick => () => LeftClicks++;
        public Action RightClick => () => RightClicks++;
    }

    [Fact]
    public void FloatReader_ReadsTheProperty()
    {
        var b = new Binding();
        var read = MarkupBindings.BindRequiredFloatReader("{At}", b, "split position");
        Assert.Equal(120f, read());
        b.At = 7.5f;
        Assert.Equal(7.5f, read());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("12")]
    [InlineData("{Missing}")]
    [InlineData("{NotFloat}")]
    public void FloatReader_RejectsAbsentLiteralsAndWrongTypes(string? expression)
    {
        var ex = Assert.Throws<FormatException>(
            () => MarkupBindings.BindRequiredFloatReader(expression, new Binding(), "split position"));
        Assert.Contains("split position", ex.Message);
        Assert.Contains("float", ex.Message);
    }

    [Fact]
    public void FloatAction_MissingIsNullAndBoundInvokes()
    {
        var b = new Binding();
        Assert.Null(MarkupBindings.BindFloatAction(null, b, "ctx"));
        MarkupBindings.BindFloatAction("{SetAt}", b, "ctx")!(3f);
        Assert.Equal(3f, b.At);
    }

    [Theory]
    [InlineData("SetAt")]
    [InlineData("{WrongAction}")]
    [InlineData("{Missing}")]
    public void FloatAction_RejectsLiteralsAndWrongTypes(string attribute)
    {
        var ex = Assert.Throws<FormatException>(
            () => MarkupBindings.BindFloatAction(attribute, new Binding(), "split onchange"));
        Assert.Contains("split onchange", ex.Message);
        Assert.Contains("Action<float>", ex.Message);
    }
}
```

- [ ] **Step 2: Run to see them fail**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupSplitTests"`
Expected: build error — `MarkupBindings` has no `BindRequiredFloatReader` / `BindFloatAction`.

- [ ] **Step 3: Implement** — in `MarkupBindings`, after `BindIntAction`:

```csharp
    /// <summary>An optional <c>Action&lt;float&gt;</c>; null when the attribute is absent.</summary>
    public static Action<float>? BindFloatAction(string? attribute, object binding, string context) =>
        BindOptionalAction<float>(attribute, binding, context, "float");

    /// <summary>A required <c>float</c> binding, read live.</summary>
    public static Func<float> BindRequiredFloatReader(string? expression, object binding, string context)
    {
        if (string.IsNullOrWhiteSpace(expression) || !MarkupDocument.IsBinding(expression))
            throw new FormatException($"{context} must be a float binding");
        PropertyInfo? property = binding.GetType().GetProperty(expression[1..^1]);
        if (property is null || property.PropertyType != typeof(float))
        {
            throw new FormatException(
                $"{expression} did not resolve to a float property on "
                + binding.GetType().Name + $" ({context})");
        }
        return () => property.GetValue(binding) is float value ? value : 0f;
    }
```

- [ ] **Step 4: Run** — same command → PASS.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json' 2>/dev/null
git add src/AcDream.App/UI/Markup/MarkupBindings.cs tests/AcDream.App.Tests/UI/MarkupSplitTests.cs
git commit -m "feat(ui): float bindings for markup elements

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 2: `<split>` element

**Goal:** A working `<split>`: two panes and a bar laid out from one position, pointer drag and arrow keys, clamping, resize behaviour, bound or remembered position, disabled, cursor, build errors.

**Files:**
- Create: `src/AcDream.App/UI/UiMarkupSplit.cs`, `src/AcDream.App/UI/UiMarkupSplitBar.cs`, `src/AcDream.App/UI/Markup/MarkupSplit.cs`
- Modify: `src/AcDream.App/UI/MarkupDocument.cs` (one `case`)
- Test: `tests/AcDream.App.Tests/UI/MarkupSplitTests.cs`

**Acceptance Criteria:**
- [ ] Default layout: a horizontal 405×200 split lays out First (0,0,200,200), Bar (200,0,5,200), Second (205,0,200,200); vertical 300×205 stacks First h 100, Bar top 100, Second top 105 h 100.
- [ ] Dragging the bar 50 px left gives First.Width 150, Second.Left 155, Second.Width 250, and `onchange` received exactly `[150]`.
- [ ] `min`/`max` clamp a drag; without `max` the bar stops at the room (First.Width 400 for a 405-wide split).
- [ ] Widening the split keeps First's width; shrinking it below First's width clamps First to the room and Second to 0.
- [ ] Buttons inside both panes receive clicks; the bar press leaves `KeyboardFocus` null.
- [ ] Bound: layout follows `position` every tick; a drag calls `onchange` and nothing else; without `onchange` a drag changes nothing.
- [ ] Remembered (named, unbound): a drag stores `"150"` under (`main`, `position`); dragging back to the default removes it; a stored `"150"` lays out at 150 at build; a stored `"abc"` uses the default; a character switch re-reads; an unnamed split never calls the state.
- [ ] Keys: focused bar + Right → 208, Left → 200, Up does nothing on a horizontal split (and isn't handled); a key press is remembered.
- [ ] Disabled: drag and keys do nothing; after a tick the bar has no valid cursor. Enabled: horizontal bar's `ActiveCursor()` is `0x06006128`, vertical `0x06005E66`.
- [ ] Build throws `FormatException` for: one group, three groups, a non-group child, `orientation="diagonal"`, `min="-1"`, `max="x"`, `min="100" max="50"`, `position="{NotFloat}"`, `position="12"`, `onchange="{WrongAction}"`.
- [ ] `tooltip` shows on the bar (`Bar.GetTooltipText()`).

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupSplitTests|FullyQualifiedName~AcDream.App.Tests.UI.Markup"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — append inside `MarkupSplitTests`:

```csharp
    // The split sits at (10, 10). Horizontal 405×200: room 400, default 200, bar x 210-215 on screen.
    private static string Markup(string attributes, string size = "w=\"405\" h=\"200\"") => $"""
        <panel x="0" y="0" w="500" h="300">
          <split name="main" x="10" y="10" {size} {attributes}>
            <group><button name="l" x="4" y="4" w="60" h="20" text="L" onclick="{"{LeftClick}"}"/></group>
            <group><button name="r" x="4" y="4" w="60" h="20" text="R" onclick="{"{RightClick}"}"/></group>
          </split>
        </panel>
        """;

    private static (UiRoot Root, UiNineSlicePanel Panel, UiMarkupSplit Split) Mount(
        string attributes, Binding binding, IMarkupControlState? state = null, string size = "w=\"405\" h=\"200\"")
    {
        var panel = MarkupDocument.Build(Markup(attributes, size), binding, _ => (1u, 16, 16), controlState: state);
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        return (root, panel, panel.Children.OfType<UiMarkupSplit>().Single());
    }

    private static void Click(UiRoot root, int x, int y)
    {
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
    }

    private static void Drag(UiRoot root, int fromX, int toX, int y = 100)
    {
        root.OnMouseDown(UiMouseButton.Left, fromX, y);
        root.OnMouseMove(toX, y);
        root.OnMouseUp(UiMouseButton.Left, toX, y);
    }

    private static void DragY(UiRoot root, int fromY, int toY, int x = 100)
    {
        root.OnMouseDown(UiMouseButton.Left, x, fromY);
        root.OnMouseMove(x, toY);
        root.OnMouseUp(UiMouseButton.Left, x, toY);
    }

    private static (float X, float Y, float W, float H) Rect(UiElement e) => (e.Left, e.Top, e.Width, e.Height);

    /// <summary>Records every call, so a test can prove the state was never touched.</summary>
    private sealed class RecordingState : IMarkupControlState
    {
        public int Calls { get; private set; }
        public int Generation { get { Calls++; return 1; } }
        public string? Get(string control, string kind) { Calls++; return null; }
        public void Set(string control, string kind, string value) => Calls++;
        public void Remove(string control, string kind) => Calls++;
    }

    [Fact]
    public void Split_DefaultsToHalfAndLaysOutThePanes()
    {
        var (_, _, split) = Mount("", new Binding());
        Assert.Equal((0f, 0f, 200f, 200f), Rect(split.First!));
        Assert.Equal((200f, 0f, 5f, 200f), Rect(split.Bar));
        Assert.Equal((205f, 0f, 200f, 200f), Rect(split.Second!));
    }

    [Fact]
    public void Split_VerticalStacksThePanes()
    {
        var (_, _, split) = Mount("orientation=\"vertical\"", new Binding(), size: "w=\"300\" h=\"205\"");
        Assert.Equal((0f, 0f, 300f, 100f), Rect(split.First!));
        Assert.Equal((0f, 100f, 300f, 5f), Rect(split.Bar));
        Assert.Equal((0f, 105f, 300f, 100f), Rect(split.Second!));
    }

    [Fact]
    public void Split_VerticalDragMovesTheBarDown()
    {
        var (root, _, split) = Mount("orientation=\"vertical\"", new Binding(), size: "w=\"300\" h=\"205\"");
        DragY(root, 10 + 100 + 2, 10 + 100 + 2 + 30);
        Assert.Equal(130f, split.First!.Height);
        Assert.Equal(135f, split.Second!.Top);
        Assert.Equal(70f, split.Second.Height);
    }

    [Fact]
    public void Split_DragMovesTheBarAndResizesThePanes()
    {
        var b = new Binding();
        var (root, _, split) = Mount("onchange=\"{Notify}\"", b);
        Drag(root, 212, 162);
        Assert.Equal(150f, split.First!.Width);
        Assert.Equal(155f, split.Second!.Left);
        Assert.Equal(250f, split.Second.Width);
        Assert.Equal(new[] { 150f }, b.Changes);
    }

    [Fact]
    public void Split_MinAndMaxClampADrag()
    {
        var (root, _, split) = Mount("min=\"80\" max=\"300\"", new Binding());
        Drag(root, 212, 0);
        Assert.Equal(80f, split.First!.Width);
        Drag(root, 10 + 80 + 2, 790);
        Assert.Equal(300f, split.First.Width);
    }

    [Fact]
    public void Split_WithoutMaxStopsAtTheRoom()
    {
        var (root, _, split) = Mount("", new Binding());
        Drag(root, 212, 790);
        Assert.Equal(400f, split.First!.Width);
        Assert.Equal(0f, split.Second!.Width);
    }

    [Fact]
    public void Split_ResizeKeepsTheFirstPaneAndClampsWhenTooSmall()
    {
        var (_, panel, split) = Mount("anchor=\"left top right\"", new Binding());
        MarkupDrawProbe.Draw(panel, 800f, 600f);
        panel.Width = 600f;
        MarkupDrawProbe.Draw(panel, 800f, 600f);
        Assert.Equal(505f, split.Width);
        Assert.Equal(200f, split.First!.Width);
        Assert.Equal(300f, split.Second!.Width);
        panel.Width = 250f;
        MarkupDrawProbe.Draw(panel, 800f, 600f);
        Assert.Equal(155f, split.Width);
        Assert.Equal(150f, split.First.Width);
        Assert.Equal(0f, split.Second.Width);
        panel.Width = 500f;
        MarkupDrawProbe.Draw(panel, 800f, 600f);
        Assert.Equal(200f, split.First.Width);   // the host value was never clamped, only its layout
    }

    [Fact]
    public void Split_PaneChildrenAreClickableAndTheBarTakesNoFocus()
    {
        var b = new Binding();
        var (root, _, _) = Mount("", b);
        Click(root, 10 + 4 + 5, 10 + 4 + 5);
        Click(root, 10 + 205 + 4 + 5, 10 + 4 + 5);
        Assert.Equal(1, b.LeftClicks);
        Assert.Equal(1, b.RightClicks);
        Click(root, 212, 100);
        Assert.Null(root.KeyboardFocus);
    }

    [Fact]
    public void Split_BoundPositionBelongsToThePlugin()
    {
        var b = new Binding();
        var (root, _, split) = Mount("position=\"{At}\" onchange=\"{SetAt}\"", b);
        Assert.Equal(120f, split.First!.Width);
        Drag(root, 10 + 120 + 2, 10 + 170 + 2);
        Assert.Equal(new[] { 170f }, b.Changes);
        Assert.Equal(170f, split.First.Width);
        b.At = 60f;
        root.Tick(0.016, 0);
        Assert.Equal(60f, split.First.Width);
    }

    [Fact]
    public void Split_BoundWithoutOnchangeOnlyDisplays()
    {
        var b = new Binding();
        var (root, _, split) = Mount("position=\"{At}\"", b);
        Drag(root, 10 + 120 + 2, 10 + 170 + 2);
        Assert.Equal(120f, split.First!.Width);
        Assert.Equal(120f, b.At);
    }

    [Fact]
    public void Split_RemembersAtDragEndAndForgetsTheDefault()
    {
        var store = new MarkupControlStateStore(null, () => "alice");
        var state = store.ForWindow("win");
        var (root, _, split) = Mount("", new Binding(), state);
        Drag(root, 212, 162);
        Assert.Equal("150", state.Get("main", "position"));
        Drag(root, 10 + 150 + 2, 212);
        Assert.Equal(200f, split.First!.Width);
        Assert.Null(state.Get("main", "position"));
    }

    [Fact]
    public void Split_StoredPositionAppliesAtBuildAndMalformedIsIgnored()
    {
        var store = new MarkupControlStateStore(null, () => "alice");
        var state = store.ForWindow("win");
        state.Set("main", "position", "150");
        var (_, _, remembered) = Mount("", new Binding(), state);
        Assert.Equal(150f, remembered.First!.Width);
        state.Set("main", "position", "abc");
        var (_, _, malformed) = Mount("", new Binding(), state);
        Assert.Equal(200f, malformed.First!.Width);
    }

    [Fact]
    public void Split_ACharacterSwitchReReads()
    {
        string character = "alice";
        var store = new MarkupControlStateStore(null, () => character);
        var state = store.ForWindow("win");
        var (root, _, split) = Mount("", new Binding(), state);
        Drag(root, 212, 162);
        character = "bob";
        root.Tick(0.016, 0);
        Assert.Equal(200f, split.First!.Width);
        character = "alice";
        root.Tick(0.016, 0);
        Assert.Equal(150f, split.First.Width);
    }

    [Fact]
    public void Split_UnnamedOrBoundIsNeverRemembered()
    {
        var unnamedState = new RecordingState();
        var panel = MarkupDocument.Build("""
            <panel x="0" y="0" w="500" h="300">
              <split x="10" y="10" w="405" h="200"><group/><group/></split>
            </panel>
            """, new Binding(), _ => (1u, 16, 16), controlState: unnamedState);
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        Drag(root, 212, 162);
        root.Tick(0.016, 0);
        Assert.Equal(0, unnamedState.Calls);

        var boundState = new RecordingState();
        var (boundRoot, _, _) = Mount("position=\"{At}\" onchange=\"{SetAt}\"", new Binding(), boundState);
        Drag(boundRoot, 10 + 120 + 2, 10 + 170 + 2);
        boundRoot.Tick(0.016, 0);
        Assert.Equal(0, boundState.Calls);
    }

    [Fact]
    public void Split_ArrowKeysMoveTheFocusedBarAndAreRemembered()
    {
        var store = new MarkupControlStateStore(null, () => "alice");
        var state = store.ForWindow("win");
        var (root, _, split) = Mount("", new Binding(), state);
        Assert.True(split.Bar.TabStop);
        root.SetKeyboardFocus(split.Bar);
        root.OnKeyDown((int)Key.Right);
        Assert.Equal(208f, split.First!.Width);
        Assert.Equal("208", state.Get("main", "position"));
        root.OnKeyDown((int)Key.Left);
        Assert.Equal(200f, split.First.Width);
        Assert.Null(state.Get("main", "position"));
        Assert.False(split.Bar.OnEvent(new UiEvent { Type = UiEventType.KeyDown, Data0 = (int)Key.Up }));
        Assert.Equal(200f, split.First.Width);
    }

    [Fact]
    public void Split_DisabledIgnoresDragsAndKeysAndShowsNoResizeCursor()
    {
        var (root, _, split) = Mount("enabled=\"false\"", new Binding());
        Drag(root, 212, 162);
        split.Bar.OnEvent(new UiEvent { Type = UiEventType.FocusGained });
        split.Bar.OnEvent(new UiEvent { Type = UiEventType.KeyDown, Data0 = (int)Key.Right });
        Assert.Equal(200f, split.First!.Width);
        root.Tick(0.016, 0);
        Assert.False(split.Bar.ActiveCursor().IsValid);
    }

    [Fact]
    public void Split_BarShowsTheResizeCursorForItsAxis()
    {
        var (_, _, horizontal) = Mount("", new Binding());
        Assert.Equal(0x06006128u, horizontal.Bar.ActiveCursor().File);
        var (_, _, vertical) = Mount("orientation=\"vertical\"", new Binding(), size: "w=\"300\" h=\"205\"");
        Assert.Equal(0x06005E66u, vertical.Bar.ActiveCursor().File);
    }

    [Fact]
    public void Split_TooltipShowsOnTheBar()
    {
        var (_, _, split) = Mount("tooltip=\"Drag to resize\"", new Binding());
        Assert.Equal("Drag to resize", split.Bar.GetTooltipText());
    }

    [Theory]
    [InlineData("<split w=\"100\" h=\"50\"><group/></split>")]
    [InlineData("<split w=\"100\" h=\"50\"><group/><group/><group/></split>")]
    [InlineData("<split w=\"100\" h=\"50\"><group/><label text=\"x\"/></split>")]
    [InlineData("<split w=\"100\" h=\"50\" orientation=\"diagonal\"><group/><group/></split>")]
    [InlineData("<split w=\"100\" h=\"50\" min=\"-1\"><group/><group/></split>")]
    [InlineData("<split w=\"100\" h=\"50\" max=\"x\"><group/><group/></split>")]
    [InlineData("<split w=\"100\" h=\"50\" at=\"wide\"><group/><group/></split>")]
    [InlineData("<split w=\"100\" h=\"50\" min=\"100\" max=\"50\"><group/><group/></split>")]
    [InlineData("<split w=\"100\" h=\"50\" position=\"{NotFloat}\"><group/><group/></split>")]
    [InlineData("<split w=\"100\" h=\"50\" position=\"12\"><group/><group/></split>")]
    [InlineData("<split w=\"100\" h=\"50\" onchange=\"{WrongAction}\"><group/><group/></split>")]
    public void Split_BadMarkupThrowsAtBuild(string split)
    {
        Assert.Throws<FormatException>(() => MarkupDocument.Build(
            $"<panel x=\"0\" y=\"0\" w=\"300\" h=\"100\">{split}</panel>", new Binding(), _ => (1u, 16, 16)));
    }
```

- [ ] **Step 2: Run to see them fail**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupSplitTests"`
Expected: build error — `UiMarkupSplit` does not exist.

- [ ] **Step 3: The split** — create `src/AcDream.App/UI/UiMarkupSplit.cs`:

```csharp
using System.Globalization;
using AcDream.App.UI.Markup;

namespace AcDream.App.UI;

/// <summary>
/// A markup splitter: two panes with a bar between them that the player drags.
/// Horizontal places the panes side by side (the bar is vertical); vertical
/// stacks them. The position is the first pane's size in pixels, clamped to
/// <see cref="Min"/>/<see cref="Max"/> and to the room the split has. The split
/// lays its panes and bar out itself (they have no anchors), so when it
/// resizes the first pane keeps its size and the second takes the rest.
/// </summary>
public sealed class UiMarkupSplit : UiElement
{
    public const float BarSize = 5f;

    /// <summary>How far one arrow key press moves the bar.</summary>
    public const float KeyStep = 8f;

    public bool Horizontal { get; }
    public UiMarkupSplitBar Bar { get; }
    public UiElement? First { get; private set; }
    public UiElement? Second { get; private set; }

    public float Min { get; set; }
    public float? Max { get; set; }

    /// <summary>The markup's <c>at</c> (or half the room): where unbound state starts, and what isn't remembered.</summary>
    public float DefaultPosition { get; set; }

    /// <summary>Bound position: when set, the plugin owns it and the layout follows it.</summary>
    public Func<float>? PositionSource { get; set; }

    /// <summary>Called with each new position a drag or key press asks for.</summary>
    public Action<float>? Changed { get; set; }

    internal RememberedValue? Remembered { get; set; }

    public bool IsRemembered => Remembered?.IsActive == true;

    /// <summary>The first pane's size as last laid out (where the bar is).</summary>
    public float LaidOutPosition { get; private set; }

    private float? _hostPosition;
    private float _asked;
    private float _moveStart;

    public UiMarkupSplit(bool horizontal)
    {
        Horizontal = horizontal;
        ClickThrough = true;
        Bar = new UiMarkupSplitBar(this)
        {
            AcceptsFocus = true,
            FocusOnMouseClick = false,
            TabStop = true,
            Anchors = AnchorEdges.None,
        };
        AddChild(Bar);
    }

    /// <summary>Adopts the two built panes; the split sizes them from now on.</summary>
    internal void SetPanes(UiElement first, UiElement second)
    {
        First = first;
        Second = second;
        first.Anchors = AnchorEdges.None;
        second.Anchors = AnchorEdges.None;
    }

    /// <summary>The position asked for (bound, remembered or default), before clamping.</summary>
    public float Position => PositionSource?.Invoke() ?? HostPosition;

    private float HostPosition
    {
        get
        {
            if (Remembered is { } remembered && remembered.TryRefresh(out string? text))
                _hostPosition = Parse(text) ?? DefaultPosition;
            return _hostPosition ??= DefaultPosition;
        }
    }

    private static float? Parse(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value >= 0
            ? value
            : null;

    private static string Encode(float position) =>
        ((int)MathF.Round(position)).ToString(CultureInfo.InvariantCulture);

    private float Room => MathF.Max(0f, (Horizontal ? Width : Height) - BarSize);

    /// <summary>Whole pixels, within min/max and the room.</summary>
    internal float Clamp(float position)
    {
        if (float.IsNaN(position))
            position = DefaultPosition;
        float room = Room;
        float hi = MathF.Min(Max ?? room, room);
        float lo = MathF.Min(Min, hi);
        return MathF.Round(Math.Clamp(position, lo, hi));
    }

    internal void Layout()
    {
        float at = Clamp(Position);
        LaidOutPosition = at;
        float second = at + BarSize;
        if (Horizontal)
        {
            Place(First, 0f, 0f, at, Height);
            Place(Bar, at, 0f, BarSize, Height);
            Place(Second, second, 0f, MathF.Max(0f, Width - second), Height);
        }
        else
        {
            Place(First, 0f, 0f, Width, at);
            Place(Bar, 0f, at, Width, BarSize);
            Place(Second, 0f, second, Width, MathF.Max(0f, Height - second));
        }
    }

    private static void Place(UiElement? element, float x, float y, float w, float h)
    {
        if (element is null)
            return;
        element.Left = x;
        element.Top = y;
        element.Width = w;
        element.Height = h;
    }

    protected override void OnTick(double deltaSeconds) => Layout();

    // Runs after the parent's anchor pass sized the split and before the panes'
    // children are anchored, so a resize reaches the panes in the same frame.
    protected override void OnDraw(UiRenderContext ctx) => Layout();

    /// <summary>A drag or key press starts from where the bar is now (a fresh read).</summary>
    internal void BeginMove()
    {
        Layout();
        _asked = LaidOutPosition;
        _moveStart = LaidOutPosition;
    }

    /// <summary>Asks for a new position; each distinct whole pixel is reported once.</summary>
    internal void MoveTo(float position)
    {
        float at = Clamp(position);
        if (at == _asked)
            return;
        if (PositionSource is not null && Changed is null)
            return;
        _asked = at;
        if (PositionSource is null)
            _hostPosition = at;
        Changed?.Invoke(at);
        Layout();
    }

    /// <summary>The player let go (or pressed a key): remember an unbound change.</summary>
    internal void EndMove()
    {
        if (PositionSource is not null || _asked == _moveStart)
            return;
        string encoded = Encode(_asked);
        Remembered?.Remember(encoded == Encode(DefaultPosition) ? null : encoded);
    }

    internal void Nudge(int direction)
    {
        BeginMove();
        MoveTo(LaidOutPosition + direction * KeyStep);
        EndMove();
    }
}
```

- [ ] **Step 4: The bar** — create `src/AcDream.App/UI/UiMarkupSplitBar.cs`:

```csharp
using System.Numerics;
using Silk.NET.Input;

namespace AcDream.App.UI;

/// <summary>
/// A split's bar: the player drags it, or moves it with the arrow keys along
/// its axis while it has focus. Shows the retail resize cursor while enabled
/// and highlights on hover and while dragged.
/// </summary>
public sealed class UiMarkupSplitBar : UiElement
{
    public static readonly Vector4 ClassicFill = UiCheckLamp.UncheckedOuter;
    public static readonly Vector4 ClassicGrip = UiCheckLamp.UncheckedInner;
    public static readonly Vector4 ClassicHot = UiMarkupExpanderHeader.ClassicArrow;
    public static readonly Vector4 FocusColor = UiMarkupExpanderHeader.FocusColor;

    private static readonly IReadOnlyDictionary<string, UiCursorMedia> NoCursor =
        new Dictionary<string, UiCursorMedia>();

    private readonly UiMarkupSplit _owner;
    private readonly IReadOnlyDictionary<string, UiCursorMedia> _resizeCursor;
    private bool _cursorShown;
    private bool _focused;
    private bool _hovered;
    private bool _dragging;
    private float _grab;

    public PluginUiPalette? ThemePalette { get; set; }

    internal UiMarkupSplitBar(UiMarkupSplit owner)
    {
        _owner = owner;
        CapturesPointerDrag = true;
        RetailCursorCatalog.TryGetWindowControlCursor(
            owner.Horizontal ? CursorFeedbackKind.ResizeHorizontal : CursorFeedbackKind.ResizeVertical,
            out UiCursorMedia cursor);
        _resizeCursor = new Dictionary<string, UiCursorMedia> { [""] = cursor };
        SetStateCursors(_resizeCursor);
        _cursorShown = true;
    }

    private bool Live => Enabled && _owner.Enabled;

    /// <summary>Hovered or dragged: drawn highlighted.</summary>
    public bool Hot => Live && (_dragging || _hovered);

    /// <summary>The bar takes the hover, so it shows its split's tooltip.</summary>
    public override string? GetTooltipText() => _owner.GetTooltipText();

    protected override void OnTick(double deltaSeconds)
    {
        bool live = Live;
        if (live == _cursorShown)
            return;
        SetStateCursors(live ? _resizeCursor : NoCursor);
        _cursorShown = live;
    }

    private float Along(in UiEvent e) => _owner.Horizontal ? e.Data1 : e.Data2;

    private float Offset => _owner.Horizontal ? Left : Top;

    private int Direction(Key key) => (_owner.Horizontal, key) switch
    {
        (true, Key.Left) or (false, Key.Up) => -1,
        (true, Key.Right) or (false, Key.Down) => 1,
        _ => 0,
    };

    public override bool OnEvent(in UiEvent e)
    {
        switch (e.Type)
        {
            case UiEventType.FocusGained when TabStop:
                _focused = true;
                return true;
            case UiEventType.FocusLost when TabStop:
                _focused = false;
                return true;
            case UiEventType.HoverEnter:
                _hovered = true;
                return true;
            case UiEventType.HoverLeave:
                _hovered = false;
                return true;
            case UiEventType.MouseDown:
                if (!Live)
                    return true;
                _dragging = true;
                _grab = Along(e);
                _owner.BeginMove();
                return true;
            case UiEventType.MouseMove:
                if (_dragging)
                    _owner.MoveTo(Offset + Along(e) - _grab);
                return true;
            case UiEventType.MouseUp:
            case UiEventType.CaptureChanged:
                if (_dragging)
                {
                    _dragging = false;
                    _owner.EndMove();
                }
                return true;
            case UiEventType.Click:
            case UiEventType.DoubleClick:
                return true;
            case UiEventType.KeyDown when _focused && Live:
                int direction = Direction((Key)e.Data0);
                if (direction == 0)
                    return false;
                _owner.Nudge(direction);
                return true;
        }
        return false;
    }

    private Vector4 Fade(Vector4 color) =>
        Live ? color : color with { W = color.W * UiMarkupToggle.DisabledAlpha };

    protected override void OnDraw(UiRenderContext ctx)
    {
        if (Width <= 0f || Height <= 0f)
            return;
        Vector4 fill = Fade(ThemePalette?.Field ?? ClassicFill);
        Vector4 grip = Fade(Hot
            ? ThemePalette?.Accent ?? ClassicHot
            : ThemePalette?.Border ?? ClassicGrip);
        ctx.DrawFill(0f, 0f, Width, Height, fill);
        if (_owner.Horizontal)
            ctx.DrawFill(MathF.Floor((Width - 1f) * 0.5f), 0f, 1f, Height, grip);
        else
            ctx.DrawFill(0f, MathF.Floor((Height - 1f) * 0.5f), Width, 1f, grip);
        if (_focused)
            ctx.DrawRectOutline(0f, 0f, Width, Height, FocusColor, 1f);
    }
}
```

If `RetailCursorCatalog` or `CursorFeedbackKind` is not visible from here (both are in `AcDream.App.UI`; `RetailCursorCatalog` is `internal`, which is fine inside the same assembly), no extra `using` is needed.

- [ ] **Step 5: The builder** — create `src/AcDream.App/UI/Markup/MarkupSplit.cs`:

```csharp
using System.Globalization;
using System.Xml.Linq;

namespace AcDream.App.UI.Markup;

internal static class MarkupSplit
{
    public static UiMarkupSplit Build(XElement el, object binding, MarkupBuildContext context)
    {
        bool horizontal = (string?)el.Attribute("orientation") switch
        {
            null or "horizontal" => true,
            "vertical" => false,
            var other => throw new FormatException(
                $"<split orientation=\"{other}\"> must be horizontal or vertical"),
        };
        XElement[] panes = el.Elements().ToArray();
        if (panes.Length != 2 || panes.Any(pane => pane.Name.LocalName != "group"))
            throw new FormatException("<split> must contain exactly two <group> elements");

        var split = new UiMarkupSplit(horizontal)
        {
            Left = MarkupDocument.F(el, "x"),
            Top = MarkupDocument.F(el, "y"),
            Width = MarkupDocument.F(el, "w"),
            Height = MarkupDocument.F(el, "h"),
            Min = Length(el, "min") ?? 0f,
            Max = Length(el, "max"),
            Changed = MarkupBindings.BindFloatAction((string?)el.Attribute("onchange"), binding, "split onchange"),
        };
        if (split.Max < split.Min)
            throw new FormatException($"<split max=\"{split.Max}\"> must not be less than min=\"{split.Min}\"");
        float room = MathF.Max(0f, (horizontal ? split.Width : split.Height) - UiMarkupSplit.BarSize);
        split.DefaultPosition = Length(el, "at") ?? MathF.Floor(room / 2f);

        string? positionAttr = (string?)el.Attribute("position");
        if (positionAttr is not null)
            split.PositionSource = MarkupBindings.BindRequiredFloatReader(positionAttr, binding, "split position");

        MarkupDocument.ApplyCommon(split, el, binding);
        // The bar takes the hover, so it carries the tooltip's popup settings;
        // its text comes from the split (see UiMarkupSplitBar.GetTooltipText).
        UiMarkupSplitBar bar = split.Bar;
        bar.AuthoredTooltipEnabled = split.AuthoredTooltipEnabled;
        bar.AuthoredTooltipRootElementId = split.AuthoredTooltipRootElementId;
        bar.AuthoredTooltipLayoutDid = split.AuthoredTooltipLayoutDid;
        bar.AuthoredTooltipDelaySeconds = split.AuthoredTooltipDelaySeconds;
        if (split.PositionSource is null)
            split.Remembered = new RememberedValue(context.ControlState, split.Name, "position");

        if (context.AddChild is not { } addChild)
            throw new InvalidOperationException("<split> needs a build context that can add children");
        addChild(split, panes[0]);
        UiElement first = split.Children[^1];
        addChild(split, panes[1]);
        UiElement second = split.Children[^1];
        split.SetPanes(first, second);
        // Lay out now, so the panes' children record their anchors against the
        // pane sizes they will actually have.
        split.Layout();
        return split;
    }

    private static float? Length(XElement el, string name)
    {
        string? text = (string?)el.Attribute(name);
        if (text is null)
            return null;
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            || !float.IsFinite(value) || value < 0f)
        {
            throw new FormatException($"<split {name}=\"{text}\"> must be a number of pixels, 0 or more");
        }
        return value;
    }
}
```

`AuthoredTooltip*` setters are on `UiElement` (the expander builder sets them on its header the same way). If any of them is not settable from here, mirror exactly what `MarkupExpander.Build` does.

- [ ] **Step 6: Register the element** — in `MarkupDocument.AddElement`, right after the `case "expander":` block:

```csharp
            case "split":
                parent.AddChild(MarkupSplit.Build(el, binding, Context()));
                break;
```

- [ ] **Step 7: Run**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupSplitTests|FullyQualifiedName~AcDream.App.Tests.UI.Markup"`
Expected: PASS. If a geometry test is off by the whole room or a pane, check the order `Layout()` runs relative to `ApplyAnchor` (the split's own anchor first, then `OnDraw` → `Layout`, then the panes' children) before changing expected values; the numbers in the tests follow the contract above.

- [ ] **Step 8: Commit**

```bash
git checkout -- '*.lock.json' 2>/dev/null
git add src/AcDream.App/UI/UiMarkupSplit.cs src/AcDream.App/UI/UiMarkupSplitBar.cs \
        src/AcDream.App/UI/Markup/MarkupSplit.cs src/AcDream.App/UI/MarkupDocument.cs \
        tests/AcDream.App.Tests/UI/MarkupSplitTests.cs
git commit -m "feat(ui): <split> markup element

Two panes and a draggable bar; the position is the plugin's when bound
and remembered per character when not.

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 3: Theme and drawing

**Goal:** The bar takes the plugin theme, and its Classic and themed drawing (normal, hot, disabled, focused) is pinned by tests.

**Files:**
- Modify: `src/AcDream.App/UI/PluginMarkupTheme.cs`
- Test: `tests/AcDream.App.Tests/UI/MarkupSplitTests.cs`

**Acceptance Criteria:**
- [ ] Classic: the bar draws one `ClassicFill` quad (5×200) and one 1 px wide `ClassicGrip` quad; hovered (pointer moved over it through `UiRoot`) the grip is `ClassicHot` instead.
- [ ] Disabled: fill and grip drawn at `DisabledAlpha`; no full-alpha `ClassicFill`.
- [ ] Focused: 4 `FocusColor` quads.
- [ ] Under `theme="plugin"` (Moss) `Bar.ThemePalette` is `PluginUiPalette.Moss`, the bar fills with `Moss.Field` and the grip is `Moss.Border`; a `<toggle>` inside a pane gets the palette.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupSplitTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the tests** — append inside `MarkupSplitTests`:

```csharp
    [Fact]
    public void Split_ClassicBarDrawsAFillAndAGripThatLightsOnHover()
    {
        var (root, _, split) = Mount("", new Binding());
        var renderer = MarkupDrawProbe.Draw(split.Bar);
        var fill = Assert.Single(MarkupDrawProbe.QuadBounds(renderer, UiMarkupSplitBar.ClassicFill));
        Assert.Equal((5f, 200f), (fill.W, fill.H));
        var grip = Assert.Single(MarkupDrawProbe.QuadBounds(renderer, UiMarkupSplitBar.ClassicGrip));
        Assert.Equal(1f, grip.W);

        root.OnMouseMove(212, 100);
        Assert.True(split.Bar.Hot);
        var hot = MarkupDrawProbe.Draw(split.Bar);
        Assert.Equal(1, MarkupDrawProbe.CountQuads(hot, UiMarkupSplitBar.ClassicHot));
        Assert.Equal(0, MarkupDrawProbe.CountQuads(hot, UiMarkupSplitBar.ClassicGrip));
    }

    [Fact]
    public void Split_DisabledBarDrawsFaded()
    {
        var (_, _, split) = Mount("enabled=\"false\"", new Binding());
        var renderer = MarkupDrawProbe.Draw(split.Bar);
        var faded = UiMarkupSplitBar.ClassicFill with { W = UiMarkupToggle.DisabledAlpha };
        Assert.Equal(1, MarkupDrawProbe.CountQuads(renderer, faded));
        Assert.Equal(0, MarkupDrawProbe.CountQuads(renderer, UiMarkupSplitBar.ClassicFill));
    }

    [Fact]
    public void Split_FocusDrawsAnOutline()
    {
        var (_, _, split) = Mount("", new Binding());
        split.Bar.OnEvent(new UiEvent { Type = UiEventType.FocusGained });
        var renderer = MarkupDrawProbe.Draw(split.Bar);
        Assert.Equal(4, MarkupDrawProbe.CountQuads(renderer, UiMarkupSplitBar.FocusColor));
    }

    [Fact]
    public void Split_PluginThemeReachesTheBarAndThePanes()
    {
        var themes = new PluginUiThemeSettings { Theme = PluginUiTheme.Moss };
        var panel = MarkupDocument.Build("""
            <panel theme="plugin" x="0" y="0" w="500" h="300">
              <split x="10" y="10" w="405" h="200">
                <group><toggle x="4" y="4" w="120" h="16" text="Inner" checked="{LeftOn}"/></group>
                <group/>
              </split>
            </panel>
            """, new ThemedBinding(), _ => (1u, 16, 16), themes: themes);
        var split = panel.Children.OfType<UiMarkupSplit>().Single();
        Assert.Same(PluginUiPalette.Moss, split.Bar.ThemePalette);
        var renderer = MarkupDrawProbe.Draw(split.Bar);
        Assert.Equal(1, MarkupDrawProbe.CountQuads(renderer, PluginUiPalette.Moss.Field));
        Assert.Equal(1, MarkupDrawProbe.CountQuads(renderer, PluginUiPalette.Moss.Border));
        var toggle = Assert.IsType<UiMarkupToggle>(split.First!.Children[0]);
        Assert.Same(PluginUiPalette.Moss, toggle.ThemePalette);
    }

    private sealed class ThemedBinding
    {
        public bool LeftOn { get; set; }
    }
```

If `Moss.Field` and `Moss.Border` happen to be equal, assert `CountQuads(renderer, Moss.Field) == 2` instead (check the values in `PluginUiThemeSettings.cs`).

- [ ] **Step 2: Run** — the Classic draw tests pass already; the theme test FAILS (`ThemePalette` is null).

- [ ] **Step 3: Theme** — in `PluginMarkupTheme.Register`'s second switch, after the `case UiMarkupExpander expander:` section:

```csharp
            case UiMarkupSplit split:
                panel.AddThemeAction(p => split.Bar.ThemePalette = p);
                break;
```

(The split has no text, so it needs no font case.)

- [ ] **Step 4: Run, then commit** — same filter → PASS.

```bash
git checkout -- '*.lock.json' 2>/dev/null
git add src/AcDream.App/UI/PluginMarkupTheme.cs tests/AcDream.App.Tests/UI/MarkupSplitTests.cs
git commit -m "feat(ui): plugin theme for <split>

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 4: Docs

**Goal:** `docs/plugin-ui-markup.md` documents `<split>`.

**Files:**
- Modify: `docs/plugin-ui-markup.md`

**Acceptance Criteria:**
- [ ] Elements table has a `split` row after `expander`; Bindings table has `split position` and `split onchange` rows after the expander rows.
- [ ] The focus/keyboard paragraph says a focused split bar moves with the arrow keys along its axis.
- [ ] A `## Split` section after `## Expander` covers: two `<group>` children (their geometry is the split's), orientation, the 5 px bar and dragging, `at`/`min`/`max` and clamping, resize behaviour, bound vs unbound (remembered per character when named, at drag end, forgotten at `at`), `onchange` firing per pixel during a drag, keyboard, disabled, and the older-host line.

**Verify:** `grep -c 'split' docs/plugin-ui-markup.md` → at least 10.

**Steps:**

- [ ] **Step 1: Tables**

Elements table, after the `expander` row:

```markdown
| `split` | Two panes with a bar between them the player drags | `name x y w h orientation position onchange at min max` |
```

Bindings table, after the expander rows:

```markdown
| `split position` | `float`, the first pane's size; when bound, the plugin owns it | throws |
| `split onchange` | `Action<float>`, the size asked for | throws |
```

- [ ] **Step 2: Keyboard** — after the sentence listing what Enter or Space activates, add: "A focused `split` bar moves 8 px with the arrow keys along its axis (Left/Right side by side, Up/Down stacked)."

- [ ] **Step 3: Section** — after the `## Expander` section (before `## Resizable panels and anchors`):

````markdown
## Split

```xml
<split name="main" x="0" y="0" w="400" h="300" min="80" max="320">
  <group><list x="0" y="0" w="100" h="300" anchor="left top right bottom" items="{Names}"/></group>
  <group><label x="8" y="8" text="Details"/></group>
</split>
```

A `<split>` holds exactly two `<group>` elements with a 5 px bar between
them. With `orientation="horizontal"` (the default) the groups sit side by
side; with `vertical` they are stacked. The split sizes and places its
groups, so their own `x y w h` and `anchor` are ignored; anchor the elements
inside them to fill a group.

The player drags the bar to share the space out. Its position is the first
group's size in pixels, kept between `min` (default 0) and `max` (default
all of it). When the split itself resizes, the first group keeps its size
and the second takes the rest.

- Without `position` the client keeps the position. It starts at `at`
  (default half). If the split has a `name`, the client remembers where the
  player left the bar per character, when they let go of it, and forgets it
  again when it is back at `at`.
- With `position="{Float}"` the plugin keeps it: dragging only calls
  `onchange` with the size asked for, and the split follows the property.
  Without `onchange` it only displays. Bound positions are never remembered
  by the client.
- `onchange` is called for every pixel the bar moves during a drag, not
  only when the player lets go.

A disabled split can't be dragged and draws its bar faded. On a host older
than splits, a `<split>` fails the build of its window as unknown.
````

- [ ] **Step 4: Commit**

```bash
git add docs/plugin-ui-markup.md
git commit -m "docs(ui): document <split>

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 5: Series gate

**Goal:** Release build clean; no new failures in App, Plugin or UI.Abstractions tests; every commit builds.

**Files:** none changed.

**Acceptance Criteria:**
- [ ] `dotnet build AcDream.slnx -c Release` → 0 errors, 0 warnings.
- [ ] App + Plugin failures ⊆ `mc-base.txt`; UI.Abstractions failures ⊆ `mc-uiabs-base.txt` (empty).
- [ ] Each of the series' commits builds and passes `UI.Markup|MarkupSplitTests|UiFieldTests|RememberedValueTests` on its own.
- [ ] Nothing under `docs/superpowers/` on the branch.

**Verify:** both `comm -13` comparisons print nothing.

**Steps:**

- [ ] **Step 1: Release build and tests**

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls
T=/private/tmp/claude-501/-Users-davidsmith-code-OpenAC-OpenAC
norm() { grep -E '^[[:space:]]+Failed [A-Za-z]' | sed -E 's/^[[:space:]]+Failed //; s/ \[[^]]*\]$//' | sort -u; }
dotnet build AcDream.slnx -c Release 2>&1 | grep -E ': error|: warning|Warning\(s\)|Error\(s\)' | sort -u
for p in tests/AcDream.App.Tests/AcDream.App.Tests.csproj tests/AcDream.Plugin.Tests/AcDream.Plugin.Tests.csproj; do
  dotnet test "$p" -c Release 2>&1
done | norm > $T/mc6-after.txt
dotnet test tests/AcDream.UI.Abstractions.Tests/AcDream.UI.Abstractions.Tests.csproj -c Release 2>&1 | norm > $T/mc6-uiabs-after.txt
echo "new App/Plugin:"; comm -13 $T/mc-base.txt $T/mc6-after.txt
echo "new UI.Abstractions:"; comm -13 $T/mc-uiabs-base.txt $T/mc6-uiabs-after.txt
git checkout -- '*.lock.json' 2>/dev/null; git status --short
```
Expected: `0 Warning(s)`, `0 Error(s)`, nothing after either "new" line.

- [ ] **Step 2: Each commit builds alone**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
for c in $(git rev-list --reverse 13aa1206..feat/markup-controls); do
  git worktree add -q --detach .worktrees/mc-commit-check $c
  (cd .worktrees/mc-commit-check && dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release \
     --filter "FullyQualifiedName~AcDream.App.Tests.UI.Markup|FullyQualifiedName~AcDream.App.Tests.UI.UiFieldTests|FullyQualifiedName~RememberedValueTests" 2>&1 \
     | grep -E ': error|: warning|Passed!|Failed!' | sort -u | sed "s/^/$(git rev-parse --short $c) /")
  git worktree remove --force .worktrees/mc-commit-check
done
```
Expected: one `Passed!` per commit, no `error`/`warning`. (`MarkupSplitTests` matches the `UI.Markup` filter.)

- [ ] **Step 3: Branch contents**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls
git log --oneline 13aa1206..HEAD
git diff --stat 391e709a..HEAD | grep superpowers || echo "no superpowers docs"
```
Expected: the 4 commits of Tasks 1-4 (plus any review-fix commits); "no superpowers docs".

No commit.
