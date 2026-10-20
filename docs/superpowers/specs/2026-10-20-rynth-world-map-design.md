# RynthAi dashboard: World Map, Mini Map and Radar

Date: 2026-10-20. Branch `feat/rynth-world-map`.

## Goal

RynthCore's last three map panels work in OpenAC:

- **World Map**: a pannable, zoomable picture of Dereth (NavData `worldmap.rmap`
  tiles, `worldmap.vec` building and road outlines) with grid, places from the
  Atlas, RynthNav's route, walk path and arrow, the player, a find box and the
  right-click menu (Go here, Route here, Arrow here, Copy coordinates).
- **Mini Map**: the same view in a small window with its own zoom, Follow and
  Rotate.
- **Radar**: the round/square radar with markers, hover text (name, distance,
  health), click-to-select, settings popup (legend), and outdoors the World Map
  picture drawn behind it.

Their source compiles unchanged (rule: [[rynth-no-rewrite]]); everything
missing is a stand-in. User decisions 2026-10-20: all three panels in one
sub-project (they share the tile layer), Radar last; DXT1 map packs supported
by decoding to RGBA in the stand-in (with RynthCore's own `DxtUtil`).

Out of scope: the Characters layer's data (RynthNet is not in OpenAC — the
layer is there and stays empty, as in RynthCore without RynthNet); installing
the optional map packs (our navdata pin has the Standard map only; a pack
dropped into `MapPacks` by hand is used).

## Findings

- The installed `worldmap.rmap` (NavData pin) is version 1: flat RGB tiles,
  2 px per cell, 2048 x 2048 cells, 5 levels. `worldmap.vec` is installed too.
- `WorldMapTiles` touches Direct3D 9 only through `DX9Backend.CreateTextureRgba`,
  `CreateTextureDxt1` and `ReleaseTexture`. We already stand `DX9Backend` in over
  the overlay's texture upload (Inventory's retail art), so `WorldMapTiles`
  compiles unchanged; its `BeginFrame(device)` and `Shutdown()` are called by
  RynthCore's `EngineFrameController`, which we don't compile, so the host calls
  them.
- A compile probe (throwaway worktree, 2026-10-20) adding the files below
  reported only these gaps: `UiSources.Characters`,
  `ClientObjectHooks.TryGetSnapshotPosition` / `LiveObjectIds`,
  `PlayerVitalsHooks` / `PlayerVitalsSnapshot`, `TeleportStateHooks`,
  `DX9Backend.CreateTextureDxt1`; for the Radar also `PanelSpec.GoldGrip`,
  `ImGuiPanelHost.TextSizeMenu`, `Hud.MonsterHudData` / `Hud.HealthObservation`,
  `AvaloniaOverlay.RadarCtrlGatedClickThrough`,
  `AcMainThreadQueue.EnqueueOverlaySelect`. (The probe stops at the first
  errors per file; the plan's build steps may find a few more of the same kind.)
- Once `WorldMapFace` is registered, RynthCore's own buttons light up: the
  dashboard's World Map button (`RynthAiFace`), the RynthNav panel's World Map
  and Mini Map buttons (`RynthNavFace`), and the map view's "Open the World
  Map" menu item. RynthCore's bar has a Radar (compass) button; our
  `RynthBarIcons` already maps "Radar" to the compass.
- `AvaloniaOverlay.RadarCtrlGatedClickThrough` is read only by RynthCore's
  Avalonia overlay, not by the ImGui face, so a plain field is parity.

## RynthCore files compiled unchanged (AcDream.Rynth.EngineUi)

- `ImGui/WorldMapTiles.cs`, `ImGui/WorldMapVectors.cs`
- `ImGui/Panels/WorldMapFace.cs` (also holds `WorldMapFeeds`),
  `MiniMapFace.cs`, `WorldMapView.cs`, `WorldMapTileDrawer.cs`,
  `WorldMapSettings.cs`, `WorldMapMaps.cs`, `MapProjection.cs`
- `UI/Data/CharactersData.cs`, `UI/Data/CharactersModel.cs`
- `ImGui/Panels/RadarFace.cs`

Removed: `StandIns/RadarFaceStandIn.cs`, and the placeholder `WorldMapFace` and
`MiniMapFace` classes in `StandIns/DashboardStandIns.cs`.

## Stand-in changes

All in `AcDream.Rynth.EngineUi/StandIns` unless noted; each new hook is a
settable delegate the host fills and clears (the existing pattern, e.g.
`RynthInventoryHookup`), returning "no data" when unset.

- `DX9Backend.CreateTextureDxt1(device, w, h, blocks)`: decodes with
  RynthCore's own `RynthCore.Imaging.DxtUtil.DecompressDxt1` (already compiled
  into EngineUi for the Skills icons) and uploads the RGBA through the existing
  `Upload`. `CreateTextureRgba` and `ReleaseTexture` unchanged.
