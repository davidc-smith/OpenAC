# Plugin Dock Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The plugin dock (`PluginSidePanel`) becomes one soft floating pill in Classic, Moss and Brass, with an edge rail mode that locks it to the left or right screen edge, a hover label, monograms, a gear for plugin appearance, and a single icon-drawing seam that SVG icons can fill later.

**Architecture:** A pure `PluginDockLayout` works out every rectangle from the mode, collapse state, owners and available height. A new partial `PluginUiStyle.Dock.cs` holds every dock shape and size, drawn with the painter-v2 calls. `PluginSidePanel` keeps its public surface and applies the layout; Classic draws through the same path with a dock-only `PluginUiPalette.ClassicDock`. The dock mode is saved beside the theme in the `pluginUi` settings section, and one no-op-by-default `UiElement.ConstrainWindowDrag` hook in `UiRoot` lets the dock snap while dragged.

**Tech Stack:** C# / .NET 10, xUnit, the retained UI in `src/AcDream.App/UI`, `SettingsStore` in `AcDream.UI.Abstractions`.

**Spec:** `docs/superpowers/specs/2026-10-02-plugin-dock-design.md` (fork-only docs branch `docs/modern-theme-spec`).

## Global Constraints

- **The spec is binding.** Sizes, colours and behaviours come from it; the constants live in `PluginUiStyle.Dock.cs` (Task 3) and nowhere else.
- **No plugin API change:** nothing in `src/AcDream.Plugin.Abstractions` changes (its doc comments that say "shelf" stay too). `PluginSidePanel`'s public members keep their signatures.
- **Saved layouts still load:** `RetainedWindowState.Collapsed`/`RequestedVisible` keep their meaning; a settings file without `dock` loads as Floating; window name `plugin-shelf` is unchanged.
- **Classic:** only the dock changes in Classic (decision 5). Every other Classic window draws as before; `PluginUiThemeSettings.Palette` stays null for Classic, and `PluginThemeClassicIdentityTests` must keep passing.
- **Icon seam:** every icon goes through `PluginShelfButton.DrawIcon(ctx, x, y, extent, colour)`; never hard-code the PNG path elsewhere (shared with `2026-10-02-svg-plugin-icons-design.md`).
- **Branching:** code on `modern-theme/dock` from fork `main` in `.worktrees/dock`. `docs/superpowers/` never goes on the code branch. Don't merge into `main` or push without asking the user (Task 10).
- **Build env:** `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH`. Building rewrites `*.lock.json`; revert with `git checkout -- '*.lock.json'` before each commit.
- **Commit trailer:** end every commit message with `Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB`.
- **Code in this plan was prototyped** task by task in a throwaway worktree from 16b0db10 and built and passed exactly as written (counts in each Verify). Diffs apply to the previous task's result with `git apply`.

**Readings of the spec chosen while prototyping (follow these):**
- **Hover label mechanism:** drawn in the dock's own overlay pass (`ExpandsClipForPopup => true` + `OnDrawOverlay`), not as a separate root child. Same result the spec asks for — outside the dock's clip, above windows, never hit-testable — with no extra element to keep in sync.
- **Collapsed rail tab is 12×44, not 8×40:** a press on a `WindowMoveHandle` starts a window drag and its release delivers no click, so the tab cannot be both the drag handle and the click target. It is a 14pt grip (dots) above a 30pt expand button.
- **Rail shadow:** `DrawSoftShadow` only drops downward, so a rail uses drop 2 and spread 14 (its edge side is off screen) instead of a sideways offset.
- **Rail slot gap is 2pt** so the slot pitch (40) matches floating (36 + 4) and snapping does not jump the slots.
- **Handle row:** dots and collapse button sit in a 12pt row at y 2; the grip element covers x 0..30, y 0..16; slots start at y 16.
- **No reduced-motion setting exists** in the client, so the edge pill always eases (20pt per 120ms).
- **The gear is always shown** (a no-op click when the dock has no appearance callback, as in most tests).
- **Monogram letters** use the title font: `ModernTitleFont` (else `ModernFont`) in Moss/Brass, the DAT font in Classic; initials are capped at two characters in every theme.
- **Front window** = the visible dock window with the highest `ZOrder` (what `BringToFront` raises).
- **The mode a drag snaps into** is held in `_dragMode` and saved in the dock's `Moved` handler, which `UiRoot` fires once at drag end.
- **A null `themes` argument** makes the dock create a default (Classic) `PluginUiThemeSettings`.

**User decisions (already made):**
- "A · Floating dock" direction, plus an edge rail mode.
- The rail locks to the left or right edge only.
- Switching uses a setting plus snap (Plugin appearance Dock menu; dragging to an edge snaps).
- In rail mode the rail slides along its edge and its height is saved.
- Classic gets the new shape in its own black-and-gold colours (reverses the byte-identical rule for the dock only).
- A gear slot at the bottom opens Plugin appearance; right-click keeps working.
- The icon seam `DrawIcon(ctx, x, y, extent, colour)` is shared with the SVG icons spec.
- Fork first, specs fork-only, ask before push or merge.

**Baseline (2026-10-02, this Mac):** the full App suite on `main` 16b0db10 has 166 environment failures (DAT paths, IPC; memory notes said 163 earlier). After this plan: 164 — the same names minus the two deleted `PluginSidePanelToggleGlyphClipTests` InstalledDat failures, and no new ones. Release build: 0 warnings. `AcDream.UI.Abstractions.Tests`: 983 passed, the 5 new ones included.

Test command used throughout (the filter varies):

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/dock && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~<Name>"
```

---
### Task 0: Branch and worktree for the dock

**Goal:** A worktree `.worktrees/dock` on a new branch `modern-theme/dock`, made from fork `main`, that builds and whose dock-related tests pass before any change.

**Files:**
- None (git only)

**Acceptance Criteria:**
- [ ] Worktree `.worktrees/dock` exists on branch `modern-theme/dock`, created from fork `main` (16b0db10 or later).
- [ ] `dotnet build AcDream.slnx -c Release` in the worktree reports 0 warnings and 0 errors.
- [ ] `PluginSidePanelTests` reports 29 passed before any change.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~PluginSidePanelTests&Lane!=InstalledDat"` → 29 passed

**Steps:**

- [ ] **Step 1: Create the worktree**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
git status --short          # must be empty
git log --oneline -1 main   # 16b0db10 or later
git worktree add .worktrees/dock -b modern-theme/dock main
```

- [ ] **Step 2: Build and run the baseline**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/dock && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet build AcDream.slnx -c Release 2>&1 | tail -3
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~PluginSidePanelTests&Lane!=InstalledDat"
git checkout -- '*.lock.json'
```

Expected: `0 Warning(s)`, `0 Error(s)`; `Passed: 29`.

- [ ] **Step 3: Record the full App baseline (used by Task 10)**

```bash
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj 2>&1 | grep -E "\[FAIL\]|Passed!|Failed!" | sed -E 's/^.*\] +//; s/ \[FAIL\]//' | sort > /tmp/dock-base-fails.txt
tail -1 /tmp/dock-base-fails.txt
git checkout -- '*.lock.json'
```

Expected on this Mac (2026-10-02): `Failed: 166, Passed: 8902` — environment failures (DAT paths, IPC), not regressions. Keep the file.


### Task 1: Dock mode saved beside the theme

**Goal:** `PluginUiThemeSettings` gains a persisted `Dock` (`PluginDockMode` Floating/Left/Right), `DockPalette`, and the Classic-only dock palette `PluginUiPalette.ClassicDock`; `SettingsStore` saves theme and dock together in the `pluginUi` section.

**Files:**
- Create: `src/AcDream.UI.Abstractions/Panels/Settings/PluginUiSettings.cs`
- Modify: `src/AcDream.UI.Abstractions/Panels/Settings/SettingsStore.cs` (replace `LoadPluginUiTheme`/`SavePluginUiTheme`, about lines 249-267)
- Modify: `src/AcDream.App/UI/PluginUiThemeSettings.cs`
- Test: `tests/AcDream.UI.Abstractions.Tests/Panels/Settings/PluginUiSettingsStoreTests.cs`
- Test: `tests/AcDream.App.Tests/UI/PluginDockSettingsTests.cs`

**Acceptance Criteria:**
- [ ] `SettingsStore.LoadPluginUi()` returns `PluginUiSettings("Classic", "floating")` for a missing file, a missing section, or an old section without `dock`.
- [ ] `SavePluginUi` writes `theme` and `dock` together; saving after changing one keeps the other; other sections survive.
- [ ] `PluginUiThemeSettings.Dock` defaults to Floating, persists as "floating"/"left"/"right", and loads unknown values ("top", "") as Floating; values are case-insensitive.
- [ ] `DockPalette` is `Palette ?? PluginUiPalette.ClassicDock`; `Palette` is still null for Classic.
- [ ] `ClassicDock` is Background #000000 at 88%, Field #060605, Border #9E7A29, Text #F0EBDD, Muted #A8925C, Accent #DBB852, Selected #17300E.
- [ ] `LoadPluginUiTheme`/`SavePluginUiTheme` no longer exist (they had no other callers).

**Verify:** `dotnet test tests/AcDream.UI.Abstractions.Tests/AcDream.UI.Abstractions.Tests.csproj --filter FullyQualifiedName~PluginUiSettingsStoreTests` → 5 passed; `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~PluginDockSettingsTests|FullyQualifiedName~PluginUiThemeTests"` → 12 passed

**Steps:**

- [ ] **Step 1: Write the failing store tests**

`tests/AcDream.UI.Abstractions.Tests/Panels/Settings/PluginUiSettingsStoreTests.cs`:

```csharp
using System.IO;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.UI.Abstractions.Tests.Panels.Settings;

public sealed class PluginUiSettingsStoreTests : System.IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"acdream-plugin-ui-test-{System.Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void Missing_file_loads_classic_and_floating()
    {
        Assert.Equal(PluginUiSettings.Default, new SettingsStore(_path).LoadPluginUi());
        Assert.Equal(new PluginUiSettings("Classic", "floating"), PluginUiSettings.Default);
    }

    [Fact]
    public void Theme_and_dock_round_trip_together()
    {
        var store = new SettingsStore(_path);
        store.SavePluginUi(new PluginUiSettings("Moss", "left"));

        Assert.Equal(new PluginUiSettings("Moss", "left"), new SettingsStore(_path).LoadPluginUi());
    }

    [Fact]
    public void Saving_one_field_keeps_the_other()
    {
        var store = new SettingsStore(_path);
        store.SavePluginUi(new PluginUiSettings("Brass", "right"));
        store.SavePluginUi(store.LoadPluginUi() with { Theme = "Moss" });
        Assert.Equal(new PluginUiSettings("Moss", "right"), store.LoadPluginUi());

        store.SavePluginUi(store.LoadPluginUi() with { Dock = "floating" });
        Assert.Equal(new PluginUiSettings("Moss", "floating"), store.LoadPluginUi());
    }

    [Fact]
    public void A_file_written_before_the_dock_existed_loads_floating()
    {
        File.WriteAllText(_path, """{ "version": 4, "pluginUi": { "theme": "Brass" } }""");

        Assert.Equal(new PluginUiSettings("Brass", "floating"), new SettingsStore(_path).LoadPluginUi());
    }

    [Fact]
    public void Other_sections_survive_a_plugin_ui_save()
    {
        var store = new SettingsStore(_path);
        store.SaveMisc(MiscSettings.Default with { TooltipDelaySeconds = 0.75f });
        store.SavePluginUi(new PluginUiSettings("Moss", "left"));

        Assert.Equal(0.75f, store.LoadMisc().TooltipDelaySeconds);
    }
}
```

- [ ] **Step 2: Write the failing App tests**

`tests/AcDream.App.Tests/UI/PluginDockSettingsTests.cs`:

```csharp
using AcDream.App.UI;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.Tests.UI;

public sealed class PluginDockSettingsTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"acdream-dock-settings-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void DockDefaultsToFloating_AndPersistsBesideTheTheme()
    {
        var store = new SettingsStore(_path);
        var settings = new PluginUiThemeSettings(store);
        Assert.Equal(PluginDockMode.Floating, settings.Dock);

        settings.Theme = PluginUiTheme.Moss;
        settings.Dock = PluginDockMode.Right;
        Assert.Equal(new PluginUiSettings("Moss", "right"), store.LoadPluginUi());

        settings.Theme = PluginUiTheme.Brass;
        Assert.Equal(new PluginUiSettings("Brass", "right"), store.LoadPluginUi());

        var reloaded = new PluginUiThemeSettings(store);
        Assert.Equal(PluginUiTheme.Brass, reloaded.Theme);
        Assert.Equal(PluginDockMode.Right, reloaded.Dock);
    }

    [Theory]
    [InlineData("left", PluginDockMode.Left)]
    [InlineData("RIGHT", PluginDockMode.Right)]
    [InlineData("floating", PluginDockMode.Floating)]
    [InlineData("top", PluginDockMode.Floating)]
    [InlineData("", PluginDockMode.Floating)]
    public void UnknownDockValuesLoadAsFloating(string stored, PluginDockMode expected)
    {
        new SettingsStore(_path).SavePluginUi(new PluginUiSettings("Classic", stored));
        Assert.Equal(expected, new PluginUiThemeSettings(new SettingsStore(_path)).Dock);
    }

    [Fact]
    public void DockPaletteIsTheThemePaletteOrClassicDock_AndClassicWindowsStayUnthemed()
    {
        var settings = new PluginUiThemeSettings();
        Assert.Null(settings.Palette);
        Assert.Same(PluginUiPalette.ClassicDock, settings.DockPalette);

        settings.Theme = PluginUiTheme.Moss;
        Assert.Same(PluginUiPalette.Moss, settings.DockPalette);
        settings.Theme = PluginUiTheme.Brass;
        Assert.Same(PluginUiPalette.Brass, settings.DockPalette);
    }

    [Fact]
    public void ClassicDockCarriesTodaysClassicDockColours()
    {
        PluginUiPalette p = PluginUiPalette.ClassicDock;
        Assert.Equal(0.88f, p.Background.W, 3);
        Assert.Equal(0f, p.Background.X);
        Assert.Equal(0x9E / 255f, p.Border.X, 3);
        Assert.Equal(0xDB / 255f, p.Accent.X, 3);
        Assert.Equal(0x30 / 255f, p.Selected.Y, 3);
    }
}
```

- [ ] **Step 3: Run them and see them fail**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/dock && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test tests/AcDream.UI.Abstractions.Tests/AcDream.UI.Abstractions.Tests.csproj --filter FullyQualifiedName~PluginUiSettingsStoreTests
```

Expected: build error `CS0103: The name 'PluginUiSettings' does not exist`.

- [ ] **Step 4: Add the settings record**

`src/AcDream.UI.Abstractions/Panels/Settings/PluginUiSettings.cs`:

```csharp
namespace AcDream.UI.Abstractions.Panels.Settings;

/// <summary>
/// The plugin appearance the player picked: the theme's name and where the
/// plugin dock sits ("floating", "left" or "right"). Both are saved together
/// in the <c>pluginUi</c> section, so saving one never drops the other.
/// </summary>
public sealed record PluginUiSettings(string Theme, string Dock)
{
    public static PluginUiSettings Default { get; } = new("Classic", "floating");
}
```

- [ ] **Step 5: Replace the store's plugin-UI load/save**

Save the diff below as `/tmp/dock-SettingsStore.cs.patch` and run `git apply /tmp/dock-SettingsStore.cs.patch` from the worktree root (or make the same edits by hand).

````diff
diff --git a/src/AcDream.UI.Abstractions/Panels/Settings/SettingsStore.cs b/src/AcDream.UI.Abstractions/Panels/Settings/SettingsStore.cs
index 3f39e777..1598376a 100644
--- a/src/AcDream.UI.Abstractions/Panels/Settings/SettingsStore.cs
+++ b/src/AcDream.UI.Abstractions/Panels/Settings/SettingsStore.cs
@@ -246,25 +246,33 @@ public sealed class SettingsStore
     public void SaveCameraTurning(CameraTurningSettings cameraTurning)
         => SaveSection("cameraTurning", BuildCameraTurningObject(cameraTurning));
 
-    public string LoadPluginUiTheme()
+    public PluginUiSettings LoadPluginUi()
     {
-        if (!File.Exists(_path)) return "Classic";
+        var d = PluginUiSettings.Default;
+        if (!File.Exists(_path)) return d;
         try
         {
             using var doc = JsonDocument.Parse(File.ReadAllText(_path));
-            return doc.RootElement.TryGetProperty("pluginUi", out var section)
-                && section.ValueKind == JsonValueKind.Object
-                ? ReadString(section, "theme", "Classic") : "Classic";
+            if (!doc.RootElement.TryGetProperty("pluginUi", out var section)
+                || section.ValueKind != JsonValueKind.Object)
+                return d;
+            return new PluginUiSettings(
+                Theme: ReadString(section, "theme", d.Theme),
+                Dock: ReadString(section, "dock", d.Dock));
         }
         catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
         {
             Console.WriteLine($"settings: failed to load plugin appearance: {ex.Message}");
-            return "Classic";
+            return d;
         }
     }
 
-    public void SavePluginUiTheme(string theme)
-        => SaveSection("pluginUi", new SortedDictionary<string, object> { ["theme"] = theme });
+    public void SavePluginUi(PluginUiSettings pluginUi)
+        => SaveSection("pluginUi", new SortedDictionary<string, object>
+        {
+            ["dock"] = pluginUi.Dock,
+            ["theme"] = pluginUi.Theme,
+        });
 
     public MiscSettings LoadMisc()
     {
````

- [ ] **Step 6: Add the dock mode, `DockPalette` and `ClassicDock`**

Save the diff below as `/tmp/dock-PluginUiThemeSettings.cs.patch` and run `git apply /tmp/dock-PluginUiThemeSettings.cs.patch` from the worktree root (or make the same edits by hand).

````diff
diff --git a/src/AcDream.App/UI/PluginUiThemeSettings.cs b/src/AcDream.App/UI/PluginUiThemeSettings.cs
index ae54f034..5a76902f 100644
--- a/src/AcDream.App/UI/PluginUiThemeSettings.cs
+++ b/src/AcDream.App/UI/PluginUiThemeSettings.cs
@@ -5,11 +5,15 @@ namespace AcDream.App.UI;
 
 public enum PluginUiTheme { Classic, Moss, Brass }
 
+/// <summary>Where the plugin dock sits: floating anywhere, or locked to the left or right screen edge.</summary>
+public enum PluginDockMode { Floating, Left, Right }
+
 /// <summary>One appearance preference shared by opted-in plugin windows.</summary>
 public sealed class PluginUiThemeSettings
 {
     private readonly SettingsStore? _store;
     private PluginUiTheme _theme;
+    private PluginDockMode _dock;
     private readonly Lazy<UiDatFont>? _modernFont;
     private readonly Lazy<UiDatFont>? _modernTitleFont;
 
@@ -28,8 +32,10 @@ public sealed class PluginUiThemeSettings
         _store = store;
         _modernFont = modernFont;
         _modernTitleFont = modernTitleFont;
-        _theme = Enum.TryParse<PluginUiTheme>(store?.LoadPluginUiTheme(), out var value)
+        PluginUiSettings saved = store?.LoadPluginUi() ?? PluginUiSettings.Default;
+        _theme = Enum.TryParse<PluginUiTheme>(saved.Theme, out var value)
             && Enum.IsDefined(value) ? value : PluginUiTheme.Classic;
+        _dock = ParseDock(saved.Dock);
     }
     public PluginUiTheme Theme
     {
@@ -38,8 +44,21 @@ public sealed class PluginUiThemeSettings
         {
             if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
             if (_theme == value) return;
-            _store?.SavePluginUiTheme(value.ToString());
             _theme = value;
+            Save();
+        }
+    }
+
+    /// <summary>Where the plugin dock sits. Saved with the theme.</summary>
+    public PluginDockMode Dock
+    {
+        get => _dock;
+        set
+        {
+            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
+            if (_dock == value) return;
+            _dock = value;
+            Save();
         }
     }
     public PluginUiPalette? Palette => Theme switch
@@ -48,6 +67,25 @@ public sealed class PluginUiThemeSettings
         PluginUiTheme.Brass => PluginUiPalette.Brass,
         _ => null,
     };
+
+    /// <summary>The colours the plugin dock draws in: the theme's palette, or the dock's own Classic set.</summary>
+    public PluginUiPalette DockPalette => Palette ?? PluginUiPalette.ClassicDock;
+
+    private void Save() => _store?.SavePluginUi(new PluginUiSettings(_theme.ToString(), DockName(_dock)));
+
+    private static string DockName(PluginDockMode mode) => mode switch
+    {
+        PluginDockMode.Left => "left",
+        PluginDockMode.Right => "right",
+        _ => "floating",
+    };
+
+    private static PluginDockMode ParseDock(string? name) => name?.ToLowerInvariant() switch
+    {
+        "left" => PluginDockMode.Left,
+        "right" => PluginDockMode.Right,
+        _ => PluginDockMode.Floating,
+    };
 }
 
 public sealed record PluginUiPalette(Vector4 Background, Vector4 Field, Vector4 Border,
@@ -59,6 +97,13 @@ public sealed record PluginUiPalette(Vector4 Background, Vector4 Field, Vector4
         C(0x35443B), C(0xE0E8E2), C(0x9AA99E), C(0x97BE81), C(0x344B37));
     public static PluginUiPalette Brass { get; } = new(C(0x221F1B), C(0x181612),
         C(0x4C4335), C(0xE9E2D5), C(0xB2A58E), C(0xC9A665), C(0x51442D));
+
+    /// <summary>
+    /// The plugin dock's colours under Classic, taken from the Classic dock's old black and gold.
+    /// Only the dock uses it: it is not a theme, and Classic windows still draw unthemed.
+    /// </summary>
+    internal static PluginUiPalette ClassicDock { get; } = new(C(0x000000) with { W = 0.88f }, C(0x060605),
+        C(0x9E7A29), C(0xF0EBDD), C(0xA8925C), C(0xDBB852), C(0x17300E));
     public Vector4 Token(string name) => name switch
     {
         "text" => Text, "muted" => Muted, "field" => Field,
````

- [ ] **Step 7: Run the tests and see them pass**

```bash
dotnet test tests/AcDream.UI.Abstractions.Tests/AcDream.UI.Abstractions.Tests.csproj --filter FullyQualifiedName~PluginUiSettingsStoreTests
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~PluginDockSettingsTests|FullyQualifiedName~PluginUiThemeTests"
```

Expected: `Passed: 5`, then `Passed: 12`.

- [ ] **Step 8: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.UI.Abstractions/Panels/Settings/PluginUiSettings.cs src/AcDream.UI.Abstractions/Panels/Settings/SettingsStore.cs src/AcDream.App/UI/PluginUiThemeSettings.cs tests/AcDream.UI.Abstractions.Tests/Panels/Settings/PluginUiSettingsStoreTests.cs tests/AcDream.App.Tests/UI/PluginDockSettingsTests.cs
git commit -m "settings: save the plugin dock mode beside the plugin theme

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```


