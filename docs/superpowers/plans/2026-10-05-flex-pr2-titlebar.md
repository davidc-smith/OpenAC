# Flex layout PR 2 (title bar) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Plugin markup windows can have a host title bar (window name and a close button) above a content area, opted in with `titlebar="true"`, which ends the clash between a window's title and its content.

**Architecture:** `MarkupDocument` gains an internal `BuildWindow` that returns a `MarkupWindow` (frame, content root, title bar, saved-size revision); public `Build` returns its frame, so every existing caller is unchanged. A window with a bar gets two layers under its frame: a `PluginTitleBar` (the chrome layer: title text and a `PluginCloseButton`) and a `UiPluginContentHost` inset by the chrome. The host owns every authored child. Insets, the close-button art id and the authored-inputs revision live in `PluginWindowChrome`. `MarkupWindow.Register` registers the content host as `ContentRoot` and wires the close button to `RetailWindowManager.Close`. Windows without the attribute are built exactly as today.

**Tech Stack:** C# / .NET 10, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-05-plugin-flex-layout-design.md` (fork-only docs branch `docs/flex-layout-spec`): sections 1.4, 2, 3.4 (revision only), 5 (`titlebar` value), 6, 7 (`MarkupTitleBarTests`) and 9 (PR 2). This plan covers PR 2 only.

## Global Constraints

