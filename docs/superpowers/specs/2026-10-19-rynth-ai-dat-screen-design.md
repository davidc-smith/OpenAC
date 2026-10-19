# RynthAi #6: dat geometry and screen slots — design

Date: 2026-10-19. Status: section 1 approved; section 2 written from the recommended
design, for review with this spec.

## Goal

RynthAi's dat-backed features work in OpenAC: line of sight in combat, indoor nav
detours, dungeon map bake, terrain passability overlay, dungeon hazard surfaces and the
spell component database. Radar walls and the 2D nav path overlay draw over OpenAC's
radar and 3D view.

This is sub-project #6 of the RynthAi split (#1 dashboard, #2 state and actions,
#3 combat and buffs, #4 navigation and Nav3D, #5 loot/salvage/vendor/trade).

## Decisions

- **Their code, unchanged.** RynthAi compiles from the submodule as-is. Their own dat
  reader (`RynthCore.TerrainData.DatDatabase`) and `GeometryLoader` do all dat work.
- **Scope B** (user, 2026-10-19): the dat path plus `GetRadarRectFn`, `WorldToScreenFn`
  and `GetViewportSizeFn`. Outdoor detours (`GetPluginInterfaceFn` + a RynthNav build +
  its baked DotRecast tiles) are left for a later sub-project; RynthAi keeps its
  jump/side-step ladder outdoors, as it does today.
- **The dat directory reaches RynthAi through a registry stand-in** (user, 2026-10-19),
  not by redirecting `Environment.ProcessPath` and not by symlinking dats.
- **No contract bump.** The screen data reaches the Rynth host through
  `ClientExtensionContext`, which is not part of the plugin contract. Contract stays
  `0.1.25-fork.9`.
- All three slots pass their version gates already (`ApiVersion` 86; radar needs 53).

## Feasibility (checked 2026-10-19)

A throwaway build of their `Tools/RynthCore.DatReaderCheck` (`geo` mode) on macOS
against `~/AsheronsCall`: `GeometryLoader.Initialize` 978 ms; portal 79,694 entries,
cell 805,348; all 3,409 dungeon landblocks and 4,096 sampled outdoor landblocks
through `DungeonLOS` with no errors. Their reader reads our dats unchanged.

## Section 1: dats through a registry stand-in

### How RynthAi looks for the dats today

`RynthAiPlugin` (their `RynthAiPlugin.cs:519-536`) passes the exe's directory only when
a portal dat sits beside it (true under acclient injection, false for OpenAC).
Otherwise `GeometryLoader.FindACFolder()` tries fixed `C:\` / `D:\` paths, then
`Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Turbine\Asheron's Call")`
→ `GetValue("InstallPath")`. `ComponentDatabase` (`Combat/ComponentDatabase.cs:286`)
does the same registry read to find `client_portal.dat`. Off Windows the registry
throws `PlatformNotSupportedException`, which both catch, so today `raycast init=False`.

### The stand-in

`src/AcDream.Rynth.Plugin.RynthAi/StandIns/RegistryStandIn.cs`:

- `RegistryStandIn` — static class with `LocalMachine` (a `RegistryKeyStandIn` for the
  root) and `static string? InstallPath { get; set; }`.
- `RegistryKeyStandIn : IDisposable` — `OpenSubKey(string name)` answers a key only for
  `SOFTWARE\WOW6432Node\Turbine\Asheron's Call` (case-insensitive, as Windows) and only
  while `InstallPath` is set; otherwise null. `GetValue(string name)` answers
  `InstallPath` for `"InstallPath"` (case-insensitive), else null. `Dispose()` does
  nothing.
- Only the members RynthAi calls exist. A member they call later that the stand-in
  lacks fails the redirect at build time (below), not at run time.

### The redirect: a new type mode in RynthInteropRedirect

Today `tools/RynthInteropRedirect` only retargets P/Invokes. It gains
`--redirect-type <from>=<to>` (repeatable; `<to>` is a type in a referenced assembly):

- Every `MethodReference` whose declaring type is `<from>` is rebound to the member
  of `<to>` with the same name, static-ness and parameter types (after mapping
  `<from>` types to `<to>` types). No match → the tool fails with the method's name.
- Every local variable, field, parameter, return and generic argument typed `<from>`
  is retyped to `<to>`, so `using (var key = …)` stores the stand-in.
- `Microsoft.Win32.Registry=…RegistryStandIn` and
  `Microsoft.Win32.RegistryKey=…RegistryKeyStandIn` are passed by the RynthAi
  wrapper's `MakeWin32Inert` target, on every OS (as the clipboard redirect).
- The tool then checks that nothing references a redirected type:
  after the rewrite the assembly has no reference to either `<from>` type.

### Setting the path

`ClientExtensionContext` gains `string? DatDirectory`, set by the App's
`ClientExtensionHost` from the dat directory the client loaded (the resolved
`ACDREAM_DAT_DIR`). `RynthClientExtension` sets `RegistryStandIn.InstallPath` from it
before the host loads RynthAi (beside `RynthInstallFolders.Apply()`), because RynthAi
starts its raycast init on a thread-pool thread in `Startup`. Null → the stand-in
answers nothing and RynthAi logs "AC installation folder not found", as today.

