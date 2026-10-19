# RynthAi outdoor detours: RynthNav path service and navmesh data — design

Date: 2026-10-19. Status: sections 1-3 approved in chat; spec for review.

## Goal

When RynthAi's nav gets stuck or off-track outdoors, it plans a detour over RynthNav's
baked DotRecast navmesh and walks round the obstacle, as it does under RynthCore. Today
our host leaves `GetPluginInterfaceFn` at zero, so `NavRecovery.PlanOutdoor` returns
"RynthNav needs a newer RynthCore" and RynthAi only has its jump / side-step ladder.

This is sub-project A, the outdoor-detour part deferred from RynthAi #6 (the dat/screen
spec, `2026-10-19-rynth-ai-dat-screen-design.md`). Follow-ups:

- **B** (next session): RynthNav as a supported feature: `/rn` commands, its panel,
  goto / Atlas travel / recalls / Town Network, any host slots those need, live checks.
- **A2**: the launcher installs the navdata from our release manifest (the pin below),
  once we know how fork releases ship.
- Later: ask the RynthSuite author for permission to mirror the archive.

## Decisions

- **Their code, unchanged.** RynthNav and the parts of RynthCore's updater we use compile
  from the submodules as they are; stand-ins only where they reach Windows or Avalonia.
- **Scope A** (user, 2026-10-19): detours only. RynthNav is loaded as a whole plugin (the
  path table comes from its exports and needs its tick), but only the path service is
  built for, tested and live-checked. Its other features get whatever slots already exist.
- **The navdata follows our release, not theirs** (user, 2026-10-19). Their launcher keeps
  players on their latest tiles; our players run the RynthNav we compiled from a pinned
  submodule, so we pin the tile archive too and bump it on purpose with the submodule.
- **Their URL for now** (user, 2026-10-19); mirroring needs the author's permission.
- **Our own SHA-256 pin.** Their current signed feed (release 2026101901) has no `navdata`
  entry (published with `-SkipNavData`), so there is no signed hash to pin. The only
  archive on their server is `releases/2026101501/RynthNav-NavData.zip` (1,572,337,084
  bytes, 15 Oct 2026). We download it once, check it with their install rules, and pin
  our own size, SHA-256 and file count.
- **The client never downloads anything.** Installing the tiles is a tool run for A; the
  launcher in A2.
- **No contract bump; `ApiVersion` stays 86.** RynthAi gates on `Version >= 68` plus a
  non-zero slot.
- The 1.57 GB download during implementation is approved (user, 2026-10-19).

## How it works today (their side)

- ABI: `Plugins/Shared/RynthNavPathApi.cs` — `RynthNavPathApiV1 { uint Version; uint
  StructSize; delegate* unmanaged[Cdecl]<double ×6, double* points, int maxPoints,
  int* count, int> FindPath; }`, results Ok 1 / NoPath 0 / NoMesh -1. Fetched again on
  every call, pump thread only.
- Caller: RynthAi `NavRecovery.PlanOutdoor` (`NavRecovery.cs:201-250`):
  `GetPluginInterface("RynthNav", "RynthNav.Path", 1)`, 128 corners. Never used in real
  dungeon landblocks (those keep the dungeon map planner).
- Provider: RynthNav `PluginExports.RynthPluginQueryInterface` → `PathInterface.Query` →
  `RynthNavPlugin.InterfaceFindPath` (`RynthNavPlugin.cs:1073-1126`). It answers NoMesh
  until its own `OnTick` has seen a player pose; loads tiles over start/goal + 1 margin
  (max 25); 512 polys, 256 straight-path corners; an end more than 6 units from the goal
  is NoPath.
- RynthCore's `GetPluginInterfaceAction` (`PluginManager.cs:4222-4237`) resolves the
  plugin by exact display name, then a case-insensitive "contains" match, among
  initialised, non-failed plugins, and calls its `RynthPluginQueryInterface`.
