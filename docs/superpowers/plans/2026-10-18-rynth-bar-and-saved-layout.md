# Rynth Launcher Bar and Saved Window Layout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Script windows (RynthOracle and later display plugins) come back where the player left them, at the same size, and reopen if left open; a small launcher bar opens and closes them; all of it saved once per install.

**Architecture:** The ScriptWindows wrapper project gains an `IPanelLayout` interface that our `ImGuiPanelHost` stand-in uses to restore and record each window's rectangle and open state. `AcDream.Rynth.Host` gets `RynthLayoutStore` (a JSON file in a folder the client hands the extension) and `RynthBar` (an ImGui strip built on the registry's `BarButtons`/`BarItems`). The client passes the folder through one new optional `ClientExtensionContext.StateDirectory`.

**Tech Stack:** .NET 10 (`~/.dotnet/dotnet`), ImGui.NET 1.91.6.1, System.Text.Json.Nodes, xunit v3 (VSTest runner: `--filter "FullyQualifiedName~X"`).

**Spec:** `docs/superpowers/specs/2026-10-18-rynth-bar-and-saved-layout-design.md`

## Global Constraints

- Repo `~/code/OpenAC/OpenAC-Rynth`, branch `feat/rynth-imgui`. Do not push.
- Use `~/.dotnet/dotnet`. New files only in the projects named here; no new projects.
- Never edit anything inside `external/RynthCore` or `external/RynthSuite`.
- Upstream files change only where Task 1 says: `src/AcDream.ClientExtensions/ClientExtensionContext.cs`, `src/AcDream.App/Extensions/ClientExtensionHost.cs`, and one line in `src/AcDream.App/Rendering/GameWindow.cs`.
- `AcDream.Rynth.Host` builds with warnings as errors and analyzers on: new code there must build clean. `AcDream.Rynth.ScriptWindows` has warnings off, but write it to the same standard.
- Layout values are logical units: pixels divided by `EngineFrameController.FontScale` (1 today).
- File: `<StateDirectory>/rynth-layout.json`, format `{"version":1,"bar":{"x":..,"y":..},"windows":{"<panel key>":{"x":..,"y":..,"w":..,"h":..,"open":true}}}`. `x/y/w/h` are absent for a window the player never placed.
- Disk errors are logged, never thrown into a frame.
- Every `ImGui.Begin` has its `End`, even when the body throws.
- Never pass script- or plugin-supplied text (titles) as an ImGui format string: use `TextUnformatted` (tooltips via `BeginTooltip`/`TextUnformatted`/`EndTooltip`).
- Known unrelated test failures exist in `AcDream.Core.Tests`, `AcDream.Core.Net.Tests` and `AcDream.App.Tests` (missing dat files, live-server tests). Run App tests with a filter, as each task's Verify shows. `AcDream.Rynth.Host.Tests` must be fully green.
- Never launch a client against a server without asking the user first; only the local ACE server 10.10.20.20, never Dreamweave; never print passwords.

**User decisions (already made):**
- "Window bar" = RynthCore's overlay launcher bar (buttons that open plugin windows), not a per-window title bar.
- Layout is saved per install, not per character.
- Saved per window: position, size and open state.
- Approach A: our own layout file and our own small bar (not ImGui's ini, not a port of `ImGuiBar.cs`).
- Icons stay placeholders here (first letter); real icons are a later piece of work.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/AcDream.ClientExtensions/ClientExtensionContext.cs` (modify) | `StateDirectory` property |
| `src/AcDream.App/Extensions/ClientExtensionHost.cs` (modify) | passes the folder in |
| `src/AcDream.App/Rendering/GameWindow.cs` (modify, 1 line) | names the folder |
| `src/AcDream.Rynth.ScriptWindows/StandIns/PanelLayout.cs` (create) | `PanelRect`, `PanelPlacement`, `IPanelLayout` |
| `src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiPanelHost.cs` (modify) | restore/record rectangle and open state, restore-open |
| `src/AcDream.Rynth.Host/RynthLayoutStore.cs` (create) | the JSON file |
| `src/AcDream.Rynth.Host/RynthBar.cs` (create) | the launcher bar |
| `src/AcDream.Rynth.Host/RynthClientExtension.cs` (modify) | wires store, layout hook and bar |
| `src/AcDream.Rynth.Host/AcDream.Rynth.Host.csproj` (modify) | `InternalsVisibleTo` for its tests |
| `tests/AcDream.Rynth.Host.Tests/FakePanelLayout.cs` (create) | recording `IPanelLayout` |
| `tests/AcDream.Rynth.Host.Tests/RynthLayoutStoreTests.cs` (create) | store tests |
| `tests/AcDream.Rynth.Host.Tests/PanelHostLayoutTests.cs` (create) | panel host layout tests |
| `tests/AcDream.Rynth.Host.Tests/ScriptWindowPipelineTests.cs` (modify) | restore-open with real Oracle |
| `tests/AcDream.Rynth.Host.Tests/RynthBarTests.cs` (create) | bar tests |
| `tests/AcDream.App.Tests/Extensions/RynthOverlayExtensionTests.cs` (modify) | wiring test |

---

### Task 1: The extension state folder

**Goal:** Extensions receive a per-install folder for their own files through `ClientExtensionContext.StateDirectory`, and the client sets it to `PluginFilesDirectory("rynth")`.

**Files:**
- Modify: `src/AcDream.ClientExtensions/ClientExtensionContext.cs`
- Modify: `src/AcDream.App/Extensions/ClientExtensionHost.cs`
- Modify: `src/AcDream.App/Rendering/GameWindow.cs:538`

**Acceptance Criteria:**
- [ ] `ClientExtensionContext` has `public string? StateDirectory { get; init; }` with a doc comment.
- [ ] `ClientExtensionHost.Attach` takes `string? stateDirectory = null` and sets `StateDirectory = stateDirectory`.
- [ ] `GameWindow` passes `stateDirectory: _applicationPaths.PluginFilesDirectory("rynth")`.
- [ ] `dotnet build src/AcDream.App` succeeds with 0 errors; the upstream diff is these three files only.

**Verify:** `~/.dotnet/dotnet build src/AcDream.App -nologo -v q` → `0 Error(s)`

**Steps:**

- [ ] **Step 1: Add the property** to `ClientExtensionContext`, after `SubmitChatCommand`:

```csharp
    /// <summary>
    /// A folder the extension may keep its own files in (one per install; the
    /// extension creates it when it first writes). Null: don't save anything.
    /// </summary>
    public string? StateDirectory { get; init; }
```

- [ ] **Step 2: Pass it through `ClientExtensionHost.Attach`.** Change the signature and the initializer:

```csharp
    public void Attach(
        GameRuntime runtime,
        IPluginCommandRegistry? commands,
        IClientOverlay? overlay = null,
        Func<string, bool>? submitChatCommand = null,
        string? stateDirectory = null)
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
            StateDirectory = stateDirectory,
        };
```

- [ ] **Step 3: Name the folder in `GameWindow`** (line 538). Replace

```csharp
        _extensions.Attach(_runtime, _automation?.PluginCommands, _overlay, text => _pluginSessionCommands?.SubmitChatText(text) == true);
