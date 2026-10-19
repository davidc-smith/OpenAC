# RynthAi dashboard: Dungeon Map

Date: 2026-10-19. Branch `feat/rynth-dungeon-map`.

## Goal

The dashboard's Map button opens RynthCore's real Dungeon Map: a pannable,
zoomable map of the dungeon you are in with floors, walls, explored cells,
markers (monsters, players, NPCs, vendors, pets, corpses, lifestones, portals,
doors, ground items), the player, and the RynthAi nav route with route editing
(add / move / delete point, set active). Indoors the readout names the
dungeon ("Matron Hive East  Z 12.0"). Their source compiles unchanged (rule:
[[rynth-no-rewrite]]); everything missing is a stand-in.

Out of scope (user decision 2026-10-19): RynthCore's separate round Radar
panel (`RadarFace`), whose outdoor backdrop builds Direct3D 9 world-map tiles.
Only its `KindRowsOrder` legend table is borrowed, through a stand-in.

## Where the data comes from

Everything the map draws comes from one RynthAi export,
`RynthPluginGetRadarSnapshot(uint mapVersion)`, which returns JSON
(`RynthRadarUi.BuildSnapshotJson`). RynthAi builds it from its own dat reader
(`DungeonMapUi.EnsureCache`: floor polygons, wall segments, per-floor Zs, read
through the dat folder the host already gives it), its own visited-cell grid,
and host slots OpenAC's `RynthApiTable` already answers (player pose and
heading, object position / item type / bitfield / attackable / string
properties). Their `RadarSource` (RadarData.cs) polls the export every 33 ms
through the existing `PluginExportBinder.Resolver`, caches walls and fills by
map version, and publishes a `RadarView`. The nav route comes from
`UiSources.Nav` (already compiled and polled); edits go through the already
compiled `NavCommands.Send`.

No new client data, no new host slots, no contract change. The map needs no
textures: floors are filled rectangles, walls lines, icons Phosphor glyphs.

## RynthCore files compiled unchanged

Added to `AcDream.Rynth.EngineUi.csproj` (a probe build on 2026-10-19 confirmed
this exact frontier compiles once the stand-in changes below are made):

- `ImGui/Panels/DungeonMapFace.cs` (the panel and its `DungeonMapSettingsStore`)
- `UI/Data/RadarData.cs`, `UI/Data/RadarSnapshotJson.cs`, `UI/Data/DungeonMapMath.cs`
- `ImGui/Panels/RadarMarkerStyle.cs`, `ImGui/Panels/CircleClip.cs` (`RoundMask`,
  used in a `RadarMarkerStyle` signature), `UI/Panels/MarkerStyleSettings.cs`,
  `ImGui/Panels/MarkerStyleEditor.cs`, `UI/Panels/RadarSettingsStore.cs`
- `UI/Data/RynthNavAtlasData.cs` (dungeon names) and `UI/Data/RynthNavData.cs`
  (its `RynthNavStatus` type, `RynthNavSource` and the real `RynthNavCommands`;
  with no RynthNav plugin loaded the source resolves nothing and publishes
  nothing, and the commands do nothing — the same behaviour as the stand-in
  they replace)

The project test `TheProjectCompilesTheirFilesAndHasNoCopies` lists them.

## Stand-in changes

- `DashboardStandIns.cs`: the empty `DungeonMapFace` stand-in is removed.
- New `RadarFace` stand-in (namespace `RynthCore.Engine.ImGuiBackend.Panels`)
  holding only `KindRowsOrder`, its text copied from theirs.
- `ChatEngineStandIns.cs`: the `RynthNavCommands` stand-in is removed (the real
  one compiles); `UiSources` gains `Radar` (`RadarSource`) and `RynthNav`
  (`RynthNavSource`).
- `PanelSpec` (ScriptWindows `ImGuiStandIns.cs`) gains `bool OpenCentered = false`.
- `ImGuiPanelHost` (ScriptWindows):
  - `OpenCentered`: with no saved spot, no requested position and no
    `DefaultPos`, the window's first placement is centred on the display
    (RynthCore's `CenteredLogicalPos`: `max(0, (display/scale - size)/2)`).
  - `HiddenWhileDocked`: an open panel whose instance answers true is not
    drawn that frame (no `Begin`); it stays open and comes back when the
    answer turns false. A panel stopped after repeated failures is never
    hidden (its error line shows), and a throwing getter counts as false —
    as RynthCore's host. The map answers true outdoors.
- `RynthAiDashboard`: `UiSources.Radar` joins `Sources`; `Start()` registers
  `DungeonMapFace` (their `RynthAiFace` already calls `DungeonMapFace.Register()`
  and its Map button toggles `DungeonMapFace.Title`).

## Dungeon names

`locations.json` (1.9 MB, `external/RynthCore/tools/navdata/NavData/`) is
embedded in `AcDream.Rynth.EngineUi` as a resource. `RynthAiDashboard.Start()`
installs it once to `<RynthInstallPaths.CoreDir>/NavData/locations.json`
(`~/Library/Application Support/RynthCore/NavData` on macOS) — the path their
`RynthNavAtlas.EnsureLoadedForNames` falls back to when no RynthNav plugin
reports one:

- written when the file is missing;
- off Windows also rewritten when its size differs from the embedded copy
  (that folder is ours there); on Windows an existing file is left alone, so a
  real RynthCore install keeps its own;
- written to a temp file then moved, so a crash never leaves half a file;
- an IO error is logged and the map falls back to "LB 6146" labels.

The install runs synchronously (a size check, and a 1.9 MB write on first run
only). Our `UiBackgroundWriter` stand-in runs work inline, so the atlas load
(parse of 1.9 MB) would freeze the frame on the first indoor poll; `Start()`
therefore warms it once on a thread-pool thread
(`Task.Run(RynthNavAtlas.EnsureLoadedForNames)`) after the install. Their
loader swaps its tables in whole and is written for a background thread; later
calls from the frame thread see the path already requested and return.

## Settings

`dungeon_map_settings.txt` and `radar_settings.txt` stay at their own
`LocalApplicationData` paths, unchanged (as `item_huds.txt`).

## Testing (AcDream.Rynth.Host.Tests)

- Project lists their files; no copies in `src`.
- `RadarFace.KindRowsOrder` matches theirs (parse their file's table text).
- `OpenCentered` places the first open centred; a saved spot still wins.
- `HiddenWhileDocked`: a hidden open panel draws nothing and stays open; it
  draws again when the answer flips; a stopped panel and a throwing getter
  are not hidden.
- The atlas install: writes when missing; leaves a matching file; rewrites a
  different-size file off Windows only; an unwritable folder logs and does not
  throw. `RynthNavAtlas.DungeonLabel` gives a real dungeon name from the
  installed file.
- With the real RynthAi plugin loaded (`StartWithRynthAi`), subscribing
  `UiSources.Radar` and ticking the dashboard publishes a `RadarView` whose
  player matches the fake view's pose.
- The map registers with the dashboard, the Map button opens it, and with an
  indoor snapshot (floors, walls, markers, published through a test export)
  it draws headless with no log lines; with an outdoor snapshot it is hidden.

## Live check (user, later)

Enter a dungeon: the map opens centred; walls and floors line up; the readout
shows the dungeon name; monsters, doors and corpses appear and move; floors
switch going up and down stairs; the nav route draws and adding / moving a
point works; the map hides outdoors and returns indoors; settings survive a
relog; Retina looks right.
