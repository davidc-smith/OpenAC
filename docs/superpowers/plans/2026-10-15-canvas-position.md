# Canvas Position Persistence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A plugin canvas registered with `RemembersPosition = true` has its anchor and offset saved per character by the host and put back when that character next enters the world.

**Architecture:** A new `SettingsStore` section holds `{anchor, x, y}` per character per `<pluginId>/<canvasId>`. A new `PluginCanvasPositionPersistence` in `AcDream.App/UI` tracks opted-in screen canvases through a small `IRememberedCanvas` interface (implemented by `PluginCanvasRegistration`): it restores in `RetailUiRuntime.RestoreLayout()`, saves a change once the canvas has stayed put for 0.5 s with no press held (polled from `RetailUiRuntime.Tick`), and flushes on unmount/dispose. Restored positions are clamped on screen using a placement helper shared with `PluginCanvasElement.Layout`.

**Tech Stack:** C# / .NET (SDK at `~/.dotnet`), xUnit, System.Text.Json.Nodes.

**Spec:** `docs/superpowers/specs/2026-10-15-canvas-position-design.md` (branch `docs/canvas-position-spec`).

## Global Constraints

- Work on branch `feat/canvas-position` in `/Users/davidsmith/code/OpenAC/OpenAC` (already created from `main` f6000588).
- Use `~/.dotnet/dotnet` for every build and test command.
- Settings JSON shape is exactly `"pluginCanvasPositions": { "<characterKey>": { "<pluginId>/<canvasId>": { "anchor": "<PluginCanvasAnchor name>", "x": <number>, "y": <number> } } }`.
- Settle time is exactly 0.5 seconds (`PluginCanvasPositionPersistence.SettleSeconds`).
- An empty/whitespace or `"default"` (case-insensitive) character key never saves or restores (same rule as `RetailWindowLayoutPersistence.CanPersist`).
- Only canvases with `RemembersPosition == true` and `InMarkup == false` are tracked.
- `ForkContractVersion` becomes `0.1.25-fork.7`.
- Match surrounding code style: XML doc comments in the same plain-English voice, file-scoped namespaces, `internal` for host types.
- Every commit message ends with the line `Claude-Session: https://claude.ai/code/session_01Lm9T9oP2Hi5FEjwsLmXeyN`.

**User decisions (already made):**
- Opt-in flag `PluginCanvasDescriptor.RemembersPosition` (not automatic for every canvas).
- No new event: the host just sets `Anchor`/`Offset`; docs tell plugins to read rather than cache.
- Scope is the fork client only: no Aethrix HUD migration, no upstream PR. Aethrix `docs/api-gaps.md` #52 is marked fixed in the fork.

---

## File Structure

- `src/AcDream.UI.Abstractions/Panels/Settings/SettingsStore.cs` — modify: `PluginCanvasPosition` record + load/save.
- `src/AcDream.Plugin.Abstractions/PluginCanvas.cs` — modify: `RemembersPosition` on the descriptor.
- `src/AcDream.Plugin.Abstractions/AcDream.Plugin.Abstractions.csproj` — modify: contract version.
- `src/AcDream.App/UI/Layout/PluginCanvasPlacement.cs` — create: anchor/offset → top-left, and clamp on screen.
- `src/AcDream.App/UI/Layout/PluginCanvasElement.cs` — modify: use the helper; publish "holding a press" to the registration.
- `src/AcDream.App/Plugins/PluginCanvasRegistration.cs` — modify: implement `IRememberedCanvas`, `HoldingPointer` hook.
- `src/AcDream.App/UI/PluginCanvasPositionPersistence.cs` — create: the persistence (with `IRememberedCanvas`).
- `src/AcDream.App/UI/RetailUiRuntime.cs` — modify: construct, tick, restore, track on mount, untrack on teardown, dispose.
- `docs/plugin-api.md` — modify: "Remembered position" subsection.
- Tests: `tests/AcDream.UI.Abstractions.Tests/Panels/Settings/PluginCanvasPositionStoreTests.cs`, `tests/AcDream.App.Tests/UI/Layout/PluginCanvasPlacementTests.cs`, `tests/AcDream.App.Tests/UI/Layout/PluginCanvasPointerTests.cs` (add tests), `tests/AcDream.App.Tests/UI/PluginCanvasPositionPersistenceTests.cs`.

---

### Task 1: Settings store section for canvas positions

**Goal:** `SettingsStore` can save and load a plugin canvas position per character.

**Files:**
- Modify: `src/AcDream.UI.Abstractions/Panels/Settings/SettingsStore.cs` (record beside `UiWindowPosition` at line 12; methods after `SaveWindowPlacement`, ~line 530)
- Test: `tests/AcDream.UI.Abstractions.Tests/Panels/Settings/PluginCanvasPositionStoreTests.cs`