### Task 2: Window drag hook in UiRoot

**Goal:** `UiElement.ConstrainWindowDrag(ref left, ref top, pointerX, pointerY)` (internal virtual, no-op) is called by `UiRoot` on every move of a handle drag, after the clamp to the parent, so a window can snap.

**Files:**
- Modify: `src/AcDream.App/UI/UiElement.cs` (after `WindowMoveHandle`, about line 178)
- Modify: `src/AcDream.App/UI/UiRoot.cs` (the window-move branch of `OnMouseMove`, about line 494)
- Test: `tests/AcDream.App.Tests/UI/UiRootWindowDragHookTests.cs`

**Acceptance Criteria:**
- [ ] The dragged window receives the clamped left/top and the pointer position on each move, and its changes to left/top are applied.
- [ ] Any window that does not override the hook moves exactly as before (clamped inside the parent).
- [ ] `UiRootInputTests` and `PluginSidePanelTests` still pass unchanged.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~UiRootWindowDragHookTests|FullyQualifiedName~UiRootInputTests|FullyQualifiedName~PluginSidePanelTests"` → 95 passed

**Steps:**

- [ ] **Step 1: Write the failing test**

`tests/AcDream.App.Tests/UI/UiRootWindowDragHookTests.cs`:

```csharp
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public sealed class UiRootWindowDragHookTests
{
    private sealed class SnappingWindow : UiPanel
    {
        public readonly List<(float Left, float Top, int X, int Y)> Calls = [];

        internal override void ConstrainWindowDrag(ref float left, ref float top, int pointerX, int pointerY)
        {
            Calls.Add((left, top, pointerX, pointerY));
            if (left < 20f) left = 0f;
        }
    }

    private static (UiRoot Root, T Window) Mount<T>(T window) where T : UiPanel
    {
        var root = new UiRoot { Width = 800f, Height = 600f };
        window.Left = 100f;
        window.Top = 100f;
        window.Width = 120f;
        window.Height = 80f;
        window.AddChild(new UiPanel { Width = 120f, Height = 12f, WindowMoveHandle = true });
        root.AddChild(window);
        return (root, window);
    }

    [Fact]
    public void TheDraggedWindowSeesTheClampedPositionAndThePointer_AndCanMoveIt()
    {
        var (root, window) = Mount(new SnappingWindow());

        root.OnMouseDown(UiMouseButton.Left, 110, 105);
        root.OnMouseMove(25, 205);
        root.OnMouseUp(UiMouseButton.Left, 25, 205);

        Assert.Equal((15f, 200f, 25, 205), Assert.Single(window.Calls));
        Assert.Equal(0f, window.Left);
        Assert.Equal(200f, window.Top);
    }

    [Fact]
    public void AnyOtherWindowMovesExactlyAsBefore()
    {
        var (root, window) = Mount(new UiPanel());

        root.OnMouseDown(UiMouseButton.Left, 110, 105);
        root.OnMouseMove(25, 205);
        root.OnMouseMove(-50, 900);
        root.OnMouseUp(UiMouseButton.Left, -50, 900);

        Assert.Equal(0f, window.Left);
        Assert.Equal(520f, window.Top);
    }
}
```

- [ ] **Step 2: Run it and see it fail**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/dock && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter FullyQualifiedName~UiRootWindowDragHookTests
```

Expected: build error `CS0115: 'UiRootWindowDragHookTests.SnappingWindow.ConstrainWindowDrag(ref float, ref float, int, int)': no suitable method found to override`.

- [ ] **Step 3: Add the hook**

````diff
diff --git a/src/AcDream.App/UI/UiElement.cs b/src/AcDream.App/UI/UiElement.cs
index a7c95d29..b6f63719 100644
--- a/src/AcDream.App/UI/UiElement.cs
+++ b/src/AcDream.App/UI/UiElement.cs
@@ -177,6 +177,15 @@ public abstract class UiElement
 
     public bool WindowMoveHandle { get; set; }
 
+    /// <summary>
+    /// Called on each pointer move while this window is being dragged by a
+    /// move handle, with where the drag would put it (already kept inside the
+    /// parent) and the pointer's position. A window that snaps somewhere
+    /// changes <paramref name="left"/> and <paramref name="top"/>. Most windows
+    /// leave them alone.
+    /// </summary>
+    internal virtual void ConstrainWindowDrag(ref float left, ref float top, int pointerX, int pointerY) { }
+
 
     public bool ConstrainResizeToParent { get; set; }
 
````

````diff
diff --git a/src/AcDream.App/UI/UiRoot.cs b/src/AcDream.App/UI/UiRoot.cs
index 80e77466..9e71ec62 100644
--- a/src/AcDream.App/UI/UiRoot.cs
+++ b/src/AcDream.App/UI/UiRoot.cs
@@ -492,6 +492,7 @@ public sealed class UiRoot : UiElement
                 left = Math.Clamp(left, 0f, Math.Max(0f, parent.Width - _windowDragTarget.Width));
                 top = Math.Clamp(top, 0f, Math.Max(0f, parent.Height - _windowDragTarget.Height));
             }
+            _windowDragTarget.ConstrainWindowDrag(ref left, ref top, x, y);
             _windowDragTarget.Left = left;
             _windowDragTarget.Top  = top;
             _windowDragTarget.ResetAnchorCapture();
````

- [ ] **Step 4: Run the tests and see them pass**

```bash
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~UiRootWindowDragHookTests|FullyQualifiedName~UiRootInputTests|FullyQualifiedName~PluginSidePanelTests"
```

Expected: `Passed: 95` (2 + 64 + 29).

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/UiElement.cs src/AcDream.App/UI/UiRoot.cs tests/AcDream.App.Tests/UI/UiRootWindowDragHookTests.cs
git commit -m "ui: let a dragged window adjust where the drag puts it

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```


### Task 3: Dock drawing pieces in PluginUiStyle

**Goal:** Every dock shape and size lives in a new partial `PluginUiStyle.Dock.cs`: body, slot well, open dot, edge pill, divider, fade, handle dots, chevrons, gear, monogram (with its FNV-1a hue) and hover-label box.

**Files:**
- Modify: `src/AcDream.App/UI/PluginUiStyle.cs` (make the class `partial`, line 14)
- Create: `src/AcDream.App/UI/PluginUiStyle.Dock.cs`
- Test: `tests/AcDream.App.Tests/UI/PluginDockStyleTests.cs`

**Acceptance Criteria:**
- [ ] Constants match the spec: dock 48 wide (6 + 36 + 6), radius 14, slot 36, gap 4, art 24, slot radius 9; rail 46 wide, radius 12, slot 38, gap 2; pill 3 wide at 4/8/20; label gap 8, padding 9×5, radius 7; collapsed pill 48×24; rail tab 12×44.
- [ ] `DockCorners` is 14 all round floating, (0,12,12,0) on a left rail, (12,0,0,12) on a right rail.
- [ ] `EdgePill` sits on the screen edge (x 0..3 left, w-3..w right), centred on the slot; zero height draws nothing.
- [ ] `OpenDot` is a 3pt `Accent` circle 1.5pt in from the side nearer the screen edge.
- [ ] `DockSlotWell` draws nothing when idle and `Hover(Field)` on hover.
- [ ] `MonogramHue` is one of the eight spec hues by FNV-1a of the id's UTF-8 bytes ("a" → #8B8259).
- [ ] `DockBody` draws `Background`, a rim of `Border` mixed 8% toward `Text`, and a shadow below.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter FullyQualifiedName~PluginDockStyleTests` → 10 passed

**Steps:**

- [ ] **Step 1: Write the failing tests**

`tests/AcDream.App.Tests/UI/PluginDockStyleTests.cs`:

```csharp
using System.Numerics;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public sealed class PluginDockStyleTests
{
    private static readonly PluginUiPalette Moss = PluginUiPalette.Moss;

    [Fact]
    public void FloatingBody_FillsInTheBackground_WithARimAndAShadowBelow()
    {
        var (renderer, ctx) = ThemeDrawCapture.Context(400, 400);
        ctx.PushTransform(100f, 100f);
        PluginUiStyle.DockBody(ctx, Moss, PluginDockMode.Floating, PluginUiStyle.DockWidth, 200f);
        var vertices = ThemeDrawCapture.Vertices(renderer);

        Assert.True(ThemeDrawCapture.HasColor(vertices, Moss.Background));
        Assert.True(ThemeDrawCapture.HasColor(vertices, PluginUiStyle.Mix(Moss.Border, Moss.Text, 0.08f)));
        Assert.Contains(vertices, v => v.Position.Y > 300f && v.Color.W > 0f);
    }

    [Fact]
    public void LeftRail_IsSquareOnItsEdgeSide()
    {
        Assert.Equal(new CanvasCornerRadii(0f, 12f, 12f, 0f), PluginUiStyle.DockCorners(PluginDockMode.Left));
        Assert.Equal(new CanvasCornerRadii(12f, 0f, 0f, 12f), PluginUiStyle.DockCorners(PluginDockMode.Right));
        Assert.Equal(new CanvasCornerRadii(14f, 14f, 14f, 14f), PluginUiStyle.DockCorners(PluginDockMode.Floating));
    }

    [Theory]
    [InlineData(false, 0f, 3f)]
    [InlineData(true, 43f, 46f)]
    public void EdgePill_SitsOnTheScreenEdge_CentredOnTheSlot(bool right, float minX, float maxX)
    {
        DockSide side = right ? DockSide.Right : DockSide.Left;
        var (renderer, ctx) = ThemeDrawCapture.Context(200, 200);
        PluginUiStyle.EdgePill(ctx, Moss.Accent, side, PluginUiStyle.RailWidth, 40f, 38f, PluginUiStyle.RailPillOpen);
        var ink = ThemeDrawCapture.Vertices(renderer).Where(v => v.Color.W > 0.5f).ToList();

        Assert.NotEmpty(ink);
        Assert.All(ink, v => Assert.InRange(v.Position.X, minX - 0.01f, maxX + 0.01f));
        // An 8pt pill centred on a slot from 40 to 78: 55 to 63, give or take the half-pixel soft edge.
        Assert.InRange(ink.Min(v => v.Position.Y), 54.5f, 55.5f);
        Assert.InRange(ink.Max(v => v.Position.Y), 62.5f, 63.5f);
    }

    [Fact]
    public void EdgePill_OfZeroHeight_DrawsNothing()
    {
        var (renderer, ctx) = ThemeDrawCapture.Context(200, 200);
        PluginUiStyle.EdgePill(ctx, Moss.Accent, DockSide.Left, 46f, 40f, 38f, 0f);
        Assert.Empty(ThemeDrawCapture.Vertices(renderer));
    }

    [Theory]
    [InlineData(false, 1.5f, 4.5f)]
    [InlineData(true, 43.5f, 46.5f)]
    public void OpenDot_SitsOnTheSideNearerTheScreenEdge(bool right, float minX, float maxX)
    {
        DockSide side = right ? DockSide.Right : DockSide.Left;
        var (renderer, ctx) = ThemeDrawCapture.Context(200, 200);
        PluginUiStyle.OpenDot(ctx, Moss, side, PluginUiStyle.DockWidth, 16f, 36f);
        var ink = ThemeDrawCapture.Vertices(renderer).Where(v => v.Color.W > 0.5f).ToList();

        Assert.True(ThemeDrawCapture.HasColor(ink, Moss.Accent));
        Assert.All(ink, v => Assert.InRange(v.Position.X, minX - 0.01f, maxX + 0.01f));
    }

    [Fact]
    public void SlotWell_IsEmptyWhenIdle_AndFilledOnHover()
    {
        var (idleRenderer, idle) = ThemeDrawCapture.Context(100, 100);
        PluginUiStyle.DockSlotWell(idle, Moss, 0f, 0f, 36f, 36f, UiControlState.Normal);
        Assert.Empty(ThemeDrawCapture.Vertices(idleRenderer));

        var (hoverRenderer, hover) = ThemeDrawCapture.Context(100, 100);
        PluginUiStyle.DockSlotWell(hover, Moss, 0f, 0f, 36f, 36f, UiControlState.Hovered);
        Assert.True(ThemeDrawCapture.HasColor(ThemeDrawCapture.Vertices(hoverRenderer), PluginUiStyle.Hover(Moss, Moss.Field)));
    }

    [Fact]
    public void MonogramHue_IsStableForAnId_AndOneOfTheEightFixedHues()
    {
        Vector4 first = PluginUiStyle.MonogramHue("acdream.mosstank");
        Assert.Equal(first, PluginUiStyle.MonogramHue("acdream.mosstank"));
        var hues = new[] { "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l" }
            .Select(PluginUiStyle.MonogramHue).Distinct().ToList();
        Assert.True(hues.Count > 1);
        Assert.True(hues.Count <= 8);
        // FNV-1a of "a" is 0xE40C292C, and 0xE40C292C % 8 = 4: the fifth hue, #8B8259.
        Assert.Equal(new Vector4(0x8B / 255f, 0x82 / 255f, 0x59 / 255f, 1f), PluginUiStyle.MonogramHue("a"));
    }

    [Fact]
    public void HoverLabel_DrawsItsBoxAndAShadow()
    {
        var (renderer, ctx) = ThemeDrawCapture.Context(300, 300);
        PluginUiStyle.HoverLabel(ctx, Moss, 60f, 20f, 90f, 36f);
        var vertices = ThemeDrawCapture.Vertices(renderer);
        Assert.True(ThemeDrawCapture.HasColor(vertices, Moss.Background));
        Assert.True(ThemeDrawCapture.HasColor(vertices, Moss.Border));
        Assert.Contains(vertices, v => v.Position.Y > 56f && v.Color.W > 0f);
    }
}
```

- [ ] **Step 2: Run them and see them fail**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/dock && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter FullyQualifiedName~PluginDockStyleTests
```

Expected: build errors such as `CS0117: 'PluginUiStyle' does not contain a definition for 'DockBody'`.

- [ ] **Step 3: Make `PluginUiStyle` partial**

````diff
diff --git a/src/AcDream.App/UI/PluginUiStyle.cs b/src/AcDream.App/UI/PluginUiStyle.cs
index 065c38c1..27706b7f 100644
--- a/src/AcDream.App/UI/PluginUiStyle.cs
+++ b/src/AcDream.App/UI/PluginUiStyle.cs
@@ -11,7 +11,7 @@ namespace AcDream.App.UI;
 /// <para>Everything is drawn inside the bounds given: an element clips to
 /// its own rectangle, so a ring or glow outside it would be cut off.</para>
 /// </summary>