- `ClientObjectHooks.LiveObjectIds` (uint[]; empty when unset) and
  `TryGetSnapshotPosition(id, out cell, out x, out y, out z)`: the host answers
  from `IRynthGameView.LiveObjectIds` and `TryGetObjectPosition` (landblock-local
  origin, as RynthCore's snapshot position).
- `PlayerVitalsHooks.TryGetSnapshot(out PlayerVitalsSnapshot)` with the fields
  `CharactersData` reads: host answers from `TryGetPlayerVitals`.
- `TeleportStateHooks.IsPortaling`: host answers from `IRynthGameView.IsPortaling`.
- `UiSources.Characters`: their real `CharactersSource` (from `CharactersData`)
  added to our `UiSources` stand-in and to the dashboard's hub source list. It
  polls RynthNet's export through the existing plugin export lookup; RynthNet is
  never loaded, so the snapshot stays empty.
- `Hud.MonsterHudData.TryGetHealth(id, out HealthObservation)` and
  `HealthObservation` (`Ratio`): like RynthCore, every health update counts,
  ratio-only ones included. The host answers `Ratio` from the client's latest
  health ratio for the object (`TryGetHealthRatio`, from `CombatState`), with
  `Current`/`Max` from the last appraisal (`TryGetTargetVitals`) when known,
  else 0/0 with the ratio still answered; an object with no ratio is false.
- `AcMainThreadQueue.EnqueueOverlaySelect(id)`: forwards to the existing
  `AcMainThreadQueue.SelectObject` hook.
- `AvaloniaOverlay.RadarCtrlGatedClickThrough`: a `volatile bool` field.

Our panel host (`AcDream.Rynth.ScriptWindows/StandIns`):

- `PanelSpec` gains `bool GoldGrip = false`, accepted and not drawn: our
  windows keep ImGui's own resize grip (the existing `BodyGripReserve => 0`
  note), so the Radar gets that grip, not RynthCore's gold one.
- `ImGuiPanelHost.TextSizeMenu(string title)`: a "Text size" submenu with the
  disabled line "Uses OpenAC's UI scale.", matching our existing
  `TextSizeRows` stand-in (the overlay has one font size per panel host).

## Wiring

- `RynthAiDashboard.Start`: registers `WorldMapFace` (which loads its settings
  and registers `MiniMapFace`) and `RadarFace`, each behind the existing
  `HasFace` check. `RadarFace.Register` also calls `InventoryFace.Register`,
  which the dashboard already registers: `RegisterDynamic` already replaces an
  entry's spec and factory in place, so a second register is harmless; a test
  pins that.
- `RynthAiDashboard.BeginFrame`: after `RetailSprites.BeginFrame`, calls
  `WorldMapTiles.BeginFrame(IntPtr.Zero)` (tiles read since the last frame
  become textures; a pending `Switch` takes effect).
- Teardown: `RynthInventoryHookup.Dispose` already shuts `RetailSprites` down
  while our upload is still installed; `WorldMapTiles.Shutdown()` runs there
  too, before `DX9Backend.Upload/Release` are cleared.