**Acceptance Criteria:**
- [ ] Save then load returns the same anchor name, x and y; another character or canvas key loads null.
- [ ] Saving a canvas position keeps other sections (e.g. `windowPlacements`) intact.
- [ ] A malformed entry (missing field, wrong type, non-finite) or an unreadable file loads null without throwing.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.UI.Abstractions.Tests --filter PluginCanvasPositionStoreTests` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.IO;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.UI.Abstractions.Tests.Panels.Settings;

public sealed class PluginCanvasPositionStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"canvas-position-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void ASavedPositionLoadsBackForThatCharacterAndCanvasOnly()
    {
        var store = new SettingsStore(_path);
        var position = new PluginCanvasPosition("BottomRight", -12.5, -40);
        store.SavePluginCanvasPosition("alice", "aethrix/hud", position);

        Assert.Equal(position, store.LoadPluginCanvasPosition("alice", "aethrix/hud"));
        Assert.Null(store.LoadPluginCanvasPosition("bob", "aethrix/hud"));
        Assert.Null(store.LoadPluginCanvasPosition("alice", "aethrix/xp"));
    }

    [Fact]
    public void SavingAPositionKeepsTheOtherSections()
    {
        var store = new SettingsStore(_path);
        var placement = new UiWindowPlacement(new UiWindowLayout(1, 2, 300, 200, true, false, false), 1920, 1080);
        store.SaveWindowPlacement("alice", "chat", placement);
        store.SavePluginCanvasPosition("alice", "aethrix/hud", new PluginCanvasPosition("TopLeft", 5, 6));

        Assert.Equal(placement, store.LoadWindowPlacement("alice", "1920x1080", "chat", placement.Layout));
        store.SavePluginCanvasPosition("alice", "aethrix/xp", new PluginCanvasPosition("Center", 0, 0));
        Assert.Equal(new PluginCanvasPosition("TopLeft", 5, 6), store.LoadPluginCanvasPosition("alice", "aethrix/hud"));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("""{"pluginCanvasPositions":{"alice":{"aethrix/hud":{"x":1,"y":2}}}}""")]
    [InlineData("""{"pluginCanvasPositions":{"alice":{"aethrix/hud":{"anchor":"TopLeft","x":"one","y":2}}}}""")]
    [InlineData("""{"pluginCanvasPositions":{"alice":{"aethrix/hud":{"anchor":3,"x":1,"y":2}}}}""")]
    [InlineData("""{"pluginCanvasPositions":{"alice":"nope"}}""")]
    public void AMalformedEntryLoadsAsNull(string json)
    {
        File.WriteAllText(_path, json);
        Assert.Null(new SettingsStore(_path).LoadPluginCanvasPosition("alice", "aethrix/hud"));
    }

    [Fact]
    public void ANonFiniteNumberLoadsAsNull()
    {
        var store = new SettingsStore(_path);
        store.SavePluginCanvasPosition("alice", "aethrix/hud", new PluginCanvasPosition("TopLeft", 1, 2));
        File.WriteAllText(_path, File.ReadAllText(_path).Replace("\"x\": 1", "\"x\": 1e400"));
        Assert.Null(store.LoadPluginCanvasPosition("alice", "aethrix/hud"));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `~/.dotnet/dotnet test tests/AcDream.UI.Abstractions.Tests --filter PluginCanvasPositionStoreTests`
Expected: build error, `PluginCanvasPosition` / `SavePluginCanvasPosition` not defined.

- [ ] **Step 3: Implement**

Beside `UiWindowPosition` (line 12):

```csharp
/// <summary>
/// Where a plugin canvas was left: its anchor's name and its offset from
/// that anchor, in interface pixels. The anchor is kept by name so the
/// settings file needs nothing from the plugin contract.
/// </summary>
public readonly record struct PluginCanvasPosition(string Anchor, double X, double Y);
```

After `SaveWindowPlacement`:

```csharp
    /// <summary>
    /// The position saved for one plugin canvas of one character, under
    /// <c>pluginCanvasPositions[toonKey][canvasKey]</c>; null when there is
    /// none or it can't be read.
    /// </summary>
    public PluginCanvasPosition? LoadPluginCanvasPosition(string toonKey, string canvasKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toonKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(canvasKey);
        if (!File.Exists(_path)) return null;
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(_path)) as JsonObject;
            if ((root?["pluginCanvasPositions"] as JsonObject)?[toonKey] is not JsonObject toon
                || toon[canvasKey] is not JsonObject node
                || node["anchor"] is not JsonValue anchorValue
                || !anchorValue.TryGetValue(out string? anchor)
                || node["x"] is not JsonValue xValue
                || !xValue.TryGetValue(out double x)
                || node["y"] is not JsonValue yValue
                || !yValue.TryGetValue(out double y)
                || !double.IsFinite(x)
                || !double.IsFinite(y))
                return null;
            return new PluginCanvasPosition(anchor, x, y);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"settings: failed to load plugin canvas position from {_path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Saves one plugin canvas's position for one character under
    /// <c>pluginCanvasPositions[toonKey][canvasKey]</c>, preserving all other sections.
    /// </summary>
    public void SavePluginCanvasPosition(string toonKey, string canvasKey, PluginCanvasPosition position)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toonKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(canvasKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(position.Anchor);
        JsonObject root = LoadMutableRoot();
        JsonObject toon = GetOrCreateObject(GetOrCreateObject(root, "pluginCanvasPositions"), toonKey);
        toon[canvasKey] = new JsonObject
        {
            ["anchor"] = position.Anchor,
            ["x"] = position.X,
            ["y"] = position.Y,
        };
        root["version"] = CurrentSchemaVersion;
        WriteMutableRoot(root);
    }
```

If `1e400` parses as infinity rather than throwing in `TryGetValue<double>`, the `IsFinite` check returns null; if `JsonNode.Parse` throws instead, the catch returns null. Either way the test passes.

- [ ] **Step 4: Run to verify they pass**

Run: `~/.dotnet/dotnet test tests/AcDream.UI.Abstractions.Tests --filter PluginCanvasPositionStoreTests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.UI.Abstractions/Panels/Settings/SettingsStore.cs tests/AcDream.UI.Abstractions.Tests/Panels/Settings/PluginCanvasPositionStoreTests.cs
git commit -m "feat(settings): store plugin canvas positions per character

Claude-Session: https://claude.ai/code/session_01Lm9T9oP2Hi5FEjwsLmXeyN"
```

---

### Task 2: `RemembersPosition` on the canvas descriptor + contract version

**Goal:** Plugins can opt a canvas in; the contract version says so.

**Files:**
- Modify: `src/AcDream.Plugin.Abstractions/PluginCanvas.cs` (after `WantsOutsidePresses`, before `InMarkup`, ~line 140)
- Modify: `src/AcDream.Plugin.Abstractions/AcDream.Plugin.Abstractions.csproj:39`

**Acceptance Criteria:**
- [ ] `new PluginCanvasDescriptor("a", 1, 1).RemembersPosition` is `false`; `with { RemembersPosition = true }` sets it.
- [ ] `<ForkContractVersion>0.1.25-fork.7</ForkContractVersion>`.
- [ ] Solution builds; `AcDream.HostParity.Tests` and `AcDream.Plugin.Tests` pass.

**Verify:** `~/.dotnet/dotnet build AcDream.slnx -c Debug && ~/.dotnet/dotnet test tests/AcDream.HostParity.Tests && ~/.dotnet/dotnet test tests/AcDream.Plugin.Tests` → build succeeds, tests pass

**Steps:**

- [ ] **Step 1: Add the property**

```csharp
    /// <summary>
    /// Whether the host remembers where the canvas was left. On, the host
    /// saves the canvas's <see cref="IPluginCanvas.Anchor"/> and
    /// <see cref="IPluginCanvas.Offset"/> for the logged-in character and
    /// puts them back when that character next enters the world, or at
    /// once for a canvas registered while the character is already in it;
    /// a position put back is kept on screen. A move is saved once the
    /// canvas has stayed put for half a second with no press held on it,
    /// so a drag is saved once, after the release. Read
    /// <see cref="IPluginCanvas.Anchor"/> and <see cref="IPluginCanvas.Offset"/>
    /// rather than keeping your own copy: the host may change them when a
    /// character enters the world. Nothing is saved or put back before a
    /// character has logged in, for a canvas registered with
    /// <see cref="InMarkup"/> (its element places it), or on a host without
    /// a window, which keeps the flag. Off, the default, nothing changes.
    /// A host before contract <c>0.1.25-fork.7</c> ignores it.
    /// </summary>
    public bool RemembersPosition { get; init; }
