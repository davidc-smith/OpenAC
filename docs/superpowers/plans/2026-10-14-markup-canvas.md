# Markup Canvas Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A `<canvas id="...">` markup element hosts a plugin canvas registered with `PluginCanvasDescriptor.InMarkup = true`, so a panel can hold a drawing laid out by flex.

**Architecture:**
- The plugin keeps the ordinary `IPluginCanvas` handle.
- `<canvas>` builds a `UiMarkupCanvas`, an empty flex leaf.
- A pairing table owned by `RetailUiRuntime` (`PluginMarkupCanvases`) puts a `PluginCanvasElement` in hosted mode inside the matching `UiMarkupCanvas`.
- In hosted mode the element fills its host and writes the laid-out size back into the registration. The existing retained-paint, input, focus and outside-press code is reused unchanged.

**Tech Stack:** C# / .NET 10, xUnit, the App's retained UI tree (`UiElement`, `UiRoot`, flex in `UI/Layout/Flex`), the plugin contract in `AcDream.Plugin.Abstractions`.

**Spec:** `docs/superpowers/specs/2026-10-14-markup-canvas-design.md` (branch `docs/markup-canvas-spec`)

## Global Constraints

- Work on branch `feat/markup-canvas`, created from fork `main` (fa682513 or later). Never put `docs/superpowers/` on that branch.
- Use the `~/.dotnet` SDK: prefix every dotnet command with `PATH=$HOME/.dotnet:$PATH`.
  - A wrong-SDK run churns lock files (Bake/Content osx-arm64, Launcher neutral). Never commit those.
- Contract version: `ForkContractVersion` goes from `0.1.25-fork.4` to exactly `0.1.25-fork.5`. Do not touch `<Version>`.
- `InMarkup` is an init-only `bool` on `PluginCanvasDescriptor`, default `false`. No other contract member is added.
- Pairing is by (plugin owner id, canvas id), ordinal comparison.
  - The first `<canvas>` mounted (in mount order) hosts a canvas.
  - A duplicate id in one window is a `FormatException`, and so is a `<canvas>` without an id.
- A hosted canvas's size is its host's laid-out size rounded to whole pixels: `(int)MathF.Floor(v + 0.5f)`.
  - Below 1×1 the canvas keeps its last size.
  - `TryResize` returns `false` for an `InMarkup` canvas.
- Log lines go through the `report` delegates. Each "waiting" or "shows nothing" line is reported at most once per registration or per `<canvas>` element.
- Match the surrounding code: XML-doc `<summary>` prose in full sentences, the file-scoped namespaces used nearby, and `internal sealed` classes.
- Commit messages end with:
  `Claude-Session: https://claude.ai/code/session_01Lm9T9oP2Hi5FEjwsLmXeyN`

**User decisions (already made):**
- Approach A: register the canvas with `InMarkup = true` and place it with `<canvas id>` markup. The binding-style element was rejected.
- Scope: the client feature only. The Aethrix Delve page is a separate later task.
- Fork-only for now, with no upstream PR.
- After the feature is on fork `main` and pushed, update Aethrix `docs/api-gaps.md` #62. Include the minimum contract version (`0.1.25-fork.5`) and the specifics a plugin needs to use it.

---

## File structure

| File | Responsibility |
|---|---|
| `src/AcDream.Plugin.Abstractions/PluginCanvas.cs` (modify) | `InMarkup` flag; doc notes on `TryResize`/`IsAvailable` |
| `src/AcDream.Plugin.Abstractions/AcDream.Plugin.Abstractions.csproj` (modify) | `ForkContractVersion` → `0.1.25-fork.5` |
| `src/AcDream.App/Plugins/PluginCanvasRegistration.cs` (modify) | `IsHosted`, `SetLaidOutSize`, `TryResize` refusal, `IsAvailable` rule |
| `src/AcDream.App/Plugins/BufferedUiRegistry.cs` (modify) | `FindCanvas(ownerId, canvasId)` |
| `src/AcDream.App/UI/UiMarkupCanvas.cs` (create) | The `<canvas>` element: an empty clipping leaf that hosts one canvas element and sizes it |
| `src/AcDream.App/UI/Layout/PluginCanvasElement.cs` (modify) | Hosted mode: fill the host instead of anchoring |
| `src/AcDream.App/UI/MarkupDocument.cs` (modify) | `canvas` case; collect `Canvases` and reject duplicates |
| `src/AcDream.App/UI/MarkupContentSize.cs` (modify) | `UiMarkupCanvas` measures 0×0 |
| `src/AcDream.App/UI/MarkupWindow.cs` (modify) | `Canvases` list |
| `src/AcDream.App/UI/Layout/PluginMarkupCanvases.cs` (create) | Pairing table: waiting canvases ↔ `<canvas>` elements, host/unhost, outside presses |
| `src/AcDream.App/UI/RetailUiRuntime.cs` (modify) | Route `InMarkup` canvases to the table; register/unregister window canvases; press reporting |
| `tests/AcDream.Plugin.Tests/PluginCanvasContractTests.cs` (modify) | `InMarkup` default |
| `tests/AcDream.App.Tests/Plugins/BufferedUiRegistryCanvasTests.cs` (modify) | Registration rules |
| `tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.Markup.cs` (create) | Hosted element behaviour |
| `tests/AcDream.App.Tests/UI/MarkupCanvasTests.cs` (create) | Parser and layout of `<canvas>` |
| `tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.MarkupPairing.cs` (create) | Pairing table end to end with markup windows |
| `docs/plugin-ui-markup.md`, `docs/plugin-api.md` (modify) | Docs |

Test command used throughout (from the repo root):

```bash
PATH=$HOME/.dotnet:$PATH dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~<Name>"
```

---

### Task 0: Branch

**Goal:** Create `feat/markup-canvas` from fork `main`.

**Files:** none

**Acceptance Criteria:**
- [ ] `git branch --show-current` prints `feat/markup-canvas`
- [ ] The branch starts at fork `main`'s head

**Verify:** `git log --oneline -1` shows the same commit as `git log --oneline -1 main`.

**Steps:**

- [ ] **Step 1: Create the branch**

```bash
git switch main && git pull --ff-only origin main && git switch -c feat/markup-canvas
```

---

### Task 1: Contract flag `InMarkup` and version bump

**Goal:** `PluginCanvasDescriptor.InMarkup` exists, defaults to false, and is documented, and the fork contract version is `0.1.25-fork.5`.

**Files:**
- Modify: `src/AcDream.Plugin.Abstractions/PluginCanvas.cs` (after `WantsOutsidePresses`, ~line 139; `IsAvailable` doc ~608; `TryResize` doc ~611-627)
- Modify: `src/AcDream.Plugin.Abstractions/AcDream.Plugin.Abstractions.csproj:39`
- Test: `tests/AcDream.Plugin.Tests/PluginCanvasContractTests.cs`

**Acceptance Criteria:**
- [ ] `new PluginCanvasDescriptor("map", 10, 10).InMarkup` is false, and `with { InMarkup = true }` reads true
- [ ] The XML docs on `InMarkup` describe sizing, the ignored members, visibility, pairing and lifetime
- [ ] The `TryResize` docs say it answers false for an `InMarkup` canvas
- [ ] `ForkContractVersion` is `0.1.25-fork.5`

**Verify:** `PATH=$HOME/.dotnet:$PATH dotnet test tests/AcDream.Plugin.Tests --filter "FullyQualifiedName~PluginCanvasContractTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing test.** Add to `PluginCanvasContractTests`:

```csharp
    [Fact]
    public void InMarkupIsOffByDefaultAndKeptWhenSet()
    {
        var descriptor = new PluginCanvasDescriptor("map", 10, 10);
        Assert.False(descriptor.InMarkup);
        Assert.True((descriptor with { InMarkup = true }).InMarkup);
    }
```

- [ ] **Step 2: Run it.** Expected: a compile error, because `InMarkup` is not defined.

- [ ] **Step 3: Add the property** after `WantsOutsidePresses` in `PluginCanvasDescriptor`:

```csharp
    /// <summary>
    /// Whether the canvas sits inside one of the plugin's markup windows
    /// instead of on the screen by itself. On, a <c>&lt;canvas id="..."&gt;</c>
    /// element whose id is this canvas's <see cref="CanvasId"/> hosts it: the
    /// window's layout decides where it is and how large (<c>w</c>, <c>h</c>,
    /// <c>grow</c> and the other flex item attributes), and the canvas fills
    /// that rectangle, rounded to whole pixels. <see cref="Width"/> and
    /// <see cref="Height"/> are its size only until it is first laid out;
    /// after that <see cref="IPluginCanvas.Width"/> and
    /// <see cref="IPluginCanvas.Height"/> report the laid-out size, and a
    /// change of it repaints the canvas. <see cref="IPluginCanvas.TryResize"/>
    /// answers false, and <see cref="Anchor"/>, <see cref="Offset"/>,
    /// <see cref="Layer"/> and <see cref="ZOrder"/> are kept but ignored.
    /// The canvas is drawn and takes input only while
    /// <see cref="IPluginCanvas.IsVisible"/> is true and its element is shown:
    /// its window open, its tab or group not hidden.
    /// <see cref="IPluginCanvas.IsAvailable"/> is true only while an element
    /// hosts it. Either may come first: a canvas waits for its element, and
    /// an element draws nothing until its canvas is registered. When two of
    /// the plugin's windows have an element with the same id, the first one
    /// mounted hosts the canvas, and the other takes over if that window goes
    /// away. When the window goes, the canvas waits for another element;
    /// pointer, keyboard and outside-press input work as for any canvas, in
    /// the canvas's own pixels. Off, the default, nothing changes. On a host
    /// without a window the flag is kept and nothing is drawn.
    /// </summary>
    public bool InMarkup { get; init; }