### What comes back

Line of sight (combat target filtering, missile arc), which also ends the
permanent `RaycastWarmingUp` state, so the no-progress blacklist and cast-miss judging
run; indoor detours (`NavRecovery.PlanIndoor`); dungeon map bake; terrain passability
overlay; dungeon hazard surfaces; the spell component database from `client_portal.dat`.
Their caches go under `RynthInstallPaths.RynthAiDir`, which already resolves under
ApplicationData off Windows. The signal: `RynthAi: raycast init=True … acDir=(auto)`.

## Section 2: screen slots

### Units and frame

All three answer in **logical points**, the space ImGui's `DisplaySize` uses
(`ImGuiOverlay.cs:195-197`) and the retained UI uses (`Root` = `window.Size`). No
Retina scaling in the host. World input is RynthCore's: metres from the player's
landblock, X east, Y up, Z north — the frame `RynthNav3D` already takes.

### Client side: `IClientScreen` (new, `AcDream.ClientExtensions`)

```csharp
public interface IClientScreen
{
    bool TryGetViewportSize(out Vector2 size);                       // points
    bool TryProject(PluginNavigationPosition position, out Vector2 screen); // points
    bool TryGetRadarDisc(out Vector2 centre, out float radius);       // points
}
```

`ClientExtensionContext.Screen` carries it; App implements it as
`App/Extensions/ClientScreen.cs`:

- **Viewport:** `SelectionCameraSource.UiSnapshot()`'s `Viewport` (`window.Size`);
  false when either side is 0.
- **Project:** false while `LiveWorldOriginState` is not known or no camera snapshot;
  else `PluginNavigationProjection.ToWorld(position, centreX, centreY)` (the
  conversion the world-triangle layer uses), then the math of
  `Core/Selection/ScreenProjection` with the snapshot's View × Projection: false when
  clip W ≤ 0.001 (RynthCore's cut-off), else NDC → points, Y down. Points off-screen
  are still returned with true, as RynthCore does.
- **Radar disc:** false unless the radar window is visible
  (`WindowManager.IsVisible(WindowNames.Radar)`) and mounted; centre =
  radar `ContentRoot.ScreenPosition + UiRadar.Center`, radius =
  `RadarController.RadarPixelRadius` (50).

### Host side

`IRynthGameView` gains `TryWorldToScreen(float x, float y, float z, out float sx,
out float sy)`, `TryGetViewportSize(out uint w, out uint h)` and
`TryGetRadarRect(out int x0, out int y0, out int x1, out int y1)`.
`RuntimeRynthGameView` takes the `IClientScreen?` (null → all false):

- **WorldToScreen:** anchors on the player's current cell (as `RynthNav3D`), then
  `RuntimeNavigationPositions.FromCellLocal(cell, (x, z, y))` → `IClientScreen.TryProject`.
- **ViewportSize:** rounds the points size to `uint`.
- **RadarRect:** RynthCore returns the radar element's surface box and RynthAi draws at
  `min(w, h) / 2 − 2`. The host returns the square around the disc with radius + 2,
  rounded, so RynthAi's walls land on OpenAC's disc edge.

`RynthApiTable.Screen.cs` binds `WorldToScreenFn`, `GetViewportSizeFn`,
`GetRadarRectFn` in the usual way (marshal, catch → "threw" line, answer 0; a null
out-pointer answers 0). Slots run on the game thread; RynthAi calls them from its
render callback, which OpenAC runs on the same thread.

### What comes back

Radar walls (`RadarWallRenderer`, also needs section 1's `DungeonLOS`) and the 2D
screen-projected nav path in `NavMarkerRenderer` (the 3D Nav3D markers already work).

## Testing

- **RynthInteropRedirect:** unit tests on a small fixture assembly: calls, locals and
  `using` retyped; a missing stand-in member fails the run naming it; no reference to
  the `<from>` type remains.
- **Stand-in:** key and value lookups, case-insensitivity, null when `InstallPath` unset.
- **Wrapper build check:** the built RynthAi assembly references neither
  `Microsoft.Win32.Registry` nor `RegistryKey`.
- **Host (`AcDream.Rynth.Host.Tests`, `FakeGameView`):** the three slots marshal and
  answer 0 when the view says false or a pointer is null; WorldToScreen anchors and
  swaps axes as Nav3D; radar rect is disc ± (radius + 2).
- **App:** `ClientScreen` projection matches `ScreenProjection` for a known camera;
  radar disc false when hidden.
- **Live check (user, later):** `raycast init=True` in the log; combat skips a target
  behind a wall; indoor stuck → detour instead of only jumping; dungeon map bakes;
  radar walls line up with the radar disc and turn with the player; 2D path overlay
  sits on the route; a Retina display and a moved radar window.

## Out of scope

`GetPluginInterfaceFn`, RynthNav and its nav tiles (outdoor detours);
`SetRadarSuppressed` and other radar hooks; any change to RynthSuite.