```

- [ ] **Step 2: Bump the version** — line 39 of the csproj: `<ForkContractVersion>0.1.25-fork.7</ForkContractVersion>`. Also update the csproj comment above it if it lists the fork history (read lines 25-40 first; add `fork.7 remembered canvas positions` in the same style if a list is there).

- [ ] **Step 3: Build and run the parity and plugin tests**

Run: `~/.dotnet/dotnet build AcDream.slnx -c Debug && ~/.dotnet/dotnet test tests/AcDream.HostParity.Tests && ~/.dotnet/dotnet test tests/AcDream.Plugin.Tests`
Expected: success. If a parity/allow-list test fails because a new descriptor member needs listing, add `RemembersPosition` to that list next to `WantsOutsidePresses`/`InMarkup` in the same form and rerun.

- [ ] **Step 4: Commit**

```bash
git add src/AcDream.Plugin.Abstractions tests/AcDream.HostParity.Tests
git commit -m "feat(plugins): a canvas can ask the host to remember its position

Contract 0.1.25-fork.7.

Claude-Session: https://claude.ai/code/session_01Lm9T9oP2Hi5FEjwsLmXeyN"
```

---

### Task 3: Shared placement helper and "holding a press" on the registration

**Goal:** One placement routine for the element and the persistence's clamp; the registration reports whether its element holds a press.

**Files:**
- Create: `src/AcDream.App/UI/Layout/PluginCanvasPlacement.cs`
- Modify: `src/AcDream.App/UI/Layout/PluginCanvasElement.cs` (`Layout()` ~line 532; constructor ~line 183; `ReleaseTargets()` ~line 750)
- Modify: `src/AcDream.App/Plugins/PluginCanvasRegistration.cs`
- Test: `tests/AcDream.App.Tests/UI/Layout/PluginCanvasPlacementTests.cs`, `tests/AcDream.App.Tests/UI/Layout/PluginCanvasPointerTests.cs`

**Acceptance Criteria:**
- [ ] `PluginCanvasPlacement.TopLeft` gives the same positions `Layout()` gave for all nine anchors (existing `PluginCanvasPointerTests.APressArrivesInCanvasPixelsUnderEveryAnchor` still passes).
- [ ] `ClampOnScreen` returns the offset unchanged when the canvas is on screen, pulls it fully on screen for every anchor when it is not, pins to 0 when the canvas is larger than the screen, and returns the offset unchanged for a layer under 1 px.
- [ ] `registration.IsHoldingPointer` is true between a press on the canvas and its release, cancel, or `ReleasePointer()`, and false for a click-through canvas.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~PluginCanvasPlacementTests|FullyQualifiedName~PluginCanvasPointerTests|FullyQualifiedName~PluginCanvasElementTests"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing placement tests**

```csharp
using AcDream.App.UI.Layout;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.UI.Layout;

public sealed class PluginCanvasPlacementTests
{
    [Theory]
    [InlineData(PluginCanvasAnchor.TopLeft, 10, 20)]
    [InlineData(PluginCanvasAnchor.TopCenter, 310, 20)]
    [InlineData(PluginCanvasAnchor.TopRight, 610, 20)]
    [InlineData(PluginCanvasAnchor.CenterLeft, 10, 270)]
    [InlineData(PluginCanvasAnchor.Center, 310, 270)]
    [InlineData(PluginCanvasAnchor.CenterRight, 610, 270)]
    [InlineData(PluginCanvasAnchor.BottomLeft, 10, 520)]
    [InlineData(PluginCanvasAnchor.BottomCenter, 310, 520)]
    [InlineData(PluginCanvasAnchor.BottomRight, 610, 520)]
    public void TopLeftAddsTheOffsetToTheAnchorsCorner(PluginCanvasAnchor anchor, float left, float top)
    {
        (float x, float y) = PluginCanvasPlacement.TopLeft(anchor, new PluginPoint(10, 20), 200, 100, 800, 600);
        Assert.Equal((left, top), (x, y));
    }

    [Fact]
    public void AnOnScreenCanvasKeepsItsOffset()
    {
        var offset = new PluginPoint(-30, -40);
        Assert.Equal(offset, PluginCanvasPlacement.ClampOnScreen(PluginCanvasAnchor.BottomRight, offset, 200, 100, 800, 600));
    }

    [Theory]
    [InlineData(PluginCanvasAnchor.TopLeft, 900, 700, 600, 500)]
    [InlineData(PluginCanvasAnchor.TopLeft, -50, -60, 0, 0)]
    [InlineData(PluginCanvasAnchor.BottomRight, 50, 60, 0, 0)]
    [InlineData(PluginCanvasAnchor.BottomRight, -900, -700, -600, -500)]
    [InlineData(PluginCanvasAnchor.Center, 500, -400, 300, -250)]
    public void AnOffScreenCanvasIsPulledFullyOnScreenKeepingItsAnchor(
        PluginCanvasAnchor anchor, double x, double y, double clampedX, double clampedY)
    {
        Assert.Equal(new PluginPoint(clampedX, clampedY),
            PluginCanvasPlacement.ClampOnScreen(anchor, new PluginPoint(x, y), 200, 100, 800, 600));
    }

    [Fact]
    public void ACanvasLargerThanTheScreenIsPinnedToTheTopLeft()
    {
        Assert.Equal(new PluginPoint(0, 0),
            PluginCanvasPlacement.ClampOnScreen(PluginCanvasAnchor.TopLeft, new PluginPoint(50, 50), 1000, 700, 800, 600));
    }

    [Fact]
    public void AnUnsizedScreenLeavesTheOffsetAlone()
    {
        var offset = new PluginPoint(5000, 5000);
        Assert.Equal(offset, PluginCanvasPlacement.ClampOnScreen(PluginCanvasAnchor.TopLeft, offset, 200, 100, 0, 0));
    }
}
```

Check the Center row: canvas 200x100 on 800x600, Center puts top-left at (300+500, 250-400) = (800, -150); clamp to (600, 0) → shift (-200, +150) → offset (300, -250). ✓. BottomRight (-900,-700): top-left (600-900, 500-700) = (-300, -200) → (0,0), shift (+300, +200) → (-600, -500). ✓

- [ ] **Step 2: Write the failing hold tests** — append to `PluginCanvasPointerTests` (uses its existing `MountedMap` helper; the map sits at (10,20) size 200x100):

```csharp
    [Fact]
    public void TheRegistrationKnowsWhileItsCanvasHoldsAPress()
    {
        (Harness harness, PluginCanvasRegistration registration, _, _) = MountedMap();
        Assert.False(registration.IsHoldingPointer);

        harness.Root.OnMouseDown(UiMouseButton.Left, 100, 60);
        Assert.True(registration.IsHoldingPointer);
        harness.Root.OnMouseMove(150, 80);
        Assert.True(registration.IsHoldingPointer);
        harness.Root.OnMouseUp(UiMouseButton.Left, 150, 80);
        Assert.False(registration.IsHoldingPointer);
    }

    [Fact]
    public void ACancelOrAPluginReleaseEndsTheHold()
    {
        (Harness harness, PluginCanvasRegistration registration, _, _) = MountedMap();

        harness.Root.OnMouseDown(UiMouseButton.Left, 100, 60);
        registration.IsVisible = false;
        harness.Tick();
        Assert.False(registration.IsHoldingPointer);

        registration.IsVisible = true;
        harness.Tick();
        harness.Root.OnMouseDown(UiMouseButton.Left, 100, 60);
        registration.ReleasePointer();
        Assert.False(registration.IsHoldingPointer);
    }

    [Fact]
    public void AClickThroughCanvasNeverHolds()
    {
        (Harness harness, PluginCanvasRegistration registration, _, _) = MountedMap(input: false);
        harness.Root.OnMouseDown(UiMouseButton.Left, 100, 60);
        Assert.False(registration.IsHoldingPointer);
        harness.Root.OnMouseUp(UiMouseButton.Left, 100, 60);
    }
