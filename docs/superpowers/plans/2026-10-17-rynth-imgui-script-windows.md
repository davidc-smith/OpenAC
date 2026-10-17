# Rynth Script Windows on an ImGui Overlay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show RynthSuite plugin windows (RynthOracle) inside the OpenAC client with real Dear ImGui, compiled from RynthCore's own script-window code.

**Architecture:**
- An ImGui overlay lives in AcDream.App (new files) and draws through OpenAC's internal GPU layer as the last backbuffer pass. It plugs into the existing, unused dev-tools frame hook.
- Extensions reach it through `IClientOverlay` in `AcDream.ClientExtensions`.
- A new wrapper project compiles four RynthCore files unchanged: the parser, the registry, the replay and the window face. Small stand-ins replace the engine types those files use.
- The Rynth host gains RynthCore's UI table slots and a dispatch scope that tells the registry which plugin is calling.

**Tech Stack:** .NET 10 (`~/.dotnet/dotnet`), ImGui.NET 1.91.6.1 (cimgui; macOS universal dylib), Silk.NET input, OpenAC Vulkan GPU layer, xunit v3, git submodules.

**Spec:** `docs/superpowers/specs/2026-10-17-rynth-imgui-script-windows-design.md`. It builds on `docs/superpowers/specs/2026-10-17-rynth-host-poc-design.md`.

## Global Constraints

**Repository and branch**
- Repo `~/code/OpenAC/OpenAC-Rynth`, branch `feat/rynth-imgui`, which builds on `feat/rynth-host-poc`. Do not push.
- Run every dotnet command as `~/.dotnet/dotnet`.
- Never edit anything under `external/`. When their code must differ, exclude the file in our wrapper and compile a replacement.
- Solutions are `.slnx` only (`AcDream.slnx`).

**Upstream files**
- Upstream files (files that exist on `upstream/main`) change only where a task says so. The target is about 15 changed lines in total, plus the package line and regenerated lock files.

**Build settings**
- Our own new projects keep the repo `Directory.Build.props` settings: warnings as errors, analyzers on, nullable, implicit usings.
- Exception: `AcDream.Rynth.ScriptWindows` and `AcDream.Rynth.Plugin.RynthOracle` compile RynthCore and RynthSuite source, so they set `TreatWarningsAsErrors=false` and `RunAnalyzers=false`, as the earlier wrapper projects do.

**Versions and code style**
- ImGui.NET is pinned to exactly `1.91.6.1`, RynthCore's version. Their files use 1.91.x-only native names.
- Static fields use `_camelCase`.
- No exception may leave an `[UnmanagedCallersOnly]` method.

**Process**
- Commit messages end with `Claude-Session: https://claude.ai/code/session_013qHSfUa7LKyjkciWdcioQr`.
- Never launch a client against a server without the user. The offline probe run in Task 8 needs no server and is allowed.

**ImGui facts**
- A new ImGui window draws nothing on its first frame while it auto-sizes. Tests run at least two frames.
- ImGui state is process-global. Every test that touches ImGui or the registry shares one xunit collection and resets that state.

**User decisions (already made):**
- "Real ImGui (Recommended)": draw script windows with real ImGui, not OpenAC's retained UI.
- "Yes lets try it" to approach A: the overlay is a service inside AcDream.App, offered via `IClientOverlay`, and RynthCore's files are compiled unchanged with stand-ins.
- RynthOracle is the test plugin. Icons are placeholders, positions aren't saved, there's no window bar, and RynthAi is out of scope.
- The user does the visual check when available. The agent may run the offline client itself.

---

## File Structure

```
Directory.Packages.props                                   +1 PackageVersion ImGui.NET (upstream file)
src/AcDream.Rynth.ScriptWindows/                           NEW wrapper: their 4 files + our stand-ins
  AcDream.Rynth.ScriptWindows.csproj
  StandIns/EngineStandIns.cs                               RynthLog, UiBackgroundWriter, LoadedPlugin, EngineSettings,
                                                           LoginLifecycleHooks, UiInfoNative
  StandIns/ImGuiStandIns.cs                                EngineFrameController, ImGuiTextFocus, ScriptIcons, UiFont,
                                                           ImGuiFonts, PanelChrome, PanelSpec, IImGuiPanel
  StandIns/ImGuiPanelHost.cs                               window host: register/open/close/draw
src/AcDream.Rynth.Plugin.RynthOracle/                      NEW wrapper for their RynthOracle
  AcDream.Rynth.Plugin.RynthOracle.csproj
  FellowshipTrackerStandIn.cs                              replaces RynthAi's Windows-memory FellowshipTracker
src/AcDream.Rynth.Host/
  RynthDispatch.cs                                         NEW: which plugin is inside a call
  RynthApiTable.cs                                         + UI, InvokeChatParser, GetSelectedItemId slots
  RynthPlugin.cs                                           + dispatch scope, OnChatBarEnter, Owner
  RynthHost.cs                                             + scriptWindows flag, DropOwner on dispose
  RynthEventBridge.cs                                      + login flag for the registry
  IRynthGameView.cs                                        + SubmitChatCommand, SelectedObjectId
  RuntimeRynthGameView.cs                                  + the two new members
  RynthClientExtension.cs                                  + overlay callback, /ro forwarding, RynthOracle
src/AcDream.ClientExtensions/
  IClientOverlay.cs                                        NEW contract
  ClientExtensionContext.cs                                + Overlay, SubmitChatCommand (optional)
src/AcDream.App/Extensions/Overlay/                        NEW
  ImGuiOverlay.cs                                          context, frame lifecycle, callbacks, capture flags
  ImGuiInputQueue.cs                                       Silk input -> queued ImGui IO events
  ImGuiKeyMap.cs                                           Silk Key -> ImGuiKey
  ImGuiScissor.cs                                          clip rect -> OpenAC scissor (pure)
  IImGuiDrawRenderer.cs                                    renderer seam (GPU or fake)
  ImGuiVulkanRenderer.cs                                   font atlas + draw through the GPU layer
  OverlayInputGate.cs                                      static flags the retained UI reads
  Shaders/imgui_overlay.vert, .frag (+ .spv, embedded)
src/AcDream.App/Extensions/ClientExtensionHost.cs          + overlay / submit parameters (our file)
src/AcDream.App/Rendering/GameWindow.cs                    +3 lines (upstream)
src/AcDream.App/Composition/FrameRootComposition.cs        +4 lines (upstream)
src/AcDream.App/Composition/FrameRootDependencies (record) +1 member (upstream; find with grep)
src/AcDream.App/Input/InputCaptureSources.cs               +3 lines (upstream)
src/AcDream.App/UI/RetainedUiInputBinding.cs               +2 lines (upstream)
src/AcDream.App/AcDream.App.csproj                         +1 PackageReference, +1 EmbeddedResource group (upstream)
tests/AcDream.Rynth.Host.Tests/                            + stand-in, table and pipeline tests
tests/AcDream.App.Tests/Extensions/                        + overlay and extension tests
```

---

### Task 1: ImGui.NET package and the script-windows wrapper with stand-ins

**Goal:** `AcDream.Rynth.ScriptWindows` compiles RynthCore's `DisplayListParser.cs`, `ScriptWindowRegistry.cs`, `ScriptWindowReplay.cs` and `ScriptWindowFace.cs` unchanged against our stand-ins. A headless ImGui test shows the window host draws with balanced `Begin`/`End`.

**Files:**
- Modify: `Directory.Packages.props`: add `<PackageVersion Include="ImGui.NET" Version="1.91.6.1" />`
- Create: `src/AcDream.Rynth.ScriptWindows/AcDream.Rynth.ScriptWindows.csproj`, `StandIns/EngineStandIns.cs`, `StandIns/ImGuiStandIns.cs`, `StandIns/ImGuiPanelHost.cs`
- Modify: `AcDream.slnx`, `tests/AcDream.Rynth.Host.Tests/AcDream.Rynth.Host.Tests.csproj` (reference the new project)
- Test: `tests/AcDream.Rynth.Host.Tests/HeadlessImGui.cs`, `tests/AcDream.Rynth.Host.Tests/ImGuiPanelHostTests.cs`

**Acceptance Criteria:**
- [ ] `dotnet build src/AcDream.Rynth.ScriptWindows` succeeds with all four RynthCore files linked from `external/` and none of them edited.
- [ ] `git status --porcelain external/` is empty.
- [ ] `ImGuiPanelHostTests` pass. An open panel's `Draw` runs inside a window, a panel whose `Draw` throws still leaves ImGui balanced (the next frame works), and `Close` calls `OnHidden`.
- [ ] `ScriptWindowRegistry.GetInfo` returns 480 bytes with `Flags` bit0 set when `EngineSettings.EnableImGuiBackend` is true, and returns 0 when it is false.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo` → all tests pass

**Steps:**

- [ ] **Step 1: Add the package version.** In `Directory.Packages.props`, inside the existing `<ItemGroup>` of `PackageVersion` items, add:

```xml
    <PackageVersion Include="ImGui.NET" Version="1.91.6.1" />
```

- [ ] **Step 2: Create the wrapper project.** `src/AcDream.Rynth.ScriptWindows/AcDream.Rynth.ScriptWindows.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <!-- RynthCore's script-window code (parser, registry, ImGui replay, window face),
       compiled unchanged from the submodule, plus our small stand-ins for the
       engine types those files use (StandIns/). Their code, their warnings:
       warnings-as-errors and analyzers are off for this project only. -->
  <PropertyGroup>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <ImplicitUsings>disable</ImplicitUsings>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <RunAnalyzers>false</RunAnalyzers>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
    <IsPackable>false</IsPackable>
    <RynthEngine>$(MSBuildThisFileDirectory)..\..\external\RynthCore\src\RynthCore.Engine\</RynthEngine>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="$(RynthEngine)UI\ScriptWindows\DisplayListParser.cs" Link="RynthCore\DisplayListParser.cs" />
    <Compile Include="$(RynthEngine)UI\ScriptWindows\ScriptWindowRegistry.cs" Link="RynthCore\ScriptWindowRegistry.cs" />
    <Compile Include="$(RynthEngine)ImGui\ScriptWindowReplay.cs" Link="RynthCore\ScriptWindowReplay.cs" />
    <Compile Include="$(RynthEngine)ImGui\Panels\ScriptWindowFace.cs" Link="RynthCore\ScriptWindowFace.cs" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="ImGui.NET" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="AcDream.Rynth.Host" />
    <InternalsVisibleTo Include="AcDream.Rynth.Host.Tests" />
    <InternalsVisibleTo Include="AcDream.App.Tests" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Engine stand-ins.** `src/AcDream.Rynth.ScriptWindows/StandIns/EngineStandIns.cs`. Copy `UiInfoNative`'s field layout exactly from `external/RynthCore/src/RynthCore.Engine/Plugins/PluginContract.cs` (search `struct UiInfoNative`). It must be 480 bytes.

```csharp
// Stand-ins for the RynthCore engine types the compiled script-window files use.
// Each keeps only the members those files touch; see the plan's Task 1.
using System;
using System.Runtime.InteropServices;

namespace RynthCore.Engine
{
    /// <summary>RynthCore's log; the host points <see cref="Sink"/> at its own log.</summary>
    internal static class RynthLog
    {
        internal static Action<string>? Sink { get; set; }

        internal static void UI(string msg) => Sink?.Invoke("[ScriptWindows] " + msg);
    }
}

namespace RynthCore.Engine.UI
{
    /// <summary>RynthCore queues this work to the thread pool; here it runs inline.</summary>
    internal static class UiBackgroundWriter
    {
        public static void Enqueue(string label, Action work)
        {
            try { work(); }
            catch { }
        }
    }
}

namespace RynthCore.Engine.Plugins
{
    /// <summary>A script-window owner: one per hosted plugin, compared by reference.</summary>
    internal sealed class LoadedPlugin
    {
        public string DisplayName { get; set; } = "";
        public bool Failed { get; set; }
    }

    internal static class EngineSettings
    {
        /// <summary>True while an ImGui overlay is drawing script windows.</summary>
        public static bool EnableImGuiBackend { get; set; }
    }

    /// <summary>Layout copied from RynthCore.Engine/Plugins/PluginContract.cs (480 bytes).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal unsafe struct UiInfoNative
    {
        public uint Size;
        public uint Flags;
        public ushort MaxFormatVersion;
        public ushort OpLevel;
        public uint MaxOpsPerWindow;
        public uint MaxBytesPerWindow;
        public uint MaxWindowsPerOwner;
        public uint MaxBytesPerSubmit;
        public float DisplayWidth;
        public float DisplayHeight;
        public float UiScale;
        public float TextLineHeight;
        public float FrameHeight;
        public float ItemSpacingX;
        public float ItemSpacingY;
        public float FramePaddingX;
        public float FramePaddingY;
        public fixed float AsciiAdvance[95];
        public uint FrameCounter;
        public fixed uint Reserved[8];
    }
}

namespace RynthCore.Engine.Compatibility
{
    internal static class LoginLifecycleHooks
    {
        /// <summary>Set by the host's event bridge on login and logout.</summary>
        public static bool HasObservedLoginComplete { get; set; }
    }
}
```

If the engine's `UiInfoNative` differs from this listing, use the engine's fields and order. The `Size` test in Step 7 checks the total size.

- [ ] **Step 4: ImGui-side stand-ins.** `src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiStandIns.cs`:

```csharp
// Stand-ins for the RynthCore ImGui-backend types the compiled files use.
using System;
using System.Numerics;
using ImGuiNET;
using RynthCore.Engine.UI.ScriptWindows;

namespace RynthCore.Engine.ImGuiBackend
{
    internal static class EngineFrameController
    {
        /// <summary>RynthCore's UI scale. The overlay already handles the display scale, so 1.</summary>
        internal static float FontScale => 1f;
    }

    internal static class ImGuiTextFocus
    {
        public static void NoteActive(string id)
        {
        }
    }

    internal static class ScriptIcons
    {
        /// <summary>No AC icons yet: the replay draws its placeholder square.</summary>
        public static bool TryGet(ScriptIconKind kind, uint id, out IntPtr texture)
        {
            texture = IntPtr.Zero;
            return false;
        }
    }

    internal enum UiFont
    {
        Ui11,
    }

    internal static class ImGuiFonts
    {
        /// <summary>RynthCore's sized UI fonts; here, the overlay's current font.</summary>
        public static ImFontPtr Get(UiFont font) => ImGui.GetFont();
    }

    internal enum PanelChrome
    {
        Standard,
        None,
    }

    /// <summary>The members of RynthCore's PanelSpec that the registry sets.</summary>
    internal sealed record PanelSpec(
        Vector2 DefaultSize,
        Vector2 MinSize,
        PanelChrome Chrome = PanelChrome.Standard,
        uint? Background = null,
        bool NoPopOut = false,
        ImGuiWindowFlags BodyFlags = ImGuiWindowFlags.None,
        Vector2? DefaultPos = null);

    internal interface IImGuiPanel
    {
        void OnShown()
        {
        }

        void OnHidden()
        {
        }

        void Draw();

        Vector2? MinSize => null;

        bool ClickThrough => false;

        int PopOutIdleHz => 10;

        bool HiddenWhileDocked => false;
    }
}
```

