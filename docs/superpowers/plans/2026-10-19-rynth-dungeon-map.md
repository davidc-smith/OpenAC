# RynthAi Dungeon Map Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The RynthAi dashboard's Map button opens RynthCore's own Dungeon Map (floors, walls, explored cells, markers, player, nav route with editing, dungeon names) in OpenAC.

**Architecture:** RynthCore's map, radar data and atlas files compile unchanged in `AcDream.Rynth.EngineUi`; their `RadarSource` polls the already-loaded RynthAi plugin's `RynthPluginGetRadarSnapshot` export through the existing `PluginExportBinder` stand-in. The panel host stand-in learns `OpenCentered` and `HiddenWhileDocked`; the dashboard installs RynthCore's `locations.json` (embedded) into the RynthCore folder and warms their atlas off-thread.

**Tech Stack:** .NET 10, C#, ImGui.NET, xunit.

**Spec:** `docs/superpowers/specs/2026-10-19-rynth-dungeon-map-design.md`

## Global Constraints

- Never edit anything under `external/RynthCore` or `external/RynthSuite`; their files are compiled as-is (memory: rynth-no-rewrite).
- Build with the SDK in `~/.dotnet`: `export PATH=~/.dotnet:$PATH DOTNET_ROOT=~/.dotnet`.
- Stand-in style: same namespace and member names as RynthCore's type, a short `<summary>` saying what RynthCore does and what OpenAC does instead (see `src/AcDream.Rynth.EngineUi/StandIns/DashboardStandIns.cs`).
- No new host slots, no `IRynthGameView` changes, no contract bump (nothing in `AcDream.Plugin.Abstractions` changes).
- RynthCore's Radar panel (`RadarFace`) is not compiled; only its `KindRowsOrder` table is copied into a stand-in.
- Don't run `tools/update-package-locks.ps1`.
- Commit message trailer: `Claude-Session: https://claude.ai/code/session_011FtdjFB5dEVWdBQnZmstpz`.

**User decisions (already made):**
- Dungeon Map only; the round Radar panel waits (2026-10-19: "Dungeon Map only").
- Real dungeon names from RynthCore's `locations.json`, compiling their `RynthNavAtlasData.cs` unchanged (2026-10-19: "Real names").
- Live checks happen later, when the user says so (memory: ask-before-live-client).

---

### Task 1: Panel host: `OpenCentered` and `HiddenWhileDocked`

**Goal:** The ImGui panel host stand-in places an `OpenCentered` panel centred on its first open and skips drawing an open panel while it answers `HiddenWhileDocked`, as RynthCore's host does.

**Files:**
- Modify: `src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiStandIns.cs` (`PanelSpec`, ~line 96)
- Modify: `src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiPanelHost.cs` (`DrawAll` open-window loops, ~lines 284-345)
- Test: `tests/AcDream.Rynth.Host.Tests/ImGuiPanelHostTests.cs`

**Acceptance Criteria:**
- [ ] `PanelSpec` has `bool OpenCentered = false` as its last parameter.
- [ ] An `OpenCentered` panel with no saved spot, requested position or `DefaultPos` opens at `max(0, (display/scale - DefaultSize)/2)`: (400, 140) for 480×520 on the 1280×800 headless display.
- [ ] A saved layout spot still wins over `OpenCentered`.
- [ ] An open panel answering `HiddenWhileDocked == true` gets no window (no `Draw` calls), stays open, and draws again once it answers false.
- [ ] A panel stopped after `MaxFailures` is never hidden; a throwing `HiddenWhileDocked` getter counts as false.

**Verify:** `dotnet test tests/AcDream.Rynth.Host.Tests --filter ImGuiPanelHostTests` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** (append to `ImGuiPanelHostTests`, plus a new panel class beside `RecordingPanel`)