- Data: `nav_XXXX.tile` per landblock (raw DotRecast `DtMeshData`, absolute tile coords,
  ~40,000 files) plus optional `navgraph.bin`, `portals.tsv`, `locations.json`,
  `townnet.json`, `worldmap.*`. RynthNav finds the folder via `RYNTHNAV_NAVDATA`, then
  `navDataDir` in `<AppData>/RynthCore/rynthnav.json`, then `C:\Games\RynthCore\NavData`.
  It does not use `RynthInstallPaths`, so our `RYNTHCORE_DIR` does not reach it.
- Their baker cannot build here (needs the absent `AcClientReborn` project); not needed.

## Section 1: `GetPluginInterface` and the RynthNav wrapper

### The slot

`RynthApiTable` sets `GetPluginInterfaceFn` to an `[UnmanagedCallersOnly(Cdecl)]`
`(IntPtr pluginName, IntPtr interfaceName, uint version) → IntPtr`, following RynthCore:

1. Read both ANSI strings; null or empty → 0.
2. Among the host's loaded plugins that initialised and have not failed, pick the first
   whose `RynthPluginName` export equals the name (ordinal, case-insensitive); else the
   first whose name contains it (case-insensitive). Our host's namespace-based names
   ("RynthCore.Plugin.RynthNav") therefore also match by "contains".
3. Resolve that plugin's `RynthPluginQueryInterface` export; missing → 0.
4. Call it with the interface name pointer and version; return its pointer. Any
   exception → 0 (logged once per name/interface pair).

The existing resolver in `RynthChatHookup.cs:157-167` is the model; the lookup becomes
one host helper both use. This also fixes RynthLua's
`GetPluginInterface("RynthAi", "RynthAi.Script", 1)` (`RynthAiBridge.cs:38`).
`RynthApiTableTests.DeferredSlotsStayUnset` drops this slot.

### The wrapper

`src/AcDream.Rynth.Plugin.RynthNav`, modelled on `AcDream.Rynth.Plugin.RynthLua`:

- Compiles `$(RynthSuite)Plugins/RynthCore.Plugin.RynthNav/**/*.cs` plus the files their
  csproj links: `Tools/RynthNav.PortalGraph/PortalRoute.cs`, `Shared/RynthNavPathApi.cs`,
  `UseAudit.cs`, `Nav3DText.cs`, `RynthInstallPaths.cs`, RynthAi's
  `LegacyUi/NavRouteParser.cs` (exact list taken from their csproj in the plan).
- References `AcDream.Rynth.Sdk`; DotRecast.Core and DotRecast.Detour 2026.1.3 added to
  `Directory.Packages.props`; lock files refreshed with
  `dotnet restore AcDream.slnx --force-evaluate` (not `tools/update-package-locks.ps1`).
- `RynthInstallPaths`' advapi32 P/Invoke goes through the existing RynthInteropRedirect
  inert pass, as in the RynthAi wrapper.
- `RynthClientExtension` loads it before RynthAi. Chat commands (`/rn`) are not routed
  (B).

### The navdata folder

- Before RynthNav's Init the host sets `RYNTHNAV_NAVDATA` to
  `<CoreDir>/NavData` (`~/Library/Application Support/RynthCore/NavData` on macOS, the
  folder `NavAtlasInstall` already writes `locations.json` to), unless the variable is
  already set.
- A `navDataDir` in `rynthnav.json` does not override it (their order puts the variable
  first); that is accepted.

### The hint line

When RynthNav starts and the folder has no `nav_*.tile`, the host logs and writes one chat
line once per session: outdoor detours need RynthNav's navmesh tiles, install them with
`tools/RynthNavData install` (exact wording in the plan). RynthAi's own "no RynthNav
navmesh here" line is unchanged.

## Section 2: the pin file and the maintainer tool

### `external/rynth-navdata.json`

```json
{ "url": "https://aelrynth.com/downloads/rynth/releases/2026101501/RynthNav-NavData.zip",
  "size": 1572337084, "sha256": "<archive sha256>", "files": <count>, "bytes": <unpacked>,
  "id": "<archive sha256>", "rynthSuite": "e6fffb0f", "pinned": "2026-10-19" }
```

