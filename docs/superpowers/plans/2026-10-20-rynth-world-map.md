# RynthAi World Map, Mini Map and Radar Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** RynthCore's World Map, Mini Map and round Radar panels work in OpenAC, compiled unchanged from the RynthCore submodule.

**Architecture:** RynthCore's map files go into `AcDream.Rynth.EngineUi` as `<Compile Include>` links. Their Direct3D 9 tile textures go through our existing `DX9Backend` stand-in (overlay upload). The engine hooks they read are small stand-ins whose delegates a new host class, `RynthMapHookup`, fills from `IRynthGameView`. The dashboard registers the faces and runs the tile frame step; the bar gets a Radar button.

**Tech Stack:** C# / .NET 10, ImGui.NET, xUnit (`tests/AcDream.Rynth.Host.Tests`), RynthCore submodule `external/RynthCore`.

**Spec:** `docs/superpowers/specs/2026-10-20-rynth-world-map-design.md`

## Global Constraints

- RynthCore source is compiled UNCHANGED (rule: no rewrites, only stand-ins). Never edit anything under `external/`.
- Stand-in pattern: a settable `internal static` delegate the host fills and clears; with no delegate the stand-in answers "no data" (false / empty / zero). The host's hookup clears a delegate only if it is still its own (`if (X == _mine) X = null;`).
- Every host read of the view goes through a `Guard(what, read, fallback)` that logs `[Rynth] map {what}: view failed: {Type}: {Message}` and returns the fallback (same shape as `RynthChatHookup.Guard`).
- Build and tests use the home SDK: prefix commands with `PATH=$HOME/.dotnet:$PATH`.
- Test command for this work: `PATH=$HOME/.dotnet:$PATH dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthWorldMap|FullyQualifiedName~RynthRadar"`; the whole project must stay green (`PATH=$HOME/.dotnet:$PATH dotnet test tests/AcDream.Rynth.Host.Tests`, 411 passing before this work).
- Mac restores can rewrite `src/AcDream.Launcher/packages.neutral.lock.json`'s RID header: revert that file before each commit if it shows as modified.
- Commit messages end with the line `Claude-Session: https://claude.ai/code/session_011FtdjFB5dEVWdBQnZmstpz`.
- If a build step reports a missing member not listed in this plan, add a stand-in of the same kind (a no-data answer or a host delegate) next to the others of its class, and list it in the commit message. Stop and ask if one needs behaviour beyond "no data" or a straight view read.