- [ ] **Step 5: The window host stand-in.** `src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiPanelHost.cs`. The registry calls every method with the window's panel key (`"script:" + key`).

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace RynthCore.Engine.ImGuiBackend;

/// <summary>
/// Our stand-in for RynthCore's ImGuiPanelHost: keeps each script window's
/// open state and draws the open ones as ImGui windows. Open and Close apply
/// at once (RynthCore queues them to the next frame), so nothing is ever
/// "transitioning". Every Begin has its End, even when a body throws.
/// </summary>
internal static class ImGuiPanelHost
{
    private sealed class Entry
    {
        public string Title = "";
        public PanelSpec Spec = null!;
        public Func<IImGuiPanel> Factory = null!;
        public IImGuiPanel? Instance;
        public bool Open;
        public Vector2? RequestedSize;
        public Vector2? RequestedPos;
    }

    private static readonly object _gate = new();
    private static readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Where a body's exception is reported.</summary>
    internal static Action<string>? Log { get; set; }

    internal static int OpenCount
    {
        get { lock (_gate) return _entries.Values.Count(e => e.Open); }
    }

    public static void RegisterDynamic(string key, string displayTitle, PanelSpec spec, Func<IImGuiPanel> factory)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out Entry? entry))
                _entries[key] = entry = new Entry();
            entry.Title = displayTitle;
            entry.Spec = spec;
            entry.Factory = factory;
        }
    }

    public static void SetDisplayTitle(string key, string displayTitle)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out Entry? entry))
                entry.Title = displayTitle;
        }
    }

    public static void Unregister(string key)
    {
        Entry? removed;
        lock (_gate)
        {
            if (!_entries.Remove(key, out removed))
                return;
        }

        if (removed.Open)
            removed.Instance?.OnHidden();
    }

    public static bool IsOpen(string key)
    {
        lock (_gate) return _entries.TryGetValue(key, out Entry? e) && e.Open;
    }

    public static void Open(string key)
    {
        IImGuiPanel? shown = null;
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out Entry? entry) || entry.Open)
                return;
            entry.Open = true;
            shown = entry.Instance ??= entry.Factory();
        }

        shown.OnShown();
    }

    public static void Close(string key)
    {
        IImGuiPanel? hidden = null;
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out Entry? entry) || !entry.Open)
                return;
            entry.Open = false;
            hidden = entry.Instance;
        }

        hidden?.OnHidden();
    }

    public static void RequestSize(string key, Vector2 logicalSize)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out Entry? entry))
                entry.RequestedSize = logicalSize;
        }
    }

    public static void RequestPos(string key, Vector2 logicalPos)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out Entry? entry))
                entry.RequestedPos = logicalPos;
        }
    }

    public static bool IsTransitioning(string key) => false;

    /// <summary>Forgets every window (tests, and host shutdown).</summary>
    internal static void Reset()
    {
        lock (_gate) _entries.Clear();
    }

    /// <summary>Draws every open window. Call between ImGui.NewFrame and ImGui.Render.</summary>
    internal static void DrawAll()
    {
        KeyValuePair<string, Entry>[] open;
        lock (_gate) open = _entries.Where(pair => pair.Value.Open).ToArray();

        foreach ((string key, Entry entry) in open)
        {
            float s = EngineFrameController.FontScale;
            if (entry.RequestedSize is { } size)
            {
                ImGui.SetNextWindowSize(size * s, ImGuiCond.Always);
                entry.RequestedSize = null;
            }
            else
            {
                ImGui.SetNextWindowSize(entry.Spec.DefaultSize * s, ImGuiCond.FirstUseEver);
            }

            if (entry.RequestedPos is { } pos)
            {
                ImGui.SetNextWindowPos(pos * s, ImGuiCond.Always);
                entry.RequestedPos = null;
            }
            else if (entry.Spec.DefaultPos is { } defaultPos)
            {
                ImGui.SetNextWindowPos(defaultPos * s, ImGuiCond.FirstUseEver);
            }

            ImGui.SetNextWindowSizeConstraints(entry.Spec.MinSize * s, new Vector2(float.MaxValue, float.MaxValue));

            ImGuiWindowFlags flags = entry.Spec.BodyFlags;
            if (entry.Spec.Chrome == PanelChrome.None)
                flags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize;
            if (entry.Spec.Background == 0u)
                flags |= ImGuiWindowFlags.NoBackground;

            string label = entry.Title + "###" + key;
            bool keepOpen = true;
            bool visible = entry.Spec.Chrome == PanelChrome.None
                ? ImGui.Begin(label, flags)
                : ImGui.Begin(label, ref keepOpen, flags);
            try
            {
                if (visible)
                    entry.Instance?.Draw();
            }
            catch (Exception error)
            {
                Log?.Invoke($"[ScriptWindows] {entry.Title}: {error.GetType().Name}: {error.Message}");
            }
            finally
            {
                ImGui.End();
            }

            if (!keepOpen)
                Close(key);
        }
    }
}
```

- [ ] **Step 6: Reference the wrapper from the tests and the solution.** In `tests/AcDream.Rynth.Host.Tests/AcDream.Rynth.Host.Tests.csproj`, add to the `ProjectReference` group:

```xml
    <ProjectReference Include="..\..\src\AcDream.Rynth.ScriptWindows\AcDream.Rynth.ScriptWindows.csproj" />
```

Then:

```bash
cd ~/code/OpenAC/OpenAC-Rynth
~/.dotnet/dotnet sln AcDream.slnx add src/AcDream.Rynth.ScriptWindows/AcDream.Rynth.ScriptWindows.csproj
~/.dotnet/dotnet build src/AcDream.Rynth.ScriptWindows -nologo
```

Expected: `Build succeeded.` with 0 errors. If an error names a type or member missing from the stand-ins, find its real definition in `external/RynthCore/src/RynthCore.Engine/`. Add the smallest stand-in that satisfies the usage, in the same namespace and file pattern, and list it in the report. If a stand-in would need real logic beyond a field, a constant or a no-op, stop and report BLOCKED.

- [ ] **Step 7: Write the tests.** `tests/AcDream.Rynth.Host.Tests/HeadlessImGui.cs`, the shared headless ImGui setup:

```csharp
using System.Numerics;
using ImGuiNET;

namespace AcDream.Rynth.Host.Tests;

/// <summary>A headless ImGui context (fonts built, no renderer) for one test.</summary>
internal sealed class HeadlessImGui : IDisposable
{
    private readonly IntPtr _context;

    public HeadlessImGui()
    {
        _context = ImGui.CreateContext();
        ImGui.SetCurrentContext(_context);
        ImGuiIOPtr io = ImGui.GetIO();
        io.DisplaySize = new Vector2(1280, 800);
        io.Fonts.GetTexDataAsRGBA32(out IntPtr _, out int _, out int _, out int _);
        io.Fonts.SetTexID(1);
    }

    /// <summary>Runs one frame and returns its draw data's vertex count.</summary>
    public int Frame(Action draw)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        io.DeltaTime = 1f / 60f;
        ImGui.NewFrame();
        draw();
        ImGui.Render();
        return ImGui.GetDrawData().TotalVtxCount;
    }

    public void Dispose() => ImGui.DestroyContext(_context);
}
```

`tests/AcDream.Rynth.Host.Tests/ImGuiPanelHostTests.cs`:

```csharp
using System.Numerics;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.Plugins;
using RynthCore.Engine.UI.ScriptWindows;

namespace AcDream.Rynth.Host.Tests;

public sealed unsafe class ImGuiPanelHostTests : IDisposable
{
    private readonly HeadlessImGui _imgui = new();

    public ImGuiPanelHostTests() => ImGuiPanelHost.Reset();

    public void Dispose()
    {
        ImGuiPanelHost.Reset();
        _imgui.Dispose();
    }

    [Fact]
    public void AnOpenPanelDrawsInsideAWindow()
    {
        var panel = new RecordingPanel();
        ImGuiPanelHost.RegisterDynamic("script:test", "Test", new PanelSpec(new Vector2(200, 100), new Vector2(50, 50)), () => panel);
        ImGuiPanelHost.Open("script:test");

        _imgui.Frame(ImGuiPanelHost.DrawAll);
        int vertices = _imgui.Frame(ImGuiPanelHost.DrawAll);

        Assert.True(panel.Shown);
        Assert.True(panel.Draws >= 1);
        Assert.True(vertices > 0);
    }

    [Fact]
    public void APanelThatThrowsLeavesImGuiBalanced()
    {
        var panel = new RecordingPanel { Throw = true };
        var log = new List<string>();
        ImGuiPanelHost.Log = log.Add;
        ImGuiPanelHost.RegisterDynamic("script:bad", "Bad", new PanelSpec(new Vector2(200, 100), new Vector2(50, 50)), () => panel);
        ImGuiPanelHost.Open("script:bad");

        _imgui.Frame(ImGuiPanelHost.DrawAll);
        _imgui.Frame(ImGuiPanelHost.DrawAll);
        int vertices = _imgui.Frame(() => { ImGui.Begin("after"); ImGui.Text("still fine"); ImGui.End(); });

        Assert.Contains(log, line => line.Contains("Bad", StringComparison.Ordinal));
        Assert.True(vertices >= 0);
    }

    [Fact]
    public void CloseCallsOnHidden()
    {
        var panel = new RecordingPanel();
        ImGuiPanelHost.RegisterDynamic("script:x", "X", new PanelSpec(new Vector2(200, 100), new Vector2(50, 50)), () => panel);
        ImGuiPanelHost.Open("script:x");
        ImGuiPanelHost.Close("script:x");

        Assert.True(panel.Hidden);
        Assert.False(ImGuiPanelHost.IsOpen("script:x"));
    }

    [Fact]
    public void GetInfoReportsImGuiOnlyWhenEnabled()
    {
        var info = new UiInfoNative { Size = (uint)sizeof(UiInfoNative) };
        EngineSettings.EnableImGuiBackend = false;
        Assert.Equal(0, ScriptWindowRegistry.GetInfo(&info));

        EngineSettings.EnableImGuiBackend = true;
        info = new UiInfoNative { Size = (uint)sizeof(UiInfoNative) };
        int written = ScriptWindowRegistry.GetInfo(&info);
        EngineSettings.EnableImGuiBackend = false;

        Assert.Equal(480, sizeof(UiInfoNative));
        Assert.Equal(480, written);
        Assert.Equal(1u, info.Flags & 1u);
        Assert.Equal((ushort)1, info.MaxFormatVersion);
    }

    private sealed class RecordingPanel : IImGuiPanel
    {
        public bool Throw { get; init; }
        public bool Shown { get; private set; }
        public bool Hidden { get; private set; }
        public int Draws { get; private set; }

        public void OnShown() => Shown = true;

        public void OnHidden() => Hidden = true;

        public void Draw()
        {
            Draws++;
            ImGui.Text("panel body");
            if (Throw)
                throw new InvalidOperationException("body failed");
        }
    }
}
```

Add `using ImGuiNET;` at the top of the test file. `ImGuiNET.ImGui` is used directly.

- [ ] **Step 8: Run the tests.** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo`. Expected: all pass, including the 17 existing tests. If `GetInfo`'s signature differs (for example a `ref` parameter), adapt the test call to the real signature in `ScriptWindowRegistry.cs`.

- [ ] **Step 9: Commit.**

```bash
git add Directory.Packages.props AcDream.slnx src/AcDream.Rynth.ScriptWindows tests/AcDream.Rynth.Host.Tests
git commit -m "feat(rynth): compile RynthCore's script-window code against small engine stand-ins

Claude-Session: https://claude.ai/code/session_013qHSfUa7LKyjkciWdcioQr"
```

---

### Task 2: RynthOracle wrapper and the host's dispatch scope, chat-bar and chat-parser slots

**Goal:** RynthOracle's unmodified source builds and loads in the host. `/ro` reaches it through `RynthPluginOnChatBarEnter`, and without script windows it answers with its "can't show plugin windows" line in chat.

**Files:**
- Create: `src/AcDream.Rynth.Plugin.RynthOracle/AcDream.Rynth.Plugin.RynthOracle.csproj`, `src/AcDream.Rynth.Plugin.RynthOracle/FellowshipTrackerStandIn.cs`
- Create: `src/AcDream.Rynth.Host/RynthDispatch.cs`
- Modify: `src/AcDream.Rynth.Host/RynthPlugin.cs`, `RynthApiTable.cs`, `IRynthGameView.cs`, `RuntimeRynthGameView.cs`, `AcDream.Rynth.Host.csproj`
- Modify: `tests/AcDream.Rynth.Host.Tests/FakeGameView.cs`
- Modify: `AcDream.slnx`
- Test: `tests/AcDream.Rynth.Host.Tests/RynthOracleTests.cs`

**Acceptance Criteria:**
- [ ] `dotnet build src/AcDream.Rynth.Plugin.RynthOracle` succeeds. RynthAi's `FellowshipTracker.cs` is not compiled; the stand-in replaces it.
- [ ] A host with RynthOracle loads it (`IsActive`).
- [ ] `OnChatBarEnter("/ro")` returns true (eaten). After a tick, the fake view's chat holds `[RynthOracle] This RynthCore engine can't show plugin windows`.
- [ ] `/ro about` puts the "Based on Oracle of Dereth" line in chat.
- [ ] `InvokeChatParser` reaches `IRynthGameView.SubmitChatCommand`, and `GetSelectedItemId` returns `IRynthGameView.SelectedObjectId`.
- [ ] Every export call except `OnChatBarEnter` runs inside `RynthDispatch` with that plugin as `Current`, and `Current` is null outside calls.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo` → all pass

**Steps:**

- [ ] **Step 1: The RynthOracle wrapper.** First list the plugin's files: `ls external/RynthSuite/Plugins/RynthCore.Plugin.RynthOracle external/RynthSuite/Plugins/RynthCore.Plugin.RynthOracle/Resources`. Then create `src/AcDream.Rynth.Plugin.RynthOracle/AcDream.Rynth.Plugin.RynthOracle.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <!-- RynthSuite's RynthOracle, compiled unchanged from the submodule as plain net10.0.
       Its csproj also compiles RynthAi's FellowshipTracker.cs, which reads the
       retail client's memory through kernel32; FellowshipTrackerStandIn.cs replaces it.
       The embedded data files keep the plugin's own logical names. -->
  <PropertyGroup>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <ImplicitUsings>disable</ImplicitUsings>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <RunAnalyzers>false</RunAnalyzers>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
    <IsPackable>false</IsPackable>
    <RynthSuitePlugins>$(MSBuildThisFileDirectory)..\..\external\RynthSuite\Plugins\</RynthSuitePlugins>
    <OraclePlugin>$(RynthSuitePlugins)RynthCore.Plugin.RynthOracle\</OraclePlugin>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="$(OraclePlugin)**\*.cs" Exclude="$(OraclePlugin)obj\**;$(OraclePlugin)bin\**" />
    <Compile Include="FellowshipTrackerStandIn.cs" />
  </ItemGroup>

  <ItemGroup>
    <EmbeddedResource Include="$(OraclePlugin)Resources\*.csv" LogicalName="RynthOracle.%(Filename)%(Extension)" />
    <EmbeddedResource Include="$(RynthSuitePlugins)RynthCore.Plugin.RynthAi\Combat\SpellData.txt" LogicalName="RynthOracle.SpellData.txt" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\AcDream.Rynth.Sdk\AcDream.Rynth.Sdk.csproj" />
  </ItemGroup>