```

- [ ] **Step 4: Extend the `TryResize` docs.** Replace the `<returns>` text of `IPluginCanvas.TryResize` with:

```csharp
    /// <returns>
    /// True when the canvas now has that size. False for a width or height
    /// below 1, on a disposed canvas, for a canvas registered with
    /// <see cref="PluginCanvasDescriptor.InMarkup"/> (its element decides its
    /// size), and on a host that cannot resize, which is what the default
    /// implementation answers; the canvas is unchanged.
    /// </returns>
```

- [ ] **Step 5: Extend the `IsAvailable` docs.** Replace its summary with:

```csharp
    /// <summary>Whether the host draws this canvas at all: false without a window, and for an <see cref="PluginCanvasDescriptor.InMarkup"/> canvas while no markup element hosts it.</summary>
```

- [ ] **Step 6: Bump the version.** In the csproj, change `<ForkContractVersion>0.1.25-fork.4</ForkContractVersion>` to `<ForkContractVersion>0.1.25-fork.5</ForkContractVersion>`.

- [ ] **Step 7: Run the test.** Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.Plugin.Abstractions tests/AcDream.Plugin.Tests/PluginCanvasContractTests.cs
git commit -m "feat(plugins): PluginCanvasDescriptor.InMarkup, contract 0.1.25-fork.5

Claude-Session: https://claude.ai/code/session_01Lm9T9oP2Hi5FEjwsLmXeyN"
```

---

### Task 2: Registration rules for markup canvases

**Goal:** The registration supports being hosted:
- It refuses `TryResize`.
- It takes a laid-out size from the host.
- It reports `IsAvailable` only while hosted.
- The registry can look a canvas up by owner and id.

**Files:**
- Modify: `src/AcDream.App/Plugins/PluginCanvasRegistration.cs` (fields ~line 20-30; `TryResize` ~153; `IsAvailable` ~163)
- Modify: `src/AcDream.App/Plugins/BufferedUiRegistry.cs` (after `CanvasCount`, ~line 560)
- Test: `tests/AcDream.App.Tests/Plugins/BufferedUiRegistryCanvasTests.cs`

**Acceptance Criteria:**
- [ ] `TryResize` returns false for an `InMarkup` canvas, and its size is unchanged
- [ ] `SetLaidOutSize(w, h)` changes the size and invalidates. The same size again does not invalidate. Below 1 it returns false.
- [ ] An `InMarkup` canvas that is mounted is not `IsAvailable` until `IsHosted` is true
- [ ] `FindCanvas` finds only the given owner's canvas, and returns null after dispose

**Verify:** `PATH=$HOME/.dotnet:$PATH dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~BufferedUiRegistryCanvasTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests.** Add to `BufferedUiRegistryCanvasTests`:

```csharp
    private static PluginCanvasDescriptor Markup(string id) => new(id, 10, 10) { InMarkup = true };

    [Fact]
    public void AMarkupCanvasRefusesTryResizeButTakesItsLaidOutSize()
    {
        var registry = new BufferedUiRegistry();
        var canvas = (PluginCanvasRegistration)registry.RegisterCanvas(A, Markup("map"), Paint);
        Assert.True(canvas.TakeInvalidation()); // the first paint every canvas starts with

        Assert.False(canvas.TryResize(40, 30));
        Assert.Equal((10, 10), (canvas.Width, canvas.Height));
        Assert.False(canvas.TakeInvalidation());

        Assert.True(canvas.SetLaidOutSize(40, 30));
        Assert.Equal((40, 30), (canvas.Width, canvas.Height));
        Assert.True(canvas.TakeInvalidation());

        Assert.True(canvas.SetLaidOutSize(40, 30));
        Assert.False(canvas.TakeInvalidation());

        Assert.False(canvas.SetLaidOutSize(0, 30));
        Assert.Equal((40, 30), (canvas.Width, canvas.Height));
    }

    [Fact]
    public void AMarkupCanvasIsAvailableOnlyWhileHosted()
    {
        var registry = new BufferedUiRegistry();
        var canvas = (PluginCanvasRegistration)registry.RegisterCanvas(A, Markup("map"), Paint);
        Assert.Same(canvas, Assert.Single(registry.DrainCanvases()));
        registry.CompleteCanvasMount(canvas, () => { });

        Assert.False(canvas.IsAvailable);
        canvas.IsHosted = true;
        Assert.True(canvas.IsAvailable);
        canvas.IsHosted = false;
        Assert.False(canvas.IsAvailable);
    }

    [Fact]
    public void APlainCanvasIsAvailableOnceMountedAsBefore()
    {
        var registry = new BufferedUiRegistry();
        var canvas = (PluginCanvasRegistration)registry.RegisterCanvas(A, Canvas("hud"), Paint);
        registry.DrainCanvases();
        registry.CompleteCanvasMount(canvas, () => { });
        Assert.True(canvas.IsAvailable);
        Assert.True(canvas.TryResize(20, 20));
    }

    [Fact]
    public void FindCanvasLooksOnlyAtTheGivenPlugin()
    {
        var registry = new BufferedUiRegistry();
        IPluginCanvas mine = registry.RegisterCanvas(A, Markup("map"), Paint);
        registry.RegisterCanvas(B, Canvas("other"), Paint);

        Assert.Same(mine, registry.FindCanvas("a", "map"));
        Assert.Null(registry.FindCanvas("b", "map"));
        Assert.Null(registry.FindCanvas("a", "other"));

        mine.Dispose();
        Assert.Null(registry.FindCanvas("a", "map"));
    }
```

- [ ] **Step 2: Run them.** Expected: a compile error (`SetLaidOutSize`, `IsHosted` and `FindCanvas` are missing).

- [ ] **Step 3: Implement in `PluginCanvasRegistration`.** Add a field next to the others:

```csharp
    private volatile bool _hosted;
```

Replace `TryResize` with:

```csharp
    public bool TryResize(int width, int height) =>
        !Descriptor.InMarkup && Resize(width, height);

    /// <summary>
    /// Takes the size the canvas's markup element was laid out at. The same
    /// write as <see cref="TryResize"/>, which a markup canvas refuses because
    /// its element, not the plugin, decides its size.
    /// </summary>
    internal bool SetLaidOutSize(int width, int height) => Resize(width, height);

    private bool Resize(int width, int height)
    {
        if (width < 1 || height < 1 || _paint is null)
            return false;
        long size = Pack(width, height);
        if (Interlocked.Exchange(ref _size, size) != size)
            Invalidate();
        return true;
    }

    /// <summary>
    /// Whether a markup element hosts the canvas now; set by the interface's
    /// pairing table. Always false for a canvas not registered in markup.
    /// </summary>
    internal bool IsHosted
    {
        get => _hosted;
        set => _hosted = value;
    }
```

Replace `IsAvailable` with:

```csharp
    public bool IsAvailable =>
        IsMounted && !IsDropped && _paint is not null && (!Descriptor.InMarkup || _hosted);
```

- [ ] **Step 4: Implement `FindCanvas` in `BufferedUiRegistry`** (after `CanvasCount`):

```csharp
    /// <summary>
    /// The canvas <paramref name="ownerId"/> registered as
    /// <paramref name="canvasId"/>, or null. Read by the interface to say why
    /// a markup <c>&lt;canvas&gt;</c> shows nothing.
    /// </summary>
    internal PluginCanvasRegistration? FindCanvas(string ownerId, string canvasId)
    {
        lock (_gate)
        {
            foreach (PluginCanvasRegistration registration in _canvases.Values)
            {
                if (registration.Owner.Id.Equals(ownerId, StringComparison.Ordinal)
                    && registration.CanvasId.Equals(canvasId, StringComparison.Ordinal))
                    return registration;
            }
            return null;
        }
    }
```

- [ ] **Step 5: Run the tests.** Expected: all `BufferedUiRegistryCanvasTests` pass.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.App/Plugins tests/AcDream.App.Tests/Plugins/BufferedUiRegistryCanvasTests.cs
git commit -m "feat(plugins): registration rules for canvases hosted in markup

Claude-Session: https://claude.ai/code/session_01Lm9T9oP2Hi5FEjwsLmXeyN"
```

---

### Task 3: `UiMarkupCanvas` and the canvas element's hosted mode

**Goal:**
- A `PluginCanvasElement` created with `hosted: true` fills its `UiMarkupCanvas` host instead of anchoring itself, and pushes the host's rounded size into the registration.
- It paints at that size, and repaints once when it changes.
- It hides and hit-tests with its host.

**Files:**
- Create: `src/AcDream.App/UI/UiMarkupCanvas.cs`
- Modify: `src/AcDream.App/UI/Layout/PluginCanvasElement.cs` (constructor ~line 108; `Layout()` ~514)
- Test: `tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.Markup.cs` (create; a partial of `PluginCanvasElementTests`, so it reuses its `Harness`)

**Acceptance Criteria:**
- [ ] A host at (40, 30) sized 120.4×80.6 paints the canvas at 120×81. The element sits at (0, 0) in the host at 120×81.
- [ ] Changing the host to 200×50 repaints once at 200×50, with one new render target of 200×50. A frame with no change paints nothing.
- [ ] A host at 0×50 keeps the last size and paints nothing new
- [ ] A press at screen (50, 40) reaches the pointer handler at canvas-local (10, 10)
- [ ] A hidden host gets no paint and no hit. Showing it again paints.
- [ ] Non-hosted canvases are unchanged: all existing `PluginCanvasElementTests`/`PluginCanvasPointerTests` pass