```csharp
    [Fact]
    public void AnOpenCenteredPanelOpensInTheMiddleOfTheDisplay()
    {
        var panel = new PositionPanel();
        ImGuiPanelHost.RegisterDynamic("script:map", "Map",
            new PanelSpec(new Vector2(480, 520), new Vector2(240, 200), OpenCentered: true), () => panel);
        ImGuiPanelHost.Open("script:map");

        _imgui.Frame(ImGuiPanelHost.DrawAll);
        _imgui.Frame(ImGuiPanelHost.DrawAll);

        Assert.Equal(new Vector2(400, 140), panel.WindowPos);
    }

    [Fact]
    public void ASavedSpotWinsOverOpenCentered()
    {
        var layout = new FakePanelLayout();
        layout.Saved["script:map"] = new PanelPlacement(new PanelRect(new Vector2(10, 20), new Vector2(480, 520)), false);
        ImGuiPanelHost.Layout = layout;
        try
        {
            var panel = new PositionPanel();
            ImGuiPanelHost.RegisterDynamic("script:map", "Map",
                new PanelSpec(new Vector2(480, 520), new Vector2(240, 200), OpenCentered: true), () => panel);
            ImGuiPanelHost.Open("script:map");

            _imgui.Frame(ImGuiPanelHost.DrawAll);
            _imgui.Frame(ImGuiPanelHost.DrawAll);

            Assert.Equal(new Vector2(10, 20), panel.WindowPos);
        }
        finally { ImGuiPanelHost.Layout = null; }
    }

    [Fact]
    public void AHiddenWhileDockedPanelIsNotDrawnButStaysOpen()
    {
        var panel = new PositionPanel { Hide = true };
        ImGuiPanelHost.RegisterDynamic("script:map", "Map", new PanelSpec(new Vector2(200, 100), new Vector2(50, 50)), () => panel);
        ImGuiPanelHost.Open("script:map");

        _imgui.Frame(ImGuiPanelHost.DrawAll);
        _imgui.Frame(ImGuiPanelHost.DrawAll);
        Assert.Equal(0, panel.Draws);
        Assert.True(ImGuiPanelHost.IsOpen("script:map"));

        panel.Hide = false;
        _imgui.Frame(ImGuiPanelHost.DrawAll);
        Assert.True(panel.Draws >= 1);
    }

    [Fact]
    public void AThrowingHiddenWhileDockedGetterCountsAsShown()
    {
        var panel = new PositionPanel { HideThrows = true };
        ImGuiPanelHost.RegisterDynamic("script:map", "Map", new PanelSpec(new Vector2(200, 100), new Vector2(50, 50)), () => panel);
        ImGuiPanelHost.Open("script:map");

        _imgui.Frame(ImGuiPanelHost.DrawAll);

        Assert.True(panel.Draws >= 1);
    }

    [Fact]
    public void AStoppedPanelIsNeverHidden()
    {
        var panel = new PositionPanel { Throw = true };
        var log = new List<string>();
        ImGuiPanelHost.Log = log.Add;
        ImGuiPanelHost.RegisterDynamic("script:map", "Map", new PanelSpec(new Vector2(200, 100), new Vector2(50, 50)), () => panel);
        ImGuiPanelHost.Open("script:map");
        for (int i = 0; i < 5; i++)
            _imgui.Frame(ImGuiPanelHost.DrawAll);   // stopped after MaxFailures

        panel.Hide = true;
        int vertices = _imgui.Frame(ImGuiPanelHost.DrawAll);

        Assert.True(vertices > 0, "the stopped panel's error line should still draw");
    }

    private sealed class PositionPanel : IImGuiPanel
    {
        public bool Hide { get; set; }
        public bool HideThrows { get; init; }
        public bool Throw { get; init; }
        public int Draws { get; private set; }
        public Vector2 WindowPos { get; private set; }

        public bool HiddenWhileDocked => HideThrows ? throw new InvalidOperationException("getter failed") : Hide;

        public void Draw()
        {
            Draws++;
            WindowPos = ImGui.GetWindowPos();
            ImGui.Text("panel body");
            if (Throw)
                throw new InvalidOperationException("body failed");
        }
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/AcDream.Rynth.Host.Tests --filter ImGuiPanelHostTests`
Expected: compile error `PanelSpec does not have a parameter named 'OpenCentered'`.

- [ ] **Step 3: Add `OpenCentered` to `PanelSpec`** (`ImGuiStandIns.cs`; update its summary to mention it)

```csharp
    /// <summary>The members of RynthCore's PanelSpec that their faces set (of Background, BorderColor and Rounding only a zero Background is honoured; OpenCentered centres a first open).</summary>
    internal sealed record PanelSpec(
        Vector2 DefaultSize,
        Vector2 MinSize,
        PanelChrome Chrome = PanelChrome.Standard,
        uint? Background = null,
        bool NoPopOut = false,
        ImGuiWindowFlags BodyFlags = ImGuiWindowFlags.None,
        Vector2? DefaultPos = null,
        bool EdgeToEdge = false,
        bool GripInBody = false,
        uint? BorderColor = null,
        float Rounding = 0f,
        bool OpenCentered = false);
```

- [ ] **Step 4: Hide and centre in `ImGuiPanelHost.DrawAll`**

In the first loop (the one under `lock (_gate)` that builds `open`), skip hidden panels before any placement flag is consumed, so a saved spot still applies when the panel comes back:

```csharp
                Entry e = pair.Value;
                if (!e.Open || HiddenWhileDocked(e))
                    continue;
```

In the position chain of the second loop, after the `DefaultPos` branch:

```csharp
            else if (entry.Spec.DefaultPos is { } defaultPos)
            {
                ImGui.SetNextWindowPos(defaultPos * s, ImGuiCond.FirstUseEver);
            }
            else if (entry.Spec.OpenCentered)
            {
                // RynthCore's CenteredLogicalPos: the default size, centred, never off the top-left.
                Vector2 screen = ImGui.GetIO().DisplaySize;
                Vector2 centred = Vector2.Max(Vector2.Zero, (screen / s - entry.Spec.DefaultSize) * 0.5f);
                ImGui.SetNextWindowPos(centred * s, ImGuiCond.FirstUseEver);
            }
```

And the helper, beside `TrackPlacement`:

```csharp
    /// <summary>
    /// RynthCore's HiddenWhileDocked: an open panel can ask not to be drawn for now
    /// (the Dungeon Map outdoors). A stopped panel always shows its error line, and a
    /// getter that throws counts as shown.
    /// </summary>
    private static bool HiddenWhileDocked(Entry entry)
    {
        if (entry.Failures >= MaxFailures)
            return false;
        try { return entry.Instance?.HiddenWhileDocked ?? false; }
        catch { return false; }
    }
```

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test tests/AcDream.Rynth.Host.Tests --filter ImGuiPanelHostTests`
Expected: all pass. Then run the whole project once (`dotnet test tests/AcDream.Rynth.Host.Tests`) — expected 315 before + 5 new, all pass.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiStandIns.cs src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiPanelHost.cs tests/AcDream.Rynth.Host.Tests/ImGuiPanelHostTests.cs
git commit -m "feat(rynth): panel host honours OpenCentered and HiddenWhileDocked"
```

---

### Task 2: Compile RynthCore's Dungeon Map with stand-ins

**Goal:** Their Dungeon Map, radar data, marker style and atlas files compile unchanged; the dashboard registers the map, polls `UiSources.Radar`, and the panel draws a RynthAi radar snapshot.