```

with

```csharp
        _extensions.Attach(_runtime, _automation?.PluginCommands, _overlay, text => _pluginSessionCommands?.SubmitChatText(text) == true, stateDirectory: _applicationPaths.PluginFilesDirectory("rynth"));
```

- [ ] **Step 4: Build.** Run `~/.dotnet/dotnet build src/AcDream.App -nologo -v q`. Expected: `0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.ClientExtensions/ClientExtensionContext.cs src/AcDream.App/Extensions/ClientExtensionHost.cs src/AcDream.App/Rendering/GameWindow.cs
git commit -m "feat(extensions): a per-install state folder for client extensions"
```

---

### Task 2: Layout types and `RynthLayoutStore`

**Goal:** A per-install JSON store of each window's rectangle and open state plus the bar position, behind the `IPanelLayout` interface the panel host will use.

**Files:**
- Create: `src/AcDream.Rynth.ScriptWindows/StandIns/PanelLayout.cs`
- Create: `src/AcDream.Rynth.Host/RynthLayoutStore.cs`
- Modify: `src/AcDream.Rynth.Host/AcDream.Rynth.Host.csproj` (add `InternalsVisibleTo`)
- Test: `tests/AcDream.Rynth.Host.Tests/RynthLayoutStoreTests.cs`

**Acceptance Criteria:**
- [ ] Saving a rectangle, an open flag and a bar position, then constructing a new store on the same folder, reads all three back.
- [ ] A missing file gives an empty layout; a missing folder is created on the first save.
- [ ] A file that doesn't parse logs one line containing `layout file unreadable`, gives an empty layout, and is left byte-for-byte unchanged by construction.
- [ ] A store with a null folder never touches the disk.
- [ ] `SetOpen` keeps the rectangle and `SetRect` keeps the open flag.
- [ ] Saves go through `rynth-layout.json.tmp` and a move; no `.tmp` file is left behind.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo --filter "FullyQualifiedName~RynthLayoutStoreTests"` → all pass

**Steps:**

- [ ] **Step 1: Create the layout types** in `src/AcDream.Rynth.ScriptWindows/StandIns/PanelLayout.cs`:

```csharp
using System.Numerics;

namespace RynthCore.Engine.ImGuiBackend;

/// <summary>A window's rectangle, in logical units.</summary>
internal readonly record struct PanelRect(Vector2 Pos, Vector2 Size);

/// <summary>What is kept for one window: its rectangle once the player placed it (null until then) and whether it was left open.</summary>
internal readonly record struct PanelPlacement(PanelRect? Rect, bool Open);

/// <summary>Where the panel host keeps window placement between sessions.</summary>
internal interface IPanelLayout
{
    /// <summary>The saved placement, or null when nothing is saved for the key.</summary>
    PanelPlacement? Get(string key);

    /// <summary>Records a rectangle the player gave the window (keeps its open flag).</summary>
    void SetRect(string key, PanelRect rect);

    /// <summary>Records whether the window is open (keeps its rectangle).</summary>
    void SetOpen(string key, bool open);
}
```

- [ ] **Step 2: Let the host's tests see its internals.** In `src/AcDream.Rynth.Host/AcDream.Rynth.Host.csproj`, add before `</Project>`:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="AcDream.Rynth.Host.Tests" />
    <InternalsVisibleTo Include="AcDream.App.Tests" />
  </ItemGroup>
```

- [ ] **Step 3: Write the failing tests** in `tests/AcDream.Rynth.Host.Tests/RynthLayoutStoreTests.cs`:

```csharp
using System.Numerics;
using AcDream.Rynth.Host;
using RynthCore.Engine.ImGuiBackend;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthLayoutStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rynth-layout-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _log = new();

    private string FilePath => Path.Combine(_dir, RynthLayoutStore.FileName);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void WhatIsSavedIsReadBackByTheNextStore()
    {
        var first = new RynthLayoutStore(_dir, _log.Add);
        first.SetRect("script:A/Main", new PanelRect(new Vector2(30, 40), new Vector2(500, 300)));
        first.SetOpen("script:A/Main", true);
        first.SetOpen("script:A/Other", false);
        first.SetBarPosition(new Vector2(12, 90));

        var second = new RynthLayoutStore(_dir, _log.Add);

        Assert.Equal(new PanelPlacement(new PanelRect(new Vector2(30, 40), new Vector2(500, 300)), true), second.Get("script:A/Main"));
        Assert.Equal(new PanelPlacement(null, false), second.Get("script:A/Other"));
        Assert.Equal(new Vector2(12, 90), second.BarPosition);
        Assert.False(File.Exists(FilePath + ".tmp"));
        Assert.Empty(_log);
    }

    [Fact]
    public void AMissingFileIsAnEmptyLayout()
    {
        var store = new RynthLayoutStore(_dir, _log.Add);

        Assert.Null(store.Get("script:A/Main"));
        Assert.Null(store.BarPosition);
        Assert.False(Directory.Exists(_dir));
        Assert.Empty(_log);
    }

    [Fact]
    public void AnUnreadableFileIsLoggedOnceAndLeftAlone()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ not json");

        var store = new RynthLayoutStore(_dir, _log.Add);

        Assert.Null(store.Get("script:A/Main"));
        Assert.Single(_log, line => line.Contains("layout file unreadable", StringComparison.Ordinal));
        Assert.Equal("{ not json", File.ReadAllText(FilePath));
    }

    [Fact]
    public void WithoutAFolderNothingIsWritten()
    {
        var store = new RynthLayoutStore(null, _log.Add);
        store.SetOpen("script:A/Main", true);
        store.SetBarPosition(new Vector2(1, 2));

        Assert.Equal(new PanelPlacement(null, true), store.Get("script:A/Main"));
        Assert.False(Directory.Exists(_dir));
    }

    [Fact]
    public void OpenAndRectAreKeptIndependently()
    {
        var store = new RynthLayoutStore(_dir, _log.Add);
        var rect = new PanelRect(new Vector2(5, 6), new Vector2(200, 100));

        store.SetOpen("script:A/Main", true);
        store.SetRect("script:A/Main", rect);
        Assert.Equal(new PanelPlacement(rect, true), store.Get("script:A/Main"));

        store.SetOpen("script:A/Main", false);
        Assert.Equal(new PanelPlacement(rect, false), store.Get("script:A/Main"));
    }
}
```

- [ ] **Step 4: Run them to see them fail.** Run the Verify command. Expected: build error, `RynthLayoutStore` does not exist.

- [ ] **Step 5: Write `src/AcDream.Rynth.Host/RynthLayoutStore.cs`:**

```csharp
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using RynthCore.Engine.ImGuiBackend;

namespace AcDream.Rynth.Host;