**Verify:** `PATH=$HOME/.dotnet:$PATH dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~PluginCanvasElementTests|FullyQualifiedName~PluginCanvasPointerTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests.** Create `tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.Markup.cs`:

```csharp
using System.Collections.Generic;
using AcDream.App.Plugins;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI;
using AcDream.App.UI.Layout;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.UI.Layout;

/// <summary>
/// A canvas hosted by a markup <c>&lt;canvas&gt;</c> element: it fills its
/// host, rounded to whole pixels, is painted at that size and again when it
/// changes, keeps its last size while the host has no room, and is hidden and
/// hit-tested with its host.
/// </summary>
public sealed partial class PluginCanvasElementTests
{
    private static PluginCanvasRegistration RegisterInMarkup(
        Harness harness, string id, Action<IPluginPainter> paint, bool pointer = false, bool outside = false)
    {
        var registration = (PluginCanvasRegistration)harness.Registry.RegisterCanvas(
            harness.Owner,
            new PluginCanvasDescriptor(id, 50, 50)
            {
                InMarkup = true,
                AcceptsPointerInput = pointer,
                WantsOutsidePresses = outside,
            },
            paint);
        Assert.Contains(registration, harness.Registry.DrainCanvases());
        return registration;
    }

    private static PluginCanvasElement HostedElement(Harness harness, PluginCanvasRegistration registration) =>
        new(registration, harness.Surface, () => harness.Registry.FindImages(harness.Owner), harness.Reports.Add,
            harness.Clock.Read, fonts: () => harness.Registry.FindFonts(harness.Owner), hosted: true);

    private static (PluginCanvasRegistration Registration, UiMarkupCanvas Host, List<(int Width, int Height)> Sizes)
        HostedCanvas(Harness harness, bool pointer = false)
    {
        var sizes = new List<(int Width, int Height)>();
        PluginCanvasRegistration registration =
            RegisterInMarkup(harness, "map", painter => sizes.Add((painter.Width, painter.Height)), pointer);
        // No anchors, as under a flex container: the test sets the host's size by hand.
        var host = new UiMarkupCanvas("map")
        {
            Left = 40f, Top = 30f, Width = 120.4f, Height = 80.6f, Anchors = AnchorEdges.None,
        };
        harness.Root.AddChild(host);
        host.Host(HostedElement(harness, registration));
        return (registration, host, sizes);
    }

    [Fact]
    public void AHostedCanvasFillsItsHostRoundedToWholePixels()
    {
        var harness = new Harness();
        (PluginCanvasRegistration registration, UiMarkupCanvas host, List<(int, int)> sizes) = HostedCanvas(harness);

        harness.Frame();

        Assert.Equal([(120, 81)], sizes);
        Assert.Equal((120, 81), (registration.Width, registration.Height));
        var element = Assert.IsType<PluginCanvasElement>(host.Hosted);
        Assert.Equal((0f, 0f, 120f, 81f), (element.Left, element.Top, element.Width, element.Height));
    }

    [Fact]
    public void AHostResizeRepaintsOnceAtTheNewSize()
    {
        var harness = new Harness();
        (PluginCanvasRegistration registration, UiMarkupCanvas host, List<(int, int)> sizes) = HostedCanvas(harness);
        harness.Frame();
        harness.Device.Clear();

        host.Width = 200f;
        host.Height = 50f;
        harness.Frame();
        harness.Frame();

        Assert.Equal([(120, 81), (200, 50)], sizes);
        GpuRecordedRenderTargetCreate created = Assert.Single(harness.Device.OfKind<GpuRecordedRenderTargetCreate>());
        Assert.Equal((200, 50), (created.Description.Width, created.Description.Height));
    }

    [Fact]
    public void AHostWithNoRoomKeepsTheLastSizeAndPaintsNothingNew()
    {
        var harness = new Harness();
        (PluginCanvasRegistration registration, UiMarkupCanvas host, List<(int, int)> sizes) = HostedCanvas(harness);
        harness.Frame();

        host.Width = 0f;
        harness.Frame();

        Assert.Single(sizes);
        Assert.Equal((120, 81), (registration.Width, registration.Height));
    }

    [Fact]
    public void APressOnAHostedCanvasArrivesInCanvasPixels()
    {
        var harness = new Harness();
        (PluginCanvasRegistration registration, _, _) = HostedCanvas(harness, pointer: true);
        var events = new List<PluginPointerEvent>();
        registration.PointerHandler = events.Add;
        harness.Frame();

        harness.Root.OnMouseDown(UiMouseButton.Left, 50, 40);
        harness.Root.OnMouseUp(UiMouseButton.Left, 50, 40);

        Assert.Equal(
            [(PluginPointerEventKind.Down, new PluginPoint(10, 10)), (PluginPointerEventKind.Up, new PluginPoint(10, 10))],
            events.ConvertAll(e => (e.Kind, e.Position)));
    }

    [Fact]
    public void AHiddenHostHidesItsCanvas()
    {
        var harness = new Harness();
        (PluginCanvasRegistration registration, UiMarkupCanvas host, List<(int, int)> sizes) =
            HostedCanvas(harness, pointer: true);
        var events = new List<PluginPointerEvent>();
        registration.PointerHandler = events.Add;
        host.Visible = false;

        harness.Frame();
        harness.Root.OnMouseDown(UiMouseButton.Left, 50, 40);
        harness.Root.OnMouseUp(UiMouseButton.Left, 50, 40);
        Assert.Empty(sizes);
        Assert.Empty(events);

        host.Visible = true;
        harness.Frame();
        Assert.Equal([(120, 81)], sizes);
    }

    [Fact]
    public void UnhostingGivesTheElementBack()
    {
        var harness = new Harness();
        (_, UiMarkupCanvas host, _) = HostedCanvas(harness);
        harness.Frame();
        var element = Assert.IsType<PluginCanvasElement>(host.Hosted);

        Assert.Same(element, host.Unhost());

        Assert.Null(host.Hosted);
        Assert.Null(element.Parent);
        Assert.Null(host.Unhost());
    }
}
```

- [ ] **Step 2: Run them.** Expected: a compile error (`UiMarkupCanvas` and the `hosted` parameter don't exist).

- [ ] **Step 3: Create `src/AcDream.App/UI/UiMarkupCanvas.cs`:**

```csharp
using AcDream.App.UI.Layout;

namespace AcDream.App.UI;

/// <summary>
/// A markup <c>&lt;canvas id="..."&gt;</c>: an empty rectangle the window's
/// layout places and sizes, which hosts the plugin canvas registered under
/// the same id with <c>InMarkup</c>. It draws nothing itself and lets clicks
/// through; the hosted canvas element fills it and answers for itself. It
/// clips, so a canvas whose host has no room shows nothing.
/// </summary>
internal sealed class UiMarkupCanvas : UiElement
{
    internal UiMarkupCanvas(string canvasId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canvasId);
        CanvasId = canvasId;
        ClickThrough = true;
    }

    /// <summary>The plugin's canvas id this element hosts.</summary>
    internal string CanvasId { get; }

    /// <summary>The canvas element shown here, or null while no canvas is hosted.</summary>
    internal PluginCanvasElement? Hosted { get; private set; }

    protected override bool ClipsChildren => true;

    /// <summary>Shows <paramref name="canvas"/> here, sized to this element.</summary>
    internal void Host(PluginCanvasElement canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (Hosted is not null)
            throw new InvalidOperationException($"<canvas id=\"{CanvasId}\"> already hosts a canvas");
        Hosted = canvas;
        AddChild(canvas);
        canvas.FillHost(Width, Height);
    }

    /// <summary>
    /// Takes the hosted element out of the tree and returns it, or null when
    /// none was hosted. The caller gives its targets back.
    /// </summary>
    internal PluginCanvasElement? Unhost()
    {
        PluginCanvasElement? canvas = Hosted;
        if (canvas is null) return null;
        Hosted = null;
        RemoveChild(canvas);
        return canvas;
    }

    // Children are laid out just before they are drawn, after this element's
    // own layout placed it; the canvas follows the size it was given.
    private protected override void LayoutChildren() => Hosted?.FillHost(Width, Height);
}
```

- [ ] **Step 4: Add hosted mode to `PluginCanvasElement`.**

4a. Add the constructor parameter `bool hosted = false` (last), a field and a property:

```csharp
    private readonly bool _hosted;