**Files:**
- Modify: `src/AcDream.Rynth.EngineUi/AcDream.Rynth.EngineUi.csproj` (Compile items after `TellMemory.cs`; update the header comment's panel list)
- Modify: `src/AcDream.Rynth.EngineUi/StandIns/DashboardStandIns.cs` (remove `DungeonMapFace` stand-in)
- Modify: `src/AcDream.Rynth.EngineUi/StandIns/ChatEngineStandIns.cs` (remove `RynthNavCommands` stand-in; `UiSources.Radar`, `UiSources.RynthNav`)
- Create: `src/AcDream.Rynth.EngineUi/StandIns/RadarFaceStandIn.cs`
- Modify: `src/AcDream.Rynth.EngineUi/RynthAiDashboard.cs` (`Start`, `Sources`, summary)
- Test: `tests/AcDream.Rynth.Host.Tests/RynthAiDashboardTests.cs`, Create: `tests/AcDream.Rynth.Host.Tests/RynthDungeonMapTests.cs`

**Acceptance Criteria:**
- [ ] The csproj compiles these unchanged: `ImGui\Panels\DungeonMapFace.cs`, `UI\Data\RadarData.cs`, `UI\Data\RadarSnapshotJson.cs`, `UI\Data\DungeonMapMath.cs`, `ImGui\Panels\RadarMarkerStyle.cs`, `ImGui\Panels\CircleClip.cs`, `UI\Panels\MarkerStyleSettings.cs`, `ImGui\Panels\MarkerStyleEditor.cs`, `UI\Panels\RadarSettingsStore.cs`, `UI\Data\RynthNavAtlasData.cs`, `UI\Data\RynthNavData.cs`; all listed in `TheirFiles`; no copies in `src`.
- [ ] `RadarFace.KindRowsOrder` stand-in equals their table (12 rows, same order and labels), checked by parsing their `RadarFace.cs`.
- [ ] `RynthAiDashboard.Start()` registers `DungeonMapFace`; the dashboard's Map toggle (`PanelRouter.Toggle(DungeonMapFace.Title)`) opens it.
- [ ] With the real RynthAi plugin and the fake view's pose, subscribing `UiSources.Radar` and ticking publishes a `RadarView` whose `Live.Player.CellId` is the pose's cell.
- [ ] With a test export returning an indoor snapshot (floors, walls, a monster and a door marker), the open map draws headless (> 100 vertices) with no log lines; with an outdoor snapshot it draws nothing and stays open.

**Verify:** `dotnet test tests/AcDream.Rynth.Host.Tests` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests**

In `RynthAiDashboardTests.TheirFiles`, append:

```csharp
        @"ImGui\Panels\DungeonMapFace.cs", @"UI\Data\RadarData.cs", @"UI\Data\RadarSnapshotJson.cs", @"UI\Data\DungeonMapMath.cs",
        @"ImGui\Panels\RadarMarkerStyle.cs", @"ImGui\Panels\CircleClip.cs", @"UI\Panels\MarkerStyleSettings.cs",
        @"ImGui\Panels\MarkerStyleEditor.cs", @"UI\Panels\RadarSettingsStore.cs", @"UI\Data\RynthNavAtlasData.cs", @"UI\Data\RynthNavData.cs",
```

Create `tests/AcDream.Rynth.Host.Tests/RynthDungeonMapTests.cs`:

```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using AcDream.Rynth.Host;
using AcDream.TestSupport;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.ImGuiBackend.Panels;
using RynthCore.Engine.UI.Data;

namespace AcDream.Rynth.Host.Tests;

public sealed unsafe class RynthDungeonMapTests : IDisposable
{
    // Indoor: landblock 0x0153 (Shreth Hive), one floor at z 0 with a strip, one wall, a monster and a door.
    private const string IndoorJson =
        "{\"mapVersion\":7,\"geometryIncluded\":true,\"isIndoor\":true," +
        "\"player\":{\"cellId\":22217000,\"landblock\":339,\"x\":10,\"y\":10,\"z\":0,\"worldX\":10,\"worldY\":10,\"heading\":90}," +
        "\"layerZs\":[0],\"currentLayerZ\":0," +
        "\"walls\":[{\"z\":0,\"segments\":[0,0,20,0,10,10]}]," +
        "\"fills\":[{\"z\":0,\"strips\":[0,0,20,20,0]}]," +
        "\"visited\":[{\"z\":0,\"strips\":[0,0,10,10]}]," +
        "\"markers\":[{\"kind\":0,\"x\":12,\"y\":12,\"z\":0,\"label\":\"Drudge\",\"id\":1},{\"kind\":3,\"x\":5,\"y\":0,\"z\":0,\"label\":\"Door\",\"id\":2}]}";

    private const string OutdoorJson =
        "{\"mapVersion\":8,\"geometryIncluded\":false,\"isIndoor\":false," +
        "\"player\":{\"cellId\":2847146017,\"landblock\":43444,\"x\":100,\"y\":100,\"z\":50,\"worldX\":100,\"worldY\":100,\"heading\":0}," +
        "\"ns\":12.5,\"ew\":40.1,\"markers\":[]}";

    private static IntPtr _json;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static IntPtr Snapshot(uint mapVersion) => _json;

    private readonly List<string> _log = new();
    private readonly RynthConfigSandbox _sandbox = new("rynth-dmap-");
    private readonly FakeGameView _view = new() { PlayerId = 0x50000001u };

    public RynthDungeonMapTests()
    {
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = _log.Add;
        UiDataHub.Clear();
    }

    public void Dispose()
    {
        AcDream.Rynth.EngineUi.RynthAiDashboard.Stop();
        PluginExportBinder.Resolver = null;
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = null;
        LoginLifecycleHooks.HasObservedLoginComplete = false;
        SetJson(null);
        _sandbox.Dispose();
    }

    private static void SetJson(string? json)
    {
        IntPtr old = _json;
        _json = json is null ? IntPtr.Zero : Marshal.StringToHGlobalAnsi(json);
        if (old != IntPtr.Zero)
            Marshal.FreeHGlobal(old);
    }

    private static void UseTestExport() =>
        PluginExportBinder.Resolver = (plugin, export) =>
            plugin == "RynthAi" && export == "RynthPluginGetRadarSnapshot"
                ? (IntPtr)(delegate* unmanaged[Cdecl]<uint, IntPtr>)&Snapshot
                : IntPtr.Zero;

    [Fact]
    public void TheRadarFaceStandInHasTheirLegendRows()
    {
        string theirs = File.ReadAllText(Path.Combine(RynthAiDashboardTests.RepoRoot(),
            "external/RynthCore/src/RynthCore.Engine/ImGui/Panels/RadarFace.cs"));
        string table = theirs[theirs.IndexOf("KindRowsOrder", StringComparison.Ordinal)..];
        table = table[..table.IndexOf("};", StringComparison.Ordinal)];
        var rows = Regex.Matches(table, @"\(RadarKind\.(\w+), ""([^""]+)""\)")
            .Select(m => ((byte)typeof(RadarKind).GetField(m.Groups[1].Value)!.GetValue(null)!, m.Groups[2].Value))
            .ToArray();

        Assert.Equal(12, rows.Length);
        Assert.Equal(rows, RadarFace.KindRowsOrder.Select(r => (r.Kind, r.Label)).ToArray());
    }

    [Fact]
    public void TheRealRynthAiRadarExportReachesTheRadarSource()
    {
        _view.Pose = (0xA9B40021u, new System.Numerics.Vector3(100, 100, 50), System.Numerics.Quaternion.Identity);
        _view.PlayerHeading = 0f;
        using var host = new RynthHost(_view, _log.Add, [typeof(RynthCore.Plugin.RynthAi.PluginExports)]);
        using var exports = new RynthChatHookup(host, _view, _log.Add);
        using var ai = new RynthAiHookup(host, _view, _log.Add);
        AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
        UiSources.Radar.Subscribe();
        try
        {
            UiSources.Radar.RequestRefresh();
            AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();

            RadarView? view = UiSources.Radar.Current?.Value;
            Assert.NotNull(view);
            Assert.Equal(0xA9B40021u, view!.Live.Player.CellId);
            Assert.False(view.Live.IsIndoor);
        }
        finally { UiSources.Radar.Unsubscribe(); }
    }

    [Fact]
    public void TheMapRegistersWithTheDashboardAndDrawsAnIndoorSnapshot()
    {
        using var imgui = new HeadlessImGui();
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
        UseTestExport();
        SetJson(IndoorJson);
        Assert.True(ImGuiPanelHost.HasFace(DungeonMapFace.Title));

        RynthCore.Engine.UI.PanelRouter.Toggle(DungeonMapFace.Title);
        try
        {
            for (int i = 0; i < 3; i++)
            {
                AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();
                int vertices = imgui.Frame(ImGuiPanelHost.DrawAll);
                Assert.True(vertices > 100, $"indoor frame {i}: only {vertices} vertices");
            }

            Assert.True(UiSources.Radar.Current?.Value?.Live.IsIndoor);
            Assert.Empty(_log);
        }
        finally { RynthCore.Engine.UI.PanelRouter.Toggle(DungeonMapFace.Title); }
    }

    [Fact]
    public void TheMapHidesOutdoorsAndStaysOpen()
    {
        using var imgui = new HeadlessImGui();
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
        UseTestExport();
        SetJson(OutdoorJson);

        RynthCore.Engine.UI.PanelRouter.Toggle(DungeonMapFace.Title);
        try
        {
            int vertices = 0;
            for (int i = 0; i < 3; i++)
            {
                AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();
                vertices = imgui.Frame(ImGuiPanelHost.DrawAll);
            }

            Assert.False(UiSources.Radar.Current?.Value?.Live.IsIndoor);
            Assert.True(ImGuiPanelHost.IsOpen(DungeonMapFace.Title));
            Assert.Equal(0, vertices);
        }
        finally { RynthCore.Engine.UI.PanelRouter.Toggle(DungeonMapFace.Title); }
    }

}
```

Notes for the implementer:
- `RynthAiDashboardTests.RepoRoot()` is `internal static`; reuse it, don't copy it.
- The map subscribes `UiSources.Radar` itself while shown (their `OnShown`: settings load, `Subscribe`, `RequestRefresh`), so the panel-drawing tests don't call `Subscribe`.
- `ImGuiPanelHost.DrawAll` draws only panels, so with the map hidden and nothing else open the frame has 0 vertices.
- If their `DungeonMapFace` logs a status line on open (as `SkillsFace` does), relax `Assert.Empty(_log)` to `Assert.All(_log, l => Assert.StartsWith("[Rynth] [ScriptWindows] Dungeon Map: ", l))` and say so in the commit message.
- If the real-plugin test finds `UiSources.Radar.Current` null because RynthAi answers `"{}"` before its dashboard renderer exists, drive RynthAi's startup the way `TheSnapshotReachesTheRynthAiSource` gets a RynthAi snapshot (same `RynthHost` + hookups + `Start()`), and report what was needed.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/AcDream.Rynth.Host.Tests --filter "RynthDungeonMapTests|TheProjectCompilesTheirFilesAndHasNoCopies"`
Expected: compile errors (`RadarFace`, `UiSources.Radar`, `RadarView` not found).

- [ ] **Step 3: Add their files to the csproj** (after the `TellMemory.cs` line)

```xml
    <Compile Include="$(RynthEngine)ImGui\Panels\DungeonMapFace.cs" Link="RynthCore\DungeonMapFace.cs" />
    <Compile Include="$(RynthEngine)UI\Data\RadarData.cs" Link="RynthCore\Data\RadarData.cs" />
    <Compile Include="$(RynthEngine)UI\Data\RadarSnapshotJson.cs" Link="RynthCore\Data\RadarSnapshotJson.cs" />
    <Compile Include="$(RynthEngine)UI\Data\DungeonMapMath.cs" Link="RynthCore\Data\DungeonMapMath.cs" />
    <Compile Include="$(RynthEngine)ImGui\Panels\RadarMarkerStyle.cs" Link="RynthCore\RadarMarkerStyle.cs" />
    <Compile Include="$(RynthEngine)ImGui\Panels\CircleClip.cs" Link="RynthCore\CircleClip.cs" />
    <Compile Include="$(RynthEngine)UI\Panels\MarkerStyleSettings.cs" Link="RynthCore\MarkerStyleSettings.cs" />
    <Compile Include="$(RynthEngine)ImGui\Panels\MarkerStyleEditor.cs" Link="RynthCore\MarkerStyleEditor.cs" />
    <Compile Include="$(RynthEngine)UI\Panels\RadarSettingsStore.cs" Link="RynthCore\RadarSettingsStore.cs" />
    <Compile Include="$(RynthEngine)UI\Data\RynthNavAtlasData.cs" Link="RynthCore\Data\RynthNavAtlasData.cs" />
    <Compile Include="$(RynthEngine)UI\Data\RynthNavData.cs" Link="RynthCore\Data\RynthNavData.cs" />
```

Add "Dungeon Map" to the header comment's list of compiled panels.

- [ ] **Step 4: Stand-ins**

In `DashboardStandIns.cs` delete the whole `DungeonMapFace` stand-in (its `<summary>` and class).

In `ChatEngineStandIns.cs` delete the `RynthNavCommands` stand-in (summary "RynthNav's "Arrow to" / "Go to"; no RynthNav in OpenAC." and its class); their `RynthNavData.cs` now supplies the real one, which resolves no RynthNav export and does nothing. Add to `UiSources` after `MonsterDetail`:

```csharp
        public static readonly RadarSource Radar = new();
        public static readonly RynthNavSource RynthNav = new();
```

Create `src/AcDream.Rynth.EngineUi/StandIns/RadarFaceStandIn.cs`:

```csharp
using RynthCore.Engine.UI.Data;

namespace RynthCore.Engine.ImGuiBackend.Panels
{
    /// <summary>
    /// RynthCore's round Radar panel; not in OpenAC (its outdoor backdrop builds Direct3D 9
    /// world-map tiles). The Dungeon Map's Markers window lists the marker kinds in the
    /// radar's legend order, so only that table is here, copied from theirs.
    /// </summary>
    internal static class RadarFace
    {
        // Legend order: creatures, then corpses, then places, then items.
        internal static readonly (byte Kind, string Label)[] KindRowsOrder =
        {
            (RadarKind.Monster, "Monsters"), (RadarKind.Player, "Players"), (RadarKind.Fellow, "Fellows"),
            (RadarKind.Npc, "NPCs"), (RadarKind.Vendor, "Vendors"), (RadarKind.Pet, "Your pets"),
            (RadarKind.Corpse, "Corpses"), (RadarKind.OwnCorpse, "Your corpses"), (RadarKind.Lifestone, "Lifestones"),
            (RadarKind.Portal, "Portals"), (RadarKind.Door, "Doors"), (RadarKind.GroundItem, "Items on the ground"),
        };
    }
}
```

- [ ] **Step 5: Dashboard wiring** (`RynthAiDashboard.cs`)

In `Start()`, after the Skills registration:

```csharp
        if (!ImGuiPanelHost.HasFace(DungeonMapFace.Title))
            DungeonMapFace.Register();
```

Add `UiSources.Radar` to `Sources` (after `UiSources.Progression`), and "Dungeon Map" to the class summary's panel list.

- [ ] **Step 6: Run to verify they pass**

Run: `dotnet test tests/AcDream.Rynth.Host.Tests`
Expected: all pass (Task 1 total + 5 new). `dotnet build src/AcDream.Rynth.EngineUi` has no new warnings.

- [ ] **Step 7: Commit**

```bash
git add src/AcDream.Rynth.EngineUi tests/AcDream.Rynth.Host.Tests/RynthAiDashboardTests.cs tests/AcDream.Rynth.Host.Tests/RynthDungeonMapTests.cs
git commit -m "feat(rynth): RynthCore's Dungeon Map compiled unchanged"
```

---

### Task 3: Dungeon names: install and warm the Atlas

**Goal:** `RynthAiDashboard.Start()` installs RynthCore's embedded `locations.json` into `<CoreDir>/NavData/` and warms their `RynthNavAtlas` on a pool thread, so the map's indoor readout names the dungeon.

**Files:**
- Modify: `src/AcDream.Rynth.EngineUi/AcDream.Rynth.EngineUi.csproj` (EmbeddedResource)
- Create: `src/AcDream.Rynth.EngineUi/NavAtlasInstall.cs`
- Modify: `src/AcDream.Rynth.EngineUi/RynthAiDashboard.cs` (`Start`)
- Test: `tests/AcDream.Rynth.Host.Tests/RynthDungeonMapTests.cs`

**Acceptance Criteria:**
- [ ] `locations.json` is embedded as `AcDream.Rynth.EngineUi.locations.json` from `external/RynthCore/tools/navdata/NavData/locations.json`.
- [ ] `NavAtlasInstall.Ensure(coreDir, isWindows, log)` writes `<coreDir>/NavData/locations.json` when missing; leaves a same-size file untouched; rewrites a different-size file off Windows only; writes via `locations.json.tmp` then move; on an IO / access error logs one line naming the path and returns false without throwing.
- [ ] After installing into a temp folder, `RynthNavAtlas.EnsureLoaded(path)` then `RynthNavAtlas.DungeonLabel(0x0153)` is `"Shreth Hive"`; an unknown landblock gives `"LB 0001"`.
- [ ] `Start()` calls `NavAtlasInstall.Ensure(RynthInstallPaths.CoreDir, OperatingSystem.IsWindows(), Log)` once, then `Task.Run(RynthNavAtlas.EnsureLoadedForNames)`.

**Verify:** `dotnet test tests/AcDream.Rynth.Host.Tests` → all pass; `dotnet build AcDream.slnx -c Release` clean.

**Steps:**

- [ ] **Step 1: Write the failing tests** (append to `RynthDungeonMapTests`)

```csharp
    [Fact]
    public void TheAtlasIsInstalledWhenMissingAndNamesTheDungeon()
    {
        string core = Path.Combine(_sandbox.Root, "atlas-core");
        var log = new List<string>();

        Assert.True(AcDream.Rynth.EngineUi.NavAtlasInstall.Ensure(core, isWindows: false, log.Add));

        string path = AcDream.Rynth.EngineUi.NavAtlasInstall.PathUnder(core);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Empty(log);
        // Their loader runs inline here (UiBackgroundWriter stand-in), so names are ready after the call.
        RynthNavAtlas.EnsureLoaded(path);
        Assert.Equal("Shreth Hive", RynthNavAtlas.DungeonLabel(0x0153));
        Assert.Equal("LB 0001", RynthNavAtlas.DungeonLabel(0x0001));
    }

    [Fact]
    public void AMatchingAtlasIsLeftAlone()
    {
        string core = Path.Combine(_sandbox.Root, "atlas-same");
        AcDream.Rynth.EngineUi.NavAtlasInstall.Ensure(core, isWindows: false, _ => { });
        string path = AcDream.Rynth.EngineUi.NavAtlasInstall.PathUnder(core);
        var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, stamp);

        Assert.True(AcDream.Rynth.EngineUi.NavAtlasInstall.Ensure(core, isWindows: false, _ => { }));

        Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void ADifferentAtlasIsReplacedOffWindowsOnly()
    {
        string core = Path.Combine(_sandbox.Root, "atlas-diff");
        string path = AcDream.Rynth.EngineUi.NavAtlasInstall.PathUnder(core);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{\"locations\":[]}");

        Assert.True(AcDream.Rynth.EngineUi.NavAtlasInstall.Ensure(core, isWindows: true, _ => { }));
        Assert.Equal("{\"locations\":[]}", File.ReadAllText(path));

        Assert.True(AcDream.Rynth.EngineUi.NavAtlasInstall.Ensure(core, isWindows: false, _ => { }));
        Assert.True(new FileInfo(path).Length > 1_000_000);
    }

    [Fact]
    public void AnUnwritableFolderIsLoggedNotThrown()
    {
        // A file where the RynthCore folder should be: creating NavData under it fails.
        string core = Path.Combine(_sandbox.Root, "atlas-blocked");
        File.WriteAllText(core, "not a folder");
        var log = new List<string>();

        Assert.False(AcDream.Rynth.EngineUi.NavAtlasInstall.Ensure(core, isWindows: false, log.Add));

        string line = Assert.Single(log);
        Assert.Contains(AcDream.Rynth.EngineUi.NavAtlasInstall.PathUnder(core), line, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/AcDream.Rynth.Host.Tests --filter RynthDungeonMapTests`
Expected: compile error `NavAtlasInstall does not exist`.

- [ ] **Step 3: Embed the file** (`AcDream.Rynth.EngineUi.csproj`, in the `PackageReference` item group's neighbour or a new `ItemGroup`)

```xml
  <!-- RynthNav's places database, which RynthCore's installer puts in the RynthCore
       folder; their atlas reads dungeon names from it. NavAtlasInstall writes it there. -->
  <ItemGroup>
    <EmbeddedResource Include="$(RynthEngine)..\..\tools\navdata\NavData\locations.json"
                      LogicalName="AcDream.Rynth.EngineUi.locations.json" />
  </ItemGroup>
```

- [ ] **Step 4: Write `NavAtlasInstall`** (`src/AcDream.Rynth.EngineUi/NavAtlasInstall.cs`)

```csharp
using System;
using System.IO;

namespace AcDream.Rynth.EngineUi;

/// <summary>
/// RynthCore's installer puts RynthNav's places database in the RynthCore folder
/// (NavData/locations.json); their RynthNavAtlas reads dungeon names from there when no
/// RynthNav plugin reports another path. OpenAC has no installer, so the dashboard writes
/// the copy embedded here. On Windows an existing file is left alone (a real RynthCore
/// install keeps its own); elsewhere the folder is ours and a different file is replaced.
/// </summary>
internal static class NavAtlasInstall
{
    internal const string ResourceName = "AcDream.Rynth.EngineUi.locations.json";

    internal static string PathUnder(string coreDir) => Path.Combine(coreDir, "NavData", "locations.json");

    /// <summary>True when the file is in place afterwards. IO errors are logged, never thrown.</summary>
    internal static bool Ensure(string coreDir, bool isWindows, Action<string> log)
    {
        string target = PathUnder(coreDir);
        try
        {
            using Stream? source = typeof(NavAtlasInstall).Assembly.GetManifestResourceStream(ResourceName);
            if (source is null)
            {
                log("[RynthAi] the embedded Atlas is missing; dungeons show as LB numbers.");
                return File.Exists(target);
            }

            var existing = new FileInfo(target);
            if (existing.Exists && (isWindows || existing.Length == source.Length))
                return true;

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string temp = target + ".tmp";
            using (FileStream output = File.Create(temp))
                source.CopyTo(output);
            File.Move(temp, target, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log($"[RynthAi] the Atlas could not be installed to {target}: {ex.Message}; dungeons show as LB numbers.");
            return false;
        }
    }
}
```

- [ ] **Step 5: Call it from `Start()`** (`RynthAiDashboard.cs`; add `using System.Threading.Tasks;` and `using RynthCore.Install;` if `RynthInstallPaths` is in that namespace — check `external/RynthCore/src/Shared/RynthInstallPaths.cs`)

```csharp
        if (!_started)
        {
            EntryPoint.InitStartedUtc = DateTime.UtcNow;
            _started = true;
            NavAtlasInstall.Ensure(RynthInstallPaths.CoreDir, OperatingSystem.IsWindows(), Log);
            // Our UiBackgroundWriter runs work inline, and parsing the 1.9 MB Atlas would stall
            // the first indoor frame; their loader is written for a background thread, and later
            // calls see the path already requested.
            Task.Run(RynthNavAtlas.EnsureLoadedForNames);
        }
```

- [ ] **Step 6: Run to verify they pass**

Run: `dotnet test tests/AcDream.Rynth.Host.Tests`
Expected: all pass (Task 2 total + 4 new).
Run: `dotnet build AcDream.slnx -c Release`
Expected: build succeeded, no new warnings.

- [ ] **Step 7: Commit**

```bash
git add src/AcDream.Rynth.EngineUi tests/AcDream.Rynth.Host.Tests/RynthDungeonMapTests.cs
git commit -m "feat(rynth): install RynthCore's Atlas so the Dungeon Map names dungeons"
```

---

### Task 4: Live check (user)

**Goal:** On the local ACE server, the Dungeon Map shows the dungeon you are in and follows you.

**Files:** none (results go in the memory note `rynth-status.md` and this plan's ledger).

**Acceptance Criteria:**
- [ ] Wait until the user says they are available; launch only on the local ACE test server (memory: live-testing-rules), never Dreamweave.
- [ ] Indoors, dashboard Map opens the Dungeon Map centred on first open; no `threw` lines in the log.
- [ ] Walls and floors line up with the dungeon. (No name readout: RynthCore's map has none — only their Radar panel draws it; see the spec's correction.)
- [ ] Monsters, doors and corpses appear and move; floors switch going up and down stairs.
- [ ] The RynthAi nav route draws; adding and moving a point from the map changes the route.
- [ ] Walking outside hides the map; going back in shows it again.
- [ ] Map settings (toolbar toggles, marker sizes) survive a relog; text and markers look right on Retina.

**Verify:** user confirmation in chat for each item above.

**Steps:**

- [ ] **Step 1:** Ask the user whether they are available for a live check; do nothing else until they say yes.
- [ ] **Step 2:** Walk the list above with the user, record PASS/FAIL per item in the ledger, and fix failures with systematic-debugging.