-internal static class PluginUiStyle
+internal static partial class PluginUiStyle
 {
     internal const float WindowRadius = 10f;
     internal const float ControlRadius = 6f;
````

- [ ] **Step 4: Add the dock pieces**

`src/AcDream.App/UI/PluginUiStyle.Dock.cs`:

```csharp
using System.Numerics;
using System.Text;

namespace AcDream.App.UI;

/// <summary>Which side of the screen a dock piece faces.</summary>
internal enum DockSide { Left, Right }

/// <summary>Which way a dock chevron points.</summary>
internal enum ChevronDirection { Up, Down, Left, Right }

/// <summary>
/// The plugin dock's shapes and sizes, in interface points. All three themes
/// draw the dock through here; Classic passes <see cref="PluginUiPalette.ClassicDock"/>.
/// </summary>
internal static partial class PluginUiStyle
{
    internal const float DockPadding = 6f;
    internal const float DockSlot = 36f;
    internal const float DockGap = 4f;
    internal const float DockArt = 24f;
    internal const float DockRadius = 14f;
    internal const float DockWidth = DockPadding + DockSlot + DockPadding;
    internal const float DockHandleTop = 2f;
    internal const float DockHandleHeight = 12f;
    internal const float DockSlotsTop = DockHandleTop + DockHandleHeight + 2f;
    internal const float DockHandleWidth = 30f;
    internal const float DockCollapseSize = 12f;
    internal const float DockDividerSpace = 4f;
    internal const float DockDividerInset = 8f;
    internal const float DockFadeHeight = 12f;
    internal const float DockSlotRadius = 9f;
    internal const float DockOpenDot = 3f;
    internal const float DockOpenDotInset = 1.5f;
    internal const float DockCollapsedHeight = 24f;
    internal const float RailWidth = 46f;
    internal const float RailRadius = 12f;
    internal const float RailSlot = 38f;
    internal const float RailGap = 2f;
    internal const float RailPillWidth = 3f;
    internal const float RailPillHover = 4f;
    internal const float RailPillOpen = 8f;
    internal const float RailPillFront = 20f;
    internal const float RailPillSeconds = 0.12f;
    internal const float RailTabWidth = 12f;
    internal const float RailTabHeight = 44f;
    internal const float LabelGap = 8f;
    internal const float LabelPadX = 9f;
    internal const float LabelPadY = 5f;
    internal const float LabelRadius = 7f;
    internal const float MonogramRadius = 6f;
    internal const float ClosedArtAlpha = 0.85f;

    private static readonly Vector4 MonogramInk = Rgb(0xF1ECE2);

    private static readonly Vector4[] MonogramHues =
    [
        Rgb(0x6E8B5E), Rgb(0x5E7F8B), Rgb(0x8B6E5E), Rgb(0x7A6A99),
        Rgb(0x8B8259), Rgb(0x5E6E8B), Rgb(0x8B5E74), Rgb(0x5E8B7A),
    ];

    private static Vector4 Rgb(uint rgb) =>
        new((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f, 1f);

    /// <summary>The dock's rounded corners: all four when floating, only the inner two on a rail.</summary>
    internal static CanvasCornerRadii DockCorners(PluginDockMode mode) => mode switch
    {
        PluginDockMode.Left => new(0f, RailRadius, RailRadius, 0f),
        PluginDockMode.Right => new(RailRadius, 0f, 0f, RailRadius),
        _ => new(DockRadius, DockRadius, DockRadius, DockRadius),
    };

    /// <summary>The dock's body: shadow, fill, a faint top sheen and a rim (left off the screen-edge side of a rail).</summary>
    internal static void DockBody(UiRenderContext ctx, PluginUiPalette p, PluginDockMode mode, float w, float h)
    {
        bool rail = mode != PluginDockMode.Floating;
        float radius = rail ? RailRadius : DockRadius;
        ctx.DrawSoftShadow(0f, 0f, w, h, radius, rail ? 2f : 4f, 14f, Black with { W = 0.42f });
        CanvasCornerRadii corners = DockCorners(mode);
        ctx.FillRoundedRect(0f, 0f, w, h, corners, p.Background);
        Vector4 sheen = p.Text with { W = 0.04f };
        ctx.FillVerticalGradient(0f, 0f, w, MathF.Min(12f, h),
            corners with { BottomLeft = 0f, BottomRight = 0f }, sheen, sheen with { W = 0f });
        Vector4 rim = Mix(p.Border, p.Text, 0.08f);
        // A rail's rim runs off the element on its edge side, where the dock's clip cuts it away.
        float x = mode == PluginDockMode.Left ? -radius : 0f;
        float rimWidth = rail ? w + radius : w;
        ctx.StrokeRoundedRect(x + 0.5f, 0.5f, rimWidth - 1f, h - 1f, radius - 0.5f, rim, 1f);
    }

    /// <summary>The well behind a slot while it is hovered or pressed; nothing when idle.</summary>
    internal static void DockSlotWell(UiRenderContext ctx, PluginUiPalette p, float x, float y, float w, float h, UiControlState state)
    {
        if (state == UiControlState.Hovered)
            ctx.FillRoundedRect(x, y, w, h, DockSlotRadius, Hover(p, p.Field));
        else if (state == UiControlState.Pressed)
            ctx.FillRoundedRect(x, y, w, h, DockSlotRadius, Pressed(Hover(p, p.Field)));
    }

    /// <summary>The small accent dot beside an open window's slot, on the side nearer the screen edge.</summary>
    internal static void OpenDot(UiRenderContext ctx, PluginUiPalette p, DockSide side, float dockWidth, float slotTop, float slotHeight)
    {
        float x = side == DockSide.Left ? DockOpenDotInset : dockWidth - DockOpenDotInset - DockOpenDot;
        ctx.FillEllipse(x, slotTop + (slotHeight - DockOpenDot) / 2f, DockOpenDot, DockOpenDot, p.Accent);
    }

    /// <summary>The pill on a rail's screen edge beside a slot, <paramref name="height"/> tall.</summary>
    internal static void EdgePill(UiRenderContext ctx, Vector4 color, DockSide side, float dockWidth, float slotTop, float slotHeight, float height)
    {
        if (!(height > 0f)) return;
        float r = RailPillWidth;
        float x = side == DockSide.Left ? 0f : dockWidth - RailPillWidth;
        CanvasCornerRadii corners = side == DockSide.Left ? new(0f, r, r, 0f) : new(r, 0f, 0f, r);
        ctx.FillRoundedRect(x, slotTop + (slotHeight - height) / 2f, RailPillWidth, height, corners, color);
    }

    /// <summary>A hairline between two plugins' slots, at <paramref name="y"/>.</summary>
    internal static void DockDivider(UiRenderContext ctx, PluginUiPalette p, float dockWidth, float y) =>
        ctx.DrawFill(DockDividerInset, y, dockWidth - 2f * DockDividerInset, 1f, p.Border);

    /// <summary>A fade into the dock's fill, marking the end where slots are cut off.</summary>
    internal static void DockFade(UiRenderContext ctx, PluginUiPalette p, float y, float w, bool darkAtBottom)
    {
        Vector4 solid = p.Background;
        Vector4 clear = solid with { W = 0f };
        ctx.FillVerticalGradient(0f, y, w, DockFadeHeight, default,
            darkAtBottom ? clear : solid, darkAtBottom ? solid : clear);
    }

    /// <summary>Three small dots, the dock's move handle, centred at (<paramref name="cx"/>, <paramref name="cy"/>).</summary>
    internal static void HandleDots(UiRenderContext ctx, PluginUiPalette p, float cx, float cy)
    {
        for (int i = -1; i <= 1; i++)
            ctx.FillEllipse(cx + i * 5f - 1f, cy - 1f, 2f, 2f, p.Muted);
    }

    /// <summary>A two-stroke chevron centred at (<paramref name="cx"/>, <paramref name="cy"/>), <paramref name="size"/> across.</summary>
    internal static void Chevron(UiRenderContext ctx, float cx, float cy, float size, ChevronDirection direction, Vector4 color)
    {
        float a = size / 2f;
        float b = size / 4f;
        (Vector2 p0, Vector2 tip, Vector2 p1) = direction switch
        {
            ChevronDirection.Up => (new Vector2(-a, b), new Vector2(0f, -b), new Vector2(a, b)),
            ChevronDirection.Down => (new Vector2(-a, -b), new Vector2(0f, b), new Vector2(a, -b)),
            ChevronDirection.Left => (new Vector2(b, -a), new Vector2(-b, 0f), new Vector2(b, a)),
            _ => (new Vector2(-b, -a), new Vector2(b, 0f), new Vector2(-b, a)),
        };
        ctx.DrawSmoothLine(cx + p0.X, cy + p0.Y, cx + tip.X, cy + tip.Y, color, 1.5f);
        ctx.DrawSmoothLine(cx + tip.X, cy + tip.Y, cx + p1.X, cy + p1.Y, color, 1.5f);
    }

    /// <summary>The gear in the dock's appearance slot, filling a <paramref name="size"/> box at (x, y).</summary>
    internal static void Gear(UiRenderContext ctx, float x, float y, float size, Vector4 color)
    {
        float c = size / 2f;
        float ring = size * 0.34f;
        ctx.StrokeEllipse(x + c - ring, y + c - ring, ring * 2f, ring * 2f, color, 1.75f);
        for (int i = 0; i < 8; i++)
        {
            float angle = i * MathF.PI / 4f;
            float dx = MathF.Cos(angle);
            float dy = MathF.Sin(angle);
            ctx.DrawSmoothLine(x + c + dx * ring, y + c + dy * ring,
                x + c + dx * size * 0.46f, y + c + dy * size * 0.46f, color, 2f);
        }
    }

    /// <summary>
    /// The colour behind a plugin's monogram: one of eight fixed hues, picked by
    /// FNV-1a of the plugin id, so a plugin keeps its colour between sessions.
    /// </summary>
    internal static Vector4 MonogramHue(string pluginId)
    {
        uint hash = 2166136261u;
        foreach (byte b in Encoding.UTF8.GetBytes(pluginId))
            hash = (hash ^ b) * 16777619u;
        return MonogramHues[hash % (uint)MonogramHues.Length];
    }

    /// <summary>A plugin's two-letter monogram filling the <paramref name="size"/> box at (x, y).</summary>
    internal static void Monogram(UiRenderContext ctx, UiDatFont? font, string letters, Vector4 hue,
        float x, float y, float size, float alpha)
    {
        Vector4 top = Mix(hue, Vector4.One, 0.12f);
        ctx.FillVerticalGradient(x, y, size, size,
            new CanvasCornerRadii(MonogramRadius, MonogramRadius, MonogramRadius, MonogramRadius),
            top with { W = alpha }, hue with { W = alpha });
        if (font is null || letters.Length == 0) return;
        float width = font.MeasureWidth(letters);
        ctx.DrawStringDat(font, letters, x + (size - width) / 2f, y + (size - font.LineHeight) / 2f,
            MonogramInk with { W = alpha });
    }

    /// <summary>The hover label's box: background, edge and shadow, at (x, y) in the dock's space.</summary>
    internal static void HoverLabel(UiRenderContext ctx, PluginUiPalette p, float x, float y, float w, float h)
    {
        ctx.DrawSoftShadow(x, y, w, h, LabelRadius, 3f, 8f, Black with { W = 0.35f });
        Surface(ctx, x, y, w, h, LabelRadius, p.Background, p.Border);
    }
}
```

- [ ] **Step 5: Run the tests and see them pass**

```bash
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter FullyQualifiedName~PluginDockStyleTests
```

Expected: `Passed: 10`.

- [ ] **Step 6: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/PluginUiStyle.cs src/AcDream.App/UI/PluginUiStyle.Dock.cs tests/AcDream.App.Tests/UI/PluginDockStyleTests.cs
git commit -m "ui: the plugin dock shapes and sizes in PluginUiStyle

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```


### Task 4: Pure dock layout

**Goal:** `PluginDockLayout.Compute(mode, collapsed, ownerIds, maxHeight, firstRow)` returns every dock rectangle: slots (null when scrolled out), dividers, gear, handle, toggle, sizes, scroll range and fades.

**Files:**
- Create: `src/AcDream.App/UI/PluginDockLayout.cs`
- Test: `tests/AcDream.App.Tests/UI/PluginDockLayoutTests.cs`

**Acceptance Criteria:**
- [ ] One floating slot: width 48, height 103 (16 + 36 + 9 + 36 + 6), slot (6,16,36,36), divider at 56.5, gear (6,61,36,36), handle (0,0,30,16), toggle (30,2,12,12).
- [ ] Slots of one owner are 4 apart; a different owner adds a 9pt divider block (4 + 1 + 4) with the line at its middle.
- [ ] Overflow shows what fits from `FirstRow`, clamps `FirstRow` to `MaxFirstRow`, and sets `FadeTop`/`FadeBottom`; at least one slot always shows.
- [ ] Rails are 46 wide with full-width 46×38 slots 2 apart (pitch 40, as floating); the toggle is at (28,2).
- [ ] Collapsed: floating 48×24 with handle (0,0,30,24) and toggle (30,0,12,24); rail 12×44 with handle (0,0,12,14) and toggle (0,14,12,30); no slots or dividers.
- [ ] No entries: no divider before the gear.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter FullyQualifiedName~PluginDockLayoutTests` → 8 passed

**Steps:**

- [ ] **Step 1: Write the failing tests**

`tests/AcDream.App.Tests/UI/PluginDockLayoutTests.cs`:

```csharp
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public sealed class PluginDockLayoutTests
{
    private static PluginDockLayout Floating(string[] owners, float maxHeight = float.PositiveInfinity, int firstRow = 0) =>
        PluginDockLayout.Compute(PluginDockMode.Floating, collapsed: false, owners, maxHeight, firstRow);

    [Fact]
    public void OneSlot_IsHandleRowSlotDividerGearAndPadding()
    {
        PluginDockLayout layout = Floating(["a"]);

        Assert.Equal(48f, layout.Width);
        // 16 (handle row) + 36 (slot) + 9 (divider) + 36 (gear) + 6 (padding)
        Assert.Equal(103f, layout.Height);
        Assert.Equal(new DockRect(6f, 16f, 36f, 36f), layout.Slots[0]);
        Assert.Equal([56.5f], layout.Dividers);
        Assert.Equal(new DockRect(6f, 61f, 36f, 36f), layout.Gear);
        Assert.Equal(new DockRect(0f, 0f, 30f, 16f), layout.Handle);
        Assert.Equal(new DockRect(30f, 2f, 12f, 12f), layout.Toggle);
    }

    [Fact]
    public void SameOwnerSlotsAreGapped_AndADifferentOwnerGetsADivider()
    {
        PluginDockLayout layout = Floating(["moss", "moss", "goarrow"]);

        Assert.Equal(16f, layout.Slots[0]!.Value.Y);
        Assert.Equal(56f, layout.Slots[1]!.Value.Y);   // 16 + 36 + 4
        Assert.Equal(101f, layout.Slots[2]!.Value.Y);  // 56 + 36 + 9
        Assert.Equal([96.5f, 141.5f], layout.Dividers);
        Assert.Equal(146f, layout.Gear.Y);
        Assert.Equal(188f, layout.Height);
    }

    [Fact]
    public void Overflow_ShowsWhatFits_AndScrollingIsClampedToTheLastFullPage()
    {
        string[] owners = Enumerable.Range(0, 12).Select(i => $"p{i}").ToArray();
        // Room for 16 + 3 slots (36 + 9 + 36 + 9 + 36) + 9 + 36 + 6 = 193.
        PluginDockLayout top = Floating(owners, maxHeight: 200f);

        Assert.Equal(3, top.VisibleCount);
        Assert.Equal(0, top.FirstRow);
        Assert.Equal(9, top.MaxFirstRow);
        Assert.False(top.FadeTop);
        Assert.True(top.FadeBottom);
        Assert.True(top.Height <= 200f);
        Assert.NotNull(top.Slots[2]);
        Assert.Null(top.Slots[3]);

        PluginDockLayout end = Floating(owners, maxHeight: 200f, firstRow: 50);
        Assert.Equal(9, end.FirstRow);
        Assert.True(end.FadeTop);
        Assert.False(end.FadeBottom);
        Assert.Null(end.Slots[8]);
        Assert.Equal(16f, end.Slots[9]!.Value.Y);
        Assert.NotNull(end.Slots[11]);
    }

    [Fact]
    public void TooLittleRoom_StillShowsOneSlot()
    {
        PluginDockLayout layout = Floating(["a", "b"], maxHeight: 10f);
        Assert.Equal(1, layout.VisibleCount);
        Assert.NotNull(layout.Slots[0]);
    }

    [Theory]
    [InlineData(PluginDockMode.Left)]
    [InlineData(PluginDockMode.Right)]
    public void Rail_IsFullWidthSlots_WithTheSamePitchAsFloating(PluginDockMode mode)
    {
        PluginDockLayout rail = PluginDockLayout.Compute(mode, false, ["a", "a"], float.PositiveInfinity, 0);
        PluginDockLayout floating = Floating(["a", "a"]);

        Assert.Equal(46f, rail.Width);
        Assert.Equal(new DockRect(0f, 16f, 46f, 38f), rail.Slots[0]);
        Assert.Equal(56f, rail.Slots[1]!.Value.Y);   // 16 + 38 + 2
        // Slot pitch is 40 in both (36 + 4, 38 + 2); the last slot and the gear are 2pt taller each.
        Assert.Equal(floating.Height + 4f, rail.Height);
        Assert.Equal(new DockRect(28f, 2f, 12f, 12f), rail.Toggle);
    }

    [Fact]
    public void Collapsed_FloatingIsAHandlePill_AndARailIsAnEdgeTab()
    {
        PluginDockLayout pill = PluginDockLayout.Compute(PluginDockMode.Floating, true, ["a"], 500f, 0);
        Assert.Equal((48f, 24f), (pill.Width, pill.Height));
        Assert.Equal(new DockRect(0f, 0f, 30f, 24f), pill.Handle);
        Assert.Equal(new DockRect(30f, 0f, 12f, 24f), pill.Toggle);
        Assert.All(pill.Slots, s => Assert.Null(s));
        Assert.Empty(pill.Dividers);

        PluginDockLayout tab = PluginDockLayout.Compute(PluginDockMode.Left, true, ["a"], 500f, 0);
        Assert.Equal((12f, 44f), (tab.Width, tab.Height));
        Assert.Equal(new DockRect(0f, 0f, 12f, 14f), tab.Handle);
        Assert.Equal(new DockRect(0f, 14f, 12f, 30f), tab.Toggle);
    }

    [Fact]
    public void NoEntries_HasNoDividerBeforeTheGear()
    {
        PluginDockLayout layout = Floating([]);
        Assert.Empty(layout.Dividers);
        Assert.Equal(16f, layout.Gear.Y);
    }
}
```

- [ ] **Step 2: Run them and see them fail**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/dock && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter FullyQualifiedName~PluginDockLayoutTests
```

Expected: build error `CS0246: The type or namespace name 'PluginDockLayout' could not be found`.

- [ ] **Step 3: Write the layout**

`src/AcDream.App/UI/PluginDockLayout.cs`:

```csharp
using static AcDream.App.UI.PluginUiStyle;

namespace AcDream.App.UI;

/// <summary>A rectangle in the dock's own space.</summary>
internal readonly record struct DockRect(float X, float Y, float W, float H)
{
    public bool Contains(float x, float y) => x >= X && x < X + W && y >= Y && y < Y + H;
}

/// <summary>
/// Where everything in the plugin dock goes, worked out from the mode, whether
/// it is collapsed, which plugin owns each slot, and how tall it may be. Pure:
/// <see cref="PluginSidePanel"/> applies the result to its children.
/// <para>From the top: the handle row, the slots (a divider between two
/// plugins, a gap between one plugin's windows), a divider, the gear slot,
/// and padding. When the slots do not fit, only <see cref="VisibleCount"/> of
/// them from <see cref="FirstRow"/> are laid out; the handle row and gear stay.</para>
/// </summary>
internal sealed record PluginDockLayout(
    float Width,
    float Height,
    IReadOnlyList<DockRect?> Slots,
    IReadOnlyList<float> Dividers,
    DockRect Gear,
    DockRect Handle,
    DockRect Toggle,
    int FirstRow,
    int VisibleCount,
    int MaxFirstRow,
    bool FadeTop,
    bool FadeBottom,
    float ListTop,
    float ListBottom)
{
    private const float DividerBlock = DockDividerSpace * 2f + 1f;

    public static PluginDockLayout Compute(
        PluginDockMode mode, bool collapsed, IReadOnlyList<string> ownerIds, float maxHeight, int firstRow)
    {
        bool rail = mode != PluginDockMode.Floating;
        int count = ownerIds.Count;
        var slots = new DockRect?[count];

        if (collapsed)
        {
            return rail
                ? new(RailTabWidth, RailTabHeight, slots, [], default,
                    new(0f, 0f, RailTabWidth, 14f), new(0f, 14f, RailTabWidth, RailTabHeight - 14f),
                    0, 0, 0, false, false, 0f, 0f)
                : new(DockWidth, DockCollapsedHeight, slots, [], default,
                    new(0f, 0f, DockHandleWidth, DockCollapsedHeight),
                    new(DockHandleWidth, 0f, DockCollapseSize, DockCollapsedHeight),
                    0, 0, 0, false, false, 0f, 0f);
        }

        float width = rail ? RailWidth : DockWidth;
        float slotX = rail ? 0f : DockPadding;
        float slotW = rail ? RailWidth : DockSlot;
        float slotH = rail ? RailSlot : DockSlot;
        float gap = rail ? RailGap : DockGap;
        float Spacing(int i) => i > 0 && ownerIds[i] != ownerIds[i - 1] ? DividerBlock : gap;

        // Everything that is not the slot list: the handle row above it, and the
        // divider, gear and padding below it.
        float chrome = DockSlotsTop + (count > 0 ? DividerBlock : 0f) + slotH + DockPadding;
        float room = maxHeight - chrome;

        int FitFrom(int first)
        {
            int fitted = 0;
            float used = 0f;
            for (int i = first; i < count; i++)
            {
                float next = used + (fitted > 0 ? Spacing(i) : 0f) + slotH;
                if (next > room && fitted > 0) break;
                used = next;
                fitted++;
            }
            return fitted;
        }

        int fitBackward = 0;
        {
            float used = 0f;
            for (int i = count - 1; i >= 0; i--)
            {
                float next = used + (fitBackward > 0 ? Spacing(i + 1) : 0f) + slotH;
                if (next > room && fitBackward > 0) break;
                used = next;
                fitBackward++;
            }
        }
        int maxFirst = Math.Max(0, count - fitBackward);
        int first = Math.Clamp(firstRow, 0, maxFirst);
        int visible = count == 0 ? 0 : FitFrom(first);

        var dividers = new List<float>();
        float y = DockSlotsTop;
        for (int i = first; i < first + visible; i++)
        {
            if (i > first)
            {
                float spacing = Spacing(i);
                if (spacing == DividerBlock) dividers.Add(y + DockDividerSpace + 0.5f);
                y += spacing;
            }
            slots[i] = new DockRect(slotX, y, slotW, slotH);
            y += slotH;
        }
        float listBottom = y;
        if (count > 0)
        {
            dividers.Add(y + DockDividerSpace + 0.5f);
            y += DividerBlock;
        }
        var gear = new DockRect(slotX, y, slotW, slotH);
        float height = y + slotH + DockPadding;

        return new(width, height, slots, dividers, gear,
            new(0f, 0f, DockHandleWidth, DockSlotsTop),
            new(width - DockPadding - DockCollapseSize, DockHandleTop, DockCollapseSize, DockHandleHeight),
            first, visible, maxFirst, first > 0, first + visible < count, DockSlotsTop, listBottom);
    }
}
```

- [ ] **Step 4: Run the tests and see them pass**

```bash
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter FullyQualifiedName~PluginDockLayoutTests
```

Expected: `Passed: 8`.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/PluginDockLayout.cs tests/AcDream.App.Tests/UI/PluginDockLayoutTests.cs
git commit -m "ui: work out the plugin dock layout in one pure function

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```


### Task 5: Floating dock in every theme

**Goal:** `PluginSidePanel` is rebuilt on `PluginDockLayout` and the dock style: one floating pill in Classic, Moss and Brass, with hover-only handle dots and collapse chevron, a gear slot, open dots, dividers, scroll fades, monograms, and every icon drawn through `PluginShelfButton.DrawIcon(ctx, x, y, extent, colour)`.

**Files:**
- Modify (rewrite): `src/AcDream.App/UI/PluginSidePanel.cs`
- Modify: `tests/AcDream.App.Tests/UI/PluginSidePanelTests.cs`
- Modify (rewrite): `tests/AcDream.App.Tests/UI/PluginSidePanelThemeTests.cs`
- Delete: `tests/AcDream.App.Tests/UI/PluginSidePanelToggleGlyphClipTests.cs`
- Test: `tests/AcDream.App.Tests/UI/PluginDockChromeTests.cs`

**Acceptance Criteria:**
- [ ] All three themes draw the same 48-wide dock; Classic draws in `ClassicDock`, Moss/Brass in their palette; Classic's column wrap is gone and every theme scrolls by one slot per wheel step.
- [ ] The public surface is unchanged: `Add`, `Show`, `Hide`, `EntryCount`, `Dispose`, `CaptureWindowState`/`RestoreWindowState`, the minimize button, window reachability, right-click for appearance, default home (10, 116).
- [ ] The handle row (0..30 × 0..16) is the only move handle; the toggle at (30,2) collapses to a 48×24 pill and back; dots and chevron draw only while the pointer is over the dock (or collapsed).
- [ ] The gear slot (after a divider) invokes the appearance callback.
- [ ] Icon order is unchanged (file icon, DAT surface, monogram); `Text` still holds the initials (two letters at most); colour art draws at 85% alpha while its window is closed and 100% otherwise; iconless slots draw their `MonogramHue`.
- [ ] Open windows get the accent dot; closed ones do not.
- [ ] Monogram letters are two at most in every theme ("ABC" → "AB", "Loot Editor" → "LE", "Golem" → "GO").
- [ ] Existing behaviour tests keep their intent; only geometry assertions changed.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "(FullyQualifiedName~PluginSidePanel|FullyQualifiedName~PluginDockChrome)&Lane!=InstalledDat"` → 46 passed

**Steps:**

- [ ] **Step 1: Write the new chrome tests**

`tests/AcDream.App.Tests/UI/PluginDockChromeTests.cs`:

```csharp
using System.Numerics;
using AcDream.App.Rendering;
using AcDream.App.UI;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.UI;

public sealed class PluginDockChromeTests
{
    private static (UiRoot Root, PluginSidePanel Dock, List<RetailWindowHandle> Windows) Mount(
        int count, float height = 600f, PluginUiThemeSettings? settings = null,
        (uint, int, int)? fileIcon = null)
    {
        var root = new UiRoot { Width = 800f, Height = height };
        var dock = new PluginSidePanel(root.WindowManager, _ => (0u, 0, 0), font: null, settings);
        root.AddChild(dock);
        root.WindowManager.Register(WindowNames.PluginShelf, dock, dock, controller: dock);
        var windows = new List<RetailWindowHandle>();
        for (int i = 0; i < count; i++)
        {
            var frame = new UiPanel { Left = 300f, Width = 200f, Height = 100f };
            root.AddChild(frame);
            RetailWindowHandle handle = root.WindowManager.Register($"plugin:test:{i}", frame);
            dock.Add(new PluginUiOwner($"test.{i}", $"Plugin {i}"),
                new PluginPanelDescriptor("main", $"Plugin {i}"), handle, fileIcon);
            windows.Add(handle);
        }
        root.Tick(0.016d, 16L);
        return (root, dock, windows);
    }

    private static UiElement Grip(PluginSidePanel dock) =>
        Assert.Single(dock.Children, c => c.WindowMoveHandle);

    private static UiSimpleButton Toggle(PluginSidePanel dock) =>
        (UiSimpleButton)Assert.Single(dock.Children, c => c.GetTooltipText() is "Collapse the dock" or "Expand the dock");

    [Fact]
    public void HandleAndToggle_FollowTheLayout_ExpandedAndCollapsed_AfterADraw()
    {
        var (root, dock, _) = Mount(2);
        ThemeDrawCapture.Draw(root);

        Assert.Equal((0f, 0f, 30f, 16f), (Grip(dock).Left, Grip(dock).Top, Grip(dock).Width, Grip(dock).Height));
        Assert.Equal((30f, 2f, 12f, 12f), (Toggle(dock).Left, Toggle(dock).Top, Toggle(dock).Width, Toggle(dock).Height));

        int x = (int)(dock.Left + 36f), y = (int)(dock.Top + 8f);
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
        ThemeDrawCapture.Draw(root);

        Assert.True(dock.CaptureWindowState().Collapsed);
        Assert.Equal((48f, 24f), (dock.Width, dock.Height));
        Assert.Equal((0f, 0f, 30f, 24f), (Grip(dock).Left, Grip(dock).Top, Grip(dock).Width, Grip(dock).Height));
        Assert.Equal((30f, 0f, 12f, 24f), (Toggle(dock).Left, Toggle(dock).Top, Toggle(dock).Width, Toggle(dock).Height));
        Assert.Equal("Expand the dock", Toggle(dock).GetTooltipText());

        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
        Assert.False(dock.CaptureWindowState().Collapsed);
    }

    [Fact]
    public void AfterScrollingAndADraw_EveryVisibleSlotMatchesAFreshLayout()
    {
        var (root, dock, _) = Mount(12, height: 300f);
        ThemeDrawCapture.Draw(root);
        dock.OnEvent(new UiEvent { Type = UiEventType.Scroll, Data0 = -1 });
        dock.OnEvent(new UiEvent { Type = UiEventType.Scroll, Data0 = -1 });
        ThemeDrawCapture.Draw(root);

        string[] owners = Enumerable.Range(0, 12).Select(i => $"test.{i}").ToArray();
        PluginDockLayout fresh = PluginDockLayout.Compute(
            PluginDockMode.Floating, false, owners, 300f - dock.Top - 8f, 2);
        var buttons = dock.Children.OfType<PluginSidePanel.PluginShelfButton>().ToArray();
        for (int i = 0; i < buttons.Length; i++)
        {
            Assert.Equal(fresh.Slots[i] is not null, buttons[i].Visible);
            if (fresh.Slots[i] is { } slot)
                Assert.Equal((slot.X, slot.Y), (buttons[i].Left, buttons[i].Top));
        }
        Assert.Equal(fresh.Height, dock.Height);
    }

    [Fact]
    public void HandleDotsAndToggle_AreDrawnOnlyWhileThePointerIsOverTheDock()
    {
        var (root, dock, _) = Mount(1, settings: new PluginUiThemeSettings { Theme = PluginUiTheme.Moss });
        root.OnMouseMove(700, 500);
        root.Tick(0.016d, 32L);
        int away = ThemeDrawCapture.Draw(root).Length;

        root.OnMouseMove((int)dock.Left + 20, (int)dock.Top + 40);
        root.Tick(0.016d, 48L);
        Assert.True(dock.PointerOver);
        int over = ThemeDrawCapture.Draw(root).Length;

        Assert.True(over > away, $"hovered draw ({over} floats) is not larger than the idle one ({away})");
    }

    [Fact]
    public void AnIconlessPlugin_GetsItsMonogramHue()
    {
        var (_, dock, _) = Mount(1, settings: new PluginUiThemeSettings { Theme = PluginUiTheme.Brass });
        var (renderer, ctx) = ThemeDrawCapture.Context(800, 600);
        dock.DrawSelfAndChildren(ctx);

        Vector4 hue = PluginUiStyle.MonogramHue("test.0");
        Assert.True(ThemeDrawCapture.HasColor(ThemeDrawCapture.Vertices(renderer), hue));
    }

    [Theory]
    [InlineData("ABC", "Loot Editor", "AB")]
    [InlineData(null, "Loot Editor", "LE")]
    [InlineData(null, "Golem", "GO")]
    public void MonogramLetters_AreTwoAtMost_InEveryTheme(string? iconText, string title, string expected)
    {
        foreach (PluginUiTheme theme in Enum.GetValues<PluginUiTheme>())
        {
            var root = new UiRoot { Width = 800f, Height = 600f };
            using var dock = new PluginSidePanel(root.WindowManager, _ => (0u, 0, 0), font: null,
                new PluginUiThemeSettings { Theme = theme });
            root.AddChild(dock);
            var frame = new UiPanel { Width = 200f, Height = 100f };
            root.AddChild(frame);
            dock.Add(new PluginUiOwner("test", "Test"),
                new PluginPanelDescriptor("main", title) { IconText = iconText },
                root.WindowManager.Register("plugin:test:main", frame));

            Assert.Equal(expected, Assert.Single(dock.Children.OfType<PluginSidePanel.PluginShelfButton>()).Text);
        }
    }

    [Fact]
    public void ColourArt_IsDimmerWhileItsWindowIsClosed()
    {
        var (_, dock, windows) = Mount(1, fileIcon: (7u, 64, 64));
        PluginSidePanel.PluginShelfButton button =
            Assert.Single(dock.Children.OfType<PluginSidePanel.PluginShelfButton>());

        windows[0].Hide();
        Assert.Equal(PluginUiStyle.ClosedArtAlpha, IconAlpha(button), 3);
        windows[0].Show();
        Assert.Equal(1f, IconAlpha(button), 3);
    }

    private static float IconAlpha(PluginSidePanel.PluginShelfButton button)
    {
        var (renderer, ctx) = ThemeDrawCapture.Context(200, 200);
        button.DrawSelfAndChildren(ctx);
        var icon = Assert.Single(renderer.DebugSpriteSegmentVerts, s => s.Texture == 7u);
        return icon.Verts[TextRenderer.FloatsPerVertex - 1];
    }
}
```

- [ ] **Step 2: Rewrite the theme tests for one dock shape**

`tests/AcDream.App.Tests/UI/PluginSidePanelThemeTests.cs`:

```csharp
using System.Numerics;
using AcDream.App.UI;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.UI;

public sealed class PluginSidePanelThemeTests
{
    [Theory]
    [InlineData(PluginUiTheme.Classic)]
    [InlineData(PluginUiTheme.Moss)]
    [InlineData(PluginUiTheme.Brass)]
    public void EveryThemeHasTheSameDockShape_AndScrollsEveryEntryIntoView(PluginUiTheme theme)
    {
        var root = new UiRoot { Width = 800, Height = 260 };
        var settings = new PluginUiThemeSettings { Theme = theme };
        using var shelf = new PluginSidePanel(root.WindowManager, _ => (0u, 0, 0), null, settings);
        root.AddChild(shelf);
        for (int i = 0; i < 12; i++) Add(root, shelf, i);
        root.Tick(0.016, 16);
        Assert.Equal(PluginUiStyle.DockWidth, shelf.Width);
        Assert.True(shelf.Top + shelf.Height <= root.Height);
        var buttons = shelf.Children.OfType<PluginSidePanel.PluginShelfButton>().ToArray();
        Assert.True(buttons[0].Visible);
        Assert.False(buttons[^1].Visible);
        for (int i = 0; i < 20; i++) shelf.OnEvent(new UiEvent { Type = UiEventType.Scroll, Data0 = -1 });
        Assert.True(buttons[^1].Visible);
        Assert.False(buttons[0].Visible);
        Assert.All(buttons.Where(b => b.Visible), b =>
        {
            Assert.Equal(PluginUiStyle.DockPadding, b.Left);
            Assert.Equal(PluginUiStyle.DockSlot, b.Width);
            Assert.True(b.Top >= PluginUiStyle.DockSlotsTop);
            Assert.True(b.Top + b.Height <= shelf.Height);
        });
    }

    [Theory]
    [InlineData(PluginUiTheme.Classic)]
    [InlineData(PluginUiTheme.Moss)]
    [InlineData(PluginUiTheme.Brass)]
    public void RightClickOnEntryOpensAppearanceWithoutTogglingPlugin(PluginUiTheme theme)
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var settings = new PluginUiThemeSettings { Theme = theme };
        int requests = 0;
        using var shelf = new PluginSidePanel(root.WindowManager, _ => (0u, 0, 0), null, settings,
            () => requests++);
        var handle = Add(root, shelf, 0);
        root.AddChild(shelf);
        root.Tick(0.016, 16);
        var button = Assert.Single(shelf.Children.OfType<PluginSidePanel.PluginShelfButton>());
        int x = (int)(shelf.Left + button.Left + button.Width / 2);
        int y = (int)(shelf.Top + button.Top + button.Height / 2);
        bool visible = handle.IsVisible;
        root.OnMouseDown(UiMouseButton.Right, x, y, 0);
        root.OnMouseUp(UiMouseButton.Right, x, y, 0);
        Assert.Equal(1, requests);
        Assert.Equal(visible, handle.IsVisible);
    }

    [Fact]
    public void TheGearOpensAppearance()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        int requests = 0;
        using var shelf = new PluginSidePanel(root.WindowManager, _ => (0u, 0, 0), null,
            new PluginUiThemeSettings(), () => requests++);
        Add(root, shelf, 0);
        root.AddChild(shelf);
        root.Tick(0.016, 16);
        DockRect gear = shelf.Layout.Gear;
        int x = (int)(shelf.Left + gear.X + gear.W / 2);
        int y = (int)(shelf.Top + gear.Y + gear.H / 2);
        root.OnMouseDown(UiMouseButton.Left, x, y, 0);
        root.OnMouseUp(UiMouseButton.Left, x, y, 0);
        Assert.Equal(1, requests);
    }

    private static RetailWindowHandle AddThemed(UiRoot root, PluginSidePanel shelf, PluginUiThemeSettings settings, int index)
    {
        var frame = MarkupDocument.Build(
            "<panel x=\"100\" y=\"100\" w=\"200\" h=\"100\" title=\"T\" theme=\"plugin\" />",
            new object(), _ => (0u, 0, 0), themes: settings);
        root.AddChild(frame);
        var handle = root.WindowManager.Register($"plugin:themed:{index}", frame);
        shelf.Add(new PluginUiOwner($"themed.{index}", $"Themed {index}"),
            new PluginPanelDescriptor("main", $"Themed {index}") { IconText = "TT" }, handle);
        return handle;
    }

    [Fact]
    public void TheDockDrawsInTheDockPalette_AndMinimizeFollowsTheWindowsTheme()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var settings = new PluginUiThemeSettings { Theme = PluginUiTheme.Moss };
        using var shelf = new PluginSidePanel(root.WindowManager, _ => (0u, 0, 0), null, settings);
        root.AddChild(shelf);
        var themed = AddThemed(root, shelf, settings, 0);
        var plain = Add(root, shelf, 1);
        root.Tick(0.016, 16);

        var themedMin = themed.OuterFrame.Children.OfType<UiSimpleButton>().Single(b => b.Text == "–");
        var plainMin = plain.OuterFrame.Children.OfType<UiSimpleButton>().Single(b => b.Text == "–");
        Assert.Same(settings.Palette, themedMin.ThemePalette);
        Assert.Equal(settings.Palette!.Muted, themedMin.TextColor);
        Assert.Null(plainMin.ThemePalette);
        Assert.True(plainMin.Outline);

        var (renderer, ctx) = ThemeDrawCapture.Context(800, 600);
        shelf.DrawSelfAndChildren(ctx);
        var moss = ThemeDrawCapture.Vertices(renderer);
        Assert.True(ThemeDrawCapture.HasColor(moss, PluginUiPalette.Moss.Background));
        Assert.Contains(moss, v => v.Position.Y > shelf.Top + shelf.Height);   // the shadow

        settings.Theme = PluginUiTheme.Classic;
        root.Tick(0.016, 32);
        (renderer, ctx) = ThemeDrawCapture.Context(800, 600);
        shelf.DrawSelfAndChildren(ctx);
        Assert.True(ThemeDrawCapture.HasColor(ThemeDrawCapture.Vertices(renderer), PluginUiPalette.ClassicDock.Border,
            tolerance: 0.05f));
        Assert.Null(themedMin.ThemePalette);
        Assert.Equal(Vector4.One, themedMin.TextColor);
    }

    [Fact]
    public void AnOpenWindowGetsAnAccentDot_AndAClosedOneDoesNot()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var settings = new PluginUiThemeSettings { Theme = PluginUiTheme.Moss };
        using var shelf = new PluginSidePanel(root.WindowManager, _ => (0u, 0, 0), null, settings);
        root.AddChild(shelf);
        var handle = Add(root, shelf, 0);
        root.Tick(0.016, 16);

        handle.Show();
        var (renderer, ctx) = ThemeDrawCapture.Context(800, 600);
        shelf.DrawSelfAndChildren(ctx);
        Assert.Contains(ThemeDrawCapture.Vertices(renderer), v => IsDot(v, shelf));

        handle.Hide();
        (renderer, ctx) = ThemeDrawCapture.Context(800, 600);
        shelf.DrawSelfAndChildren(ctx);
        Assert.DoesNotContain(ThemeDrawCapture.Vertices(renderer), v => IsDot(v, shelf));
    }

    private static bool IsDot((Vector2 Position, Vector4 Color) v, PluginSidePanel shelf) =>
        v.Position.X < shelf.Left + PluginUiStyle.DockPadding
        && v.Color.W > 0.5f
        && MathF.Abs(v.Color.X - PluginUiPalette.Moss.Accent.X) < 0.01f
        && MathF.Abs(v.Color.Y - PluginUiPalette.Moss.Accent.Y) < 0.01f;

    private static RetailWindowHandle Add(UiRoot root, PluginSidePanel shelf, int index)
    {
        var frame = new UiPanel { Left = 100, Width = 200, Height = 100 };
        root.AddChild(frame);
        var handle = root.WindowManager.Register($"plugin:test:{index}", frame);
        shelf.Add(new PluginUiOwner($"test.{index}", $"Plugin {index}"),
            new PluginPanelDescriptor("main", $"Plugin {index}") { IconText = "TP" }, handle);
        return handle;
    }
}
```

- [ ] **Step 3: Update the geometry assertions in `PluginSidePanelTests`**

The behaviour tests stay; only what pinned the old 28/22pt tiles, column wrap and grip band changes. Save the diff below as `/tmp/dock-PluginSidePanelTests.cs.patch` and run `git apply /tmp/dock-PluginSidePanelTests.cs.patch` from the worktree root (or make the same edits by hand).

````diff
diff --git a/tests/AcDream.App.Tests/UI/PluginSidePanelTests.cs b/tests/AcDream.App.Tests/UI/PluginSidePanelTests.cs
index f434f15d..d7ac23c5 100644
--- a/tests/AcDream.App.Tests/UI/PluginSidePanelTests.cs
+++ b/tests/AcDream.App.Tests/UI/PluginSidePanelTests.cs
@@ -11,7 +11,7 @@ namespace AcDream.App.Tests.UI;
 public sealed class PluginSidePanelTests
 {
     [Fact]
-    public void ManyPluginsWrapIntoReachableColumnsWithinTheLiveScreenHeight()
+    public void ManyPluginsScrollWithinTheLiveScreenHeight()
     {
         var root = new UiRoot { Width = 800f, Height = 260f };
         using var shelf = new PluginSidePanel(
@@ -36,11 +36,14 @@ public sealed class PluginSidePanelTests
         root.Tick(0.016d, 16L);
 
         Assert.Equal(12, shelf.EntryCount);
-        Assert.True(shelf.Width > 36f);
+        Assert.Equal(48f, shelf.Width);
         Assert.True(shelf.Top + shelf.Height <= root.Height);
         Assert.All(
-            shelf.Children,
+            shelf.Children.Where(child => child.Visible),
             child => Assert.True(child.Top + child.Height <= shelf.Height));
+        Assert.Contains(
+            shelf.Children.OfType<PluginSidePanel.PluginShelfButton>(),
+            button => !button.Visible);
         Assert.Equal(10f, shelf.Left);
     }
 
@@ -160,9 +163,9 @@ public sealed class PluginSidePanelTests
     }
 
     [Fact]
-    public void TopLeftCorner_StaysFixed_WhenReflowChangesWidthWhileStillDocked()
+    public void TopLeftCorner_StaysFixed_WhenReflowChangesHeightWhileStillHome()
     {
-        var root = new UiRoot { Width = 800f, Height = 260f };
+        var root = new UiRoot { Width = 800f, Height = 600f };
         using var shelf = new PluginSidePanel(
             root.WindowManager, _ => (0u, 0, 0), font: null);
         root.AddChild(shelf);
@@ -182,12 +185,12 @@ public sealed class PluginSidePanelTests
 
         root.Tick(0.016d, 16L);
         float leftEdge = shelf.Left;
-        float widthBefore = shelf.Width;
+        float heightBefore = shelf.Height;
 
-        root.Height = 150f;
+        root.Height = 260f;
         root.Tick(0.016d, 16L);
 
-        Assert.NotEqual(widthBefore, shelf.Width);
+        Assert.NotEqual(heightBefore, shelf.Height);
         Assert.Equal(leftEdge, shelf.Left, precision: 3);
     }
 
@@ -220,11 +223,11 @@ public sealed class PluginSidePanelTests
         Assert.Equal(216f, shelf.Top);
 
         float leftAfterDrag = shelf.Left;
-        float widthBeforeCollapse = shelf.Width;
+        float heightBeforeCollapse = shelf.Height;
 
         shelf.RestoreWindowState(new RetainedWindowState(Collapsed: true));
 
-        Assert.NotEqual(widthBeforeCollapse, shelf.Width);
+        Assert.NotEqual(heightBeforeCollapse, shelf.Height);
         Assert.Equal(leftAfterDrag, shelf.Left);
     }
 
@@ -247,8 +250,8 @@ public sealed class PluginSidePanelTests
             pluginHandle);
         root.Tick(0.016d, 16L);   // establishes the initial left-edge dock
 
-        int pressX = (int)shelf.Left + 2;   // left of button.Left (4)
-        int pressY = (int)shelf.Top + (int)shelf.ExpandedGripBandHeight + 8;
+        int pressX = (int)shelf.Left + 2;   // left of the slot, which starts at 6
+        int pressY = (int)shelf.Top + (int)shelf.Layout.Slots[0]!.Value.Y + 8;
         float leftBefore = shelf.Left;
         float topBefore = shelf.Top;
 
@@ -294,7 +297,7 @@ public sealed class PluginSidePanelTests
     }
 
     [Fact]
-    public void Collapse_HidesButtonsAndShrinksWidth_RoundTripsThroughWindowState()
+    public void Collapse_HidesButtonsAndShrinksToTheHandlePill_RoundTripsThroughWindowState()
     {
         var root = new UiRoot { Width = 800f, Height = 600f };
         using var shelf = new PluginSidePanel(
@@ -312,7 +315,7 @@ public sealed class PluginSidePanelTests
             pluginHandle);
         root.Tick(0.016d, 16L);
 
-        float expandedWidth = shelf.Width;
+        float expandedHeight = shelf.Height;
         float leftBeforeCollapse = shelf.Left;
         PluginSidePanel.PluginShelfButton button = Assert.Single(
             shelf.Children.OfType<PluginSidePanel.PluginShelfButton>());
@@ -324,7 +327,8 @@ public sealed class PluginSidePanelTests
         root.OnMouseUp(UiMouseButton.Left, toggleX, toggleY);
 
         Assert.False(button.Visible);
-        Assert.True(shelf.Width < expandedWidth);
+        Assert.Equal((PluginUiStyle.DockWidth, PluginUiStyle.DockCollapsedHeight), (shelf.Width, shelf.Height));
+        Assert.True(shelf.Height < expandedHeight);
         RetainedWindowState captured = shelf.CaptureWindowState();
         Assert.True(captured.Collapsed);
         // The left origin remains fixed when the shelf width changes.
@@ -336,7 +340,7 @@ public sealed class PluginSidePanelTests
         shelf.RestoreWindowState(new RetainedWindowState(Collapsed: false));
 
         Assert.True(button.Visible);
-        Assert.Equal(expandedWidth, shelf.Width);
+        Assert.Equal(expandedHeight, shelf.Height);
         Assert.False(shelf.CaptureWindowState().Collapsed);
     }
 
@@ -686,7 +690,7 @@ public sealed class PluginSidePanelTests
     }
 
     [Fact]
-    public void CollapseExpandViaTheToggle_DoesNotCollapseTheColumnWrapToOneColumn()
+    public void CollapseThenExpandViaTheToggle_WithManyPlugins_StillFitsTheScreen()
     {
         var root = new UiRoot { Width = 800f, Height = 260f };
         using var shelf = new PluginSidePanel(
@@ -720,7 +724,7 @@ public sealed class PluginSidePanelTests
 
         Assert.True(shelf.Top + shelf.Height <= root.Height);
         Assert.All(
-            shelf.Children.OfType<PluginSidePanel.PluginShelfButton>(),
+            shelf.Children.OfType<PluginSidePanel.PluginShelfButton>().Where(button => button.Visible),
             button => Assert.True(button.Top + button.Height <= shelf.Height));
     }
 
````

- [ ] **Step 4: Delete the old glyph-clip tests**

They measured the `>`/`<` glyph inside the old grip band, which no longer exists; `PluginDockChromeTests` covers the new handle and toggle.

```bash
git rm tests/AcDream.App.Tests/UI/PluginSidePanelToggleGlyphClipTests.cs
```

- [ ] **Step 5: Run the tests and see them fail**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/dock && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "(FullyQualifiedName~PluginSidePanel|FullyQualifiedName~PluginDockChrome)&Lane!=InstalledDat"
```

