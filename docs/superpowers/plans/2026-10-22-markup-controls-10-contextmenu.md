# Markup controls, plan 10: `<contextmenu>` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Any markup element can have one `<contextmenu>` child. A right click on the element opens a popup menu at the cursor; the player picks an item with the mouse or Up/Down/Enter, and the plugin hears the item index. On a `<list>`, the right click first selects the row under the cursor and tells the plugin which row it was.

**Architecture:**
- **Root plumbing.** A small interface, `IUiContextMenu` (`TryOpen(screenX, screenY)`), and a `UiElement.ContextMenu` property.
  - `UiRoot` bubbles a right click as before. After each element leaves it unhandled, the root offers it to that element's `ContextMenu`, so the innermost menu wins.
  - `UiRoot.ClearSubtreeOwnership` (run when an element hides or leaves the tree) now also dismisses the active popup if it lives in that subtree. This applies to `<menu>` too.
- **Widget.** `UiMarkupContextMenu` is a child of the element it belongs to (the owner). It takes no room: it sits at the owner's origin with no size and `Anchors = None`.
  - When it opens it reads the items once, measures itself, and places its popup at the cursor. If the popup would cross the right or bottom screen edge it flips to the other side of the cursor, then shifts onto the screen.
  - It keeps the popup rectangle in the owner's coordinates and draws it in the overlay pass (`ExpandsClipForPopup`), the way `UiMenu` does.
  - Open, it holds the root's popup slot (`SetActivePopup`) and the keyboard focus. On close it hands the focus back to whatever had it.
- **Builder.** `Markup/MarkupContextMenu.Attach` runs after every element is built (in the tail of `MarkupDocument.AddElement`). It binds `items enabled onselect onopen` and adds the menu to the built element.
  - `case "contextmenu"` in `AddElement` builds nothing (containers pass it through).
  - `<list>` and `<split>` leave `<contextmenu>` out of their child lists.
  - A `<contextmenu>` directly in `<panel>` or in a `<column>` fails the build.
- **List.** A new partial, `UiMarkupList.ContextMenu.cs`, adds `PrepareContextMenu(x, y)`. It selects the row under the point (unless the row is already in a multi selection) and returns the plugin row, or −1. The builder wires it into the menu's `Opening` hook for lists, then calls `onopen` with the result.