</Project>
```

Compare the `EmbeddedResource` list against the original csproj (`external/RynthSuite/Plugins/RynthCore.Plugin.RynthOracle/RynthCore.Plugin.RynthOracle.csproj`). If it lists files the glob misses, or uses different logical names, mirror the original exactly.

- [ ] **Step 2: The FellowshipTracker stand-in.** Read the public members of `external/RynthSuite/Plugins/RynthCore.Plugin.RynthAi/Combat/FellowshipTracker.cs` (`grep -n "public " ...`). Create `src/AcDream.Rynth.Plugin.RynthOracle/FellowshipTrackerStandIn.cs` with the same namespace, class name and public members, each returning "not in a fellowship". Start from this listing. Add any other public member the original has, with the same signature and a neutral value; the build tells you if one is missing.

```csharp
using System;
using System.Collections.Generic;

namespace RynthCore.Plugin.RynthAi;

/// <summary>
/// Stand-in for RynthAi's FellowshipTracker, which reads the retail client's
/// memory. OpenAC has no such memory, so this reports "not in a fellowship".
/// </summary>
public class FellowshipTracker : IDisposable
{
    public FellowshipTracker() { }

    public void Dispose() { }

    public bool IsInFellowship => false;

    public int MemberCount => 0;

    public string FellowshipName => string.Empty;

    public int LeaderId => 0;

    public bool IsLeader => false;

    public bool IsOpen => false;

    public bool IsLocked => false;

    public bool ShareXP => false;

    public bool IsMember(string name) => false;

    public bool IsMember(int characterId) => false;

    public IEnumerable<string> GetMemberNames() => Array.Empty<string>();

    public string GetMemberName(int index) => string.Empty;

    public int GetMemberId(int index) => 0;
}
```

Build: `~/.dotnet/dotnet sln AcDream.slnx add src/AcDream.Rynth.Plugin.RynthOracle/AcDream.Rynth.Plugin.RynthOracle.csproj && ~/.dotnet/dotnet build src/AcDream.Rynth.Plugin.RynthOracle -nologo`. Expected: 0 errors.

- [ ] **Step 3: Reference Oracle and ScriptWindows from the host.** In `src/AcDream.Rynth.Host/AcDream.Rynth.Host.csproj`, add:

```xml
    <ProjectReference Include="..\AcDream.Rynth.Plugin.RynthOracle\AcDream.Rynth.Plugin.RynthOracle.csproj" />
    <ProjectReference Include="..\AcDream.Rynth.ScriptWindows\AcDream.Rynth.ScriptWindows.csproj" />
```

- [ ] **Step 4: The dispatch scope.** `src/AcDream.Rynth.Host/RynthDispatch.cs`:

```csharp
namespace AcDream.Rynth.Host;

/// <summary>
/// Which plugin is inside a call from the host, as RynthCore's
/// PluginManager.CurrentDispatch does: the script-window functions use it to
/// know whose windows a UiSubmit or UiPollEvents is for. Thread-bound.
/// </summary>
public static class RynthDispatch
{
    [ThreadStatic]
    private static RynthPlugin? _current;

    /// <summary>The plugin in the innermost open call on this thread, or null.</summary>
    public static RynthPlugin? Current => _current;

    /// <summary>Marks <paramref name="plugin"/> as current until the result is disposed.</summary>
    public static Scope Enter(RynthPlugin plugin)
    {
        RynthPlugin? outer = _current;
        _current = plugin;
        return new Scope(outer);
    }

    public readonly struct Scope : IDisposable
    {
        private readonly RynthPlugin? _outer;

        internal Scope(RynthPlugin? outer) => _outer = outer;

        public void Dispose() => _current = _outer;
    }
}
```

- [ ] **Step 5: Use the scope in `RynthPlugin`, add `Owner` and `OnChatBarEnter`.** In `src/AcDream.Rynth.Host/RynthPlugin.cs`:

1. Add the owner record the registry keys windows by, after the `Name` property:

```csharp
    /// <summary>This plugin's identity in RynthCore's script-window registry.</summary>
    internal RynthCore.Engine.Plugins.LoadedPlugin Owner { get; } = new();
```

   In the constructor, after `Name = name;`, add `Owner.DisplayName = name;`.

2. Wrap every export call except `OnChatBarEnter` in the scope, matching RynthCore, which dispatches chat-bar text outside it. That is `Initialize`, `CallVoid`, `CallObject`, `OnUpdateHealth`, `OnChatWindowText`, `OnKillNotification`, `CallStringExport` and `Shutdown` (through `CallVoid`). Add `using RynthDispatch.Scope scope = RynthDispatch.Enter(this);` as the first line inside each method's call block, just before the function-pointer call. For example, `CallVoid` becomes:

```csharp
    private void CallVoid(string entryPoint)
    {
        if (IsActive && _exports.TryGet(entryPoint, out IntPtr export))
        {
            using RynthDispatch.Scope scope = RynthDispatch.Enter(this);
            ((delegate* unmanaged[Cdecl]<void>)export)();
        }
    }
```

   and `Initialize` becomes:

```csharp
    public int Initialize(RynthCoreApiNative* api)
    {
        if (!_exports.TryGet("RynthPluginInit", out IntPtr init))
            return -1;
        int result;
        using (RynthDispatch.Enter(this))
            result = ((delegate* unmanaged[Cdecl]<RynthCoreApiNative*, int>)init)(api);
        IsActive = result == 0;
        return result;
    }
```

3. Add the chat-bar export:

```csharp
    /// <summary>
    /// Text typed in the chat bar, offered to the plugin before the client
    /// handles it. Returns true when the plugin ate it. Like RynthCore, this
    /// runs outside the dispatch scope.
    /// </summary>
    public bool OnChatBarEnter(string text)
    {
        if (!IsActive || !_exports.TryGet("RynthPluginOnChatBarEnter", out IntPtr export))
            return false;
        int eat = 0;
        fixed (char* textUtf16 = text)
        {
            ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)export)((IntPtr)textUtf16, (IntPtr)(&eat));
        }

        return eat != 0;
    }
```

- [ ] **Step 6: Grow the game view.** In `src/AcDream.Rynth.Host/IRynthGameView.cs`, add:

```csharp
    /// <summary>The selected object's id, 0 when nothing is selected.</summary>
    uint SelectedObjectId { get; }

    /// <summary>Runs text as if typed in the chat bar. True when the client accepted it.</summary>
    bool SubmitChatCommand(string text);
```

In `src/AcDream.Rynth.Host/RuntimeRynthGameView.cs`, change the constructor and add the members:

```csharp
    private readonly Func<string, bool>? _submitChatCommand;

    public RuntimeRynthGameView(GameRuntime runtime, Func<string, bool>? submitChatCommand = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _submitChatCommand = submitChatCommand;
    }

    public uint SelectedObjectId => _runtime.ActionOwner.Selection.SelectedObjectId ?? 0u;

    public bool SubmitChatCommand(string text) => _submitChatCommand?.Invoke(text) ?? false;
```

(Replace the existing expression-bodied constructor.) In `tests/AcDream.Rynth.Host.Tests/FakeGameView.cs`, add:

```csharp
    public uint SelectedObjectId { get; set; }
    public List<string> SubmittedCommands { get; } = new();

    public bool SubmitChatCommand(string text)
    {
        SubmittedCommands.Add(text);
        return true;
    }
```

- [ ] **Step 7: Two more table slots.** In `src/AcDream.Rynth.Host/RynthApiTable.cs`, in the constructor after the existing slot assignments, add:

```csharp
        Native->InvokeChatParserFn = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, int>)&InvokeChatParser;
        Native->GetSelectedItemIdFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint>)&GetSelectedItemId;