- A new host hookup class (`RynthMapHookup`, beside `RynthInventoryHookup`)
  sets and clears the `ClientObjectHooks` position/ids, `PlayerVitalsHooks`,
  `TeleportStateHooks` and `MonsterHudData` delegates.
- `RynthClientExtension`: the bar's `PanelButtons` gains `RadarFace.Title`
  ("Radar") after RynthNav.
- Sources: `UiSources.Radar` is already pumped by the dashboard part and
  `UiSources.RynthNav` by the RynthNav panel part; `UiSources.Characters` joins
  the dashboard part.

## Threads and data flow

- Tile reads and inflation run on one pool worker (their code); texture
  uploads happen in `BeginFrame` on the overlay frame thread, at most 4 a frame
  (their cap). The overlay upload is already called from that thread for
  Inventory art.
- `WorldMapMaps` scans `<CoreDir>/NavData` and `MapPacks` on a pool thread;
  `RynthCore.Install.RynthInstallPaths.CoreDir` is already answered (navdata
  install).
- `WorldMapSettings` saves to `LocalApplicationData/RynthCore/world_map_settings.txt`
  (beside `dungeon_map_settings.txt`); Radar settings already load at dashboard
  Start (`radar_settings.txt`).
- Host hooks read the game view on the frame thread, like the existing hookups.
- DXT1 pack tiles are decoded to RGBA on the frame thread before upload (up to
  4 x 256 KB a frame), so GPU memory is RGBA-sized: up to 192 tiles is about
  48 MB, against RynthCore's roughly 6 MB of DXT1. Accepted; packs are not
  installed by default.

## Errors

- Missing or corrupt `worldmap.rmap`: their status text ("Loading the map...",
  or the file error) is drawn in the map; the Radar draws without a backdrop.
- Upload failure: `CreateTexture*` returns zero; their code retries the tile
  after ~5 s.
- A hook that throws is caught and logged by the hookup's existing `Guard`
  pattern and answers "no data".

## Testing (AcDream.Rynth.Host.Tests, plus EngineUi tests where they live)

- `DX9Backend.CreateTextureDxt1` uploads 256 x 256 RGBA through `Upload`, and
  a known four-colour block decodes to its end-point colours.
- `WorldMapTiles` against a small synthetic RMAP v1 file (2 levels): opens,
  `TryGet` queues reads, `BeginFrame` uploads through the fake upload,
  `Shutdown` releases every texture.
- Registration: after dashboard Start, `HasFace` is true for World Map, Mini
  Map and Radar; Inventory registered twice keeps one entry.
- `RynthMapHookup`: each hook answers from a fake `IRynthGameView` and is
  cleared on dispose (only when still ours).
- Health: a ratio-only object answers its ratio with 0/0; an object with an
  appraisal answers the latest ratio plus the appraisal's `Current`/`Max`;
  unknown is false; a throwing view is false and logged; disposing an earlier
  hookup leaves a later one's reader.
- `EnqueueOverlaySelect` reaches `SelectObject`.
- `PanelSpec.GoldGrip` compiles with RadarFace's spec; `TextSizeMenu` draws
  without throwing in the headless ImGui test context.
- Bar: `PanelButtons` lists Radar.
- Real map file test (skipped when NavData isn't installed): opens the
  installed `worldmap.rmap`, reads the top tile.

## Live check (user, later)

- Dashboard World Map button opens the map: tiles load and sharpen on zoom,
  buildings/roads appear zoomed in, places and find box work, player arrow
  tracks, RynthNav route shows, right-click Go here / Route here / Arrow here.
- RynthNav panel's Mini Map button: Follow and Rotate.
- Bar Radar button: markers, hover text with health %, click selects, settings
  popup, outdoors the map backdrop, indoors the dungeon geometry.
- Retina: tiles and text at the right size; windows remember position.