```

```csharp
        Func<bool>? keyboardCaptured = null,
        bool hosted = false)
    {
        _hosted = hosted;
```

```csharp
    /// <summary>True for a canvas a markup <c>&lt;canvas&gt;</c> hosts: it fills its host instead of anchoring.</summary>
    internal bool IsHosted => _hosted;
```

4b. At the top of `Layout()`, before the existing body:

```csharp
        if (_hosted)
        {
            FillHost(Parent?.Width ?? 0f, Parent?.Height ?? 0f);
            return;
        }
```

4c. Add `FillHost` after `Layout()`:

```csharp
    /// <summary>
    /// Fills a host of <paramref name="hostWidth"/> by
    /// <paramref name="hostHeight"/>: the canvas takes that size rounded to
    /// whole pixels, which repaints it when it changed. A host under one pixel
    /// either way leaves the canvas at its last size; the host clips it away.
    /// </summary>
    internal void FillHost(float hostWidth, float hostHeight)
    {
        Left = 0f;
        Top = 0f;
        int width = (int)MathF.Floor(hostWidth + 0.5f);
        int height = (int)MathF.Floor(hostHeight + 0.5f);
        if (width >= 1 && height >= 1)
            _registration.SetLaidOutSize(width, height);
        (int sizedWidth, int sizedHeight) = _registration.Size;
        Width = sizedWidth;
        Height = sizedHeight;
    }
```

`FollowSize()` in `OnDraw` then sees the new registration size, gives the targets back and repaints in the same draw. It already does this for `TryResize`, so it needs no change.

- [ ] **Step 5: Run the tests.** Expected: the new `Markup` tests and all existing `PluginCanvasElementTests` and `PluginCanvasPointerTests` pass.
  - If `AHostedCanvasFillsItsHostRoundedToWholePixels` sees `(50, 50)` first: `Host()` was called before the host had its size. Here it is called after, so that should not happen. Check that `FillHost` runs in `Host()`.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.App/UI/UiMarkupCanvas.cs src/AcDream.App/UI/Layout/PluginCanvasElement.cs tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.Markup.cs
git commit -m "feat(ui): a plugin canvas can fill a markup host instead of anchoring

Claude-Session: https://claude.ai/code/session_01Lm9T9oP2Hi5FEjwsLmXeyN"
```

---

### Task 4: The `<canvas>` markup element

**Goal:** Markup `<canvas id="...">` builds a `UiMarkupCanvas`, laid out by flex or by `x`/`y`/`w`/`h`, and listed on `MarkupWindow.Canvases`. A missing or duplicate id is a `FormatException`.

**Files:**
- Modify: `src/AcDream.App/UI/MarkupDocument.cs` (`AddElement` switch before `default:` ~line 848; `BuildWindow` before `new MarkupWindow(` ~line 202)
- Modify: `src/AcDream.App/UI/MarkupWindow.cs`
- Modify: `src/AcDream.App/UI/MarkupContentSize.cs` (`Measure` switch)
- Test: `tests/AcDream.App.Tests/UI/MarkupCanvasTests.cs` (create)

**Acceptance Criteria:**
- [ ] `<canvas id="map" x="10" y="20" w="100" h="60" />` builds a `UiMarkupCanvas` named `map` with `CanvasId` "map" at (10, 20, 100, 60)
- [ ] `name="map"` works like `id`
- [ ] `window.Canvases` lists the window's canvases in document order, including ones in nested groups
- [ ] In a 300×200 column with a 14-high label, `grow="1"` gives the canvas 300×186
- [ ] In a row, `w="80" h="40"` gives 80×40
- [ ] No id → `FormatException` whose message contains `<canvas> needs an id`
- [ ] The same id twice in one window → `FormatException` whose message contains `more than one <canvas id="map">`
- [ ] `visible="{Shown}"` binds as on every element

**Verify:** `PATH=$HOME/.dotnet:$PATH dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~MarkupCanvasTests|FullyQualifiedName~MarkupContentSizeTests|FullyQualifiedName~MarkupFlexTests|FullyQualifiedName~MarkupDocumentTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests.** Create `tests/AcDream.App.Tests/UI/MarkupCanvasTests.cs`:

```csharp
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

/// <summary>
/// <c>&lt;canvas&gt;</c> through the markup builder: an empty, id-carrying leaf
/// placed by <c>x</c>/<c>y</c>/<c>w</c>/<c>h</c> or by flex, listed on the
/// window so the interface can host the plugin's canvas in it, with a missing
/// or repeated id refused. Headless metrics: 14-point lines.
/// </summary>
public sealed class MarkupCanvasTests
{
    private sealed class Binding
    {
        public bool Shown { get; set; } = true;
    }

    private static MarkupWindow Build(string body, object? binding = null) =>
        MarkupDocument.BuildWindow(
            $"<panel x=\"0\" y=\"0\" w=\"300\" h=\"200\">{body}</panel>", binding ?? new object(), _ => (0u, 0, 0));

    private static MarkupWindow Mount(string body, object? binding = null)
    {
        MarkupWindow window = Build(body, binding);
        var root = new UiRoot { Width = 1280, Height = 720 };
        root.AddChild(window.Frame);
        root.Tick(0, 0);
        ThemeDrawCapture.Draw(root);
        return window;
    }

    private static (float, float, float, float) Rect(UiElement e) => (e.Left, e.Top, e.Width, e.Height);

    [Fact]
    public void An_absolute_canvas_is_an_empty_leaf_at_its_authored_rectangle()
    {
        MarkupWindow window = Mount("""<canvas id="map" x="10" y="20" w="100" h="60" />""");

        UiMarkupCanvas canvas = Assert.Single(window.Canvases);
        Assert.Equal("map", canvas.CanvasId);
        Assert.Equal("map", canvas.Name);
        Assert.Equal((10f, 20f, 100f, 60f), Rect(canvas));
        Assert.Null(canvas.Hosted);
        Assert.Empty(canvas.Children);
    }

    [Fact]
    public void Name_works_like_id_and_canvases_are_listed_in_document_order()
    {
        MarkupWindow window = Build("""
            <canvas name="a" x="0" y="0" w="10" h="10" />
            <group x="0" y="20" w="100" h="100">
              <canvas id="b" x="0" y="0" w="10" h="10" />
            </group>
            """);

        Assert.Equal(["a", "b"], window.Canvases.Select(c => c.CanvasId));
    }

    [Fact]
    public void Grow_fills_what_a_column_leaves()
    {
        MarkupWindow window = Mount("""
            <group x="0" y="0" w="300" h="200" layout="column">
              <label text="Map" />
              <canvas id="map" grow="1" />
            </group>
            """);

        Assert.Equal((0f, 14f, 300f, 186f), Rect(Assert.Single(window.Canvases)));
    }

    [Fact]
    public void W_and_h_size_it_in_a_row()
    {
        MarkupWindow window = Mount("""
            <group x="0" y="0" w="300" h="40" layout="row" align="start">
              <canvas id="map" w="80" h="40" />
            </group>
            """);

        Assert.Equal((0f, 0f, 80f, 40f), Rect(Assert.Single(window.Canvases)));
    }

    [Fact]
    public void A_canvas_without_an_id_is_refused()
    {
        var error = Assert.Throws<FormatException>(() => Build("""<canvas x="0" y="0" w="10" h="10" />"""));
        Assert.Contains("<canvas> needs an id", error.Message);
    }

    [Fact]
    public void The_same_id_twice_in_one_window_is_refused()
    {
        var error = Assert.Throws<FormatException>(() => Build("""
            <canvas id="map" x="0" y="0" w="10" h="10" />
            <group x="0" y="20" w="100" h="100"><canvas id="map" x="0" y="0" w="10" h="10" /></group>
            """));
        Assert.Contains("more than one <canvas id=\"map\">", error.Message);
    }

    [Fact]
    public void Visible_binds_as_on_every_element()
    {
        var binding = new Binding { Shown = false };
        MarkupWindow window = Mount("""<canvas id="map" x="0" y="0" w="10" h="10" visible="{Shown}" />""", binding);

        Assert.False(Assert.Single(window.Canvases).Visible);
    }
}
```

`ThemeDrawCapture` is the existing test helper `MarkupFlexTests` uses. It is in the same namespace.

- [ ] **Step 2: Run them.** Expected: a compile error (`MarkupWindow.Canvases` is missing), then `FormatException: unknown element <canvas>`.

- [ ] **Step 3: Add `Canvases` to `MarkupWindow`** (after `AuthoredGeometryRevision`):

```csharp
    /// <summary>The window's <c>&lt;canvas&gt;</c> elements in document order; each hosts the plugin canvas with its id.</summary>
    public IReadOnlyList<UiMarkupCanvas> Canvases { get; init; } = [];
```

- [ ] **Step 4: Add the `canvas` case** in `AddElement`, just before `default:`:

```csharp
            case "canvas":
            {
                string? canvasId = (string?)el.Attribute("id") ?? (string?)el.Attribute("name");
                if (string.IsNullOrWhiteSpace(canvasId))
                {
                    throw new FormatException(
                        "<canvas> needs an id: the CanvasId of the plugin canvas, registered with InMarkup, it shows");
                }
                var canvas = new UiMarkupCanvas(canvasId)
                {
                    Left = F(el, "x"),
                    Top = F(el, "y"),
                    Width = F(el, "w"),
                    Height = F(el, "h"),
                };
                ApplyCommon(canvas, el, binding);
                parent.AddChild(canvas);
                break;
            }
```

- [ ] **Step 5: Collect the canvases in `BuildWindow`.** Replace `var window = new MarkupWindow(panel, contentParent, titleBar, revision);` with:

```csharp
        var window = new MarkupWindow(panel, contentParent, titleBar, revision) { Canvases = CollectCanvases(panel) };
```

Then add the helpers next to `AbsoluteGroupSize`:

```csharp
    /// <summary>
    /// The window's <c>&lt;canvas&gt;</c> elements in document order. One
    /// window shows a canvas once, so an id used twice is an authoring error.
    /// </summary>
    private static IReadOnlyList<UiMarkupCanvas> CollectCanvases(UiElement root)
    {
        var canvases = new List<UiMarkupCanvas>();
        Collect(root);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (UiMarkupCanvas canvas in canvases)
        {
            if (!seen.Add(canvas.CanvasId))
            {
                throw new FormatException(
                    $"more than one <canvas id=\"{canvas.CanvasId}\"> in one window; a canvas is shown in one place");
            }
        }
        return canvases;

        void Collect(UiElement element)
        {
            if (element is UiMarkupCanvas canvas) canvases.Add(canvas);
            foreach (UiElement child in element.Children)
                Collect(child);
        }
    }
```

- [ ] **Step 6: Measure it in `MarkupContentSize.Measure`.** Add before `default:`:

```csharp
            case UiMarkupCanvas:
                // Nothing of its own: its w/h, basis or grow, or the container's
                // stretch, decide its size.
                return Fixed(0f, 0f);
```

- [ ] **Step 7: Run the tests.** Expected: PASS.
  - If `Grow_fills_what_a_column_leaves` reports a different label height, read `MarkupContentSize.FallbackLineHeight`. The expected top is that value, and the height is 200 minus it. Fix the expected numbers, not the code.

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.App/UI/MarkupDocument.cs src/AcDream.App/UI/MarkupWindow.cs src/AcDream.App/UI/MarkupContentSize.cs tests/AcDream.App.Tests/UI/MarkupCanvasTests.cs
git commit -m "feat(ui): <canvas id> markup element, laid out like any leaf

Claude-Session: https://claude.ai/code/session_01Lm9T9oP2Hi5FEjwsLmXeyN"
```

---

### Task 5: Pairing table `PluginMarkupCanvases`

**Goal:** Pair each `InMarkup` registration with the first mounted `<canvas>` of the same plugin and id, in either order.
- When a window goes, re-pair the canvas with another of the plugin's windows, or let it wait.
- When the canvas is disposed, empty its element.
- Report outside presses.
- Log once for a waiting canvas, a `<canvas>` shadowed by another window, and a `<canvas>` naming a non-`InMarkup` canvas.

**Files:**
- Create: `src/AcDream.App/UI/Layout/PluginMarkupCanvases.cs`
- Test: `tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.MarkupPairing.cs` (create; a partial of `PluginCanvasElementTests`)

**Acceptance Criteria:**
- [ ] A grow canvas in a 300×200 column with a 14-high label paints at 300×186, whichever of canvas and window comes first
- [ ] A canvas with no window reports `waiting for a <canvas id="map">` exactly once
- [ ] Hidden page (`visible="{Shown}"` false): no paint. Shown: one paint.
- [ ] `IsVisible = false`: no paint
- [ ] Inside a `scroll="y"` group scrolled by 90, a press at screen (10, 70) on a canvas authored at y=150 arrives at (10, 10)
- [ ] A wheel over a canvas without pointer input scrolls its group by 48
- [ ] An outside press reaches a shown hosted canvas at canvas-local coordinates, and nothing reaches a hidden one
- [ ] Window teardown → `IsAvailable` false and the element's targets given back. A new window hosts it again and it paints without the plugin invalidating.
- [ ] Dispose → the host is emptied and `IsAvailable` is false
- [ ] Same id in two windows: the first hosts it and the second reports once (`another window`). Removing the first moves the canvas to the second.
- [ ] A `<canvas>` naming a canvas registered without `InMarkup` hosts nothing and reports once (`without InMarkup`)

**Verify:** `PATH=$HOME/.dotnet:$PATH dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~PluginCanvasElementTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests.** Create `tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.MarkupPairing.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using AcDream.App.Plugins;
using AcDream.App.UI;
using AcDream.App.UI.Layout;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.UI.Layout;

/// <summary>
/// The interface's pairing of markup canvases with <c>&lt;canvas&gt;</c>
/// elements, end to end with real markup windows: either side may come first,
/// the window's layout sizes the canvas, hiding and scrolling work through the
/// tree, a canvas outlives its window and moves to another of the plugin's,
/// and what shows nothing says why once.
/// </summary>
public sealed partial class PluginCanvasElementTests
{
    private sealed class PageBinding
    {
        public bool Shown { get; set; } = true;
    }

    private const string GrowMap = """
        <group x="0" y="0" w="300" h="200" layout="column">
          <label text="Map" />
          <canvas id="map" grow="1" />
        </group>
        """;

    private static PluginMarkupCanvases Table(Harness harness) =>
        new(registration => HostedElement(harness, registration),
            (owner, id) => harness.Registry.FindCanvas(owner, id),
            harness.Reports.Add);

    private static MarkupWindow MountWindow(
        Harness harness, PluginMarkupCanvases table, string body, object? binding = null, int x = 0)
    {
        MarkupWindow window = MarkupDocument.BuildWindow(
            $"<panel x=\"{x}\" y=\"0\" w=\"300\" h=\"200\">{body}</panel>", binding ?? new object(), _ => (0u, 0, 0));
        harness.Root.AddChild(window.Frame);
        table.AddWindow(harness.Owner.Id, window.Canvases);
        return window;
    }

    private static void UnmountWindow(Harness harness, PluginMarkupCanvases table, MarkupWindow window)
    {
        table.RemoveWindow(window.Canvases);
        harness.Root.RemoveChild(window.Frame);
    }

    /// <summary>Mirrors the runtime: the table takes the canvas, and its teardown takes it out again.</summary>
    private static void AddToTable(Harness harness, PluginMarkupCanvases table, PluginCanvasRegistration registration)
    {
        table.AddCanvas(registration);
        harness.Registry.CompleteCanvasMount(registration, () => table.RemoveCanvas(registration));
    }

    private static PluginCanvasRegistration MapCanvas(
        Harness harness, PluginMarkupCanvases table, List<(int, int)> sizes, bool pointer = false, bool outside = false)
    {
        PluginCanvasRegistration registration =
            RegisterInMarkup(harness, "map", painter => sizes.Add((painter.Width, painter.Height)), pointer, outside);
        AddToTable(harness, table, registration);
        return registration;
    }

    [Fact]
    public void ACanvasRegisteredFirstIsHostedWhenItsWindowMounts()
    {
        var harness = new Harness();
        PluginMarkupCanvases table = Table(harness);
        var sizes = new List<(int, int)>();
        PluginCanvasRegistration registration = MapCanvas(harness, table, sizes);
        harness.Frame();
        Assert.Empty(sizes);
        Assert.False(registration.IsAvailable);
        Assert.Single(harness.Reports, line => line.Contains("waiting for a <canvas id=\"map\">"));

        MountWindow(harness, table, GrowMap);
        harness.Frame();

        Assert.Equal([(300, 186)], sizes);
        Assert.True(registration.IsAvailable);
        Assert.Single(harness.Reports, line => line.Contains("waiting"));
    }

    [Fact]
    public void AWindowMountedFirstHostsTheCanvasWhenItIsRegistered()
    {
        var harness = new Harness();
        PluginMarkupCanvases table = Table(harness);
        MountWindow(harness, table, GrowMap);
        harness.Frame();

        var sizes = new List<(int, int)>();
        MapCanvas(harness, table, sizes);
        harness.Frame();

        Assert.Equal([(300, 186)], sizes);
        Assert.Empty(harness.Reports);
    }

    [Fact]
    public void AHiddenPageOrAHiddenCanvasPaintsNothing()
    {
        var harness = new Harness();
        PluginMarkupCanvases table = Table(harness);
        var binding = new PageBinding { Shown = false };
        MountWindow(harness, table, """
            <group x="0" y="0" w="300" h="200" visible="{Shown}">
              <canvas id="map" x="0" y="0" w="100" h="100" />
            </group>
            """, binding);
        var sizes = new List<(int, int)>();
        PluginCanvasRegistration registration = MapCanvas(harness, table, sizes);

        harness.Frame();
        Assert.Empty(sizes);

        binding.Shown = true;
        harness.Frame();
        Assert.Equal([(100, 100)], sizes);

        registration.IsVisible = false;
        registration.Invalidate();
        harness.Frame();
        Assert.Single(sizes);
    }

    [Fact]
    public void InAScrolledGroupAPressArrivesInCanvasPixels()
    {
        var harness = new Harness();
        PluginMarkupCanvases table = Table(harness);
        MarkupWindow window = MountWindow(harness, table, """
            <group name="g" x="0" y="0" w="100" h="100" scroll="y">
              <canvas id="map" x="0" y="150" w="80" h="40" />
            </group>
            """);
        var sizes = new List<(int, int)>();
        PluginCanvasRegistration registration = MapCanvas(harness, table, sizes, pointer: true);
        var events = new List<PluginPointerEvent>();
        registration.PointerHandler = events.Add;
        harness.Frame();

        UiScrollArea area = ((IUiScrollHost)Find(window.Frame, "g")).ScrollArea!;
        area.Y.SetScrollY(90);
        harness.Frame();
        harness.Root.OnMouseDown(UiMouseButton.Left, 10, 70);
        harness.Root.OnMouseUp(UiMouseButton.Left, 10, 70);

        Assert.Equal(90, area.Y.ScrollY);
        Assert.Equal(new PluginPoint(10, 10), Assert.Single(events, e => e.Kind == PluginPointerEventKind.Down).Position);
    }

    [Fact]
    public void AWheelOverACanvasWithoutPointerInputScrollsItsGroup()
    {
        var harness = new Harness();
        PluginMarkupCanvases table = Table(harness);
        MarkupWindow window = MountWindow(harness, table, """
            <group name="g" x="0" y="0" w="100" h="100" scroll="y">
              <canvas id="map" x="0" y="0" w="80" h="300" />
            </group>
            """);
        MapCanvas(harness, table, []);
        harness.Frame();

        harness.Root.OnMouseMove(40, 40);
        harness.Root.OnScroll(0, -1, shift: false);
        harness.Frame();

        Assert.Equal(48, ((IUiScrollHost)Find(window.Frame, "g")).ScrollArea!.Y.ScrollY);
    }

    [Fact]
    public void OutsidePressesReachAShownHostedCanvasOnly()
    {
        var harness = new Harness();
        PluginMarkupCanvases table = Table(harness);
        var binding = new PageBinding();
        MountWindow(harness, table, """
            <group x="0" y="0" w="300" h="200" visible="{Shown}">
              <canvas id="map" x="20" y="20" w="100" h="100" />
            </group>
            """, binding);
        PluginCanvasRegistration registration = MapCanvas(harness, table, [], pointer: true, outside: true);
        var events = new List<PluginPointerEvent>();
        registration.PointerHandler = events.Add;
        harness.Frame();

        table.ReportPress(UiMouseButton.Left, 250, 150);
        binding.Shown = false;
        harness.Frame();
        table.ReportPress(UiMouseButton.Left, 250, 150);

        PluginPointerEvent outside = Assert.Single(events);
        Assert.Equal(PluginPointerEventKind.PressedOutside, outside.Kind);
        Assert.Equal(new PluginPoint(230, 130), outside.Position);
    }

    [Fact]
    public void ACanvasOutlivesItsWindowAndIsHostedAgainByTheNext()
    {
        var harness = new Harness();
        PluginMarkupCanvases table = Table(harness);
        var sizes = new List<(int, int)>();
        PluginCanvasRegistration registration = MapCanvas(harness, table, sizes);
        MarkupWindow first = MountWindow(harness, table, GrowMap);
        harness.Frame();
        PluginCanvasElement element = Assert.Single(first.Canvases).Hosted!;
        harness.Device.Clear();

        UnmountWindow(harness, table, first);

        Assert.False(registration.IsAvailable);
        Assert.Equal(0, element.TargetCount);
        Assert.Null(Assert.Single(first.Canvases).Hosted);

        MountWindow(harness, table, GrowMap);
        harness.Frame();
        Assert.True(registration.IsAvailable);
        Assert.Equal(2, sizes.Count);
    }

    [Fact]
    public void DisposingTheCanvasEmptiesItsElement()
    {
        var harness = new Harness();
        PluginMarkupCanvases table = Table(harness);
        PluginCanvasRegistration registration = MapCanvas(harness, table, []);
        MarkupWindow window = MountWindow(harness, table, GrowMap);
        harness.Frame();

        registration.Dispose();

        Assert.Null(Assert.Single(window.Canvases).Hosted);
        Assert.False(registration.IsAvailable);
        harness.Frame();
    }

    [Fact]
    public void TheFirstOfTwoWindowsHostsTheCanvasAndTheOtherTakesOver()
    {
        var harness = new Harness();
        PluginMarkupCanvases table = Table(harness);
        PluginCanvasRegistration registration = MapCanvas(harness, table, []);
        MarkupWindow first = MountWindow(harness, table, GrowMap);
        MarkupWindow second = MountWindow(harness, table, GrowMap, x: 400);
        harness.Frame();

        Assert.NotNull(Assert.Single(first.Canvases).Hosted);
        Assert.Null(Assert.Single(second.Canvases).Hosted);
        Assert.Single(harness.Reports, line => line.Contains("another window"));

        UnmountWindow(harness, table, first);

        Assert.NotNull(Assert.Single(second.Canvases).Hosted);
        Assert.True(registration.IsAvailable);
    }

    [Fact]
    public void ACanvasElementNamingAPlainCanvasShowsNothingAndSaysWhy()
    {
        var harness = new Harness();
        PluginMarkupCanvases table = Table(harness);
        harness.Registry.RegisterCanvas(harness.Owner, new PluginCanvasDescriptor("map", 50, 50), _ => { });
        MarkupWindow window = MountWindow(harness, table, GrowMap);
        harness.Frame();

        Assert.Null(Assert.Single(window.Canvases).Hosted);
        Assert.Single(harness.Reports, line => line.Contains("without InMarkup"));
    }

    private static UiElement Find(UiElement root, string name)
    {
        if (root.Name == name) return root;
        foreach (UiElement child in root.Children)
        {
            UiElement? found = TryFindNamed(child, name);
            if (found is not null) return found;
        }
        throw new InvalidOperationException($"no element named {name}");
    }

    private static UiElement? TryFindNamed(UiElement element, string name)
    {
        if (element.Name == name) return element;
        foreach (UiElement child in element.Children)
        {
            UiElement? found = TryFindNamed(child, name);
            if (found is not null) return found;
        }
        return null;
    }
}
```

(`PluginCanvasElementTests` has no `Find` helper of its own today; if a name clash appears, rename these to `FindNamed`.)

- [ ] **Step 2: Run them.** Expected: a compile error (`PluginMarkupCanvases` is missing).

- [ ] **Step 3: Create `src/AcDream.App/UI/Layout/PluginMarkupCanvases.cs`:**

```csharp
using System.Collections.Generic;
using AcDream.App.Plugins;

namespace AcDream.App.UI.Layout;

/// <summary>
/// Pairs plugin canvases registered with <c>InMarkup</c> with the
/// <c>&lt;canvas&gt;</c> elements of the plugins' mounted windows, by plugin
/// and canvas id. Either side may arrive first; the first element mounted
/// hosts a canvas, and when its window goes the canvas moves to the next one
/// or waits. A hosted canvas gets an element of its own each time it is
/// hosted, made by the interface the way a screen canvas's is, and gives its
/// targets back each time it leaves.
///
/// <para>What shows nothing says why once: a canvas waiting for its element,
/// an element whose canvas another window already shows, and an element
/// naming a canvas that was registered to sit on the screen.</para>
/// </summary>
internal sealed class PluginMarkupCanvases
{
    private sealed class Slot(string ownerId, UiMarkupCanvas element)
    {
        internal string OwnerId { get; } = ownerId;
        internal UiMarkupCanvas Element { get; } = element;
        internal bool Reported { get; set; }
    }

    private sealed class Entry(PluginCanvasRegistration registration)
    {
        internal PluginCanvasRegistration Registration { get; } = registration;
        internal Slot? Slot { get; set; }
        internal PluginCanvasElement? Element { get; set; }
        internal bool ReportedWaiting { get; set; }
    }

    private readonly Func<PluginCanvasRegistration, PluginCanvasElement> _createElement;
    private readonly Func<string, string, PluginCanvasRegistration?> _findCanvas;
    private readonly Action<string> _report;
    // Both in arrival order: the first element mounted is the first offered.
    private readonly List<Slot> _slots = [];
    private readonly List<Entry> _entries = [];

    internal PluginMarkupCanvases(
        Func<PluginCanvasRegistration, PluginCanvasElement> createElement,
        Func<string, string, PluginCanvasRegistration?> findCanvas,
        Action<string> report)
    {
        _createElement = createElement ?? throw new ArgumentNullException(nameof(createElement));
        _findCanvas = findCanvas ?? throw new ArgumentNullException(nameof(findCanvas));
        _report = report ?? throw new ArgumentNullException(nameof(report));
    }

    /// <summary>A canvas registered with <c>InMarkup</c>, drained by the interface; hosted at once if its element is mounted.</summary>
    internal void AddCanvas(PluginCanvasRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        _entries.Add(new Entry(registration));
        Pair();
    }

    /// <summary>The canvas was disposed or its interface is going away: its element is emptied.</summary>
    internal void RemoveCanvas(PluginCanvasRegistration registration)
    {
        Entry? entry = _entries.Find(e => ReferenceEquals(e.Registration, registration));
        if (entry is null) return;
        Unhost(entry);
        _entries.Remove(entry);
    }

    /// <summary>A window of plugin <paramref name="ownerId"/> mounted with these <c>&lt;canvas&gt;</c> elements.</summary>
    internal void AddWindow(string ownerId, IReadOnlyList<UiMarkupCanvas> elements)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentNullException.ThrowIfNull(elements);
        foreach (UiMarkupCanvas element in elements)
            _slots.Add(new Slot(ownerId, element));
        Pair();
        ReportIdle();
    }

    /// <summary>A window went: its canvases move to another of the plugin's windows, or wait.</summary>
    internal void RemoveWindow(IReadOnlyList<UiMarkupCanvas> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        foreach (UiMarkupCanvas element in elements)
        {
            Slot? slot = _slots.Find(s => ReferenceEquals(s.Element, element));
            if (slot is null) continue;
            foreach (Entry entry in _entries)
            {
                if (ReferenceEquals(entry.Slot, slot))
                    Unhost(entry);
            }
            _slots.Remove(slot);
        }
        Pair();
    }

    /// <summary>A press went down at interface (<paramref name="x"/>, <paramref name="y"/>): hosted canvases that asked hear of it.</summary>
    internal void ReportPress(UiMouseButton button, int x, int y)
    {
        foreach (Entry entry in _entries.ToArray())
        {
            if (entry.Element is { } element && entry.Registration.WantsOutsidePresses)
                element.ReportPress(button, x, y);
        }
    }

    private void Pair()
    {
        foreach (Entry entry in _entries)
        {
            if (entry.Slot is not null) continue;
            Slot? slot = _slots.Find(s => s.Element.Hosted is null && Matches(s, entry.Registration));
            if (slot is null)
            {
                if (!entry.ReportedWaiting)
                {
                    entry.ReportedWaiting = true;
                    _report(
                        $"Plugin canvas '{Name(entry.Registration)}' is registered InMarkup and is waiting for a "
                        + $"<canvas id=\"{entry.Registration.CanvasId}\"> in one of the plugin's windows; "
                        + "nothing is drawn until one is mounted.");
                }
                continue;
            }
            Host(entry, slot);
        }
    }

    private void Host(Entry entry, Slot slot)
    {
        PluginCanvasElement element = _createElement(entry.Registration);
        slot.Element.Host(element);
        entry.Slot = slot;
        entry.Element = element;
        entry.ReportedWaiting = false;
        entry.Registration.IsHosted = true;
        // A new element has nothing to show: paint it whether or not its size changed.
        entry.Registration.Invalidate();
    }

    private static void Unhost(Entry entry)
    {
        if (entry.Element is not { } element) return;
        entry.Registration.IsHosted = false;
        // Out of the tree first, so the root takes back a press or focus the
        // canvas holds, through the plugin's handlers; then the targets.
        entry.Slot!.Element.Unhost();
        element.ReleaseTargets();
        entry.Slot = null;
        entry.Element = null;
    }

    /// <summary>Says once, for each element still empty, why it shows nothing when the reason is known.</summary>
    private void ReportIdle()
    {
        foreach (Slot slot in _slots)
        {
            if (slot.Reported || slot.Element.Hosted is not null) continue;
            string where = $"<canvas id=\"{slot.Element.CanvasId}\"> of plugin '{slot.OwnerId}'";
            if (_entries.Exists(e => Matches(slot, e.Registration)))
            {
                slot.Reported = true;
                _report(
                    $"{where} shows nothing: another window of the plugin already shows that canvas. "
                    + "It takes over if that window goes away.");
            }
            else if (_findCanvas(slot.OwnerId, slot.Element.CanvasId) is { Descriptor.InMarkup: false })
            {
                slot.Reported = true;
                _report(
                    $"{where} shows nothing: the plugin registered canvas '{slot.Element.CanvasId}' without InMarkup, "
                    + "so it sits on the screen by itself.");
            }
        }
    }

    private static bool Matches(Slot slot, PluginCanvasRegistration registration) =>
        slot.OwnerId.Equals(registration.Owner.Id, StringComparison.Ordinal)
        && slot.Element.CanvasId.Equals(registration.CanvasId, StringComparison.Ordinal);

    private static string Name(PluginCanvasRegistration registration) =>
        $"{registration.Owner.Id}/{registration.CanvasId}";
}
```

- [ ] **Step 4: Run the tests.** Expected: PASS.
  - If the scroll test's `SetScrollY(90)` is clamped below 90: the content height comes from `SettleAbsolute` on a frame, so the first `harness.Frame()` before `SetScrollY` must stay.
  - If the outside-press test gets a second event: `ReportPress` must skip a hidden canvas. `PluginCanvasElement.ReportPress` already checks `IsShownInTree()`, so look at whether the group's `VisibleSource` was re-read on the frame.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Layout/PluginMarkupCanvases.cs tests/AcDream.App.Tests/UI/Layout/PluginCanvasElementTests.MarkupPairing.cs
git commit -m "feat(ui): pair markup canvases with their <canvas> elements

Claude-Session: https://claude.ai/code/session_01Lm9T9oP2Hi5FEjwsLmXeyN"
```