**User decisions (already made):**
- All three panels in one sub-project; Radar is the last code task.
- DXT1 map packs are supported by decoding to RGBA in the `DX9Backend` stand-in (with RynthCore's `DxtUtil`).
- The Characters layer stays empty (no RynthNet), as RynthCore without RynthNet.
- `PanelSpec.GoldGrip` is accepted, not drawn (ImGui's own grip); `TextSizeMenu` shows "Uses OpenAC's UI scale." like our `TextSizeRows`.
- Live checks are done by the user later; never launch a client.

---

## File map

- Modify `src/AcDream.Rynth.EngineUi/StandIns/InventoryStandIns.cs`: `DX9Backend.CreateTextureDxt1`.
- Modify `src/AcDream.Rynth.EngineUi/StandIns/ChatEngineStandIns.cs`: `ClientObjectHooks.LiveObjectIds` / `TryGetSnapshotPosition`; `UiSources.Characters`; `AvaloniaOverlay.RadarCtrlGatedClickThrough`.
- Create `src/AcDream.Rynth.EngineUi/StandIns/MapStandIns.cs`: `PlayerVitalsSnapshot`, `PlayerVitalsHooks`, `TeleportStateHooks`; (Task 4) `Hud.HealthObservation`, `Hud.MonsterHudData`.
- Modify `src/AcDream.Rynth.EngineUi/StandIns/DashboardStandIns.cs`: delete the `WorldMapFace` / `MiniMapFace` placeholders; (Task 4) `AcMainThreadQueue.EnqueueOverlaySelect`.
- Delete (Task 4) `src/AcDream.Rynth.EngineUi/StandIns/RadarFaceStandIn.cs`.
- Modify `src/AcDream.Rynth.EngineUi/AcDream.Rynth.EngineUi.csproj`: the compile links and the header comment.
- Modify `src/AcDream.Rynth.EngineUi/RynthAiDashboard.cs`: registration, `WorldMapTiles.BeginFrame`, `UiSources.Characters` in `Sources`.
- Modify (Task 4) `src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiStandIns.cs` (`PanelSpec.GoldGrip`) and `src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiPanelHost.cs` (`TextSizeMenu`).
- Create `src/AcDream.Rynth.Host/RynthMapHookup.cs`; modify `src/AcDream.Rynth.Host/RynthClientExtension.cs` (create/dispose the hookup, bar buttons) and `src/AcDream.Rynth.Host/RynthInventoryHookup.cs` (`WorldMapTiles.Shutdown` in `Dispose`).
- Tests: create `tests/AcDream.Rynth.Host.Tests/RynthWorldMapTests.cs` and `RynthRadarTests.cs`; modify `RynthDungeonMapTests.cs` (the legend-table stand-in test goes away with the stand-in).

---

### Task 1: DXT1 tiles through the DX9Backend stand-in

**Goal:** `DX9Backend.CreateTextureDxt1` decodes DXT1 blocks with RynthCore's `DxtUtil` and uploads RGBA through the existing `Upload` delegate.

**Files:**
- Modify: `src/AcDream.Rynth.EngineUi/StandIns/InventoryStandIns.cs:215-225`
- Test: `tests/AcDream.Rynth.Host.Tests/RynthWorldMapTests.cs` (create)

**Acceptance Criteria:**
- [ ] `CreateTextureDxt1(IntPtr.Zero, 256, 256, blocks)` calls `Upload` once with a 262144-byte RGBA array, width 256, height 256, and returns the upload's handle.
- [ ] A four-colour block (c0 = 0xF800 red, c1 = 0x001F blue, all indices 0) decodes to red (255, 0, 0, 255) at pixel 0.
- [ ] With no `Upload` set it returns `IntPtr.Zero`.

**Verify:** `PATH=$HOME/.dotnet:$PATH dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthWorldMapTests"` → the 2 new tests pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — create `tests/AcDream.Rynth.Host.Tests/RynthWorldMapTests.cs`:

```csharp
using RynthCore.Engine.ImGuiBackend;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthWorldMapTests : IDisposable
{
    private readonly Func<byte[], int, int, IntPtr>? _upload = DX9Backend.Upload;
    private readonly Func<IntPtr, bool>? _release = DX9Backend.Release;

    public void Dispose()
    {
        DX9Backend.Upload = _upload;
        DX9Backend.Release = _release;
    }

    // One DXT1 tile (256 x 256 = 64 x 64 blocks): every block c0 red, c1 blue, all indices 0 (c0).
    private static byte[] RedDxt1Tile()
    {
        var blocks = new byte[64 * 64 * 8];
        for (int b = 0; b < blocks.Length; b += 8)
        {
            blocks[b] = 0x00; blocks[b + 1] = 0xF8;      // c0 = 0xF800 (red)
            blocks[b + 2] = 0x1F; blocks[b + 3] = 0x00;  // c1 = 0x001F (blue); c0 > c1: four-colour form
        }
        return blocks;
    }

    [Fact]
    public void ADxt1TileUploadsAsRgba()
    {
        var uploads = new List<(byte[] Rgba, int W, int H)>();
        DX9Backend.Upload = (rgba, w, h) => { uploads.Add((rgba, w, h)); return (IntPtr)7; };

        IntPtr tex = DX9Backend.CreateTextureDxt1(IntPtr.Zero, 256, 256, RedDxt1Tile());

        Assert.Equal((IntPtr)7, tex);
        var (data, width, height) = Assert.Single(uploads);
        Assert.Equal((256, 256, 256 * 256 * 4), (width, height, data.Length));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, data[..4]);
    }

    [Fact]
    public void WithNoUploadADxt1TileIsNoTexture()
    {
        DX9Backend.Upload = null;
        Assert.Equal(IntPtr.Zero, DX9Backend.CreateTextureDxt1(IntPtr.Zero, 256, 256, RedDxt1Tile()));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `PATH=$HOME/.dotnet:$PATH dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthWorldMapTests"`
Expected: build error CS0117 `'DX9Backend' does not contain a definition for 'CreateTextureDxt1'`.

- [ ] **Step 3: Implement** — in `InventoryStandIns.cs`, inside `DX9Backend` after `CreateTextureRgba`, and update the class summary:

```csharp
    /// <summary>RynthCore makes D3D9 textures; here the overlay's own upload (device unused). DXT1 (World Map packs) is decoded to RGBA first.</summary>
    internal static class DX9Backend
    {
        internal static Func<byte[], int, int, IntPtr>? Upload { get; set; }
        internal static Func<IntPtr, bool>? Release { get; set; }

        public static IntPtr CreateTextureRgba(IntPtr pDevice, int width, int height, ReadOnlySpan<byte> rgba) =>
            Upload?.Invoke(rgba.ToArray(), width, height) ?? IntPtr.Zero;

        public static IntPtr CreateTextureDxt1(IntPtr pDevice, int width, int height, byte[] blocks) =>
            Upload is { } upload ? upload(RynthCore.Imaging.DxtUtil.DecompressDxt1(blocks, width, height), width, height) : IntPtr.Zero;

        public static void ReleaseTexture(IntPtr tex) => Release?.Invoke(tex);
    }
```

(Their caller passes a `byte[]`: `DX9Backend.CreateTextureDxt1(device, TileSize, TileSize, r.Rgba)`.)

- [ ] **Step 4: Run to verify they pass** — same command; expected 2 passed.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Rynth.EngineUi/StandIns/InventoryStandIns.cs tests/AcDream.Rynth.Host.Tests/RynthWorldMapTests.cs
git commit -m "feat(rynth): DXT1 textures through the DX9Backend stand-in (World Map packs)

Claude-Session: https://claude.ai/code/session_011FtdjFB5dEVWdBQnZmstpz"
```

---

### Task 2: Compile the World Map and Mini Map with their stand-ins

**Goal:** RynthCore's World Map, Mini Map, tile/vector readers and characters data compile unchanged into EngineUi, and their tile reader works against a synthetic map file through the overlay upload.

**Files:**
- Modify: `src/AcDream.Rynth.EngineUi/AcDream.Rynth.EngineUi.csproj` (links after the `RynthNavData.cs` line; header comment)
- Modify: `src/AcDream.Rynth.EngineUi/StandIns/ChatEngineStandIns.cs` (`ClientObjectHooks`, `UiSources`)
- Create: `src/AcDream.Rynth.EngineUi/StandIns/MapStandIns.cs`
- Modify: `src/AcDream.Rynth.EngineUi/StandIns/DashboardStandIns.cs:298-317` (delete `WorldMapFace`, `MiniMapFace`)
- Test: `tests/AcDream.Rynth.Host.Tests/RynthWorldMapTests.cs`

**Acceptance Criteria:**
- [ ] EngineUi builds with the 11 RynthCore files linked; the `WorldMapFace` / `MiniMapFace` placeholders are gone.
- [ ] `WorldMapTiles.Open` on a synthetic 2-level RMAP v1 file reaches `IsOpen`; `TryGet` + `BeginFrame` upload the top tile as 256 x 256 RGBA whose first pixel is the file's colour; `Shutdown` releases every uploaded texture.
- [ ] A missing file sets `Status` to RynthCore's "No world map file yet (NavData\worldmap.rmap)." text.
- [ ] With no host delegates: `ClientObjectHooks.LiveObjectIds` is empty, `TryGetSnapshotPosition` false, `PlayerVitalsHooks.TryGetSnapshot` false, `TeleportStateHooks.IsPortaling` false.
- [ ] When the installed NavData has `worldmap.rmap`, a test opens it and gets the top tile; skipped (returns early) otherwise.

**Verify:** `PATH=$HOME/.dotnet:$PATH dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthWorldMapTests"` → all pass.

**Steps:**

- [ ] **Step 1: Add the compile links** — in the csproj after the `RynthNavData.cs` line:

```xml
    <Compile Include="$(RynthEngine)ImGui\WorldMapTiles.cs" Link="RynthCore\WorldMapTiles.cs" />
    <Compile Include="$(RynthEngine)ImGui\WorldMapVectors.cs" Link="RynthCore\WorldMapVectors.cs" />
    <Compile Include="$(RynthEngine)ImGui\Panels\WorldMapFace.cs" Link="RynthCore\WorldMapFace.cs" />
    <Compile Include="$(RynthEngine)ImGui\Panels\MiniMapFace.cs" Link="RynthCore\MiniMapFace.cs" />
    <Compile Include="$(RynthEngine)ImGui\Panels\WorldMapView.cs" Link="RynthCore\WorldMapView.cs" />
    <Compile Include="$(RynthEngine)ImGui\Panels\WorldMapTileDrawer.cs" Link="RynthCore\WorldMapTileDrawer.cs" />
    <Compile Include="$(RynthEngine)ImGui\Panels\WorldMapSettings.cs" Link="RynthCore\WorldMapSettings.cs" />
    <Compile Include="$(RynthEngine)ImGui\Panels\WorldMapMaps.cs" Link="RynthCore\WorldMapMaps.cs" />
    <Compile Include="$(RynthEngine)ImGui\Panels\MapProjection.cs" Link="RynthCore\MapProjection.cs" />
    <Compile Include="$(RynthEngine)UI\Data\CharactersData.cs" Link="RynthCore\Data\CharactersData.cs" />
    <Compile Include="$(RynthEngine)UI\Data\CharactersModel.cs" Link="RynthCore\Data\CharactersModel.cs" />
```

In the header comment, change "Skills and Dungeon Map panels" to "Skills, Dungeon Map, World Map and Mini Map panels".

Delete from `DashboardStandIns.cs` the two placeholder classes:

```csharp
    internal static class WorldMapFace
    {
        public const string Title = "World Map";
    }

    /// <summary>RynthCore's minimap (RynthNav maps); never registered in OpenAC, so the RynthNav panel hides its map buttons.</summary>
    internal static class MiniMapFace
    {
        public const string Title = "Mini Map";
    }
```

- [ ] **Step 2: Build to see the gaps**

Run: `PATH=$HOME/.dotnet:$PATH dotnet build src/AcDream.Rynth.EngineUi -c Debug 2>&1 | grep -E " error " | sort -u`
Expected (from the 2026-10-20 probe): `UiSources.Characters`, `ClientObjectHooks.TryGetSnapshotPosition` / `LiveObjectIds`, `PlayerVitalsSnapshot`, `PlayerVitalsHooks`, `TeleportStateHooks`. (`DX9Backend.CreateTextureDxt1` is done in Task 1.)

- [ ] **Step 3: Add the stand-ins**

In `ChatEngineStandIns.cs`, inside `ClientObjectHooks` (after `TryGetObjectName`):

```csharp
        internal delegate bool ObjectPositionReader(uint objectId, out uint cellId, out float x, out float y, out float z);

        /// <summary>The ids of the objects the client knows now; the host sets it.</summary>
        internal static Func<uint[]>? LiveIds { get; set; }

        /// <summary>An object's cell and landblock-local origin; the host sets it.</summary>
        internal static ObjectPositionReader? PositionSnapshot { get; set; }

        public static uint[] LiveObjectIds => LiveIds?.Invoke() ?? Array.Empty<uint>();

        public static bool TryGetSnapshotPosition(uint objectId, out uint objCellId, out float x, out float y, out float z)
        {
            objCellId = 0; x = y = z = 0;
            return PositionSnapshot is { } read && read(objectId, out objCellId, out x, out y, out z);
        }
```

In `UiSources` (same file), after `RynthNavArrowWatch`:

```csharp
        public static readonly CharactersSource Characters = new();
```

Create `src/AcDream.Rynth.EngineUi/StandIns/MapStandIns.cs`:

```csharp
using System;

namespace RynthCore.Engine.Compatibility
{
    /// <summary>RynthCore's player vitals snapshot (their PlayerVitalsHooks.cs, the record only).</summary>
    internal readonly record struct PlayerVitalsSnapshot(
        uint Health,
        uint MaxHealth,
        uint Stamina,
        uint MaxStamina,
        uint Mana,
        uint MaxMana);

    /// <summary>RynthCore reads the vitals from acclient's qualities; here the host's game view.</summary>
    internal static class PlayerVitalsHooks
    {
        internal delegate bool SnapshotReader(out PlayerVitalsSnapshot snapshot);

        internal static SnapshotReader? Reader { get; set; }

        public static bool TryGetSnapshot(out PlayerVitalsSnapshot snapshot)
        {
            snapshot = default;
            return Reader is { } read && read(out snapshot);
        }
    }

    /// <summary>RynthCore watches acclient's teleport state; here the host's game view.</summary>
    internal static class TeleportStateHooks
    {
        internal static Func<bool>? Portaling { get; set; }

        public static bool IsPortaling => Portaling?.Invoke() ?? false;
    }
}
```

- [ ] **Step 4: Build clean** — `PATH=$HOME/.dotnet:$PATH dotnet build src/AcDream.Rynth.EngineUi -c Debug` → 0 errors. Any further gap: Global Constraints' last rule.

- [ ] **Step 5: Write the tile and stand-in tests** — add to `RynthWorldMapTests.cs` (add `using System.IO.Compression;` and `using RynthCore.Engine.Compatibility;` at the top; add `WorldMapTiles.Shutdown();` as the first line of `Dispose`, and a `private readonly List<string> _temp = new();` whose files `Dispose` deletes):

```csharp
    // RMAP v1 (WorldMapTiles' header comment): level 0 = 2 x 2 tiles, level 1 = the top tile; RGB tiles, zlib.
    private string WriteMap(byte r, byte g, byte b)
    {
        const int tile = 256;
        var levels = new (int W, int H, int Cols, int Rows, int First)[] { (512, 512, 2, 2, 0), (256, 256, 1, 1, 4) };
        var tiles = new List<byte[]>();
        for (int i = 0; i < 5; i++)
        {
            var rgb = new byte[tile * tile * 3];
            for (int p = 0; p < rgb.Length; p += 3) { rgb[p] = r; rgb[p + 1] = g; rgb[p + 2] = b; }
            using var ms = new MemoryStream();
            using (var z = new ZLibStream(ms, CompressionLevel.Fastest, leaveOpen: true)) z.Write(rgb);
            tiles.Add(ms.ToArray());
        }
        string path = Path.Combine(Path.GetTempPath(), $"rynth-wmap-{Guid.NewGuid():N}.rmap");
        _temp.Add(path);
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write(0x5041_4D52u); w.Write(1u); w.Write((uint)tile); w.Write((uint)levels.Length);
        w.Write(2u); w.Write(256u); w.Write(256u);                      // px per cell, cells wide, cells high
        foreach (var l in levels) { w.Write((uint)l.W); w.Write((uint)l.H); w.Write((uint)l.Cols); w.Write((uint)l.Rows); w.Write((uint)l.First); }
        w.Write((uint)tiles.Count);
        long offset = 7 * 4 + levels.Length * 5 * 4 + 4 + tiles.Count * 12;
        foreach (byte[] t in tiles) { w.Write((ulong)offset); w.Write((uint)t.Length); offset += t.Length; }
        foreach (byte[] t in tiles) w.Write(t);
        return path;
    }

    private static void WaitFor(Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!done() && DateTime.UtcNow < until) Thread.Sleep(10);
        Assert.True(done());
    }

    [Fact]
    public void TheTopTileOfAMapFileBecomesAnOverlayTexture()
    {
        var uploads = new List<byte[]>();
        var released = new List<IntPtr>();
        DX9Backend.Upload = (rgba, w, h) => { Assert.Equal((256, 256), (w, h)); uploads.Add(rgba); return (IntPtr)(100 + uploads.Count); };
        DX9Backend.Release = tex => { released.Add(tex); return true; };

        WorldMapTiles.Open(WriteMap(10, 20, 30));
        WaitFor(() => WorldMapTiles.IsOpen);
        Assert.Equal(2, WorldMapTiles.Levels.Count);

        IntPtr tex = IntPtr.Zero;
        WaitFor(() => { WorldMapTiles.BeginFrame(IntPtr.Zero); return WorldMapTiles.TryGet(1, 0, 0, out tex); });
        Assert.Equal((IntPtr)101, tex);
        Assert.Equal(new byte[] { 10, 20, 30, 255 }, uploads[0][..4]);

        WorldMapTiles.Shutdown();
        Assert.Equal([(IntPtr)101], released);
        Assert.False(WorldMapTiles.IsOpen);
    }

    [Fact]
    public void AMissingMapFileSaysSo()
    {
        WorldMapTiles.Open(Path.Combine(Path.GetTempPath(), $"rynth-wmap-missing-{Guid.NewGuid():N}.rmap"));
        WaitFor(() => WorldMapTiles.Status.StartsWith("No world map file yet", StringComparison.Ordinal));
    }

    [Fact]
    public void WithNoHostTheMapHooksAnswerNothing()
    {
        Assert.Empty(ClientObjectHooks.LiveObjectIds);
        Assert.False(ClientObjectHooks.TryGetSnapshotPosition(1, out _, out _, out _, out _));
        Assert.False(PlayerVitalsHooks.TryGetSnapshot(out _));
        Assert.False(TeleportStateHooks.IsPortaling);
    }

    [Fact]
    public void TheInstalledWorldMapOpensWhenThereIsOne()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RynthCore", "NavData", "worldmap.rmap");
        if (!File.Exists(path)) return;   // NavData not installed on this machine
        var uploads = 0;
        DX9Backend.Upload = (_, _, _) => (IntPtr)(++uploads);
        DX9Backend.Release = _ => true;
        WorldMapTiles.Open(path);
        WaitFor(() => WorldMapTiles.IsOpen);
        int top = WorldMapTiles.Levels.Count - 1;
        WaitFor(() => { WorldMapTiles.BeginFrame(IntPtr.Zero); return WorldMapTiles.TryGet(top, 0, 0, out _); });
    }
```

(`Environment.SpecialFolder.ApplicationData` is `~/Library/Application Support` on macOS, where the navdata install puts `RynthCore/NavData`. These tests must not run inside a `RynthConfigSandbox`, which moves HOME.) Mark the class `[Collection("WorldMapTiles")]`, since `WorldMapTiles` is static: the tests in it must not run in parallel with Task 3's or Task 4's tests that touch it (put those classes in the same collection).

- [ ] **Step 6: Run** — `PATH=$HOME/.dotnet:$PATH dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthWorldMapTests"` → all pass. Then the whole project: `PATH=$HOME/.dotnet:$PATH dotnet test tests/AcDream.Rynth.Host.Tests` → no new failures.

- [ ] **Step 7: Commit**

```bash
git add src/AcDream.Rynth.EngineUi tests/AcDream.Rynth.Host.Tests/RynthWorldMapTests.cs
git commit -m "feat(rynth): compile RynthCore's World Map and Mini Map with their stand-ins

Claude-Session: https://claude.ai/code/session_011FtdjFB5dEVWdBQnZmstpz"
```

---

### Task 3: Host hookup and dashboard wiring for the maps

**Goal:** The map faces are registered and drawn, their tile layer runs every frame and is shut down with the overlay upload, and `RynthMapHookup` answers their hooks from the game view.

**Files:**
- Create: `src/AcDream.Rynth.Host/RynthMapHookup.cs`
- Modify: `src/AcDream.Rynth.Host/RynthClientExtension.cs:27-29,109-111,272-282` (field, create, dispose)
- Modify: `src/AcDream.Rynth.Host/RynthInventoryHookup.cs:146-153` (`WorldMapTiles.Shutdown`)
- Modify: `src/AcDream.Rynth.EngineUi/RynthAiDashboard.cs:30-34,47-76,78-83`
- Test: `tests/AcDream.Rynth.Host.Tests/RynthWorldMapTests.cs`

**Acceptance Criteria:**
- [ ] After `RynthAiDashboard.Start()`, `ImGuiPanelHost.HasFace` is true for "World Map" and "Mini Map".
- [ ] `RynthAiDashboard.BeginFrame()` runs `WorldMapTiles.BeginFrame` (a requested tile becomes a texture with no other frame call).
- [ ] `UiSources.Characters` is in the dashboard's hub sources.
- [ ] `RynthMapHookup` answers `LiveObjectIds`, `TryGetSnapshotPosition`, `PlayerVitalsHooks.TryGetSnapshot` and `TeleportStateHooks.IsPortaling` from a `FakeGameView`, and on `Dispose` clears only the delegates that are still its own.
- [ ] A view that throws is logged as `[Rynth] map …: view failed: …` and answers no data.
- [ ] `RynthInventoryHookup.Dispose` calls `WorldMapTiles.Shutdown()` while its upload/release are still installed (the texture is released through it).
- [ ] With the World Map opened, a headless frame of `ImGuiPanelHost.DrawAll()` logs nothing.

**Verify:** `PATH=$HOME/.dotnet:$PATH dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthWorldMapTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — add a second class to `RynthWorldMapTests.cs`, in the same `[Collection("WorldMapTiles")]`:

```csharp
[Collection("WorldMapTiles")]
public sealed class RynthWorldMapHostTests : IDisposable
{
    private readonly List<string> _log = new();
    private readonly RynthConfigSandbox _sandbox = new("rynth-wmap-");
    private readonly FakeGameView _view = new() { PlayerId = 0x50000001u };

    public RynthWorldMapHostTests()
    {
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = _log.Add;
        UiDataHub.Clear();
    }

    public void Dispose()
    {
        AcDream.Rynth.EngineUi.RynthAiDashboard.Stop();
        WorldMapTiles.Shutdown();
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = null;
        _sandbox.Dispose();
    }

    [Fact]
    public void TheDashboardRegistersTheWorldMapAndMiniMap()
    {
        AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
        Assert.True(ImGuiPanelHost.HasFace(WorldMapFace.Title));
        Assert.True(ImGuiPanelHost.HasFace(MiniMapFace.Title));
    }

    [Fact]
    public void TheMapHooksAnswerFromTheView()
    {
        _view.Live = [0x80000001u, 0x80000002u];
        _view.Positions[0x80000001u] = (0xA9B40021u, new System.Numerics.Vector3(1, 2, 3));
        _view.Vitals = new RynthVitals(50, 100, 60, 120, 70, 140);
        _view.Portaling = true;
        using (new RynthMapHookup(_view, _log.Add))
        {
            Assert.Equal([0x80000001u, 0x80000002u], ClientObjectHooks.LiveObjectIds);
            Assert.True(ClientObjectHooks.TryGetSnapshotPosition(0x80000001u, out uint cell, out float x, out float y, out float z));
            Assert.Equal((0xA9B40021u, 1f, 2f, 3f), (cell, x, y, z));
            Assert.False(ClientObjectHooks.TryGetSnapshotPosition(0x80000002u, out _, out _, out _, out _));
            Assert.True(PlayerVitalsHooks.TryGetSnapshot(out PlayerVitalsSnapshot v));
            Assert.Equal(new PlayerVitalsSnapshot(50, 100, 60, 120, 70, 140), v);
            Assert.True(TeleportStateHooks.IsPortaling);
        }
        Assert.Empty(ClientObjectHooks.LiveObjectIds);
        Assert.False(TeleportStateHooks.IsPortaling);
    }

    [Fact]
    public void ALaterHookupKeepsItsHooksWhenAnEarlierOneIsDisposed()
    {
        var first = new RynthMapHookup(_view, _log.Add);
        using var second = new RynthMapHookup(_view, _log.Add);
        _view.Portaling = true;
        first.Dispose();
        Assert.True(TeleportStateHooks.IsPortaling);
    }

    [Fact]
    public void AViewThatThrowsIsLoggedAndAnswersNothing()
    {
        using var hookup = new RynthMapHookup(_view, _log.Add);
        _view.Throw = true;
        Assert.Empty(ClientObjectHooks.LiveObjectIds);
        Assert.Contains(_log, l => l.StartsWith("[Rynth] map LiveObjectIds: view failed:", StringComparison.Ordinal));
    }

    [Fact]
    public void TheCharactersSourceIsPumpedByTheDashboard()
    {
        AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
        Assert.Contains(UiSources.Characters, AcDream.Rynth.EngineUi.RynthAiDashboard.HubSources);
    }

    [Fact]
    public void TheWorldMapDrawsWithNoErrors()
    {
        using var imgui = new HeadlessImGui();
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        try
        {
            AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
            RynthCore.Engine.UI.PanelRouter.Toggle(WorldMapFace.Title);
            for (int i = 0; i < 3; i++)
                imgui.Frame(() => { AcDream.Rynth.EngineUi.RynthAiDashboard.BeginFrame(); ImGuiPanelHost.DrawAll(); });
            Assert.True(ImGuiPanelHost.IsOpen(WorldMapFace.Title));
            Assert.Empty(_log);
        }
        finally { LoginLifecycleHooks.HasObservedLoginComplete = false; }
    }
}
```

Usings for this class: `AcDream.Rynth.Host`, `RynthCore.Engine.Compatibility`, `RynthCore.Engine.ImGuiBackend`, `RynthCore.Engine.ImGuiBackend.Panels`, `RynthCore.Engine.UI.Data`.

`FakeGameView` already has what these need: `Live` (backs `LiveObjectIds`), `Positions`, `Vitals`, `Portaling`, `TargetVitals`, and `Throw` (every read throws `InvalidOperationException("view failed")`).

And the teardown test, in the same class. No overlay-icons fake exists in the tests yet; add this one inside the class (`IClientOverlayIcons` is in `AcDream.ClientExtensions`; `Upload`/`Release` are default interface members, so they are implemented explicitly here):

```csharp
    private sealed class RecordingIcons : AcDream.ClientExtensions.IClientOverlayIcons
    {
        private int _next;
        public List<IntPtr> Released { get; } = new();
        public IntPtr Icon(uint iconId) => IntPtr.Zero;
        public IntPtr SpellIcon(uint spellId) => IntPtr.Zero;
        public IntPtr ObjectIcon(uint objectId) => IntPtr.Zero;
        IntPtr AcDream.ClientExtensions.IClientOverlayIcons.Upload(byte[] rgba, int width, int height, string name) => (IntPtr)(++_next);
        bool AcDream.ClientExtensions.IClientOverlayIcons.Release(IntPtr texture) { Released.Add(texture); return true; }
    }

    private readonly List<string> _temp = new();

    [Fact]
    public void InventoryTeardownReleasesTheMapTilesThroughItsUpload()
    {
        var icons = new RecordingIcons();
        var hookup = new RynthInventoryHookup(_view, icons, _log.Add);
        WorldMapTiles.Open(RynthWorldMapTests.WriteMap(_temp, 1, 2, 3));
        Assert.True(SpinWait.SpinUntil(() => WorldMapTiles.IsOpen, 5000));
        Assert.True(SpinWait.SpinUntil(() => { WorldMapTiles.BeginFrame(IntPtr.Zero); return WorldMapTiles.TryGet(1, 0, 0, out _); }, 5000));
        hookup.Dispose();
        Assert.Single(icons.Released);
    }
```

If `IClientOverlayIcons` has more members than the five above, implement them returning zero/false in the same way. Add `foreach (string f in _temp) File.Delete(f);` to this class's `Dispose`.

Make `WriteMap` in `RynthWorldMapTests` usable from here: change it to `internal static string WriteMap(List<string> temp, byte r, byte g, byte b)` (it adds its path to `temp` instead of `_temp`) and its caller to `WriteMap(_temp, 10, 20, 30)`.

- [ ] **Step 2: Run to verify they fail** — the filter command; expected build errors: `RynthMapHookup` not found, `RynthAiDashboard.HubSources` not found.

- [ ] **Step 3: Implement `RynthMapHookup`** — create `src/AcDream.Rynth.Host/RynthMapHookup.cs`:

```csharp
using System.Numerics;
using RynthCore.Engine.Compatibility;

namespace AcDream.Rynth.Host;

/// <summary>
/// Points the map faces' engine stand-ins (World Map, Mini Map, Radar, the
/// Characters feed) at the game view, and unpoints them on Dispose (only the
/// ones still pointing here). Every answer is guarded: a failure logs and
/// answers no data.
/// </summary>
internal sealed class RynthMapHookup : IDisposable
{
    private readonly IRynthGameView _view;
    private readonly Action<string> _log;
    private readonly Func<uint[]> _liveIds;
    private readonly ClientObjectHooks.ObjectPositionReader _position;
    private readonly PlayerVitalsHooks.SnapshotReader _vitals;
    private readonly Func<bool> _portaling;

    public RynthMapHookup(IRynthGameView view, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(log);
        _view = view;
        _log = log;
        _liveIds = () => Guard(nameof(ClientObjectHooks.LiveObjectIds), () => _view.LiveObjectIds switch
        {
            null => [],
            uint[] ids => ids,
            var ids => ids.ToArray(),
        }, []);
        _position = TryGetPosition;
        _vitals = TryGetVitals;
        _portaling = () => Guard(nameof(TeleportStateHooks.IsPortaling), () => _view.IsPortaling, false);

        ClientObjectHooks.LiveIds = _liveIds;
        ClientObjectHooks.PositionSnapshot = _position;
        PlayerVitalsHooks.Reader = _vitals;
        TeleportStateHooks.Portaling = _portaling;
    }

    public void Dispose()
    {
        if (ClientObjectHooks.LiveIds == _liveIds) ClientObjectHooks.LiveIds = null;
        if (ClientObjectHooks.PositionSnapshot == _position) ClientObjectHooks.PositionSnapshot = null;
        if (PlayerVitalsHooks.Reader == _vitals) PlayerVitalsHooks.Reader = null;
        if (TeleportStateHooks.Portaling == _portaling) TeleportStateHooks.Portaling = null;
    }

    private bool TryGetPosition(uint objectId, out uint cell, out float x, out float y, out float z)
    {
        uint foundCell = 0;
        Vector3 origin = default;
        bool ok = Guard(nameof(ClientObjectHooks.TryGetSnapshotPosition),
            () => _view.TryGetObjectPosition(objectId, out foundCell, out origin), false);
        cell = ok ? foundCell : 0u;
        (x, y, z) = ok ? (origin.X, origin.Y, origin.Z) : (0f, 0f, 0f);
        return ok;
    }

    private bool TryGetVitals(out PlayerVitalsSnapshot snapshot)
    {
        RynthVitals v = default;
        bool ok = Guard(nameof(PlayerVitalsHooks.TryGetSnapshot), () => _view.TryGetPlayerVitals(out v), false);
        snapshot = ok ? new PlayerVitalsSnapshot(v.Health, v.MaxHealth, v.Stamina, v.MaxStamina, v.Mana, v.MaxMana) : default;
        return ok;
    }

    private T Guard<T>(string what, Func<T> read, T fallback)
    {
        try { return read(); }
        catch (Exception error)
        {
            _log($"[Rynth] map {what}: view failed: {error.GetType().Name}: {error.Message}");
            return fallback;
        }
    }
}
```

- [ ] **Step 4: Create and dispose it in the extension** — in `RynthClientExtension.cs`: field `private RynthMapHookup? _mapHookup;` beside `_aiHookup`; after `_aiHookup = new RynthAiHookup(_host, view, context.Log);` add `_mapHookup = new RynthMapHookup(view, context.Log);`; in `Dispose`, after `_aiHookup = null;` add:

```csharp
            _mapHookup?.Dispose();
            _mapHookup = null;
```

- [ ] **Step 5: Wire the dashboard** — in `RynthAiDashboard.cs`:

`BeginFrame`:

```csharp
    /// <summary>RynthCore's EngineFrameController: the retail pictures and World Map tiles read since the last frame become textures before the faces draw.</summary>
    internal static void BeginFrame()
    {
        if (!_started)
            return;
        RetailSprites.BeginFrame(IntPtr.Zero);
        WorldMapTiles.BeginFrame(IntPtr.Zero);
    }
```

In `Start`, after the `MiniRemoteFace` registration:

```csharp
        // Registers the Mini Map too, and loads world_map_settings.txt.
        if (!ImGuiPanelHost.HasFace(WorldMapFace.Title))
            WorldMapFace.Register();
```

In `Sources`, add `UiSources.Characters` after `UiSources.Radar`, and expose it for tests:

```csharp
    internal static IReadOnlyList<UiSource> HubSources => Sources;
```

- [ ] **Step 6: Shut the tiles down with the upload** — in `RynthInventoryHookup.Dispose`, inside the existing `EngineThreads.StoppingFlag` block, after the `RetailSprites.Shutdown` guard:

```csharp
        try
        {
            Guard(nameof(RetailSprites.Shutdown), () => { RetailSprites.Shutdown(); return true; }, false);
            Guard(nameof(WorldMapTiles.Shutdown), () => { WorldMapTiles.Shutdown(); return true; }, false);
        }
        finally { EngineThreads.StoppingFlag = false; }
```

and update its comment to "Their retail pictures and World Map tiles go back to the overlay while our upload is still the one in use."

- [ ] **Step 7: Run** — the filter command → all pass; then the whole project → no new failures.

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.Rynth.Host src/AcDream.Rynth.EngineUi tests/AcDream.Rynth.Host.Tests
git commit -m "feat(rynth): World Map and Mini Map on the dashboard, map hooks from the game view

Claude-Session: https://claude.ai/code/session_011FtdjFB5dEVWdBQnZmstpz"
```

---

### Task 4: The round Radar

**Goal:** RynthCore's `RadarFace` compiles unchanged in place of our legend-table stand-in, is registered, has a bar button, and shows health, selects on click and draws with no errors.

**Files:**
- Modify: `src/AcDream.Rynth.EngineUi/AcDream.Rynth.EngineUi.csproj` (link `RadarFace.cs`; header comment)
- Delete: `src/AcDream.Rynth.EngineUi/StandIns/RadarFaceStandIn.cs`
- Modify: `src/AcDream.Rynth.EngineUi/StandIns/MapStandIns.cs` (`Hud` namespace)
- Modify: `src/AcDream.Rynth.EngineUi/StandIns/DashboardStandIns.cs:120-134` (`EnqueueOverlaySelect`)
- Modify: `src/AcDream.Rynth.EngineUi/StandIns/ChatEngineStandIns.cs:259-263` (`RadarCtrlGatedClickThrough`)
- Modify: `src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiStandIns.cs:106-119` (`GoldGrip`)
- Modify: `src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiPanelHost.cs:153-158` (`TextSizeMenu`)
- Modify: `src/AcDream.Rynth.EngineUi/RynthAiDashboard.cs:42-43,75-77`
- Modify: `src/AcDream.Rynth.Host/RynthMapHookup.cs` (health)
- Modify: `src/AcDream.Rynth.Host/RynthClientExtension.cs:64` (bar buttons)
- Modify: `tests/AcDream.Rynth.Host.Tests/RynthDungeonMapTests.cs:70-86` (remove `TheRadarFaceStandInHasTheirLegendRows`)
- Test: `tests/AcDream.Rynth.Host.Tests/RynthRadarTests.cs` (create)

**Acceptance Criteria:**
- [ ] `RadarFaceStandIn.cs` is deleted; RynthCore's `RadarFace.cs` is compiled; the Dungeon Map still builds against their real `RadarFace.KindRowsOrder`.
- [ ] After `RynthAiDashboard.Start()`, `HasFace("Radar")` is true, and Inventory (which `RadarFace.Register` also registers) is still one working face.
- [ ] `Hud.MonsterHudData.TryGetHealth(id)` answers `Ratio = health / max` from `IRynthGameView.TryGetTargetVitals`; false when unknown or max is 0.
- [ ] `AcMainThreadQueue.EnqueueOverlaySelect(id)` reaches `AcMainThreadQueue.SelectObject`.
- [ ] The bar's panel buttons are Chat, RynthAi, RynthNav, Radar, in that order.
- [ ] With the Radar opened, three headless frames log nothing.

**Verify:** `PATH=$HOME/.dotnet:$PATH dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthRadar|FullyQualifiedName~RynthDungeonMap"` → all pass.

**Steps:**

- [ ] **Step 1: Swap the stand-in for their face** — delete `StandIns/RadarFaceStandIn.cs`; add after the `MapProjection.cs` link:

```xml
    <Compile Include="$(RynthEngine)ImGui\Panels\RadarFace.cs" Link="RynthCore\RadarFace.cs" />
```

Header comment: "World Map and Mini Map panels" → "World Map, Mini Map and Radar panels". Remove `TheRadarFaceStandInHasTheirLegendRows` from `RynthDungeonMapTests.cs` (it compared our copy of the table with theirs; now theirs is compiled).

- [ ] **Step 2: Build to see the gaps** — `PATH=$HOME/.dotnet:$PATH dotnet build src/AcDream.Rynth.EngineUi -c Debug 2>&1 | grep -E " error " | sort -u`. Expected from the probe: `PanelSpec.GoldGrip`, `ImGuiPanelHost.TextSizeMenu`, `Hud.MonsterHudData`, `Hud.HealthObservation`, `AvaloniaOverlay.RadarCtrlGatedClickThrough`, `AcMainThreadQueue.EnqueueOverlaySelect`.

- [ ] **Step 3: Add the stand-ins**

`ImGuiStandIns.cs`, `PanelSpec`: add a last parameter `bool GoldGrip = false` after `bool NoResize = false`, and append to the summary: "GoldGrip (the Radar's gold grip) is accepted and not drawn: our windows keep ImGui's own resize grip."

`ImGuiPanelHost.cs`, after `TextSizeRows`:

```csharp
    /// <summary>RynthCore's per-panel "Text size" context submenu; the overlay has one font size, so this says so.</summary>
    public static void TextSizeMenu(string title)
    {
        if (!ImGui.BeginMenu("Text size"))
            return;
        ImGui.TextDisabled("Uses OpenAC's UI scale.");
        ImGui.EndMenu();
    }
```

`ChatEngineStandIns.cs`, `AvaloniaOverlay`:

```csharp
    /// <summary>RynthCore's Avalonia overlay flags; OpenAC has no Avalonia overlay (their ImGui faces only set them).</summary>
    internal static class AvaloniaOverlay
    {
        internal static volatile bool ChatCtrlGatedClickThrough;
        internal static volatile bool RadarCtrlGatedClickThrough;
    }
```

`DashboardStandIns.cs`, `AcMainThreadQueue`, after `EnqueueSetSelectedObject`:

```csharp
        /// <summary>The Radar's click on a dot: selects the object (RynthCore re-checks it on AC's thread; ours is one thread).</summary>
        public static bool EnqueueOverlaySelect(uint objectId) => SelectObject?.Invoke(objectId) ?? false;
```

`MapStandIns.cs`, append:

```csharp
namespace RynthCore.Engine.ImGuiBackend.Hud
{
    /// <summary>RynthCore's health reading (their MonsterHudData.cs, the record only).</summary>
    internal readonly record struct HealthObservation(float Ratio, uint Current, uint Max, long Ticks, float Prev = -1f, bool Estimated = false);

    /// <summary>RynthCore keeps every health update it sees; here the host answers from the client's latest reading.</summary>
    internal static class MonsterHudData
    {
        internal delegate bool HealthReader(uint objectId, out uint current, out uint max);

        internal static HealthReader? Reader { get; set; }

        public static bool TryGetHealth(uint id, out HealthObservation h)
        {
            h = default;
            if (Reader is not { } read || !read(id, out uint current, out uint max) || max == 0)
                return false;
            h = new HealthObservation(current / (float)max, current, max, Environment.TickCount64);
            return true;
        }
    }
}
```

Build clean (`dotnet build src/AcDream.Rynth.EngineUi`); further gaps follow Global Constraints' last rule.

- [ ] **Step 4: Write the failing tests** — create `tests/AcDream.Rynth.Host.Tests/RynthRadarTests.cs`:

```csharp
using AcDream.Rynth.Host;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.ImGuiBackend.Hud;
using RynthCore.Engine.ImGuiBackend.Panels;
using RynthCore.Engine.UI.Data;

namespace AcDream.Rynth.Host.Tests;

[Collection("WorldMapTiles")]
public sealed class RynthRadarTests : IDisposable
{
    private readonly List<string> _log = new();
    private readonly RynthConfigSandbox _sandbox = new("rynth-radar-");
    private readonly FakeGameView _view = new() { PlayerId = 0x50000001u };

    public RynthRadarTests()
    {
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = _log.Add;
        UiDataHub.Clear();
    }

    public void Dispose()
    {
        AcDream.Rynth.EngineUi.RynthAiDashboard.Stop();
        WorldMapTiles.Shutdown();
        AcMainThreadQueue.SelectObject = null;
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = null;
        _sandbox.Dispose();
    }

    [Fact]
    public void TheDashboardRegistersTheRadarAndKeepsInventory()
    {
        AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
        Assert.True(ImGuiPanelHost.HasFace(RadarFace.Title));
        Assert.True(ImGuiPanelHost.HasFace(InventoryFace.Title));
    }

    [Fact]
    public void HealthComesFromTheTargetsVitals()
    {
        _view.TargetVitals[0x80000005u] = new RynthVitals(30, 120, 0, 0, 0, 0);
        _view.TargetVitals[0x80000006u] = new RynthVitals(0, 0, 0, 0, 0, 0);
        using var hookup = new RynthMapHookup(_view, _log.Add);
        Assert.True(MonsterHudData.TryGetHealth(0x80000005u, out HealthObservation h));
        Assert.Equal(0.25f, h.Ratio);
        Assert.False(MonsterHudData.TryGetHealth(0x80000006u, out _));
        Assert.False(MonsterHudData.TryGetHealth(0x80000007u, out _));
    }

    [Fact]
    public void AClickOnADotSelectsIt()
    {
        uint selected = 0;
        AcMainThreadQueue.SelectObject = id => { selected = id; return true; };
        Assert.True(AcMainThreadQueue.EnqueueOverlaySelect(0x80000009u));
        Assert.Equal(0x80000009u, selected);
    }

    [Fact]
    public void TheBarHasARadarButton() =>
        Assert.Equal(["Chat", "RynthAi", "RynthNav", "Radar"], RynthClientExtension.BarPanels);

    [Fact]
    public void TheRadarDrawsWithNoErrors()
    {
        using var imgui = new HeadlessImGui();
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        try
        {
            AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
            RynthCore.Engine.UI.PanelRouter.Toggle(RadarFace.Title);
            for (int i = 0; i < 3; i++)
                imgui.Frame(() => { AcDream.Rynth.EngineUi.RynthAiDashboard.BeginFrame(); ImGuiPanelHost.DrawAll(); });
            Assert.True(ImGuiPanelHost.IsOpen(RadarFace.Title));
            Assert.Empty(_log);
        }
        finally { LoginLifecycleHooks.HasObservedLoginComplete = false; }
    }
}
```

Check the titles: `RynthChatWindow.Title`, `RynthAiDashboard.Title` and `RynthNavPanel.Title` must be "Chat", "RynthAi" and "RynthNav"; if one differs, use the real title in the expected list.

- [ ] **Step 5: Run to verify they fail** — the Verify command; expected build errors for `RynthClientExtension.BarPanels`, and the registration/health tests failing.

- [ ] **Step 6: Implement**

`RynthAiDashboard.Start`, after the World Map registration:

```csharp
        // Registers Inventory again (RynthCore does it from here); RegisterDynamic replaces that entry in place.
        if (!ImGuiPanelHost.HasFace(RadarFace.Title))
            RadarFace.Register();
```

and change the earlier comment "RynthCore's EntryPoint loads radar_settings.txt at startup; their Radar panel isn't compiled here." to "RynthCore's EntryPoint loads radar_settings.txt at startup, before the Radar registers."

`RynthMapHookup`: field `private readonly RynthCore.Engine.ImGuiBackend.Hud.MonsterHudData.HealthReader _health;`; in the constructor `_health = TryGetHealth;` and `RynthCore.Engine.ImGuiBackend.Hud.MonsterHudData.Reader = _health;`; in `Dispose` `if (MonsterHudData.Reader == _health) MonsterHudData.Reader = null;` (with `using RynthCore.Engine.ImGuiBackend.Hud;`); and:

```csharp
    private bool TryGetHealth(uint objectId, out uint current, out uint max)
    {
        RynthVitals v = default;
        bool ok = Guard(nameof(MonsterHudData.TryGetHealth), () => _view.TryGetTargetVitals(objectId, out v), false);
        (current, max) = ok ? (v.Health, v.MaxHealth) : (0u, 0u);
        return ok;
    }
```

Update the class summary's list to "(World Map, Mini Map, Radar health, the Characters feed)".

`RynthClientExtension`: add

```csharp
    /// <summary>The bar's panel buttons, in order (RynthCore's bar has the Radar too).</summary>
    internal static readonly IReadOnlyList<string> BarPanels =
        [RynthChatWindow.Title, RynthAiDashboard.Title, RynthNavPanel.Title, RynthCore.Engine.ImGuiBackend.Panels.RadarFace.Title];
```

and change line 64 to `PanelButtons = () => BarPanels`. If `PanelButtons` is typed `Func<IReadOnlyList<string>>`, this compiles as is.

- [ ] **Step 7: Run** — Verify command → all pass; then the whole project → no new failures.

- [ ] **Step 8: Commit**

```bash
git add -A src/AcDream.Rynth.EngineUi src/AcDream.Rynth.ScriptWindows src/AcDream.Rynth.Host tests/AcDream.Rynth.Host.Tests
git commit -m "feat(rynth): RynthCore's Radar with map backdrop, health and click-to-select; bar button

Claude-Session: https://claude.ai/code/session_011FtdjFB5dEVWdBQnZmstpz"
```

---

### Task 5: Full gate, Release build and the live check

**Goal:** The whole solution's Rynth tests pass, the Release build used by the user's client is made, and the user runs the live checklist later.

**Files:**
- None changed unless the gate finds a failure (fix it in the task that owns the code, with a test).

**Acceptance Criteria:**
- [ ] `PATH=$HOME/.dotnet:$PATH dotnet test tests/AcDream.Rynth.Host.Tests` reports 0 failed and at least 411 + this plan's new tests passed.
- [ ] `PATH=$HOME/.dotnet:$PATH dotnet build AcDream.slnx -c Release` succeeds with 0 errors.
- [ ] The live checklist below is handed to the user; the agent does NOT launch a client (user rule: ask first; live checks later).

**Verify:** `PATH=$HOME/.dotnet:$PATH dotnet test tests/AcDream.Rynth.Host.Tests 2>&1 | tail -3` → `Failed: 0`.

**Steps:**

- [ ] **Step 1:** Run the Host test project and the Release build (commands above); capture the summary lines.
- [ ] **Step 2:** Give the user this checklist (live, on the local ACE server, when they say they are available):
  - Dashboard World Map button opens the map: tiles load and sharpen on zoom; buildings and roads appear when zoomed in; places and the find box work; the player arrow tracks; the RynthNav route shows; right-click Go here / Route here / Arrow here.
  - RynthNav panel Mini Map button: Follow and Rotate.
  - Bar Radar button: markers, hover text with health %, click on a dot selects it, settings popup (legend), outdoors the map backdrop (settings: Show world map), indoors the dungeon geometry.
  - Retina: tiles and text at the right size; the three windows remember their position after a relog.