/// <summary>
/// The script windows' and the bar's saved layout: one JSON file per install
/// (<see cref="FileName"/> in the folder the client gives the extension).
/// Without a folder the layout lives in memory only. Every change is written
/// at once, since changes come once per finished drag, resize, open or close.
/// A disk error is logged and never thrown.
/// </summary>
internal sealed class RynthLayoutStore : IPanelLayout
{
    internal const string FileName = "rynth-layout.json";
    private const int Version = 1;
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly object _gate = new();
    private readonly string? _path;
    private readonly Action<string> _log;
    private readonly Dictionary<string, PanelPlacement> _windows = new(StringComparer.OrdinalIgnoreCase);
    private Vector2? _bar;

    public RynthLayoutStore(string? directory, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
        if (string.IsNullOrWhiteSpace(directory))
            return;
        _path = Path.Combine(directory, FileName);
        Load();
    }

    /// <summary>Where the player left the bar, or null.</summary>
    public Vector2? BarPosition
    {
        get { lock (_gate) return _bar; }
    }

    public void SetBarPosition(Vector2 pos)
    {
        lock (_gate)
        {
            if (_bar == pos)
                return;
            _bar = pos;
            SaveLocked();
        }
    }

    public PanelPlacement? Get(string key)
    {
        lock (_gate) return _windows.TryGetValue(key, out PanelPlacement placement) ? placement : null;
    }

    public void SetRect(string key, PanelRect rect)
    {
        lock (_gate)
        {
            bool known = _windows.TryGetValue(key, out PanelPlacement prior);
            if (known && prior.Rect == rect)
                return;
            _windows[key] = prior with { Rect = rect };
            SaveLocked();
        }
    }

    public void SetOpen(string key, bool open)
    {
        lock (_gate)
        {
            bool known = _windows.TryGetValue(key, out PanelPlacement prior);
            if (known && prior.Open == open)
                return;
            _windows[key] = prior with { Open = open };
            SaveLocked();
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
                return;
            JsonNode? root = JsonNode.Parse(File.ReadAllText(_path));
            if (root?["bar"] is JsonObject bar)
                _bar = new Vector2(Read(bar, "x"), Read(bar, "y"));
            if (root?["windows"] is JsonObject windows)
            {
                foreach ((string key, JsonNode? node) in windows)
                {
                    if (node is not JsonObject w)
                        continue;
                    PanelRect? rect = w.ContainsKey("w")
                        ? new PanelRect(new Vector2(Read(w, "x"), Read(w, "y")), new Vector2(Read(w, "w"), Read(w, "h")))
                        : null;
                    _windows[key] = new PanelPlacement(rect, w["open"]?.GetValue<bool>() ?? false);
                }
            }
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException
            or InvalidOperationException or FormatException)
        {
            _windows.Clear();
            _bar = null;
            _log($"[Rynth] layout file unreadable ({_path}): {error.Message}");
        }
    }

    private static float Read(JsonObject node, string name) => node[name]?.GetValue<float>() ?? 0f;

    private void SaveLocked()
    {
        if (_path is null)
            return;
        var windows = new JsonObject();
        foreach ((string key, PanelPlacement placement) in _windows)
        {
            var w = new JsonObject();
            if (placement.Rect is { } rect)
            {
                w["x"] = rect.Pos.X;
                w["y"] = rect.Pos.Y;
                w["w"] = rect.Size.X;
                w["h"] = rect.Size.Y;
            }

            w["open"] = placement.Open;
            windows[key] = w;
        }

        var root = new JsonObject { ["version"] = Version };
        if (_bar is { } bar)
            root["bar"] = new JsonObject { ["x"] = bar.X, ["y"] = bar.Y };
        root["windows"] = windows;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, root.ToJsonString(Indented));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _log($"[Rynth] layout not saved ({_path}): {error.Message}");
        }
    }
}
```

- [ ] **Step 6: Run the tests.** Run the Verify command. Expected: 5 passed. If an analyzer rule fails the build, fix the code (don't suppress it) unless the rule conflicts with the repo's existing pattern (e.g. the host's own `catch (Exception)` guards); then match that pattern.

- [ ] **Step 7: Commit**

```bash
git add src/AcDream.Rynth.ScriptWindows/StandIns/PanelLayout.cs src/AcDream.Rynth.Host/RynthLayoutStore.cs src/AcDream.Rynth.Host/AcDream.Rynth.Host.csproj tests/AcDream.Rynth.Host.Tests/RynthLayoutStoreTests.cs
git commit -m "feat(rynth): a per-install layout store for script windows and the bar"
```

---

### Task 3: The panel host restores and records placement

**Goal:** `ImGuiPanelHost` places a window at its saved rectangle when it opens, saves the rectangle once per finished player drag or resize, records open and close, and reopens windows saved as open the way the bar does.

**Files:**
- Modify: `src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiPanelHost.cs`
- Create: `tests/AcDream.Rynth.Host.Tests/FakePanelLayout.cs`
- Create: `tests/AcDream.Rynth.Host.Tests/PanelHostLayoutTests.cs`
- Modify: `tests/AcDream.Rynth.Host.Tests/ScriptWindowPipelineTests.cs`

**Acceptance Criteria:**
- [ ] A window with a saved rectangle opens at that position and size on its first frame.
- [ ] A saved position that would put the window off the display is clamped onto it (display 1280×800, saved (5000,5000) size (300,220) → (980,580)).
- [ ] Moving a window (body calls `ImGui.SetWindowPos`) with the mouse up calls `SetRect` exactly once with the new rectangle; with the left button held it calls nothing until the button is released.
- [ ] A plugin `RequestPos` is applied and does not call `SetRect`.
- [ ] `Open` calls `SetOpen(key, true)`, `Close` calls `SetOpen(key, false)`, `Unregister` calls neither.
- [ ] A key saved as open, when first registered, is passed once to `RestoreOpener` on the next `DrawAll`; not at all if the window is already open by then.
- [ ] With the real Oracle plugin and its window saved as open, after ticks and one `DrawAll` the window is open and the plugin reports `visible=True`.
- [ ] With no `Layout` set, existing `ImGuiPanelHostTests` and `ScriptWindowPipelineTests` still pass.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo` → all pass

**Steps:**

- [ ] **Step 1: Create the recording fake** `tests/AcDream.Rynth.Host.Tests/FakePanelLayout.cs`:

```csharp
using RynthCore.Engine.ImGuiBackend;

namespace AcDream.Rynth.Host.Tests;

/// <summary>An in-memory IPanelLayout that records every call.</summary>
internal sealed class FakePanelLayout : IPanelLayout
{
    public Dictionary<string, PanelPlacement> Saved { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(string Key, PanelRect Rect)> RectCalls { get; } = new();
    public List<(string Key, bool Open)> OpenCalls { get; } = new();

    public PanelPlacement? Get(string key) => Saved.TryGetValue(key, out PanelPlacement p) ? p : null;

    public void SetRect(string key, PanelRect rect)
    {
        RectCalls.Add((key, rect));
        Saved.TryGetValue(key, out PanelPlacement prior);
        Saved[key] = prior with { Rect = rect };
    }

    public void SetOpen(string key, bool open)
    {
        OpenCalls.Add((key, open));
        Saved.TryGetValue(key, out PanelPlacement prior);
        Saved[key] = prior with { Open = open };
    }
}
```