Expected: build errors such as `CS1061: 'PluginSidePanel' does not contain a definition for 'Layout'`.

- [ ] **Step 6: Rewrite `PluginSidePanel`**

Replace the whole file with:

`src/AcDream.App/UI/PluginSidePanel.cs`:

```csharp
using System.Numerics;
using AcDream.Plugin.Abstractions;
using static AcDream.App.UI.PluginUiStyle;

namespace AcDream.App.UI;

/// <summary>
/// The plugin dock: one slot per plugin window, a move handle, a collapse
/// button and a gear that opens plugin appearance. It draws the same shape in
/// every theme, in the theme's palette or the dock's own Classic colours
/// (<see cref="PluginUiThemeSettings.DockPalette"/>). Where things go is
/// worked out by <see cref="PluginDockLayout"/>.
/// </summary>
public sealed class PluginSidePanel : UiPanel, IDisposable, IRetainedWindowStateController, IRetainedPanelController
{
    private const float DefaultTop = 116f;
    private const float DefaultLeft = 10f;
    private const float BottomMargin = 8f;

    private readonly RetailWindowManager _windows;
    private readonly Func<uint, (uint tex, int width, int height)> _resolve;
    private readonly UiDatFont? _font;
    private readonly Dictionary<RetailWindowHandle, ShelfEntry> _entries = [];
    private readonly List<RetailWindowHandle> _order = [];
    private readonly DockGrip _grip;
    private readonly DockToggleButton _toggle;
    private readonly DockGearButton _gear;
    private readonly PluginUiThemeSettings _themes;
    private readonly Action? _appearanceRequested;
    private PluginUiTheme _lastTheme;
    private PluginDockLayout _layout;
    private int _firstRow;
    private bool _disposed;
    private float _lastLayoutHeight = -1f;

    private bool _collapsed;

    private bool _requestedVisible = true;

    private bool _userPositioned;

    private bool _homeApplied;

    private float _homeLeft;
    private float _homeTop;

    private RetailWindowHandle? _ownHandle;

    public PluginSidePanel(
        RetailWindowManager windows,
        Func<uint, (uint tex, int width, int height)> resolve,
        UiDatFont? font,
        PluginUiThemeSettings? themes = null,
        Action? appearanceRequested = null)
    {
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _font = font;
        _themes = themes ?? new PluginUiThemeSettings();
        _appearanceRequested = appearanceRequested;
        _lastTheme = _themes.Theme;

        Top = DefaultTop;
        Anchors = AnchorEdges.None;
        Draggable = false;
        Resizable = false;
        // The dock's Width/Height are entirely derived (Reflow), so a restored
        // layout's saved dimensions must never stomp them via ResizeTo.
        ResizeX = false;
        ResizeY = false;
        BackgroundColor = Vector4.Zero;
        BorderColor = Vector4.Zero;
        Visible = false;

        _grip = new DockGrip(this) { WindowMoveHandle = true, Anchors = AnchorEdges.None };
        _toggle = new DockToggleButton(this) { Anchors = AnchorEdges.None };
        _toggle.Click += ToggleCollapsed;
        _gear = new DockGearButton(this) { Anchors = AnchorEdges.None };
        _gear.Click += () => _appearanceRequested?.Invoke();
        AddChild(_grip);
        AddChild(_toggle);
        AddChild(_gear);
        _layout = PluginDockLayout.Compute(PluginDockMode.Floating, false, [], float.PositiveInfinity, 0);
        Reflow();

        _windows.WindowUnregistered += OnWindowUnregistered;
        _windows.WindowRegistered += OnWindowRegistered;
    }

    /// <summary>Number of live plugin-window entries, exposed for gates.</summary>
    public int EntryCount => _entries.Count;

    /// <summary>Where the dock's parts are now, for tests and for drawing.</summary>
    internal PluginDockLayout Layout => _layout;

    internal PluginUiPalette Palette => _themes.DockPalette;

    /// <summary>The font for monograms and labels: the bundled title face in Moss/Brass, the DAT font in Classic.</summary>
    internal UiDatFont? TitleFont => _themes.Palette is null ? _font : _themes.ModernTitleFont ?? _themes.ModernFont ?? _font;

    internal UiDatFont? BodyFont => _themes.Palette is null ? _font : _themes.ModernFont ?? _font;

    /// <summary>Whether the pointer is over the dock, which shows the handle dots and the collapse button.</summary>
    internal bool PointerOver { get; private set; }

    internal bool Collapsed => _collapsed;

    /// <summary>
    /// Adds one manifest-scoped plugin window and its minimize affordance.
    /// Duplicate handles are idempotent.
    /// </summary>
    public void Add(
        PluginUiOwner owner,
        PluginPanelDescriptor descriptor,
        RetailWindowHandle handle,
        (uint Texture, int Width, int Height)? fileIcon = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner.Id);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(handle);
        if (_entries.ContainsKey(handle))
            return;

        handle.OuterFrame.ConstrainResizeToParent = true;
        KeepWindowReachable(handle);

        var button = new PluginShelfButton(this, descriptor, owner, handle, _resolve, fileIcon)
        {
            Width = DockSlot,
            Height = DockSlot,
        };
        button.Click += () =>
        {
            if (handle.IsVisible)
                handle.Hide();
            else
                handle.Show();
        };

        var minimize = new PluginMinimizeButton(handle, _font, _themes)
        {
            Left = MathF.Max(8f, handle.OuterFrame.Width - 23f),
            Top = 3f,
            Width = 18f,
            Height = 17f,
            Anchors = AnchorEdges.Top | AnchorEdges.Right,
        };
        handle.OuterFrame.AddChild(minimize);

        _entries.Add(handle, new ShelfEntry(button, minimize));
        _order.Add(handle);
        AddChild(button);
        Reflow();
    }

    public void Show()
    {
        _requestedVisible = true;
        if (_collapsed)
            _collapsed = false;
        Reflow();
        _ownHandle?.NotifyStateChanged();
    }

    /// <summary>Hide the dock. Preserves entries/positions; never disables a
    /// plugin or touches any plugin window's own visibility.</summary>
    public void Hide()
    {
        _requestedVisible = false;
        Reflow();
        _ownHandle?.NotifyStateChanged();
    }

    protected override void OnDraw(UiRenderContext ctx)
    {
        PluginUiPalette p = Palette;
        DockBody(ctx, p, PluginDockMode.Floating, Width, Height);
        foreach (float y in _layout.Dividers)
            DockDivider(ctx, p, Width, y);
        if (_collapsed) return;
        DockSide side = ScreenSide;
        foreach (ShelfEntry entry in _entries.Values)
        {
            if (entry.Button.Visible && entry.Button.IsOpen)
                OpenDot(ctx, p, side, Width, entry.Button.Top, entry.Button.Height);
        }
    }

    protected override void OnDrawAfterChildren(UiRenderContext ctx)
    {
        if (_collapsed) return;
        if (_layout.FadeTop) DockFade(ctx, Palette, _layout.ListTop, Width, darkAtBottom: false);
        if (_layout.FadeBottom) DockFade(ctx, Palette, _layout.ListBottom - DockFadeHeight, Width, darkAtBottom: true);
    }

    /// <summary>The side of the screen the dock is nearer: where its open dots go.</summary>
    internal DockSide ScreenSide =>
        Parent is { } parent && Left + Width / 2f > parent.Width / 2f ? DockSide.Right : DockSide.Left;

    protected override void OnTick(double deltaSeconds)
    {
        base.OnTick(deltaSeconds);

        if (_lastTheme != _themes.Theme)
        {
            _lastTheme = _themes.Theme;
            Reflow();
        }
        _grip.Opacity = _windows.IsLocked ? 0.5f : 1f;
        PointerOver = Parent is UiRoot root
            && root.MouseX >= Left && root.MouseX < Left + Width
            && root.MouseY >= Top && root.MouseY < Top + Height;

        if (Parent is { } parent)
        {
            float availableHeight = MathF.Max(0f, parent.Height - Top - BottomMargin);
            if (MathF.Abs(availableHeight - _lastLayoutHeight) > 0.5f)
            {
                _lastLayoutHeight = availableHeight;
                Reflow(availableHeight);
            }

            if (!_homeApplied && !_userPositioned && parent.Width > 0f)
            {
                float homeLeft = MathF.Min(DefaultLeft, MathF.Max(0f, parent.Width - Width));
                float homeTop = Top;
                _homeLeft = homeLeft;
                _homeTop = homeTop;
                _homeApplied = true;
                if (_ownHandle is { } handle)
                    handle.MoveTo(homeLeft, homeTop);
                else
                    Left = homeLeft;
            }
        }

        foreach (RetailWindowHandle handle in _entries.Keys)
            KeepWindowReachable(handle);
    }

    /// <inheritdoc />
    public override bool OnEvent(in UiEvent e)
    {
        if (e.Type == UiEventType.RightClick && _appearanceRequested is not null)
        {
            _appearanceRequested();
            return true;
        }
        if (e.Type == UiEventType.Scroll && !_collapsed)
        {
            _firstRow = Math.Clamp(_firstRow + (e.Data0 > 0 ? -1 : 1), 0, _layout.MaxFirstRow);
            Reflow();
            return true;
        }
        return base.OnEvent(in e);
    }

    private void ToggleCollapsed()
    {
        _collapsed = !_collapsed;
        Reflow();
        _ownHandle?.NotifyStateChanged();
    }

    private void OnWindowRegistered(RetailWindowHandle handle)
    {
        if (!ReferenceEquals(handle.OuterFrame, this)) return;
        _ownHandle = handle;
        handle.Moved += OnHandleMoved;
        _windows.WindowRegistered -= OnWindowRegistered;
    }

    private void OnHandleMoved(RetailWindowHandle _)
    {
        if (!_homeApplied)
        {
            _userPositioned = true;
            return;
        }
        if (Parent is { } parent)
        {
            float currentHomeLeft = MathF.Min(DefaultLeft, MathF.Max(0f, parent.Width - Width));
            float reachabilityClampOfPriorHome = Math.Clamp(
                _homeLeft, 0f, MathF.Max(0f, parent.Width - Width));
            bool stillHome = Top == _homeTop
                && (Left == currentHomeLeft || Left == reachabilityClampOfPriorHome);
            if (stillHome)
            {
                _homeLeft = Left;
                _homeTop = Top;
                return;
            }
        }

        if (Left != _homeLeft || Top != _homeTop)
            _userPositioned = true;
    }

    // ── IRetainedPanelController: separates "has entries" (availability) from
    // the user's own show/hide request — see ApplyVisibility. ────────────────

    void IRetainedPanelController.OnShown() => _requestedVisible = true;

    void IRetainedPanelController.OnHidden()
    {
        // Hidden because EntryCount hit zero is temporary (availability gate);
        // hidden while entries remain is a real user hide.
        if (_entries.Count > 0)
            _requestedVisible = false;
    }

    public RetainedWindowState CaptureWindowState() =>
        new(Collapsed: _collapsed, RequestedVisible: _requestedVisible);

    public void RestoreWindowState(RetainedWindowState state)
    {
        _collapsed = state.Collapsed;
        if (state.RequestedVisible is { } requestedVisible)
            _requestedVisible = requestedVisible;
        Reflow();
    }

    private void OnWindowUnregistered(RetailWindowHandle handle)
    {
        if (!_entries.Remove(handle, out ShelfEntry entry))
            return;

        _order.Remove(handle);
        RemoveChild(entry.Button);
        if (ReferenceEquals(entry.Minimize.Parent, handle.OuterFrame))
            handle.OuterFrame.RemoveChild(entry.Minimize);
        Reflow();
    }

    private static void KeepWindowReachable(RetailWindowHandle handle)
    {
        if (handle.OuterFrame.Parent is not { } parent
            || parent.Width <= 0f
            || parent.Height <= 0f)
        {
            return;
        }

        float left = Math.Clamp(
            handle.Left,
            0f,
            MathF.Max(0f, parent.Width - handle.Width));
        float top = Math.Clamp(
            handle.Top,
            0f,
            MathF.Max(0f, parent.Height - handle.Height));
        if (left != handle.Left || top != handle.Top)
            handle.MoveTo(left, top);
    }

    private void Reflow(float? maximumHeight = null)
    {
        float effectiveHeight = maximumHeight
            ?? (_lastLayoutHeight >= 0f ? _lastLayoutHeight : float.PositiveInfinity);
        string[] owners = _order.Select(h => _entries[h].Button.OwnerId).ToArray();
        _layout = PluginDockLayout.Compute(PluginDockMode.Floating, _collapsed, owners, effectiveHeight, _firstRow);
        _firstRow = _layout.FirstRow;

        for (int i = 0; i < _order.Count; i++)
        {
            PluginShelfButton button = _entries[_order[i]].Button;
            if (_layout.Slots[i] is { } slot)
            {
                Place(button, slot);
                button.Visible = true;
            }
            else
            {
                button.Visible = false;
            }
        }

        Width = _layout.Width;
        Height = _layout.Height;
        Place(_grip, _layout.Handle);
        Place(_toggle, _layout.Toggle);
        Place(_gear, _layout.Gear);
        _gear.Visible = !_collapsed;
        ApplyVisibility();

        if (_homeApplied && !_userPositioned)
        {
            _homeLeft = Left;
            _homeTop = Top;
        }
    }

    private static void Place(UiElement element, DockRect rect)
    {
        element.Left = rect.X;
        element.Top = rect.Y;
        element.Width = rect.W;
        element.Height = rect.H;
    }

    private void ApplyVisibility()
    {
        Visible = _requestedVisible && _entries.Count > 0;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _windows.WindowUnregistered -= OnWindowUnregistered;
        _windows.WindowRegistered -= OnWindowRegistered;
        if (_ownHandle is { } ownHandle)
            ownHandle.Moved -= OnHandleMoved;

        foreach ((RetailWindowHandle handle, ShelfEntry entry) in _entries)
        {
            if (ReferenceEquals(entry.Minimize.Parent, handle.OuterFrame))
                handle.OuterFrame.RemoveChild(entry.Minimize);
        }
        _entries.Clear();
        _order.Clear();
        Visible = false;
    }

    private readonly record struct ShelfEntry(
        PluginShelfButton Button,
        PluginMinimizeButton Minimize);

    /// <summary>The move handle: three dots, shown while the pointer is over the dock or it is collapsed.</summary>
    private sealed class DockGrip(PluginSidePanel dock) : UiPanel
    {
        protected override void OnDraw(UiRenderContext ctx)
        {
            if (!dock.PointerOver && !dock.Collapsed) return;
            HandleDots(ctx, dock.Palette, Width / 2f, dock.Collapsed ? Height / 2f : DockHandleTop + DockHandleHeight / 2f);
        }
    }

    /// <summary>Collapses or expands the dock. Shown with the handle dots.</summary>
    private sealed class DockToggleButton(PluginSidePanel dock) : UiSimpleButton
    {
        public override string? GetTooltipText() => dock.Collapsed ? "Expand the dock" : "Collapse the dock";

        protected override void OnDraw(UiRenderContext ctx)
        {
            if (!dock.PointerOver && !dock.Collapsed) return;
            PluginUiPalette p = dock.Palette;
            GhostButton(ctx, p, Width, Height, ThemeState);
            Chevron(ctx, Width / 2f, Height / 2f, 6f,
                dock.Collapsed ? ChevronDirection.Down : ChevronDirection.Up,
                ThemeState == UiControlState.Normal ? p.Muted : p.Text);
        }
    }

    /// <summary>The gear slot at the bottom of the dock, which opens plugin appearance.</summary>
    private sealed class DockGearButton(PluginSidePanel dock) : UiSimpleButton
    {
        public override string? GetTooltipText() => "Plugin appearance";

        protected override void OnDraw(UiRenderContext ctx)
        {
            PluginUiPalette p = dock.Palette;
            DockSlotWell(ctx, p, 0f, 0f, Width, Height, ThemeState);
            Gear(ctx, (Width - DockArt) / 2f, (Height - DockArt) / 2f, DockArt,
                ThemeState == UiControlState.Normal ? p.Muted : p.Text);
        }
    }

    internal sealed class PluginShelfButton : UiSimpleButton
    {
        private readonly PluginSidePanel _dock;
        private readonly RetailWindowHandle _handle;
        private readonly Func<uint, (uint tex, int width, int height)> _resolve;
        private readonly (uint Texture, int Width, int Height)? _fileIcon;
        private readonly uint _iconSurfaceId;
        private readonly string _tooltip;
        private readonly string _initialsFallback;
        private readonly Vector4 _monogramHue;

        private bool _iconResolveAttempted;
        private bool _iconAvailable;

        internal PluginShelfButton(
            PluginSidePanel dock,
            PluginPanelDescriptor descriptor,
            PluginUiOwner owner,
            RetailWindowHandle handle,
            Func<uint, (uint tex, int width, int height)> resolve,
            (uint Texture, int Width, int Height)? fileIcon = null)
        {
            _dock = dock;
            _handle = handle;
            _resolve = resolve;
            _fileIcon = fileIcon;
            OwnerId = owner.Id;
            WindowTitle = descriptor.Title;
            OwnerName = owner.DisplayName;
            _iconSurfaceId = PluginIcons.Normalize(descriptor.IconSurfaceId);
            _tooltip = string.Equals(descriptor.Title, owner.DisplayName,
                    StringComparison.Ordinal)
                ? descriptor.Title
                : $"{owner.DisplayName} — {descriptor.Title}";
            _initialsFallback = Initials(descriptor.IconText, descriptor.Title);
            _monogramHue = MonogramHue(owner.Id);
            Text = _fileIcon is null && _iconSurfaceId == 0 ? _initialsFallback : string.Empty;
            Anchors = AnchorEdges.None;
        }

        internal string OwnerId { get; }

        internal string WindowTitle { get; }

        internal string OwnerName { get; }

        internal bool IsOpen => _handle.IsVisible;

        internal RetailWindowHandle Window => _handle;

        internal UiControlState State => ThemeState;

        public override string? GetTooltipText() => _tooltip;

        protected override void OnDraw(UiRenderContext ctx)
        {
            PluginUiPalette p = _dock.Palette;
            DockSlotWell(ctx, p, 0f, 0f, Width, Height, ThemeState);
            Vector4 colour = ThemeState is UiControlState.Hovered or UiControlState.Pressed ? p.Text
                : IsOpen ? p.Accent : p.Muted;
            DrawIcon(ctx, (Width - DockArt) / 2f, (Height - DockArt) / 2f, DockArt, colour);
        }

        /// <summary>
        /// Draws this window's icon in the <paramref name="extent"/> box at (x, y): its
        /// file icon, else its DAT surface, else its monogram. Colour art keeps its own
        /// colours and only takes <paramref name="colour"/>'s strength (dimmer while
        /// closed); one-colour art is drawn in <paramref name="colour"/>.
        /// </summary>
        internal void DrawIcon(UiRenderContext ctx, float x, float y, float extent, Vector4 colour)
        {
            if (_fileIcon is null && !_iconResolveAttempted && _iconSurfaceId != 0)
            {
                _iconResolveAttempted = true;
                (uint tex, int w, int h) = _resolve(_iconSurfaceId);
                _iconAvailable = tex != 0 && w > 0 && h > 0;
                if (!_iconAvailable)
                    Text = _initialsFallback;
            }

            float alpha = IsOpen || ThemeState != UiControlState.Normal ? 1f : ClosedArtAlpha;
            uint texture = 0;
            int width = 0, height = 0;
            if (_fileIcon is { } file)
                (texture, width, height) = file;
            else if (_iconSurfaceId != 0 && _iconAvailable)
                (texture, width, height) = _resolve(_iconSurfaceId);

            if (texture != 0 && width > 0 && height > 0)
            {
                ctx.DrawSprite(texture, x, y, extent, extent, 0f, 0f, 1f, 1f, Vector4.One with { W = alpha });
                return;
            }
            Monogram(ctx, _dock.TitleFont, Text, _monogramHue, x, y, extent, alpha);
        }

        private static string Initials(string? requested, string title)
        {
            if (!string.IsNullOrWhiteSpace(requested))
                return requested.Trim()[..Math.Min(2, requested.Trim().Length)];

            string[] words = title.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (words.Length == 0)
                return "?";
            if (words.Length == 1)
                return words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant();
            return string.Concat(words.Take(2).Select(static word =>
                char.ToUpperInvariant(word[0])));
        }
    }

    private sealed class PluginMinimizeButton : UiSimpleButton
    {
        private readonly RetailWindowHandle _handle;
        private readonly PluginUiThemeSettings? _themes;
        private readonly UiDatFont? _classicFont;

        internal PluginMinimizeButton(RetailWindowHandle handle, UiDatFont? font, PluginUiThemeSettings? themes)
        {
            _handle = handle;
            _themes = themes;
            _classicFont = font;
            Text = "–";
            DatFont = font;
            Outline = true;
            BackgroundColor = new Vector4(0.02f, 0.02f, 0.015f, 0.94f);
            BorderColor = new Vector4(0.58f, 0.46f, 0.17f, 1f);
            BorderThickness = 1f;
            Click += () => _handle.Hide();
        }

        public override string? GetTooltipText() => "Minimize to plugin sidepanel";

        /// <summary>Only windows that opted into the shared theme get the ghost button in their header.</summary>
        protected override void OnTick(double deltaSeconds)
        {
            base.OnTick(deltaSeconds);
            ThemePalette = _handle.OuterFrame is UiPluginMarkupPanel ? _themes?.Palette : null;
            Outline = ThemePalette is null;
            TextColor = ThemePalette?.Muted ?? Vector4.One;
            DatFont = ThemePalette is null ? _classicFont : _themes?.ModernFont ?? _classicFont;
        }

        private protected override void DrawThemedFace(UiRenderContext ctx, PluginUiPalette palette) =>
            PluginUiStyle.GhostButton(ctx, palette, Width, Height, ThemeState);
    }
}
```