- Work in a worktree `.worktrees/flex-titlebar` on a new branch `flex/titlebar` from fork `main` (0991b97c or later). Never commit `docs/superpowers/**` on that branch.
- Build and test with the pinned SDK: every shell starts with `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH`. Building rewrites some `*.lock.json` files with local RID churn; revert with `git checkout -- '*.lock.json'` before each commit, never commit that churn.
- The repository builds with `TreatWarningsAsErrors`; the build must stay at 0 warnings.
- **No contract change.** `src/AcDream.Plugin.Abstractions/**` is untouched (no fork contract version bump).
- **Classic byte-identity.** A root without `titlebar` must build the same tree, the same sizes, the same revision and draw the same vertices as today. `PluginThemeClassicIdentityTests`, `PluginThemeWindowTests`, `MarkupDocumentTests`, `MarkupPanelClickTests`, `PluginSidePanelThemeTests` and `RetailWindowLayoutPersistenceTests` must pass unchanged.
- Chrome constants (one place, `PluginWindowChrome`): `Border = 5` (= `RetailChromeSprites.Border`), `TitleBarHeight = 24` (= `PluginUiStyle.HeaderHeight`), `CloseButtonSize = 19` (bar height minus the border), `Version = 1`, `ClassicCloseSprite = 0x06004D0C` (the retail inventory close button's Normal-state image, element `0x100001D2` of layout `0x21000023`, 24×23, found with a throwaway DAT probe on 2026-10-05).
- Content area of a window with a bar: left/right/bottom inset 5, top inset 24. `w`/`h`/`minw`/`minh` are content-area sizes; the frame is `w + 10` × `h + 29`.
- Close button rectangle, frame-relative: `Left = frameWidth − 24`, `Width = Height = 19`, `Top = 5` in Classic (below the top border) and `Top = 3` in modern themes (centred in the band).
- Title text: Classic at x = 8, y = `5 + floor((19 − lineHeight) / 2 + 0.5)`; modern at x = 12, y = `floor((24 − lineHeight) / 2 + 0.5)`. It ends 4 points before the close button and is shortened with an ASCII `...` (dat fonts may lack `…`).
- The authored-inputs revision is FNV-1a over the inputs (stable across processes; `string.GetHashCode` is randomised per process and must not be used).
- Commit messages end with the line `Claude-Session: https://claude.ai/code/session_019dvJ5gakBkq3uJyr1PAC3u`.

**User decisions (already made):**
- Scope: markup windows only; canvases out of scope.
- Title bar default: on for windows that use `layout` (PR 3), opt-in `titlebar="true"` elsewhere, `titlebar="false"` turns it off.
- Close does what the dock slot does: clears the player's request (the plugin keeps running; the dock reopens the window).
- **2026-10-05 (this plan): on a window with a title bar, the bar's close button replaces the dock's "–" minimize button.** The dock does not add "–" to such a window, and the X's tooltip becomes "Close (reopen from the dock)". Windows without a bar keep "–" exactly as today.
- Delivery as four single-topic PRs from fork main, merged into fork main after review; spec and plans stay on the fork-only docs branch.

## Spec corrections made while planning

Folded into the spec (section "Planning corrections (PR 2)") in the same docs commit as this plan:

1. **The dock already puts a "–" minimize button on docked plugin windows** (`PluginSidePanel.Add`, top-right, 18×17, `handle.Hide()`). This is the "close button" that `docs/plugin-ui-markup.md` describes. The spec did not know about it, and it would sit under the bar's X. Resolved by the user: the X replaces "–" on windows with a bar.
2. **The close button is 19 points square, not the full bar height.** In Classic the frame's top border (5 points) is drawn over the children, so the button sits below it; modern themes centre the same square in the band.
3. **Eager first measurement moves to PR 3.** In PR 2 every root states its `w`/`h` (only `layout` roots may omit them), so the frame size is known at build time without a measure pass. PR 3 adds the measure pass at the end of `BuildWindow`.
4. **`CloseRequested` lives on `PluginTitleBar`**, and `MarkupWindow.Register` subscribes it after registration, rather than an event on the frame. Classic frames are plain `UiNineSlicePanel`s with no room for it.
5. The ellipsis is ASCII `...`.

## File Structure

| File | Responsibility |
|---|---|
| Create `src/AcDream.App/UI/PluginWindowChrome.cs` | Chrome constants, insets, the authored-inputs list for the revision, title ellipsising. |
| Modify `src/AcDream.App/UI/RetailWindowManager.cs` | `ComputeAuthoredGeometryRevision(IReadOnlyList<string?>)` overload (FNV-1a). |
| Create `src/AcDream.App/UI/PluginTitleBar.cs` | `PluginTitleBar` (the chrome layer: title text, `CloseRequested`) and `PluginCloseButton` (Classic sprite / themed X). |
| Create `src/AcDream.App/UI/UiPluginContentHost.cs` | The element that owns a window's authored children inside the chrome. |
| Modify `src/AcDream.App/UI/PluginUiStyle.cs` | `CloseButton` drawing and `CloseGlyphSize`. |
| Create `src/AcDream.App/UI/MarkupWindow.cs` | The built window and `Register` (content root, revision, close wiring). |
| Modify `src/AcDream.App/UI/MarkupDocument.cs` | `BuildWindow`, `titlebar` parsing, content area, fallback title; `Build` delegates. |
| Modify `src/AcDream.App/UI/PluginMarkupTheme.cs` | `RegisterTitleBar`. |
| Modify `src/AcDream.App/UI/RetailUiRuntime.cs` | `MountPlugins` uses `BuildWindow` + `MarkupWindow.Register`. |
| Modify `src/AcDream.App/UI/PluginSidePanel.cs` | No "–" on a window with a bar; docked tooltip on its X. |
| Modify `docs/plugin-ui-markup.md` | `titlebar` attribute, "Title bar" section, close-button paragraph corrected. |
| Modify `samples/AcDream.Plugins.ThemeGallery/gallery.xml` | Adopts the title bar (the live-gate window). |
| Create `tests/AcDream.App.Tests/UI/PluginWindowChromeTests.cs` | Revision, insets, ellipsis. |
| Create `tests/AcDream.App.Tests/UI/PluginTitleBarTests.cs` | The bar and close button on their own: geometry, looks, click and keyboard. |
| Create `tests/AcDream.App.Tests/UI/MarkupTitleBarTests.cs` | Markup: defaults, errors, content area, fallback title, tree split, Tab order, drag, themes. |
| Create `tests/AcDream.App.Tests/UI/MarkupWindowMountTests.cs` | Register wiring, close path, dock interplay, saved-size revision. |

---

### Task 1: Window chrome constants, authored-inputs revision and title ellipsis

**Goal:** A single `PluginWindowChrome` holds every chrome number, and `RetailWindowManager` can hash a window's authored inputs into a revision that is stable across processes.

**Files:**
- Create: `src/AcDream.App/UI/PluginWindowChrome.cs`
- Modify: `src/AcDream.App/UI/RetailWindowManager.cs` (add an overload after the existing `ComputeAuthoredGeometryRevision`, line ~103)
- Test: `tests/AcDream.App.Tests/UI/PluginWindowChromeTests.cs`

**Acceptance Criteria:**
- [ ] `PluginWindowChrome.Border == 5`, `TitleBarHeight == 24`, `CloseButtonSize == 19`, `HorizontalInsets == 10`, `VerticalInsets == 29`.
- [ ] `ComputeAuthoredGeometryRevision(["300","200",null,null,null,null,null,"true","1"]) == 1338326359` (a pinned value computed independently, which proves the hash is FNV-1a and process-stable).
- [ ] Absent (`null`) and empty (`""`) inputs hash differently; changing any one input changes the hash; the result is non-negative.
- [ ] `AuthoredInputs` lists `w h minw minh resizable resize layout titlebar` as raw attribute strings, then `Version`, and ignores attributes that cannot change geometry (`title`, `x`, `theme`).
- [ ] `Ellipsize` returns the text unchanged when it fits, the longest prefix (trailing spaces trimmed) plus `...` that fits otherwise, `...` alone when nothing else fits, and `""` when even that does not fit.

**Verify:** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~PluginWindowChromeTests"` → all pass.

**Steps:**

- [ ] **Step 1: Create the worktree and branch**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
git worktree add .worktrees/flex-titlebar -b flex/titlebar main
cd .worktrees/flex-titlebar
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
```

- [ ] **Step 2: Write the failing tests**

`tests/AcDream.App.Tests/UI/PluginWindowChromeTests.cs`:

```csharp
using System.Xml.Linq;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public sealed class PluginWindowChromeTests
{
    [Fact]
    public void TheChromeInsetsAreTheClassicBorderAndTheHeaderBand()
    {
        Assert.Equal(5f, PluginWindowChrome.Border);
        Assert.Equal(24f, PluginWindowChrome.TitleBarHeight);
        Assert.Equal(19f, PluginWindowChrome.CloseButtonSize);
        Assert.Equal(10f, PluginWindowChrome.HorizontalInsets);
        Assert.Equal(29f, PluginWindowChrome.VerticalInsets);
    }

    [Fact]
    public void TheRevisionIsFnv1aOverTheAuthoredInputs()
    {
        // Pinned: computed independently. A per-process string hash would fail here.
        Assert.Equal(1338326359, RetailWindowManager.ComputeAuthoredGeometryRevision(
            ["300", "200", null, null, null, null, null, "true", "1"]));
    }

    [Fact]
    public void AnAbsentInputIsNotAnEmptyOne()
    {
        Assert.NotEqual(
            RetailWindowManager.ComputeAuthoredGeometryRevision([null, "1"]),
            RetailWindowManager.ComputeAuthoredGeometryRevision(["", "1"]));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
    public void ChangingAnyInputChangesTheRevision(int index)
    {
        string?[] inputs = ["300", "200", "250", "150", "true", "x", null, "true", "1"];
        int before = RetailWindowManager.ComputeAuthoredGeometryRevision(inputs);
        inputs[index] = inputs[index] is null ? "row" : inputs[index] + "0";
        int after = RetailWindowManager.ComputeAuthoredGeometryRevision(inputs);
        Assert.NotEqual(before, after);
        Assert.True(after >= 0);
    }

    [Fact]
    public void TheAuthoredInputsAreTheGeometryAttributesThenTheChromeVersion()
    {
        var root = XElement.Parse(
            "<panel x=\"9\" title=\"T\" theme=\"plugin\" w=\"300\" h=\"200\" minw=\"250\" "
            + "resizable=\"true\" resize=\"x\" titlebar=\"true\" />");
        Assert.Equal(
            new string?[] { "300", "200", "250", null, "true", "x", null, "true", "1" },
            PluginWindowChrome.AuthoredInputs(root));
    }

    private static float Measure(string s) => s.Length * 10f;

    [Theory]
    [InlineData("Buffs", 50f, "Buffs")]
    [InlineData("Buff Bot", 70f, "Buff...")]
    [InlineData("Buff Bot", 60f, "Buf...")]
    [InlineData("Buff Bot", 30f, "...")]
    [InlineData("Buff Bot", 29f, "")]
    public void LongTitlesAreShortenedWithDots(string text, float width, string expected) =>
        Assert.Equal(expected, PluginWindowChrome.Ellipsize(text, Measure, width));
}
```

Note: `"Buff Bot"` at 70 → the 4-character prefix `"Buff"` plus `...` is 7 characters = 70, which fits. At 60 → `"Buff "` trimmed is `"Buff"` (70, too wide), so `"Buf..."` (60).

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~PluginWindowChromeTests"`
Expected: build FAIL — `PluginWindowChrome` does not exist.

- [ ] **Step 4: Write `PluginWindowChrome`**

`src/AcDream.App/UI/PluginWindowChrome.cs`:

```csharp
using System.Globalization;
using System.Xml.Linq;

namespace AcDream.App.UI;

/// <summary>
/// The host-drawn chrome around a plugin window's content area: the frame's
/// border and, when the window has one, its title bar. A window with a
/// content area places its authored children inside these insets, and its
/// authored <c>w</c>/<c>h</c>/<c>minw</c>/<c>minh</c> describe that area.
/// Classic and the modern themes share the same insets.
/// </summary>
internal static class PluginWindowChrome
{
    /// <summary>The Classic border's width; the left, right and bottom inset.</summary>
    internal const float Border = RetailChromeSprites.Border;

    /// <summary>The title bar's height; the top inset of a window with a bar.</summary>
    internal const float TitleBarHeight = PluginUiStyle.HeaderHeight;

    /// <summary>The close button's side: the bar below the frame's top border.</summary>
    internal const float CloseButtonSize = TitleBarHeight - Border;

    internal const float HorizontalInsets = 2f * Border;
    internal const float VerticalInsets = TitleBarHeight + Border;

    /// <summary>
    /// Bumped whenever the insets above change, so windows whose chrome grew
    /// or shrank drop their saved size once.
    /// </summary>
    internal const int Version = 1;

    /// <summary>The retail inventory close button's art (element 0x100001D2 of layout 0x21000023).</summary>
    internal const uint ClassicCloseSprite = 0x06004D0Cu;

    internal const string CloseTooltip = "Close";
    internal const string DockedCloseTooltip = "Close (reopen from the dock)";

    private static readonly string[] GeometryAttributes =
        ["w", "h", "minw", "minh", "resizable", "resize", "layout", "titlebar"];

    /// <summary>
    /// What the markup states about a window's geometry, as written: each
    /// geometry attribute's raw value (null when absent), then the chrome
    /// <see cref="Version"/>. Nothing measured, so a bound caption or a theme
    /// never resets a player's saved size.
    /// </summary>
    internal static string?[] AuthoredInputs(XElement root)
    {
        var inputs = new string?[GeometryAttributes.Length + 1];
        for (int i = 0; i < GeometryAttributes.Length; i++)
            inputs[i] = (string?)root.Attribute(GeometryAttributes[i]);
        inputs[^1] = Version.ToString(CultureInfo.InvariantCulture);
        return inputs;
    }

    /// <summary>
    /// <paramref name="text"/> if it fits in <paramref name="maxWidth"/>, else
    /// its longest prefix that fits with "..." after it (dat fonts have no
    /// ellipsis glyph), else "..." alone, else nothing.
    /// </summary>
    internal static string Ellipsize(string text, Func<string, float> measure, float maxWidth)
    {
        if (measure(text) <= maxWidth) return text;
        const string Dots = "...";
        for (int n = text.Length - 1; n > 0; n--)
        {
            string candidate = text[..n].TrimEnd() + Dots;
            if (measure(candidate) <= maxWidth) return candidate;
        }
        return measure(Dots) <= maxWidth ? Dots : string.Empty;
    }
}
```

- [ ] **Step 5: Add the revision overload**

In `src/AcDream.App/UI/RetailWindowManager.cs`, directly after the existing `ComputeAuthoredGeometryRevision(float, float, float, float, bool)` method:

```csharp
    /// <summary>
    /// The revision of a window whose geometry its markup states rather than
    /// measures: one entry per authored input, null where it is absent. FNV-1a,
    /// so the value is the same in every process; it is saved with the window's
    /// layout.
    /// </summary>
    public static int ComputeAuthoredGeometryRevision(IReadOnlyList<string?> authoredInputs)
    {
        ArgumentNullException.ThrowIfNull(authoredInputs);
        const uint Prime = 16777619u;
        uint hash = 2166136261u;
        unchecked
        {
            foreach (string? input in authoredInputs)
            {
                foreach (char c in input ?? "\0absent")
                    hash = (hash ^ c) * Prime;
                hash = (hash ^ 0xFFFFu) * Prime;   // between inputs
            }
        }
        return (int)(hash & 0x7FFFFFFFu);
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~PluginWindowChromeTests"`
Expected: PASS (all).

- [ ] **Step 7: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/PluginWindowChrome.cs src/AcDream.App/UI/RetailWindowManager.cs tests/AcDream.App.Tests/UI/PluginWindowChromeTests.cs
git commit -m "feat(ui): plugin window chrome constants and authored-inputs revision

Claude-Session: https://claude.ai/code/session_019dvJ5gakBkq3uJyr1PAC3u"
```

---

### Task 2: Title bar, close button and content host controls

**Goal:** A `PluginTitleBar` draws a window name and a close button in Classic and modern looks and raises `CloseRequested` on click or Enter/Space, and a `UiPluginContentHost` holds content inside the chrome.

**Files:**
- Create: `src/AcDream.App/UI/PluginTitleBar.cs`
- Create: `src/AcDream.App/UI/UiPluginContentHost.cs`
- Modify: `src/AcDream.App/UI/PluginUiStyle.cs` (add `CloseGlyphSize` and `CloseButton` after `GhostButton`, line ~81)
- Test: `tests/AcDream.App.Tests/UI/PluginTitleBarTests.cs`

**Acceptance Criteria:**
- [ ] A bar built for a 300-wide frame is at (0, 0, 300, 24), anchored left/top/right, click-through, with its close button at (276, 5, 19, 19), anchored top/right, a Tab stop that accepts focus and does not take focus on mouse click.
- [ ] Setting `ThemePalette` on the bar themes the close button and moves it to `Top = 3`; clearing it puts it back at `Top = 5`.
- [ ] A click on the close button, and Enter or Space while it has keyboard focus, each raise `CloseRequested` once.
- [ ] Classic: the close button draws texture `ClassicCloseSprite` resolves to (aspect kept, centred). Modern: it draws the X in `Muted` at rest and in `Text` while hovered, with no sprite.
- [ ] The bar draws its title ellipsised to end 4 points before the close button.
- [ ] `GetTooltipText()` on the close button returns its `Tooltip`, `"Close"` by default.
- [ ] `UiPluginContentHost` is click-through, anchored on all four edges, and clips its children.

**Verify:** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~PluginTitleBarTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests**

`tests/AcDream.App.Tests/UI/PluginTitleBarTests.cs`:

```csharp
using System.Numerics;
using AcDream.App.UI;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

public sealed class PluginTitleBarTests
{
    private const uint CloseTexture = 77u;

    private static (uint, int, int) Resolve(uint id) =>
        id == PluginWindowChrome.ClassicCloseSprite ? (CloseTexture, 24, 23) : (0u, 0, 0);

    private static (UiRoot Root, UiPanel Frame, PluginTitleBar Bar) Mount(string title = "Buffs")
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var frame = new UiPanel { Left = 100, Top = 100, Width = 300, Height = 200 };
        var bar = new PluginTitleBar(Resolve, frame.Width) { Title = title };
        frame.AddChild(bar);
        root.AddChild(frame);
        root.Tick(0, 0);
        return (root, frame, bar);
    }

    [Fact]
    public void TheBarSpansTheTopAndTheCloseButtonSitsBelowTheBorderAtTheRight()
    {
        var (_, _, bar) = Mount();
        Assert.Equal((0f, 0f, 300f, 24f), (bar.Left, bar.Top, bar.Width, bar.Height));
        Assert.Equal(AnchorEdges.Left | AnchorEdges.Top | AnchorEdges.Right, bar.Anchors);
        Assert.True(bar.ClickThrough);
        PluginCloseButton close = bar.Close;
        Assert.Equal((276f, 5f, 19f, 19f), (close.Left, close.Top, close.Width, close.Height));
        Assert.Equal(AnchorEdges.Top | AnchorEdges.Right, close.Anchors);
        Assert.True(close.TabStop);
        Assert.True(close.AcceptsFocus);
        Assert.False(close.FocusOnMouseClick);
    }

    [Fact]
    public void ATheme_CentresTheCloseButtonInTheBand()
    {
        var (_, _, bar) = Mount();
        bar.ThemePalette = PluginUiPalette.Moss;
        Assert.Same(PluginUiPalette.Moss, bar.Close.ThemePalette);
        Assert.Equal(3f, bar.Close.Top);
        bar.ThemePalette = null;
        Assert.Null(bar.Close.ThemePalette);
        Assert.Equal(5f, bar.Close.Top);
    }

    [Fact]
    public void ClickingTheCloseButtonRequestsClose()
    {
        var (root, _, bar) = Mount();
        int requests = 0;
        bar.CloseRequested += () => requests++;
        (int x, int y) = (100 + 276 + 9, 100 + 5 + 9);
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
        Assert.Equal(1, requests);
        Assert.Null(root.KeyboardFocus);   // a click never takes focus from the game
    }

    [Theory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Space)]
    public void EnterOrSpaceOnTheFocusedCloseButtonRequestsClose(Key key)
    {
        var (root, _, bar) = Mount();
        int requests = 0;
        bar.CloseRequested += () => requests++;
        root.SetKeyboardFocus(bar.Close);
        root.OnKeyDown((int)key);
        Assert.Equal(1, requests);
    }

    [Fact]
    public void Classic_DrawsTheRetailCloseArt()
    {
        var (root, _, _) = Mount();
        var (renderer, ctx) = ThemeDrawCapture.Context(800, 600);
        root.Draw(ctx);
        Assert.Contains(renderer.DebugSpriteSegmentVerts, s => s.Texture == CloseTexture);
    }

    [Fact]
    public void Modern_DrawsAMutedXThatTakesTheTextColourOnHover()
    {
        var (root, _, bar) = Mount();
        var p = PluginUiPalette.Moss;
        bar.ThemePalette = p;

        var (renderer, ctx) = ThemeDrawCapture.Context(800, 600);
        root.Draw(ctx);
        Assert.DoesNotContain(renderer.DebugSpriteSegmentVerts, s => s.Texture == CloseTexture);
        var rest = ThemeDrawCapture.Vertices(renderer);
        Assert.True(ThemeDrawCapture.HasColor(rest, p.Muted));
        Assert.False(ThemeDrawCapture.HasColor(rest, p.Text));

        root.OnMouseMove(100 + 276 + 9, 100 + 3 + 9);
        (renderer, ctx) = ThemeDrawCapture.Context(800, 600);
        root.Draw(ctx);
        Assert.True(ThemeDrawCapture.HasColor(ThemeDrawCapture.Vertices(renderer), p.Text));
    }

    [Fact]
    public void TheTitleIsShortenedToEndBeforeTheCloseButton()
    {
        var font = BundledUiFont.Bake(12).CreateFont(1);
        var (_, _, bar) = Mount(new string('W', 80));
        bar.DatFont = font;
        string shown = bar.DisplayedTitle(s => font.MeasureWidth(s));
        Assert.EndsWith("...", shown);
        Assert.True(PluginTitleBar.ClassicTextLeft + font.MeasureWidth(shown) <= bar.Close.Left - 4f);
    }

    [Fact]
    public void TheCloseButtonSaysWhatItDoes()
    {
        var (_, _, bar) = Mount();
        Assert.Equal("Close", bar.Close.GetTooltipText());
        bar.Close.Tooltip = PluginWindowChrome.DockedCloseTooltip;
        Assert.Equal("Close (reopen from the dock)", bar.Close.GetTooltipText());
    }

    [Fact]
    public void TheContentHostIsATransparentStretchingClip()
    {
        var host = new UiPluginContentHost();
        Assert.True(host.ClickThrough);
        Assert.Equal(AnchorEdges.Left | AnchorEdges.Top | AnchorEdges.Right | AnchorEdges.Bottom, host.Anchors);
        Assert.True(host.ClipsChildrenForTest);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~PluginTitleBarTests"`
Expected: build FAIL — `PluginTitleBar` does not exist.

- [ ] **Step 3: Add the themed close drawing to `PluginUiStyle`**

In `src/AcDream.App/UI/PluginUiStyle.cs`, add `internal const float CloseGlyphSize = 8f;` with the other constants, and after `GhostButton`:

```csharp
    /// <summary>
    /// A title bar's close button: a ghost face and an X, muted at rest and in
    /// the text colour while hovered or pressed, ringed while focused.
    /// </summary>
    internal static void CloseButton(
        UiRenderContext ctx, PluginUiPalette p, float w, float h, UiControlState state, bool focused)
    {
        GhostButton(ctx, p, w, h, state);
        Vector4 color = state is UiControlState.Hovered or UiControlState.Pressed ? p.Text : p.Muted;
        float x = (w - CloseGlyphSize) / 2f, y = (h - CloseGlyphSize) / 2f;
        ctx.DrawSmoothLine(x, y, x + CloseGlyphSize, y + CloseGlyphSize, color, 1.5f);
        ctx.DrawSmoothLine(x + CloseGlyphSize, y, x, y + CloseGlyphSize, color, 1.5f);
        if (focused) FocusRing(ctx, p, w, h, SmallRadius);
    }
```

(`DrawSmoothLine` fills through `CanvasGeometry.FillConvexPolygon`, the painter-v2 shape path the spec asks for.)

- [ ] **Step 4: Write the content host**

`src/AcDream.App/UI/UiPluginContentHost.cs`:

```csharp
namespace AcDream.App.UI;

/// <summary>
/// The content area of a plugin window with chrome: owns every authored
/// child, sits inside the frame's border and title bar, and stretches with
/// the frame. It draws nothing and lets presses through to the frame, so the
/// window still drags from any empty point.
/// </summary>
internal sealed class UiPluginContentHost : UiElement
{
    public UiPluginContentHost()
    {
        ClickThrough = true;
        Anchors = AnchorEdges.Left | AnchorEdges.Top | AnchorEdges.Right | AnchorEdges.Bottom;
    }

    internal bool ClipsChildrenForTest => ClipsChildren;
}
```

Check `UiElement` has no abstract members that need overriding (it is the base of `UiLabel`, which overrides only what it needs); if `OnDraw` is abstract, add an empty `protected override void OnDraw(UiRenderContext ctx) { }`.

- [ ] **Step 5: Write the bar and close button**

`src/AcDream.App/UI/PluginTitleBar.cs`:

```csharp
using System.Numerics;

namespace AcDream.App.UI;

/// <summary>
/// The chrome layer of a plugin window: the window's name and a close button
/// across the top of the frame. It never scrolls or takes part in layout,
/// and comes before the content in Tab order. Presses on it outside the
/// close button fall through to the frame, which drags.
/// </summary>
internal sealed class PluginTitleBar : UiElement
{
    internal const float ClassicTextLeft = 8f;
    internal const float ThemedTextLeft = 12f;
    private const float ThemedCloseTop = 3f;
    private const float TextGap = 4f;

    private PluginUiPalette? _palette;
    private string? _shownFor;
    private float _shownWidth = float.NaN;
    private UiDatFont? _shownFont;
    private string _shown = string.Empty;

    public PluginTitleBar(Func<uint, (uint, int, int)> resolve, float frameWidth)
    {
        ClickThrough = true;
        Width = frameWidth;
        Height = PluginWindowChrome.TitleBarHeight;
        Anchors = AnchorEdges.Left | AnchorEdges.Top | AnchorEdges.Right;
        Close = new PluginCloseButton(resolve)
        {
            Left = frameWidth - PluginWindowChrome.TitleBarHeight,
            Top = PluginWindowChrome.Border,
            Width = PluginWindowChrome.CloseButtonSize,
            Height = PluginWindowChrome.CloseButtonSize,
        };
        Close.Click += () => CloseRequested?.Invoke();
        AddChild(Close);
    }

    internal PluginCloseButton Close { get; }

    /// <summary>Raised when the player clicks or activates the close button.</summary>
    internal event Action? CloseRequested;

    internal string Title { get; set; } = string.Empty;
    internal UiDatFont? DatFont { get; set; }
    internal Vector4 TextColor { get; set; } = Vector4.One;
    internal bool Outline { get; set; } = true;

    /// <summary>The shared theme the bar draws in; null draws Classic.</summary>
    internal PluginUiPalette? ThemePalette
    {
        get => _palette;
        set
        {
            _palette = value;
            Close.ThemePalette = value;
            Close.Top = value is null ? PluginWindowChrome.Border : ThemedCloseTop;
        }
    }

    /// <summary>The title as drawn: shortened to end before the close button. Cached per text, width and font.</summary>
    internal string DisplayedTitle(Func<string, float> measure)
    {
        float left = _palette is null ? ClassicTextLeft : ThemedTextLeft;
        float width = MathF.Max(0f, Close.Left - TextGap - left);
        if (!ReferenceEquals(_shownFor, Title) || _shownWidth != width || !ReferenceEquals(_shownFont, DatFont))
        {
            _shown = PluginWindowChrome.Ellipsize(Title, measure, width);
            _shownFor = Title;
            _shownWidth = width;
            _shownFont = DatFont;
        }
        return _shown;
    }

    protected override void OnDraw(UiRenderContext ctx)
    {
        if (Title.Length == 0) return;
        UiDatFont? dat = DatFont;
        var font = ctx.DefaultFont;
        Func<string, float> measure = dat is not null ? s => dat.MeasureWidth(s)
            : font is not null ? s => font.MeasureWidth(s)
            : static s => s.Length * 7f;
        string text = DisplayedTitle(measure);
        if (text.Length == 0) return;
        float lineHeight = DatFont?.LineHeight ?? ctx.DefaultFont?.LineHeight ?? 14f;
        float x, y;
        if (_palette is null)
        {
            x = ClassicTextLeft;
            y = PluginWindowChrome.Border
                + MathF.Floor((PluginWindowChrome.CloseButtonSize - lineHeight) / 2f + 0.5f);
        }
        else
        {
            x = ThemedTextLeft;
            y = MathF.Floor((PluginWindowChrome.TitleBarHeight - lineHeight) / 2f + 0.5f);
        }
        if (DatFont is { } datFont)
            ctx.DrawStringDat(datFont, text, x, y, TextColor, Outline);
        else if (ctx.DefaultFont is not null)
            ctx.DrawString(text, x, y, TextColor);
    }
}

/// <summary>
/// A plugin window's close button: the retail close art in Classic, a ghost
/// X in the modern themes. Keyboard-focusable like other markup buttons;
/// a click does not take focus from the game.
/// </summary>
internal sealed class PluginCloseButton : UiSimpleButton
{
    private readonly Func<uint, (uint, int, int)> _resolve;

    public PluginCloseButton(Func<uint, (uint, int, int)> resolve)
    {
        _resolve = resolve;
        AcceptsFocus = true;
        TabStop = true;
        FocusOnMouseClick = false;
        Outline = false;
        BackgroundColor = Vector4.Zero;
        BorderColor = Vector4.Zero;
        Anchors = AnchorEdges.Top | AnchorEdges.Right;
    }

    internal string Tooltip { get; set; } = PluginWindowChrome.CloseTooltip;

    public override string? GetTooltipText() => Tooltip;

    protected override void OnDraw(UiRenderContext ctx)
    {
        if (ThemePalette is { } palette)
        {
            PluginUiStyle.CloseButton(ctx, palette, Width, Height, ThemeState, KeyboardFocused);
            return;
        }
        var (tex, tw, th) = _resolve(PluginWindowChrome.ClassicCloseSprite);
        if (tex != 0u && tw > 0 && th > 0)
        {
            float scale = MathF.Min(Width / tw, Height / th);
            float w = tw * scale, h = th * scale;
            ctx.DrawSprite(tex, (Width - w) / 2f, (Height - h) / 2f, w, h, 0f, 0f, 1f, 1f, Vector4.One);
        }
        if (KeyboardFocused)
            ctx.DrawRectOutline(1f, 1f, Width - 2f, Height - 2f, new Vector4(1f, 0.82f, 0.25f, 1f), 1f);
    }
}
```

If `UiSimpleButton.GetTooltipText` is not virtual on `UiElement` with that exact signature, match the signature `PluginMinimizeButton` uses (`public override string? GetTooltipText()`); it is the same base.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~PluginTitleBarTests"`
Expected: PASS (all). If the hover test fails because the pointer state needs a tick between move and draw, add `root.Tick(0.016, 1);` after `OnMouseMove`. Do not weaken the assertions.

- [ ] **Step 7: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/PluginTitleBar.cs src/AcDream.App/UI/UiPluginContentHost.cs src/AcDream.App/UI/PluginUiStyle.cs tests/AcDream.App.Tests/UI/PluginTitleBarTests.cs
git commit -m "feat(ui): plugin window title bar, close button and content host

Claude-Session: https://claude.ai/code/session_019dvJ5gakBkq3uJyr1PAC3u"
```

---

### Task 3: `titlebar` in markup, content area and `BuildWindow`

**Goal:** `<panel titlebar="true">` builds a frame with a title bar and a content host, with authored children placed in it and the frame sized to content plus chrome. Every other root builds exactly as today.

**Files:**
- Create: `src/AcDream.App/UI/MarkupWindow.cs`
- Modify: `src/AcDream.App/UI/MarkupDocument.cs:21-91` (`Build` → `BuildWindow`)
- Modify: `src/AcDream.App/UI/PluginMarkupTheme.cs` (add `RegisterTitleBar`)
- Test: `tests/AcDream.App.Tests/UI/MarkupTitleBarTests.cs`

**Acceptance Criteria:**
- [ ] Defaults (spec 2.2, PR 2 rows): no `titlebar` → no bar and no content host (children are direct children of the frame, as today); `titlebar="true"` → bar; `titlebar="false"` → no bar. Any other value (`"yes"`, `"True"`, `""`) → `FormatException` naming `titlebar` and the value.
- [ ] With a bar: the frame's children are exactly `[PluginTitleBar, UiPluginContentHost]` in that order; the host is at (5, 24, w, h); the frame is (w + 10) × (h + 29); `MinWidth`/`MinHeight` are `minw`/`minh` (default `w`/`h`) plus the chrome.
- [ ] An absolute child at `x="0" y="0"` lands at frame-local (5, 24) and screen (frameX + 5, frameY + 24); its click still runs its bound action.
- [ ] The bar's title is the markup `title`, else `fallbackTitle`, else empty, and no (8, 4) title `UiLabel` is created.
- [ ] Pressing on the bar's title area and moving drags the window.
- [ ] Tab from the last content control reaches the close button first in the window's order (chrome before content).
- [ ] `MarkupWindow.ContentRoot` is the content host with a bar and the frame without one; `AuthoredGeometryRevision` is the authored-inputs revision with a bar and today's `ComputeAuthoredGeometryRevision(w, h, minw, minh, resizable)` without one.
- [ ] Themed with a bar: `HasTitle` is on (the header band draws); under Moss/Brass the bar's palette is set, its font is `ModernTitleFont` and its text colour the palette's `Text` without outline; back to Classic restores the classic font, `ControlsIni` title colour and outline.
- [ ] Resizing a bar window: the content host keeps its insets (frame 400×300 → host 390×271) and an anchored `right bottom` child follows the host.
- [ ] All existing markup, theme, identity and persistence suites pass unchanged.

**Verify:** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~MarkupTitleBarTests|FullyQualifiedName~MarkupDocumentTests|FullyQualifiedName~PluginTheme|FullyQualifiedName~MarkupPanelClickTests|FullyQualifiedName~MarkupResizableAnchorTests|FullyQualifiedName~RetailWindowLayoutPersistenceTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests**

`tests/AcDream.App.Tests/UI/MarkupTitleBarTests.cs`:

```csharp
using System.Numerics;
using AcDream.App.UI;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

public sealed class MarkupTitleBarTests
{
    private sealed class Binding
    {
        public int Clicks { get; private set; }
        public Action Go => () => Clicks++;
    }

    private static MarkupWindow Window(string attrs, string body = "", object? binding = null,
        string? fallback = null, PluginUiThemeSettings? themes = null, UiDatFont? font = null) =>
        MarkupDocument.BuildWindow(
            $"<panel x=\"100\" y=\"50\" w=\"300\" h=\"200\" {attrs}>{body}</panel>",
            binding ?? new object(), _ => (0u, 0, 0), datFont: font, themes: themes, fallbackTitle: fallback);

    private static UiRoot Mount(MarkupWindow window)
    {
        var root = new UiRoot { Width = 1280, Height = 720 };
        root.AddChild(window.Frame);
        root.Tick(0, 0);
        return root;
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("titlebar=\"false\"", false)]
    [InlineData("titlebar=\"true\"", true)]
    public void TheBarIsOptIn(string attrs, bool expected)
    {
        MarkupWindow window = Window(attrs, "<label x=\"0\" y=\"0\" text=\"a\" />");
        Assert.Equal(expected, window.TitleBar is not null);
        Assert.Equal(expected, window.Frame.Children.OfType<PluginTitleBar>().Any());
        Assert.Equal(expected, window.Frame.Children.OfType<UiPluginContentHost>().Any());
        if (!expected)
        {
            Assert.Same(window.Frame, window.ContentRoot);
            Assert.IsType<UiLabel>(Assert.Single(window.Frame.Children));
        }
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("True")]
    [InlineData("")]
    public void AnythingButTrueOrFalseIsABuildError(string value)
    {
        var ex = Assert.Throws<FormatException>(() => Window($"titlebar=\"{value}\""));
        Assert.Contains("titlebar", ex.Message);
        Assert.Contains($"\"{value}\"", ex.Message);
    }

    [Fact]
    public void WithABar_TheFrameIsTheContentAreaPlusChrome()
    {
        MarkupWindow window = Window("titlebar=\"true\" minw=\"250\"");
        UiNineSlicePanel frame = window.Frame;
        Assert.Equal((100f, 50f, 310f, 229f), (frame.Left, frame.Top, frame.Width, frame.Height));
        Assert.Equal((260f, 229f), (frame.MinWidth, frame.MinHeight));
        Assert.Collection(frame.Children,
            c => Assert.IsType<PluginTitleBar>(c),
            c => Assert.IsType<UiPluginContentHost>(c));
        UiElement host = window.ContentRoot;
        Assert.IsType<UiPluginContentHost>(host);
        Assert.Equal((5f, 24f, 300f, 200f), (host.Left, host.Top, host.Width, host.Height));
    }

    [Fact]
    public void ChildrenArePlacedFromTheContentAreaCorner_AndStillClick()
    {
        var binding = new Binding();
        MarkupWindow window = Window("titlebar=\"true\"",
            "<button x=\"0\" y=\"0\" w=\"80\" h=\"20\" text=\"Go\" onclick=\"{Go}\" />", binding);
        UiRoot root = Mount(window);
        var button = Assert.IsType<UiSimpleButton>(Assert.Single(window.ContentRoot.Children));
        Assert.Equal((0f, 0f), (button.Left, button.Top));
        Assert.Same(button, root.Pick(100 + 5 + 40, 50 + 24 + 10));
        root.OnMouseDown(UiMouseButton.Left, 145, 84);
        root.OnMouseUp(UiMouseButton.Left, 145, 84);
        Assert.Equal(1, binding.Clicks);
    }

    [Theory]
    [InlineData("title=\"Buff Bot\"", "Registered", "Buff Bot")]
    [InlineData("", "Registered", "Registered")]
    [InlineData("title=\"\"", "Registered", "Registered")]
    [InlineData("", null, "")]
    public void TheBarShowsTheMarkupTitleElseTheRegistrationTitle(string attrs, string? fallback, string expected)
    {
        MarkupWindow window = Window($"titlebar=\"true\" {attrs}", fallback: fallback);
        Assert.Equal(expected, window.TitleBar!.Title);
        Assert.Empty(window.ContentRoot.Children.OfType<UiLabel>());
        Assert.Empty(window.Frame.Children.OfType<UiLabel>());
    }

    [Fact]
    public void DraggingTheBarMovesTheWindow()
    {
        MarkupWindow window = Window("titlebar=\"true\" title=\"T\"");
        UiRoot root = Mount(window);
        root.OnMouseDown(UiMouseButton.Left, 160, 62);
        root.OnMouseMove(200, 92);
        root.OnMouseUp(UiMouseButton.Left, 200, 92);
        Assert.Equal((140f, 80f), (window.Frame.Left, window.Frame.Top));
    }

    [Fact]
    public void TheCloseButtonComesFirstInTabOrder()
    {
        MarkupWindow window = Window("titlebar=\"true\"",
            "<button x=\"0\" y=\"0\" w=\"80\" h=\"20\" text=\"Go\" />");
        UiRoot root = Mount(window);
        var button = Assert.IsType<UiSimpleButton>(Assert.Single(window.ContentRoot.Children));
        root.SetKeyboardFocus(button);
        root.OnKeyDown((int)Key.Tab);
        Assert.Same(window.TitleBar!.Close, root.KeyboardFocus);
        root.OnKeyDown((int)Key.Tab);
        Assert.Same(button, root.KeyboardFocus);
    }

    [Fact]
    public void TheRevisionIsAuthoredInputsWithABar_AndTodaysHashWithout()
    {
        MarkupWindow bar = Window("titlebar=\"true\"");
        Assert.Equal(
            RetailWindowManager.ComputeAuthoredGeometryRevision(
                ["300", "200", null, null, null, null, null, "true", "1"]),
            bar.AuthoredGeometryRevision);

        MarkupWindow plain = Window("");
        UiNineSlicePanel f = plain.Frame;
        Assert.Equal(
            RetailWindowManager.ComputeAuthoredGeometryRevision(f.Width, f.Height, f.MinWidth, f.MinHeight, f.Resizable),
            plain.AuthoredGeometryRevision);
    }

    [Fact]
    public void ResizingKeepsTheInsets_AndAnchoredContentFollows()
    {
        MarkupWindow window = Window("titlebar=\"true\" resizable=\"true\"",
            "<button x=\"210\" y=\"170\" w=\"80\" h=\"20\" text=\"OK\" anchor=\"right bottom\" />");
        UiRoot root = Mount(window);
        RetailWindowHandle handle = root.RegisterWindow("w", window.Frame, window.ContentRoot);
        handle.ResizeTo(400f, 300f);
        var (renderer, ctx) = ThemeDrawCapture.Context(1280, 720);
        root.Draw(ctx);
        UiElement host = window.ContentRoot;
        Assert.Equal((5f, 24f, 390f, 271f), (host.Left, host.Top, host.Width, host.Height));
        var ok = Assert.Single(host.Children);
        Assert.Equal((300f, 241f), (ok.Left, ok.Top));
    }

    [Fact]
    public void Themed_TheBarFollowsTheThemeAndBack()
    {
        var classic = BundledUiFont.Bake(12).CreateFont(1);
        var bold = BundledUiFont.Bake(16, BundledUiFontWeight.SemiBold).CreateFont(3);
        var settings = new PluginUiThemeSettings(modernTitleFont: new(() => bold));
        MarkupWindow window = Window("titlebar=\"true\" title=\"T\" theme=\"plugin\"", themes: settings, font: classic);
        UiRoot root = Mount(window);
        var panel = Assert.IsType<UiPluginMarkupPanel>(window.Frame);
        PluginTitleBar bar = window.TitleBar!;
        Assert.True(panel.HasTitle);
        Assert.Null(bar.ThemePalette);
        Assert.Same(classic, bar.DatFont);
        Assert.True(bar.Outline);
        Vector4 classicColor = bar.TextColor;

        settings.Theme = PluginUiTheme.Brass;
        root.Tick(0.016, 1);
        Assert.Same(PluginUiPalette.Brass, bar.ThemePalette);
        Assert.Same(bold, bar.DatFont);
        Assert.Equal(PluginUiPalette.Brass.Text, bar.TextColor);
        Assert.False(bar.Outline);

        settings.Theme = PluginUiTheme.Classic;
        root.Tick(0.016, 2);
        Assert.Null(bar.ThemePalette);
        Assert.Same(classic, bar.DatFont);
        Assert.Equal(classicColor, bar.TextColor);
        Assert.True(bar.Outline);
    }
}
```

The drag test assumes `UiRoot` drags a `Draggable` frame when the press lands on click-through chrome (the frame is hit). Point (160, 62) is frame-local (60, 12): inside the bar, left of the close button (x ≥ 276), and away from the 5-point resize edges. If it fails, find out why with `root.Pick(160, 62)` before changing anything; the press must reach the frame.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~MarkupTitleBarTests"`
Expected: build FAIL — `MarkupWindow` / `BuildWindow` do not exist.

- [ ] **Step 3: Write `MarkupWindow`**

`src/AcDream.App/UI/MarkupWindow.cs` (Task 4 adds `Register`):

```csharp
namespace AcDream.App.UI;

/// <summary>
/// A built markup window and what mounting it needs: the frame to add to
/// the root, the element that holds its authored content (the frame itself
/// unless the window has chrome), its title bar if it has one, and the
/// revision its saved size is kept under.
/// </summary>
internal sealed class MarkupWindow(
    UiNineSlicePanel frame, UiElement contentRoot, PluginTitleBar? titleBar, int authoredGeometryRevision)
{
    public UiNineSlicePanel Frame { get; } = frame;
    public UiElement ContentRoot { get; } = contentRoot;
    public PluginTitleBar? TitleBar { get; } = titleBar;
    public int AuthoredGeometryRevision { get; } = authoredGeometryRevision;
}
```

- [ ] **Step 4: Replace `Build` with `BuildWindow`**

In `src/AcDream.App/UI/MarkupDocument.cs`, replace the whole `Build` method (lines 21–91) with:

```csharp
    public static UiNineSlicePanel Build(
        string xml, object binding, Func<uint, (uint, int, int)> resolve,
        ControlsIni? style = null, UiDatFont? datFont = null,
        IMarkupIconResolver? icons = null, PluginUiThemeSettings? themes = null)
        => BuildWindow(xml, binding, resolve, style, datFont, icons, themes).Frame;

    /// <summary>
    /// Builds a markup window and what mounting it needs (see
    /// <see cref="MarkupWindow"/>). <paramref name="fallbackTitle"/> names the
    /// title bar when the markup has no <c>title</c>: the registration's title.
    /// </summary>
    internal static MarkupWindow BuildWindow(
        string xml, object binding, Func<uint, (uint, int, int)> resolve,
        ControlsIni? style = null, UiDatFont? datFont = null,
        IMarkupIconResolver? icons = null, PluginUiThemeSettings? themes = null,
        string? fallbackTitle = null)
    {
        var root = XDocument.Parse(xml).Root ?? throw new FormatException("empty markup");
        if (root.Name.LocalName != "panel")
            throw new FormatException($"root must be <panel>, got <{root.Name.LocalName}>");

        bool themed = (string?)root.Attribute("theme") == "plugin";
        if (themed) themes ??= new PluginUiThemeSettings();
        UiNineSlicePanel panel = themed
            ? new UiPluginMarkupPanel(resolve, themes ?? new PluginUiThemeSettings())
            : new UiNineSlicePanel(resolve);

        // A window with chrome has a content area: its authored sizes are the
        // area inside the border and title bar, and the frame adds the chrome.
        bool hasTitleBar = TitleBar(root);
        float chromeW = hasTitleBar ? PluginWindowChrome.HorizontalInsets : 0f;
        float chromeH = hasTitleBar ? PluginWindowChrome.VerticalInsets : 0f;
        float contentW = F(root, "w"), contentH = F(root, "h");
        panel.Left = F(root, "x"); panel.Top = F(root, "y");
        panel.Width = contentW + chromeW; panel.Height = contentH + chromeH;

        bool resizable = B(root, "resizable", false);
        panel.Resizable = resizable;
        panel.MinWidth = FOr(root, "minw", contentW) + chromeW;
        panel.MinHeight = FOr(root, "minh", contentH) + chromeH;
        panel.ResizeX = resizable;
        panel.ResizeY = resizable;

        string? resize = (string?)root.Attribute("resize");
        if (resize is not null)
        {
            panel.ResizeX = resize is "x" or "both";
            panel.ResizeY = resize is "y" or "both";
        }

        string? visible = (string?)root.Attribute("visible");
        if (visible is not null && IsBinding(visible))
        {
            PropertyInfo? flag = binding.GetType().GetProperty(visible[1..^1]);
            if (flag is null || flag.PropertyType != typeof(bool))
            {
                throw new FormatException(
                    $"<panel visible=\"{visible}\"> did not resolve to a bool property "
                    + $"on {binding.GetType().Name}");
            }
            panel.VisibleSource = () => flag.GetValue(binding) is true;
        }

        string? title = (string?)root.Attribute("title");
        Vector4 titleColor = style is not null && style.TryColor("title", "color", out var c) ? c : Vector4.One;
        UiElement contentParent = panel;
        PluginTitleBar? titleBar = null;
        if (hasTitleBar)
        {
            titleBar = new PluginTitleBar(resolve, panel.Width)
            {
                Title = string.IsNullOrEmpty(title) ? fallbackTitle ?? string.Empty : title,
                DatFont = datFont,
                TextColor = titleColor,
            };
            panel.AddChild(titleBar);
            var content = new UiPluginContentHost
            {
                Left = PluginWindowChrome.Border,
                Top = PluginWindowChrome.TitleBarHeight,
                Width = contentW,
                Height = contentH,
            };
            panel.AddChild(content);
            contentParent = content;
            if (panel is UiPluginMarkupPanel barPanel)
            {
                barPanel.HasTitle = true;
                PluginMarkupTheme.RegisterTitleBar(barPanel, titleBar);
            }
        }
        else if (!string.IsNullOrEmpty(title))
        {
            panel.AddChild(new UiLabel
            {
                Text = title, Left = 8, Top = 4, TextColor = titleColor, DatFont = datFont,
            });
            if (panel is UiPluginMarkupPanel titlePanel && panel.Children[0] is UiLabel titleLabel)
            {
                titlePanel.HasTitle = true;
                PluginMarkupTheme.Register(titlePanel, titleLabel, new XElement("label"));
                PluginMarkupTheme.RegisterTitle(titlePanel, titleLabel);
            }
        }

        foreach (var el in root.Elements())
            AddElement(contentParent, el, binding, resolve, datFont, icons, panel as UiPluginMarkupPanel, themes);

        // The whole document now sits at its authored sizes, so this is the one
        // moment every anchor margin can be read off the layout its author wrote.
        // Pin them here rather than on first draw: a group that is hidden when the
        // window is resized is never reached by the draw-time anchor pass, and
        // would otherwise measure its children against the resized parent the first
        // time it is opened.
        panel.CaptureAuthoredAnchorBaselines();

        int revision = hasTitleBar
            ? RetailWindowManager.ComputeAuthoredGeometryRevision(PluginWindowChrome.AuthoredInputs(root))
            : RetailWindowManager.ComputeAuthoredGeometryRevision(
                panel.Width, panel.Height, panel.MinWidth, panel.MinHeight, panel.Resizable);
        return new MarkupWindow(panel, contentParent, titleBar, revision);
    }

    /// <summary>
    /// Whether the root asks for the host title bar. Off unless
    /// <c>titlebar="true"</c>; PR 3 turns it on by default for roots with
    /// <c>layout</c>.
    /// </summary>
    private static bool TitleBar(XElement root) => (string?)root.Attribute("titlebar") switch
    {
        null or "false" => false,
        "true" => true,
        string other => throw new FormatException(
            $"<panel titlebar=\"{other}\"> must be true or false"),
    };
```

The no-bar path must stay byte-identical: today's `MinWidth = FOr(root, "minw", panel.Width)` with `panel.Width = F(root, "w")` is the same as `FOr(root, "minw", contentW) + 0`. The title label code and its theming calls are unchanged apart from reading `titleColor`.

- [ ] **Step 5: Add `RegisterTitleBar`**

In `src/AcDream.App/UI/PluginMarkupTheme.cs`, after `RegisterTitle`:

```csharp
    /// <summary>
    /// Themes a window's title bar: the palette for its band and close button,
    /// the title weight without outline under a theme, and Classic's own font,
    /// title colour and outline back under Classic. The modern fonts are read
    /// only once a theme is on, so a Classic window never loads them.
    /// </summary>
    public static void RegisterTitleBar(UiPluginMarkupPanel panel, PluginTitleBar bar)
    {
        UiDatFont? classicFont = bar.DatFont;
        var classicColor = bar.TextColor;
        panel.AddThemeAction(p =>
        {
            bar.ThemePalette = p;
            bar.DatFont = p is null ? classicFont : panel.ModernTitleFont ?? panel.ModernFont ?? classicFont;
            bar.TextColor = p?.Text ?? classicColor;
            bar.Outline = p is null;
        });
    }
```

- [ ] **Step 6: Run the new and the guarding suites**

Run: `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~MarkupTitleBarTests|FullyQualifiedName~MarkupDocumentTests|FullyQualifiedName~PluginTheme|FullyQualifiedName~MarkupPanelClickTests|FullyQualifiedName~MarkupResizableAnchorTests|FullyQualifiedName~RetailWindowLayoutPersistenceTests"`
Expected: PASS (all). An existing suite failing means the no-bar path changed. Fix the code, not the test.

- [ ] **Step 7: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/MarkupWindow.cs src/AcDream.App/UI/MarkupDocument.cs src/AcDream.App/UI/PluginMarkupTheme.cs tests/AcDream.App.Tests/UI/MarkupTitleBarTests.cs
git commit -m "feat(ui): titlebar markup attribute with a content area inside the chrome

Claude-Session: https://claude.ai/code/session_019dvJ5gakBkq3uJyr1PAC3u"
```

---

### Task 4: Mounting: register the content root, wire close, and the dock

**Goal:** Plugin windows mount through `MarkupWindow.Register`, so the close button clears the player's request through `RetailWindowManager.Close`, and the dock leaves its "–" off windows that have a bar.

**Files:**
- Modify: `src/AcDream.App/UI/MarkupWindow.cs` (add `Register`)
- Modify: `src/AcDream.App/UI/RetailUiRuntime.cs:4103-4160` (`MountPlugins`)
- Modify: `src/AcDream.App/UI/PluginSidePanel.cs:166-176, 542-543, 631-632, 641` (minimize optional)
- Test: `tests/AcDream.App.Tests/UI/MarkupWindowMountTests.cs`

**Acceptance Criteria:**
- [ ] `MarkupWindow.Register(manager, name, controller)` registers the frame with `ContentRoot` = the content host (or the frame) and the window's `AuthoredGeometryRevision`.
- [ ] Clicking the close button of a registered window behind a `PluginWindowVisibilityController` hides it, raises the handle's `Hidden` then `Closed`, and clears the player's request: after the next tick it stays hidden, and a bound `visible` going false→true does not reopen it; `handle.Show()` does.
- [ ] A window with a bar added to the dock gets no "–" child, and its close button's tooltip is `"Close (reopen from the dock)"`. A window without a bar still gets "–" (existing `PluginSidePanelThemeTests` pass unchanged). Removing either kind from the dock does not throw.
- [ ] A saved size survives a fresh session with the same `titlebar` markup. Adopting `titlebar="true"` on a window that was saved without it resets the size to the new authored frame size and keeps the position.
- [ ] `MountPlugins` builds with `BuildWindow(..., fallbackTitle: panel.Descriptor.Title)` and registers through `MarkupWindow.Register`. Behaviour for windows without a bar is unchanged.

**Verify:** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~MarkupWindowMountTests|FullyQualifiedName~PluginSidePanel|FullyQualifiedName~PluginDock|FullyQualifiedName~PluginShelf|FullyQualifiedName~RetailWindowLayoutPersistenceTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests**

`tests/AcDream.App.Tests/UI/MarkupWindowMountTests.cs`. First read `RetailWindowLayoutPersistenceTests.cs` for its `PathName` field and `SettingsStore` setup, and copy the same disposable temp-store pattern into this class. Copy the `using` lines `PluginSidePanelThemeTests.cs` needs for `PluginUiOwner`/`PluginPanelDescriptor`, and the ones the persistence tests need for `SettingsStore`:

```csharp
using AcDream.App.UI;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.UI;

public sealed class MarkupWindowMountTests
{
    private sealed class Binding
    {
        public bool Available { get; set; } = true;
    }

    private const string BarXml =
        "<panel x=\"100\" y=\"100\" w=\"300\" h=\"200\" titlebar=\"true\" title=\"T\" visible=\"{Available}\" />";

    private static (UiRoot Root, MarkupWindow Window, RetailWindowHandle Handle, Binding Bound) Mount(
        string xml = BarXml)
    {
        var bound = new Binding();
        MarkupWindow window = MarkupDocument.BuildWindow(xml, bound, _ => (0u, 0, 0));
        var visibility = new PluginWindowVisibilityController(window.Frame.VisibleSource, startVisible: true);
        window.Frame.VisibleSource = visibility.ShouldBeVisible;
        window.Frame.Visible = visibility.ShouldBeVisible();
        var root = new UiRoot { Width = 1280, Height = 720 };
        root.AddChild(window.Frame);
        RetailWindowHandle handle = window.Register(root.WindowManager, "plugin:t:main", visibility);
        root.Tick(0, 0);
        return (root, window, handle, bound);
    }

    [Fact]
    public void RegisterUsesTheContentHostAndTheAuthoredRevision()
    {
        var (_, window, handle, _) = Mount();
        Assert.Same(window.Frame, handle.OuterFrame);
        Assert.Same(window.ContentRoot, handle.ContentRoot);
        Assert.IsType<UiPluginContentHost>(handle.ContentRoot);
        Assert.Equal(window.AuthoredGeometryRevision, handle.AuthoredGeometryRevision);
    }

    [Fact]
    public void CloseClearsThePlayersRequest_AndRaisesHiddenThenClosed()
    {
        var (root, window, handle, bound) = Mount();
        var events = new List<string>();
        handle.Hidden += _ => events.Add("hidden");
        handle.Closed += _ => events.Add("closed");

        PluginCloseButton close = window.TitleBar!.Close;
        (int x, int y) = (100 + (int)close.Left + 9, 100 + (int)close.Top + 9);
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
        root.Tick(0.016, 1);

        Assert.Equal(new[] { "hidden", "closed" }, events);
        Assert.False(window.Frame.Visible);

        bound.Available = false;
        root.Tick(0.016, 2);
        bound.Available = true;
        root.Tick(0.016, 3);
        Assert.False(window.Frame.Visible);   // the binding does not reopen a window the player closed

        handle.Show();
        root.Tick(0.016, 4);
        Assert.True(window.Frame.Visible);
    }

    [Fact]
    public void TheDockLeavesItsMinimizeOffAWindowWithABar()
    {
        var root = new UiRoot { Width = 1280, Height = 720 };
        var settings = new PluginUiThemeSettings();
        using var shelf = new PluginSidePanel(root.WindowManager, _ => (0u, 0, 0), null, settings);
        root.AddChild(shelf);

        MarkupWindow bar = MarkupDocument.BuildWindow(
            "<panel x=\"100\" y=\"100\" w=\"200\" h=\"100\" titlebar=\"true\" />", new object(), _ => (0u, 0, 0));
        root.AddChild(bar.Frame);
        RetailWindowHandle barHandle = bar.Register(root.WindowManager, "plugin:a:main",
            new PluginWindowVisibilityController(null, true));
        shelf.Add(new PluginUiOwner("a", "A"), new PluginPanelDescriptor("main", "A"), barHandle);

        UiNineSlicePanel plain = MarkupDocument.Build(
            "<panel x=\"100\" y=\"300\" w=\"200\" h=\"100\" />", new object(), _ => (0u, 0, 0));
        root.AddChild(plain);
        RetailWindowHandle plainHandle = root.WindowManager.Register("plugin:b:main", plain);
        shelf.Add(new PluginUiOwner("b", "B"), new PluginPanelDescriptor("main", "B"), plainHandle);

        Assert.DoesNotContain(bar.Frame.Children, c => c is UiSimpleButton { Text: "–" });
        Assert.Equal(PluginWindowChrome.DockedCloseTooltip, bar.TitleBar!.Close.GetTooltipText());
        Assert.Contains(plain.Children, c => c is UiSimpleButton { Text: "–" });

        root.WindowManager.Unregister("plugin:a:main");
        root.WindowManager.Unregister("plugin:b:main");
    }
}
```

Persistence: add to `MarkupWindowMountTests` (with the temp `SettingsStore` set up exactly as `RetailWindowLayoutPersistenceTests` does):

```csharp
    [Fact]
    public void ASavedSizeSurvivesTheSameBarMarkup()
    {
        const string xml = "<panel x=\"0\" y=\"0\" w=\"300\" h=\"200\" resizable=\"true\" titlebar=\"true\" />";
        var store = new SettingsStore(PathName);
        MarkupWindow first = MarkupDocument.BuildWindow(xml, new object(), _ => (1u, 32, 32));
        var root = new UiRoot { Width = 1280, Height = 720 };
        root.AddChild(first.Frame);
        RetailWindowHandle handle = first.Register(root.WindowManager, "w", new PluginWindowVisibilityController(null, true));
        using (new RetailWindowLayoutPersistence(root.WindowManager, store, () => "Alice", () => (1280, 720)))
        {
            handle.MoveTo(50f, 50f);
            handle.ResizeTo(500f, 400f);
        }

        MarkupWindow fresh = MarkupDocument.BuildWindow(xml, new object(), _ => (1u, 32, 32));
        var freshRoot = new UiRoot { Width = 1280, Height = 720 };
        freshRoot.AddChild(fresh.Frame);
        fresh.Register(freshRoot.WindowManager, "w", new PluginWindowVisibilityController(null, true));
        using var persistence = new RetailWindowLayoutPersistence(freshRoot.WindowManager, store, () => "Alice", () => (1280, 720));
        persistence.RestoreAll();
        Assert.Equal((500f, 400f), (fresh.Frame.Width, fresh.Frame.Height));
    }

    [Fact]
    public void AdoptingTheBarResetsTheSavedSizeButKeepsThePosition()
    {
        var store = new SettingsStore(PathName);
        MarkupWindow old = MarkupDocument.BuildWindow(
            "<panel x=\"0\" y=\"0\" w=\"300\" h=\"200\" resizable=\"true\" />", new object(), _ => (1u, 32, 32));
        var root = new UiRoot { Width = 1280, Height = 720 };
        root.AddChild(old.Frame);
        RetailWindowHandle handle = old.Register(root.WindowManager, "w", new PluginWindowVisibilityController(null, true));
        using (new RetailWindowLayoutPersistence(root.WindowManager, store, () => "Alice", () => (1280, 720)))
        {
            handle.MoveTo(120f, 90f);
            handle.ResizeTo(500f, 400f);
        }

        MarkupWindow bar = MarkupDocument.BuildWindow(
            "<panel x=\"0\" y=\"0\" w=\"300\" h=\"200\" resizable=\"true\" titlebar=\"true\" />",
            new object(), _ => (1u, 32, 32));
        var freshRoot = new UiRoot { Width = 1280, Height = 720 };
        freshRoot.AddChild(bar.Frame);
        bar.Register(freshRoot.WindowManager, "w", new PluginWindowVisibilityController(null, true));
        using var persistence = new RetailWindowLayoutPersistence(freshRoot.WindowManager, store, () => "Alice", () => (1280, 720));
        persistence.RestoreAll();
        Assert.Equal((310f, 229f), (bar.Frame.Width, bar.Frame.Height));
        Assert.Equal((120f, 90f), (bar.Frame.Left, bar.Frame.Top));
    }
```

If `RetailWindowLayoutPersistenceTests` makes the class `IDisposable` to delete its temp file, do the same here.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~MarkupWindowMountTests"`
Expected: build FAIL — `MarkupWindow.Register` does not exist.

- [ ] **Step 3: Add `Register`**

In `src/AcDream.App/UI/MarkupWindow.cs`, inside the class:

```csharp
    /// <summary>
    /// Registers the mounted frame under <paramref name="name"/>, with the
    /// content root and the authored revision, and connects the close button
    /// to <see cref="RetailWindowManager.Close"/>: the path a dock slot takes,
    /// so the player's request is cleared and Hidden/Closed fire as usual.
    /// The frame must already be a direct child of the manager's root.
    /// </summary>
    internal RetailWindowHandle Register(
        RetailWindowManager manager, string name, IRetainedPanelController controller)
    {
        RetailWindowHandle handle = manager.Register(
            name, Frame, ContentRoot, controller, authoredGeometryRevision: AuthoredGeometryRevision);
        if (TitleBar is { } bar)
            bar.CloseRequested += () => manager.Close(name);
        return handle;
    }
```

- [ ] **Step 4: Mount through it**

In `RetailUiRuntime.MountPlugins` (`src/AcDream.App/UI/RetailUiRuntime.cs`), replace the build call and the registration:

```csharp
                MarkupWindow window = MarkupDocument.BuildWindow(
                    xml,
                    panel.Binding,
                    _bindings.Assets.ResolveSprite,
                    _bindings.Assets.Controls,
                    _bindings.Assets.DefaultFont,
                    iconResolver, _pluginThemes,
                    fallbackTitle: panel.Descriptor.Title);
                UiNineSlicePanel element = window.Frame;
```

and replace the `int authoredGeometryRevision = …` statement and the `Host.WindowManager.Register(...)` call that follows it with:

```csharp
                RetailWindowHandle handle = window.Register(Host.WindowManager, panel.WindowName, visibility);
```

Leave everything else in the loop (visibility controller, `CompleteMount`, `CompleteWindowMount`, the dock) unchanged.

- [ ] **Step 5: The dock leaves "–" off windows with a bar**

In `src/AcDream.App/UI/PluginSidePanel.cs`:

1. `ShelfEntry` becomes `private readonly record struct ShelfEntry(PluginShelfButton Button, PluginMinimizeButton? Minimize);`
2. In `Add`, replace the `var minimize = …; handle.OuterFrame.AddChild(minimize);` block with:

```csharp
            // A window with a title bar already has a close button, which does
            // what "–" would; the bar's X takes its place and says where the
            // window went.
            PluginMinimizeButton? minimize = null;
            if (handle.OuterFrame.Children.OfType<PluginTitleBar>().FirstOrDefault() is { } titleBar)
            {
                titleBar.Close.Tooltip = PluginWindowChrome.DockedCloseTooltip;
            }
            else
            {
                minimize = new PluginMinimizeButton(handle, _font, _themes)
                {
                    Left = MathF.Max(8f, handle.OuterFrame.Width - 23f),
                    Top = 3f,
                    Width = 18f,
                    Height = 17f,
                    Anchors = AnchorEdges.Top | AnchorEdges.Right,
                };
                handle.OuterFrame.AddChild(minimize);
            }
```

3. At both removal sites (around lines 542 and 631), change `if (ReferenceEquals(entry.Minimize.Parent, handle.OuterFrame)) handle.OuterFrame.RemoveChild(entry.Minimize);` to:

```csharp
            if (entry.Minimize is { } minimize && ReferenceEquals(minimize.Parent, handle.OuterFrame))
                handle.OuterFrame.RemoveChild(minimize);
```

Grep the file for any other `.Minimize` use and make it null-safe the same way. Add `using System.Linq;` if it is not already imported.

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~MarkupWindowMountTests|FullyQualifiedName~PluginSidePanel|FullyQualifiedName~PluginDock|FullyQualifiedName~PluginShelf|FullyQualifiedName~RetailWindowLayoutPersistenceTests"`
Expected: PASS (all).

- [ ] **Step 7: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/MarkupWindow.cs src/AcDream.App/UI/RetailUiRuntime.cs src/AcDream.App/UI/PluginSidePanel.cs tests/AcDream.App.Tests/UI/MarkupWindowMountTests.cs
git commit -m "feat(ui): mount plugin windows with their content root and wire the close button

Claude-Session: https://claude.ai/code/session_019dvJ5gakBkq3uJyr1PAC3u"
```

---

### Task 5: Docs and the gallery sample

**Goal:** `docs/plugin-ui-markup.md` documents `titlebar` and describes the close button accurately, and the Theme Gallery sample adopts the bar with its content in the same place on screen.

**Files:**
- Modify: `docs/plugin-ui-markup.md` (Elements table line ~233; new "Title bar" section before "## Resizable panels and anchors"; "## Showing and hiding your own window" paragraph)
- Modify: `samples/AcDream.Plugins.ThemeGallery/gallery.xml`

**Acceptance Criteria:**
- [ ] The `panel` row lists `titlebar`.
- [ ] A "Title bar" section states: opt-in with `titlebar="true"`, `true`/`false` only (anything else is a build error); the bar shows `title`, else the registration's title; children are placed from the content area's corner; `w`/`h`/`minw`/`minh` describe the content area and the frame adds 10 × 29 points; the X clears the player's request like the dock slot (the plugin keeps running) and replaces the dock's "–"; the bar drags the window; adopting the bar resets a saved size once and keeps the position; a minimal example with no title collision.
- [ ] "Showing and hiding your own window" says a window with a title bar closes from its X, and one without a bar from its dock slot (the "–" on its corner) or `HidePanel`.
- [ ] `gallery.xml` has `titlebar="true"`, `h="402" minh="402"`, and every `y` reduced by 28; `w`/`minw`/`x` unchanged; it still parses (`ThemeGallery` tests, if any, pass).

**Verify:** `dotnet build samples/AcDream.Plugins.ThemeGallery` → 0 warnings; `grep -c 'titlebar' docs/plugin-ui-markup.md` → ≥ 3.

**Steps:**

- [ ] **Step 1: Docs**

In `docs/plugin-ui-markup.md`:

1. The `panel` row of the Elements table becomes `` | `panel` (root) | The window | `x y w h title visible resizable minw minh resize titlebar` | ``.

2. Insert before `## Resizable panels and anchors`:

````markdown
## Title bar

`titlebar="true"` on the root `<panel>` gives the window a host title bar:
the window's name on the left and a close button on the right, drawn in the
window's look (the game's close art in Classic; the header band and a ghost
X in the shared themes). The name is the panel's `title`, or the title the
window was registered with when the markup has none. `titlebar` takes
`true` or `false`; anything else fails the build.

With the bar on, the window has a **content area** inside the chrome:

- children's `x`/`y` are measured from the content area's top-left corner,
  so nothing you place can sit under the title;
- `w`, `h`, `minw` and `minh` describe the content area, and the host adds
  the chrome: 5 points left, right and bottom, and the 24-point bar on top
  (the frame is 10 points wider and 29 points taller);
- dragging the bar moves the window.

```xml
<panel x="120" y="120" w="300" h="160" title="Buff Bot" titlebar="true">
  <label x="8" y="8" text="Ready" />
  <button x="212" y="128" w="80" h="24" text="Start" onclick="{Start}" anchor="right bottom" />
</panel>
```

The close button does what the window's dock slot does: it closes the
window for the player and the plugin keeps running; the dock slot (or
`ShowPanel`) opens it again. A window with a bar does not get the dock's
"–" button, since its X does the same thing. Turning the bar on for an
existing window changes its size, so a size the player saved is reset once
(the position is kept).
````

3. Replace the first paragraph of `## Showing and hiding your own window` with:

```markdown
A plugin window is on screen only while two things agree: the player's own
request and, when the markup binds the root's `visible`, the plugin's
binding. The player closes a window with its title bar's X (see "Title
bar"), or, without a bar, with the dock's "–" on its corner or its dock
slot; each clears the player's request, so setting the binding back to true
does not reopen it. `ShowPanel` and `HidePanel` make that request from the
plugin, exactly as the dock slot does:
```

- [ ] **Step 2: Gallery**

Replace `samples/AcDream.Plugins.ThemeGallery/gallery.xml` with the same file in which the root line is:

```xml
<panel x="120" y="120" w="420" h="402" title="Theme Gallery" theme="plugin" resizable="true" minw="420" minh="402" titlebar="true">
```

and every child's `y` is reduced by 28: tabs 32→4; labels/fields 70→42, 66→38, 102→74, 98→70; toggles 132→104; 164→136; slider 162→134; list 190→162; log 294→266; meter 374→346; buttons 394→366. Do this with an exact edit per line; leave every other attribute and line untouched. The bottom-right buttons keep their 10-point bottom margin (402 − 366 − 26 = 10).

- [ ] **Step 3: Build and commit**

```bash
dotnet build samples/AcDream.Plugins.ThemeGallery -warnaserror
git checkout -- '*.lock.json'
git add docs/plugin-ui-markup.md samples/AcDream.Plugins.ThemeGallery/gallery.xml
git commit -m "docs(ui): document the plugin window title bar; gallery adopts it

Claude-Session: https://claude.ai/code/session_019dvJ5gakBkq3uJyr1PAC3u"
```

---

### Task 6: Full verification and live gate

**Goal:** The branch builds at 0 warnings and passes the App suite with no new failures against main, and the title bar is seen working in the real client in Classic, Moss and Brass at 2×.

**Files:**
- None created; evidence (captures) saved to the scratchpad, not the repo.

**Acceptance Criteria:**
- [ ] `dotnet build -c Release` → 0 warnings, 0 errors.
- [ ] `dotnet test tests/AcDream.App.Tests` → the failure set equals main's (166 environment failures at 0991b97c, by name), with no new failures.
- [ ] Live, on the local ACE server (10.10.20.20 via `~/OpenAC-dev/dev-client.env`): the Theme Gallery window shows the bar with "Theme Gallery" and an X, and its content is not overlapped. Captures at 2× in Classic, Moss and Brass.
- [ ] Live: the X closes the gallery and its dock slot reopens it; the bar drags the window; hovering the X in Moss shows the text-colour X and the "Close (reopen from the dock)" tooltip; the gallery has no "–".

**Verify:** the two commands above, plus the three captures shown to the user.

**Steps:**

- [ ] **Step 1: Release build**

```bash
dotnet build -c Release 2>&1 | tail -3
```
Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 2: Suite against the main baseline**

```bash
dotnet test tests/AcDream.App.Tests --logger "trx;LogFileName=branch.trx" 2>&1 | tail -3
```
Compare the failed test names with the same command run in a temporary detached worktree of `main` (`git worktree add --detach ../baseline-main main`, removed afterwards). Any test that fails here but not on main is a regression to fix before going on.

- [ ] **Step 3: Live gate**

Run the client with the Theme Gallery plugin against the local ACE test server (`tools/run-dev-client.sh` sourcing `~/OpenAC-dev/dev-client.env`; never Dreamweave). Capture the client backbuffer at 2× (retina capture workflow: the user drags the window to the built-in display) in Classic, Moss and Brass. Then check close/reopen, dragging, hover and tooltip, and that there is no "–". Show the captures to the user. Ask the user to tune anything that looks off (title baseline, X size). Any constant change goes back through Tasks 1–3's tests.

- [ ] **Step 4: Finish**

Use superpowers-extended-cc:finishing-a-development-branch. Delivery as for PR 1: merge `flex/titlebar` into local main with `--no-ff` after review, keep the branch locally, push nothing without the user's word.

## Execution notes (2026-10-05)

Branch `flex/titlebar`, head ad953c38 (8 commits). Tasks 1–5 reviewed clean;
the final review's fix wave (46ec8946) re-captures the close button's anchor
baseline on theme switch and documents that non-docked bar windows reopen only
through `ShowPanel` (ruling: they keep the X). The live gate on 10.10.20.20
(player account) found the close button had no tooltip (it lacked the markup
tooltip skin, root 0x10000397 / layout 0x21000041) and no Classic hover. The fix (ad953c38)
adds the skin and, by user decision, keeps the retail sprite 0x06004D0C
(24×25), adds its pressed sprite 0x06004D0D and a hover highlight. App suite:
the same 166 environment failures as main, no new ones.