---

### Task 6: Wire the runtime

**Goal:** `RetailUiRuntime` does the following:
- It sends `InMarkup` canvases to the pairing table.
- It registers each mounted plugin window's `<canvas>` elements, and unregisters them when the window goes.
- It reports presses to hosted canvases.
- It builds screen and hosted canvas elements through one factory.

**Files:**
- Modify: `src/AcDream.App/UI/RetailUiRuntime.cs`:
  - fields near `_pluginCanvasSurface` (~395)
  - `MountPlugins` around `CompleteWindowMount` (~4182)
  - `CanvasStack` (~4245)
  - `ReportPressToCanvases` (~4259)
  - `MountPluginCanvases` (~4273)
  - teardown (~5247)

**Acceptance Criteria:**
- [ ] Each canvas registered with `InMarkup` is added to the table, and its teardown removes it from the table
- [ ] Without renderer services an `InMarkup` canvas fails to mount and is reported, as a screen canvas does today
- [ ] A plugin window's canvases join the table before `CompleteWindowMount`, and its window cleanup removes them
- [ ] `PressStarting` is subscribed once, whichever comes first (a canvas stack or the table), and the press order is: above windows, then markup, then world
- [ ] The solution builds and every App test passes

**Verify:**
- `PATH=$HOME/.dotnet:$PATH dotnet build src/AcDream.App` → 0 errors, 0 new warnings
- `PATH=$HOME/.dotnet:$PATH dotnet test tests/AcDream.App.Tests` → no new failures (compare with the pre-existing failures on `main`)