```

and the two functions, in the same style as the existing ones:

```csharp
    // RynthCore: runs the text as if typed (ClientHelperHooks.InvokeParser); 1 = handled.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int InvokeChatParser(IntPtr textUtf16)
    {
        RynthApiTable? table = _current;
        if (table is null || textUtf16 == IntPtr.Zero)
            return 0;
        try
        {
            string text = Marshal.PtrToStringUni(textUtf16) ?? string.Empty;
            return text.Trim().Length > 0 && table._view.SubmitChatCommand(text.Trim()) ? 1 : 0;
        }
        catch (Exception error)
        {
            table.Fail(nameof(InvokeChatParser), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static uint GetSelectedItemId()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table._view.SelectedObjectId; }
        catch (Exception error)
        {
            table.Fail(nameof(GetSelectedItemId), error);
            return 0;
        }
    }
```

Update `RynthApiTableTests.NewTable_HasApiVersionAndOnlyTheImplementedSlots` if it asserts that a now-filled slot is unavailable. Neither `HasUseObject` nor `HasCastSpell` changes.

- [ ] **Step 8: The Oracle tests.** `tests/AcDream.Rynth.Host.Tests/RynthOracleTests.cs`:

```csharp
using AcDream.Rynth.Host;
using RynthCore.PluginSdk;

namespace AcDream.Rynth.Host.Tests;

public sealed unsafe class RynthOracleTests
{
    private readonly List<string> _log = new();
    private readonly FakeGameView _view = new()
    {
        PlayerId = 0x50000000u | (uint)Random.Shared.Next(1, 0xFFFF),
    };

    private RynthHost CreateOracleHost() =>
        new(_view, _log.Add, [typeof(RynthCore.Plugin.RynthOracle.PluginExports)]);

    [Fact]
    public void OracleLoads()
    {
        using RynthHost host = CreateOracleHost();
        Assert.True(Assert.Single(host.Plugins).IsActive);
    }

    [Fact]
    public void WithoutScriptWindowsSlashRoSaysSoInChat()
    {
        using RynthHost host = CreateOracleHost();

        Assert.True(host.Plugins[0].OnChatBarEnter("/ro"));
        host.Tick();

        Assert.Contains(_view.ChatLines, line =>
            line.Text.StartsWith("[RynthOracle] This RynthCore engine can't show plugin windows", StringComparison.Ordinal));
    }

    [Fact]
    public void SlashRoAboutPrintsTheAttribution()
    {
        using RynthHost host = CreateOracleHost();

        host.Plugins[0].OnChatBarEnter("/ro about");
        host.Tick();

        Assert.Contains(_view.ChatLines, line => line.Text.Contains("Based on Oracle of Dereth", StringComparison.Ordinal));
    }

    [Fact]
    public void ChatParserAndSelectionReachTheView()
    {
        _view.SelectedObjectId = 0x80000042;
        using var table = new RynthApiTable(_view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.True(host.InvokeChatParser("/myquests"));
        Assert.Equal("/myquests", Assert.Single(_view.SubmittedCommands));
        Assert.Equal(0x80000042u, host.GetSelectedItemId());
    }

    [Fact]
    public void DispatchIsSetOnlyInsideCalls()
    {
        using RynthHost host = CreateOracleHost();
        Assert.Null(RynthDispatch.Current);
        host.Tick();
        Assert.Null(RynthDispatch.Current);
    }
}
```

Check the SDK wrapper names before running: `grep -n "public bool InvokeChatParser\|public uint GetSelectedItemId" external/RynthCore/src/RynthCore.PluginSdk/RynthCoreHost.cs`. Use the real names and return types if they differ.

- [ ] **Step 9: Run and commit.** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo`. Expected: all pass. Then:

```bash
git add AcDream.slnx src/AcDream.Rynth.Plugin.RynthOracle src/AcDream.Rynth.Host tests/AcDream.Rynth.Host.Tests
git commit -m "feat(rynth): host RynthOracle; dispatch scope, chat-bar text and chat-parser slots

Claude-Session: https://claude.ai/code/session_013qHSfUa7LKyjkciWdcioQr"
```

---

### Task 3: Script-window table slots and the headless pipeline through ImGui

**Goal:** With script windows on, `/ro` opens RynthOracle's window in RynthCore's registry. One ImGui frame of the window host draws it, and events flow back to the plugin.

**Files:**
- Modify: `src/AcDream.Rynth.Host/RynthApiTable.cs` (UI slots, `scriptWindows` flag), `RynthHost.cs` (flag, log sink, DropOwner), `RynthEventBridge.cs` (login flag)
- Test: `tests/AcDream.Rynth.Host.Tests/ScriptWindowPipelineTests.cs`

**Acceptance Criteria:**
- [ ] A table created with `scriptWindows: true` gives `HasUi == true`. One created without it gives `HasUi == false`.
- [ ] `UiSubmit` outside a dispatch scope returns -2.
- [ ] With script windows, `/ro` plus two ticks makes `ScriptWindowRegistry.IsOpen("RynthOracle/Oracle")` true.
- [ ] Two frames of `ImGuiPanelHost.DrawAll` produce more than 100 vertices.
- [ ] Closing the window through the host (as a player would) followed by ticks leaves it closed. The plugin got the Visibility event and stopped asking for it.
- [ ] Disposing the host removes the plugin's windows (`IsOpen` false, `ImGuiPanelHost.OpenCount == 0`) and turns `EngineSettings.EnableImGuiBackend` off.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo` → all pass

**Steps:**

- [ ] **Step 1: Write the failing pipeline test.** `tests/AcDream.Rynth.Host.Tests/ScriptWindowPipelineTests.cs`:

```csharp
using AcDream.Rynth.Host;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.Plugins;
using RynthCore.Engine.UI.ScriptWindows;
using RynthCore.PluginSdk;

namespace AcDream.Rynth.Host.Tests;

public sealed unsafe class ScriptWindowPipelineTests : IDisposable
{
    private const string OracleKey = "RynthOracle/Oracle";
    private readonly HeadlessImGui _imgui = new();
    private readonly List<string> _log = new();
    private readonly FakeGameView _view = new()
    {
        PlayerId = 0x50000000u | (uint)Random.Shared.Next(1, 0xFFFF),
    };

    public ScriptWindowPipelineTests()
    {
        ScriptWindowRegistry.Reset();
        ImGuiPanelHost.Reset();
    }

    public void Dispose()
    {
        ScriptWindowRegistry.Reset();
        ImGuiPanelHost.Reset();
        _imgui.Dispose();
    }

    private RynthHost CreateOracleHost() =>
        new(_view, _log.Add, [typeof(RynthCore.Plugin.RynthOracle.PluginExports)], scriptWindows: true);

    [Fact]
    public void HasUiFollowsTheScriptWindowsFlag()
    {
        using (var with = new RynthApiTable(_view, _log.Add, scriptWindows: true))
            Assert.True(new RynthCoreHost(*with.Native).HasUi);
        using (var without = new RynthApiTable(_view, _log.Add))
            Assert.False(new RynthCoreHost(*without.Native).HasUi);
    }

    [Fact]
    public void SubmitOutsideADispatchIsRefused()
    {
        using var table = new RynthApiTable(_view, _log.Add, scriptWindows: true);
        var host = new RynthCoreHost(*table.Native);
        Assert.Equal(-2, host.UiSubmit(new byte[16]));
    }

    [Fact]
    public void SlashRoOpensOraclesWindowAndImGuiDrawsIt()
    {
        using RynthHost host = CreateOracleHost();

        host.Plugins[0].OnChatBarEnter("/ro");
        host.Tick();
        host.Tick();

        Assert.True(ScriptWindowRegistry.IsOpen(OracleKey));
        _imgui.Frame(ImGuiPanelHost.DrawAll);
        int vertices = _imgui.Frame(ImGuiPanelHost.DrawAll);
        Assert.True(vertices > 100, $"only {vertices} vertices");
    }

    [Fact]
    public void APlayerCloseReachesThePluginAndTheWindowStaysClosed()
    {
        using RynthHost host = CreateOracleHost();
        host.Plugins[0].OnChatBarEnter("/ro");
        host.Tick();
        host.Tick();
        _imgui.Frame(ImGuiPanelHost.DrawAll);
        _imgui.Frame(ImGuiPanelHost.DrawAll);

        ImGuiPanelHost.Close("script:" + OracleKey);
        for (int i = 0; i < 5; i++)
            host.Tick();

        Assert.False(ScriptWindowRegistry.IsOpen(OracleKey));
    }

    [Fact]
    public void DisposingTheHostRemovesItsWindows()
    {
        RynthHost host = CreateOracleHost();
        host.Plugins[0].OnChatBarEnter("/ro");
        host.Tick();
        host.Tick();

        host.Dispose();

        Assert.False(ScriptWindowRegistry.IsOpen(OracleKey));
        Assert.Equal(0, ImGuiPanelHost.OpenCount);
        Assert.False(EngineSettings.EnableImGuiBackend);
    }
}
```

Check the SDK wrapper `UiSubmit`'s parameter type (`grep -n "public int UiSubmit" external/RynthCore/src/RynthCore.PluginSdk/RynthCoreHost.cs`) and pass what it takes. The panel key prefix is `ScriptWindowRegistry.PanelKeyPrefix` (`"script:"`), and the sanitised key here equals the window key.

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo`. Expected: build errors, because there is no `scriptWindows` parameter yet.

- [ ] **Step 2: The UI slots.** In `src/AcDream.Rynth.Host/RynthApiTable.cs`:

1. Add `using RynthCore.Engine.Plugins;` and `using RynthCore.Engine.UI.ScriptWindows;`.
2. Change the constructor signature to `public RynthApiTable(IRynthGameView view, Action<string> log, bool scriptWindows = false)`.
3. After the existing slot assignments, add:

```csharp
        if (scriptWindows)
        {
            Native->UiSubmitFn = (IntPtr)(delegate* unmanaged[Cdecl]<byte*, int, int>)&UiSubmit;
            Native->UiPollEventsFn = (IntPtr)(delegate* unmanaged[Cdecl]<byte*, int, int*, int>)&UiPollEvents;
            Native->UiGetInfoFn = (IntPtr)(delegate* unmanaged[Cdecl]<UiInfoNative*, int>)&UiGetInfo;
            EngineSettings.EnableImGuiBackend = true;
        }
```

4. In `Dispose`, before releasing `_current`, add `EngineSettings.EnableImGuiBackend = false;`.
5. Add the three functions. They match RynthCore's `PluginManager.UiSubmitAction` / `UiPollEventsAction` / `UiGetInfoAction`:

```csharp
    // RynthCore return codes: 0 ok, -1 malformed or threw, -2 not inside a plugin call,
    // -3 over a limit, -4 unsupported format (the last three from the registry).
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int UiSubmit(byte* data, int length)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return -2;
        try
        {
            RynthPlugin? owner = RynthDispatch.Current;
            return owner is null ? -2 : ScriptWindowRegistry.Submit(owner.Owner, data, length);
        }
        catch (Exception error)
        {
            table.Fail(nameof(UiSubmit), error);
            return -1;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int UiPollEvents(byte* buffer, int capacity, int* remaining)
    {
        RynthApiTable? table = _current;
        if (remaining != null)
            *remaining = 0;
        if (table is null)
            return 0;
        try
        {
            RynthPlugin? owner = RynthDispatch.Current;
            return owner is null ? -2 : ScriptWindowRegistry.PollEvents(owner.Owner, buffer, capacity, remaining);
        }
        catch (Exception error)
        {
            table.Fail(nameof(UiPollEvents), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int UiGetInfo(UiInfoNative* info)
    {
        RynthApiTable? table = _current;
        if (table is null || info is null)
            return 0;
        try { return ScriptWindowRegistry.GetInfo(info); }
        catch (Exception error)
        {
            table.Fail(nameof(UiGetInfo), error);
            return 0;
        }
    }
```

If the registry's `Submit`, `PollEvents` or `GetInfo` signatures differ from these calls, adapt the calls to the real signatures in `ScriptWindowRegistry.cs`.

- [ ] **Step 3: The host flag, log sink and owner cleanup.** In `src/AcDream.Rynth.Host/RynthHost.cs`:

1. Change the constructor signature to `public RynthHost(IRynthGameView view, Action<string> log, IEnumerable<Type> exportsTypes, bool scriptWindows = false)`.
2. Create the table with `new RynthApiTable(view, _log, scriptWindows)`.
3. Right after creating the table, add `RynthCore.Engine.RynthLog.Sink = _log;` and `RynthCore.Engine.ImGuiBackend.ImGuiPanelHost.Log = _log;`.
4. Make `Dispose` drop each plugin's windows after its shutdown, as RynthCore does at unload:

```csharp
    public void Dispose()
    {
        foreach (RynthPlugin plugin in _plugins)
        {
            plugin.Shutdown();
            RynthCore.Engine.UI.ScriptWindows.ScriptWindowRegistry.DropOwner(plugin.Owner);
        }

        _plugins.Clear();
        RynthCore.Engine.Compatibility.LoginLifecycleHooks.HasObservedLoginComplete = false;
        _table.Dispose();
    }
```

- [ ] **Step 4: Login flag.** In `src/AcDream.Rynth.Host/RynthEventBridge.cs`:
- In `OnLifecycle`, inside the InWorld branch before the loop, add `RynthCore.Engine.Compatibility.LoginLifecycleHooks.HasObservedLoginComplete = true;`.
- In `OnLeavingWorld`, add `RynthCore.Engine.Compatibility.LoginLifecycleHooks.HasObservedLoginComplete = false;` before the loop.

- [ ] **Step 5: Run.** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo`. Expected: all pass.

If `SlashRoOpensOraclesWindowAndImGuiDrawsIt` fails because the window isn't open after two ticks:
- Log `ScriptWindowRegistry.Describe()` and the `_log` lines in the assertion message to see the registry's view (refused lists, errors).
- Read `external/RynthSuite/Plugins/RynthCore.Plugin.RynthOracle/RynthOraclePlugin.cs` `OnTick` and `Ui/UiHost.cs` to see how many ticks it needs.
- Adjust the tick count only, never the plugin.

If `APlayerCloseReachesThePluginAndTheWindowStaysClosed` fails, the face must have had `OnShown` before `Close`. That is why the test draws two frames first. Check `ScriptWindowFace.NoteVisibility` for what it needs.

- [ ] **Step 6: Commit.**

```bash
git add src/AcDream.Rynth.Host tests/AcDream.Rynth.Host.Tests
git commit -m "feat(rynth): script-window slots; RynthOracle's window draws through RynthCore's replay

Claude-Session: https://claude.ai/code/session_013qHSfUa7LKyjkciWdcioQr"
```

---

### Task 4: The overlay contract and the Rynth extension's use of it

**Goal:** `IClientOverlay` exists in the contract. With an overlay, the Rynth extension turns script windows on, draws them from an overlay frame callback and forwards `/ro …` to RynthOracle. Without one, RynthOracle answers with its no-UI line.

**Files:**
- Create: `src/AcDream.ClientExtensions/IClientOverlay.cs`
- Modify: `src/AcDream.ClientExtensions/ClientExtensionContext.cs`, `src/AcDream.Rynth.Host/RynthClientExtension.cs`
- Test: `tests/AcDream.App.Tests/Extensions/RynthOverlayExtensionTests.cs`

**Acceptance Criteria:**
- [ ] `ClientExtensionContext` has optional `Overlay` (`IClientOverlay?`, default null) and `SubmitChatCommand` (`Func<string, bool>?`, default null). The existing tests that build it with only the three required members still compile.
- [ ] With a fake overlay, `Attach` registers exactly one frame callback. After `/ro` (through the registered `ro` command handler) and two ticks, running the callback inside two headless ImGui frames produces more than 100 vertices.
- [ ] Without an overlay, the `ro` handler makes the runtime chat contain RynthOracle's "can't show plugin windows" line after a tick.
- [ ] `Dispose` removes the frame callback (the fake records the disposal) and the `ro` and `rynth` registrations.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests -nologo --filter "FullyQualifiedName~Extensions"` → all pass

**Steps:**

- [ ] **Step 1: The contract.** `src/AcDream.ClientExtensions/IClientOverlay.cs`:

```csharp
namespace AcDream.ClientExtensions;

/// <summary>
/// The client's Dear ImGui overlay, drawn over everything else. An extension
/// adds a callback that makes ImGui.NET calls; the client runs it once per
/// rendered frame between ImGui.NewFrame and ImGui.Render, on the game thread.
/// </summary>
public interface IClientOverlay
{
    /// <summary>Adds a per-frame draw callback. Dispose the result to remove it.</summary>
    IDisposable AddFrameCallback(Action draw);

    /// <summary>True while ImGui uses the mouse (over a window, dragging).</summary>
    bool WantsMouse { get; }

    /// <summary>True while an ImGui text field has the keyboard.</summary>
    bool WantsKeyboard { get; }
}
```

In `src/AcDream.ClientExtensions/ClientExtensionContext.cs`, add after `RegisterChatCommand`:

```csharp
    /// <summary>The ImGui overlay, or null when the client has none.</summary>
    public IClientOverlay? Overlay { get; init; }

    /// <summary>
    /// Runs text as if typed in the chat bar (commands, tells, say). True when
    /// the client accepted it. Null when the client offers no submission.
    /// </summary>
    public Func<string, bool>? SubmitChatCommand { get; init; }
```

- [ ] **Step 2: The extension.** Replace `src/AcDream.Rynth.Host/RynthClientExtension.cs` with:

```csharp
using AcDream.ClientExtensions;
using AcDream.Core.Combat;
using RynthCore.Engine.ImGuiBackend;

namespace AcDream.Rynth.Host;

/// <summary>
/// Runs RynthCore plugins (RynthTracker, RynthOracle) inside the client: the
/// API table reads the runtime, runtime events become plugin callbacks,
/// script windows draw on the client's ImGui overlay when it has one, and
/// <c>/rynth tracker</c> and <c>/ro</c> reach the plugins.
/// </summary>
public sealed class RynthClientExtension : IClientExtension
{
    private RynthHost? _host;
    private IDisposable? _runtimeSubscription;
    private IDisposable? _frameCallback;
    private readonly List<IDisposable> _commands = new();
    private CombatState? _combat;

    public void Attach(ClientExtensionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var view = new RuntimeRynthGameView(context.Runtime, context.SubmitChatCommand);
        bool scriptWindows = context.Overlay is not null;
        _host = new RynthHost(
            view,
            context.Log,
            [
                typeof(RynthCore.Plugin.RynthTracker.PluginExports),
                typeof(RynthCore.Plugin.RynthOracle.PluginExports),
            ],
            scriptWindows);
        _runtimeSubscription = context.Runtime.Subscribe(_host.Bridge);
        _combat = context.Runtime.ActionOwner.Combat;
        _combat.HealthChanged += OnHealthChanged;
        _combat.KillLanded += OnKillLanded;
        _commands.Add(context.RegisterChatCommand("rynth", arguments => OnRynthCommand(view, arguments)));
        _commands.Add(context.RegisterChatCommand("ro", arguments => OfferChatBarText("/ro " + arguments)));
        if (context.Overlay is { } overlay)
            _frameCallback = overlay.AddFrameCallback(ImGuiPanelHost.DrawAll);
    }

    public void Tick(double deltaSeconds) => _host?.Tick();

    public void Dispose()
    {
        _frameCallback?.Dispose();
        _frameCallback = null;
        foreach (IDisposable command in _commands)
            command.Dispose();
        _commands.Clear();
        if (_combat is not null)
        {
            _combat.HealthChanged -= OnHealthChanged;
            _combat.KillLanded -= OnKillLanded;
            _combat = null;
        }

        _runtimeSubscription?.Dispose();
        _runtimeSubscription = null;
        _host?.Dispose();
        _host = null;
    }

    private void OnHealthChanged(uint targetId, float healthRatio) =>
        _host?.Bridge.OnHealthChanged(targetId, healthRatio);

    private void OnKillLanded(string deathMessage, uint victimId) =>
        _host?.Bridge.OnKillLanded(deathMessage);

    /// <summary>Offers chat-bar text to each plugin until one eats it.</summary>
    private void OfferChatBarText(string text)
    {
        if (_host is null)
            return;
        foreach (RynthPlugin plugin in _host.Plugins)
        {
            if (plugin.OnChatBarEnter(text.TrimEnd()))
                return;
        }
    }

    private void OnRynthCommand(IRynthGameView view, string arguments)
    {
        if (!string.Equals(arguments.Trim(), "tracker", StringComparison.OrdinalIgnoreCase))
        {
            view.WriteChatLine("Usage: /rynth tracker", 0);
            return;
        }

        RynthPlugin? tracker = _host?.Plugins.FirstOrDefault(
            plugin => plugin.Name.EndsWith("RynthTracker", StringComparison.Ordinal));
        view.WriteChatLine(
            TrackerSnapshotText.Format(tracker?.CallStringExport("RynthTrackerGetSnapshotJson")),
            0);
    }
}
```

This makes `Dispose` idempotent, which also closes the earlier deferred finding.

- [ ] **Step 3: The tests.** `tests/AcDream.App.Tests/Extensions/RynthOverlayExtensionTests.cs`. It joins the existing `[Collection("Rynth")]`, because only one API table can be live:

```csharp
using System.Numerics;
using AcDream.ClientExtensions;
using AcDream.Rynth.Host;
using ImGuiNET;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.UI.ScriptWindows;

namespace AcDream.App.Tests.Extensions;

[Collection("Rynth")]
public sealed class RynthOverlayExtensionTests : IDisposable
{
    private readonly Dictionary<string, Action<string>> _commands = new();
    private readonly List<string> _log = new();
    private readonly IntPtr _imgui;

    public RynthOverlayExtensionTests()
    {
        ScriptWindowRegistry.Reset();
        ImGuiPanelHost.Reset();
        _imgui = ImGui.CreateContext();
        ImGui.SetCurrentContext(_imgui);
        ImGuiIOPtr io = ImGui.GetIO();
        io.DisplaySize = new Vector2(1280, 800);
        io.Fonts.GetTexDataAsRGBA32(out IntPtr _, out int _, out int _, out int _);
        io.Fonts.SetTexID(1);
    }

    public void Dispose()
    {
        ScriptWindowRegistry.Reset();
        ImGuiPanelHost.Reset();
        ImGui.DestroyContext(_imgui);
    }

    private ClientExtensionContext Context(AcDream.Runtime.GameRuntime runtime, IClientOverlay? overlay) => new()
    {
        Runtime = runtime,
        Log = _log.Add,
        RegisterChatCommand = (verb, handler) =>
        {
            _commands[verb] = handler;
            return new Registration(() => _commands.Remove(verb));
        },
        Overlay = overlay,
    };

    [Fact]
    public void WithAnOverlayOraclesWindowDrawsFromTheFrameCallback()
    {
        var overlay = new FakeOverlay();
        var extension = new RynthClientExtension();
        extension.Attach(Context(GameRuntimeTestFactory.Create(), overlay));

        _commands["ro"]("");
        extension.Tick(1 / 60.0);
        extension.Tick(1 / 60.0);
        Action draw = Assert.Single(overlay.Callbacks);
        Frame(draw);
        int vertices = Frame(draw);

        extension.Dispose();
        Assert.True(vertices > 100, $"only {vertices} vertices");
        Assert.True(overlay.CallbackRemoved);
        Assert.Empty(_commands);
    }

    [Fact]
    public void WithoutAnOverlaySlashRoSaysSoInChat()
    {
        var runtime = GameRuntimeTestFactory.Create();
        var extension = new RynthClientExtension();
        extension.Attach(Context(runtime, overlay: null));

        _commands["ro"]("");
        extension.Tick(1 / 60.0);
        extension.Dispose();

        Assert.Contains(runtime.CommunicationOwner.Chat.Snapshot(), entry =>
            entry.Text.Contains("can't show plugin windows", StringComparison.Ordinal));
    }

    private static int Frame(Action draw)
    {
        ImGui.GetIO().DeltaTime = 1f / 60f;
        ImGui.NewFrame();
        draw();
        ImGui.Render();
        return ImGui.GetDrawData().TotalVtxCount;
    }

    private sealed class FakeOverlay : IClientOverlay
    {
        public List<Action> Callbacks { get; } = new();
        public bool CallbackRemoved { get; private set; }
        public bool WantsMouse => false;
        public bool WantsKeyboard => false;

        public IDisposable AddFrameCallback(Action draw)
        {
            Callbacks.Add(draw);
            return new Registration(() => { Callbacks.Remove(draw); CallbackRemoved = true; });
        }
    }

    private sealed class Registration(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
```

The existing `RynthClientExtensionTests` expects exactly one `rynth` registration. Now there are two registrations (`rynth`, `ro`), so update its recording delegate to tolerate more than one verb, while still asserting that `rynth` was registered and disposed. AcDream.App.Tests reaches ImGui.NET through Rynth.Host → ScriptWindows. If the test project can't see `ImGuiNET` types, add `<PackageReference Include="ImGui.NET" />` to `tests/AcDream.App.Tests/AcDream.App.Tests.csproj`.

- [ ] **Step 4: Run and commit.** `~/.dotnet/dotnet test tests/AcDream.App.Tests -nologo --filter "FullyQualifiedName~Extensions"` and `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo`. Expected: all pass. Restore may change the `packages.*.lock.json` files of the projects that reference Rynth.Host: commit them.

```bash
git add src/AcDream.ClientExtensions src/AcDream.Rynth.Host tests/AcDream.App.Tests
git add $(git status --porcelain | awk '/lock.json$/ {print $2}')
git commit -m "feat(rynth): IClientOverlay; the Rynth extension draws script windows and forwards /ro

Claude-Session: https://claude.ai/code/session_013qHSfUa7LKyjkciWdcioQr"
```

---

### Task 5: The overlay core in the client (no GPU): context, frame lifecycle, input queue, scissor maths

**Goal:** `ImGuiOverlay` implements `IClientOverlay` and `IDevToolsFrameLifecycle` with a pluggable renderer. It runs callbacks, reports capture flags and survives aborts. `ImGuiInputQueue` turns Silk-style input into ImGui IO events, and `ImGuiScissor` converts clip rects. All are headless-tested.

**Files:**
- Modify: `src/AcDream.App/AcDream.App.csproj` (add `<PackageReference Include="ImGui.NET" />`)
- Create: `src/AcDream.App/Extensions/Overlay/IImGuiDrawRenderer.cs`, `ImGuiOverlay.cs`, `ImGuiInputQueue.cs`, `ImGuiKeyMap.cs`, `ImGuiScissor.cs`
- Test: `tests/AcDream.App.Tests/Extensions/ImGuiOverlayTests.cs`, `ImGuiScissorTests.cs`

**Acceptance Criteria:**
- [ ] Before `Initialize`, `BeginFrame`/`Render`/`AbortFrame` do nothing, callbacks never run, and `WantsMouse` is false.
- [ ] After `Initialize` with a fake renderer, one `BeginFrame` + `Render` runs each callback once, hands the fake renderer draw data, and calls its `PrepareFrame` before `NewFrame`.
- [ ] A callback that throws is removed and logged. The frame still ends and the next frame works.
- [ ] `AbortFrame` after `BeginFrame` leaves ImGui balanced (the next `BeginFrame` works). Calling it twice, or without `BeginFrame`, does nothing.
- [ ] A queued mouse move over an open ImGui window, applied in `BeginFrame`, makes `WantsMouse` true on the following frame.
- [ ] `ImGuiScissor.TryMap` maps clip `(10,20)-(110,70)` at scale 1 with framebuffer height 600 to `(10, 530, 100, 50)`, and at scale 2 with height 1200 to `(20, 1060, 200, 100)`. It returns false for an empty or fully off-screen rect and clamps a partly off-screen one.
- [ ] `ImGuiKeyMap.Map` maps Silk `Key.A`→`ImGuiKey.A`, `Key.Enter`→`ImGuiKey.Enter`, `Key.Backspace`→`ImGuiKey.Backspace` and an unknown key to `ImGuiKey.None`.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests -nologo --filter "FullyQualifiedName~Extensions"` → all pass

**Steps:**

- [ ] **Step 1: Package reference.** In `src/AcDream.App/AcDream.App.csproj`, add `<PackageReference Include="ImGui.NET" />` to the ItemGroup that holds the other `PackageReference` items. They have no `Version` attribute, because of central package management.

- [ ] **Step 2: The renderer seam.** `src/AcDream.App/Extensions/Overlay/IImGuiDrawRenderer.cs`:

```csharp
using System.Numerics;
using ImGuiNET;

namespace AcDream.App.Extensions.Overlay;

/// <summary>Draws ImGui's output. The client's is <see cref="ImGuiVulkanRenderer"/>; tests use a fake.</summary>
internal interface IImGuiDrawRenderer : IDisposable
{
    /// <summary>
    /// Called before ImGui.NewFrame each frame, with no pass open: build or
    /// rebuild the font atlas for this framebuffer scale and upload it.
    /// </summary>
    void PrepareFrame(ImGuiIOPtr io, Vector2 framebufferScale);

    /// <summary>Draws one frame's output; called after ImGui.Render.</summary>
    void Render(ImDrawDataPtr drawData, Vector2 framebufferSize);
}
```

- [ ] **Step 3: Scissor maths.** `src/AcDream.App/Extensions/Overlay/ImGuiScissor.cs`:

```csharp
using System.Numerics;

namespace AcDream.App.Extensions.Overlay;

/// <summary>
/// Turns an ImGui clip rectangle (display points, origin top-left) into an
/// OpenAC scissor: framebuffer pixels with Y counted from the bottom, which
/// is how IGpuPassEncoder.SetScissor takes it.
/// </summary>
internal static class ImGuiScissor
{
    public static bool TryMap(
        Vector4 clipRect,
        Vector2 displayPos,
        Vector2 framebufferScale,
        int framebufferWidth,
        int framebufferHeight,
        out (int X, int Y, int Width, int Height) scissor)
    {
        float x0 = MathF.Max(0f, (clipRect.X - displayPos.X) * framebufferScale.X);
        float y0 = MathF.Max(0f, (clipRect.Y - displayPos.Y) * framebufferScale.Y);
        float x1 = MathF.Min(framebufferWidth, (clipRect.Z - displayPos.X) * framebufferScale.X);
        float y1 = MathF.Min(framebufferHeight, (clipRect.W - displayPos.Y) * framebufferScale.Y);
        if (x1 <= x0 || y1 <= y0)
        {
            scissor = default;
            return false;
        }

        int left = (int)x0;
        int top = (int)y0;
        int width = (int)MathF.Ceiling(x1) - left;
        int height = (int)MathF.Ceiling(y1) - top;
        scissor = (left, framebufferHeight - (top + height), width, height);
        return true;
    }
}
```

`tests/AcDream.App.Tests/Extensions/ImGuiScissorTests.cs`:

```csharp
using System.Numerics;
using AcDream.App.Extensions.Overlay;

namespace AcDream.App.Tests.Extensions;

public sealed class ImGuiScissorTests
{
    [Fact]
    public void ScaleOne()
    {
        Assert.True(ImGuiScissor.TryMap(new Vector4(10, 20, 110, 70), Vector2.Zero, Vector2.One, 800, 600, out var s));
        Assert.Equal((10, 530, 100, 50), s);
    }

    [Fact]
    public void ScaleTwo()
    {
        Assert.True(ImGuiScissor.TryMap(new Vector4(10, 20, 110, 70), Vector2.Zero, new Vector2(2, 2), 1600, 1200, out var s));
        Assert.Equal((20, 1060, 200, 100), s);
    }

    [Fact]
    public void EmptyAndOffScreenAreSkipped()
    {
        Assert.False(ImGuiScissor.TryMap(new Vector4(10, 10, 10, 50), Vector2.Zero, Vector2.One, 800, 600, out _));
        Assert.False(ImGuiScissor.TryMap(new Vector4(900, 10, 950, 50), Vector2.Zero, Vector2.One, 800, 600, out _));
    }

    [Fact]
    public void PartlyOffScreenIsClamped()
    {
        Assert.True(ImGuiScissor.TryMap(new Vector4(-20, -10, 30, 40), Vector2.Zero, Vector2.One, 800, 600, out var s));
        Assert.Equal((0, 560, 30, 40), s);
    }
}
```

- [ ] **Step 4: Key map.** `src/AcDream.App/Extensions/Overlay/ImGuiKeyMap.cs`:

```csharp
using ImGuiNET;
using Silk.NET.Input;

namespace AcDream.App.Extensions.Overlay;

/// <summary>Silk.NET keys to ImGui keys (the keys ImGui widgets use).</summary>
internal static class ImGuiKeyMap
{
    public static ImGuiKey Map(Key key) => key switch
    {
        >= Key.A and <= Key.Z => ImGuiKey.A + (key - Key.A),
        >= Key.Number0 and <= Key.Number9 => ImGuiKey._0 + (key - Key.Number0),
        >= Key.F1 and <= Key.F12 => ImGuiKey.F1 + (key - Key.F1),
        >= Key.Keypad0 and <= Key.Keypad9 => ImGuiKey.Keypad0 + (key - Key.Keypad0),
        Key.Tab => ImGuiKey.Tab,
        Key.Left => ImGuiKey.LeftArrow,
        Key.Right => ImGuiKey.RightArrow,
        Key.Up => ImGuiKey.UpArrow,
        Key.Down => ImGuiKey.DownArrow,
        Key.PageUp => ImGuiKey.PageUp,
        Key.PageDown => ImGuiKey.PageDown,
        Key.Home => ImGuiKey.Home,
        Key.End => ImGuiKey.End,
        Key.Insert => ImGuiKey.Insert,
        Key.Delete => ImGuiKey.Delete,
        Key.Backspace => ImGuiKey.Backspace,
        Key.Space => ImGuiKey.Space,
        Key.Enter => ImGuiKey.Enter,
        Key.KeypadEnter => ImGuiKey.KeypadEnter,
        Key.Escape => ImGuiKey.Escape,
        Key.ControlLeft => ImGuiKey.LeftCtrl,
        Key.ControlRight => ImGuiKey.RightCtrl,
        Key.ShiftLeft => ImGuiKey.LeftShift,
        Key.ShiftRight => ImGuiKey.RightShift,
        Key.AltLeft => ImGuiKey.LeftAlt,
        Key.AltRight => ImGuiKey.RightAlt,
        Key.SuperLeft => ImGuiKey.LeftSuper,
        Key.SuperRight => ImGuiKey.RightSuper,
        Key.Comma => ImGuiKey.Comma,
        Key.Period => ImGuiKey.Period,
        Key.Minus => ImGuiKey.Minus,
        Key.Equal => ImGuiKey.Equal,
        Key.Slash => ImGuiKey.Slash,
        Key.Semicolon => ImGuiKey.Semicolon,
        Key.Apostrophe => ImGuiKey.Apostrophe,
        Key.LeftBracket => ImGuiKey.LeftBracket,
        Key.RightBracket => ImGuiKey.RightBracket,
        Key.BackSlash => ImGuiKey.Backslash,
        Key.GraveAccent => ImGuiKey.GraveAccent,
        _ => ImGuiKey.None,
    };
}
```

Check the Silk and ImGui enum member names compile (`Key.BackSlash`, `ImGuiKey._0`, `ImGuiKey.Keypad0`). Fix any name to the real member and keep the mapping.

- [ ] **Step 5: Input queue.** `src/AcDream.App/Extensions/Overlay/ImGuiInputQueue.cs`:

```csharp
using System.Numerics;
using ImGuiNET;
using Silk.NET.Input;

namespace AcDream.App.Extensions.Overlay;

/// <summary>
/// Collects input events as they arrive (Silk callbacks) and hands them to
/// ImGui at the start of the next frame. Thread-safe; touches no game state.
/// </summary>
internal sealed class ImGuiInputQueue
{
    private readonly object _gate = new();
    private readonly List<Action<ImGuiIOPtr>> _pending = new();

    public void MouseMove(Vector2 points) => Add(io => io.AddMousePosEvent(points.X, points.Y));

    public void MouseButton(MouseButton button, bool down)
    {
        int index = button switch
        {
            Silk.NET.Input.MouseButton.Left => 0,
            Silk.NET.Input.MouseButton.Right => 1,
            Silk.NET.Input.MouseButton.Middle => 2,
            _ => -1,
        };
        if (index >= 0)
            Add(io => io.AddMouseButtonEvent(index, down));
    }

    public void Scroll(Vector2 wheel) => Add(io => io.AddMouseWheelEvent(wheel.X, wheel.Y));

    public void KeyChange(Key key, bool down)
    {
        ImGuiKey mapped = ImGuiKeyMap.Map(key);
        if (mapped == ImGuiKey.None)
            return;
        Add(io =>
        {
            io.AddKeyEvent(mapped, down);
            ImGuiKey modifier = mapped switch
            {
                ImGuiKey.LeftCtrl or ImGuiKey.RightCtrl => ImGuiKey.ModCtrl,
                ImGuiKey.LeftShift or ImGuiKey.RightShift => ImGuiKey.ModShift,
                ImGuiKey.LeftAlt or ImGuiKey.RightAlt => ImGuiKey.ModAlt,
                ImGuiKey.LeftSuper or ImGuiKey.RightSuper => ImGuiKey.ModSuper,
                _ => ImGuiKey.None,
            };
            if (modifier != ImGuiKey.None)
                io.AddKeyEvent(modifier, down);
        });
    }

    public void Character(char value) => Add(io => io.AddInputCharacter(value));

    /// <summary>Applies everything queued since the last call.</summary>
    public void ApplyTo(ImGuiIOPtr io)
    {
        Action<ImGuiIOPtr>[] events;
        lock (_gate)
        {
            events = _pending.ToArray();
            _pending.Clear();
        }

        foreach (Action<ImGuiIOPtr> apply in events)
            apply(io);
    }

    private void Add(Action<ImGuiIOPtr> apply)
    {
        lock (_gate)
            _pending.Add(apply);
    }
}
```

- [ ] **Step 6: The overlay.** `src/AcDream.App/Extensions/Overlay/ImGuiOverlay.cs`:

```csharp
using System.Diagnostics;
using System.Numerics;
using AcDream.App.Rendering;
using AcDream.ClientExtensions;
using ImGuiNET;

namespace AcDream.App.Extensions.Overlay;

/// <summary>
/// The client's Dear ImGui overlay. Created with the window (before anything
/// GPU exists) so extensions can hold it from Attach; Initialize gives it a
/// renderer and the window size once the GPU is up. Until then, and if
/// starting fails, it stays inert. It is the dev-tools frame hook: BeginFrame
/// (before the world pass) starts the ImGui frame, Render (after the retained
/// UI) runs the callbacks and draws, AbortFrame closes a frame that failed.
/// </summary>
internal sealed class ImGuiOverlay : IClientOverlay, IDevToolsFrameLifecycle, IDisposable
{
    private readonly Action<string> _log;
    private readonly List<Action> _callbacks = new();
    private readonly Stopwatch _summaryClock = Stopwatch.StartNew();
    private IImGuiDrawRenderer? _renderer;
    private Func<(Vector2 Points, Vector2 Pixels)>? _windowSize;
    private IntPtr _context;
    private bool _frameOpen;
    private bool _failed;

    public ImGuiOverlay(Action<string> log) => _log = log ?? throw new ArgumentNullException(nameof(log));

    /// <summary>Input arrives here from the window's devices.</summary>
    public ImGuiInputQueue Input { get; } = new();

    public bool WantsMouse { get; private set; }

    public bool WantsKeyboard { get; private set; }

    /// <summary>True once Initialize succeeded.</summary>
    public bool IsAvailable => _renderer is not null && !_failed;

    public IDisposable AddFrameCallback(Action draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        _callbacks.Add(draw);
        return new CallbackRegistration(this, draw);
    }

    /// <summary>
    /// Starts the overlay. <paramref name="windowSize"/> gives the window size
    /// in points and the framebuffer size in pixels, read every frame.
    /// </summary>
    public void Initialize(IImGuiDrawRenderer renderer, Func<(Vector2 Points, Vector2 Pixels)> windowSize)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(windowSize);
        try
        {
            _context = ImGui.CreateContext();
            ImGui.SetCurrentContext(_context);
            ImGuiIOPtr io = ImGui.GetIO();
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
            io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
            unsafe { io.NativePtr->IniFilename = null; }
            _renderer = renderer;
            _windowSize = windowSize;
            _log($"[ImGui] overlay started (ImGui {ImGui.GetVersion()})");
        }
        catch (Exception error)
        {
            _failed = true;
            _log($"[ImGui] overlay unavailable: {error.GetType().Name}: {error.Message}");
        }
    }

    public void BeginFrame(float deltaSeconds)
    {
        if (!IsAvailable || _frameOpen)
            return;
        try
        {
            ImGui.SetCurrentContext(_context);
            ImGuiIOPtr io = ImGui.GetIO();
            (Vector2 points, Vector2 pixels) = _windowSize!();
            if (points.X <= 0 || points.Y <= 0)
                return;
            Vector2 scale = pixels / points;
            io.DisplaySize = points;
            io.DisplayFramebufferScale = scale;
            io.DeltaTime = MathF.Max(deltaSeconds, 1e-4f);
            _renderer!.PrepareFrame(io, scale);
            Input.ApplyTo(io);
            ImGui.NewFrame();
            _frameOpen = true;
            WantsMouse = io.WantCaptureMouse;
            WantsKeyboard = io.WantCaptureKeyboard;
        }
        catch (Exception error)
        {
            Fail("begin frame", error);
        }
    }

    public void Render(double deltaSeconds, int viewportWidth, int viewportHeight)
    {
        if (!_frameOpen)
            return;
        ImGui.SetCurrentContext(_context);
        foreach (Action callback in _callbacks.ToArray())
        {
            try
            {
                callback();
            }
            catch (Exception error)
            {
                _callbacks.Remove(callback);
                _log($"[ImGui] frame callback removed: {error.GetType().Name}: {error.Message}");
            }
        }

        try
        {
            ImGui.Render();
            _frameOpen = false;
            ImDrawDataPtr drawData = ImGui.GetDrawData();
            (Vector2 _, Vector2 pixels) = _windowSize!();
            _renderer!.Render(drawData, pixels);
            if (drawData.TotalVtxCount > 0 && _summaryClock.Elapsed.TotalSeconds >= 1)
            {
                _summaryClock.Restart();
                _log($"[ImGui] frame: lists={drawData.CmdListsCount} vtx={drawData.TotalVtxCount}");
            }
        }
        catch (Exception error)
        {
            _frameOpen = false;
            Fail("render", error);
        }
    }

    public void AbortFrame()
    {
        if (!_frameOpen)
            return;
        _frameOpen = false;
        try
        {
            ImGui.SetCurrentContext(_context);
            ImGui.EndFrame();
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        AbortFrame();
        _callbacks.Clear();
        _renderer?.Dispose();
        _renderer = null;
        if (_context != IntPtr.Zero)
        {
            ImGui.DestroyContext(_context);
            _context = IntPtr.Zero;
        }
    }

    private void Fail(string stage, Exception error)
    {
        _failed = true;
        WantsMouse = false;
        WantsKeyboard = false;
        _log($"[ImGui] overlay stopped at {stage}: {error.GetType().Name}: {error.Message}");
    }

    private sealed class CallbackRegistration(ImGuiOverlay owner, Action draw) : IDisposable
    {
        public void Dispose() => owner._callbacks.Remove(draw);
    }
}
```

Check `IDevToolsFrameLifecycle`'s namespace (`src/AcDream.App/Rendering/RenderFramePreparationController.cs`) and adjust the `using`. If `io.NativePtr->IniFilename` doesn't compile, use `ImGuiNative.igGetIO()->IniFilename = null;` in an `unsafe` block. The intent is to keep ImGui from writing `imgui.ini`.

- [ ] **Step 7: Overlay tests.** `tests/AcDream.App.Tests/Extensions/ImGuiOverlayTests.cs`, in `[Collection("Rynth")]` because ImGui is process-global:

```csharp
using System.Numerics;
using AcDream.App.Extensions.Overlay;
using ImGuiNET;

namespace AcDream.App.Tests.Extensions;

[Collection("Rynth")]
public sealed class ImGuiOverlayTests : IDisposable
{
    private readonly List<string> _log = new();
    private readonly FakeRenderer _renderer = new();
    private readonly ImGuiOverlay _overlay;

    public ImGuiOverlayTests() => _overlay = new ImGuiOverlay(_log.Add);

    public void Dispose() => _overlay.Dispose();

    private void Start() =>
        _overlay.Initialize(_renderer, () => (new Vector2(800, 600), new Vector2(1600, 1200)));

    private void RunFrame()
    {
        _overlay.BeginFrame(1f / 60f);
        _overlay.Render(1 / 60.0, 800, 600);
    }

    [Fact]
    public void BeforeInitializeNothingRuns()
    {
        int calls = 0;
        _overlay.AddFrameCallback(() => calls++);
        RunFrame();
        _overlay.AbortFrame();
        Assert.Equal(0, calls);
        Assert.False(_overlay.WantsMouse);
    }

    [Fact]
    public void AFrameRunsCallbacksAndHandsTheRendererDrawData()
    {
        Start();
        int calls = 0;
        _overlay.AddFrameCallback(() => { calls++; ImGui.Begin("t"); ImGui.Text("hello"); ImGui.End(); });
        RunFrame();
        RunFrame();
        Assert.Equal(2, calls);
        Assert.Equal(2, _renderer.Prepared);
        Assert.True(_renderer.LastVertexCount > 0);
        Assert.Equal(new Vector2(2, 2), _renderer.LastScale);
    }

    [Fact]
    public void AThrowingCallbackIsRemovedAndTheNextFrameWorks()
    {
        Start();
        int good = 0;
        _overlay.AddFrameCallback(() => throw new InvalidOperationException("boom"));
        _overlay.AddFrameCallback(() => good++);
        RunFrame();
        RunFrame();
        Assert.Equal(2, good);
        Assert.Contains(_log, line => line.Contains("frame callback removed", StringComparison.Ordinal));
    }

    [Fact]
    public void AbortLeavesImGuiBalancedAndIsIdempotent()
    {
        Start();
        _overlay.AbortFrame();
        _overlay.BeginFrame(1f / 60f);
        _overlay.AbortFrame();
        _overlay.AbortFrame();
        RunFrame();
        Assert.True(_renderer.Rendered >= 1);
    }

    [Fact]
    public void MouseOverAWindowSetsWantsMouse()
    {
        Start();
        _overlay.AddFrameCallback(() =>
        {
            ImGui.SetNextWindowPos(new Vector2(100, 100));
            ImGui.SetNextWindowSize(new Vector2(200, 200));
            ImGui.Begin("hover");
            ImGui.End();
        });
        RunFrame();
        _overlay.Input.MouseMove(new Vector2(150, 150));
        RunFrame();
        RunFrame();
        Assert.True(_overlay.WantsMouse);
    }

    private sealed class FakeRenderer : IImGuiDrawRenderer
    {
        public int Prepared { get; private set; }
        public int Rendered { get; private set; }
        public int LastVertexCount { get; private set; }
        public Vector2 LastScale { get; private set; }

        public void PrepareFrame(ImGuiIOPtr io, Vector2 framebufferScale)
        {
            Prepared++;
            LastScale = framebufferScale;
            if (!io.Fonts.IsBuilt())
            {
                io.Fonts.GetTexDataAsRGBA32(out IntPtr _, out int _, out int _, out int _);
                io.Fonts.SetTexID(1);
            }
        }

        public void Render(ImDrawDataPtr drawData, Vector2 framebufferSize)
        {
            Rendered++;
            LastVertexCount = drawData.TotalVtxCount;
        }

        public void Dispose()
        {
        }
    }
}
```

Add a `ImGuiKeyMapTests` class in the same file or a new one, asserting the four mappings from the acceptance criteria. If `ImFontAtlasPtr.IsBuilt()` isn't available in 1.91.6.1, track "built" with a bool field in the fake instead.

- [ ] **Step 8: Run and commit.** `~/.dotnet/dotnet test tests/AcDream.App.Tests -nologo --filter "FullyQualifiedName~Extensions"`. Expected: all pass. App's lock files change with the new package; regenerate all RIDs as in Task 7 Step 6 or let Task 7 do it. At minimum, commit the neutral lock files restore wrote.

```bash
git add src/AcDream.App/AcDream.App.csproj src/AcDream.App/Extensions/Overlay tests/AcDream.App.Tests
git add $(git status --porcelain | awk '/lock.json$/ {print $2}')
git commit -m "feat(overlay): ImGui overlay core: frame lifecycle, callbacks, input queue, scissor maths

Claude-Session: https://claude.ai/code/session_013qHSfUa7LKyjkciWdcioQr"
```

---

### Task 6: The Vulkan renderer and its shaders

**Goal:** `ImGuiVulkanRenderer` builds the font atlas at framebuffer scale, uploads it as a bindless texture before any pass opens, and draws ImGui's lists in one single-sample backbuffer pass through OpenAC's GPU layer. It uses embedded SPIR-V compiled by the repo's own shader compiler.

**Files:**
- Create: `src/AcDream.App/Extensions/Overlay/Shaders/imgui_overlay.vert`, `imgui_overlay.frag`, `imgui_overlay.vert.spv`, `imgui_overlay.frag.spv`
- Create: `src/AcDream.App/Extensions/Overlay/ImGuiVulkanRenderer.cs`
- Modify: `src/AcDream.App/AcDream.App.csproj` (embed the two `.spv` files)
- Test: `tests/AcDream.App.Tests/Extensions/ImGuiVulkanRendererTests.cs`

**Acceptance Criteria:**
- [ ] Both `.spv` files start with the SPIR-V magic `0x07230203`, and the shader compiler printed `imgui_overlay: ok`.
- [ ] The `.spv` files load as manifest resources `AcDream.App.Overlay.imgui_overlay.vert.spv` and `AcDream.App.Overlay.imgui_overlay.frag.spv`.
- [ ] The renderer's vertex layout is stride 20 with Float2 at 0, Float2 at 8 and `UByte4Normalized` at 16.
- [ ] `dotnet build src/AcDream.App` has 0 warnings.
- [ ] `tools/ShaderCompiler/packages.*.lock.json` is not left in the repo.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests -nologo --filter "FullyQualifiedName~ImGuiVulkanRenderer"` → all pass

**Steps:**

- [ ] **Step 1: The shaders.** `src/AcDream.App/Extensions/Overlay/Shaders/imgui_overlay.vert`:

```glsl
#version 430 core
// ImGui overlay (src/AcDream.App/Extensions/Overlay). Positions are ImGui display
// points, origin top-left; uParamA/uParamB are the display size in points.
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 aUv;
layout(location = 2) in vec4 aColor;   // ImGui's packed RGBA, as UByte4Normalized

uniform float uParamA;
uniform float uParamB;

out vec2 vUv;
out vec4 vColor;

void main() {
    vec2 ndc = vec2(
        aPos.x / uParamA * 2.0 - 1.0,
        1.0 - aPos.y / uParamB * 2.0);
    gl_Position = vec4(ndc, 0.0, 1.0);
    vUv = aUv;
    vColor = aColor;
}
```

`src/AcDream.App/Extensions/Overlay/Shaders/imgui_overlay.frag`:

```glsl
#version 430 core
#extension GL_ARB_bindless_texture : require
// ImGui overlay: the texture is the RGBA font atlas (or another ImGui texture).
in vec2 vUv;
in vec4 vColor;
out vec4 FragColor;

uniform uint uTextureIndexA;

void main() {
    FragColor = ACDREAM_SAMPLE_2D(uTextureIndexA, vUv) * vColor;
}
```

- [ ] **Step 2: Compile them with the repo's shader compiler.** It injects the Vulkan preamble that OpenAC's pipeline layout needs. Run from a directory outside the repo, then remove the lock file the publish leaves behind:

```bash
R=~/code/OpenAC/OpenAC-Rynth
S=$(mktemp -d)
cd "$S"
~/.dotnet/dotnet publish "$R/tools/ShaderCompiler/ShaderCompiler.csproj" -c Release -r osx-arm64 --self-contained false -o "$S/tool" --artifacts-path "$S/artifacts" --nologo -v q
~/.dotnet/dotnet "$S/tool/AcDream.Tools.ShaderCompiler.dll" "$R/src/AcDream.App/Extensions/Overlay/Shaders" "$S/out"
cp "$S/out/imgui_overlay.vert.spv" "$S/out/imgui_overlay.frag.spv" "$R/src/AcDream.App/Extensions/Overlay/Shaders/"
cd "$R" && git status --porcelain tools/ && rm -f tools/ShaderCompiler/packages.*.lock.json
xxd -l 4 src/AcDream.App/Extensions/Overlay/Shaders/imgui_overlay.vert.spv
```

Expected: the compiler prints `imgui_overlay: ok` (it prints `NOT VULKAN-EXPRESSIBLE YET` and still exits 0 on failure, so read the line), and `xxd` shows `0302 2307`. Don't copy `shaders.manifest.json`.

- [ ] **Step 3: Embed the SPIR-V.** In `src/AcDream.App/AcDream.App.csproj`, add:

```xml
  <ItemGroup>
    <EmbeddedResource Include="Extensions\Overlay\Shaders\imgui_overlay.vert.spv" LogicalName="AcDream.App.Overlay.imgui_overlay.vert.spv" />
    <EmbeddedResource Include="Extensions\Overlay\Shaders\imgui_overlay.frag.spv" LogicalName="AcDream.App.Overlay.imgui_overlay.frag.spv" />
  </ItemGroup>
```

If the csproj already copies `Rendering\Shaders\**` to output, nothing changes there. Our files live elsewhere and are embedded, not copied.

- [ ] **Step 4: The renderer.** `src/AcDream.App/Extensions/Overlay/ImGuiVulkanRenderer.cs`:

```csharp
using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using ImGuiNET;

namespace AcDream.App.Extensions.Overlay;

/// <summary>
/// Draws ImGui through OpenAC's GPU layer: one single-sample pass onto the
/// backbuffer after everything else, straight-alpha blending, no depth, a
/// scissor per command. The font atlas is rasterised at the framebuffer scale
/// (sharp on Retina) and drawn back at 1/scale, and rebuilt when the scale changes.
/// </summary>
internal sealed unsafe class ImGuiVulkanRenderer : IImGuiDrawRenderer
{
    internal const float FontSizePoints = 13f;

    internal static readonly GpuVertexLayout VertexLayout = GpuVertexLayout.Interleaved(
        strideBytes: 20,
        [
            new GpuVertexAttribute(0, GpuVertexFormat.Float2, 0),
            new GpuVertexAttribute(1, GpuVertexFormat.Float2, 8),
            new GpuVertexAttribute(2, GpuVertexFormat.UByte4Normalized, 16),
        ]);

    private readonly IGpuDevice _device;
    private readonly ICurrentGpuFrameSource _frames;
    private readonly IGpuPipeline _pipeline;
    private IGpuTexture? _atlas;
    private GpuTextureSlot _atlasSlot = GpuTextureSlot.Unassigned;
    private float _atlasScale;

    public ImGuiVulkanRenderer(IGpuDevice device, ICurrentGpuFrameSource frames)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _frames = frames ?? throw new ArgumentNullException(nameof(frames));
        _pipeline = device.CreatePipeline(new GpuPipelineDescription
        {
            Name = "imgui-overlay",
            Shaders = new GpuShaderSet(
                "imgui-overlay",
                LoadSpirv("AcDream.App.Overlay.imgui_overlay.vert.spv"),
                LoadSpirv("AcDream.App.Overlay.imgui_overlay.frag.spv")),
            VertexLayout = VertexLayout,
            Topology = GpuPrimitiveTopology.TriangleList,
            Blend = GpuBlendMode.StraightAlpha,
            Depth = GpuDepthState.Disabled,
            Cull = GpuCullMode.None,
            SampleCount = 1,
        });
    }

    internal static byte[] LoadSpirv(string resourceName)
    {
        using Stream stream = typeof(ImGuiVulkanRenderer).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"missing embedded shader {resourceName}");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public void PrepareFrame(ImGuiIOPtr io, Vector2 framebufferScale)
    {
        float scale = MathF.Max(1f, framebufferScale.X);
        if (_atlas is not null && Math.Abs(scale - _atlasScale) < 0.01f)
            return;

        ReleaseAtlas();
        io.Fonts.Clear();
        ImFontConfigPtr config = ImGuiNative.ImFontConfig_ImFontConfig();
        config.SizePixels = FontSizePoints * scale;
        io.Fonts.AddFontDefault(config);
        config.Destroy();
        io.FontGlobalScale = 1f / scale;
        io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out int bytesPerPixel);

        IGpuTexture texture = _device.CreateTexture(new GpuTextureDescription(
            "imgui-font-atlas", GpuTextureKind.Texture2D, GpuTextureFormat.Rgba8Unorm, width, height, 1, 1));
        try
        {
            texture.Upload(0, 0, new ReadOnlySpan<byte>((void*)pixels, width * height * bytesPerPixel));
            _atlasSlot = _device.RegisterTexture(texture, _device.CreateSampler(GpuSamplerDescription.WorldClamp));
        }
        catch
        {
            texture.Dispose();
            throw;
        }

        _atlas = texture;
        _atlasScale = scale;
        io.Fonts.SetTexID((IntPtr)_atlasSlot.Index);
        io.Fonts.ClearTexData();
    }

    public void Render(ImDrawDataPtr drawData, Vector2 framebufferSize)
    {
        IGpuFrame? frame = _frames.CurrentFrame;
        if (frame is null || drawData.CmdListsCount == 0 || drawData.TotalVtxCount == 0)
            return;

        int fbWidth = (int)framebufferSize.X;
        int fbHeight = (int)framebufferSize.Y;
        if (fbWidth <= 0 || fbHeight <= 0)
            return;

        int vertexBytes = drawData.TotalVtxCount * sizeof(ImDrawVert);
        int indexBytes = drawData.TotalIdxCount * sizeof(ushort);
        GpuRingAllocation vertices = frame.AllocateRing(vertexBytes, GpuRingUsage.Vertex);
        GpuRingAllocation indices = frame.AllocateRing(indexBytes, GpuRingUsage.Index);
        int vtxWritten = 0;
        int idxWritten = 0;
        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            ImDrawListPtr list = drawData.CmdLists[n];
            var vtx = new ReadOnlySpan<byte>((void*)list.VtxBuffer.Data, list.VtxBuffer.Size * sizeof(ImDrawVert));
            var idx = new ReadOnlySpan<byte>((void*)list.IdxBuffer.Data, list.IdxBuffer.Size * sizeof(ushort));
            vtx.CopyTo(vertices.Data[vtxWritten..]);
            idx.CopyTo(indices.Data[idxWritten..]);
            vtxWritten += vtx.Length;
            idxWritten += idx.Length;
        }

        using IGpuPassEncoder encoder = frame.BeginPass(new GpuPassDescription
        {
            Name = "imgui-overlay",
            Color = new GpuColorAttachment(Target: null, Load: GpuLoadOp.Load, Store: GpuStoreOp.Store, ClearColor: default),
            Depth = null,
            SampleCount = 1,
        });
        encoder.BindPipeline(_pipeline);
        encoder.BindVertexBuffer(0, vertices.Buffer, vertices.OffsetBytes);
        encoder.BindIndexBuffer(indices.Buffer, indices.OffsetBytes, GpuIndexType.UInt16);

        GpuPushConstants constants = GpuPushConstants.Default;
        constants.ParamA = drawData.DisplaySize.X;
        constants.ParamB = drawData.DisplaySize.Y;
        uint boundTexture = uint.MaxValue;
        int listVtxBase = 0;
        int listIdxBase = 0;
        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            ImDrawListPtr list = drawData.CmdLists[n];
            for (int c = 0; c < list.CmdBuffer.Size; c++)
            {
                ImDrawCmdPtr cmd = list.CmdBuffer[c];
                if (cmd.UserCallback != IntPtr.Zero || cmd.ElemCount == 0)
                    continue;
                if (!ImGuiScissor.TryMap(cmd.ClipRect, drawData.DisplayPos, drawData.FramebufferScale, fbWidth, fbHeight, out var s))
                    continue;
                uint texture = (uint)cmd.TextureId;
                if (texture != boundTexture)
                {
                    constants.TextureIndexA = texture;
                    encoder.SetPushConstants(constants);
                    boundTexture = texture;
                }

                encoder.SetScissor(s.X, s.Y, s.Width, s.Height);
                encoder.DrawIndexed(cmd.ElemCount, 1, (uint)(listIdxBase + cmd.IdxOffset), listVtxBase + (int)cmd.VtxOffset, 0);
            }

            listVtxBase += list.VtxBuffer.Size;
            listIdxBase += list.IdxBuffer.Size;
        }
    }

    public void Dispose()
    {
        ReleaseAtlas();
        _pipeline.Dispose();
    }

    private void ReleaseAtlas()
    {
        if (_atlasSlot.IsAssigned)
            _device.ReleaseTextureSlot(_atlasSlot);
        _atlasSlot = GpuTextureSlot.Unassigned;
        _atlas?.Dispose();
        _atlas = null;
    }
}
```

Adapt the code to the real names where they differ, and keep the behaviour:
- `GpuPrimitiveTopology`, `GpuTextureKind`, `GpuTextureFormat.Rgba8Unorm` and `ICurrentGpuFrameSource` live in `AcDream.App.Rendering.Gpu` and `AcDream.App.Rendering`; check with grep.
- The `ImFontConfig` constructor native name in 1.91.6.1.
- `ImDrawListPtr` member names.

`Render` allocates the ring memory before opening the pass. That's allowed: ring allocations aren't staged copies.

- [ ] **Step 5: Tests (no GPU).** `tests/AcDream.App.Tests/Extensions/ImGuiVulkanRendererTests.cs`:

```csharp
using AcDream.App.Extensions.Overlay;
using AcDream.App.Rendering.Gpu;

namespace AcDream.App.Tests.Extensions;

public sealed class ImGuiVulkanRendererTests
{
    [Theory]
    [InlineData("AcDream.App.Overlay.imgui_overlay.vert.spv")]
    [InlineData("AcDream.App.Overlay.imgui_overlay.frag.spv")]
    public void EmbeddedShadersAreSpirv(string resource)
    {
        byte[] spirv = ImGuiVulkanRenderer.LoadSpirv(resource);
        Assert.True(spirv.Length > 16);
        Assert.Equal(0, spirv.Length % 4);
        Assert.Equal(0x07230203u, BitConverter.ToUInt32(spirv, 0));
    }

    [Fact]
    public void VertexLayoutMatchesImDrawVert()
    {
        GpuVertexLayout layout = ImGuiVulkanRenderer.VertexLayout;
        Assert.Equal(20u, layout.StrideBytes);
        Assert.Equal(GpuVertexFormat.Float2, layout.Attributes[0].Format);
        Assert.Equal(8u, layout.Attributes[1].OffsetBytes);
        Assert.Equal(GpuVertexFormat.UByte4Normalized, layout.Attributes[2].Format);
        Assert.Equal(16u, layout.Attributes[2].OffsetBytes);
        unsafe { Assert.Equal(20, sizeof(ImGuiNET.ImDrawVert)); }
    }
}
```

- [ ] **Step 6: Build, test, commit.** `~/.dotnet/dotnet build src/AcDream.App -nologo` (0 warnings), then `~/.dotnet/dotnet test tests/AcDream.App.Tests -nologo --filter "FullyQualifiedName~Extensions"`.

```bash
git add src/AcDream.App/Extensions/Overlay src/AcDream.App/AcDream.App.csproj tests/AcDream.App.Tests/Extensions
git commit -m "feat(overlay): Vulkan renderer for the ImGui overlay with embedded SPIR-V

Claude-Session: https://claude.ai/code/session_013qHSfUa7LKyjkciWdcioQr"
```

---

### Task 7: Wire the overlay into the client

**Goal:** The client creates the overlay with the window and hands it to extensions. It starts the overlay with the real renderer once the GPU is up and runs it as the dev-tools frame hook (including abort recovery). Its input capture blocks gameplay and the retail panels underneath.

**Files:**
- Create: `src/AcDream.App/Extensions/Overlay/OverlayInputGate.cs`, `src/AcDream.App/Extensions/Overlay/ImGuiInputFeed.cs`
- Modify (our file): `src/AcDream.App/Extensions/ClientExtensionHost.cs`
- Modify (upstream, line budget in brackets):
  - `src/AcDream.App/Rendering/GameWindow.cs` [3]
  - `src/AcDream.App/Composition/FrameRootComposition.cs` [4]
  - the `FrameRootDependencies` record [1] (`grep -rn "record FrameRootDependencies" src/AcDream.App`)
  - `src/AcDream.App/Input/InputCaptureSources.cs` [3]
  - `src/AcDream.App/UI/RetainedUiInputBinding.cs` [2]
- Modify: lock files for every RID of the projects restore changes
- Test: `tests/AcDream.App.Tests/Extensions/OverlayWiringTests.cs`

**Acceptance Criteria:**
- [ ] `dotnet build AcDream.slnx` has 0 errors.
- [ ] `DevToolsInputCaptureSource` reports the overlay's `WantsMouse`/`WantsKeyboard` when given them, and still returns false when constructed the old way.
- [ ] `OverlayInputGate.SuppressMouse` / `SuppressKeyboard` follow the overlay. The retained mouse and keyboard bindings skip callbacks while they are true.
- [ ] `FrameRootComposition` passes the overlay as `devTools` (both sites) and as the orchestrator's failure recovery.
- [ ] The existing call-order and recovery tests (`GameWindowRenderLeafCompositionTests`, `RenderFrameRecoveryIntegrationTests`, `PrivatePresentationRendererTests`, `RenderFramePreparationControllerTests`, `RenderFrameOrchestratorTests`) pass.
- [ ] The App.Tests failure names are identical to before this task (same comparison method as the host PoC's Task 5).
- [ ] `git diff upstream/main --stat` over the upstream files listed above totals about 15 or fewer inserted lines, plus `AcDream.App.csproj`, `Directory.Packages.props` and lock files.
- [ ] Lock files are regenerated for neutral and `linux-x64`, `osx-arm64`, `osx-x64` and `win-x64` wherever restore changed them.

**Verify:** `~/.dotnet/dotnet build AcDream.slnx -nologo -v q` → `0 Error(s)`; `~/.dotnet/dotnet test tests/AcDream.App.Tests -nologo --filter "FullyQualifiedName~Extensions|FullyQualifiedName~RenderFrame|FullyQualifiedName~PrivatePresentation|FullyQualifiedName~GameWindowRenderLeaf"` → all pass

**Steps:**

- [ ] **Step 1: The input gate and the feed.** `src/AcDream.App/Extensions/Overlay/OverlayInputGate.cs`:

```csharp
namespace AcDream.App.Extensions.Overlay;

/// <summary>
/// What the retail UI's raw input bindings check so a click or key meant for
/// an ImGui window doesn't also reach the retail panel underneath. Static so
/// the gate costs the upstream bindings one condition each.
/// </summary>
internal static class OverlayInputGate
{
    private static ImGuiOverlay? _overlay;

    public static bool SuppressMouse => _overlay?.WantsMouse == true;

    public static bool SuppressKeyboard => _overlay?.WantsKeyboard == true;

    public static void Bind(ImGuiOverlay? overlay) => _overlay = overlay;
}
```

`src/AcDream.App/Extensions/Overlay/ImGuiInputFeed.cs`:

```csharp
using System.Numerics;
using Silk.NET.Input;

namespace AcDream.App.Extensions.Overlay;

/// <summary>Subscribes the overlay's input queue to the window's mice and keyboards.</summary>
internal sealed class ImGuiInputFeed : IDisposable
{
    private readonly IInputContext _input;
    private readonly ImGuiInputQueue _queue;

    public ImGuiInputFeed(IInputContext input, ImGuiInputQueue queue)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        foreach (IMouse mouse in input.Mice)
        {
            mouse.MouseMove += OnMouseMove;
            mouse.MouseDown += OnMouseDown;
            mouse.MouseUp += OnMouseUp;
            mouse.Scroll += OnScroll;
        }

        foreach (IKeyboard keyboard in input.Keyboards)
        {
            keyboard.KeyDown += OnKeyDown;
            keyboard.KeyUp += OnKeyUp;
            keyboard.KeyChar += OnKeyChar;
        }
    }

    public void Dispose()
    {
        foreach (IMouse mouse in _input.Mice)
        {
            mouse.MouseMove -= OnMouseMove;
            mouse.MouseDown -= OnMouseDown;
            mouse.MouseUp -= OnMouseUp;
            mouse.Scroll -= OnScroll;
        }

        foreach (IKeyboard keyboard in _input.Keyboards)
        {
            keyboard.KeyDown -= OnKeyDown;
            keyboard.KeyUp -= OnKeyUp;
            keyboard.KeyChar -= OnKeyChar;
        }
    }

    private void OnMouseMove(IMouse mouse, Vector2 position) => _queue.MouseMove(position);

    private void OnMouseDown(IMouse mouse, MouseButton button) => _queue.MouseButton(button, true);

    private void OnMouseUp(IMouse mouse, MouseButton button) => _queue.MouseButton(button, false);

    private void OnScroll(IMouse mouse, ScrollWheel wheel) => _queue.Scroll(new Vector2(wheel.X, wheel.Y));

    private void OnKeyDown(IKeyboard keyboard, Key key, int scancode) => _queue.KeyChange(key, true);

    private void OnKeyUp(IKeyboard keyboard, Key key, int scancode) => _queue.KeyChange(key, false);

    private void OnKeyChar(IKeyboard keyboard, char value) => _queue.Character(value);
}
```

Add a `Start(IGpuDevice device, ICurrentGpuFrameSource frames, Silk.NET.Windowing.IWindow window, IInputContext input)` method to `ImGuiOverlay`. It wires the real renderer and input and returns the disposable that owns them, for the frame-root bindings:

```csharp
    /// <summary>
    /// Starts the overlay on the client's GPU, window and input. Returns what
    /// must be released before the GPU device goes away; null if it couldn't start.
    /// </summary>
    public IDisposable? Start(
        AcDream.App.Rendering.Gpu.IGpuDevice device,
        AcDream.App.Rendering.ICurrentGpuFrameSource frames,
        Silk.NET.Windowing.IWindow window,
        Silk.NET.Input.IInputContext input)
    {
        try
        {
            var renderer = new ImGuiVulkanRenderer(device, frames);
            Initialize(renderer, () => (
                new Vector2(window.Size.X, window.Size.Y),
                new Vector2(window.FramebufferSize.X, window.FramebufferSize.Y)));
            if (!IsAvailable)
                return null;
            var feed = new ImGuiInputFeed(input, Input);
            OverlayInputGate.Bind(this);
            return new Started(this, feed);
        }
        catch (Exception error)
        {
            _log($"[ImGui] overlay unavailable: {error.GetType().Name}: {error.Message}");
            return null;
        }
    }

    private sealed class Started(ImGuiOverlay overlay, ImGuiInputFeed feed) : IDisposable
    {
        public void Dispose()
        {
            OverlayInputGate.Bind(null);
            feed.Dispose();
            overlay.Dispose();
        }
    }
```

- [ ] **Step 2: The capture source (upstream, 3 lines).** In `src/AcDream.App/Input/InputCaptureSources.cs`, make `DevToolsInputCaptureSource` read optional providers:

```csharp
internal sealed class DevToolsInputCaptureSource
{
    private readonly Func<bool>? _wantsMouse;
    private readonly Func<bool>? _wantsKeyboard;

    public DevToolsInputCaptureSource(bool enabled, Func<bool>? wantsMouse = null, Func<bool>? wantsKeyboard = null)
    {
        _ = enabled;
        _wantsMouse = wantsMouse;
        _wantsKeyboard = wantsKeyboard;
    }

    public bool WantCaptureMouse => _wantsMouse?.Invoke() == true;

    public bool WantCaptureKeyboard => _wantsKeyboard?.Invoke() == true;
}
```

Keep the change as small as the language allows. The two property bodies and the constructor signature are the edit.

- [ ] **Step 3: The retained UI gate (upstream, 2 lines).** In `src/AcDream.App/UI/RetainedUiInputBinding.cs`, in the mouse binding's `Invoke` (about line 204) change the condition to:

```csharp
            if (Volatile.Read(ref _active) != 0 && !AcDream.App.Extensions.Overlay.OverlayInputGate.SuppressMouse)
```

and in the keyboard binding's `Invoke` (about line 396) to:

```csharp
            if (Volatile.Read(ref _active) != 0 && !AcDream.App.Extensions.Overlay.OverlayInputGate.SuppressKeyboard)
```

Known limit, recorded for the review: a button or key released while ImGui has the input doesn't reach the retail UI either. ImGui decides capture from the previous frame's hover, so this matches how other ImGui hosts behave.

- [ ] **Step 4: Our extension host.** In `src/AcDream.App/Extensions/ClientExtensionHost.cs`, change `Attach` to take and pass the overlay and the chat submitter:

```csharp
    public void Attach(
        GameRuntime runtime,
        IPluginCommandRegistry? commands,
        IClientOverlay? overlay = null,
        Func<string, bool>? submitChatCommand = null)
    {
        var context = new ClientExtensionContext
        {
            Runtime = runtime,
            Log = line => Serilog.Log.Information("{ExtensionLine}", line),
            RegisterChatCommand = (verb, handler) =>
                commands?.Register(verb, command => handler(command.Arguments))
                ?? new NoRegistration(),
            Overlay = overlay,
            SubmitChatCommand = submitChatCommand,
        };
```

(the rest unchanged). Add `using AcDream.ClientExtensions;` if missing.

- [ ] **Step 5: GameWindow and the frame root (upstream).**

GameWindow (`src/AcDream.App/Rendering/GameWindow.cs`):
1. Next to the `_extensions` field (about line 283):

```csharp
    private readonly AcDream.App.Extensions.Overlay.ImGuiOverlay _overlay = new(line => Serilog.Log.Information("{OverlayLine}", line));
```

2. Replace the `_extensions.Attach(...)` line (about line 537) with:

```csharp
        _extensions.Attach(_runtime, _automation?.PluginCommands, _overlay, text => _pluginSessionCommands?.SubmitChatText(text) == true);
```

3. In the `CompositeInputCaptureSource` construction (about line 561-563), change `new AcDream.App.Input.DevToolsInputCaptureSource(options.DevTools)` to:

```csharp
            new AcDream.App.Input.DevToolsInputCaptureSource(options.DevTools, () => _overlay.WantsMouse, () => _overlay.WantsKeyboard),
```

4. Where `FrameRootDependencies` is constructed (about line 1538-1580), add the overlay. Use whatever form fits the record: an `Overlay = _overlay,` initialiser, or a constructor argument. This is the frame root's 1 line.

FrameRootDependencies: add one member, `ImGuiOverlay? Overlay`, defaulting to null so other constructions (tests) still compile.

FrameRootComposition (`src/AcDream.App/Composition/FrameRootComposition.cs`):
- Before the `PrivatePresentationRenderer` construction (about line 766), add:

```csharp
        if (d.Overlay?.Start(host.GpuDevice, host.GpuFrameLifetime, d.Window, d.Input) is { } overlayGpu) bindings.Adopt("imgui-overlay", overlayGpu);
```

- Change `devTools: null` at about lines 777 and 780 to `devTools: d.Overlay`.
- Change `NullRenderFrameFailureRecovery.Instance` at about line 816 to `(IRenderFrameFailureRecovery?)d.Overlay ?? NullRenderFrameFailureRecovery.Instance`.

Check that `bindings.Adopt(string, IDisposable)` exists (`grep -n "Adopt(" src/AcDream.App/Composition/*.cs`) and use its real shape.

The ordering contract is pinned by `RenderFrameRecoveryIntegrationTests`: on teardown, extensions are disposed first (`CompleteShutdown`), then the frame-root bindings (overlay GPU), then the device. Don't move these calls.

- [ ] **Step 6: Lock files.** From a directory outside the repo:

```bash
R=~/code/OpenAC/OpenAC-Rynth
cd /tmp
for rid in "" linux-x64 osx-arm64 osx-x64 win-x64; do
  if [ -z "$rid" ]; then ~/.dotnet/dotnet restore "$R/AcDream.slnx" --force-evaluate -nologo -v q
  else ~/.dotnet/dotnet restore "$R/AcDream.slnx" --force-evaluate -r "$rid" -nologo -v q; fi
done
cd "$R" && git status --porcelain | grep lock.json
```

Keep only lock-file changes in projects that reference App, Rynth.Host or ScriptWindows, or that changed packages. Revert unrelated drift (`git checkout -- <file>`) and say so in the report, as the earlier host Task 5 did.

- [ ] **Step 7: Wiring tests.** `tests/AcDream.App.Tests/Extensions/OverlayWiringTests.cs`:

```csharp
using AcDream.App.Extensions.Overlay;
using AcDream.App.Input;

namespace AcDream.App.Tests.Extensions;

[Collection("Rynth")]
public sealed class OverlayWiringTests
{
    [Fact]
    public void DevToolsCaptureReadsTheProviders()
    {
        bool mouse = false, keyboard = false;
        var source = new DevToolsInputCaptureSource(false, () => mouse, () => keyboard);
        Assert.False(source.WantCaptureMouse);
        mouse = true;
        keyboard = true;
        Assert.True(source.WantCaptureMouse);
        Assert.True(source.WantCaptureKeyboard);
        Assert.False(new DevToolsInputCaptureSource(true).WantCaptureMouse);
    }

    [Fact]
    public void TheGateFollowsTheBoundOverlay()
    {
        var overlay = new ImGuiOverlay(_ => { });
        OverlayInputGate.Bind(overlay);
        Assert.False(OverlayInputGate.SuppressMouse);
        OverlayInputGate.Bind(null);
        Assert.False(OverlayInputGate.SuppressKeyboard);
        overlay.Dispose();
    }
}
```

- [ ] **Step 8: Build, run the named suites, compare App.Tests failures, check the footprint.**

```bash
cd ~/code/OpenAC/OpenAC-Rynth
~/.dotnet/dotnet build AcDream.slnx -nologo -v q 2>&1 | tail -3
~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo
~/.dotnet/dotnet test tests/AcDream.App.Tests -nologo --filter "FullyQualifiedName~Extensions|FullyQualifiedName~RenderFrame|FullyQualifiedName~PrivatePresentation|FullyQualifiedName~GameWindowRenderLeaf"
mkdir -p /tmp/oac
TMPDIR=/tmp/oac ~/.dotnet/dotnet test tests/AcDream.App.Tests -nologo 2>&1 | grep -E "^\s+Failed " | sort > /tmp/oac-app-after.txt
git stash -u && git checkout -q feat/rynth-host-poc
TMPDIR=/tmp/oac ~/.dotnet/dotnet test tests/AcDream.App.Tests -nologo 2>&1 | grep -E "^\s+Failed " | sort > /tmp/oac-app-before.txt
git checkout -q feat/rynth-imgui && git stash pop
diff /tmp/oac-app-before.txt /tmp/oac-app-after.txt
git diff upstream/main --stat -- src/AcDream.App/Rendering/GameWindow.cs src/AcDream.App/Composition src/AcDream.App/Input/InputCaptureSources.cs src/AcDream.App/UI/RetainedUiInputBinding.cs
```

Expected: 0 build errors, the named suites pass, `diff` prints nothing new, and the stat shows only the planned lines. The before baseline is `feat/rynth-host-poc`, the branch this builds on. If `git stash` has nothing to stash, skip the pop.

- [ ] **Step 9: Commit.**

```bash
git add src/AcDream.App tests/AcDream.App.Tests
git add $(git status --porcelain | awk '/lock.json$/ {print $2}')
git commit -m "feat(overlay): run the ImGui overlay as the client's dev-tools frame hook; input capture

Claude-Session: https://claude.ai/code/session_013qHSfUa7LKyjkciWdcioQr"
```

---

### Task 8: Offline client run (agent) and the user's visual check

**Goal:** In the real client on macOS, run offline with no server, the overlay starts, RynthOracle loads, `/ro` opens its window and ImGui frames carry vertices, all without errors. The user then confirms the window looks and behaves right.

**Files:**
- None (no code). Results go in the session report.

**Acceptance Criteria:**
- [ ] The offline probe run logs `[ImGui] overlay started (ImGui 1.91.6)`, `[Rynth] RynthCore.Plugin.RynthOracle loaded.` and, after `/ro`, at least one `[ImGui] frame: lists=` line with `vtx=` greater than 0.
- [ ] The log has no `[ImGui] overlay stopped`, `overlay unavailable` or `frame callback removed` lines and no unhandled exception. The client exits by `close-client`, not by the timeout.
- [ ] User check, once available: typing `/ro` shows the Oracle window. It can be dragged, its tab buttons respond, and clicks or typing on it don't move the character or act on retail panels underneath.

**Verify:** `grep -E "\[ImGui\]|RynthOracle" <client.log>` → the lines above

**Steps:**

- [ ] **Step 1: Build into a scratch folder** (so the user's dev build is untouched):

```bash
S=$(mktemp -d)
mkdir -p "$S/root/plugins" "$S/cwd" "$S/shots"
cd ~/code/OpenAC/OpenAC-Rynth && ~/.dotnet/dotnet build src/AcDream.App -c Release --nologo -v q --artifacts-path "$S/artifacts"
cat > "$S/probe.txt" <<'EOF'
sleep 12000
command /ro
sleep 6000
close-client
EOF
```

- [ ] **Step 2: Run offline (no `ACDREAM_LIVE`), with a 3-minute cap:**

```bash
cd "$S/cwd"
DYLD_LIBRARY_PATH="$(brew --prefix)/lib" VK_DRIVER_FILES="$(brew --prefix)/etc/vulkan/icd.d/MoltenVK_icd.json" \
ACDREAM_ROOT_DIR="$S/root" ACDREAM_DAT_DIR="$HOME/AsheronsCall" \
ACDREAM_PAK_PATH="$HOME/Library/Application Support/OpenAC/data/pak/acdream.pak" \
ACDREAM_UI_PROBE_SCRIPT="$S/probe.txt" ACDREAM_AUTOMATION_ARTIFACT_DIR="$S/shots" ACDREAM_NO_AUDIO=1 \
~/.dotnet/dotnet "$S/artifacts/bin/AcDream.App/release/AcDream.App.dll" "$HOME/AsheronsCall" > "$S/client.log" 2>&1 &
pid=$!; for i in $(seq 1 180); do kill -0 $pid 2>/dev/null || break; sleep 1; done; kill $pid 2>/dev/null
grep -nE "\[ImGui\]|\[Rynth\]|Exception|UI-PROBE" "$S/client.log" | head -40
```

Run this in the background, wait for it to exit, then read the log.

If the probe's `command /ro` isn't accepted offline (the log shows a probe error), report it. The overlay start line and the Oracle load line still count. Then ask the controller whether to add a probe-free check.

- [ ] **Step 3: Report.** Quote the matching lines. The visual check waits for the user: tell them the build path and the steps (`/ro`, drag, click a tab, click on the window over the game). Don't launch a client against a server.