- [ ] **Step 7: Run the tests and see them pass**

```bash
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "(FullyQualifiedName~PluginSidePanel|FullyQualifiedName~PluginDockChrome)&Lane!=InstalledDat"
ACDREAM_DAT_DIR=$HOME/AsheronsCall dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~PluginShelf|FullyQualifiedName~RetailMarkupIconResolverInstalledDat"
```

Expected: `Passed: 46` (29 + 9 + 8); then `Passed: 3` against the installed DATs.

- [ ] **Step 8: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/PluginSidePanel.cs tests/AcDream.App.Tests/UI/PluginSidePanelTests.cs tests/AcDream.App.Tests/UI/PluginSidePanelThemeTests.cs tests/AcDream.App.Tests/UI/PluginDockChromeTests.cs
git commit -m "ui: one floating plugin dock in every theme

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```


### Task 6: Edge rail, snap and edge pills

**Goal:** The dock follows `PluginUiThemeSettings.Dock`: Left/Right rails flush to the edge, the menu-driven moves, snapping in and out while dragging (saved at drag end), the right rail re-attaching on resize, and eased edge pills with the tall pill on the front window.

**Files:**
- Modify: `src/AcDream.App/UI/PluginSidePanel.cs`
- Test: `tests/AcDream.App.Tests/UI/PluginDockModeTests.cs`

**Acceptance Criteria:**
- [ ] Setting `Dock` moves the dock: Left → x 0, Right → parent width − 46, Floating → 10pt in from the edge it was on; the top is kept.
- [ ] Dragging a floating dock within 8pt of an edge makes it a rail at once; `settings.Dock` and the store change only when the drag ends.
- [ ] A rail follows the pointer up and down its edge and floats again once the pointer is more than 32pt from the edge.
- [ ] A right rail (and its collapsed 12×44 tab) stays on the edge after the parent resizes.
- [ ] Edge pills ease at 20pt per 120ms toward 0 / 4 (hovered closed) / 8 (open) / 20 (front: highest ZOrder visible dock window).
- [ ] A saved rail restores to its edge with its saved top.
- [ ] Rails draw edge pills instead of open dots; toggle chevrons point toward the edge to collapse and away to expand.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "(FullyQualifiedName~PluginDock|FullyQualifiedName~PluginSidePanel|FullyQualifiedName~UiRootWindowDragHook|FullyQualifiedName~RetailWindowLayoutPersistence)&Lane!=InstalledDat"` → 110 passed

