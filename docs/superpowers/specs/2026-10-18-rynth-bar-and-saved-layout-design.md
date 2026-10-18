# Rynth Launcher Bar and Saved Window Layout

Date: 2026-10-18. Branch: `feat/rynth-imgui`. Builds on
`2026-10-17-rynth-imgui-script-windows-design.md`, whose "Out of scope" list
named saved window positions and RynthCore's window bar.

## Goal

1. Script windows (RynthOracle and any later display plugin) come back where
   the player left them, at the size they left them, and the ones left open
   reopen when their plugin registers them again (next login, next launch).
2. A small draggable launcher bar opens and closes script windows that ask to
   be on it, so the player doesn't need `/ro`. Its position is saved too.

Layout is saved **per install** (one layout for every character), as
RynthCore's `panel_state.txt` is.

## Decisions

- "Window bar" means RynthCore's overlay launcher bar (`ImGuiBar.cs`), not the
  per-window title bar. Script windows keep ImGui's standard title bar.
- Approach A: our own layout file and our own small bar. Not ImGui's ini file
  (global to the overlay, fights the windows' own position requests), and not
  a port of `ImGuiBar.cs` (Phosphor fonts, pop-outs, row layout, router,
  settings UI).
- Per window we save position, size and open state.

## Architecture

```
ClientExtensionHost ──StateDirectory──▶ RynthClientExtension
                                            │ creates
                                            ▼
                         RynthLayoutStore (AcDream.Rynth.Host)
                          implements IPanelLayout ◀── ImGuiPanelHost.Layout
                                            ▲              (stand-in, ScriptWindows)
                         RynthBar (AcDream.Rynth.Host) ─ reads/writes bar position
                          reads ScriptWindowRegistry.BarButtons / BarItems
                          toggles via ScriptWindowRegistry.PlayerSetVisible
```

Our new code lives in `AcDream.Rynth.Host` (warnings as errors, analyzers on).
The ScriptWindows wrapper project (warnings off, for RynthCore's files) gains
only the `IPanelLayout` interface and the panel host changes.

### 1. The state folder (upstream contract)

- `ClientExtensionContext` gains `public string? StateDirectory { get; init; }`:
  a folder the extension may keep its own files in; null = don't save.
- `ClientExtensionHost.Attach` takes `string? stateDirectory = null` and
  passes it on. `GameWindow` passes
  `_applicationPaths.PluginFilesDirectory("rynth")` (that is,
  `<data>/plugins/rynth/files`).
- About 3 changed upstream lines. The extension creates the folder when it
  first saves.

### 2. `RynthLayoutStore` (new, `AcDream.Rynth.Host`)

- File: `<StateDirectory>/rynth-layout.json`:
  ```json
  { "version": 1,
    "bar": { "x": 12, "y": 40 },
    "windows": { "script:RynthOracle/Oracle": { "x": 300, "y": 120, "w": 520, "h": 480, "open": true } } }
  ```
  All values in logical units (divided by `EngineFrameController.FontScale`,
  which is 1 today).
- Loads once at construction. A missing file is an empty layout. A file that
  doesn't parse is logged once (`[Rynth] layout file unreadable: ...`), kept
  as is (not overwritten until the next save) and treated as empty.
- Saves by writing `rynth-layout.json.tmp` and moving it over the real file.
  Saves happen only on a change (see below), so no timer or background writer.
  A failed save is logged and the in-memory layout stays current.
- Null `StateDirectory`: an in-memory store that never touches disk.
- Implements `IPanelLayout` (below) plus `BarPosition` get/set for the bar.

### 3. Panel host changes (`ImGuiPanelHost` stand-in)

New interface in the ScriptWindows project:

```csharp
internal readonly record struct PanelRect(Vector2 Pos, Vector2 Size);
internal readonly record struct PanelPlacement(PanelRect? Rect, bool Open);  // Rect null = never placed by the player
internal interface IPanelLayout
{
    PanelPlacement? Get(string key);           // null = nothing saved
    void SetRect(string key, PanelRect rect);  // keeps Open
    void SetOpen(string key, bool open);       // keeps Rect
}
```

`ImGuiPanelHost.Layout` (`internal static IPanelLayout? Layout`) is set by the
Rynth host and cleared at its dispose, like `Log`.

- **First draw after an open:** if `Layout.Get(key)` has a placement, the
  window gets `SetNextWindowPos/Size(..., Always)` once with it, the position
  clamped so the window stays on the display. Otherwise the current
  `DefaultSize`/`DefaultPos` `FirstUseEver` path runs unchanged.
- **Plugin requests** (`RequestSize`/`RequestPos`) still apply with `Always`
  and win over a saved placement in the same frame. They are not saved: after
  applying one, the host takes the window's resulting rectangle as its
  baseline, so only a later player move or resize saves.
- **Every frame, after `Begin`:** the host reads `GetWindowPos`/`GetWindowSize`.
  When they differ from the baseline and the left mouse button is up, it calls
  `Layout.SetRect` with the new rectangle and updates the baseline. That is one save per finished drag or resize.
- **Open/close:** `Open` and `Close` record the open state with
  `Layout.SetOpen` (the saved rectangle is kept). Closing by the title bar's X goes through
  `Close`, so it's covered.
- **Unregister** (logout, plugin unload) and **Reset** don't touch the
  layout: a window removed while open stays saved as open.
- **Restore open:** when `RegisterDynamic` sees a new key whose saved
  placement is `Open`, it marks the entry `RestoreOpen`. The next `DrawAll`
  (game thread, outside the registry's lock) clears the mark and calls
  `ScriptWindowRegistry.PlayerSetVisible(<plugin key>, true)`, the plugin key
  being the panel key without the `script:` prefix. That is how the bar opens
  a window: the plugin gets a Visibility event and its own earlier "hidden"
  flag is stale, so its next submit doesn't close the window again. If the
  window is already open (the plugin's own flag opened it) nothing happens.

### 4. `RynthBar` (new, `AcDream.Rynth.Host`)

- One ImGui window, `"##RynthBar"`, flags `NoTitleBar | NoResize |
  AlwaysAutoResize | NoSavedSettings | NoScrollbar | NoCollapse | NoNav`, drawn
  from the extension's frame callback right after `ImGuiPanelHost.DrawAll`.
- Drawn only while there's something on it: at least one `BarButtons` or
  `BarItems` entry. (No plugin windows = no bar.)
- Contents, left to right:
  - a grip (`::`) that moves the bar while dragged;
  - one button per `ScriptWindowRegistry.BarButtons` item, labelled with the
    title's first letter (real icons come later), tooltip = title, shown
    highlighted while that window is open; a click toggles it with
    `PlayerSetVisible(item.Window, !open)`;
  - when `BarItems` isn't empty, a "Scripts" button that opens a popup with
    one selectable line per item (title, ticked while open; same toggle).
- Position: from `RynthLayoutStore.BarPosition` on the first draw (`Always`,
  clamped to the display), else a default top-left spot `(12, 40)`. Saved
  when it changed and the left mouse button is up, like the windows.
- The bar takes the mouse like any ImGui window, so clicks on it don't reach
  the game (the overlay's existing `WantsMouse` capture).

### 5. Wiring (`RynthClientExtension` / `RynthHost`)

- `Attach` creates the store from `context.StateDirectory`, sets
  `ImGuiPanelHost.Layout`, creates the bar, and registers one frame callback
  that runs `ImGuiPanelHost.DrawAll(); bar.Draw();`.
- `Dispose` removes the callback and clears `ImGuiPanelHost.Layout` if it is
  still ours.

## Error handling

- Disk errors (read or write) are logged and never thrown into the frame.
- The bar's draw is guarded like a window body: an exception is logged, the
  bar's `Begin` still gets its `End`, and after 3 failures the bar stops
  drawing for the session.
- A saved position off the current display (smaller window, other monitor)
  is clamped onto it when applied; the saved value is only replaced when the
  player moves the window.

## Testing

Headless ImGui tests (`tests/AcDream.Rynth.Host.Tests`, `HeadlessImGui`):

- `RynthLayoutStore`: round-trip save/load in a temp folder; missing file is
  empty; corrupt file logs once, is treated as empty, and isn't overwritten
  until a save; null folder never writes.
- Panel host with a fake `IPanelLayout`:
  - a saved placement positions the window on its first frame;
  - a simulated drag (set `io.MousePos`/`MouseDown` across frames, or
    `ImGui.SetWindowPos` inside the body) then mouse-up calls `SetRect` once;
  - a plugin `RequestPos` is applied and does not call `SetRect`;
  - `Open`/`Close` record the open state; `Unregister` doesn't;
  - a saved-open key registered through the real registry (Oracle pipeline,
    as in `ScriptWindowPipelineTests`) is open after the next `DrawAll`, and
    the owner received a Visibility event.
- `RynthBar`: with a `BarItems` entry it draws a window; a click toggles the
  window (drive via the toggle method directly rather than a screen click);
  with no entries it draws nothing.
- The extension sets `ImGuiPanelHost.Layout` while attached with an overlay
  and clears it at dispose (App tests).

Live check (asks the user first; local ACE only): `/ro`, move and resize the
Oracle window, log out and in → it's back in place and open; close it,
relaunch → it stays closed until opened from the bar's Scripts menu; drag the
bar, relaunch → the bar is where it was left.

## Out of scope

Real icons (next), RynthCore's styled window header, bar rows/size/pop-out,
per-character layouts, ImGui's ini file.