- [ ] **Step 2: Write the failing tests** in `tests/AcDream.Rynth.Host.Tests/PanelHostLayoutTests.cs`:

```csharp
using System.Numerics;
using ImGuiNET;
using RynthCore.Engine.ImGuiBackend;

namespace AcDream.Rynth.Host.Tests;

public sealed class PanelHostLayoutTests : IDisposable
{
    private const string Key = "script:test";
    private readonly HeadlessImGui _imgui = new();
    private readonly FakePanelLayout _layout = new();

    public PanelHostLayoutTests()
    {
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Layout = _layout;
    }

    public void Dispose()
    {
        ImGuiPanelHost.Reset();
        _imgui.Dispose();
    }

    private static PanelSpec Spec => new(new Vector2(200, 100), new Vector2(50, 50), DefaultPos: new Vector2(10, 10));

    private MovingPanel RegisterAndOpen()
    {
        var panel = new MovingPanel();
        ImGuiPanelHost.RegisterDynamic(Key, "Test", Spec, () => panel);
        ImGuiPanelHost.Open(Key);
        return panel;
    }

    [Fact]
    public void ASavedRectanglePlacesTheWindowOnItsFirstFrame()
    {
        _layout.Saved[Key] = new PanelPlacement(new PanelRect(new Vector2(40, 50), new Vector2(300, 220)), false);
        MovingPanel panel = RegisterAndOpen();

        _imgui.Frame(ImGuiPanelHost.DrawAll);

        Assert.Equal(new Vector2(40, 50), panel.Pos);
        Assert.Equal(new Vector2(300, 220), panel.Size);
        Assert.Empty(_layout.RectCalls);
    }

    [Fact]
    public void ASavedPositionOffTheDisplayIsClampedOntoIt()
    {
        _layout.Saved[Key] = new PanelPlacement(new PanelRect(new Vector2(5000, 5000), new Vector2(300, 220)), false);
        MovingPanel panel = RegisterAndOpen();

        _imgui.Frame(ImGuiPanelHost.DrawAll);

        Assert.Equal(new Vector2(980, 580), panel.Pos);
    }

    [Fact]
    public void APlayerMoveIsSavedOnceTheMouseIsUp()
    {
        MovingPanel panel = RegisterAndOpen();
        _imgui.Frame(ImGuiPanelHost.DrawAll);

        ImGui.GetIO().AddMouseButtonEvent(0, true);
        panel.MoveTo = new Vector2(300, 200);
        _imgui.Frame(ImGuiPanelHost.DrawAll);
        Assert.Empty(_layout.RectCalls);

        ImGui.GetIO().AddMouseButtonEvent(0, false);
        _imgui.Frame(ImGuiPanelHost.DrawAll);
        _imgui.Frame(ImGuiPanelHost.DrawAll);

        (string key, PanelRect rect) = Assert.Single(_layout.RectCalls);
        Assert.Equal(Key, key);
        Assert.Equal(new PanelRect(new Vector2(300, 200), new Vector2(200, 100)), rect);
    }

    [Fact]
    public void APluginRequestIsAppliedButNotSaved()
    {
        MovingPanel panel = RegisterAndOpen();
        _imgui.Frame(ImGuiPanelHost.DrawAll);

        ImGuiPanelHost.RequestPos(Key, new Vector2(400, 300));
        _imgui.Frame(ImGuiPanelHost.DrawAll);
        _imgui.Frame(ImGuiPanelHost.DrawAll);

        Assert.Equal(new Vector2(400, 300), panel.Pos);
        Assert.Empty(_layout.RectCalls);
    }

    [Fact]
    public void OpenAndCloseAreRecordedButUnregisterIsNot()
    {
        RegisterAndOpen();
        ImGuiPanelHost.Close(Key);
        ImGuiPanelHost.Open(Key);
        ImGuiPanelHost.Unregister(Key);

        Assert.Equal([(Key, true), (Key, false), (Key, true)], _layout.OpenCalls);
    }

    [Fact]
    public void AWindowSavedAsOpenIsReopenedOnceOnTheNextDraw()
    {
        var reopened = new List<string>();
        ImGuiPanelHost.RestoreOpener = reopened.Add;
        _layout.Saved[Key] = new PanelPlacement(null, true);
        ImGuiPanelHost.RegisterDynamic(Key, "Test", Spec, () => new MovingPanel());

        _imgui.Frame(ImGuiPanelHost.DrawAll);
        _imgui.Frame(ImGuiPanelHost.DrawAll);

        Assert.Equal([Key], reopened);
    }

    [Fact]
    public void AWindowAlreadyOpenIsNotReopened()
    {
        var reopened = new List<string>();
        ImGuiPanelHost.RestoreOpener = reopened.Add;
        _layout.Saved[Key] = new PanelPlacement(null, true);
        RegisterAndOpen();

        _imgui.Frame(ImGuiPanelHost.DrawAll);

        Assert.Empty(reopened);
    }

    private sealed class MovingPanel : IImGuiPanel
    {
        public Vector2? MoveTo { get; set; }
        public Vector2 Pos { get; private set; }
        public Vector2 Size { get; private set; }

        public void Draw()
        {
            if (MoveTo is { } to)
            {
                ImGui.SetWindowPos(to);
                MoveTo = null;
            }

            Pos = ImGui.GetWindowPos();
            Size = ImGui.GetWindowSize();
            ImGui.TextUnformatted("body");
        }
    }
}
```

- [ ] **Step 3: Run them to see them fail.** Run `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo --filter "FullyQualifiedName~PanelHostLayoutTests"`. Expected: build errors, `ImGuiPanelHost.Layout` and `RestoreOpener` don't exist.

- [ ] **Step 4: Change `ImGuiPanelHost`.** Make these edits in `src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiPanelHost.cs`.

Add `using RynthCore.Engine.UI.ScriptWindows;` to the usings.

Extend the class summary's first sentence to say it also restores and records each window's placement through `Layout`.

Add three fields to `Entry`:

```csharp
        /// <summary>Apply the saved rectangle on the next draw (set by Open).</summary>
        public bool PlacePending;
        /// <summary>Saved as open when first registered: reopen it on the next draw, as the bar would.</summary>
        public bool RestoreOpen;
        /// <summary>The rectangle last seen (logical units); a player change from it is saved.</summary>
        public PanelRect? Baseline;
```

Add after the `Log` property:

```csharp
    /// <summary>Where window placement is kept between sessions; null keeps nothing.</summary>
    internal static IPanelLayout? Layout { get; set; }

    /// <summary>Reopens a window that was left open (tests replace it). Default: as the bar does.</summary>
    internal static Action<string> RestoreOpener { get; set; } = OpenLikeTheBar;

    /// <summary>
    /// Opens a script window the way RynthCore's bar does, so the plugin gets a
    /// Visibility event and its own earlier "hidden" flag is stale.
    /// </summary>
    private static void OpenLikeTheBar(string key)
    {
        if (key.StartsWith(ScriptWindowRegistry.PanelKeyPrefix, StringComparison.OrdinalIgnoreCase))
            ScriptWindowRegistry.PlayerSetVisible(key.Substring(ScriptWindowRegistry.PanelKeyPrefix.Length), true);
        else
            Open(key);
    }
```