**Steps:**

- [ ] **Step 1: Write the failing tests**

`tests/AcDream.App.Tests/UI/PluginDockModeTests.cs`:

```csharp
using AcDream.App.UI;
using AcDream.Plugin.Abstractions;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.Tests.UI;

public sealed class PluginDockModeTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "acdream-dock-mode-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private SettingsStore Store() => new(Path.Combine(_directory, "settings.json"));

    private static (UiRoot Root, PluginSidePanel Dock, List<RetailWindowHandle> Windows) Mount(
        PluginUiThemeSettings settings, int count = 2)
    {
        var root = new UiRoot { Width = 800f, Height = 600f };
        var dock = new PluginSidePanel(root.WindowManager, _ => (0u, 0, 0), font: null, settings);
        root.AddChild(dock);
        root.WindowManager.Register(WindowNames.PluginShelf, dock, dock, controller: dock);
        var windows = new List<RetailWindowHandle>();
        for (int i = 0; i < count; i++)
        {
            var frame = new UiPanel { Left = 300f, Top = 50f + i * 120f, Width = 200f, Height = 100f };
            root.AddChild(frame);
            RetailWindowHandle handle = root.WindowManager.Register($"plugin:test:{i}", frame);
            dock.Add(new PluginUiOwner("test", "Test"), new PluginPanelDescriptor($"w{i}", $"Window {i}"), handle);
            windows.Add(handle);
        }
        root.Tick(0.016d, 16L);
        return (root, dock, windows);
    }

    private static void Drag(UiRoot root, PluginSidePanel dock, int dx, int dy, bool release = true)
    {
        int x = (int)dock.Left + 10, y = (int)dock.Top + 5;   // on the handle row
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseMove(x + dx, y + dy);
        if (release) root.OnMouseUp(UiMouseButton.Left, x + dx, y + dy);
    }

    [Fact]
    public void PickingAModeInTheSettings_MovesTheDockAndKeepsItsTop()
    {
        var settings = new PluginUiThemeSettings();
        var (root, dock, _) = Mount(settings);
        float top = dock.Top;

        settings.Dock = PluginDockMode.Left;
        root.Tick(0.016d, 32L);
        Assert.Equal((0f, top, PluginUiStyle.RailWidth), (dock.Left, dock.Top, dock.Width));

        settings.Dock = PluginDockMode.Right;
        root.Tick(0.016d, 48L);
        Assert.Equal((800f - PluginUiStyle.RailWidth, top), (dock.Left, dock.Top));

        settings.Dock = PluginDockMode.Floating;
        root.Tick(0.016d, 64L);
        Assert.Equal((800f - PluginUiStyle.DockWidth - 10f, top, PluginUiStyle.DockWidth), (dock.Left, dock.Top, dock.Width));

        settings.Dock = PluginDockMode.Left;
        root.Tick(0.016d, 80L);
        settings.Dock = PluginDockMode.Floating;
        root.Tick(0.016d, 96L);
        Assert.Equal(10f, dock.Left);
    }

    [Fact]
    public void DraggingNearTheLeftEdge_SnapsAtOnce_AndSavesTheModeOnlyWhenTheDragEnds()
    {
        var store = Store();
        var settings = new PluginUiThemeSettings(store);
        var (root, dock, _) = Mount(settings);

        Drag(root, dock, dx: -4, dy: 30, release: false);
        Assert.Equal(PluginDockMode.Left, dock.Mode);
        Assert.Equal(0f, dock.Left);
        Assert.Equal(PluginUiStyle.RailWidth, dock.Width);
        Assert.Equal(PluginDockMode.Floating, settings.Dock);
        Assert.Equal("floating", store.LoadPluginUi().Dock);

        root.OnMouseUp(UiMouseButton.Left, (int)dock.Left + 6, (int)dock.Top + 5);
        Assert.Equal(PluginDockMode.Left, settings.Dock);
        Assert.Equal("left", store.LoadPluginUi().Dock);
        Assert.Equal(146f, dock.Top);
    }

    [Fact]
    public void DraggingNearTheRightEdge_SnapsToTheRight()
    {
        var settings = new PluginUiThemeSettings();
        var (root, dock, _) = Mount(settings);

        Drag(root, dock, dx: 800 - 48 - 10 - 4, dy: 0);

        Assert.Equal(PluginDockMode.Right, settings.Dock);
        Assert.Equal(800f - PluginUiStyle.RailWidth, dock.Left);
    }

    [Fact]
    public void ARail_SlidesAlongItsEdge_AndFloatsWhenPulledMoreThan32Away()
    {
        var settings = new PluginUiThemeSettings { Dock = PluginDockMode.Left };
        var (root, dock, _) = Mount(settings);
        Assert.Equal(0f, dock.Left);

        Drag(root, dock, dx: 20, dy: 60);   // pointer ends at x = 30
        Assert.Equal(PluginDockMode.Left, settings.Dock);
        Assert.Equal((0f, 176f), (dock.Left, dock.Top));

        Drag(root, dock, dx: 25, dy: 0);    // pointer ends at x = 35
        Assert.Equal(PluginDockMode.Floating, settings.Dock);
        Assert.Equal(PluginUiStyle.DockWidth, dock.Width);
        Assert.Equal(25f, dock.Left);
    }

    [Fact]
    public void ARightRail_StaysOnTheEdgeWhenTheScreenResizes()
    {
        var settings = new PluginUiThemeSettings { Dock = PluginDockMode.Right };
        var (root, dock, _) = Mount(settings);
        Assert.Equal(800f - 46f, dock.Left);

        root.Width = 1000f;
        root.Tick(0.016d, 32L);
        Assert.Equal(1000f - 46f, dock.Left);

        dock.RestoreWindowState(new RetainedWindowState(Collapsed: true));
        root.Tick(0.016d, 48L);
        Assert.Equal((1000f - PluginUiStyle.RailTabWidth, PluginUiStyle.RailTabHeight), (dock.Left, dock.Height));
    }

    [Fact]
    public void EdgePills_EaseToTheirHeights_AndTheFrontWindowGetsTheTallOne()
    {
        var settings = new PluginUiThemeSettings { Dock = PluginDockMode.Left };
        var (root, dock, windows) = Mount(settings, count: 3);
        windows[0].Show();
        windows[1].Show();
        windows[2].Hide();
        root.WindowManager.BringToFront(windows[0].OuterFrame);
        var buttons = dock.Children.OfType<PluginSidePanel.PluginShelfButton>().ToArray();

        root.Tick(0.06d, 100L);
        Assert.InRange(buttons[0].PillHeight, 1f, 19f);   // still easing: 20pt takes 120ms

        for (int i = 0; i < 10; i++) root.Tick(0.016d, 200L + i);
        Assert.Same(windows[0], dock.FrontWindow);
        Assert.Equal(PluginUiStyle.RailPillFront, buttons[0].PillHeight);
        Assert.Equal(PluginUiStyle.RailPillOpen, buttons[1].PillHeight);
        Assert.Equal(0f, buttons[2].PillHeight);

        root.WindowManager.BringToFront(windows[1].OuterFrame);
        for (int i = 0; i < 10; i++) root.Tick(0.016d, 400L + i);
        Assert.Equal(PluginUiStyle.RailPillOpen, buttons[0].PillHeight);
        Assert.Equal(PluginUiStyle.RailPillFront, buttons[1].PillHeight);
    }

    [Fact]
    public void ASavedRail_RestoresToItsEdgeWithItsTop()
    {
        var store = Store();
        var settings = new PluginUiThemeSettings(store) { Dock = PluginDockMode.Right };
        var (root, dock, _) = Mount(settings);
        using (var persistence = new RetailWindowLayoutPersistence(
                   root.WindowManager, store, () => "Alice", () => (800, 600)))
        {
            root.WindowManager.MoveTo(WindowNames.PluginShelf, 300f, 222f);
        }

        var settings2 = new PluginUiThemeSettings(store);
        var (root2, dock2, _) = Mount(settings2);
        using var persistence2 = new RetailWindowLayoutPersistence(
            root2.WindowManager, store, () => "Alice", () => (800, 600));
        persistence2.RestoreAll();
        root2.Tick(0.016d, 32L);

        Assert.Equal(PluginDockMode.Right, dock2.Mode);
        Assert.Equal((800f - 46f, 222f), (dock2.Left, dock2.Top));
    }
}
```