**Steps:**

- [ ] **Step 1: Add the fields** next to `_pluginCanvasSurface`:

```csharp
    private Layout.PluginMarkupCanvases? _markupCanvases;
    private bool _reportingPressesToCanvases;
```

- [ ] **Step 2: Add one press subscription and the table.** Replace the first two lines of `CanvasStack`:

```csharp
        if (_worldCanvases is null && _canvasesAboveWindows is null)
            Host.Root.PressStarting += ReportPressToCanvases;
```

with:

```csharp
        ReportPressesToCanvases();
```

Then add these after `CanvasStack`:

```csharp
    /// <summary>Starts telling canvases of presses, once, whichever kind of canvas comes first.</summary>
    private void ReportPressesToCanvases()
    {
        if (_reportingPressesToCanvases) return;
        _reportingPressesToCanvases = true;
        Host.Root.PressStarting += ReportPressToCanvases;
    }

    /// <summary>The table pairing markup canvases with their windows' <c>&lt;canvas&gt;</c> elements, made on first use.</summary>
    private Layout.PluginMarkupCanvases MarkupCanvases()
    {
        if (_markupCanvases is not null) return _markupCanvases;
        ReportPressesToCanvases();
        BufferedUiRegistry plugins = _bindings.Plugins
            ?? throw new InvalidOperationException("markup canvases need the plugin registry");
        return _markupCanvases = new Layout.PluginMarkupCanvases(
            canvas => CreateCanvasElement(canvas, hosted: true),
            plugins.FindCanvas,
            line => Console.WriteLine($"[UI] {line}"));
    }
```