In `RegisterDynamic`, mark a new key that was saved as open:

```csharp
            if (!_entries.TryGetValue(key, out Entry? entry))
            {
                _entries[key] = entry = new Entry();
                entry.RestoreOpen = Layout?.Get(key) is { Open: true };
            }
```

In `Open`, inside the lock after `entry.Failures = 0;`, add:

```csharp
            entry.PlacePending = true;
            entry.Baseline = null;
            entry.RestoreOpen = false;
```

and after the lock, before `Guard(key + " OnShown", shown.OnShown);`, add `Layout?.SetOpen(key, true);`. (The early `return` when the factory throws stays before this, so a failed open isn't recorded.)

In `Close`, after the lock and before the `if (hidden is not null)` line, add `Layout?.SetOpen(key, false);`.

`Unregister` doesn't change.

Change `Reset` to also drop the hookups:

```csharp
    /// <summary>Forgets every window and the layout hookups (tests, and host shutdown).</summary>
    internal static void Reset()
    {
        lock (_gate) _entries.Clear();
        Layout = null;
        RestoreOpener = OpenLikeTheBar;
    }
```

Replace `DrawAll` with:

```csharp
    /// <summary>Draws every open window. Call between ImGui.NewFrame and ImGui.Render.</summary>
    internal static void DrawAll()
    {
        List<string> restore = new();
        lock (_gate)
        {
            foreach (KeyValuePair<string, Entry> pair in _entries)
            {
                if (!pair.Value.RestoreOpen)
                    continue;
                pair.Value.RestoreOpen = false;
                if (!pair.Value.Open)
                    restore.Add(pair.Key);
            }
        }

        // Outside the lock: the registry opens the window through Open.
        foreach (string key in restore)
            Guard(key + " restore", () => RestoreOpener(key));

        List<(string Key, Entry Entry, Vector2? Size, Vector2? Pos, PanelRect? Saved)> open = new();
        lock (_gate)
        {
            foreach (KeyValuePair<string, Entry> pair in _entries)
            {
                Entry e = pair.Value;
                if (!e.Open)
                    continue;
                PanelRect? saved = e.PlacePending ? Layout?.Get(pair.Key)?.Rect : null;
                open.Add((pair.Key, e, e.RequestedSize, e.RequestedPos, saved));
                e.RequestedSize = null;
                e.RequestedPos = null;
                e.PlacePending = false;
            }
        }

        foreach ((string key, Entry entry, Vector2? requestedSize, Vector2? requestedPos, PanelRect? saved) in open)
        {
            float s = EngineFrameController.FontScale;
            // A frame that places the window (saved spot, plugin request) only takes a new baseline.
            bool placed = false;
            if (requestedSize is { } size)
            {
                ImGui.SetNextWindowSize(size * s, ImGuiCond.Always);
                placed = true;
            }
            else if (saved is { } savedSize)
            {
                ImGui.SetNextWindowSize(savedSize.Size * s, ImGuiCond.Always);
                placed = true;
            }
            else
            {
                ImGui.SetNextWindowSize(entry.Spec.DefaultSize * s, ImGuiCond.FirstUseEver);
            }

            if (requestedPos is { } pos)
            {
                ImGui.SetNextWindowPos(pos * s, ImGuiCond.Always);
                placed = true;
            }
            else if (saved is { } savedPos)
            {
                ImGui.SetNextWindowPos(ClampToDisplay(savedPos.Pos * s, savedPos.Size * s), ImGuiCond.Always);
                placed = true;
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
                {
                    if (entry.Failures >= MaxFailures)
                        ImGui.TextDisabled("This window failed and was stopped.");
                    else
                        entry.Instance?.Draw();
                    TrackPlacement(key, entry, placed, s);
                }
            }
            catch (Exception error)
            {
                entry.Failures++;
                string tail = entry.Failures >= MaxFailures ? " (stopped after repeated failures)" : "";
                Log?.Invoke($"[ScriptWindows] {entry.Title}: {error.GetType().Name}: {error.Message}{tail}");
            }
            finally
            {
                ImGui.End();
            }

            if (!keepOpen)
                Close(key);
        }
    }

    /// <summary>
    /// Saves the window's rectangle when the player moved or resized it: it
    /// differs from the last one seen and the left mouse button is up, so once
    /// per finished drag. A frame that placed the window only takes the new
    /// rectangle as its baseline. Inside the window, after its body.
    /// </summary>
    private static void TrackPlacement(string key, Entry entry, bool placed, float s)
    {
        var now = new PanelRect(ImGui.GetWindowPos() / s, ImGui.GetWindowSize() / s);
        if (placed || entry.Baseline is null)
        {
            entry.Baseline = now;
            return;
        }

        if (now == entry.Baseline || ImGui.IsMouseDown(ImGuiMouseButton.Left))
            return;
        entry.Baseline = now;
        Layout?.SetRect(key, now);
    }

    /// <summary>Keeps a window of this size wholly on the display (when the display is known).</summary>
    private static Vector2 ClampToDisplay(Vector2 pos, Vector2 size)
    {
        Vector2 display = ImGui.GetIO().DisplaySize;
        if (display.X <= 1 || display.Y <= 1)
            return pos;
        return Vector2.Clamp(pos, Vector2.Zero, Vector2.Max(Vector2.Zero, display - size));
    }
```

- [ ] **Step 5: Run the new tests.** Run `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo --filter "FullyQualifiedName~PanelHostLayoutTests"`. Expected: 7 passed.

If `APlayerMoveIsSavedOnceTheMouseIsUp` fails because a mouse-down on empty space started an ImGui window move, set `ImGui.GetIO().AddMousePosEvent(-10000, -10000)` at the start of that test so the press happens off every window, and rerun.

- [ ] **Step 6: Add the real-Oracle restore test** to `tests/AcDream.Rynth.Host.Tests/ScriptWindowPipelineTests.cs`, after `APlayerCloseReachesThePluginAndTheWindowStaysClosed`:

```csharp
    [Fact]
    public void AWindowSavedAsOpenIsReopenedAndThePluginHearsOfIt()
    {
        var layout = new FakePanelLayout();
        layout.Saved["script:" + OracleKey] = new RynthCore.Engine.ImGuiBackend.PanelPlacement(null, true);
        ImGuiPanelHost.Layout = layout;
        using RynthHost host = CreateOracleHost();
        host.Tick();
        host.Tick();
        Assert.Contains("visible=False", OracleLine());

        _imgui.Frame(ImGuiPanelHost.DrawAll);
        for (int i = 0; i < 5; i++)
            host.Tick();

        Assert.True(ScriptWindowRegistry.IsOpen(OracleKey), Diag());
        Assert.Contains("visible=True", OracleLine());
    }
```

(The constructor and `Dispose` already call `ImGuiPanelHost.Reset()`, which clears `Layout`.)

- [ ] **Step 7: Run the whole test project.** Run the Verify command. Expected: all pass (33 existing + 5 store + 7 panel host + 1 pipeline = 46).

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiPanelHost.cs tests/AcDream.Rynth.Host.Tests/FakePanelLayout.cs tests/AcDream.Rynth.Host.Tests/PanelHostLayoutTests.cs tests/AcDream.Rynth.Host.Tests/ScriptWindowPipelineTests.cs
git commit -m "feat(rynth): script windows reopen where the player left them"
```

---

### Task 4: The launcher bar

**Goal:** `RynthBar` draws a small draggable strip with a button per `BarButtons` window and a "Scripts" menu of `BarItems`, toggles windows the way RynthCore's bar does, and keeps its position in the layout store.

**Files:**
- Create: `src/AcDream.Rynth.Host/RynthBar.cs`
- Test: `tests/AcDream.Rynth.Host.Tests/RynthBarTests.cs`

**Acceptance Criteria:**
- [ ] With no ShowInBar windows registered, `Draw` draws nothing (0 vertices).
- [ ] With Oracle loaded (its windows are ShowInBar), `HasContent` is true and `Draw` produces vertices.
- [ ] `Toggle` on Oracle's bar item opens the window and the plugin reports `visible=True`; toggling again closes it.
- [ ] On its first frame the bar sits at the store's `BarPosition` (or `(12, 40)` when none).
- [ ] Moving the bar with the mouse up saves its new position to the store.
- [ ] A body exception is logged, ImGui stays balanced, and after 3 failures the bar stops drawing.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo --filter "FullyQualifiedName~RynthBarTests"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** in `tests/AcDream.Rynth.Host.Tests/RynthBarTests.cs`:

```csharp
using System.Numerics;
using AcDream.Rynth.Host;
using ImGuiNET;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.UI.ScriptWindows;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthBarTests : IDisposable
{
    private const string OracleKey = "RynthOracle/Oracle";
    private readonly HeadlessImGui _imgui = new();
    private readonly List<string> _log = new();
    private readonly RynthLayoutStore _store;
    private readonly FakeGameView _view = new()
    {
        PlayerId = 0x50000000u | (uint)Random.Shared.Next(1, 0xFFFF),
    };

    public RynthBarTests()
    {
        ScriptWindowRegistry.Reset();
        ImGuiPanelHost.Reset();
        _store = new RynthLayoutStore(null, _log.Add);
    }

    public void Dispose()
    {
        ScriptWindowRegistry.Reset();
        ImGuiPanelHost.Reset();
        _imgui.Dispose();
    }

    private RynthHost CreateOracleHost()
    {
        var host = new RynthHost(_view, _log.Add, [typeof(RynthCore.Plugin.RynthOracle.PluginExports)], scriptWindows: true);
        host.Tick();
        host.Tick();
        return host;
    }

    private static string OracleLine() =>
        ScriptWindowRegistry.Describe().FirstOrDefault(l => l.Contains(OracleKey)) ?? "(no oracle window)";

    private static Vector2 BarPos()
    {
        ImGui.Begin("##RynthBar");
        Vector2 pos = ImGui.GetWindowPos();
        ImGui.End();
        return pos;
    }

    [Fact]
    public void WithNothingOnItTheBarDrawsNothing()
    {
        var bar = new RynthBar(_store, _log.Add);

        Assert.False(RynthBar.HasContent);
        Assert.Equal(0, _imgui.Frame(bar.Draw));
    }

    [Fact]
    public void OraclesWindowsPutTheBarOnScreen()
    {
        using RynthHost host = CreateOracleHost();
        var bar = new RynthBar(_store, _log.Add);

        Assert.True(RynthBar.HasContent);
        _imgui.Frame(bar.Draw);
        Assert.True(_imgui.Frame(bar.Draw) > 0);
    }

    [Fact]
    public void ToggleOpensAndClosesTheWindowAndThePluginHearsOfIt()
    {
        using RynthHost host = CreateOracleHost();
        ScriptBarItem item = ScriptWindowRegistry.BarItems.First(i => i.PanelKey.EndsWith(OracleKey, StringComparison.OrdinalIgnoreCase));

        Assert.True(RynthBar.Toggle(item));
        for (int i = 0; i < 5; i++)
            host.Tick();
        Assert.True(ScriptWindowRegistry.IsOpen(OracleKey));
        Assert.Contains("visible=True", OracleLine());

        Assert.False(RynthBar.Toggle(item));
        for (int i = 0; i < 5; i++)
            host.Tick();
        Assert.False(ScriptWindowRegistry.IsOpen(OracleKey));
    }

    [Fact]
    public void TheBarStartsWhereItWasLeft()
    {
        _store.SetBarPosition(new Vector2(300, 150));
        using RynthHost host = CreateOracleHost();
        var bar = new RynthBar(_store, _log.Add);
        Vector2 pos = default;

        _imgui.Frame(() => { bar.Draw(); pos = BarPos(); });

        Assert.Equal(new Vector2(300, 150), pos);
    }

    [Fact]
    public void WithNothingSavedTheBarStartsAtItsDefaultSpot()
    {
        using RynthHost host = CreateOracleHost();
        var bar = new RynthBar(_store, _log.Add);
        Vector2 pos = default;

        _imgui.Frame(() => { bar.Draw(); pos = BarPos(); });

        Assert.Equal(RynthBar.DefaultPos, pos);
    }

    [Fact]
    public void AMovedBarIsSaved()
    {
        using RynthHost host = CreateOracleHost();
        var bar = new RynthBar(_store, _log.Add);
        _imgui.Frame(bar.Draw);

        _imgui.Frame(() => { ImGui.SetWindowPos("##RynthBar", new Vector2(200, 100)); bar.Draw(); });

        Assert.Equal(new Vector2(200, 100), _store.BarPosition);
    }

    [Fact]
    public void ABarThatKeepsFailingStopsAndImGuiStaysBalanced()
    {
        using RynthHost host = CreateOracleHost();
        var bar = new RynthBar(_store, _log.Add) { ContentsHook = () => throw new InvalidOperationException("boom") };

        for (int i = 0; i < 6; i++)
            _imgui.Frame(bar.Draw);
        void After() { ImGui.Begin("after"); ImGui.TextUnformatted("fine"); ImGui.End(); }
        _imgui.Frame(After);

        Assert.Equal(3, _log.Count(line => line.Contains("bar", StringComparison.OrdinalIgnoreCase) && line.Contains("boom", StringComparison.Ordinal)));
        Assert.True(_imgui.Frame(After) > 0);
    }
}
```

- [ ] **Step 2: Run them to see them fail.** Run the Verify command. Expected: build error, `RynthBar` does not exist.

- [ ] **Step 3: Write `src/AcDream.Rynth.Host/RynthBar.cs`:**

```csharp
using System.Numerics;
using ImGuiNET;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.UI.ScriptWindows;

namespace AcDream.Rynth.Host;

/// <summary>
/// The launcher bar: a small draggable strip over the game with a button per
/// script window that asked for one (BarButton) and a "Scripts" menu for the
/// other ShowInBar windows (RynthOracle's). A click opens or closes the window
/// the way RynthCore's bar does, so the plugin hears of it. Drawn only while
/// something is on it; its position is kept in the layout store. Game thread.
/// </summary>
internal sealed class RynthBar
{
    internal static readonly Vector2 DefaultPos = new(12, 40);
    private const string WindowName = "##RynthBar";
    private const string MenuName = "##RynthBarScripts";
    private const int MaxFailures = 3;
    private const ImGuiWindowFlags Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize
        | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoScrollbar
        | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoNav;

    private readonly RynthLayoutStore _layout;
    private readonly Action<string> _log;
    private bool _placed;
    private Vector2? _baseline;
    private int _failures;

    public RynthBar(RynthLayoutStore layout, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(log);
        _layout = layout;
        _log = log;
    }

    /// <summary>Runs inside the bar before its contents (tests make the body fail with it).</summary>
    internal Action? ContentsHook { get; init; }

    /// <summary>True when any window is on the bar.</summary>
    internal static bool HasContent =>
        ScriptWindowRegistry.BarButtons.Length > 0 || ScriptWindowRegistry.BarItems.Length > 0;

    /// <summary>Opens a closed window or closes an open one, as the player asked. Returns whether it is now open.</summary>
    internal static bool Toggle(ScriptBarItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        bool open = ImGuiPanelHost.IsOpen(item.PanelKey);
        ScriptWindowRegistry.PlayerSetVisible(item.Window, !open);
        return !open;
    }

    /// <summary>Draws the bar. Call between ImGui.NewFrame and ImGui.Render, after ImGuiPanelHost.DrawAll.</summary>
    public void Draw()
    {
        if (_failures >= MaxFailures || !HasContent)
            return;
        if (!_placed)
        {
            ImGui.SetNextWindowPos(ClampToDisplay(_layout.BarPosition ?? DefaultPos), ImGuiCond.Always);
            _placed = true;
        }

        ImGui.Begin(WindowName, Flags);
        try
        {
            ContentsHook?.Invoke();
            DrawContents();
            Track();
        }
        catch (Exception error)
        {
            _failures++;
            string tail = _failures >= MaxFailures ? " (stopped after repeated failures)" : "";
            _log($"[Rynth] bar: {error.GetType().Name}: {error.Message}{tail}");
        }
        finally
        {
            ImGui.End();
        }
    }

    private static void DrawContents()
    {
        ImGui.Button("::");
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 0f))
            ImGui.SetWindowPos(ImGui.GetWindowPos() + ImGui.GetIO().MouseDelta);
        Tooltip("Rynth: drag to move");

        foreach (ScriptBarItem item in ScriptWindowRegistry.BarButtons)
        {
            ImGui.SameLine();
            bool open = ImGuiPanelHost.IsOpen(item.PanelKey);
            if (open)
                ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ButtonActive));
            if (ImGui.Button(Letter(item.Window.Title) + "##" + item.MenuLabel))
                Toggle(item);
            if (open)
                ImGui.PopStyleColor();
            Tooltip(item.Window.Title);
        }

        ScriptBarItem[] menu = ScriptWindowRegistry.BarItems;
        if (menu.Length == 0)
            return;
        ImGui.SameLine();
        if (ImGui.Button("Scripts"))
            ImGui.OpenPopup(MenuName);
        if (!ImGui.BeginPopup(MenuName))
            return;
        try
        {
            foreach (ScriptBarItem item in menu)
            {
                if (item.GroupStart)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
                    ImGui.TextUnformatted(item.Group);
                    ImGui.PopStyleColor();
                }

                if (ImGui.MenuItem(item.MenuLabel, string.Empty, ImGuiPanelHost.IsOpen(item.PanelKey)))
                    Toggle(item);
            }
        }
        finally
        {
            ImGui.EndPopup();
        }
    }

    /// <summary>Saves the bar's position once the player has moved it and let go.</summary>
    private void Track()
    {
        Vector2 pos = ImGui.GetWindowPos();
        if (_baseline is null)
        {
            _baseline = pos;
            return;
        }

        if (pos == _baseline || ImGui.IsMouseDown(ImGuiMouseButton.Left))
            return;
        _baseline = pos;
        _layout.SetBarPosition(pos);
    }

    /// <summary>A hover tooltip for the last item; the text is never a format string.</summary>
    private static void Tooltip(string text)
    {
        if (!ImGui.IsItemHovered())
            return;
        ImGui.BeginTooltip();
        ImGui.TextUnformatted(text);
        ImGui.EndTooltip();
    }

    /// <summary>The button's placeholder icon: the title's first letter.</summary>
    private static string Letter(string title)
    {
        foreach (char c in title)
        {
            if (char.IsLetterOrDigit(c))
                return new string(char.ToUpperInvariant(c), 1);
        }

        return "?";
    }

    private static Vector2 ClampToDisplay(Vector2 pos)
    {
        Vector2 display = ImGui.GetIO().DisplaySize;
        if (display.X <= 1 || display.Y <= 1)
            return pos;
        return Vector2.Clamp(pos, Vector2.Zero, Vector2.Max(Vector2.Zero, display - new Vector2(40, 24)));
    }
}
```

- [ ] **Step 4: Run the tests.** Run the Verify command. Expected: 7 passed. If `AMovedBarIsSaved` fails because `SetWindowPos` by name doesn't apply to an `AlwaysAutoResize` window before `Begin`, move the call after `bar.Draw()` and add one more `_imgui.Frame(bar.Draw)` before the assert.

- [ ] **Step 5: Run the whole test project.** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo` → all pass.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.Rynth.Host/RynthBar.cs tests/AcDream.Rynth.Host.Tests/RynthBarTests.cs
git commit -m "feat(rynth): a launcher bar for plugin script windows"
```

---

### Task 5: Wire the store, layout hook and bar into the extension

**Goal:** With an overlay, `RynthClientExtension` creates the layout store from `StateDirectory`, sets `ImGuiPanelHost.Layout` before plugins load, and draws the bar after the windows from its one frame callback; dispose undoes it.

**Files:**
- Modify: `src/AcDream.Rynth.Host/RynthClientExtension.cs`
- Modify: `tests/AcDream.App.Tests/Extensions/RynthOverlayExtensionTests.cs`

**Acceptance Criteria:**
- [ ] Attached with an overlay, `ImGuiPanelHost.Layout` is not null; after `Dispose` it is null.
- [ ] Attached without an overlay, `ImGuiPanelHost.Layout` stays null.
- [ ] Still exactly one frame callback; it draws the windows and then the bar.
- [ ] With a `StateDirectory`, opening Oracle via `/ro` writes `rynth-layout.json` in that folder with Oracle's key and `"open": true`.
- [ ] Existing `RynthOverlayExtensionTests` and `RynthClientExtensionTests` pass.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests -nologo --filter "FullyQualifiedName~Rynth"` → all pass; `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.** Add to `RynthOverlayExtensionTests`, and give `Context` an optional state folder:

```csharp
    private ClientExtensionContext Context(AcDream.Runtime.GameRuntime runtime, IClientOverlay? overlay, string? stateDirectory = null) => new()
    {
        Runtime = runtime,
        Log = _log.Add,
        RegisterChatCommand = (verb, handler) =>
        {
            _commands[verb] = handler;
            return new Registration(() => _commands.Remove(verb));
        },
        Overlay = overlay,
        StateDirectory = stateDirectory,
    };

    [Fact]
    public void WithAnOverlayTheLayoutIsHookedUpUntilDispose()
    {
        var extension = new RynthClientExtension();
        extension.Attach(Context(GameRuntimeTestFactory.Create(), new FakeOverlay()));
        Assert.NotNull(ImGuiPanelHost.Layout);

        extension.Dispose();
        Assert.Null(ImGuiPanelHost.Layout);
    }

    [Fact]
    public void WithoutAnOverlayThereIsNoLayout()
    {
        using var extension = new RynthClientExtension();
        extension.Attach(Context(GameRuntimeTestFactory.Create(), overlay: null));

        Assert.Null(ImGuiPanelHost.Layout);
    }

    [Fact]
    public void AnOpenedWindowIsSavedInTheStateFolder()
    {
        string dir = Path.Combine(Path.GetTempPath(), "rynth-ext-" + Guid.NewGuid().ToString("N"));
        try
        {
            var overlay = new FakeOverlay();
            var extension = new RynthClientExtension();
            extension.Attach(Context(GameRuntimeTestFactory.Create(), overlay, dir));
            _commands["ro"]("");
            extension.Tick(1 / 60.0);
            extension.Tick(1 / 60.0);
            Frame(Assert.Single(overlay.Callbacks));
            // Read before Dispose: shutting the plugin down may record its window as closed.
            string json = File.ReadAllText(Path.Combine(dir, "rynth-layout.json"));
            extension.Dispose();

            Assert.Contains("script:RynthOracle/Oracle", json, StringComparison.Ordinal);
            Assert.Contains("\"open\": true", json, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
```

- [ ] **Step 2: Run them to see them fail.** Run `~/.dotnet/dotnet test tests/AcDream.App.Tests -nologo --filter "FullyQualifiedName~RynthOverlayExtensionTests"`. Expected: `WithAnOverlayTheLayoutIsHookedUpUntilDispose` and `AnOpenedWindowIsSavedInTheStateFolder` fail.

- [ ] **Step 3: Wire it in `RynthClientExtension`.** Add fields:

```csharp
    private RynthLayoutStore? _layout;
    private RynthBar? _bar;
```

At the start of `Attach`, right after `ArgumentNullException.ThrowIfNull(context);`, add (before the host is created, so windows registered at load see the layout):

```csharp
        if (context.Overlay is not null)
        {
            _layout = new RynthLayoutStore(context.StateDirectory, context.Log);
            ImGuiPanelHost.Layout = _layout;
            _bar = new RynthBar(_layout, context.Log);
        }
```

Replace

```csharp
        if (context.Overlay is { } overlay)
            _frameCallback = overlay.AddFrameCallback(ImGuiPanelHost.DrawAll);
```

with

```csharp
        if (context.Overlay is { } overlay)
            _frameCallback = overlay.AddFrameCallback(DrawFrame);
```

Add the method:

```csharp
    /// <summary>The overlay frame: the script windows, then the bar over them.</summary>
    private void DrawFrame()
    {
        ImGuiPanelHost.DrawAll();
        _bar?.Draw();
    }
```

In `Dispose`, after the existing `_host` disposal (the last lines of the method), add:

```csharp
        if (_layout is not null && ImGuiPanelHost.Layout == _layout)
            ImGuiPanelHost.Layout = null;
        _layout = null;
        _bar = null;
```

Update the class summary's sentence about script windows to: "script windows draw on the client's ImGui overlay when it has one, with a launcher bar and their layout saved per install,".

- [ ] **Step 4: Run the tests.** Run both Verify commands. Expected: all pass.

- [ ] **Step 5: Build the client.** `~/.dotnet/dotnet build src/AcDream.App -nologo -v q` → `0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.Rynth.Host/RynthClientExtension.cs tests/AcDream.App.Tests/Extensions/RynthOverlayExtensionTests.cs
git commit -m "feat(rynth): the extension saves window layout and draws the launcher bar"
```

---

### Task 6: Live check on the local ACE server

> **Result (2026-10-18): PASS.** Two runs on 10.10.20.20, same data folder. The user confirmed: the bar's Scripts menu opens and closes Oracle; Oracle stays closed at character select; after a relaunch the bar and Oracle are back where they were left and Oracle reopens after login. `rynth-layout.json` held Oracle's rectangle, `open: true` and the bar position (Void was saved open as well; Oracle opens it itself). No `[Rynth] layout`, `[Rynth] bar:` or exception lines in either log.

**Goal:** In the real client on macOS against the local ACE server 10.10.20.20, the bar opens Oracle, Oracle's window comes back where it was left after a relaunch, and the bar keeps its spot.

**Files:**
- None (no code). Results go in the session report.

**Acceptance Criteria:**
- [ ] The user said they are available before the client was launched.
- [ ] After login the bar shows at top-left with a "Scripts" button; its menu lists Oracle and Void under RynthOracle; clicking Oracle opens the window and clicking it again closes it.
- [ ] After the user moves and resizes Oracle, leaves it open, and quits and relaunches, Oracle opens at the same place and size after login.
- [ ] After the user drags the bar and relaunches, the bar is where it was left.
- [ ] `<scratch root>/plugins/rynth/files/rynth-layout.json` exists and holds Oracle's rectangle and the bar position.
- [ ] The client log has no `[Rynth] layout`, `[Rynth] bar:` or `[ScriptWindows]` error lines.

**Verify:** `cat "$ACDREAM_ROOT_DIR/plugins/rynth/files/rynth-layout.json"` → JSON with `script:RynthOracle/Oracle` (x, y, w, h, open) and `bar`

**Steps:**

- [ ] **Step 1: Ask the user whether they are available.** Don't launch anything before they say yes.

- [ ] **Step 2: Build and launch** the dev client: `source ~/OpenAC-dev/dev-client.env` (never print its passwords), `ACDREAM_LIVE=1`, a scratch `ACDREAM_ROOT_DIR` kept across both launches, `DYLD_LIBRARY_PATH`/`VK_DRIVER_FILES` from brew, SDK at `~/.dotnet`. Server 10.10.20.20 only, never Dreamweave. If the first launch logs "CharacterList not received", relaunch.

- [ ] **Step 3: The user checks the bar** (Scripts → Oracle opens and closes it), moves and resizes Oracle, drags the bar, leaves Oracle open, quits.

- [ ] **Step 4: Show the layout file** (Verify command), relaunch with the same root, and have the user confirm Oracle and the bar are back in place.

- [ ] **Step 5: Grep the log** for `\[Rynth\] layout|\[Rynth\] bar:|\[ScriptWindows\]` and report the result with the exact lines.