- [ ] **Step 2: Run them and see them fail**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/dock && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter FullyQualifiedName~PluginDockModeTests
```

Expected: build errors such as `CS1061: 'PluginSidePanel' does not contain a definition for 'Mode'`.

- [ ] **Step 3: Add the modes**

Save the diff below as `/tmp/dock-PluginSidePanel.cs.patch` and run `git apply /tmp/dock-PluginSidePanel.cs.patch` from the worktree root (or make the same edits by hand).

````diff
diff --git a/src/AcDream.App/UI/PluginSidePanel.cs b/src/AcDream.App/UI/PluginSidePanel.cs
index f300452e..2105e058 100644
--- a/src/AcDream.App/UI/PluginSidePanel.cs
+++ b/src/AcDream.App/UI/PluginSidePanel.cs
@@ -16,6 +16,8 @@ public sealed class PluginSidePanel : UiPanel, IDisposable, IRetainedWindowState
     private const float DefaultTop = 116f;
     private const float DefaultLeft = 10f;
     private const float BottomMargin = 8f;
+    private const float SnapIn = 8f;
+    private const float SnapOut = 32f;
 
     private readonly RetailWindowManager _windows;
     private readonly Func<uint, (uint tex, int width, int height)> _resolve;
@@ -28,6 +30,8 @@ public sealed class PluginSidePanel : UiPanel, IDisposable, IRetainedWindowState
     private readonly PluginUiThemeSettings _themes;
     private readonly Action? _appearanceRequested;
     private PluginUiTheme _lastTheme;
+    private PluginDockMode _appliedMode;
+    private PluginDockMode? _dragMode;
     private PluginDockLayout _layout;
     private int _firstRow;
     private bool _disposed;
@@ -59,6 +63,7 @@ public sealed class PluginSidePanel : UiPanel, IDisposable, IRetainedWindowState
         _themes = themes ?? new PluginUiThemeSettings();
         _appearanceRequested = appearanceRequested;
         _lastTheme = _themes.Theme;
+        _appliedMode = _themes.Dock;
 
         Top = DefaultTop;
         Anchors = AnchorEdges.None;
@@ -105,6 +110,12 @@ public sealed class PluginSidePanel : UiPanel, IDisposable, IRetainedWindowState
 
     internal bool Collapsed => _collapsed;
 
+    /// <summary>
+    /// The mode the dock is drawn in: the saved one, or the one a drag has
+    /// snapped it into, which is saved when the drag ends.
+    /// </summary>
+    internal PluginDockMode Mode => _dragMode ?? _themes.Dock;
+
     /// <summary>
     /// Adds one manifest-scoped plugin window and its minimize affordance.
     /// Duplicate handles are idempotent.
@@ -175,15 +186,25 @@ public sealed class PluginSidePanel : UiPanel, IDisposable, IRetainedWindowState
     protected override void OnDraw(UiRenderContext ctx)
     {
         PluginUiPalette p = Palette;
-        DockBody(ctx, p, PluginDockMode.Floating, Width, Height);
+        PluginDockMode mode = Mode;
+        DockBody(ctx, p, mode, Width, Height);
         foreach (float y in _layout.Dividers)
             DockDivider(ctx, p, Width, y);
         if (_collapsed) return;
         DockSide side = ScreenSide;
         foreach (ShelfEntry entry in _entries.Values)
         {
-            if (entry.Button.Visible && entry.Button.IsOpen)
-                OpenDot(ctx, p, side, Width, entry.Button.Top, entry.Button.Height);
+            PluginShelfButton button = entry.Button;
+            if (!button.Visible) continue;
+            if (mode == PluginDockMode.Floating)
+            {
+                if (button.IsOpen)
+                    OpenDot(ctx, p, side, Width, button.Top, button.Height);
+            }
+            else
+            {
+                EdgePill(ctx, button.IsOpen ? p.Accent : p.Muted, side, Width, button.Top, button.Height, button.PillHeight);
+            }
         }
     }
 
@@ -194,9 +215,44 @@ public sealed class PluginSidePanel : UiPanel, IDisposable, IRetainedWindowState
         if (_layout.FadeBottom) DockFade(ctx, Palette, _layout.ListBottom - DockFadeHeight, Width, darkAtBottom: true);
     }
 
-    /// <summary>The side of the screen the dock is nearer: where its open dots go.</summary>
-    internal DockSide ScreenSide =>
-        Parent is { } parent && Left + Width / 2f > parent.Width / 2f ? DockSide.Right : DockSide.Left;
+    /// <summary>The screen edge the dock faces: a rail's own edge, or the nearer one for a floating dock.</summary>
+    internal DockSide ScreenSide => Mode switch
+    {
+        PluginDockMode.Left => DockSide.Left,
+        PluginDockMode.Right => DockSide.Right,
+        _ => Parent is { } parent && Left + Width / 2f > parent.Width / 2f ? DockSide.Right : DockSide.Left,
+    };
+
+    /// <summary>
+    /// While the dock is dragged: a floating dock that comes within 8pt of a
+    /// screen edge becomes a rail on that edge, and a rail follows the pointer
+    /// up and down its edge until the pointer is more than 32pt away, when it
+    /// floats again. The new mode is saved when the drag ends.
+    /// </summary>
+    internal override void ConstrainWindowDrag(ref float left, ref float top, int pointerX, int pointerY)
+    {
+        if (Parent is not { } parent) return;
+        PluginDockMode mode = Mode;
+        PluginDockMode next = mode switch
+        {
+            PluginDockMode.Floating when left <= SnapIn => PluginDockMode.Left,
+            PluginDockMode.Floating when left + Width >= parent.Width - SnapIn => PluginDockMode.Right,
+            PluginDockMode.Left when pointerX > SnapOut => PluginDockMode.Floating,
+            PluginDockMode.Right when pointerX < parent.Width - SnapOut => PluginDockMode.Floating,
+            _ => mode,
+        };
+        if (next != mode)
+        {
+            _dragMode = next;
+            _appliedMode = next;
+            Reflow();
+        }
+        if (next == PluginDockMode.Left)
+            left = 0f;
+        else if (next == PluginDockMode.Right)
+            left = parent.Width - Width;
+        top = Math.Clamp(top, 0f, MathF.Max(0f, parent.Height - Height));
+    }
 
     protected override void OnTick(double deltaSeconds)
     {
@@ -207,6 +263,8 @@ public sealed class PluginSidePanel : UiPanel, IDisposable, IRetainedWindowState
             _lastTheme = _themes.Theme;
             Reflow();
         }
+        if (_appliedMode != Mode)
+            ApplyModeFromSetting(_appliedMode, Mode);
         _grip.Opacity = _windows.IsLocked ? 0.5f : 1f;
         PointerOver = Parent is UiRoot root
             && root.MouseX >= Left && root.MouseX < Left + Width
@@ -235,10 +293,69 @@ public sealed class PluginSidePanel : UiPanel, IDisposable, IRetainedWindowState
             }
         }
 
+        if (Parent is { } edgeParent && Mode != PluginDockMode.Floating)
+            Left = Mode == PluginDockMode.Left ? 0f : edgeParent.Width - Width;
+
+        UpdateEdgePills((float)deltaSeconds);
+
         foreach (RetailWindowHandle handle in _entries.Keys)
             KeepWindowReachable(handle);
     }
 
+    /// <summary>
+    /// The player picked a mode in plugin appearance: a rail moves to its
+    /// edge, and a floating dock moves 10pt in from the edge it was on. Both
+    /// keep their top.
+    /// </summary>
+    private void ApplyModeFromSetting(PluginDockMode from, PluginDockMode to)
+    {
+        _appliedMode = to;
+        Reflow();
+        if (Parent is not { } parent) return;
+        float left = to switch
+        {
+            PluginDockMode.Left => 0f,
+            PluginDockMode.Right => parent.Width - Width,
+            _ when from == PluginDockMode.Right => MathF.Max(0f, parent.Width - Width - DefaultLeft),
+            _ => DefaultLeft,
+        };
+        if (_ownHandle is { } handle)
+            handle.MoveTo(left, Top);
+        else
+            Left = left;
+    }
+
+    /// <summary>The plugin window drawn over the dock's other open windows, which gets the tall edge pill.</summary>
+    internal RetailWindowHandle? FrontWindow
+    {
+        get
+        {
+            RetailWindowHandle? front = null;
+            foreach (RetailWindowHandle handle in _order)
+            {
+                if (handle.IsVisible && (front is null || handle.OuterFrame.ZOrder >= front.OuterFrame.ZOrder))
+                    front = handle;
+            }
+            return front;
+        }
+    }
+
+    private void UpdateEdgePills(float dt)
+    {
+        RetailWindowHandle? front = FrontWindow;
+        float step = RailPillFront / RailPillSeconds * MathF.Max(0f, dt);
+        foreach (RetailWindowHandle handle in _order)
+        {
+            PluginShelfButton button = _entries[handle].Button;
+            float target = ReferenceEquals(handle, front) ? RailPillFront
+                : button.IsOpen ? RailPillOpen
+                : button.State is UiControlState.Hovered or UiControlState.Pressed ? RailPillHover
+                : 0f;
+            float delta = target - button.PillHeight;
+            button.PillHeight += Math.Clamp(delta, -step, step);
+        }
+    }
+
     /// <inheritdoc />
     public override bool OnEvent(in UiEvent e)
     {
@@ -273,6 +390,13 @@ public sealed class PluginSidePanel : UiPanel, IDisposable, IRetainedWindowState
 
     private void OnHandleMoved(RetailWindowHandle _)
     {
+        if (_dragMode is { } snapped)
+        {
+            _dragMode = null;
+            _themes.Dock = snapped;
+        }
+        if (Mode != PluginDockMode.Floating)
+            return;
         if (!_homeApplied)
         {
             _userPositioned = true;
@@ -359,7 +483,7 @@ public sealed class PluginSidePanel : UiPanel, IDisposable, IRetainedWindowState
         float effectiveHeight = maximumHeight
             ?? (_lastLayoutHeight >= 0f ? _lastLayoutHeight : float.PositiveInfinity);
         string[] owners = _order.Select(h => _entries[h].Button.OwnerId).ToArray();
-        _layout = PluginDockLayout.Compute(PluginDockMode.Floating, _collapsed, owners, effectiveHeight, _firstRow);
+        _layout = PluginDockLayout.Compute(Mode, _collapsed, owners, effectiveHeight, _firstRow);
         _firstRow = _layout.FirstRow;
 
         for (int i = 0; i < _order.Count; i++)
@@ -434,7 +558,8 @@ public sealed class PluginSidePanel : UiPanel, IDisposable, IRetainedWindowState
         protected override void OnDraw(UiRenderContext ctx)
         {
             if (!dock.PointerOver && !dock.Collapsed) return;
-            HandleDots(ctx, dock.Palette, Width / 2f, dock.Collapsed ? Height / 2f : DockHandleTop + DockHandleHeight / 2f);
+            HandleDots(ctx, dock.Palette, Width / 2f,
+                dock.Collapsed && dock.Mode == PluginDockMode.Floating ? Height / 2f : DockHandleTop + DockHandleHeight / 2f);
         }
     }
 
@@ -448,8 +573,14 @@ public sealed class PluginSidePanel : UiPanel, IDisposable, IRetainedWindowState
             if (!dock.PointerOver && !dock.Collapsed) return;
             PluginUiPalette p = dock.Palette;
             GhostButton(ctx, p, Width, Height, ThemeState);
-            Chevron(ctx, Width / 2f, Height / 2f, 6f,
-                dock.Collapsed ? ChevronDirection.Down : ChevronDirection.Up,
+            ChevronDirection direction = (dock.Mode, dock.Collapsed) switch
+            {
+                (PluginDockMode.Left, false) or (PluginDockMode.Right, true) => ChevronDirection.Left,
+                (PluginDockMode.Left, true) or (PluginDockMode.Right, false) => ChevronDirection.Right,
+                (_, true) => ChevronDirection.Down,
+                _ => ChevronDirection.Up,
+            };
+            Chevron(ctx, Width / 2f, Height / 2f, 6f, direction,
                 ThemeState == UiControlState.Normal ? p.Muted : p.Text);
         }
     }
@@ -520,6 +651,9 @@ public sealed class PluginSidePanel : UiPanel, IDisposable, IRetainedWindowState
 
         internal UiControlState State => ThemeState;
 
+        /// <summary>How tall this slot's edge pill is now; it eases toward its state's height on a rail.</summary>
+        internal float PillHeight { get; set; }
+
         public override string? GetTooltipText() => _tooltip;
 
         protected override void OnDraw(UiRenderContext ctx)
````

- [ ] **Step 4: Run the tests and see them pass**

```bash
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "(FullyQualifiedName~PluginDock|FullyQualifiedName~PluginSidePanel|FullyQualifiedName~UiRootWindowDragHook|FullyQualifiedName~RetailWindowLayoutPersistence)&Lane!=InstalledDat"
```

Expected: `Passed: 110`.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/PluginSidePanel.cs tests/AcDream.App.Tests/UI/PluginDockModeTests.cs
git commit -m "ui: lock the plugin dock to a screen edge as a rail

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```


### Task 7: Hover label

**Goal:** Hovering a slot or the gear shows a label beside it at once — the window title, with the plugin name under it unless equal ("Plugin appearance" for the gear) — drawn in the dock's overlay pass on the side away from the screen edge; slots and the gear no longer return tooltip text.

**Files:**
- Modify: `src/AcDream.App/UI/PluginSidePanel.cs`
- Test: `tests/AcDream.App.Tests/UI/PluginDockLabelTests.cs`

**Acceptance Criteria:**
- [ ] `HoveredSlot` is the hovered visible slot or gear, null when collapsed or nothing is hovered.
- [ ] `LabelText` is (title, owner) or (title, null) when equal; the gear's is ("Plugin appearance", null).
- [ ] `LabelRect` is 8pt right of the dock when it faces the left edge, 8pt left of it when it faces the right edge (right rail, or floating in the right half), vertically centred on the slot.
- [ ] The label is not hit-testable (`UiRoot.Pick` over it returns null) and is drawn outside the dock's clip, in the overlay pass.
- [ ] `PluginShelfButton.GetTooltipText()` and the gear's return null.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "(FullyQualifiedName~PluginDock|FullyQualifiedName~PluginSidePanel|FullyQualifiedName~UiRootWindowDragHook|FullyQualifiedName~RetailWindowLayoutPersistence|FullyQualifiedName~PluginTheme|FullyQualifiedName~PluginUi)&Lane!=InstalledDat"` → 144 passed

**Steps:**

- [ ] **Step 1: Write the failing tests**

`tests/AcDream.App.Tests/UI/PluginDockLabelTests.cs`:

```csharp
using AcDream.App.UI;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.UI;

public sealed class PluginDockLabelTests
{
    private static (UiRoot Root, PluginSidePanel Dock) Mount(PluginUiThemeSettings settings, string title, string owner)
    {
        var root = new UiRoot { Width = 800f, Height = 600f };
        var dock = new PluginSidePanel(root.WindowManager, _ => (0u, 0, 0), font: null, settings);
        root.AddChild(dock);
        root.WindowManager.Register(WindowNames.PluginShelf, dock, dock, controller: dock);
        var frame = new UiPanel { Left = 300f, Width = 200f, Height = 100f };
        root.AddChild(frame);
        RetailWindowHandle handle = root.WindowManager.Register("plugin:test:main", frame);
        dock.Add(new PluginUiOwner("test", owner), new PluginPanelDescriptor("main", title), handle);
        root.Tick(0.016d, 16L);
        return (root, dock);
    }

    private static PluginSidePanel.PluginShelfButton Slot(PluginSidePanel dock) =>
        Assert.Single(dock.Children.OfType<PluginSidePanel.PluginShelfButton>());

    private static void HoverSlot(UiRoot root, PluginSidePanel dock)
    {
        var slot = Slot(dock);
        root.OnMouseMove((int)(dock.Left + slot.Left + 10), (int)(dock.Top + slot.Top + 10));
        root.Tick(0.016d, 32L);
    }

    [Fact]
    public void HoveringASlot_ShowsItsLabel_AndTheSlotHasNoTooltip()
    {
        var (root, dock) = Mount(new PluginUiThemeSettings(), "Loot Editor", "MossTank");
        Assert.Null(dock.HoveredSlot);

        HoverSlot(root, dock);

        Assert.Same(Slot(dock), dock.HoveredSlot);
        Assert.Equal(("Loot Editor", "MossTank"), PluginSidePanel.LabelText(Slot(dock)));
        Assert.Null(Slot(dock).GetTooltipText());
    }

    [Fact]
    public void ALabelIsOneLine_WhenTheTitleIsThePluginsName()
    {
        var (_, dock) = Mount(new PluginUiThemeSettings(), "GoArrow", "GoArrow");
        Assert.Equal(("GoArrow", (string?)null), PluginSidePanel.LabelText(Slot(dock)));
    }

    [Fact]
    public void TheLabelSitsAwayFromTheScreenEdge()
    {
        var settings = new PluginUiThemeSettings();
        var (root, dock) = Mount(settings, "Loot Editor", "MossTank");
        DockRect floatingLeft = dock.LabelRect(Slot(dock));
        Assert.Equal(dock.Width + PluginUiStyle.LabelGap, floatingLeft.X);
        Assert.Equal(Slot(dock).Top + Slot(dock).Height / 2f, floatingLeft.Y + floatingLeft.H / 2f, 0);

        settings.Dock = PluginDockMode.Right;
        root.Tick(0.016d, 32L);
        DockRect right = dock.LabelRect(Slot(dock));
        Assert.Equal(-PluginUiStyle.LabelGap - right.W, right.X);

        settings.Dock = PluginDockMode.Floating;
        root.Tick(0.016d, 48L);
        Assert.True(dock.Left > 400f);   // floating in the right half of the screen
        Assert.True(dock.LabelRect(Slot(dock)).X < 0f);
    }

    [Fact]
    public void TheLabelIsNotHitTestable_AndIsDrawnOutsideTheDock()
    {
        var (root, dock) = Mount(new PluginUiThemeSettings { Theme = PluginUiTheme.Moss }, "Loot Editor", "MossTank");
        HoverSlot(root, dock);
        DockRect label = dock.LabelRect(Slot(dock));
        int x = (int)(dock.Left + label.X + label.W / 2f), y = (int)(dock.Top + label.Y + label.H / 2f);

        Assert.Null(root.Pick(x, y));
        // The label is drawn in the overlay pass; draw that pass alone so its vertices are recorded.
        var (renderer, ctx) = ThemeDrawCapture.Context(root.Width, root.Height);
        dock.DrawOverlays(ctx);
        var vertices = ThemeDrawCapture.Vertices(renderer);
        Assert.Contains(vertices, v => v.Position.X > dock.Left + dock.Width + PluginUiStyle.LabelGap
            && ThemeDrawCapture.HasColor([v], PluginUiPalette.Moss.Background));
    }

    [Fact]
    public void HoveringTheGear_LabelsItPluginAppearance()
    {
        var (root, dock) = Mount(new PluginUiThemeSettings(), "Loot Editor", "MossTank");
        DockRect gear = dock.Layout.Gear;
        root.OnMouseMove((int)(dock.Left + gear.X + 10), (int)(dock.Top + gear.Y + 10));
        root.Tick(0.016d, 32L);

        Assert.NotNull(dock.HoveredSlot);
        Assert.Equal(("Plugin appearance", (string?)null), PluginSidePanel.LabelText(dock.HoveredSlot!));
    }
}
```

- [ ] **Step 2: Run them and see them fail**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/dock && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter FullyQualifiedName~PluginDockLabelTests
```

Expected: build errors such as `CS1061: 'PluginSidePanel' does not contain a definition for 'HoveredSlot'`.

- [ ] **Step 3: Add the label**

Save the diff below as `/tmp/dock-PluginSidePanel.cs.patch` and run `git apply /tmp/dock-PluginSidePanel.cs.patch` from the worktree root (or make the same edits by hand).

````diff
diff --git a/src/AcDream.App/UI/PluginSidePanel.cs b/src/AcDream.App/UI/PluginSidePanel.cs
index 2105e058..3df487b6 100644
--- a/src/AcDream.App/UI/PluginSidePanel.cs
+++ b/src/AcDream.App/UI/PluginSidePanel.cs
@@ -208,6 +208,68 @@ public sealed class PluginSidePanel : UiPanel, IDisposable, IRetainedWindowState
         }
     }
 
+    /// <summary>The hover label is drawn in the overlay pass, outside the dock's own clip.</summary>
+    protected override bool ExpandsClipForPopup => true;
+
+    /// <summary>The slot (or gear) the pointer is over, whose label shows; null when none is.</summary>
+    internal UiSimpleButton? HoveredSlot
+    {
+        get
+        {
+            if (_collapsed || !Visible) return null;
+            if (_gear.Visible && _gear.State is UiControlState.Hovered or UiControlState.Pressed)
+                return _gear;
+            foreach (RetailWindowHandle handle in _order)
+            {
+                PluginShelfButton button = _entries[handle].Button;
+                if (button.Visible && button.State is UiControlState.Hovered or UiControlState.Pressed)
+                    return button;
+            }
+            return null;
+        }
+    }
+
+    /// <summary>A slot's label: the window title, and the plugin's name under it unless they are the same.</summary>
+    internal static (string Title, string? Subtitle) LabelText(UiSimpleButton slot) => slot switch
+    {
+        PluginShelfButton button => (button.WindowTitle,
+            string.Equals(button.WindowTitle, button.OwnerName, StringComparison.Ordinal) ? null : button.OwnerName),
+        _ => ("Plugin appearance", null),
+    };
+
+    /// <summary>Where a slot's label goes, in the dock's space: beside the slot, on the side away from the screen edge.</summary>
+    internal DockRect LabelRect(UiSimpleButton slot)
+    {
+        (string title, string? subtitle) = LabelText(slot);
+        UiDatFont? titleFont = TitleFont, bodyFont = BodyFont;
+        float titleHeight = titleFont?.LineHeight ?? 12f;
+        float bodyHeight = bodyFont?.LineHeight ?? 12f;
+        float textWidth = MathF.Max(titleFont?.MeasureWidth(title) ?? 0f,
+            subtitle is null ? 0f : bodyFont?.MeasureWidth(subtitle) ?? 0f);
+        float w = MathF.Ceiling(textWidth + 2f * LabelPadX);
+        float h = MathF.Ceiling(titleHeight + (subtitle is null ? 0f : bodyHeight + 1f) + 2f * LabelPadY);
+        float x = ScreenSide == DockSide.Left ? Width + LabelGap : -LabelGap - w;
+        float y = MathF.Round(slot.Top + (slot.Height - h) / 2f);
+        return new DockRect(x, y, w, h);
+    }
+
+    protected override void OnDrawOverlay(UiRenderContext ctx)
+    {
+        if (HoveredSlot is not { } slot) return;
+        PluginUiPalette p = Palette;
+        DockRect r = LabelRect(slot);
+        HoverLabel(ctx, p, r.X, r.Y, r.W, r.H);
+        (string title, string? subtitle) = LabelText(slot);
+        float y = r.Y + LabelPadY;
+        if (TitleFont is { } titleFont)
+        {
+            ctx.DrawStringDat(titleFont, title, r.X + LabelPadX, y, p.Text);
+            y += titleFont.LineHeight + 1f;
+        }
+        if (subtitle is not null && BodyFont is { } bodyFont)
+            ctx.DrawStringDat(bodyFont, subtitle, r.X + LabelPadX, y, p.Muted);
+    }
+
     protected override void OnDrawAfterChildren(UiRenderContext ctx)
     {
         if (_collapsed) return;
@@ -588,7 +650,10 @@ public sealed class PluginSidePanel : UiPanel, IDisposable, IRetainedWindowState
     /// <summary>The gear slot at the bottom of the dock, which opens plugin appearance.</summary>
     private sealed class DockGearButton(PluginSidePanel dock) : UiSimpleButton
     {
-        public override string? GetTooltipText() => "Plugin appearance";
+        internal UiControlState State => ThemeState;
+
+        /// <summary>The dock's hover label says "Plugin appearance", so the gear has no tooltip.</summary>
+        public override string? GetTooltipText() => null;
 
         protected override void OnDraw(UiRenderContext ctx)
         {
@@ -606,7 +671,6 @@ public sealed class PluginSidePanel : UiPanel, IDisposable, IRetainedWindowState
         private readonly Func<uint, (uint tex, int width, int height)> _resolve;
         private readonly (uint Texture, int Width, int Height)? _fileIcon;
         private readonly uint _iconSurfaceId;
-        private readonly string _tooltip;
         private readonly string _initialsFallback;
         private readonly Vector4 _monogramHue;
 
@@ -629,10 +693,6 @@ public sealed class PluginSidePanel : UiPanel, IDisposable, IRetainedWindowState
             WindowTitle = descriptor.Title;
             OwnerName = owner.DisplayName;
             _iconSurfaceId = PluginIcons.Normalize(descriptor.IconSurfaceId);
-            _tooltip = string.Equals(descriptor.Title, owner.DisplayName,
-                    StringComparison.Ordinal)
-                ? descriptor.Title
-                : $"{owner.DisplayName} — {descriptor.Title}";
             _initialsFallback = Initials(descriptor.IconText, descriptor.Title);
             _monogramHue = MonogramHue(owner.Id);
             Text = _fileIcon is null && _iconSurfaceId == 0 ? _initialsFallback : string.Empty;
@@ -654,7 +714,8 @@ public sealed class PluginSidePanel : UiPanel, IDisposable, IRetainedWindowState
         /// <summary>How tall this slot's edge pill is now; it eases toward its state's height on a rail.</summary>
         internal float PillHeight { get; set; }
 
-        public override string? GetTooltipText() => _tooltip;
+        /// <summary>The dock's hover label names the window, so the slot has no tooltip.</summary>
+        public override string? GetTooltipText() => null;
 
         protected override void OnDraw(UiRenderContext ctx)
         {
````

- [ ] **Step 4: Run the tests and see them pass**

```bash
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "(FullyQualifiedName~PluginDock|FullyQualifiedName~PluginSidePanel|FullyQualifiedName~UiRootWindowDragHook|FullyQualifiedName~RetailWindowLayoutPersistence|FullyQualifiedName~PluginTheme|FullyQualifiedName~PluginUi)&Lane!=InstalledDat"
```

Expected: `Passed: 144`.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/PluginSidePanel.cs tests/AcDream.App.Tests/UI/PluginDockLabelTests.cs
git commit -m "ui: name the hovered dock slot in a label beside it

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```