```

- [ ] **Step 3: Run to verify they fail**

Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~PluginCanvasPlacementTests|FullyQualifiedName~PluginCanvasPointerTests"`
Expected: build error, `PluginCanvasPlacement` / `IsHoldingPointer` not defined.

- [ ] **Step 4: Create the helper**

```csharp
using AcDream.Plugin.Abstractions;

namespace AcDream.App.UI.Layout;

/// <summary>
/// Where a screen canvas sits for its anchor and offset, and the offset
/// that brings it back on screen. Shared by the element that places the
/// canvas each tick and the persistence that puts a saved position back.
/// </summary>
internal static class PluginCanvasPlacement
{
    /// <summary>
    /// The canvas's top-left corner inside a layer of
    /// <paramref name="layerWidth"/> by <paramref name="layerHeight"/>,
    /// before rounding to whole pixels.
    /// </summary>
    internal static (float X, float Y) TopLeft(
        PluginCanvasAnchor anchor, PluginPoint offset, float width, float height, float layerWidth, float layerHeight)
    {
        float x = (float)offset.X;
        float y = (float)offset.Y;
        switch (anchor)
        {
            case PluginCanvasAnchor.TopCenter:
            case PluginCanvasAnchor.Center:
            case PluginCanvasAnchor.BottomCenter:
                x += (layerWidth - width) * 0.5f;
                break;
            case PluginCanvasAnchor.TopRight:
            case PluginCanvasAnchor.CenterRight:
            case PluginCanvasAnchor.BottomRight:
                x += layerWidth - width;
                break;
        }
        switch (anchor)
        {
            case PluginCanvasAnchor.CenterLeft:
            case PluginCanvasAnchor.Center:
            case PluginCanvasAnchor.CenterRight:
                y += (layerHeight - height) * 0.5f;
                break;
            case PluginCanvasAnchor.BottomLeft:
            case PluginCanvasAnchor.BottomCenter:
            case PluginCanvasAnchor.BottomRight:
                y += layerHeight - height;
                break;
        }
        return (x, y);
    }

    /// <summary>
    /// The offset, from the same anchor, that puts the whole canvas inside
    /// the layer: unchanged when it already is, and against the top or left
    /// edge on an axis where the canvas is larger than the layer. A layer
    /// under a pixel either way (not sized yet) leaves the offset alone.
    /// </summary>
    internal static PluginPoint ClampOnScreen(
        PluginCanvasAnchor anchor, PluginPoint offset, float width, float height, float layerWidth, float layerHeight)
    {
        if (layerWidth < 1f || layerHeight < 1f) return offset;
        (float x, float y) = TopLeft(anchor, offset, width, height, layerWidth, layerHeight);
        float clampedX = Math.Clamp(x, 0f, MathF.Max(0f, layerWidth - width));
        float clampedY = Math.Clamp(y, 0f, MathF.Max(0f, layerHeight - height));
        if (clampedX == x && clampedY == y) return offset;
        return new PluginPoint(offset.X + (clampedX - x), offset.Y + (clampedY - y));
    }
}
```

- [ ] **Step 5: Use it in `PluginCanvasElement.Layout()`** — replace the body from `float x = (float)_registration.Offset.X;` through the second `switch` with:

```csharp
        (float x, float y) = PluginCanvasPlacement.TopLeft(
            _registration.Anchor, _registration.Offset, Width, Height, layerWidth, layerHeight);
```

keeping the two `Left = MathF.Floor(x + 0.5f); Top = MathF.Floor(y + 0.5f);` lines after it.

- [ ] **Step 6: Hold hook on the registration** — in `PluginCanvasRegistration`, beside `PointerRelease`:

```csharp
    private volatile Func<bool>? _holdingPointer;

    /// <summary>
    /// Asks the mounted element whether it holds a press; set by the element
    /// when it mounts with pointer input, cleared by it when it comes down.
    /// </summary>
    internal Func<bool>? HoldingPointer
    {
        get => _holdingPointer;
        set => _holdingPointer = value;
    }

    /// <summary>Whether the canvas holds a press right now: between a press on it and its release or cancel.</summary>
    internal bool IsHoldingPointer => _holdingPointer?.Invoke() ?? false;
```

In `PluginCanvasElement`: add a field `private readonly Func<bool>? _holdingPointer;`. In the constructor's `if (registration.AcceptsPointerInput)` block add:

```csharp
            _holdingPointer = () => _heldButton != PluginPointerButton.None;
            registration.HoldingPointer = _holdingPointer;
```

In `ReleaseTargets()`, after the `PointerRelease` clear:

```csharp
        if (ReferenceEquals(_registration.HoldingPointer, _holdingPointer))
            _registration.HoldingPointer = null;
```

(Note `_heldButton` is written by the `Press`, `Release`, `CaptureChanged` and `ReleasePointer` paths already; nothing else changes.)

- [ ] **Step 7: Run to verify all pass**

Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~PluginCanvasPlacementTests|FullyQualifiedName~PluginCanvasPointerTests|FullyQualifiedName~PluginCanvasElementTests"`
Expected: all pass (including the pre-existing anchor tests).

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.App/UI/Layout/PluginCanvasPlacement.cs src/AcDream.App/UI/Layout/PluginCanvasElement.cs src/AcDream.App/Plugins/PluginCanvasRegistration.cs tests/AcDream.App.Tests/UI/Layout/PluginCanvasPlacementTests.cs tests/AcDream.App.Tests/UI/Layout/PluginCanvasPointerTests.cs
git commit -m "refactor(ui): share canvas placement; a registration knows its held press

