# Rynth Drop Targets Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An item dragged out of OpenAC's packs or main inventory can be dropped on RynthCore's Item Count, Loot Editor and Mini Remote slots; any drag let go over an ImGui window that doesn't take it is cancelled instead of reaching the retail UI or the world.

**Architecture:** RynthCore's `ImGui/UiDropTargets.cs` compiles unchanged into AcDream.Rynth.EngineUi; its `GetCursorPos` is redirected to a cursor stand-in and `UiElementHooks.CurrentDragObject` reads the client's drag. OpenAC's `UiRoot` asks a release interceptor before its hit-test; the App's `ClientItemDrops` answers it using a new `ImGuiOverlay.PointerOverWindow` (kept alive during retail drags by an ImGui external drag source) and offers inventory drags to the Rynth host's claimer.

**Tech Stack:** .NET 10 (`~/.dotnet`), ImGui.NET 1.91.6.1, Mono.Cecil (RynthInteropRedirect), xUnit.

**Spec:** `docs/superpowers/specs/2026-10-21-rynth-drop-targets-design.md`

## Global Constraints

- RynthCore source is compiled unchanged (never edit files under `external/`). Missing engine members go in stand-ins under `src/AcDream.Rynth.EngineUi/StandIns/` in the existing style (namespace = RynthCore's, short `<summary>` saying what OpenAC does instead).
- Build/test with `export PATH=$HOME/.dotnet:$PATH`. Never run `tools/update-package-locks.ps1`.
- Points everywhere: `UiRoot`'s mouse handlers, the overlay input feed and ImGui all use Silk window points; the interceptor gets the point before `UiRoot.MapWindowToCanvas`.
- Only `ItemDragPayload` with `SourceKind == ItemDragSource.Inventory` is ever offered to a panel. Every drag (any source) let go over an ImGui window is consumed (cancelled if not claimed).
- External drag source payload type string: `"OPENAC_DRAG"`.
- No plugin contract change; fork contract stays `0.1.26-fork.2` (`ClientExtensionContext` is host-only, like `Screen`).
- Commit messages end with `Claude-Session: https://claude.ai/code/session_011FtdjFB5dEVWdBQnZmstpz`.
- Work on branch `feat/rynth-drop-targets`. Baselines on main: Rynth.Host.Tests all pass; AcDream.App.Tests has 59 pre-existing failures on Mac (no new ones allowed).

**User decisions (already made):**
- Sources: packs + main inventory only, as RynthCore (2026-10-21).
- An item let go over an ImGui window that doesn't take it is cancelled; it stays where it was (2026-10-21).
- Approach A: their `UiDropTargets` unchanged + a release claim in OpenAC (2026-10-21).

---

## File map

| File | Change |
|---|---|
| `tools/RynthInteropRedirect/Program.cs` | P/Invoke match also accepts a by-ref struct with the same instance fields |
| `tests/AcDream.Tools.RynthInteropRedirect.Tests/RedirectPInvokeTests.cs` | two tests |
| `src/AcDream.App/UI/UiRoot.cs` | `DragReleaseInterceptor`, asked in `OnMouseUp` before `FinishDrag` |
| `tests/AcDream.App.Tests/UI/UiRootDragReleaseInterceptorTests.cs` | new |
| `src/AcDream.App/Extensions/Overlay/ImGuiOverlay.cs` | `ExternalDragActive`, `PointerOverWindow`, extern source in `BeginFrame` |
| `tests/AcDream.App.Tests/Extensions/ImGuiOverlayTests.cs` | three tests |
| `src/AcDream.ClientExtensions/IClientItemDrops.cs` | new |
| `src/AcDream.ClientExtensions/ClientExtensionContext.cs` | `ItemDrops` |
| `src/AcDream.App/Extensions/ClientItemDrops.cs` | new |
| `src/AcDream.App/Extensions/ClientExtensionHost.cs` | `itemDrops` parameter |
| `src/AcDream.App/Rendering/GameWindow.cs` | create/bind `ClientItemDrops`, overlay `ExternalDragActive` |
| `tests/AcDream.App.Tests/Extensions/ClientItemDropsTests.cs` | new |
| `src/AcDream.Rynth.EngineUi/AcDream.Rynth.EngineUi.csproj` | compile their `UiDropTargets.cs`; redirect list + `Win32CursorStandIn` |
| `src/AcDream.Rynth.EngineUi/StandIns/Win32CursorStandIn.cs` | new |
| `src/AcDream.Rynth.EngineUi/StandIns/DashboardStandIns.cs` | delete `UiDropTargets`; `UiElementHooks.DragSource` |
| `src/AcDream.Rynth.EngineUi/StandIns/ChatEngineStandIns.cs` | `ImGuiPopOuts.ClientOriginOnScreen`, `IsCursorOver` |
| `src/AcDream.Rynth.EngineUi/RynthDropClaim.cs` | new |
| `src/AcDream.Rynth.Host/RynthClientExtension.cs` | `StartDrops`, clear at dispose |
| `tests/AcDream.Rynth.Host.Tests/RynthDropTargetsTests.cs` | new |
| `tests/AcDream.App.Tests/Extensions/RynthOverlayExtensionTests.cs` | wiring test |

---

### Task 1: RynthInteropRedirect matches a same-layout by-ref struct

**Goal:** A user32 P/Invoke whose parameter is `out SomePrivateStruct` redirects to a stand-in method whose parameter is `out OtherStruct` when both structs have the same instance fields (names and types, in order).

**Files:**
- Modify: `tools/RynthInteropRedirect/Program.cs` (`SameSignature`, ~line 600)
- Modify: `tests/AcDream.Tools.RynthInteropRedirect.Tests/RedirectPInvokeTests.cs`

**Acceptance Criteria:**
- [ ] `App.Native::GetCursorPos(out App.Native/POINT)` (nested, `{int X; int Y}`) redirects to `App.CursorStandIn::GetCursorPos(out App.CursorPoint)` (`{int X; int Y}`); output reads `1 P/Invoke(s) redirected to App.CursorStandIn`.
- [ ] A stand-in struct with different fields (`{int X; long Y}`) does not match: the import stays a P/Invoke and output reads `0 P/Invoke(s)`.
- [ ] The existing 3 RedirectPInvoke tests and all RedirectMember/RedirectType tests still pass.

**Verify:** `dotnet test tests/AcDream.Tools.RynthInteropRedirect.Tests` → all pass (11 old + 2 new).

**Steps:**

- [ ] **Step 1: Write the failing tests** — add to `RedirectPInvokeTests`:

```csharp
    [Fact]
    public void AnOutStructGoesToAStandInStructWithTheSameFields()
    {
        BuildCursor(standInSecondField: "Int32");

        (int code, string output, string error) = Run("App.CursorStandIn");

        Assert.True(code == 0, error);
        Assert.Contains("1 P/Invoke(s) redirected to App.CursorStandIn", output, StringComparison.Ordinal);
        using ModuleDefinition app = Read();
        Assert.Equal(["System.Boolean App.CursorStandIn::GetCursorPos(App.CursorPoint&)"], Calls(app, "GetCursorPos"));
    }

    [Fact]
    public void AnOutStructWithOtherFieldsIsNotMatched()
    {
        BuildCursor(standInSecondField: "Int64");

        (int code, string output, string error) = Run("App.CursorStandIn");

        Assert.True(code == 0, error);
        Assert.Contains("0 P/Invoke(s) redirected", output, StringComparison.Ordinal);
        using ModuleDefinition app = Read();
        Assert.True(app.GetType("App.Native").Methods.Single(m => m.Name == "GetCursorPos").IsPInvokeImpl);
    }

    private void BuildCursor(string standInSecondField)
    {
        using AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("App", new Version(1, 0)), "App", ModuleKind.Dll);
        ModuleDefinition app = assembly.MainModule;
        var user32 = new ModuleReference("user32.dll");
        app.ModuleReferences.Add(user32);

        var native = new TypeDefinition("App", "Native", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, app.TypeSystem.Object);
        app.Types.Add(native);
        TypeDefinition point = Struct(app, "", "POINT", TypeAttributes.NestedPrivate, app.TypeSystem.Int32, app.TypeSystem.Int32);
        native.NestedTypes.Add(point);
        MethodDefinition import = Import(app, user32, "GetCursorPos", app.TypeSystem.Boolean, new ByReferenceType(point));
        import.Parameters[0].Attributes = ParameterAttributes.Out;
        native.Methods.Add(import);

        TypeReference second = standInSecondField == "Int64" ? app.TypeSystem.Int64 : app.TypeSystem.Int32;
        TypeDefinition cursorPoint = Struct(app, "App", "CursorPoint", TypeAttributes.Public, app.TypeSystem.Int32, second);
        app.Types.Add(cursorPoint);
        TypeDefinition standIn = StandIn(app, "CursorStandIn", "GetCursorPos", app.TypeSystem.Boolean, new ByReferenceType(cursorPoint));
        standIn.Methods[0].Parameters[0].Attributes = ParameterAttributes.Out;
        app.Types.Add(standIn);
        assembly.Write(AppPath);
    }

    private static TypeDefinition Struct(ModuleDefinition app, string ns, string name, TypeAttributes visibility, TypeReference x, TypeReference y)
    {
        var type = new TypeDefinition(ns, name,
            visibility | TypeAttributes.SequentialLayout | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            app.ImportReference(typeof(ValueType)));
        type.Fields.Add(new FieldDefinition("X", FieldAttributes.Public, x));
        type.Fields.Add(new FieldDefinition("Y", FieldAttributes.Public, y));
        return type;
    }
```

- [ ] **Step 2: Run to verify the first fails**

Run: `dotnet test tests/AcDream.Tools.RynthInteropRedirect.Tests --filter "FullyQualifiedName~OutStruct"`
Expected: `AnOutStructGoesToAStandInStructWithTheSameFields` FAILS (`0 P/Invoke(s) redirected`); the other passes.

- [ ] **Step 3: Implement** — replace `SameSignature` in `Program.cs`:

```csharp
    private static bool SameSignature(MethodDefinition a, MethodDefinition b) =>
        a.ReturnType.FullName == b.ReturnType.FullName
        && a.Parameters.Count == b.Parameters.Count
        && a.Parameters.Zip(b.Parameters).All(pair => SameParameterType(pair.First.ParameterType, pair.Second.ParameterType));

    /// <summary>
    /// The same type, or by-ref structs with the same instance fields (names and types, in
    /// order): a stand-in can't name a P/Invoke's private nested struct (UiDropTargets' POINT),
    /// so it declares its own of the same layout.
    /// </summary>
    private static bool SameParameterType(TypeReference a, TypeReference b)
    {
        if (a.FullName == b.FullName)
            return true;
        if (a is not ByReferenceType { ElementType: var ea } || b is not ByReferenceType { ElementType: var eb })
            return false;
        if (ea.Resolve() is not { IsValueType: true } da || eb.Resolve() is not { IsValueType: true } db)
            return false;
        FieldDefinition[] fa = da.Fields.Where(f => !f.IsStatic).ToArray();
        FieldDefinition[] fb = db.Fields.Where(f => !f.IsStatic).ToArray();
        return fa.Length == fb.Length
            && fa.Zip(fb).All(pair => pair.First.Name == pair.Second.Name && pair.First.FieldType.FullName == pair.Second.FieldType.FullName);
    }
```

`CallInstead` passes the address through unchanged (`ldarg` + `call`): byref-to-different-struct is accepted by the JIT, as `Unsafe.As` does.

- [ ] **Step 4: Run all tool tests**

Run: `dotnet test tests/AcDream.Tools.RynthInteropRedirect.Tests`
Expected: all pass (13).

- [ ] **Step 5: Commit**

```bash
git add tools/RynthInteropRedirect/Program.cs tests/AcDream.Tools.RynthInteropRedirect.Tests/RedirectPInvokeTests.cs
git commit -m "feat(rynth): interop redirect matches a same-layout out struct"
```

---

### Task 2: UiRoot release interceptor

**Goal:** `UiRoot.DragReleaseInterceptor` is asked with the payload and the unmapped window point when a drag is let go; true ends the drag with no drop and no outside-UI event.

**Files:**
- Modify: `src/AcDream.App/UI/UiRoot.cs` (property near `DragReleasedOutsideUi` ~line 257; `OnMouseUp` ~line 688; new `CancelDrag` beside `FinishDrag` ~line 1149)
- Create: `tests/AcDream.App.Tests/UI/UiRootDragReleaseInterceptorTests.cs`

**Acceptance Criteria:**
- [ ] Interceptor returning true: the element under the release gets no `DropReleased`, `DragReleasedOutsideUi` isn't raised, `DragSource`/`DragPayload` are null and the source's `SetDragSourceActive(false, payload)` ran.
- [ ] Interceptor returning false (or none set): today's behaviour (`DropReleased` to the target / `DragReleasedOutsideUi` off-UI).
- [ ] With `CanvasScale = (2,2)` the interceptor gets the window point (e.g. 300,200), not the canvas point (150,100).
- [ ] The interceptor is not asked for a plain click (no drag).

**Verify:** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~UiRootDragReleaseInterceptorTests"` → 4 pass.

**Steps:**

- [ ] **Step 1: Write the failing tests.** Before writing, read `UiItemSlot` (`src/AcDream.App/UI/UiItemSlot.cs`) for how a bound slot starts a drag (`SetItem`, `GetDragPayload`) and how `CanvasScale` is set on `UiRoot` (`grep -n CanvasScale src/AcDream.App/UI/UiRoot.cs`); use the same calls in the helper below.

```csharp
using System.Numerics;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public class UiRootDragReleaseInterceptorTests
{
    private sealed class DropSpy : UiPanel
    {
        public int Drops;
        public override bool OnEvent(in UiEvent e)
        {
            if (e.Type == UiEventType.DropReleased) Drops++;
            return base.OnEvent(in e);
        }
    }

    private static (UiRoot Root, UiItemSlot Source, DropSpy Target) Scene()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var source = new UiItemSlot { Left = 10, Top = 10, Width = 32, Height = 32, SourceKind = ItemDragSource.Inventory };
        source.SetItem(0x5001u, 0x99u);
        var target = new DropSpy { Left = 400, Top = 300, Width = 100, Height = 100 };
        root.AddChild(source);
        root.AddChild(target);
        return (root, source, target);
    }

    private static void DragTo(UiRoot root, int x, int y)
    {
        root.OnMouseDown(UiMouseButton.Left, 20, 20);
        root.OnMouseMove(30, 30);      // past the 3-point drag threshold
        root.OnMouseMove(x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
    }

    [Fact]
    public void AClaimingInterceptorEndsTheDragWithNoDrop()
    {
        var (root, _, target) = Scene();
        object? seen = null;
        root.DragReleaseInterceptor = (payload, _) => { seen = payload; return true; };
        int outside = 0;
        root.DragReleasedOutsideUi += (_, _, _) => outside++;

        DragTo(root, 450, 350);

        Assert.IsType<ItemDragPayload>(seen);
        Assert.Equal(0, target.Drops);
        Assert.Equal(0, outside);
        Assert.Null(root.DragSource);
        Assert.Null(root.DragPayload);
    }

    [Fact]
    public void ADecliningInterceptorLeavesTheDropAsToday()
    {
        var (root, _, target) = Scene();
        root.DragReleaseInterceptor = (_, _) => false;
        int outside = 0;
        root.DragReleasedOutsideUi += (_, _, _) => outside++;

        DragTo(root, 450, 350);
        Assert.Equal(1, target.Drops);

        DragTo(root, 700, 50);
        Assert.Equal(1, outside);
    }

    [Fact]
    public void TheInterceptorGetsTheWindowPointNotTheCanvasPoint()
    {
        var (root, _, _) = Scene();
        root.CanvasScale = new Vector2(2, 2);
        Vector2 point = default;
        root.DragReleaseInterceptor = (_, p) => { point = p; return true; };

        root.OnMouseDown(UiMouseButton.Left, 40, 40);
        root.OnMouseMove(60, 60);
        root.OnMouseUp(UiMouseButton.Left, 300, 200);

        Assert.Equal(new Vector2(300, 200), point);
    }

    [Fact]
    public void APlainClickNeverAsks()
    {
        var (root, _, _) = Scene();
        int asked = 0;
        root.DragReleaseInterceptor = (_, _) => { asked++; return true; };

        root.OnMouseDown(UiMouseButton.Left, 20, 20);
        root.OnMouseUp(UiMouseButton.Left, 20, 20);

        Assert.Equal(0, asked);
    }
}
```

If `CanvasScale` has no public setter, set it the way `UiRootFixedCanvasTests` does and keep the assertion.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~UiRootDragReleaseInterceptorTests"`
Expected: build error — `DragReleaseInterceptor` not defined.

- [ ] **Step 3: Implement** in `UiRoot.cs`.

Beside `DragReleasedOutsideUi`:

```csharp
    /// <summary>
    /// Asked when a drag is let go, before the drop is hit-tested: the payload and the
    /// release point in window points (before the canvas mapping). True: the drag ends
    /// there, with no drop and no <see cref="DragReleasedOutsideUi"/> (an ImGui window
    /// took it or is in the way).
    /// </summary>
    public Func<object, Vector2, bool>? DragReleaseInterceptor { get; set; }
```

In `OnMouseUp`, keep the window point before mapping and ask in the drag branch:

```csharp
    public void OnMouseUp(UiMouseButton btn, int x, int y, uint flags = 0)
    {
        var windowPoint = new Vector2(x, y);
        (x, y) = MapWindowToCanvas(x, y);
```

```csharp
        if (DragSource is not null)
        {
            if (DragPayload is { } payload && DragReleaseInterceptor?.Invoke(payload, windowPoint) == true)
                CancelDrag();
            else
                FinishDrag(x, y);
            ReleaseCapture();
            _dragCandidate = false;
            return;
        }
```

Beside `FinishDrag`:

```csharp
    /// <summary>The drag ends where it began: the source shows the item again, nothing is dropped.</summary>
    private void CancelDrag()
    {
        DragSource?.SetDragSourceActive(false, DragPayload);
        DragSource = null;
        DragPayload = null;
        _dragGhost = null;
        _lastDragHoverTarget = null;
    }
```

- [ ] **Step 4: Run the tests** (same command) → 4 pass. Then `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~UI."` → no new failures vs main.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/UiRoot.cs tests/AcDream.App.Tests/UI/UiRootDragReleaseInterceptorTests.cs
git commit -m "feat(ui): drag release interceptor on UiRoot"
```

---

### Task 3: Overlay knows the pointer is over an ImGui window during a retail drag

**Goal:** `ImGuiOverlay.PointerOverWindow` is true when the mouse is over any ImGui window, including during a retail drag (the overlay submits an external drag source each frame while `ExternalDragActive` says one is active).

**Files:**
- Modify: `src/AcDream.App/Extensions/Overlay/ImGuiOverlay.cs` (properties beside `WantsMouse` ~line 49; `BeginFrame` ~lines 268-305; resets where `WantsMouse = false` at ~lines 286, 367, 464)
- Modify: `tests/AcDream.App.Tests/Extensions/ImGuiOverlayTests.cs`

**Acceptance Criteria:**
- [ ] Mouse over a window with no button held → `PointerOverWindow` true.
- [ ] Button pressed outside ImGui, held, moved over the window, `ExternalDragActive` true → after two frames `PointerOverWindow` true and `WantsMouse` false.
- [ ] Same with `ExternalDragActive` false → `PointerOverWindow` false (documents why the extern source exists); off the window → false.

**Verify:** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ImGuiOverlayTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — add to `ImGuiOverlayTests`:

```csharp
    private void HoverWindow() => _overlay.AddFrameCallback(() =>
    {
        ImGui.SetNextWindowPos(new Vector2(100, 100));
        ImGui.SetNextWindowSize(new Vector2(200, 200));
        ImGui.Begin("drop");
        ImGui.End();
    });

    [Fact]
    public void TheMouseOverAWindowIsPointerOverWindow()
    {
        Start();
        HoverWindow();
        RunFrame();
        _overlay.Input.MouseMove(new Vector2(150, 150));
        RunFrame();
        RunFrame();
        Assert.True(_overlay.PointerOverWindow);
    }

    [Fact]
    public void ARetailDragOverAWindowIsPointerOverWindowButNotWantsMouse()
    {
        Start();
        HoverWindow();
        bool dragging = true;
        _overlay.ExternalDragActive = () => dragging;
        _overlay.Input.MouseMove(new Vector2(10, 10));
        RunFrame();
        _overlay.Input.MouseButton(MouseButton.Left, true);   // pressed outside ImGui: the app owns the click
        RunFrame();
        _overlay.Input.MouseMove(new Vector2(150, 150));
        RunFrame();
        RunFrame();
        Assert.True(_overlay.PointerOverWindow);
        Assert.False(_overlay.WantsMouse);

        _overlay.Input.MouseMove(new Vector2(20, 20));
        RunFrame();
        Assert.False(_overlay.PointerOverWindow);
    }

    [Fact]
    public void WithoutTheExternalSourceARetailDragNeverHovers()
    {
        Start();
        HoverWindow();
        _overlay.Input.MouseMove(new Vector2(10, 10));
        RunFrame();
        _overlay.Input.MouseButton(MouseButton.Left, true);
        RunFrame();
        _overlay.Input.MouseMove(new Vector2(150, 150));
        RunFrame();
        RunFrame();
        Assert.False(_overlay.PointerOverWindow);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ImGuiOverlayTests"`
Expected: build error — `PointerOverWindow` / `ExternalDragActive` not defined.

- [ ] **Step 3: Implement** in `ImGuiOverlay.cs`. Beside `WantsMouse`:

```csharp
    /// <summary>The mouse is over an ImGui window (last frame), also during a retail drag.</summary>
    public bool PointerOverWindow { get; private set; }

    /// <summary>
    /// A retail drag is in progress. ImGui stops hovering windows while a button pressed
    /// outside it is held, so during one the overlay submits an external drag source each
    /// frame, which keeps <see cref="PointerOverWindow"/> answering.
    /// </summary>
    internal Func<bool>? ExternalDragActive { get; set; }

    private const string ExternalDragPayload = "OPENAC_DRAG";
```

In `BeginFrame`, after `ImGui.NewFrame(); _frameOpen = true;`:

```csharp
            if (ExternalDragActive?.Invoke() == true && ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceExtern))
            {
                ImGui.SetDragDropPayload(ExternalDragPayload, IntPtr.Zero, 0);
                ImGui.EndDragDropSource();
            }

            PointerOverWindow = ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
            WantsMouse = io.WantCaptureMouse;
```

At each place that sets `WantsMouse = false` (zero-size window, abort/fail, dispose) also set `PointerOverWindow = false;`.

- [ ] **Step 4: Run the tests** (same command) → all pass.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/Extensions/Overlay/ImGuiOverlay.cs tests/AcDream.App.Tests/Extensions/ImGuiOverlayTests.cs
git commit -m "feat(overlay): PointerOverWindow, kept alive during retail drags"
```

---

### Task 4: `IClientItemDrops` and the App's release rule

**Goal:** Extensions see the dragged inventory item and can claim drops; the App consumes every drag let go over an ImGui window, offering only inventory drags to the claimer.

**Files:**
- Create: `src/AcDream.ClientExtensions/IClientItemDrops.cs`
- Modify: `src/AcDream.ClientExtensions/ClientExtensionContext.cs` (after `Input`)
- Create: `src/AcDream.App/Extensions/ClientItemDrops.cs`
- Modify: `src/AcDream.App/Extensions/ClientExtensionHost.cs` (`Attach` parameter + context)
- Modify: `src/AcDream.App/Rendering/GameWindow.cs` (~line 542 create, ~546 pass, ~1012 bind root, ~1719 unbind)
- Create: `tests/AcDream.App.Tests/Extensions/ClientItemDropsTests.cs`

**Acceptance Criteria:**
- [ ] `DraggedItem` = `ObjId` for an `Inventory` payload; 0 for `Equipment`, `ShortcutBar`, `Ground`, a non-item payload, or no drag.
- [ ] `InterceptRelease` off ImGui → false, claimer not called.
- [ ] Over ImGui, inventory payload → claimer called with (ObjId, point); returns true whether the claimer says true or false.
- [ ] Over ImGui, equipment payload → claimer not called; returns true.
- [ ] A claimer that throws is logged once per throw and the release still returns true.
- [ ] `ClientExtensionHost.Attach(itemDrops:)` puts it on `ClientExtensionContext.ItemDrops` (one-line pass-through, like `screen:`; the host has no test-extension seam, so it is covered by the build and Task 5's extension test + the live check).

**Verify:** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ClientItemDropsTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Numerics;
using AcDream.App.Extensions;
using AcDream.App.UI;

namespace AcDream.App.Tests.Extensions;

public sealed class ClientItemDropsTests
{
    private object? _payload;
    private bool _overImGui;
    private readonly List<string> _log = new();
    private readonly ClientItemDrops _drops;

    public ClientItemDropsTests() => _drops = new ClientItemDrops(() => _payload, () => _overImGui, _log.Add);

    private static ItemDragPayload Item(ItemDragSource source) => new(0x5001u, source, 0, null);

    [Theory]
    [InlineData(ItemDragSource.Inventory, 0x5001u)]
    [InlineData(ItemDragSource.Equipment, 0u)]
    [InlineData(ItemDragSource.ShortcutBar, 0u)]
    [InlineData(ItemDragSource.Ground, 0u)]
    public void OnlyAnInventoryDragIsTheDraggedItem(ItemDragSource source, uint expected)
    {
        _payload = Item(source);
        Assert.Equal(expected, _drops.DraggedItem);
        _payload = "not an item";
        Assert.Equal(0u, _drops.DraggedItem);
        _payload = null;
        Assert.Equal(0u, _drops.DraggedItem);
    }

    [Fact]
    public void OffImGuiTheReleaseIsLeftAlone()
    {
        int calls = 0;
        _drops.SetDropClaimer((_, _) => { calls++; return true; });
        Assert.False(_drops.InterceptRelease(Item(ItemDragSource.Inventory), new Vector2(5, 5)));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OverImGuiAnInventoryDragIsOfferedAndAlwaysConsumed(bool claimed)
    {
        _overImGui = true;
        (uint Item, Vector2 Point)? offered = null;
        _drops.SetDropClaimer((item, point) => { offered = (item, point); return claimed; });

        Assert.True(_drops.InterceptRelease(Item(ItemDragSource.Inventory), new Vector2(150, 160)));
        Assert.Equal((0x5001u, new Vector2(150, 160)), offered);
    }

    [Fact]
    public void OverImGuiAnEquipmentDragIsCancelledNotOffered()
    {
        _overImGui = true;
        int calls = 0;
        _drops.SetDropClaimer((_, _) => { calls++; return true; });
        Assert.True(_drops.InterceptRelease(Item(ItemDragSource.Equipment), Vector2.Zero));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void AThrowingClaimerIsLoggedAndTheDropStillCancelled()
    {
        _overImGui = true;
        _drops.SetDropClaimer((_, _) => throw new InvalidOperationException("boom"));
        Assert.True(_drops.InterceptRelease(Item(ItemDragSource.Inventory), Vector2.Zero));
        Assert.Contains(_log, line => line.Contains("boom", StringComparison.Ordinal));
    }
}
```

- [ ] **Step 2: Run to verify they fail** — build error: `ClientItemDrops` not defined.

- [ ] **Step 3: Implement.**

`src/AcDream.ClientExtensions/IClientItemDrops.cs`:

```csharp
using System.Numerics;

namespace AcDream.ClientExtensions;

/// <summary>
/// Items dragged out of the client's packs or main inventory, for extension panels that take
/// dropped items. Points are window points (the overlay's space). Game thread.
/// </summary>
public interface IClientItemDrops
{
    /// <summary>The object being dragged out of a pack or the main inventory now; 0 otherwise.</summary>
    uint DraggedItem { get; }

    /// <summary>
    /// The one claimer asked when such an item is let go over an overlay window: the item and
    /// the release point; true when a panel took it. Null removes it.
    /// </summary>
    void SetDropClaimer(Func<uint, Vector2, bool>? claimer);
}
```

`ClientExtensionContext.cs`, after `Input`:

```csharp
    /// <summary>Inventory items dragged over the overlay; null in a host without a retail UI.</summary>
    public IClientItemDrops? ItemDrops { get; init; }
```

`src/AcDream.App/Extensions/ClientItemDrops.cs`:

```csharp
using System.Numerics;
using AcDream.App.UI;
using AcDream.ClientExtensions;

namespace AcDream.App.Extensions;

/// <summary>
/// <see cref="IClientItemDrops"/> over the retained UI's drag and the overlay's hover. Its
/// <see cref="InterceptRelease"/> is the UI root's release interceptor: a drag let go over an
/// ImGui window never reaches the retail UI or the world; an inventory item is offered to the
/// claimer first, anything else is just cancelled.
/// </summary>
internal sealed class ClientItemDrops(Func<object?> dragPayload, Func<bool> pointerOverOverlay, Action<string> log)
    : IClientItemDrops
{
    private Func<uint, Vector2, bool>? _claimer;

    public uint DraggedItem => InventoryItem(dragPayload());

    public void SetDropClaimer(Func<uint, Vector2, bool>? claimer) => _claimer = claimer;

    public bool InterceptRelease(object payload, Vector2 windowPoint)
    {
        if (!pointerOverOverlay())
            return false;
        uint item = InventoryItem(payload);
        if (item != 0 && _claimer is { } claimer)
        {
            try { claimer(item, windowPoint); }
            catch (Exception error) { log($"[Extensions] item drop claimer failed: {error.GetType().Name}: {error.Message}"); }
        }

        return true;
    }

    private static uint InventoryItem(object? payload) =>
        payload is ItemDragPayload { SourceKind: ItemDragSource.Inventory } item ? item.ObjId : 0;
}
```

`ClientExtensionHost.Attach`: add `IClientItemDrops? itemDrops = null` as the last parameter and `ItemDrops = itemDrops,` in the context initializer.

`GameWindow.cs`:
- Field beside `_clientScreen`: `private readonly AcDream.App.Extensions.ClientItemDrops _itemDrops;`
- After `_clientScreen = new(_liveWorldOrigin);`:

```csharp
        _itemDrops = new(() => _uiHost?.Root.DragPayload, () => _overlay.PointerOverWindow,
            line => Serilog.Log.Warning("{ExtensionLine}", line));
        _overlay.ExternalDragActive = () => _uiHost?.Root.DragPayload is not null;
```

- In the `_extensions.Attach(...)` call add `itemDrops: _itemDrops` after the `input:` argument.
- After `_clientScreen.BindWindows(_retailUiRuntime.Host.WindowManager);` add `_uiHost.Root.DragReleaseInterceptor = _itemDrops.InterceptRelease;`
- Beside `_clientScreen.BindWindows(null);` at shutdown add `if (_uiHost is not null) _uiHost.Root.DragReleaseInterceptor = null;`

- [ ] **Step 4: Run the tests** (Verify command) → pass. Build the app: `dotnet build src/AcDream.App -c Debug` → 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.ClientExtensions/IClientItemDrops.cs src/AcDream.ClientExtensions/ClientExtensionContext.cs src/AcDream.App/Extensions/ClientItemDrops.cs src/AcDream.App/Extensions/ClientExtensionHost.cs src/AcDream.App/Rendering/GameWindow.cs tests/AcDream.App.Tests/Extensions/ClientItemDropsTests.cs
git commit -m "feat(extensions): IClientItemDrops; drags let go over ImGui never reach the retail UI"
```

---

### Task 5: Their UiDropTargets compiled, claimer wired

**Goal:** RynthCore's `UiDropTargets.cs` compiles unchanged in EngineUi with the cursor, pop-out and drag-object stand-ins, and the Rynth host claims inventory drops through it.

**Files:**
- Modify: `src/AcDream.Rynth.EngineUi/AcDream.Rynth.EngineUi.csproj` (Compile list; redirect `Exec` stand-in list ~line 206; comment above it)
- Create: `src/AcDream.Rynth.EngineUi/StandIns/Win32CursorStandIn.cs`
- Modify: `src/AcDream.Rynth.EngineUi/StandIns/DashboardStandIns.cs` (delete `UiDropTargets` class ~lines 254-283; `UiElementHooks` ~line 182)
- Modify: `src/AcDream.Rynth.EngineUi/StandIns/ChatEngineStandIns.cs` (`ImGuiPopOuts` ~line 381)
- Create: `src/AcDream.Rynth.EngineUi/RynthDropClaim.cs`
- Modify: `src/AcDream.Rynth.Host/RynthClientExtension.cs` (after `StartHotkeys` call ~line 128; new `StartDrops`; `Dispose`)
- Create: `tests/AcDream.Rynth.Host.Tests/RynthDropTargetsTests.cs`
- Modify: `tests/AcDream.App.Tests/Extensions/RynthOverlayExtensionTests.cs`

**Acceptance Criteria:**
- [ ] The build log of EngineUi shows `GetCursorPos` redirected (the redirect count goes up by 1 vs main) and their `UiDropTargets.cs` is linked as `RynthCore\UiDropTargets.cs`.
- [ ] A target published at (100,100)-(200,200) accepting: `RynthDropClaim.Claim(0x5001, (150,150))` → true, then `UiDropTargets.TryTake(key)` gives 0x5001 once.
- [ ] Release point outside the rect, a not-accepting target, or a target last published > 500 ms ago → `Claim` false, nothing to take.
- [ ] After `Claim` the cursor stand-in answers the default source again (not the release point).
- [ ] `UiElementHooks.CurrentDragObject` and `UiDropTargets.AnyDragInProgress` follow `IClientItemDrops.DraggedItem` while the extension is attached; after `Dispose` the claimer is null and `CurrentDragObject` is 0.

**Verify:** `dotnet test tests/AcDream.Rynth.Host.Tests` → all pass (baseline + new); `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~RynthOverlayExtensionTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests.** `tests/AcDream.Rynth.Host.Tests/RynthDropTargetsTests.cs` (use the project's existing collection attribute for static Rynth state — check another test in the folder, e.g. `RynthHotkeysTests.cs`, and copy it):

```csharp
using System.Numerics;
using AcDream.Rynth.EngineUi;
using RynthCore.Engine.ImGuiBackend;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthDropTargetsTests : IDisposable
{
    private static int _n;
    private readonly string _key = $"test-drop-{Interlocked.Increment(ref _n)}";

    public RynthDropTargetsTests() => Win32CursorStandIn.Source = () => new Vector2(-1, -1);

    public void Dispose() => Win32CursorStandIn.Source = null;

    [Fact]
    public void AReleaseOverAnAcceptingTargetIsTakenOnce()
    {
        UiDropTargets.Publish(_key, "Item Count", new Vector2(100, 100), new Vector2(200, 200), accepting: true);

        Assert.True(RynthDropClaim.Claim(0x5001u, new Vector2(150, 150)));
        Assert.True(UiDropTargets.TryTake(_key, out uint item));
        Assert.Equal(0x5001u, item);
        Assert.False(UiDropTargets.TryTake(_key, out _));
    }

    [Fact]
    public void OutsideOrNotAcceptingIsNotClaimed()
    {
        UiDropTargets.Publish(_key, "Item Count", new Vector2(100, 100), new Vector2(200, 200), accepting: true);
        Assert.False(RynthDropClaim.Claim(0x5001u, new Vector2(250, 150)));

        UiDropTargets.Publish(_key, "Item Count", new Vector2(100, 100), new Vector2(200, 200), accepting: false);
        Assert.False(RynthDropClaim.Claim(0x5001u, new Vector2(150, 150)));
        Assert.False(UiDropTargets.TryTake(_key, out _));
    }

    [Fact]
    public void AStaleTargetIsNotClaimed()
    {
        UiDropTargets.Publish(_key, "Item Count", new Vector2(100, 100), new Vector2(200, 200), accepting: true);
        Thread.Sleep(600);   // their StaleMs is 500
        Assert.False(RynthDropClaim.Claim(0x5001u, new Vector2(150, 150)));
    }

    [Fact]
    public void TheCursorIsTheDefaultSourceAgainAfterAClaim()
    {
        UiDropTargets.Publish(_key, "Item Count", new Vector2(100, 100), new Vector2(200, 200), accepting: true);
        RynthDropClaim.Claim(0x5001u, new Vector2(150, 150));
        Assert.False(UiDropTargets.IsCursorOver(_key));   // default source is (-1,-1)
    }
}
```

Other tests' targets may still be published (static, keyed); a unique key per test and release points inside only this test's rect keep them apart. If another live target overlaps (100..200), move this test's rect to (1000,1000)-(1100,1100) and adjust the points.

Add to `RynthOverlayExtensionTests`:

```csharp
    private sealed class FakeItemDrops : IClientItemDrops
    {
        public uint DraggedItem { get; set; }
        public Func<uint, Vector2, bool>? Claimer { get; private set; }
        public void SetDropClaimer(Func<uint, Vector2, bool>? claimer) => Claimer = claimer;
    }

    [Fact]
    public void WithAnOverlayDropsAreClaimedAndTheDragIsSeenUntilDispose()
    {
        var drops = new FakeItemDrops { DraggedItem = 0x5001u };
        var extension = new RynthClientExtension();
        ClientExtensionContext context = Context(GameRuntimeTestFactory.Create(), new FakeOverlay());
        extension.Attach(new ClientExtensionContext
        {
            Runtime = context.Runtime, Log = context.Log, RegisterChatCommand = context.RegisterChatCommand,
            Overlay = context.Overlay, StateDirectory = context.StateDirectory, ItemDrops = drops,
        });

        Assert.NotNull(drops.Claimer);
        Assert.Equal(0x5001u, UiElementHooks.CurrentDragObject);
        Assert.True(UiDropTargets.AnyDragInProgress);

        extension.Dispose();
        Assert.Null(drops.Claimer);
        Assert.Equal(0u, UiElementHooks.CurrentDragObject);
    }
```

- [ ] **Step 2: Run to verify they fail** — build errors: `Win32CursorStandIn`, `RynthDropClaim` not defined.

- [ ] **Step 3: Implement.**

`StandIns/Win32CursorStandIn.cs`:

```csharp
// Stand-in for the user32 GetCursorPos RynthCore's UiDropTargets reads to hit-test its drop
// targets. UiDropTargets is compiled unchanged; after compile, tools/RynthInteropRedirect points
// its GetCursorPos at the method here (their POINT is private, so CursorPoint has its layout).
using System;
using System.Numerics;

namespace AcDream.Rynth.EngineUi;

/// <summary>
/// The cursor as UiDropTargets asks Windows for it, in ImGui points (OpenAC has one window and
/// no client offset): the host's source (the overlay mouse), or the release point while a drop
/// is being claimed. Game thread.
/// </summary>
internal static class Win32CursorStandIn
{
    public struct CursorPoint
    {
        public int X;
        public int Y;
    }

    /// <summary>The overlay mouse; null: no cursor (nothing is ever under it).</summary>
    internal static Func<Vector2>? Source { get; set; }

    /// <summary>Set by <see cref="RynthDropClaim"/> for the length of one claim.</summary>
    internal static Vector2? Override { get; set; }

    public static bool GetCursorPos(out CursorPoint p)
    {
        Vector2? point = Override ?? Source?.Invoke();
        p = point is { } v ? new CursorPoint { X = (int)MathF.Floor(v.X), Y = (int)MathF.Floor(v.Y) } : default;
        return point is not null;
    }
}
```

`RynthDropClaim.cs`:

```csharp
using System.Numerics;
using RynthCore.Engine.ImGuiBackend;

namespace AcDream.Rynth.EngineUi;

/// <summary>
/// An inventory item let go over an overlay window, offered to RynthCore's drop targets as
/// their UIElement drop detour does: the cursor reads the release point while their TryClaim
/// hit-tests, and the face takes the item with TryTake on its next draw. Game thread.
/// </summary>
internal static class RynthDropClaim
{
    internal static bool Claim(uint itemId, Vector2 point)
    {
        Win32CursorStandIn.Override = point;
        try
        {
            return UiDropTargets.TryClaim(itemId, 0, out _);
        }
        finally
        {
            Win32CursorStandIn.Override = null;
        }
    }
}
```

`DashboardStandIns.cs`: delete the whole `internal static class UiDropTargets { ... }` (and its `<summary>`), and change `UiElementHooks` to:

```csharp
    /// <summary>RynthCore's acclient UI hooks; only the item being dragged out of a pack or the inventory (the host's source).</summary>
    internal static class UiElementHooks
    {
        /// <summary>The client's dragged inventory item; null: no drag is ever seen.</summary>
        internal static Func<uint>? DragSource { get; set; }

        public static uint CurrentDragObject => DragSource?.Invoke() ?? 0;
    }
```

(add `using System;` at the top of that namespace block if the file doesn't have it).

`ChatEngineStandIns.cs`, `ImGuiPopOuts`:

```csharp
    /// <summary>RynthCore's pop-out windows; OpenAC has none.</summary>
    internal static class ImGuiPopOuts
    {
        public static bool InPopOutFrame => false;

        /// <summary>RynthCore's game window client origin on the desktop; OpenAC's cursor is already window points.</summary>
        public static System.Numerics.Vector2 ClientOriginOnScreen() => System.Numerics.Vector2.Zero;

        /// <summary>No pop-out window to be over.</summary>
        public static bool IsCursorOver(string panelTitle) => false;
    }
```

`AcDream.Rynth.EngineUi.csproj`:
- In the Compile list, after the `ItemCountFace.cs` line:
  `<Compile Include="$(RynthEngine)ImGui\UiDropTargets.cs" Link="RynthCore\UiDropTargets.cs" />`
- In the redirect `Exec`, change the stand-in list to `AcDream.Rynth.EngineUi.Win32ClipboardStandIn,AcDream.Rynth.EngineUi.Win32KeyStateStandIn,AcDream.Rynth.EngineUi.Win32CursorStandIn`.
- Add to the comment above: `UiDropTargets' GetCursorPos goes to Win32CursorStandIn.`

`RynthClientExtension.cs`: after `StartHotkeys(context, keyOverlay);` add

```csharp
        if (context.Overlay is not null && context.ItemDrops is { } drops)
            StartDrops(drops);
```

and the method + field:

```csharp
    private IClientItemDrops? _itemDrops;

    /// <summary>RynthCore's drop targets: items dragged out of the client's packs land on their panels.</summary>
    private void StartDrops(IClientItemDrops drops)
    {
        _itemDrops = drops;
        RynthCore.Engine.Compatibility.UiElementHooks.DragSource = () => drops.DraggedItem;
        Win32CursorStandIn.Source = () => ImGuiNET.ImGui.GetIO().MousePos;
        drops.SetDropClaimer(RynthDropClaim.Claim);
    }
```

In `Dispose`, beside the hotkeys teardown:

```csharp
        if (_itemDrops is { } drops)
        {
            drops.SetDropClaimer(null);
            _itemDrops = null;
        }
        RynthCore.Engine.Compatibility.UiElementHooks.DragSource = null;
        Win32CursorStandIn.Source = null;
```

`ImGui.GetIO()` is only read on the game thread while the overlay's context is current (faces draw inside the overlay frame; claims override it).

- [ ] **Step 4: Build and run.** Before Step 3, note the redirect count: `dotnet build src/AcDream.Rynth.EngineUi -c Debug -v n --no-incremental | grep -i "P/Invoke(s) redirected"`. After Step 3, the same command → one higher. Then run the Verify commands → all pass.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Rynth.EngineUi src/AcDream.Rynth.Host/RynthClientExtension.cs tests/AcDream.Rynth.Host.Tests/RynthDropTargetsTests.cs tests/AcDream.App.Tests/Extensions/RynthOverlayExtensionTests.cs
git commit -m "feat(rynth): RynthCore's drop targets take items dragged out of packs"
```

---

### Task 6: Gate and live check (user)

**Goal:** Full gate green against baseline, Release build, and the user's live check passes on the local ACE server.

**Files:**
- Modify: `docs/superpowers/plans/2026-10-21-rynth-drop-targets.md` (tick results)

**Acceptance Criteria:**
- [ ] `dotnet test tests/AcDream.Rynth.Host.Tests` all pass; `dotnet test tests/AcDream.Tools.RynthInteropRedirect.Tests` all pass; `dotnet test tests/AcDream.App.Tests` failures = the 59 on main (same names).
- [ ] `dotnet build AcDream.slnx -c Release` → 0 errors.
- [ ] Live (user, local ACE 10.10.20.20 per live-testing rules; ask before launching): pack item onto Item Count adds it; onto the Loot Editor makes a rule; onto a Mini Remote slot fills that slot; the panels show their drop look while dragging; item dropped on the dashboard / chat stays in the pack; paperdoll item dropped on a Remote slot stays worn; their Inventory panel item onto a Remote slot fills it; normal pack-to-pack and drop-to-ground drags unchanged; on the Retina display the hit lands where the cursor is.

**Verify:** test commands above; live checklist ticked by the user.

**Steps:**

- [ ] **Step 1:** Run the three test commands and the Release build. If App failures are not 59, compare names against main by running the same App test command in a worktree of `main`.
  Result 2026-10-21 (head 5685e1a9): Host 450/450, Redirect 23/23, App 174 failures = the identical set on main 0a94aae4 (installed-dat / live-mount probes), Release 0 errors.
- [ ] **Step 2:** Ask the user when they're available for the live check; do not launch a client before they say so. Also check (final review): a retail slot's drop highlight clears after a drop cancelled over a Rynth panel; no box follows the cursor in an ordinary pack-to-pack drag; retail keys and Rynth hotkeys work mid-drag; whether the drag ghost stays visible over a Rynth panel; drops right beside the dashboard drawer tabs (hover padding may cancel them); a quick flick-and-release onto a panel (hover is one frame old).
- [ ] **Step 3:** Record each live item's result in this task; fix wave for any failure before finishing the branch.