### Task 8: Dock menu, copy and docs

**Goal:** Plugin appearance gets a `Dock: Floating / Left edge / Right edge` menu bound to `PluginUiThemeSettings.Dock`; the minimize button says "Minimize to the dock"; `docs/plugin-ui-markup.md` calls it the dock and describes both modes.

**Files:**
- Modify: `src/AcDream.App/UI/PluginAppearanceBinding.cs`
- Modify: `src/AcDream.App/UI/RetailUiRuntime.cs` (the Plugin appearance markup in `ShowPluginAppearance`, about line 4171)
- Modify: `src/AcDream.App/UI/PluginSidePanel.cs` (`PluginMinimizeButton.GetTooltipText`)
- Modify: `docs/plugin-ui-markup.md`
- Test: `tests/AcDream.App.Tests/UI/PluginAppearanceBindingTests.cs`

**Acceptance Criteria:**
- [ ] `PluginAppearanceBinding.Docks` is ["Floating", "Left edge", "Right edge"]; `SelectedDock` follows `settings.Dock` (including snaps); `SelectDock` sets it and ignores unknown names.
- [ ] The appearance window is 196 tall with a Dock label at y 89, its menu at y 111 and Close at y 158.
- [ ] The minimize tooltip reads "Minimize to the dock".
- [ ] `docs/plugin-ui-markup.md` says dock (not shelf) for the player-facing parts and documents floating, both rails, the 8/32pt snap, the gear and scrolling.
- [ ] `src/AcDream.Plugin.Abstractions` is untouched.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~PluginAppearanceBindingTests|FullyQualifiedName~MarkupDocument"` → 34 passed; `git diff --stat main -- src/AcDream.Plugin.Abstractions` → empty

**Steps:**

- [ ] **Step 1: Write the failing test**

`tests/AcDream.App.Tests/UI/PluginAppearanceBindingTests.cs`:

```csharp
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public sealed class PluginAppearanceBindingTests
{
    [Fact]
    public void TheDockMenuShowsAndSetsTheDockMode()
    {
        var settings = new PluginUiThemeSettings();
        var binding = new PluginAppearanceBinding(settings);

        Assert.Equal(["Floating", "Left edge", "Right edge"], binding.Docks);
        Assert.Equal("Floating", binding.SelectedDock);

        binding.SelectDock("Right edge");
        Assert.Equal(PluginDockMode.Right, settings.Dock);
        Assert.Equal("Right edge", binding.SelectedDock);

        settings.Dock = PluginDockMode.Left;   // a snap while dragging
        Assert.Equal("Left edge", binding.SelectedDock);

        binding.SelectDock("Sideways");
        Assert.Equal(PluginDockMode.Left, settings.Dock);
    }

    [Fact]
    public void TheThemeMenuStillWorks()
    {
        var settings = new PluginUiThemeSettings();
        var binding = new PluginAppearanceBinding(settings);
        binding.Select("Warm graphite + brass");
        Assert.Equal(PluginUiTheme.Brass, settings.Theme);
        Assert.Equal("Warm graphite + brass", binding.Selected);
    }
}
```

- [ ] **Step 2: Run it and see it fail**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/dock && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter FullyQualifiedName~PluginAppearanceBindingTests
```

Expected: build error `CS1061: 'PluginAppearanceBinding' does not contain a definition for 'Docks'`.

- [ ] **Step 3: Add the dock list to the binding**

`src/AcDream.App/UI/PluginAppearanceBinding.cs`:

```csharp
namespace AcDream.App.UI;

public sealed class PluginAppearanceBinding(PluginUiThemeSettings settings)
{
    public IReadOnlyList<string> Themes { get; } = ["Classic", "Charcoal + moss", "Warm graphite + brass"];
    public string Selected => Themes[(int)settings.Theme];
    public Action<string> Select => name =>
    {
        for (int i = 0; i < Themes.Count; i++)
            if (Themes[i] == name) { settings.Theme = (PluginUiTheme)i; return; }
    };

    /// <summary>Where the plugin dock sits, in <see cref="PluginDockMode"/> order.</summary>
    public IReadOnlyList<string> Docks { get; } = ["Floating", "Left edge", "Right edge"];
    public string SelectedDock => Docks[(int)settings.Dock];
    public Action<string> SelectDock => name =>
    {
        for (int i = 0; i < Docks.Count; i++)
            if (Docks[i] == name) { settings.Dock = (PluginDockMode)i; return; }
    };

    public Action? Close { get; set; }
}
```

- [ ] **Step 4: Add the Dock menu to the appearance window**

````diff
diff --git a/src/AcDream.App/UI/RetailUiRuntime.cs b/src/AcDream.App/UI/RetailUiRuntime.cs
index 4f3f23f2..c3e7e4bb 100644
--- a/src/AcDream.App/UI/RetailUiRuntime.cs
+++ b/src/AcDream.App/UI/RetailUiRuntime.cs
@@ -4169,10 +4169,12 @@ public sealed class RetailUiRuntime : IDisposable
         }
         var binding = new PluginAppearanceBinding(_pluginThemes!);
         _pluginAppearance = MarkupDocument.Build("""
-            <panel x="70" y="90" w="310" h="140" title="Plugin appearance" theme="plugin">
+            <panel x="70" y="90" w="310" h="196" title="Plugin appearance" theme="plugin">
               <label x="12" y="33" text="Theme" />
               <menu x="12" y="55" w="286" h="24" items="{Themes}" selected="{Selected}" onchange="{Select}" rows="3" />
-              <button x="226" y="102" w="72" h="24" text="Close" onclick="{Close}" />
+              <label x="12" y="89" text="Dock" />
+              <menu x="12" y="111" w="286" h="24" items="{Docks}" selected="{SelectedDock}" onchange="{SelectDock}" rows="3" />
+              <button x="226" y="158" w="72" h="24" text="Close" onclick="{Close}" />
             </panel>
             """, binding, _bindings.Assets.ResolveSprite, _bindings.Assets.Controls,
             _bindings.Assets.DefaultFont, themes: _pluginThemes);
````

- [ ] **Step 5: Change the minimize tooltip**

````diff
diff --git a/src/AcDream.App/UI/PluginSidePanel.cs b/src/AcDream.App/UI/PluginSidePanel.cs
index 3df487b6..f559ca99 100644
--- a/src/AcDream.App/UI/PluginSidePanel.cs
+++ b/src/AcDream.App/UI/PluginSidePanel.cs
@@ -796,7 +796,7 @@ public sealed class PluginSidePanel : UiPanel, IDisposable, IRetainedWindowState
             Click += () => _handle.Hide();
         }
 
-        public override string? GetTooltipText() => "Minimize to plugin sidepanel";
+        public override string? GetTooltipText() => "Minimize to the dock";
 
         /// <summary>Only windows that opted into the shared theme get the ghost button in their header.</summary>
         protected override void OnTick(double deltaSeconds)
````

- [ ] **Step 6: Update the markup guide**

Save the diff below as `/tmp/dock-plugin-ui-markup.md.patch` and run `git apply /tmp/dock-plugin-ui-markup.md.patch` from the worktree root (or make the same edits by hand).

````diff
diff --git a/docs/plugin-ui-markup.md b/docs/plugin-ui-markup.md
index 18b3b39e..7af061a0 100644
--- a/docs/plugin-ui-markup.md
+++ b/docs/plugin-ui-markup.md
@@ -12,10 +12,10 @@ depend on `AcDream.App`.
 host.Ui.AddPanel(
     new PluginPanelDescriptor("main", "My Plugin")
     {
-        IconText = "MP",             // shelf button initials, the last resort
+        IconText = "MP",             // dock monogram letters, the last resort
         IconSurfaceId = 0x06002C41,  // an icon id (see "Icon ids")
         StartVisible = true,
-        ShowInSidePanel = true,      // default: a button in the plugin shelf
+        ShowInSidePanel = true,      // default: a slot in the plugin dock
     },
     Path.Combine(pluginDirectory, "main.xml"),
     binding);
@@ -27,9 +27,10 @@ host.Ui.AddPanel(
   removes the window on its own.
 - `RegisterPanelContent` takes the markup as a string instead of a file path.
 
-The plugin shelf picks a button's icon in order: the plugin's own `icon.png`
+The plugin dock picks a slot's icon in order: the plugin's own `icon.png`
 (one per plugin, at the root of its install folder), else `IconSurfaceId`,
-else the initials from `IconText`.
+else a two-letter monogram from `IconText` (or the title) on a colour picked
+from the plugin id.
 
 Every window gets drag, an optional resize, the global UI lock, and a
 persisted position keyed `plugin:{pluginId}:{windowId}`. Hiding a window never
@@ -37,7 +38,8 @@ pauses the plugin.
 
 ## Shared plugin appearance
 
-Right-click the plugin shelf or an entry to open **Plugin appearance**. Classic
+Click the gear at the bottom of the plugin dock, or right-click the dock, to
+open **Plugin appearance**. Classic
 is the default. **Charcoal + moss** and **Warm graphite + brass** apply to
 windows whose root uses `<panel theme="plugin" ...>`. The choice is saved in
 the client's settings. Existing windows without this attribute keep their
@@ -69,7 +71,7 @@ Tokens are `text`, `muted`, `field`, `border`, `accent`, and `background`;
 the fallback uses `#AARRGGBB`.
 
 The `samples/AcDream.Plugins.ThemeGallery` plugin shows every control in one
-opted-in window; switch themes from the shelf to compare them.
+opted-in window; switch themes from the dock's gear to compare them.
 
 Set `searchable="true"` on a menu to add an editable search band. Each typed
 word must match its label, ignoring case. Filtering does not select an item;
@@ -344,22 +346,35 @@ widget works in 0..1 internally. They default to 0 and 1.
         value="{HealPercent}" onchange="{SetHealPercent}"/>
 ```
 
-## The plugin shelf
+## The plugin dock
 
-The shelf is the strip of plugin-window buttons at the right screen edge. It
-is a normal window: draggable by the grip along its top, collapsible with
-the `>`/`<` toggle at the grip's right end, and persisted like any other.
-`Shift+Ctrl+F1` hides and shows it; hiding the shelf never disables a plugin
-or touches a plugin window's own visibility.
+The dock holds one slot per plugin window, with a divider between plugins and
+a gear at the bottom that opens **Plugin appearance**. Hovering a slot names
+the window and its plugin; an open window gets an accent dot. It draws the
+same shape in every theme, in Classic's black and gold or the theme's colours.
+
+- **Floating** (the default): drag it by the dots that appear along its top
+  while the pointer is over it. The chevron beside them collapses it to a
+  small pill.
+- **Left edge / Right edge**: a rail locked to that screen edge, which slides
+  up and down it. An accent pill on the edge marks open windows, and a taller
+  one the window in front.
+
+Choose the mode under **Dock** in Plugin appearance, or drag the dock to
+within 8 points of a screen edge to lock it there; pull it more than 32 points
+away to float it again. The mode is saved with the theme. When there are more
+windows than fit, the dock scrolls with the mouse wheel. `Shift+Ctrl+F1`
+hides and shows it; hiding the dock never disables a plugin or touches a
+plugin window's own visibility.
 
 ## Showing and hiding your own window
 
 A plugin window is on screen only while two things agree: the player's own
-request (its shelf button and its close button) and, when the markup binds
+request (its dock slot and its close button) and, when the markup binds
 the root's `visible`, the plugin's binding. Closing the window with its
 close button clears the player's request, so setting the binding back to
 true does not reopen it. `ShowPanel` and `HidePanel` make that request from
-the plugin, exactly as the shelf button does:
+the plugin, exactly as the dock slot does:
 
 ```csharp
 host.Ui.ShowPanel("main");          // by window id or title
````

- [ ] **Step 7: Run the tests and see them pass**

```bash
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~PluginAppearanceBindingTests|FullyQualifiedName~MarkupDocument"
git diff --stat main -- src/AcDream.Plugin.Abstractions
```

Expected: `Passed: 34`; the diff prints nothing.

- [ ] **Step 8: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/PluginAppearanceBinding.cs src/AcDream.App/UI/RetailUiRuntime.cs src/AcDream.App/UI/PluginSidePanel.cs docs/plugin-ui-markup.md tests/AcDream.App.Tests/UI/PluginAppearanceBindingTests.cs
git commit -m "ui: choose the dock mode in plugin appearance; call it the dock

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```


### Task 9: Visual gate — 2x captures judged by the user

**Goal:** 2× backbuffer captures of the dock in Classic, Moss and Brass × floating, left rail and right rail, plus one collapsed and one hover-label shot, which the user looks at and accepts before merge.

> **USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in `acceptanceCriteria` has been re-validated independently, with output captured.

**Files:**
- None in the repo. Throwaway worktree `/tmp/dock-capture` (a copy of `modern-theme/dock` plus the uncommitted capture-size patch), scratch root `/tmp/dock-root`.

**Acceptance Criteria:**
- [ ] 11 PNGs exist in `/tmp/dock-root/artifacts`: `classic-floating`, `classic-left`, `classic-right`, `moss-floating`, `moss-left`, `moss-right`, `brass-floating`, `brass-left`, `brass-right`, `moss-collapsed`, `moss-label`, each at the built-in display's 2× framebuffer size.
- [ ] Each shot was flattened onto black before viewing (the PNGs keep framebuffer alpha).
- [ ] The user has seen all 11 and said the dock looks right, or every change they asked for is fixed and recaptured.
- [ ] The capture-size patch was never committed; `/tmp/dock-capture` is removed afterwards.

**Verify:** `ls /tmp/dock-root/artifacts/*.png | wc -l` → 11, and the user's acceptance quoted in the task close

**Steps:**

- [ ] **Step 1: Make a throwaway capture worktree with the size patch**

The client's screenshot asks for the window size in points, which fails on Retina ("retained capture is 1600x1200; 800x600 was requested"). The size comes from the render loop in `GameWindow.cs` (`Vector2D<int> size = _window!.Size;`, about line 1619), which feeds `RenderFrameInput` and then `FrameScreenshotController.CapturePending`. In a throwaway worktree only, use the framebuffer size there:

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
git worktree add --detach /tmp/dock-capture modern-theme/dock
cd /tmp/dock-capture
grep -n "Vector2D<int> size = _window!.Size;" src/AcDream.App/Rendering/GameWindow.cs
sed -i '' 's/Vector2D<int> size = _window!.Size;/Vector2D<int> size = _window!.FramebufferSize;/' src/AcDream.App/Rendering/GameWindow.cs
git diff --stat   # exactly one line in GameWindow.cs; never commit it
```

This also sizes the world viewport by the framebuffer, which is fine for a throwaway capture. After the first run, check that the PNG is twice the window's point size (for example 1600×1200 for an 800×600 window); if the capture still fails or the UI is drawn at the wrong scale, stop and report the error text instead of guessing at another patch. Then build:

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet build src/AcDream.App/AcDream.App.csproj -c Release 2>&1 | tail -2
dotnet build samples/AcDream.Plugins.ThemeGallery -c Release 2>&1 | tail -1
dotnet build samples/AcDream.Plugins.CanvasDemo -c Release 2>&1 | tail -1
```

- [ ] **Step 2: Make the scratch root**

```bash
rm -rf /tmp/dock-root && mkdir -p /tmp/dock-root/plugins /tmp/dock-root/settings /tmp/dock-root/artifacts /tmp/dock-run
cp -R "$HOME/Library/Application Support/OpenAC/plugins/"* /tmp/dock-root/plugins/
cp -R /tmp/dock-capture/samples/AcDream.Plugins.ThemeGallery/bin/Release/net10.0 /tmp/dock-root/plugins/acdream.themegallery
cp -R /tmp/dock-capture/samples/AcDream.Plugins.CanvasDemo/bin/Release/net10.0 /tmp/dock-root/plugins/acdream.canvasdemo
ls /tmp/dock-root/plugins
```

Expected: `acdream.mosstank acdream.themegallery acdream.canvasdemo openac.goarrow rabiddee.golem` (check each sample's own `plugin.json` id and rename the folder to it if it differs).

- [ ] **Step 3: One run per theme and mode**

For each theme in Classic, Moss, Brass and each dock in floating, left, right, write the settings and run the client offline with a probe script. The user drags the window onto the built-in Retina display during the sleep; give them a countdown first.

```bash
run() {  # run <theme> <dock> <shot-name> [extra probe lines]
  printf '{ "version": 4, "pluginUi": { "theme": "%s", "dock": "%s" } }\n' "$1" "$2" > /tmp/dock-root/settings/settings.json
  printf 'sleep 45000\n%sscreenshot %s\nclose-client\n' "${4:-}" "$3" > /tmp/dock-run/probe.txt
  (cd /tmp/dock-run && ACDREAM_ROOT_DIR=/tmp/dock-root ACDREAM_DAT_DIR=$HOME/AsheronsCall \
     ACDREAM_UI_PROBE_SCRIPT=/tmp/dock-run/probe.txt ACDREAM_AUTOMATION_ARTIFACT_DIR=/tmp/dock-root/artifacts \
     dotnet /tmp/dock-capture/src/AcDream.App/bin/Release/net10.0/AcDream.App.dll)
}
for theme in Classic Moss Brass; do for dock in floating left right; do
  run $theme $dock "$(echo $theme | tr A-Z a-z)-$dock"
done; done
```

- [ ] **Step 4: The collapsed and hover-label shots**

```bash
run Moss floating moss-label "mousemove 30 160\nsleep 500\n"
```

For `moss-collapsed`, start `run Moss floating moss-collapsed` and ask the user to click the dock's collapse chevron (hover the dock's top edge) during the 45 s sleep.

- [ ] **Step 5: Flatten and show the user**

```bash
cd /tmp/dock-root/artifacts && ls *.png | wc -l
for f in [a-z]*.png; do magick "$f" -background black -alpha remove -alpha off "flat-$f"; done
magick identify -format '%f %wx%h\n' flat-*.png   # each is the 2x framebuffer size
```

Crop each flat shot to the dock and its surroundings, view them, and send them to the user (SendUserFile). Ask the user whether the dock looks right in each. Fix anything they ask for in the real worktree (with a test where it is behaviour), commit, rebuild `/tmp/dock-capture` from the new branch head, and recapture the affected shots.

- [ ] **Step 6: Clean up**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
git worktree remove --force /tmp/dock-capture
git worktree list   # /tmp/dock-capture is gone; the capture patch was never committed
```


### Task 10: Final review, full suite and hand-off

**Goal:** The whole branch is reviewed against the spec, the full App suite shows no new failures against the Task 0 baseline, the Release build has 0 warnings, and the user decides about merging and pushing.

**Files:**
- None new; fixes from the review go in the files they concern.

**Acceptance Criteria:**
- [ ] A code review of `main..modern-theme/dock` against the spec has been done and every finding fixed or answered.
- [ ] `dotnet build AcDream.slnx -c Release` → 0 warnings, 0 errors.
- [ ] The full App suite's failures, compared with `/tmp/dock-base-fails.txt`, show no new names (the two deleted `PluginSidePanelToggleGlyphClipTests` InstalledDat failures disappear).
- [ ] `AcDream.UI.Abstractions.Tests` all pass (983 on 2026-10-02, the 5 new ones included).
- [ ] The user was asked before any merge into `main` or push, and their answer was followed.

**Verify:** `comm -13 /tmp/dock-base-fails.txt /tmp/dock-branch-fails.txt` → only the `Passed!/Failed!` summary line

**Steps:**

- [ ] **Step 1: Review**

Run the requesting-code-review skill on `main..modern-theme/dock` with the spec `docs/superpowers/specs/2026-10-02-plugin-dock-design.md` (on the docs branch) as the requirement. Fix findings in the worktree with tests, one commit per fix.

- [ ] **Step 2: Full build and suites**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/dock && export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet build AcDream.slnx -c Release 2>&1 | tail -3
dotnet test tests/AcDream.UI.Abstractions.Tests/AcDream.UI.Abstractions.Tests.csproj 2>&1 | tail -1
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj 2>&1 | grep -E "\[FAIL\]|Passed!|Failed!" | sed -E 's/^.*\] +//; s/ \[FAIL\]//' | sort > /tmp/dock-branch-fails.txt
comm -13 /tmp/dock-base-fails.txt /tmp/dock-branch-fails.txt
git checkout -- '*.lock.json'
```

Expected: `0 Warning(s)`; `Failed: 0` for UI.Abstractions; `comm` prints only the summary line. If a name appears, rerun its class alone (the flaky list: `GraphicalPluginSessionTests.ReloadCommand…`, `LiveEntityNetworkBranchRoutingTests…GenericTail`, `GameWindowRenderLeafCompositionTests.Paperdoll…`) before treating it as a regression.

- [ ] **Step 3: Ask the user**

Ask whether to merge `modern-theme/dock` into fork `main` (with `--no-ff`, as `modern-theme/controls` was) and whether to push. Do neither without a yes. After a merge, delete the branch and worktree only if the user agrees.