Claude-Session: https://claude.ai/code/session_01Lm9T9oP2Hi5FEjwsLmXeyN"
```

---

### Task 4: `PluginCanvasPositionPersistence`

**Goal:** The host component that restores, saves after settling, and flushes canvas positions, tested against fake canvases.

**Files:**
- Create: `src/AcDream.App/UI/PluginCanvasPositionPersistence.cs`
- Modify: `src/AcDream.App/Plugins/PluginCanvasRegistration.cs` (implement `IRememberedCanvas`)
- Test: `tests/AcDream.App.Tests/UI/PluginCanvasPositionPersistenceTests.cs`

**Acceptance Criteria:**
- [ ] `Tracks(descriptor)` is true only for `RemembersPosition && !InMarkup`.
- [ ] `RestoreAll()` sets saved anchor and clamped offset; with no entry the canvas is untouched; the restore is not itself saved.
- [ ] A move is saved once after 0.5 s of stillness; a held press delays the save until released; a canvas moving every tick is not saved until it stops.
- [ ] Empty and `"default"` keys never save; nothing saves before the first `RestoreAll()`.
- [ ] `Remove()` and `Dispose()` flush a pending change; `RestoreAll()` for a new character flushes pending changes to the old character first.
- [ ] A canvas `Add`ed after a restore for the current character is restored at once.
- [ ] `PluginCanvasRegistration` implements `IRememberedCanvas` with key `"<ownerId>/<canvasId>"`.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter PluginCanvasPositionPersistenceTests` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.IO;
using AcDream.App.UI;
using AcDream.Plugin.Abstractions;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.Tests.UI;