- [ ] **Step 3: Report presses to hosted canvases.** Change `ReportPressToCanvases` to:

```csharp
    private void ReportPressToCanvases(UiMouseButton button, int x, int y)
    {
        _canvasesAboveWindows?.ReportPress(button, x, y);
        _markupCanvases?.ReportPress(button, x, y);
        _worldCanvases?.ReportPress(button, x, y);
    }
```

- [ ] **Step 4: Build elements through one factory, and route `InMarkup` canvases.** Replace the body of the `try` in `MountPluginCanvases`'s loop with:

```csharp
                if (canvas.Descriptor.InMarkup)
                {
                    // Checked here so a host without renderer services fails
                    // the canvas now, as it does a screen canvas, rather than
                    // when a window would host it.
                    RequireCanvasServices();
                    Layout.PluginMarkupCanvases markup = MarkupCanvases();
                    markup.AddCanvas(canvas);
                    plugins.CompleteCanvasMount(canvas, () => markup.RemoveCanvas(canvas));
                    Console.WriteLine(
                        $"[UI] plugin canvas registered for markup: {canvas.Owner.Id}/{canvas.CanvasId}");
                    continue;
                }
                Layout.PluginCanvasStack stack = CanvasStack(canvas.Layer);
                Layout.PluginCanvasElement element = CreateCanvasElement(canvas, hosted: false);
                stack.Add(element);
                plugins.CompleteCanvasMount(canvas, () =>
                {
                    Layout.PluginCanvasStack.Remove(element);
                    element.ReleaseTargets();
                });
                Console.WriteLine(
                    $"[UI] plugin canvas mounted: {canvas.Owner.Id}/{canvas.CanvasId} "
                    + $"({canvas.Width}x{canvas.Height})");
```

Then add these after `MountPluginCanvases`:

```csharp
    private PluginCanvasHostServices RequireCanvasServices() =>
        _bindings.PluginCanvases ?? throw new InvalidOperationException(
            "this interface was built without renderer services for plugin canvases");

    /// <summary>
    /// The element that draws <paramref name="canvas"/>: on its layer, or,
    /// <paramref name="hosted"/>, inside the markup <c>&lt;canvas&gt;</c> that hosts it.
    /// </summary>
    private Layout.PluginCanvasElement CreateCanvasElement(PluginCanvasRegistration canvas, bool hosted)
    {
        PluginCanvasHostServices services = RequireCanvasServices();
        BufferedUiRegistry plugins = _bindings.Plugins!;
        _pluginCanvasSurface ??= new Layout.PluginCanvasSurface(services, _bindings.Assets.DefaultFont);
        PluginUiOwner owner = canvas.Owner;
        return new Layout.PluginCanvasElement(
            canvas,
            _pluginCanvasSurface,
            () => plugins.FindImages(owner),
            modifiers: HeldPointerModifiers,
            fonts: () => plugins.FindFonts(owner),
            keyboardCaptured: () => _bindings.Keyboard?.Dispatcher?.IsCapturing == true,
            hosted: hosted);
    }
```

Delete the old in-loop `if (_bindings.PluginCanvases is not { } services) throw …` and `_pluginCanvasSurface ??= …` lines from the loop: `RequireCanvasServices`/`CreateCanvasElement` now do that. `PluginCanvasHostServices` is the declared type of `_bindings.PluginCanvases` (RetailUiRuntime.cs:343).

- [ ] **Step 5: Register window canvases** in `MountPlugins`. Replace:

```csharp
                _bindings.Plugins.CompleteWindowMount(
                    panel,
                    () => Host.WindowManager.Unregister(panel.WindowName));
```

with:

```csharp
                // Before the cleanup is published: a plugin that disposed the
                // window meanwhile runs the cleanup at once, which takes them out again.
                IReadOnlyList<UiMarkupCanvas> canvases = window.Canvases;
                if (canvases.Count > 0)
                    MarkupCanvases().AddWindow(panel.Owner.Id, canvases);
                _bindings.Plugins.CompleteWindowMount(
                    panel,
                    () =>
                    {
                        Host.WindowManager.Unregister(panel.WindowName);
                        if (canvases.Count > 0)
                            _markupCanvases?.RemoveWindow(canvases);
                    });
```