`id` is written to the installed `navdata.id` (64 hex, inside their `IdPattern`).
`rynthSuite` is the RynthSuite commit it was tested with; a submodule bump re-checks it.

### `tools/RynthNavData`

A net10.0 console app compiling their `RynthUpdater` NavData code and `UpdateTrust`
unchanged (`external/RynthCore/src/RynthCore.App.Avalonia/RynthUpdater*.cs`,
`UpdateTrust.cs`), with stand-ins for the Avalonia / engine types they reach (for example
`PluginContractVersion.Current`, `RynthInstallPaths`); the plan settles which partial
files compile and which stand-ins are needed.

- **`pin --url <zip> [--out external/rynth-navdata.json]`** (maintainer): download into the
  cache (`~/Library/Caches/RynthCore/updates` on macOS); run their `ExtractNavData` into a
  temporary folder (allowed names, 32 MB entry limit, no duplicates); record size,
  SHA-256, file count and unpacked bytes; write the pin; delete the temporary folder.
- **`install [--dir <NavData>]`**: build their `NavDataEntry` from the pin (no feed) and
  run their install: disk space check; download with size + SHA-256 (a verified cached
  zip is reused); unpack to `NavData.new` with the file count check; copy unshipped files
  across (`atlas.txt`, `recalls.txt`, anything else); write `navdata.id`; swap, keeping
  `NavData.previous`. A folder with `navdata.local` is skipped; a matching `navdata.id`
  does nothing. Default `--dir` is the same `<CoreDir>/NavData` the host uses.
- **`check-feed`** (optional): fetch their signed feed, verify with their
  `VerifyEnvelope` and `UpdateTrust.PublicKeys`, print the `navdata` entry if present
  (to notice a new bake). Not used by install.

For A, players install by running `install`; A2 moves the same install path into the
launcher with the pin carried in our release manifest.

## Section 3: testing

- **Host unit tests** (`tests/AcDream.Rynth.Host.Tests`): `GetPluginInterface` finds by
  exact `RynthPluginName`, then by contains; skips failed / uninitialised plugins; 0 for
  no export, a 0 answer, null names, or a throwing export; name and version passed
  through; the slot is non-zero.
- **Wrapper tests**: RynthNav loads in the host; Init, Name ("RynthNav"), Tick,
  login / logout do not throw; `RYNTHNAV_NAVDATA` is set before Init and an existing
  value is kept; the hint line appears with no tiles and not with tiles; RynthLua gets a
  non-zero `RynthAi.Script` table with real RynthAi loaded.
- **Real-tile test** (skipped when the NavData folder has no tiles, like the dat-backed
  tests): our RynthNav ticks with a stub pose at Holtburg (landblock A9B4); the
  `RynthNavPathApiV1` from `GetPluginInterface` returns Ok with at least two corners
  between two outdoor points with a building between; NoMesh before any pose; NoPath for
  a goal off the mesh. This proves the 15 Oct tiles work with the 9 Oct RynthNav. If they
  do not link, stop and decide before going on.
- **Tool tests** (`tests/AcDream.Tools.RynthNavData.Tests`, small zips built in the
  test, no network: local server or injected handler, settled in the plan): `pin` writes
  size / SHA-256 / count and rejects a bad entry name; `install` refuses a SHA-256
  mismatch, keeps `atlas.txt`, skips `navdata.local`, does nothing on a matching id,
  keeps `NavData.previous`.
- **Maintainer run in this session**: `pin` against the real URL, `install` locally, then
  the real-tile test.
- **Live check** (user, later): an outdoor nav route that snags on a building or fence —
  log shows the navmesh planner and a detour, the character walks round; a goal with no
  route falls back to the ladder; RynthLua's RynthAi bridge works.

## Out of scope

`/rn` commands, RynthNav's panel and its goto / travel features (B); launcher install
(A2); mirroring the archive; baking tiles; the World Map; the round Radar panel.