public sealed class PluginCanvasPositionPersistenceTests : IDisposable
{
    private sealed class FakeCanvas(string key, int width = 200, int height = 100) : IRememberedCanvas
    {
        public string Key { get; } = key;
        public PluginCanvasAnchor Anchor { get; set; } = PluginCanvasAnchor.TopLeft;
        public PluginPoint Offset { get; set; } = new(10, 20);
        public (int Width, int Height) Size { get; } = (width, height);
        public bool IsHoldingPointer { get; set; }
    }

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"canvas-persist-{Guid.NewGuid():N}.json");
    private readonly SettingsStore _store;
    private string _character = "alice";

    public PluginCanvasPositionPersistenceTests() => _store = new SettingsStore(_path);

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private PluginCanvasPositionPersistence Create() =>
        new(_store, () => _character, () => (800f, 600f));

    private PluginCanvasPosition? Saved(string key = "p/hud", string? character = null) =>
        _store.LoadPluginCanvasPosition(character ?? _character, key);

    [Fact]
    public void OnlyOptedInScreenCanvasesAreTracked()
    {
        var plain = new PluginCanvasDescriptor("a", 1, 1);
        Assert.False(PluginCanvasPositionPersistence.Tracks(plain));
        Assert.True(PluginCanvasPositionPersistence.Tracks(plain with { RemembersPosition = true }));
        Assert.False(PluginCanvasPositionPersistence.Tracks(plain with { RemembersPosition = true, InMarkup = true }));
    }

    [Fact]
    public void RestoreSetsTheSavedAnchorAndOffset()
    {
        _store.SavePluginCanvasPosition("alice", "p/hud", new PluginCanvasPosition("BottomRight", -30, -40));
        var canvas = new FakeCanvas("p/hud");
        using var persistence = Create();
        persistence.Add(canvas);

        persistence.RestoreAll();

        Assert.Equal(PluginCanvasAnchor.BottomRight, canvas.Anchor);
        Assert.Equal(new PluginPoint(-30, -40), canvas.Offset);
    }

    [Fact]
    public void ARestoredPositionIsKeptOnScreen()
    {
        _store.SavePluginCanvasPosition("alice", "p/hud", new PluginCanvasPosition("TopLeft", 5000, -10));
        var canvas = new FakeCanvas("p/hud");
        using var persistence = Create();
        persistence.Add(canvas);

        persistence.RestoreAll();

        Assert.Equal(new PluginPoint(600, 0), canvas.Offset);
    }

    [Fact]
    public void WithNothingSavedTheCanvasStaysWhereItIs()
    {
        var canvas = new FakeCanvas("p/hud") { Anchor = PluginCanvasAnchor.Center, Offset = new(1, 2) };
        using var persistence = Create();
        persistence.Add(canvas);

        persistence.RestoreAll();
        persistence.Tick(1.0);

        Assert.Equal(PluginCanvasAnchor.Center, canvas.Anchor);
        Assert.Equal(new PluginPoint(1, 2), canvas.Offset);
        Assert.Null(Saved());
    }

    [Fact]
    public void AnUnknownSavedAnchorIsIgnored()
    {
        _store.SavePluginCanvasPosition("alice", "p/hud", new PluginCanvasPosition("Sideways", 1, 1));
        var canvas = new FakeCanvas("p/hud");
        using var persistence = Create();
        persistence.Add(canvas);

        persistence.RestoreAll();

        Assert.Equal(new PluginPoint(10, 20), canvas.Offset);
    }

    [Fact]
    public void AMoveIsSavedOnceItHasSettled()
    {
        var canvas = new FakeCanvas("p/hud");
        using var persistence = Create();
        persistence.Add(canvas);
        persistence.RestoreAll();

        canvas.Offset = new PluginPoint(50, 60);
        persistence.Tick(0.016);
        persistence.Tick(0.4);
        Assert.Null(Saved());

        persistence.Tick(0.2);
        Assert.Equal(new PluginCanvasPosition("TopLeft", 50, 60), Saved());
    }

    [Fact]
    public void AnAnchorChangeAloneIsSaved()
    {
        var canvas = new FakeCanvas("p/hud");
        using var persistence = Create();
        persistence.Add(canvas);
        persistence.RestoreAll();

        canvas.Anchor = PluginCanvasAnchor.BottomLeft;
        persistence.Tick(0.016);
        persistence.Tick(0.6);

        Assert.Equal(new PluginCanvasPosition("BottomLeft", 10, 20), Saved());
    }

    [Fact]
    public void AHeldPressDelaysTheSaveUntilRelease()
    {
        var canvas = new FakeCanvas("p/hud");
        using var persistence = Create();
        persistence.Add(canvas);
        persistence.RestoreAll();

        canvas.IsHoldingPointer = true;
        canvas.Offset = new PluginPoint(50, 60);
        persistence.Tick(0.016);
        persistence.Tick(2.0);
        Assert.Null(Saved());

        canvas.IsHoldingPointer = false;
        persistence.Tick(0.016);
        Assert.Equal(new PluginCanvasPosition("TopLeft", 50, 60), Saved());
    }

    [Fact]
    public void ACanvasMovingEveryTickIsSavedOnlyWhenItStops()
    {
        var canvas = new FakeCanvas("p/hud");
        using var persistence = Create();
        persistence.Add(canvas);
        persistence.RestoreAll();

        for (int i = 1; i <= 100; i++)
        {
            canvas.Offset = new PluginPoint(i, 0);
            persistence.Tick(0.1);
            Assert.Null(Saved());
        }
        persistence.Tick(0.6);
        Assert.Equal(new PluginCanvasPosition("TopLeft", 100, 0), Saved());
    }

    [Theory]
    [InlineData("default")]
    [InlineData("DEFAULT")]
    [InlineData("")]
    [InlineData("  ")]
    public void ADefaultOrEmptyCharacterNeverSaves(string character)
    {
        _character = character;
        var canvas = new FakeCanvas("p/hud");
        using (var persistence = Create())
        {
            persistence.Add(canvas);
            persistence.RestoreAll();
            canvas.Offset = new PluginPoint(50, 60);
            persistence.Tick(0.016);
            persistence.Tick(1.0);
        }
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void NothingIsSavedBeforeTheFirstRestore()
    {
        var canvas = new FakeCanvas("p/hud");
        using (var persistence = Create())
        {
            persistence.Add(canvas);
            canvas.Offset = new PluginPoint(50, 60);
            persistence.Tick(0.016);
            persistence.Tick(1.0);
        }
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void RemovingACanvasFlushesItsPendingMove()
    {
        var canvas = new FakeCanvas("p/hud");
        using var persistence = Create();
        persistence.Add(canvas);
        persistence.RestoreAll();

        canvas.Offset = new PluginPoint(50, 60);
        persistence.Remove(canvas);

        Assert.Equal(new PluginCanvasPosition("TopLeft", 50, 60), Saved());
        canvas.Offset = new PluginPoint(70, 80);
        persistence.Tick(1.0);
        Assert.Equal(new PluginCanvasPosition("TopLeft", 50, 60), Saved());
    }

    [Fact]
    public void DisposeFlushesPendingMoves()
    {
        var canvas = new FakeCanvas("p/hud");
        var persistence = Create();
        persistence.Add(canvas);
        persistence.RestoreAll();

        canvas.Offset = new PluginPoint(50, 60);
        persistence.Dispose();

        Assert.Equal(new PluginCanvasPosition("TopLeft", 50, 60), Saved());
    }

    [Fact]
    public void ACharacterSwitchSavesToTheOldCharacterAndRestoresTheNewOne()
    {
        _store.SavePluginCanvasPosition("bob", "p/hud", new PluginCanvasPosition("Center", 3, 4));
        var canvas = new FakeCanvas("p/hud");
        using var persistence = Create();
        persistence.Add(canvas);
        persistence.RestoreAll();

        canvas.Offset = new PluginPoint(50, 60);
        _character = "bob";
        persistence.RestoreAll();

        Assert.Equal(new PluginCanvasPosition("TopLeft", 50, 60), Saved(character: "alice"));
        Assert.Equal(PluginCanvasAnchor.Center, canvas.Anchor);
        Assert.Equal(new PluginPoint(3, 4), canvas.Offset);
        persistence.Tick(1.0);
        Assert.Equal(new PluginCanvasPosition("Center", 3, 4), Saved(character: "bob"));
    }

    [Fact]
    public void ACanvasAddedAfterTheRestoreIsRestoredAtOnce()
    {
        _store.SavePluginCanvasPosition("alice", "p/hud", new PluginCanvasPosition("TopRight", -5, 6));
        using var persistence = Create();
        persistence.RestoreAll();

        var canvas = new FakeCanvas("p/hud");
        persistence.Add(canvas);

        Assert.Equal(PluginCanvasAnchor.TopRight, canvas.Anchor);
        Assert.Equal(new PluginPoint(-5, 6), canvas.Offset);
    }

    [Fact]
    public void ACanvasAddedAfterTheCharacterChangedWaitsForTheNextRestore()
    {
        _store.SavePluginCanvasPosition("bob", "p/hud", new PluginCanvasPosition("TopRight", -5, 6));
        using var persistence = Create();
        persistence.RestoreAll();
        _character = "bob";

        var canvas = new FakeCanvas("p/hud");
        persistence.Add(canvas);

        Assert.Equal(PluginCanvasAnchor.TopLeft, canvas.Anchor);
    }

    [Fact]
    public void ARegistrationIsKeyedByOwnerAndCanvasId()
    {
        var registry = new AcDream.App.Plugins.BufferedUiRegistry();
        IRememberedCanvas canvas = (IRememberedCanvas)registry.RegisterCanvas(
            new PluginUiOwner("aethrix", "Aethrix"), new PluginCanvasDescriptor("hud", 30, 40), _ => { });
        Assert.Equal("aethrix/hud", canvas.Key);
        Assert.Equal((30, 40), canvas.Size);
        Assert.False(canvas.IsHoldingPointer);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter PluginCanvasPositionPersistenceTests`
Expected: build error, `PluginCanvasPositionPersistence` / `IRememberedCanvas` not defined.

- [ ] **Step 3: Implement the persistence**

```csharp
using System;
using System.Collections.Generic;
using AcDream.App.UI.Layout;
using AcDream.Plugin.Abstractions;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.UI;

/// <summary>A plugin canvas as the position persistence sees it.</summary>
internal interface IRememberedCanvas
{
    /// <summary>The canvas's key in the settings file: <c>"&lt;pluginId&gt;/&lt;canvasId&gt;"</c>.</summary>
    string Key { get; }

    PluginCanvasAnchor Anchor { get; set; }

    PluginPoint Offset { get; set; }

    (int Width, int Height) Size { get; }

    /// <summary>Whether the canvas holds a press right now, so a drag is still going.</summary>
    bool IsHoldingPointer { get; }
}

/// <summary>
/// Remembers where the player left each plugin canvas that asked for it
/// (<see cref="PluginCanvasDescriptor.RemembersPosition"/>), per character.
/// A plugin moves its canvas by setting its offset as often as it likes, so
/// a change is written only once the canvas has stayed put for
/// <see cref="SettleSeconds"/> with no press held on it; a canvas that goes
/// away, or a character switch, writes what is pending at once.
/// </summary>
internal sealed class PluginCanvasPositionPersistence : IDisposable
{
    internal const double SettleSeconds = 0.5;

    private sealed class Entry(IRememberedCanvas canvas)
    {
        public IRememberedCanvas Canvas { get; } = canvas;
        public (PluginCanvasAnchor Anchor, PluginPoint Offset) Baseline;
        public (PluginCanvasAnchor Anchor, PluginPoint Offset) Seen;
        public double ChangedAt;
    }

    private readonly SettingsStore? _store;
    private readonly Func<string> _characterKey;
    private readonly Func<(float Width, float Height)> _screenSize;
    private readonly List<Entry> _entries = [];
    private string? _restoredCharacter;
    private double _now;
    private bool _disposed;

    public PluginCanvasPositionPersistence(
        SettingsStore? store,
        Func<string> characterKey,
        Func<(float Width, float Height)> screenSize)
    {
        _store = store;
        _characterKey = characterKey ?? throw new ArgumentNullException(nameof(characterKey));
        _screenSize = screenSize ?? throw new ArgumentNullException(nameof(screenSize));
    }

    /// <summary>Whether a canvas registered with <paramref name="descriptor"/> is remembered.</summary>
    internal static bool Tracks(PluginCanvasDescriptor descriptor) =>
        descriptor.RemembersPosition && !descriptor.InMarkup;

    /// <summary>
    /// Starts remembering <paramref name="canvas"/>. Put back at once when a
    /// character's positions were restored and that character is still the
    /// one logged in; otherwise it waits for the next <see cref="RestoreAll"/>.
    /// </summary>
    public void Add(IRememberedCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (_disposed || _entries.Exists(e => ReferenceEquals(e.Canvas, canvas))) return;
        var entry = new Entry(canvas);
        Rebase(entry);
        _entries.Add(entry);
        if (_restoredCharacter is { } character && character == _characterKey())
            Restore(entry, character);
    }

    /// <summary>Stops remembering <paramref name="canvas"/>, writing a move not yet saved.</summary>
    public void Remove(IRememberedCanvas canvas)
    {
        int index = _entries.FindIndex(e => ReferenceEquals(e.Canvas, canvas));
        if (index < 0) return;
        Entry entry = _entries[index];
        _entries.RemoveAt(index);
        if (Current(entry) != entry.Baseline)
            Save(entry);
    }

    /// <summary>
    /// Puts back the logged-in character's saved positions, after writing
    /// what is still pending for the character before.
    /// </summary>
    public void RestoreAll()
    {
        if (_disposed) return;
        FlushPending();
        string character = _characterKey();
        _restoredCharacter = CanPersist(character) ? character : null;
        foreach (Entry entry in _entries)
        {
            if (_restoredCharacter is { } restored)
                Restore(entry, restored);
            else
                Rebase(entry);
        }
    }

    /// <summary>
    /// Once a frame: notices moves, and writes each one that has stayed put
    /// for <see cref="SettleSeconds"/> with no press held on the canvas.
    /// </summary>
    public void Tick(double deltaSeconds)
    {
        if (_disposed) return;
        _now += deltaSeconds;
        foreach (Entry entry in _entries)
        {
            var current = Current(entry);
            if (current != entry.Seen)
            {
                entry.Seen = current;
                entry.ChangedAt = _now;
            }
            if (current != entry.Baseline
                && _now - entry.ChangedAt >= SettleSeconds
                && !entry.Canvas.IsHoldingPointer)
                Save(entry);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        FlushPending();
        _entries.Clear();
        _disposed = true;
    }

    private void FlushPending()
    {
        foreach (Entry entry in _entries)
        {
            if (Current(entry) != entry.Baseline)
                Save(entry);
        }
    }

    private void Restore(Entry entry, string character)
    {
        PluginCanvasPosition? saved = null;
        try { saved = _store?.LoadPluginCanvasPosition(character, entry.Canvas.Key); }
        catch (Exception ex)
        {
            Console.WriteLine($"settings: plugin canvas position load failed [{entry.Canvas.Key}]: {ex.Message}");
        }
        if (saved is { } position
            && Enum.TryParse(position.Anchor, ignoreCase: false, out PluginCanvasAnchor anchor)
            && Enum.IsDefined(anchor))
        {
            (int width, int height) = entry.Canvas.Size;
            (float screenWidth, float screenHeight) = _screenSize();
            entry.Canvas.Anchor = anchor;
            entry.Canvas.Offset = PluginCanvasPlacement.ClampOnScreen(
                anchor, new PluginPoint(position.X, position.Y), width, height, screenWidth, screenHeight);
        }
        Rebase(entry);
    }

    private void Save(Entry entry)
    {
        var current = Current(entry);
        if (_restoredCharacter is not { } character || _store is null) return;
        try
        {
            _store.SavePluginCanvasPosition(character, entry.Canvas.Key,
                new PluginCanvasPosition(current.Anchor.ToString(), current.Offset.X, current.Offset.Y));
            entry.Baseline = current;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"settings: plugin canvas position save failed [{entry.Canvas.Key}]: {ex.Message}");
            entry.Baseline = current;
        }
    }

    private void Rebase(Entry entry)
    {
        entry.Baseline = Current(entry);
        entry.Seen = entry.Baseline;
        entry.ChangedAt = _now;
    }

    private static (PluginCanvasAnchor Anchor, PluginPoint Offset) Current(Entry entry) =>
        (entry.Canvas.Anchor, entry.Canvas.Offset);

    private static bool CanPersist(string key) =>
        !string.IsNullOrWhiteSpace(key)
        && !string.Equals(key, "default", StringComparison.OrdinalIgnoreCase);
}
```

Note: with no character restored, `Save` returns without moving the baseline, so `Tick` keeps finding the change and re-checking; that costs a comparison per frame and writes nothing. `Enum.TryParse` rejects numeric strings only when combined with `IsDefined`; the check covers that.

- [ ] **Step 4: Implement `IRememberedCanvas` on the registration** — in `PluginCanvasRegistration.cs` add `using AcDream.App.UI;`, change the class line to `internal sealed class PluginCanvasRegistration : IPluginCanvas, IRememberedCanvas`, and add:

```csharp
    string IRememberedCanvas.Key => $"{Owner.Id}/{CanvasId}";

    (int Width, int Height) IRememberedCanvas.Size => Size;
```

(`Anchor`, `Offset` are already public with get/set; `IsHoldingPointer` was added in Task 3 as `internal` — make it satisfy the interface with an explicit implementation `bool IRememberedCanvas.IsHoldingPointer => IsHoldingPointer;`.)

- [ ] **Step 5: Run to verify they pass**

Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter PluginCanvasPositionPersistenceTests`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.App/UI/PluginCanvasPositionPersistence.cs src/AcDream.App/Plugins/PluginCanvasRegistration.cs tests/AcDream.App.Tests/UI/PluginCanvasPositionPersistenceTests.cs
git commit -m "feat(ui): remember plugin canvas positions per character

Claude-Session: https://claude.ai/code/session_01Lm9T9oP2Hi5FEjwsLmXeyN"
```

---

### Task 5: Wire into `RetailUiRuntime` and document

**Goal:** The live interface tracks opted-in canvases, ticks the persistence, restores at `RestoreLayout`, and the plugin API docs describe the flag.

**Files:**
- Modify: `src/AcDream.App/UI/RetailUiRuntime.cs` (field ~line 380; construction after `_persistence` ~line 547; `Tick` ~line 735; `RestoreLayout` ~line 1185; `MountPluginCanvases` ~line 4325; `Dispose` ~line 5280)
- Modify: `docs/plugin-api.md` (new `### Remembered position` before `### Canvases in markup`, ~line 2082)

**Acceptance Criteria:**
- [ ] A screen canvas with `Tracks(descriptor)` true is added to the persistence after `CompleteCanvasMount`, and removed (flushing) in its teardown before the element comes down.
- [ ] `RestoreLayout()` calls `RestoreAll()` after the window restore; `Tick` calls `Tick(deltaSeconds)`; `Dispose` disposes it before `_persistence`.
- [ ] Clamping uses `Layout.UiOverlayHost.ViewportOf(Host.Root)` (the rectangle canvases are placed against).
- [ ] `docs/plugin-api.md` has the subsection with an example, the min contract `0.1.25-fork.7`, and the read-don't-cache note.
- [ ] Full `AcDream.App.Tests` and `AcDream.UI.Abstractions.Tests` pass (pre-existing unrelated failures, if any, listed with names, compared with `main`).

**Verify:** `~/.dotnet/dotnet build AcDream.slnx -c Debug && ~/.dotnet/dotnet test tests/AcDream.App.Tests && ~/.dotnet/dotnet test tests/AcDream.UI.Abstractions.Tests` → build succeeds; tests pass

**Steps:**

- [ ] **Step 1: Field and construction** — beside `private RetailWindowLayoutPersistence? _persistence;`:

```csharp
    private PluginCanvasPositionPersistence? _canvasPositions;
```

Inside the `{ var persistence = bindings.Persistence; ... }` block, after `_persistence.SetGameplayActive(...)`:

```csharp
            _canvasPositions = new PluginCanvasPositionPersistence(
                persistence?.Store,
                persistence?.CharacterKey ?? (() => "default"),
                () =>
                {
                    System.Numerics.Vector2 viewport = Layout.UiOverlayHost.ViewportOf(Host.Root);
                    return (viewport.X, viewport.Y);
                });
```

- [ ] **Step 2: Tick** — in `Tick(double deltaSeconds)`, right after the `MountPluginCanvases()` lines and before `Host.Tick(deltaSeconds);`:

```csharp
        _canvasPositions?.Tick(deltaSeconds);
```

- [ ] **Step 3: Restore** — in `RestoreLayout()`, after `_persistence?.RestoreAll();`:

```csharp
        // Root is sized to the screen above, so a restored canvas is kept
        // on the screen it will be drawn on.
        _canvasPositions?.RestoreAll();
```

- [ ] **Step 4: Track on mount** — in `MountPluginCanvases`, the screen-canvas branch becomes:

```csharp
                RequireCanvasServices();
                Layout.PluginCanvasStack stack = CanvasStack(canvas.Layer);
                Layout.PluginCanvasElement element = CreateCanvasElement(canvas, hosted: false);
                stack.Add(element);
                _markupCanvases?.ScreenCanvasMounted();
                bool remembered = PluginCanvasPositionPersistence.Tracks(canvas.Descriptor);
                plugins.CompleteCanvasMount(canvas, () =>
                {
                    if (remembered) _canvasPositions?.Remove(canvas);
                    Layout.PluginCanvasStack.Remove(element);
                    element.ReleaseTargets();
                });
                if (remembered && canvas.IsMounted)
                    _canvasPositions?.Add(canvas);
```

(`IsMounted` is false when the plugin disposed the canvas between drain and mount — `CompleteCanvasMount` ran the teardown at once — so it is never added.)

- [ ] **Step 5: Dispose** — in `Dispose()`, add `() => _canvasPositions?.Dispose(),` immediately before `() => _persistence?.Dispose(),`. Read the surrounding lines first; the list is a sequence of lambdas run in order.

- [ ] **Step 6: Docs** — insert before `### Canvases in markup` in `docs/plugin-api.md`:

````markdown
### Remembered position

A canvas the player can move can ask the client to remember where it was
left. Register it with `RemembersPosition` and move it as usual by setting
`Offset` (and `Anchor`, if you change it):

```csharp
IPluginCanvas hud = host.Ui.RegisterCanvas(
    new PluginCanvasDescriptor("hud", 200, 60)
    {
        Anchor = PluginCanvasAnchor.TopLeft,
        Offset = new PluginPoint(40, 40),   // where it starts the first time
        AcceptsPointerInput = true,
        RemembersPosition = true,
    },
    painter => DrawHud(painter));
```

The client saves the anchor and offset for the logged-in character and puts
them back when that character next enters the world; a canvas registered
while the character is already in the world gets its position at once. A
position put back is kept on screen. A move is saved once the canvas has
stayed put for half a second with no press held on it, so a drag is saved
once, after the release; a move made in code is saved the same way.

Read `Anchor` and `Offset` when you need them rather than keeping your own
copy: the client may change them when a character enters the world.
Nothing is saved or put back before a character has logged in, and the
flag does nothing for a canvas registered with `InMarkup` (its element
places it). `RemembersPosition` needs contract `0.1.25-fork.7`; an older
host ignores it, and a host without a window keeps the flag.
````

- [ ] **Step 7: Build and run the suites**

Run: `~/.dotnet/dotnet build AcDream.slnx -c Debug && ~/.dotnet/dotnet test tests/AcDream.App.Tests && ~/.dotnet/dotnet test tests/AcDream.UI.Abstractions.Tests`
Expected: build succeeds; all pass. If anything fails, run the same failing test on `main` (`git stash` is not needed — use `git worktree add` of `main` in the scratchpad) to tell pre-existing failures from new ones, and report both lists.

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.App/UI/RetailUiRuntime.cs docs/plugin-api.md
git commit -m "feat(ui): the interface remembers opted-in canvas positions

Claude-Session: https://claude.ai/code/session_01Lm9T9oP2Hi5FEjwsLmXeyN"
```

---

### Task 6: Live check on the local ACE server

**Goal:** See a remembered canvas come back where it was left after logout/login on the real client.

**Files:**
- none committed (a throwaway opt-in on a sample/dev plugin, or `samples/AcDream.Plugins.CanvasDemo` if it has a draggable canvas — read it first)

**Acceptance Criteria:**
- [ ] With a draggable canvas registered with `RemembersPosition = true`, dragging it, logging out and back in on 10.10.20.20 (via `~/OpenAC-dev/dev-client.env`, never Dreamweave) shows it where it was left.
- [ ] The settings file has a `pluginCanvasPositions` entry for that character and canvas.
- [ ] The user confirms in chat.

**Verify:** `grep -A4 pluginCanvasPositions <settings file path>` → shows the canvas key with the dragged anchor/offset

**Steps:**

- [ ] **Step 1:** Find a draggable canvas sample (`grep -rn "Offset =" samples/`); if none drags, add a temporary Ctrl-drag to the CanvasDemo locally (not committed).
- [ ] **Step 2:** Build and launch the dev client per the `run` skill / `~/OpenAC-dev/dev-client.env`, log in, drag the canvas, wait a second, log out, log in.
- [ ] **Step 3:** Ask the user to confirm the canvas came back where it was left; grep the settings file for the entry.
- [ ] **Step 4:** Revert any temporary sample change (`git status` clean apart from committed work).

---

## After the plan

Finishing (merge to fork main, push, update Aethrix `docs/api-gaps.md` #52 to "(fixed in the fork, <merge commit>)" with the minimum contract `0.1.25-fork.7`, and update memory) goes through `superpowers-extended-cc:finishing-a-development-branch`.