- [ ] **Step 6: Teardown.** In the teardown block, the existing `Host.Root.PressStarting -= ReportPressToCanvases;` stays. Add after it:

```csharp
                _reportingPressesToCanvases = false;
```

Then add after `_bindings.Plugins?.UnbindCanvasHost();`:

```csharp
                // Every markup canvas left the table through its teardown just now.
                _markupCanvases = null;
```

- [ ] **Step 7: Build and test.** Run the two Verify commands. Expected: the build is clean and no App test fails that also passes on `main`. Record the pre-existing failures by running the same command on `main` first, if they are not already known.

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.App/UI/RetailUiRuntime.cs
git commit -m "feat(ui): the interface hosts markup canvases in their windows

Claude-Session: https://claude.ai/code/session_01Lm9T9oP2Hi5FEjwsLmXeyN"
```

---

### Task 7: Documentation

**Goal:** Plugin authors can find and use `<canvas>` from the fork's docs.

**Files:**
- Modify: `docs/plugin-ui-markup.md`:
  - Elements table (~263-280)
  - a new `<canvas>` section after the last element section and before "Flex layout" (~418)
  - Tests list (~822-898)
- Modify: `docs/plugin-api.md`: a "Canvases in markup" subsection in Canvases, after "Presses outside" (~2034) and before "Keyboard" (~2082)

**Acceptance Criteria:**
- [ ] The elements table has a `<canvas>` row: `id` (required), the common attributes and flex item attributes, and "hosts the plugin canvas registered with `InMarkup`"
- [ ] The `<canvas>` section covers pairing, sizing, visibility, lifetime, input, the ignored members, and a C# + XML example
- [ ] `plugin-api.md` explains `InMarkup`, `TryResize` returning false, and `IsAvailable`, and links to the markup section
- [ ] The Tests list names `MarkupCanvasTests`, `PluginCanvasElementTests.Markup` and `PluginCanvasElementTests.MarkupPairing`

**Verify:** `grep -n "InMarkup" docs/plugin-ui-markup.md docs/plugin-api.md` shows hits in both files.

**Steps:**

- [ ] **Step 1: Read the neighbouring sections** in both docs to match their heading levels, table columns and tone.

- [ ] **Step 2: Write the `<canvas>` section** in `plugin-ui-markup.md` with this content, adjusted to the file's heading level:

````markdown
### `<canvas>`: a plugin drawing inside the window

A `<canvas id="...">` shows a plugin canvas inside the window, where the layout puts it. The plugin registers the
canvas as usual, with `InMarkup = true`, and keeps the `IPluginCanvas` it gets back: `Invalidate`, `PointerHandler`,
`KeyHandler`, `ReleasePointer` and outside presses work as for any canvas.

```csharp
IPluginCanvas map = Host.Ui.RegisterCanvas(
    new PluginCanvasDescriptor("dungeon-map", 200, 200) { InMarkup = true, AcceptsPointerInput = true },
    PaintMap);
```

```xml
<group layout="column" grow="1">
  <label text="Dungeon" />
  <canvas id="dungeon-map" grow="1" />
</group>
```

- **Size.** The element is sized like any leaf: `w`/`h`, or `grow`, `basis`, `min*`/`max*` and the container's
  stretch in a flex container. On its own it measures 0×0. The canvas fills it, rounded to whole pixels;
  `painter.Width`/`Height` and `map.Width`/`Height` report that size, and a change repaints the canvas. The
  descriptor's size counts only until the first layout. `TryResize` answers false, and `Anchor`, `Offset`, `Layer`
  and `ZOrder` are ignored.
- **Shown.** The canvas is drawn and takes input only while `IsVisible` is true and the element is shown: its window
  open and no ancestor (a hidden tab page, a `visible="{...}"` group) hidden. Inside a scrolling group it scrolls and
  clips with the content; pointer positions stay in the canvas's own pixels.
- **Pairing.** The id is the canvas's `CanvasId`, matched within the plugin only. Either may come first. A canvas
  with no element yet is not drawn (`IsAvailable` is false) and the client log says it is waiting. An element with
  no canvas draws nothing. A `<canvas>` without an id, or the same id twice in one window, is a markup error. With
  the same id in two windows, the first one mounted shows the canvas, and the other takes over if that window goes.
- **Lifetime.** When the window goes, the canvas waits for another element; the plugin's handle keeps working.
  Disposing the canvas empties the element. Markup canvases count toward the 8 canvases a plugin may hold.
````

- [ ] **Step 3: Add the elements table row and the Tests entries**, in the existing format.

- [ ] **Step 4: Write the `plugin-api.md` subsection.** Give it the same facts in two short paragraphs, and point to the markup section for the rules.

- [ ] **Step 5: Commit**

```bash
git add docs/plugin-ui-markup.md docs/plugin-api.md
git commit -m "docs: <canvas> markup element and InMarkup canvases

Claude-Session: https://claude.ai/code/session_01Lm9T9oP2Hi5FEjwsLmXeyN"
```

---

### Task 8: Release gate and live check

**Goal:** The branch passes the release gate with no new failures. A probe plugin shows a markup canvas working in the real client on the local ACE server, and the user confirms it.

> **USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in `acceptanceCriteria` has been re-validated independently, with output captured.

**Files:**
- Create (throwaway, not committed): a probe plugin in the scratchpad. It copies the shape of `samples/AcDream.Plugins.CanvasDemo`, with a themed flex window that has two tab pages; one page holds `<canvas id="probe" grow="1" />` inside a `scroll="y"` group.

**Acceptance Criteria:**
- [ ] `tools/run-release-gate.ps1` (with `PATH=$HOME/.dotnet:$PATH`) reports no failures beyond the known pre-existing ones on `main`
- [ ] Live, against 10.10.20.20 via `~/OpenAC-dev/dev-client.env` (never Dreamweave):
  - the canvas paints inside the window
  - resizing the window resizes and repaints it
  - a click logs canvas-local coordinates
  - switching tabs hides and shows it
  - it looks sharp at 2× on the built-in Retina display
- [ ] The user confirms the live check

**Verify:** the gate output summary, plus the user's confirmation after the live run.

**Steps:**

- [ ] **Step 1:** Run the gate: `PATH=$HOME/.dotnet:$PATH pwsh tools/run-release-gate.ps1`. Capture the summary. If any lock files changed, revert them before going on.
- [ ] **Step 2:** Build the probe plugin against the branch and install it in the dev client root. Follow the dev-client workflow used for earlier live tests.
- [ ] **Step 3:** Ask the user to run the client and go through the checklist. Use the Retina capture workflow for the 2× check.
- [ ] **Step 4:** Remove the probe plugin from the dev root afterwards.

---

### Task 9: Merge, push, and update Aethrix gap 62

**Goal:** The feature is on fork `main` and pushed, and Aethrix `docs/api-gaps.md` #62 is marked resolved with the minimum contract version and usage specifics.

**Files:**
- Modify: `/Users/davidsmith/code/OpenAC/OpenAC-Plugins-Aethrix/docs/api-gaps.md` (section `## 62.`, ~line 1030)

**Acceptance Criteria:**
- [ ] `feat/markup-canvas` is merged into `main` with `--no-ff`, and `main` and the branch are pushed to `origin`
- [ ] Gap 62 starts with a **Resolved** line in the style of #67/#69. It names:
  - the merge commit and the feature commit
  - contract `0.1.25-fork.5` as the minimum
  - that Aethrix pins `0.1.25-fork.4` today and must bump `Directory.Build.props`
- [ ] The entry gives `InMarkup = true` plus `<canvas id grow>`, with a short example
- [ ] The entry covers: `TryResize`=false and the ignored `Anchor`/`Offset`/`Layer`/`ZOrder`; visibility; pairing and waiting; lifetime; the 8-canvas limit
- [ ] The Aethrix change is committed and pushed

**Verify:** `git log origin/main --oneline -1` shows the merge; `grep -n "0.1.25-fork.5" /Users/davidsmith/code/OpenAC/OpenAC-Plugins-Aethrix/docs/api-gaps.md` hits inside section 62.

**Steps:**

- [ ] **Step 1:** Merge and push:

```bash
git switch main && git merge --no-ff feat/markup-canvas -m "Merge branch 'feat/markup-canvas': a plugin canvas can sit inside a markup window

Claude-Session: https://claude.ai/code/session_01Lm9T9oP2Hi5FEjwsLmXeyN"
git push origin main feat/markup-canvas
```

- [ ] **Step 2:** In Aethrix, add the **Resolved** paragraph directly under the `## 62.` heading. Keep the original text below it, as the other resolved entries do. Fill in the real commit ids.
- [ ] **Step 3:** Commit and push in the Aethrix repo:

```bash
git -C /Users/davidsmith/code/OpenAC/OpenAC-Plugins-Aethrix add docs/api-gaps.md
git -C /Users/davidsmith/code/OpenAC/OpenAC-Plugins-Aethrix commit -m "docs(api-gaps): #62 resolved on the fork (markup <canvas>, contract 0.1.25-fork.5)

Claude-Session: https://claude.ai/code/session_01Lm9T9oP2Hi5FEjwsLmXeyN"
git -C /Users/davidsmith/code/OpenAC/OpenAC-Plugins-Aethrix push
```

- [ ] **Step 4:** Update memory: a `markup-canvas` memory, the `fork-contract-version` note (fork.5), and the `MEMORY.md` pointer.