**Status:** DONE 2026-10-22 — feat 9b4b5523..930a2f71 (6 commits: 5 tasks + final-review fix; each task commit builds; gate = baseline 162, 0 warnings). Plan bugs fixed in execution: widget property is `ItemsEnabledSource` and constant `MinPopupWidth` (plan's `EnabledSource`/`MinWidth` clash with UiElement). Final review fix: TryOpen re-checks owner/root/availability after `Opening` (plugin code may hide/detach the owner), already-open guard, popup origin = menu's own ScreenPosition, Close uses the root captured at open. Deferred (not blocking): docs paragraphs not rewrapped; `<contextmenu>` common attributes silently ignored (markup never rejects unknown attributes); ContextMenuRowAt duplicates OnEventColumns row math; pre-existing resize-on-pick near a resizable window edge (UiRoot.OnMouseDown checks HitEdges before CapturesPointerDrag; shared with UiMenu); test gaps (scrolled list, scrollbar area, collapsed tree, ModernFont, other popup opening, expander/number/radio owners). Fork merge: check the zero-size menu child inside flex containers/scroll panels (must not become a flex item).

**Tech Stack:** C# / .NET 10, xUnit, headless `UiRoot` + `RecordingGpuDevice` tests.

**Spec:** `docs/superpowers/specs/2026-10-21-markup-controls-design.md` (branch `docs/markup-controls-spec`), "Shared rules" and section 11. It builds on plans 7–9 (`UiMarkupListViewMap`, `UiMarkupList.Selection.cs` with `_anchorRow`/`_leadRow`, `RefreshSelectionRows`).

## Global Constraints

- **Workspace.** Worktree `/Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls`, branch `feat/markup-controls` (HEAD 9b4b5523, from upstream 391e709a).
  - Never merge fork `main` in, and never add `docs/superpowers/` or `.superpowers/` files.
  - Push only when the controller says so.
- **Build hygiene.** Every shell runs `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH` first. After building, run `git checkout -- '*.lock.json'`; never commit lock files.
- **Commits.** Every commit must build and pass its own tests. Commit messages end with `Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC`.
- **Helper conventions.**
  - `BindRequired*` helpers throw when the attribute is absent or mistyped.
  - Other `Bind*` helpers return null when absent and throw when mistyped.
  - `context` is `"<element> <attribute>"`, here `"contextmenu items"`, `"contextmenu onselect"` and so on.
  - Every element case still adds exactly one element to its parent. The context menu is added to the built element, not to the parent.
- **Plugin rows.** Every row index the plugin sees (`onopen`, the selection a right click makes) is a plugin row, never a view row.
- **`<contextmenu>` contract.**
  - A child of the element it belongs to; at most one per element. It takes no children and no common attributes (no `x y w h name visible enabled tooltip anchor`).
  - `items`: required. A literal `a|b|-|c` (no empty item) or a bound `IEnumerable<string>`. `-` is a separator.
  - `enabled`: optional bound `IEnumerable<bool>`, one per item. An item past its end is enabled. Separators are never enabled.
  - `onselect`: required `Action<int>`, the item index (separators count).
  - `onopen`: optional `Action<int>`, called before the items are read. On a `<list>` it gets the plugin row under the cursor, or −1. On any other element it gets −1.
  - `items` and `enabled` are read once, when the menu opens.
  - A right click on the owner (or on a descendant that leaves it unhandled and has no menu of its own) opens it. A drag with the right button does not (the root's existing 3 px click rule).
  - A disabled or hidden owner never opens it. If the owner (or an ancestor) is disabled while the menu is open, it closes on the next tick. If the owner hides or leaves the tree, it closes at once.
  - An empty item list does not open, and the right click goes on to any enclosing element's menu.
  - A `<contextmenu>` directly in `<panel>`, in a `<column>`, with a child, or a second one on the same element fails the build.
- **Popup layout** (`UiMarkupContextMenu` constants):
  - items 18 px tall; separators 7 px with a 1 px line at y + 3;
  - 3 px inset above the first row and below the last;
  - text 8 px from the left;
  - width = max(80, widest item text + 16), rounded up; height = 3 + rows + 3.
- **Placement.** Each axis on its own: start at the cursor; if `cursor + size > screen`, start at `cursor − size`; then clamp to `[0, screen − size]`, and to 0 if the popup is larger than the screen. `screen` is the root's `Width`/`Height`.
- **Open behaviour.**
  - Opening claims the root's popup slot, which closes any other popup.
  - It saves the current keyboard focus and focuses the menu. While open, the menu swallows every key, so keys don't reach the game; Escape, Up, Down, Enter and keypad Enter act.
  - Up/Down move the highlight to the next enabled item, wrapping. With nothing highlighted, Down goes to the first enabled item and Up to the last.
  - Enter picks the highlighted item, if there is one.
  - Hovering an enabled item highlights it; hovering anything else clears the highlight.
  - A left press on an enabled item picks it. A press on a separator or disabled item does nothing, and the menu stays open. A press outside closes the menu, and the root swallows the press.
  - Picking closes the menu first, then calls `onselect`.
  - Closing by Escape, a pick, an outside press, another popup opening, the owner hiding or the owner being disabled hands the focus back to the saved element (the root drops it if that element is no longer available). Losing the focus to something else closes the menu without handing it back.
- **List right click** (before `onopen`):
  - over a row: in single mode, call `onchange(row)` unless it is already the selected row. In multi mode, if the row is not in the selection, call `onchange([row])` and move the anchor and lead to it; if it is already selected, leave the selection alone.
  - over the header, the scrollbar or empty space: change nothing; `onopen(-1)`.
  - a disabled list never gets here (hit tests skip disabled elements).
- **Looks.** Classic uses `UiMenu`'s plain colours:
  - background (0, 0, 0, 0.92), border (0.46, 0.37, 0.16, 1), text (0.91, 0.87, 0.76, 1), disabled text (0.5, 0.5, 0.5, 1), highlight (0.40, 0.33, 0.14, 0.95);
  - separator `UiCheckLamp.UncheckedInner`;
  - text outlined in Classic only.

  Under `theme="plugin"`: `Field` background, `Border` border and separator, `Text` text, `Muted` disabled text, `Selected` highlight, and the panel's modern font. The popup is opaque (`PushAlphaAbsolute(1f)`) whatever the window opacity.

**User decisions (already made):**
- One branch, one commit series per control, upstream-portable.
- Submenus are out of scope (spec "Out of scope").
- Non-text controls don't take keyboard focus on click; focus comes by Tab from another focused control (user, 2026-10-22: leave as is for now). The open menu does take focus, but only while it is open, and it gives it back.

**Plan choices (details the spec leaves open; flag in the final review if wrong):**
- `onopen` works on every element (−1 off a list), not only on lists.
- The open menu takes keyboard focus and swallows keys, so the game doesn't also act on Up/Down/Enter/Escape.
- Clicking a separator or a disabled item keeps the menu open. Up/Down wrap.
- Hiding any popup's owner now closes the popup, for `<menu>` as well.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/AcDream.App/UI/IUiContextMenu.cs` (new) | `TryOpen(screenX, screenY)` |
| `src/AcDream.App/UI/UiElement.cs` | `internal IUiContextMenu? ContextMenu` |
| `src/AcDream.App/UI/UiRoot.cs` | `BubbleRightClick`; popup dismissal in `ClearSubtreeOwnership` |
| `src/AcDream.App/UI/UiMarkupContextMenu.cs` (new) | The widget: open/place/draw/mouse/keys/focus |
| `src/AcDream.App/UI/Markup/MarkupContextMenu.cs` (new) | Builder: binds attributes, attaches to the owner |
| `src/AcDream.App/UI/MarkupDocument.cs` | Tail attach, `case "contextmenu"`, list filtering, panel/column rejection, `BindBoolList`/`BindRequiredIntAction` → internal |
| `src/AcDream.App/UI/Markup/MarkupSplit.cs` | Panes leave out `<contextmenu>` |
| `src/AcDream.App/UI/PluginMarkupTheme.cs` | Font + palette for the menu |
| `src/AcDream.App/UI/UiMarkupList.ContextMenu.cs` (new) | `PrepareContextMenu`, row under a point |
| `tests/AcDream.App.Tests/UI/UiContextMenuRoutingTests.cs`, `UiMarkupContextMenuTests.cs`, `MarkupContextMenuTests.cs`, `MarkupListContextMenuTests.cs` (new) | Tests |
| `tests/AcDream.App.Tests/UI/MarkupDrawProbe.cs` | `DrawOverlays` helper |
| `docs/plugin-ui-markup.md` | Context menu docs |

---

### Task 1: Root routing for context menus

**Goal:** `UiRoot` offers a right click to each element's `ContextMenu` after the element leaves it unhandled, innermost first, and hiding or removing a popup's owner dismisses the popup.

**Files:**
- Create: `src/AcDream.App/UI/IUiContextMenu.cs`
- Modify: `src/AcDream.App/UI/UiElement.cs` (beside `PointerRegion`)
- Modify: `src/AcDream.App/UI/UiRoot.cs` (`OnMouseUp` right-click branch, `ClearSubtreeOwnership`, new `BubbleRightClick` beside `BubbleEvent`)
- Test: `tests/AcDream.App.Tests/UI/UiContextMenuRoutingTests.cs`

**Acceptance Criteria:**
- [ ] A right click on a child with a menu opens only the child's menu, at the release point in canvas coordinates.
- [ ] A child without a menu passes the right click to its parent's menu.
- [ ] An element whose `OnEvent` handles `RightClick` opens no menu.
- [ ] A menu whose `TryOpen` returns false passes the click on to the next menu outwards.
- [ ] A right drag (release more than 3 px from the press) and a left click open nothing.
- [ ] An open `UiMenu` closes when its panel is hidden, and stays closed when the panel is shown again.
- [ ] All existing `UiMenu*` tests still pass.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~UiContextMenuRoutingTests|FullyQualifiedName~UiMenu"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — create `tests/AcDream.App.Tests/UI/UiContextMenuRoutingTests.cs`:

```csharp
using System.Collections.Generic;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public sealed class UiContextMenuRoutingTests
{
    private sealed class RecordingMenu : IUiContextMenu
    {
        public bool Result { get; set; } = true;
        public List<(int X, int Y)> Opens { get; } = new();
        public bool TryOpen(int screenX, int screenY)
        {
            Opens.Add((screenX, screenY));
            return Result;
        }
    }

    private sealed class Box : UiElement
    {
        public bool HandlesRight { get; set; }
        public override bool HandlesClick => true;
        public override bool OnEvent(in UiEvent e) =>
            HandlesRight && e.Type == UiEventType.RightClick;
    }

    // outer (0,0 200x200) > inner (50,50 50x50)
    private static (UiRoot Root, Box Outer, Box Inner) Tree()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var outer = new Box { Left = 0, Top = 0, Width = 200, Height = 200 };
        var inner = new Box { Left = 50, Top = 50, Width = 50, Height = 50 };
        outer.AddChild(inner);
        root.AddChild(outer);
        return (root, outer, inner);
    }

    private static void RightClick(UiRoot root, int x, int y, int releaseX = -1)
    {
        root.OnMouseDown(UiMouseButton.Right, x, y);
        root.OnMouseUp(UiMouseButton.Right, releaseX < 0 ? x : releaseX, y);
    }

    [Fact]
    public void RightClick_OpensTheInnermostMenu()
    {
        var (root, outer, inner) = Tree();
        var outerMenu = new RecordingMenu();
        var innerMenu = new RecordingMenu();
        outer.ContextMenu = outerMenu;
        inner.ContextMenu = innerMenu;
        RightClick(root, 60, 70);
        Assert.Equal(new[] { (60, 70) }, innerMenu.Opens);
        Assert.Empty(outerMenu.Opens);
    }

    [Fact]
    public void ChildWithoutAMenu_PassesToItsParentsMenu()
    {
        var (root, outer, _) = Tree();
        var outerMenu = new RecordingMenu();
        outer.ContextMenu = outerMenu;
        RightClick(root, 60, 70);
        Assert.Equal(new[] { (60, 70) }, outerMenu.Opens);
    }

    [Fact]
    public void ElementHandlingTheRightClick_OpensNoMenu()
    {
        var (root, outer, inner) = Tree();
        var outerMenu = new RecordingMenu();
        outer.ContextMenu = outerMenu;
        inner.HandlesRight = true;
        RightClick(root, 60, 70);
        Assert.Empty(outerMenu.Opens);
    }

    [Fact]
    public void MenuWithNothingToShow_PassesToTheNextMenuOut()
    {
        var (root, outer, inner) = Tree();
        var outerMenu = new RecordingMenu();
        var innerMenu = new RecordingMenu { Result = false };
        outer.ContextMenu = outerMenu;
        inner.ContextMenu = innerMenu;
        RightClick(root, 60, 70);
        Assert.Single(innerMenu.Opens);
        Assert.Single(outerMenu.Opens);
    }

    [Fact]
    public void RightDragAndLeftClick_OpenNothing()
    {
        var (root, outer, _) = Tree();
        var menu = new RecordingMenu();
        outer.ContextMenu = menu;
        RightClick(root, 10, 10, releaseX: 30);
        root.OnMouseDown(UiMouseButton.Left, 10, 10);
        root.OnMouseUp(UiMouseButton.Left, 10, 10);
        Assert.Empty(menu.Opens);
    }

    [Fact]
    public void HidingAnOpenPopupsOwner_DismissesThePopup()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var panel = new UiPanel { Left = 0, Top = 0, Width = 300, Height = 300 };
        var menu = new UiMenu
        {
            Left = 10, Top = 20, Width = 46, Height = 20, OpenUpward = false,
            Items = new[] { new UiMenu.MenuItem("A", "a") },
        };
        panel.AddChild(menu);
        root.AddChild(panel);
        root.OnMouseDown(UiMouseButton.Left, 30, 30);
        root.OnMouseUp(UiMouseButton.Left, 30, 30);
        Assert.True(menu.IsOpen);

        panel.Visible = false;
        Assert.False(menu.IsOpen);
        panel.Visible = true;
        Assert.False(menu.IsOpen);
    }
}
```

- [ ] **Step 2: Run to see it fail** — `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~UiContextMenuRoutingTests"` → build error (`IUiContextMenu`, `ContextMenu` missing).

- [ ] **Step 3: Interface** — create `src/AcDream.App/UI/IUiContextMenu.cs`:

```csharp
namespace AcDream.App.UI;

/// <summary>
/// A menu an element opens on a right click. <see cref="UiRoot"/> offers each
/// element's menu a right click the element itself left unhandled, from the
/// clicked element outwards, so the innermost menu wins.
/// </summary>
internal interface IUiContextMenu
{
    /// <summary>Opens at a point in canvas coordinates; false when there is nothing to show.</summary>
    bool TryOpen(int screenX, int screenY);
}
```

- [ ] **Step 4: Property** — in `UiElement.cs`, directly after `public UiPointerRegion? PointerRegion { get; set; }`:

```csharp

    /// <summary>The menu a right click on this element, or on a child that leaves it unhandled, opens.</summary>
    internal IUiContextMenu? ContextMenu { get; set; }
```

- [ ] **Step 5: Root routing** — in `UiRoot.OnMouseUp`, in the `else if (btn == UiMouseButton.Right && …)` branch, replace `BubbleEvent(target, in click);` with `BubbleRightClick(target, in click);`. Then add after `BubbleEvent`:

```csharp
    /// <summary>
    /// Bubbles a right click like any event, but once an element leaves it
    /// unhandled, offers it to that element's context menu before going on.
    /// </summary>
    private bool BubbleRightClick(UiElement start, in UiEvent e)
    {
        for (UiElement? w = start; w is not null; w = w.Parent)
        {
            if (w.OnEvent(in e)) return true;
            if (w.ContextMenu is { } menu && w.Enabled && menu.TryOpen(MouseX, MouseY)) return true;
        }
        return false;
    }
```

- [ ] **Step 6: Popup dismissal** — at the top of `ClearSubtreeOwnership`, before the keyboard-focus check (a dismissed menu may hand focus back into the subtree; the focus check below then clears it):

```csharp
        if (IsWithinSubtree(_activePopup, subtree))
        {
            // A popup whose owner hides or leaves the tree closes with it, so it
            // does not come back open when the owner is shown again.
            var dismiss = _activePopupDismiss;
            _activePopup = null;
            _activePopupDismiss = null;
            dismiss?.Invoke();
        }
```

- [ ] **Step 7: Run tests** — the Verify filter → all pass, including every existing `UiMenu*` test.

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.App/UI/IUiContextMenu.cs src/AcDream.App/UI/UiElement.cs src/AcDream.App/UI/UiRoot.cs tests/AcDream.App.Tests/UI/UiContextMenuRoutingTests.cs
git commit -m "feat(ui): route unhandled right clicks to element context menus

Hiding or removing a popup's owner now also dismisses the popup.

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 2: `UiMarkupContextMenu` widget

**Goal:** A context menu widget that opens at the cursor (flipped or shifted to stay on screen), draws in the overlay pass in Classic or theme colours, and picks items by mouse or Up/Down/Enter. It holds focus only while open and closes with its owner.

**Files:**
- Create: `src/AcDream.App/UI/UiMarkupContextMenu.cs`
- Modify: `tests/AcDream.App.Tests/UI/MarkupDrawProbe.cs` (add `DrawOverlays`)
- Test: `tests/AcDream.App.Tests/UI/UiMarkupContextMenuTests.cs`

**Acceptance Criteria:**
- [ ] Opening at (150, 120) with items `Copy|-|Paste` puts the popup at (150, 120, 80, 49).
- [ ] At (790, 590) on an 800×600 root it flips to (710, 541). On a root 40 px high, a 49 px popup opens at y 0.
- [ ] A left press on an enabled item closes the menu and then calls `Selected(index)`. A press on a separator or disabled item calls nothing and leaves it open.
- [ ] A press outside closes it and is swallowed: the owner gets no `MouseDown`.
- [ ] An empty item list does not open, and `TryOpen` returns false. `Opening` gets the point in owner coordinates before `ItemsSource` is read.
- [ ] Down/Up skip separators and disabled items and wrap. Enter picks. Escape closes.
- [ ] While open the menu has focus and other keys don't reach `WorldKeyFallThrough`. Afterwards the focus is back on the element that had it (null stays null).
- [ ] Hiding the owner closes the menu at once. Disabling the owner closes it on the next `Tick`.
- [ ] The overlay draw uses the Classic background with no palette and `PluginUiPalette.Moss.Field` with it; the hovered or keyed item draws the highlight.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~UiMarkupContextMenuTests|FullyQualifiedName~UiContextMenuRoutingTests"` → all pass.

**Steps:**

- [ ] **Step 1: Probe helper** — add to `MarkupDrawProbe` (beside `Draw`):

```csharp
    /// <summary>Draws an element's overlay pass (open popups) headlessly.</summary>
    public static TextRenderer DrawOverlays(UiElement element, float w = 800f, float h = 600f)
    {
        var renderer = new TextRenderer(new RecordingGpuDevice(), new NullFrameSource(), "unused");
        renderer.Begin(new Vector2(w, h));
        element.DrawOverlays(new UiRenderContext(renderer, new Vector2(w, h)));
        return renderer;
    }
```

  If quads drawn this way don't show up in `DebugSpriteSegmentVerts`, draw through `UiRoot.Draw(ctx)` instead (it wraps the overlay pass in `BeginOverlayLayer`/`EndOverlayLayer`) and say so in the report.

- [ ] **Step 2: Write the failing tests** — create `tests/AcDream.App.Tests/UI/UiMarkupContextMenuTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using AcDream.App.UI;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

public sealed class UiMarkupContextMenuTests
{
    private sealed class Owner : UiElement
    {
        public int MouseDowns;
        public override bool HandlesClick => true;
        public override bool OnEvent(in UiEvent e)
        {
            if (e.Type == UiEventType.MouseDown && ReferenceEquals(e.Target, this)) MouseDowns++;
            return e.Type == UiEventType.MouseDown;
        }
    }

    private sealed class Focusable : UiElement
    {
        public Focusable() { AcceptsFocus = true; }
    }

    private sealed class Mounted
    {
        public required UiRoot Root { get; init; }
        public required Owner Owner { get; init; }
        public required UiMarkupContextMenu Menu { get; init; }
        public List<int> Picks { get; } = new();
    }

    // The owner covers the whole root so any point opens the menu.
    private static Mounted Mount(string[] items, bool[]? enabled = null, int width = 800, int height = 600)
    {
        var root = new UiRoot { Width = width, Height = height };
        var owner = new Owner { Left = 0, Top = 0, Width = width, Height = height };
        var menu = new UiMarkupContextMenu
        {
            ItemsSource = () => items,
            EnabledSource = enabled is null ? null : () => enabled,
        };
        var mounted = new Mounted { Root = root, Owner = owner, Menu = menu };
        menu.Selected = mounted.Picks.Add;
        owner.AddChild(menu);
        owner.ContextMenu = menu;
        root.AddChild(owner);
        return mounted;
    }

    private static readonly string[] CopyPaste = { "Copy", "-", "Paste" };

    private static void RightClick(UiRoot root, int x, int y)
    {
        root.OnMouseDown(UiMouseButton.Right, x, y);
        root.OnMouseUp(UiMouseButton.Right, x, y);
    }

    private static void Click(UiRoot root, int x, int y)
    {
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
    }

    [Fact]
    public void RightClick_OpensAtTheCursor()
    {
        var m = Mount(CopyPaste);
        RightClick(m.Root, 150, 120);
        Assert.True(m.Menu.IsOpen);
        Assert.Equal((150f, 120f, 80f, 49f), m.Menu.PopupScreenRect);   // 3 + 18 + 7 + 18 + 3
    }

    [Fact]
    public void NearTheRightAndBottomEdges_Flips()
    {
        var m = Mount(CopyPaste);
        RightClick(m.Root, 790, 590);
        Assert.Equal((710f, 541f, 80f, 49f), m.Menu.PopupScreenRect);
    }

    [Fact]
    public void TallerThanTheScreen_ShiftsOntoIt()
    {
        var m = Mount(CopyPaste, height: 40);
        RightClick(m.Root, 10, 30);
        Assert.Equal(0f, m.Menu.PopupScreenRect.Y);
    }

    [Fact]
    public void ClickingAnItem_ClosesAndReportsItsIndex()
    {
        var m = Mount(CopyPaste);
        int indexWhenPicked = -2;
        m.Menu.Selected = i => { indexWhenPicked = m.Menu.IsOpen ? -1 : i; };
        RightClick(m.Root, 100, 100);
        Click(m.Root, 110, 135);          // Paste: 100 + 3 + 18 + 7 = 128 .. 146
        Assert.Equal(2, indexWhenPicked);  // closed before the callback
        Assert.False(m.Menu.IsOpen);
    }

    [Fact]
    public void SeparatorAndDisabledItems_DoNothingAndStayOpen()
    {
        var m = Mount(CopyPaste, enabled: new[] { true, true, false });
        RightClick(m.Root, 100, 100);
        Click(m.Root, 110, 124);          // separator 121 .. 128
        Click(m.Root, 110, 135);          // disabled Paste
        Assert.Empty(m.Picks);
        Assert.True(m.Menu.IsOpen);
    }

    [Fact]
    public void PressOutside_ClosesAndIsSwallowed()
    {
        var m = Mount(CopyPaste);
        RightClick(m.Root, 100, 100);
        Click(m.Root, 500, 500);
        Assert.False(m.Menu.IsOpen);
        Assert.Equal(0, m.Owner.MouseDowns);
    }

    [Fact]
    public void EmptyItems_DoNotOpen()
    {
        var m = Mount(Array.Empty<string>());
        Assert.False(m.Menu.TryOpen(100, 100));
        Assert.False(m.Menu.IsOpen);
    }

    [Fact]
    public void Opening_GetsTheOwnerPoint_BeforeTheItemsAreRead()
    {
        string[] items = Array.Empty<string>();
        var root = new UiRoot { Width = 800, Height = 600 };
        var owner = new Owner { Left = 100, Top = 100, Width = 200, Height = 50 };
        var menu = new UiMarkupContextMenu { ItemsSource = () => items };
        (float X, float Y) point = default;
        menu.Opening = (x, y) => { point = (x, y); items = new[] { "Only" }; };
        owner.AddChild(menu);
        owner.ContextMenu = menu;
        root.AddChild(owner);
        RightClick(root, 150, 120);
        Assert.Equal((50f, 20f), point);
        Assert.True(menu.IsOpen);
    }

    [Fact]
    public void Keys_SkipSeparatorsAndDisabledItems_AndWrap()
    {
        var m = Mount(new[] { "A", "-", "B", "C" }, enabled: new[] { true, true, false, true });
        RightClick(m.Root, 100, 100);
        m.Root.OnKeyDown((int)Key.Down);
        Assert.Equal(0, m.Menu.HighlightForTest);
        m.Root.OnKeyDown((int)Key.Down);
        Assert.Equal(3, m.Menu.HighlightForTest);
        m.Root.OnKeyDown((int)Key.Down);
        Assert.Equal(0, m.Menu.HighlightForTest);
        m.Root.OnKeyDown((int)Key.Up);
        Assert.Equal(3, m.Menu.HighlightForTest);
        m.Root.OnKeyDown((int)Key.Enter);
        Assert.Equal(new[] { 3 }, m.Picks);
        Assert.False(m.Menu.IsOpen);
    }

    [Fact]
    public void UpWithNothingHighlighted_GoesToTheLastEnabledItem()
    {
        var m = Mount(new[] { "A", "B", "C" }, enabled: new[] { true, true, false });
        RightClick(m.Root, 100, 100);
        m.Root.OnKeyDown((int)Key.Up);
        Assert.Equal(1, m.Menu.HighlightForTest);
    }

    [Fact]
    public void Escape_Closes_AndGivesTheFocusBack()
    {
        var m = Mount(CopyPaste);
        var field = new Focusable { Left = 0, Top = 0, Width = 10, Height = 10 };
        m.Root.AddChild(field);
        m.Root.SetKeyboardFocus(field);
        RightClick(m.Root, 100, 100);
        Assert.Same(m.Menu, m.Root.KeyboardFocus);
        m.Root.OnKeyDown((int)Key.Escape);
        Assert.False(m.Menu.IsOpen);
        Assert.Same(field, m.Root.KeyboardFocus);
    }

    [Fact]
    public void WhileOpen_KeysDoNotReachTheGame_AndAPickLeavesNoFocus()
    {
        var m = Mount(CopyPaste);
        int fell = 0;
        m.Root.WorldKeyFallThrough += (_, _) => fell++;
        Assert.Null(m.Root.KeyboardFocus);
        RightClick(m.Root, 100, 100);
        m.Root.OnKeyDown((int)Key.W);
        Assert.Equal(0, fell);
        Click(m.Root, 110, 110);          // Copy
        Assert.Equal(new[] { 0 }, m.Picks);
        Assert.Null(m.Root.KeyboardFocus);
        m.Root.OnKeyDown((int)Key.W);
        Assert.Equal(1, fell);
    }

    [Fact]
    public void HidingTheOwner_ClosesIt()
    {
        var m = Mount(CopyPaste);
        RightClick(m.Root, 100, 100);
        m.Owner.Visible = false;
        Assert.False(m.Menu.IsOpen);
        m.Owner.Visible = true;
        Assert.False(m.Menu.IsOpen);
    }

    [Fact]
    public void DisablingTheOwner_ClosesItOnTheNextTick()
    {
        var m = Mount(CopyPaste);
        RightClick(m.Root, 100, 100);
        m.Owner.Enabled = false;
        m.Root.Tick(0.016, 1000);
        Assert.False(m.Menu.IsOpen);
    }

    [Fact]
    public void Hover_HighlightsEnabledItemsOnly()
    {
        var m = Mount(CopyPaste, enabled: new[] { true, true, false });
        RightClick(m.Root, 100, 100);
        m.Root.OnMouseMove(110, 110);     // Copy
        Assert.Equal(0, m.Menu.HighlightForTest);
        m.Root.OnMouseMove(110, 135);     // disabled Paste
        Assert.Equal(-1, m.Menu.HighlightForTest);
    }

    [Fact]
    public void Draw_UsesClassicColours_OrThePalette()
    {
        var m = Mount(CopyPaste);
        RightClick(m.Root, 100, 100);
        var classic = MarkupDrawProbe.DrawOverlays(m.Root);
        Assert.Equal(1, MarkupDrawProbe.CountQuads(classic, UiMarkupContextMenu.ClassicBackground));
        Assert.Equal(1, MarkupDrawProbe.CountQuads(classic, UiMarkupContextMenu.ClassicSeparator));
        Assert.Equal(0, MarkupDrawProbe.CountQuads(classic, UiMarkupContextMenu.ClassicHighlight));

        m.Root.OnKeyDown((int)Key.Down);
        var keyed = MarkupDrawProbe.DrawOverlays(m.Root);
        Assert.Equal(1, MarkupDrawProbe.CountQuads(keyed, UiMarkupContextMenu.ClassicHighlight));

        m.Menu.ThemePalette = PluginUiPalette.Moss;
        var themed = MarkupDrawProbe.DrawOverlays(m.Root);
        Assert.Equal(0, MarkupDrawProbe.CountQuads(themed, UiMarkupContextMenu.ClassicBackground));
        Assert.True(MarkupDrawProbe.CountQuads(themed, PluginUiPalette.Moss.Field) >= 1);
    }
}
```

  If `Moss.Field` happens to equal another colour the menu draws, compare against `QuadBounds` for the popup rectangle instead.

- [ ] **Step 3: Run to see it fail** — build error (`UiMarkupContextMenu` missing).

- [ ] **Step 4: Widget** — create `src/AcDream.App/UI/UiMarkupContextMenu.cs`:

```csharp
using System.Numerics;
using Silk.NET.Input;

namespace AcDream.App.UI;

/// <summary>
/// A markup context menu. It is a child of the element it belongs to (its
/// owner) and takes no room there: it sits at the owner's origin with no size
/// and draws its popup in the overlay pass where it was opened. Open, it holds
/// the root's popup slot and the keyboard focus, and gives the focus back when
/// it closes. The items are read once, when it opens; "-" is a separator.
/// </summary>
public sealed class UiMarkupContextMenu : UiElement, IUiContextMenu
{
    public const string Separator = "-";
    public const float ItemHeight = 18f;
    public const float SeparatorHeight = 7f;
    public const float Inset = 3f;
    public const float TextIndent = 8f;
    public const float MinWidth = 80f;

    // UiMenu's plain colours, so a context menu looks like the markup drop-down.
    public static readonly Vector4 ClassicBackground = new(0f, 0f, 0f, 0.92f);
    public static readonly Vector4 ClassicBorder = new(0.46f, 0.37f, 0.16f, 1f);
    public static readonly Vector4 ClassicText = new(0.91f, 0.87f, 0.76f, 1f);
    public static readonly Vector4 ClassicDisabledText = new(0.5f, 0.5f, 0.5f, 1f);
    public static readonly Vector4 ClassicHighlight = new(0.40f, 0.33f, 0.14f, 0.95f);
    public static readonly Vector4 ClassicSeparator = UiCheckLamp.UncheckedInner;

    public PluginUiPalette? ThemePalette { get; set; }
    public UiDatFont? DatFont { get; set; }
    public Func<IReadOnlyList<string>> ItemsSource { get; set; } = static () => Array.Empty<string>();

    /// <summary>Per-item enabled flags; null, or an item past the end, is enabled.</summary>
    public Func<IReadOnlyList<bool>>? EnabledSource { get; set; }

    /// <summary>Called with the index of the item picked (separators count), after the menu has closed.</summary>
    public Action<int>? Selected { get; set; }

    /// <summary>Called as a right click opens the menu, before the items are read, with the point in the owner's coordinates.</summary>
    public Action<float, float>? Opening { get; set; }

    public bool IsOpen => _open;

    internal int HighlightForTest => _highlight;

    /// <summary>The open popup in canvas coordinates.</summary>
    internal (float X, float Y, float W, float H) PopupScreenRect
    {
        get
        {
            Vector2 origin = ScreenPosition;
            return (origin.X + _x, origin.Y + _y, _w, _h);
        }
    }

    private bool _open;
    private string[] _items = Array.Empty<string>();
    private bool[] _enabled = Array.Empty<bool>();
    private float[] _rowTops = Array.Empty<float>();   // one more than the items: the last is the bottom
    private float _x, _y, _w, _h;                       // the popup, in the owner's coordinates
    private int _highlight = -1;
    private UiElement? _returnFocus;

    public UiMarkupContextMenu()
    {
        AcceptsFocus = true;
        FocusOnMouseClick = true;          // a press inside keeps the focus here
        CapturesPointerDrag = true;        // a press inside never drags the window
        Anchors = AnchorEdges.None;        // stays at the owner's origin with no size
    }

    public override bool HandlesClick => true;
    public override bool ReceivesHoverMouseMove => _open;
    protected override bool ClipsChildren => false;
    protected override bool ExpandsClipForPopup => true;

    public bool TryOpen(int screenX, int screenY)
    {
        if (Parent is not { } owner || FindRoot() is not { } root || !Available(owner))
            return false;
        Vector2 origin = owner.ScreenPosition;
        Opening?.Invoke(screenX - origin.X, screenY - origin.Y);
        string[] items = ItemsSource().ToArray();
        if (items.Length == 0)
            return false;

        IReadOnlyList<bool>? flags = EnabledSource?.Invoke();
        _items = items;
        _enabled = new bool[items.Length];
        _rowTops = new float[items.Length + 1];
        float y = Inset, textWidth = 0f;
        for (int i = 0; i < items.Length; i++)
        {
            _rowTops[i] = y;
            bool separator = items[i] == Separator;
            y += separator ? SeparatorHeight : ItemHeight;
            _enabled[i] = !separator && (flags is null || i >= flags.Count || flags[i]);
            if (!separator)
                textWidth = MathF.Max(textWidth, MeasureText(items[i]));
        }
        _rowTops[items.Length] = y;
        _w = MathF.Ceiling(MathF.Max(MinWidth, textWidth + 2f * TextIndent));
        _h = y + Inset;
        _x = Place(screenX, _w, root.Width) - origin.X;
        _y = Place(screenY, _h, root.Height) - origin.Y;
        _highlight = -1;
        _open = true;
        root.SetActivePopup(this, () => Close());
        _returnFocus = root.KeyboardFocus;
        root.SetKeyboardFocus(this);
        return true;
    }

    /// <summary>
    /// Where a popup starts on one axis: at the point, or on the other side of
    /// it when it would cross the screen edge, then shifted onto the screen.
    /// </summary>
    internal static float Place(float point, float size, float screen)
    {
        float start = point + size > screen ? point - size : point;
        return MathF.Max(0f, MathF.Min(start, screen - size));
    }

    /// <summary>Closes the popup; with <paramref name="restoreFocus"/>, hands the keyboard focus back to whatever had it when it opened.</summary>
    public void Close(bool restoreFocus = true)
    {
        if (!_open)
            return;
        _open = false;
        _highlight = -1;
        UiElement? previous = _returnFocus;
        _returnFocus = null;
        if (FindRoot() is not { } root)
            return;
        root.ClearActivePopup(this);
        if (restoreFocus && ReferenceEquals(root.KeyboardFocus, this))
            root.SetKeyboardFocus(previous);   // the root drops it if no longer available
    }

    protected override void OnTick(double deltaSeconds)
    {
        if (_open && Parent is { } owner && !Available(owner))
            Close();
    }

    /// <summary>Whether an element and all its ancestors are shown and enabled.</summary>
    private static bool Available(UiElement element)
    {
        for (UiElement? e = element; e is not null; e = e.Parent)
        {
            if (!e.Visible || !e.Enabled)
                return false;
        }
        return true;
    }

    protected override bool OnHitTest(float x, float y) =>
        _open && x >= _x && x < _x + _w && y >= _y && y < _y + _h;

    /// <summary>The item at a point in the owner's coordinates, or −1.</summary>
    internal int ItemAt(float x, float y)
    {
        if (!_open || x < _x || x >= _x + _w)
            return -1;
        float local = y - _y;
        for (int i = 0; i < _items.Length; i++)
        {
            if (local >= _rowTops[i] && local < _rowTops[i + 1])
                return i;
        }
        return -1;
    }

    public override bool OnEvent(in UiEvent e)
    {
        if (e.Type == UiEventType.FocusLost)
        {
            // Something else took the focus; Close has already run if it was us.
            Close(restoreFocus: false);
            return true;
        }
        if (!_open)
            return ReferenceEquals(e.Target, this);   // the tail of the press that closed it
        switch (e.Type)
        {
            case UiEventType.KeyDown:
                HandleKey((Key)e.Data0);
                return true;
            case UiEventType.HoverEnter:
            case UiEventType.MouseMove:
            {
                int hovered = ItemAt(e.Data1, e.Data2);
                _highlight = hovered >= 0 && _enabled[hovered] ? hovered : -1;
                return true;
            }
            case UiEventType.HoverLeave:
                _highlight = -1;
                return true;
            case UiEventType.MouseDown:
            {
                int item = ItemAt(e.Data1, e.Data2);
                if (item >= 0 && _enabled[item])
                    Pick(item);
                return true;
            }
            default:
                // Key ups, chars, releases, the right button and the wheel all stay here.
                return true;
        }
    }

    private void HandleKey(Key key)
    {
        switch (key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Up:
                Move(-1);
                break;
            case Key.Down:
                Move(1);
                break;
            case Key.Enter:
            case Key.KeypadEnter:
                if (_highlight >= 0 && _enabled[_highlight])
                    Pick(_highlight);
                break;
        }
    }

    /// <summary>The next enabled item up or down, wrapping; from none, the first or last.</summary>
    private void Move(int delta)
    {
        int count = _items.Length;
        int index = _highlight;
        for (int step = 0; step < count; step++)
        {
            index = index < 0 ? (delta > 0 ? 0 : count - 1) : (index + delta + count) % count;
            if (_enabled[index])
            {
                _highlight = index;
                return;
            }
        }
    }

    private void Pick(int index)
    {
        Close();
        Selected?.Invoke(index);
    }

    private float MeasureText(string text) => DatFont?.MeasureWidth(text) ?? text.Length * 7f;

    protected override void OnDrawOverlay(UiRenderContext ctx)
    {
        if (!_open)
            return;
        PluginUiPalette? p = ThemePalette;
        Vector4 background = p?.Field ?? ClassicBackground;
        Vector4 border = p?.Border ?? ClassicBorder;
        Vector4 text = p?.Text ?? ClassicText;
        Vector4 disabled = p?.Muted ?? ClassicDisabledText;
        Vector4 highlight = p?.Selected ?? ClassicHighlight;
        Vector4 separator = p?.Border ?? ClassicSeparator;
        ctx.PushAlphaAbsolute(1f);
        try
        {
            ctx.DrawFill(_x, _y, _w, _h, background);
            ctx.DrawRectOutline(_x, _y, _w, _h, border, 1f);
            for (int i = 0; i < _items.Length; i++)
            {
                float top = _y + _rowTops[i];
                if (_items[i] == Separator)
                {
                    ctx.DrawFill(_x + Inset, top + MathF.Floor((SeparatorHeight - 1f) * 0.5f),
                        _w - 2f * Inset, 1f, separator);
                    continue;
                }
                if (i == _highlight)
                    ctx.DrawFill(_x + 1f, top, _w - 2f, ItemHeight, highlight);
                UiCaption.Draw(ctx, DatFont, _items[i], _x + TextIndent, top, ItemHeight,
                    _enabled[i] ? text : disabled, outline: p is null);
            }
        }
        finally { ctx.PopAlpha(); }
    }
}
```

  Notes for the implementer:
  - `DrawRectOutline` draws its own quads in the border colour. The tests count only the background fill, the separator and the highlight, each drawn once.
  - Check that `UiRoot.SetKeyboardFocus` drops an unavailable destination when the outgoing element is non-null (it does: the `IsAvailableInputTarget` check after `FocusLost`).

- [ ] **Step 5: Run tests** — the Verify filter → all pass.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.App/UI/UiMarkupContextMenu.cs tests/AcDream.App.Tests/UI/MarkupDrawProbe.cs tests/AcDream.App.Tests/UI/UiMarkupContextMenuTests.cs
git commit -m "feat(ui): context menu popup widget

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 3: `<contextmenu>` builder, document wiring and theme

**Goal:** Any markup element can have one `<contextmenu items enabled onselect onopen>` child, built into a `UiMarkupContextMenu` on the element, with the plugin theme applied. Misuse fails the build.

**Files:**
- Create: `src/AcDream.App/UI/Markup/MarkupContextMenu.cs`
- Modify: `src/AcDream.App/UI/MarkupDocument.cs` (`Build` root check, `case "contextmenu"`, `list` children, `AddElement` tail, `BindBoolList` + `BindRequiredIntAction` → `internal`)
- Modify: `src/AcDream.App/UI/Markup/MarkupSplit.cs` (panes)
- Modify: `src/AcDream.App/UI/PluginMarkupTheme.cs`
- Test: `tests/AcDream.App.Tests/UI/MarkupContextMenuTests.cs`

**Acceptance Criteria:**
- [ ] `<button>` with a `<contextmenu>`: a right click opens it; clicking item 0 calls `onselect(0)`; the menu is a child of the button and is the button's `ContextMenu`. No menu is added to the panel itself, and a left click still fires `onclick`.
- [ ] Literal `items="Copy|-|Paste"` works. `enabled="{…}"` disables items. `onopen` on a non-list gets −1.
- [ ] A `<contextmenu>` on a `<group>` opens on a right click over a child of the group.
- [ ] A disabled owner (`enabled="false"`) doesn't open its menu.
- [ ] A `<list>` with `<column>` children and a `<contextmenu>` builds its columns from the `<column>`s only. A `<split>` with two `<group>`s and a `<contextmenu>` builds.
- [ ] Under `theme="plugin"` the menu's `ThemePalette` is `PluginUiPalette.Moss`.
- [ ] Build errors (`FormatException`):
  - missing `onselect`, or `onselect` not an `Action<int>`;
  - missing `items`, an empty literal item, or `items` bound to a non-string-list;
  - `enabled` bound to a non-bool-list;
  - `onopen` not an `Action<int>`;
  - two `<contextmenu>` on one element, or a `<contextmenu>` with a child;
  - a `<contextmenu>` directly in `<panel>`, or in a `<column>`.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupContextMenuTests|FullyQualifiedName~AcDream.App.Tests.UI.Markup|FullyQualifiedName~MarkupSplitTests|FullyQualifiedName~MarkupList"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — create `tests/AcDream.App.Tests/UI/MarkupContextMenuTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using AcDream.App.UI;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.Tests.UI;

public sealed class MarkupContextMenuTests
{
    private sealed class Binding
    {
        public List<string> Menu { get; set; } = new() { "Rename", "-", "Delete" };
        public List<bool> MenuEnabled { get; set; } = new() { true, true, false };
        public List<int> Picks { get; } = new();
        public List<int> Opens { get; } = new();
        public int Clicks { get; private set; }
        public Action<int> OnPick => Picks.Add;
        public Action<int> OnOpen => Opens.Add;
        public Action OnClick => () => Clicks++;
        public Action<string> WrongType => _ => { };
        public int NotAList { get; set; }
        public List<string> Names { get; set; } = new() { "a", "b" };
    }

    private const string Pick = "onselect=\"{OnPick}\"";

    private static UiPanel Build(string body, Binding binding, PluginUiThemeSettings? themes = null)
    {
        string theme = themes is null ? "" : "theme=\"plugin\"";
        return MarkupDocument.Build(
            $"<panel x=\"0\" y=\"0\" w=\"400\" h=\"300\" {theme}>{body}</panel>",
            binding, id => (id, 16, 16), themes: themes);
    }

    private static (UiRoot Root, UiPanel Panel) Mount(string body, Binding binding)
    {
        var panel = Build(body, binding);
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        return (root, panel);
    }

    private static string Button(string menu) =>
        $"<button x=\"10\" y=\"10\" w=\"80\" h=\"20\" text=\"Go\" onclick=\"{{OnClick}}\">{menu}</button>";

    private static void RightClick(UiRoot root, int x, int y)
    {
        root.OnMouseDown(UiMouseButton.Right, x, y);
        root.OnMouseUp(UiMouseButton.Right, x, y);
    }

    private static void Click(UiRoot root, int x, int y)
    {
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
    }

    [Fact]
    public void Button_RightClickOpens_AndAPickReportsTheIndex()
    {
        var b = new Binding();
        var (root, panel) = Mount(Button($"<contextmenu items=\"{{Menu}}\" {Pick}/>"), b);
        var button = panel.Children.OfType<UiSimpleButton>().Single();
        var menu = button.Children.OfType<UiMarkupContextMenu>().Single();
        Assert.Same(menu, button.ContextMenu);
        Assert.DoesNotContain(panel.Children, child => child is UiMarkupContextMenu);

        RightClick(root, 20, 15);
        Assert.True(menu.IsOpen);
        Click(root, 30, 27);              // Rename: 15 + 3 .. 15 + 21
        Assert.Equal(new[] { 0 }, b.Picks);

        Click(root, 20, 15);
        Assert.Equal(1, b.Clicks);        // the button still clicks
    }

    [Fact]
    public void LiteralItems_Work()
    {
        var b = new Binding();
        var (root, _) = Mount(Button($"<contextmenu items=\"Copy|-|Paste\" {Pick}/>"), b);
        RightClick(root, 20, 15);
        Click(root, 30, 52);              // Paste: 15 + 3 + 18 + 7 = 43 .. 61
        Assert.Equal(new[] { 2 }, b.Picks);
    }

    [Fact]
    public void EnabledList_DisablesItems()
    {
        var b = new Binding();
        var (root, panel) = Mount(Button($"<contextmenu items=\"{{Menu}}\" enabled=\"{{MenuEnabled}}\" {Pick}/>"), b);
        RightClick(root, 20, 15);
        Click(root, 30, 52);              // Delete, disabled
        Assert.Empty(b.Picks);
        var button = panel.Children.OfType<UiSimpleButton>().Single();
        Assert.True(button.Children.OfType<UiMarkupContextMenu>().Single().IsOpen);
    }

    [Fact]
    public void OnOpen_OffAList_GetsMinusOne()
    {
        var b = new Binding();
        var (root, _) = Mount(Button($"<contextmenu items=\"{{Menu}}\" onopen=\"{{OnOpen}}\" {Pick}/>"), b);
        RightClick(root, 20, 15);
        Assert.Equal(new[] { -1 }, b.Opens);
    }

    [Fact]
    public void DisabledOwner_DoesNotOpen()
    {
        var b = new Binding();
        var (root, panel) = Mount(
            $"<button x=\"10\" y=\"10\" w=\"80\" h=\"20\" text=\"Go\" enabled=\"false\"><contextmenu items=\"{{Menu}}\" {Pick}/></button>", b);
        RightClick(root, 20, 15);
        var button = panel.Children.OfType<UiSimpleButton>().Single();
        Assert.False(button.Children.OfType<UiMarkupContextMenu>().Single().IsOpen);
    }

    [Fact]
    public void Group_OpensOverItsChildren()
    {
        var b = new Binding();
        var (root, panel) = Mount(
            "<group x=\"0\" y=\"0\" w=\"200\" h=\"100\">"
            + "<button x=\"10\" y=\"10\" w=\"80\" h=\"20\" text=\"Go\"/>"
            + $"<contextmenu items=\"{{Menu}}\" {Pick}/></group>", b);
        var group = panel.Children.Single(child => child.ContextMenu is not null);
        RightClick(root, 20, 15);
        Assert.True(group.Children.OfType<UiMarkupContextMenu>().Single().IsOpen);
    }

    [Fact]
    public void ListAndSplit_LeaveTheMenuOutOfTheirChildren()
    {
        var b = new Binding();
        var list = Build(
            "<list x=\"0\" y=\"0\" w=\"200\" h=\"100\"><column type=\"text\" items=\"{Names}\"/>"
            + $"<contextmenu items=\"{{Menu}}\" {Pick}/></list>", b)
            .Children.OfType<UiMarkupList>().Single();
        Assert.Single(list.Columns!);
        Assert.NotNull(list.ContextMenu);

        var split = Build(
            "<split x=\"0\" y=\"0\" w=\"200\" h=\"100\"><group/><group/>"
            + $"<contextmenu items=\"{{Menu}}\" {Pick}/></split>", b)
            .Children.OfType<UiMarkupSplit>().Single();
        Assert.NotNull(split.ContextMenu);
    }

    [Fact]
    public void PluginTheme_SetsThePalette()
    {
        var b = new Binding();
        var panel = Build(Button($"<contextmenu items=\"{{Menu}}\" {Pick}/>"), b,
            new PluginUiThemeSettings { Theme = PluginUiTheme.Moss });
        var menu = panel.Children.OfType<UiSimpleButton>().Single().Children.OfType<UiMarkupContextMenu>().Single();
        Assert.Same(PluginUiPalette.Moss, menu.ThemePalette);
    }

    [Theory]
    [InlineData("<contextmenu items=\"{Menu}\"/>")]
    [InlineData("<contextmenu items=\"{Menu}\" onselect=\"{WrongType}\"/>")]
    [InlineData("<contextmenu onselect=\"{OnPick}\"/>")]
    [InlineData("<contextmenu items=\"a||b\" onselect=\"{OnPick}\"/>")]
    [InlineData("<contextmenu items=\"{NotAList}\" onselect=\"{OnPick}\"/>")]
    [InlineData("<contextmenu items=\"{Menu}\" enabled=\"{NotAList}\" onselect=\"{OnPick}\"/>")]
    [InlineData("<contextmenu items=\"{Menu}\" onopen=\"{WrongType}\" onselect=\"{OnPick}\"/>")]
    [InlineData("<contextmenu items=\"{Menu}\" onselect=\"{OnPick}\"/><contextmenu items=\"{Menu}\" onselect=\"{OnPick}\"/>")]
    [InlineData("<contextmenu items=\"{Menu}\" onselect=\"{OnPick}\"><label text=\"x\"/></contextmenu>")]
    public void BadContextMenu_FailsTheBuild(string menu)
    {
        Assert.Throws<FormatException>(() => Build(Button(menu), new Binding()));
    }

    [Fact]
    public void ContextMenuInThePanelOrAColumn_FailsTheBuild()
    {
        Assert.Throws<FormatException>(() =>
            Build($"<contextmenu items=\"{{Menu}}\" {Pick}/>", new Binding()));
        Assert.Throws<FormatException>(() => Build(
            "<list x=\"0\" y=\"0\" w=\"200\" h=\"100\"><column type=\"text\" items=\"{Names}\">"
            + $"<contextmenu items=\"{{Menu}}\" {Pick}/></column></list>", new Binding()));
    }
}
```

  Use the `using` lines and the `PluginUiThemeSettings`/`PluginUiTheme.Moss` setup from `MarkupRadioTests.MountThemed`. The two empty `<group/>`s in the split test must satisfy `MarkupSplit`; give them `x y w h` if it needs them.

- [ ] **Step 2: Run to see it fail** — build error (`UiMarkupContextMenu` exists, but `MarkupDocument` throws `unknown element <contextmenu>` or ignores it).

- [ ] **Step 3: Make two helpers internal** — in `MarkupDocument.cs` change `private static Func<IReadOnlyList<bool>> BindBoolList(` and `private static Action<int> BindRequiredIntAction(` to `internal static`. Nothing else in them changes.

- [ ] **Step 4: Builder** — create `src/AcDream.App/UI/Markup/MarkupContextMenu.cs`:

```csharp
using System.Xml.Linq;

namespace AcDream.App.UI.Markup;

/// <summary>
/// Builds an element's <c>&lt;contextmenu&gt;</c> child, if it has one, into a
/// <see cref="UiMarkupContextMenu"/> on the element it was built into.
/// </summary>
internal static class MarkupContextMenu
{
    public static UiMarkupContextMenu? Attach(XElement ownerEl, UiElement owner, object binding, MarkupBuildContext context)
    {
        XElement[] menus = ownerEl.Elements("contextmenu").ToArray();
        if (menus.Length == 0)
            return null;
        if (menus.Length > 1)
            throw new FormatException($"<{ownerEl.Name.LocalName}> may have only one <contextmenu>");
        XElement el = menus[0];
        if (el.HasElements)
            throw new FormatException("<contextmenu> takes no children");

        Action<int>? opened = MarkupBindings.BindIntAction(
            (string?)el.Attribute("onopen"), binding, "contextmenu onopen");
        string? enabled = (string?)el.Attribute("enabled");
        var menu = new UiMarkupContextMenu
        {
            DatFont = context.DatFont,
            ItemsSource = Items((string?)el.Attribute("items"), binding),
            EnabledSource = enabled is null
                ? null
                : MarkupDocument.BindBoolList(enabled, binding, "contextmenu enabled"),
            Selected = MarkupDocument.BindRequiredIntAction(
                (string?)el.Attribute("onselect"), binding, "contextmenu onselect"),
            Opening = opened is null ? null : (_, _) => opened(-1),
        };
        owner.AddChild(menu);
        owner.ContextMenu = menu;
        return menu;
    }

    /// <summary>A bound string list, or a literal list separated by <c>|</c>.</summary>
    private static Func<IReadOnlyList<string>> Items(string? expression, object binding)
    {
        if (string.IsNullOrEmpty(expression))
            throw new FormatException("contextmenu items must list at least one item");
        if (MarkupDocument.IsBinding(expression))
            return MarkupDocument.BindStringList(expression, binding, "contextmenu items");
        string[] items = expression.Split('|');
        if (Array.Exists(items, item => item.Length == 0))
            throw new FormatException("contextmenu items must not have an empty item");
        return () => items;
    }
}
```

- [ ] **Step 5: Document wiring** — in `MarkupDocument.cs`:
  - In `Build`, after the `root.Name.LocalName != "panel"` check:

```csharp
        if (root.Element("contextmenu") is not null)
            throw new FormatException("<contextmenu> goes inside the element it belongs to, not directly in <panel>");
```

  - In the `AddElement` switch, before `default:`:

```csharp
            case "contextmenu":
                // Built onto its owner element after the owner's own case; see the tail below.
                break;
```

  - In `case "list":` replace `var listChildren = el.Elements().ToList();` and the loop after it with:

```csharp
                var listChildren = el.Elements()
                    .Where(static child => child.Name.LocalName != "contextmenu")
                    .ToList();
                foreach (var child in listChildren)
                {
                    if (child.Name.LocalName != "column")
                    {
                        throw new FormatException(
                            $"<list> children must all be <column>, got <{child.Name.LocalName}>");
                    }
                    if (child.Element("contextmenu") is not null)
                        throw new FormatException("<contextmenu> goes on the <list>, not on a <column>");
                }
```

  - Replace the tail after the switch:

```csharp
        if (themedPanel is not null && parent.Children.Count > firstChild)
            PluginMarkupTheme.Register(themedPanel, parent.Children[firstChild], el);
```

  with:

```csharp
        if (parent.Children.Count > firstChild)
        {
            UiElement built = parent.Children[firstChild];
            if (themedPanel is not null)
                PluginMarkupTheme.Register(themedPanel, built, el);
            if (MarkupContextMenu.Attach(el, built, binding, Context()) is { } contextMenu && themedPanel is not null)
                PluginMarkupTheme.Register(themedPanel, contextMenu, el.Element("contextmenu")!);
        }
```

- [ ] **Step 6: Split panes** — in `MarkupSplit.Build` replace `XElement[] panes = el.Elements().ToArray();` with:

```csharp
        XElement[] panes = el.Elements().Where(static child => child.Name.LocalName != "contextmenu").ToArray();
```

  `MarkupExpander` and `<group>` pass every child to `AddChild`/`AddElement`, where `case "contextmenu"` builds nothing, so they need no change.

- [ ] **Step 7: Theme** — in `PluginMarkupTheme.Register`:
  - In the `ModernFont` switch add `case UiMarkupContextMenu contextMenu: Bind(() => contextMenu.DatFont, f => contextMenu.DatFont = f); break;`.
  - In the main switch add:

```csharp
            case UiMarkupContextMenu contextMenu:
                panel.AddThemeAction(p => contextMenu.ThemePalette = p);
                break;
```

- [ ] **Step 8: Run tests** — the Verify filter → all pass.

- [ ] **Step 9: Commit**

```bash
git add src/AcDream.App/UI/Markup/MarkupContextMenu.cs src/AcDream.App/UI/MarkupDocument.cs src/AcDream.App/UI/Markup/MarkupSplit.cs src/AcDream.App/UI/PluginMarkupTheme.cs tests/AcDream.App.Tests/UI/MarkupContextMenuTests.cs
git commit -m "feat(ui): <contextmenu> markup child for any element

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 4: List rows: select on right click, `onopen` with the plugin row

**Goal:** On a `<list>`, a right click selects the row under the cursor (single and multi rules) before `onopen` gets that plugin row, or −1 over the header, scrollbar or empty space. Then the menu reads its items and opens.

**Files:**
- Create: `src/AcDream.App/UI/UiMarkupList.ContextMenu.cs`
- Modify: `src/AcDream.App/UI/Markup/MarkupContextMenu.cs` (`Opening` for lists)
- Test: `tests/AcDream.App.Tests/UI/MarkupListContextMenuTests.cs`

**Acceptance Criteria:**
- [ ] Single mode, sorted list: a right click on view row 1 (plugin 1) calls `onchange(1)`, then `onopen(1)`, and the plugin already sees row 1 selected inside `onopen`. The menu opens.
- [ ] A right click on the row that is already selected doesn't call `onchange`.
- [ ] Empty space below the rows, and the header, give `onopen(-1)` and leave the selection alone.
- [ ] Multi mode: a right click on a row in the selection leaves it alone. On a row outside it, `onchange([row])` is called, and a following Shift-click ranges from that row.
- [ ] With an empty item list, the row is still selected and `onopen` called, but the menu doesn't open.
- [ ] The single-column form gives plugin rows too.
- [ ] Picking an item after a row right click calls `onselect(index)`.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupListContextMenuTests|FullyQualifiedName~MarkupContextMenuTests|FullyQualifiedName~MarkupList|FullyQualifiedName~UiMarkupList"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — create `tests/AcDream.App.Tests/UI/MarkupListContextMenuTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public sealed class MarkupListContextMenuTests
{
    private sealed class Binding
    {
        public List<string> Names { get; set; } = new() { "mosswart", "Drudge", "rat", "Banderling" };
        public List<string> Menu { get; set; } = new() { "Loot", "-", "Ignore" };
        public int Single { get; set; } = -1;
        public List<int> SingleChanges { get; } = new();
        public Action<int> Pick => row => { Single = row; SingleChanges.Add(row); };
        public List<int> Selection { get; set; } = new();
        public List<IReadOnlyList<int>> Changes { get; } = new();
        public Action<IReadOnlyList<int>> SetSelection => rows => { Selection = rows.ToList(); Changes.Add(rows); };
        public List<int> Opens { get; } = new();
        public List<int> SelectedAtOpen { get; } = new();
        public Action<int> OnOpen => row => { Opens.Add(row); SelectedAtOpen.Add(Single); };
        public List<int> Picks { get; } = new();
        public Action<int> OnPick => Picks.Add;
        public int SortCol { get; set; }
        public bool SortDesc { get; set; }
    }

    // Header y 0-18; view row v spans y 18+18v .. 36+18v; 5 rows fit in h=108.
    // Ascending view order: Banderling(3), Drudge(1), mosswart(0), rat(2).
    private const string Menu = "<contextmenu items=\"{Menu}\" onopen=\"{OnOpen}\" onselect=\"{OnPick}\"/>";

    private static string ColumnList(string selection) => $"""
          <list x="0" y="0" w="300" h="108" header="true" sortcolumn="{"{SortCol}"}" sortdescending="{"{SortDesc}"}" {selection}>
            <column type="text" header="Name" items="{"{Names}"}" sort="text"/>
            {Menu}
          </list>
        """;

    private const string SingleSelection = "selected=\"{Single}\" onchange=\"{Pick}\"";
    private const string MultiSelection = "selectmode=\"multi\" selected=\"{Selection}\" onchange=\"{SetSelection}\"";

    private static int RowY(int view) => 27 + 18 * view;

    private sealed class Mounted
    {
        public required UiRoot Root { get; init; }
        public required UiMarkupList List { get; init; }
        public required UiMarkupContextMenu Menu { get; init; }
        public UiSelectModifiers Keys { get; set; }
    }

    private static Mounted Mount(Binding binding, string listXml)
    {
        var panel = MarkupDocument.Build(
            $"<panel x=\"0\" y=\"0\" w=\"400\" h=\"300\">{listXml}</panel>", binding, id => (id, 16, 16));
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        var list = panel.Children.OfType<UiMarkupList>().Single();
        var mounted = new Mounted
        {
            Root = root, List = list, Menu = list.Children.OfType<UiMarkupContextMenu>().Single(),
        };
        list.ModifiersSource = () => mounted.Keys;
        Redraw(mounted);
        return mounted;
    }

    private static void Redraw(Mounted m) => MarkupDrawProbe.Draw(m.List.Parent!, 800f, 600f);

    private static void RightClick(Mounted m, int x, int y)
    {
        m.Root.OnMouseDown(UiMouseButton.Right, x, y);
        m.Root.OnMouseUp(UiMouseButton.Right, x, y);
        Redraw(m);
    }

    private static void Click(Mounted m, int x, int y, UiSelectModifiers keys = default)
    {
        m.Keys = keys;
        m.Root.OnMouseDown(UiMouseButton.Left, x, y);
        m.Root.OnMouseUp(UiMouseButton.Left, x, y);
        Redraw(m);
    }

    [Fact]
    public void RightClickARow_SelectsItThenOpens_WithThePluginRow()
    {
        var b = new Binding();
        var m = Mount(b, ColumnList(SingleSelection));
        RightClick(m, 60, RowY(1));
        Assert.Equal(new[] { 1 }, b.SingleChanges);
        Assert.Equal(new[] { 1 }, b.Opens);
        Assert.Equal(new[] { 1 }, b.SelectedAtOpen);
        Assert.True(m.Menu.IsOpen);
    }

    [Fact]
    public void RightClickTheSelectedRow_DoesNotCallOnchange()
    {
        var b = new Binding { Single = 3 };
        var m = Mount(b, ColumnList(SingleSelection));
        RightClick(m, 60, RowY(0));
        Assert.Empty(b.SingleChanges);
        Assert.Equal(new[] { 3 }, b.Opens);
    }

    [Fact]
    public void EmptySpaceAndHeader_GiveMinusOne_AndKeepTheSelection()
    {
        var b = new Binding { Single = 2 };
        var m = Mount(b, ColumnList(SingleSelection));
        RightClick(m, 60, RowY(4));       // below the four rows
        m.Menu.Close();
        RightClick(m, 60, 9);             // header
        Assert.Equal(new[] { -1, -1 }, b.Opens);
        Assert.Empty(b.SingleChanges);
        Assert.Equal(2, b.Single);
    }

    [Fact]
    public void Multi_RowInTheSelection_KeepsIt()
    {
        var b = new Binding { Selection = new() { 0, 1 } };
        var m = Mount(b, ColumnList(MultiSelection));
        RightClick(m, 60, RowY(1));       // plugin 1
        Assert.Empty(b.Changes);
        Assert.Equal(new[] { 1 }, b.Opens);
    }

    [Fact]
    public void Multi_RowOutsideTheSelection_SelectsOnlyIt_AndAnchorsThere()
    {
        var b = new Binding { Selection = new() { 0 } };
        var m = Mount(b, ColumnList(MultiSelection));
        RightClick(m, 60, RowY(0));       // plugin 3
        Assert.Equal(new[] { 3 }, Assert.Single(b.Changes));
        m.Menu.Close();
        Click(m, 60, RowY(2), new UiSelectModifiers(Add: false, Range: true));
        Assert.Equal(new[] { 0, 1, 3 }, b.Selection);   // views 0..2 = plugins 3, 1, 0
    }

    [Fact]
    public void EmptyMenu_SelectsAndCallsOnopen_ButDoesNotOpen()
    {
        var b = new Binding { Menu = new() };
        var m = Mount(b, ColumnList(SingleSelection));
        RightClick(m, 60, RowY(1));
        Assert.Equal(new[] { 1 }, b.SingleChanges);
        Assert.Equal(new[] { 1 }, b.Opens);
        Assert.False(m.Menu.IsOpen);
    }

    [Fact]
    public void SingleColumnForm_GivesPluginRows()
    {
        var b = new Binding();
        var m = Mount(b, $"<list x=\"0\" y=\"0\" w=\"300\" h=\"108\" items=\"{{Names}}\" {SingleSelection}>{Menu}</list>");
        RightClick(m, 60, 27);            // row 1 (no header: rows start at 0)
        Assert.Equal(new[] { 1 }, b.Opens);
        Assert.Equal(1, b.Single);
    }

    [Fact]
    public void PickAfterARowRightClick_ReportsTheItem()
    {
        var b = new Binding();
        var m = Mount(b, ColumnList(SingleSelection));
        RightClick(m, 60, RowY(1));       // popup top at y 45
        Click(m, 70, 80);                 // Ignore: 45 + 3 + 18 + 7 = 73 .. 91
        Assert.Equal(new[] { 2 }, b.Picks);
    }
}
```

- [ ] **Step 2: Run to see it fail** — `RightClickARow…` fails (`onopen` gets −1, nothing selected).

- [ ] **Step 3: List partial** — create `src/AcDream.App/UI/UiMarkupList.ContextMenu.cs`:

```csharp
namespace AcDream.App.UI;

public sealed partial class UiMarkupList
{
    /// <summary>
    /// A right click about to open the list's context menu, at a point in list
    /// coordinates: selects the row under it, unless the row is already in a
    /// multi selection, and returns its plugin row; −1 over the header, the
    /// scrollbar or empty space, where the selection is left alone.
    /// </summary>
    internal int PrepareContextMenu(float x, float y)
    {
        int row = ContextMenuRowAt(x, y);
        if (row < 0 || !Enabled)
            return row;
        if (MultiSelect)
        {
            if (!SelectedRowsSource().Contains(row))
            {
                _anchorRow = _leadRow = row;
                SelectedRowsChanged?.Invoke(new[] { row });
            }
        }
        else if (SelectedIndexSource() != row)
        {
            SelectionChanged?.Invoke(row);
        }
        return row;
    }

    /// <summary>The plugin row under a point in list coordinates, from the rows as they are now; −1 for none.</summary>
    private int ContextMenuRowAt(float x, float y)
    {
        RefreshSelectionRows();
        int rowCount = _view.ViewCount;
        int visibleRows = VisibleRows;
        float contentWidth = rowCount > visibleRows ? MathF.Max(0f, Width - ScrollbarWidth) : Width;
        if (x < 0f || x >= contentWidth || y < RowsTop)
            return -1;
        int row = (int)MathF.Floor((y - RowsTop) / MathF.Max(1f, RowHeight));
        int index = _topRow + row;
        if (row < 0 || row >= visibleRows || index < 0 || index >= rowCount)
            return -1;
        return _view.PluginRow(index);
    }
}
```

  `RowsTop` is 0 without a header, so the single-column form works unchanged. `RefreshSelectionRows` sets the view map to identity in the single-column form.

- [ ] **Step 4: Builder** — in `MarkupContextMenu.Attach` replace the `Opening = …` line with:

```csharp
            Opening = owner is UiMarkupList list
                ? (x, y) =>
                {
                    int row = list.PrepareContextMenu(x, y);
                    opened?.Invoke(row);
                }
                : opened is null ? null : (_, _) => opened(-1),
```

- [ ] **Step 5: Run tests** — the Verify filter → all pass.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.App/UI/UiMarkupList.ContextMenu.cs src/AcDream.App/UI/Markup/MarkupContextMenu.cs tests/AcDream.App.Tests/UI/MarkupListContextMenuTests.cs
git commit -m "feat(ui): list context menus select the row and report it to onopen

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 5: Docs

**Goal:** `docs/plugin-ui-markup.md` documents `<contextmenu>`.

**Files:**
- Modify: `docs/plugin-ui-markup.md`

**Acceptance Criteria:**
- [ ] The Elements table gains a `contextmenu` row: "Right-click menu for the element it is inside" with `items enabled onselect onopen`. The paragraph under the table that lists common attributes says `contextmenu` takes none of them.
- [ ] The Bindings table gains rows:
  - `contextmenu items` (`IEnumerable<string>`, or a literal list `a\|-\|b`; `-` is a separator);
  - `contextmenu enabled` (`IEnumerable<bool>`, one per item);
  - `contextmenu onselect` (`Action<int>`, the item index; separators count);
  - `contextmenu onopen` (`Action<int>`, the plugin row on a list, otherwise −1).
- [ ] A `## Context menu` section after `## Split` and before `## Resizable panels and anchors` covers:
  - both spec examples (button, list);
  - where it opens and what a right drag does;
  - innermost menu wins, and a `<group>` passes clicks on its empty space through;
  - items read at open, separators, disabled items;
  - mouse and keys (Up/Down wrap, Enter, Escape), focus held while open and given back;
  - what closes it;
  - list behaviour (select first, multi rule, −1);
  - an empty list doesn't open;
  - Classic and theme looks;
  - build errors;
  - the older-host line.
- [ ] The older-host line is checked against base 391e709a. Confirm with `git show 391e709a:src/AcDream.App/UI/MarkupDocument.cs`:
  - an element that reads no children (`button`, `label`, …) ignores a `<contextmenu>` child, so no menu;
  - `<list>` throws "children must all be <column>";
  - `<group>` throws `unknown element <contextmenu>`.

**Verify:** `grep -c 'contextmenu' docs/plugin-ui-markup.md` → at least 8.

**Steps:**

- [ ] **Step 1: Tables** — edit the Elements and Bindings tables as listed, keeping their style.

- [ ] **Step 2: Section** — insert before `## Resizable panels and anchors`:

````markdown
## Context menu

```xml
<button x="8" y="8" w="80" h="20" text="Profile">
  <contextmenu items="{ProfileMenu}" enabled="{ProfileMenuEnabled}" onselect="{OnProfileMenu}"/>
</button>
<list x="8" y="36" w="300" h="200" header="true" selected="{Sel}" onchange="{Pick}">
  <column type="text" header="Name" items="{Names}" sort="text"/>
  <contextmenu items="{RowMenu}" onopen="{OnRowMenuOpen}" onselect="{OnRowMenu}"/>
</list>
```

A `<contextmenu>` inside an element gives that element a right-click menu.
An element has at most one. The menu isn't drawn in place: a right click on
the element (or on anything inside it that has no menu of its own) opens it
at the cursor, flipped or shifted so it stays on the screen. A drag with the
right button doesn't open it. The innermost menu wins. A `<group>` lets
clicks on its empty space through, so a group's menu opens over its children
only.

`items` is a literal list (`Copy|-|Paste`) or a bound `IEnumerable<string>`,
and `-` is a separator line. `enabled` optionally binds an
`IEnumerable<bool>`, one flag per item; disabled items draw dimmed and can't
be picked, and an item past the end of the list is enabled. Both are read
once, when the menu opens. `onselect` gets the index of the item picked,
counting separators, after the menu has closed. An empty item list doesn't
open a menu.

Hovering an item highlights it, and clicking it picks it; clicking a
separator or a disabled item does nothing. While the menu is open it has
the keyboard: Up and Down move between the items that can be picked
(wrapping), Enter picks, Escape closes, and other keys do nothing, so the
game doesn't act on them. When it closes the keyboard goes back to whatever
had it before. A click outside the menu closes it (and does nothing else),
as does opening another menu or drop-down, or the element being hidden or
disabled.

`onopen` is called as the menu opens, before `items` is read, so a plugin
can change the items to suit. On a `<list>`, the right click first selects
the row under the cursor (unless it is already one of the rows selected in a
`selectmode="multi"` list) and `onopen` gets that row, as the plugin's own
row index; over the header or empty space it gets −1 and the selection
stays as it is. On other elements `onopen` gets −1.

In Classic the menu has the plain drop-down's colours; under
`theme="plugin"` it takes the palette's field, border, text, muted and
selected colours and the panel's font.

A `<contextmenu>` without `items` or `onselect`, with an empty literal item,
with `items`, `enabled`, `onselect` or `onopen` of the wrong type, with
children, a second one on the same element, or one directly in `<panel>` or
inside a `<column>` fails the build. A host older than context menus
ignores the menu inside an element that has no children of its own (such as
a `button`), and fails the build when it is inside a `<list>` or `<group>`.
````

  Re-check every sentence against `UiMarkupContextMenu.cs`, `MarkupContextMenu.cs` and `UiMarkupList.ContextMenu.cs`, and the older-host claim against base 391e709a. If the base host behaves differently, say what happens instead.

- [ ] **Step 3: Commit**

```bash
git add docs/plugin-ui-markup.md
git commit -m "docs(ui): context menus

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 6: Series gate

**Goal:** The Release build is clean, App, Plugin and UI.Abstractions tests have no new failures, and every commit builds.

**Files:** none changed.

**Acceptance Criteria:**
- [ ] `dotnet build AcDream.slnx -c Release` → 0 errors, 0 warnings.
- [ ] App + Plugin failures ⊆ `mc-base.txt` (162 names); UI.Abstractions failures ⊆ `mc-uiabs-base.txt` (empty).
- [ ] Each of the series' commits builds and passes the markup, list, menu and context-menu test filters on its own.
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
done | norm > $T/mc10-after.txt
dotnet test tests/AcDream.UI.Abstractions.Tests/AcDream.UI.Abstractions.Tests.csproj -c Release 2>&1 | norm > $T/mc10-uiabs-after.txt
echo "new App/Plugin:"; comm -13 $T/mc-base.txt $T/mc10-after.txt
echo "new UI.Abstractions:"; comm -13 $T/mc-uiabs-base.txt $T/mc10-uiabs-after.txt
git checkout -- '*.lock.json' 2>/dev/null; git status --short
```
Expected: `0 Warning(s)`, `0 Error(s)`, nothing after either "new" line.

- [ ] **Step 2: Each commit builds alone**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
for c in $(git rev-list --reverse 9b4b5523..feat/markup-controls); do
  git worktree add -q --detach .worktrees/mc-commit-check $c
  (cd .worktrees/mc-commit-check && dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release \
     --filter "FullyQualifiedName~AcDream.App.Tests.UI.Markup|FullyQualifiedName~ListButtonColumn|FullyQualifiedName~UiMarkupList|FullyQualifiedName~UiMenu|FullyQualifiedName~ContextMenu|FullyQualifiedName~RememberedValueTests" 2>&1 \
     | grep -E ': error|: warning|Passed!|Failed!' | sort -u | sed "s/^/$(git rev-parse --short $c) /")
  git worktree remove --force .worktrees/mc-commit-check
done
```
Expected: one `Passed!` per commit, no `error`/`warning`.

- [ ] **Step 3: Branch contents**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls
git log --oneline 9b4b5523..HEAD
git diff --stat 391e709a..HEAD | grep superpowers || echo "no superpowers docs"
```
Expected: the 5 commits of Tasks 1-5 (plus any review-fix commits); "no superpowers docs".

No commit.
