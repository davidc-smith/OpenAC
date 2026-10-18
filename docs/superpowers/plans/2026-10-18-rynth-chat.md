# RynthChat Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** RynthSuite's RynthChat plugin and RynthCore's own ImGui chat window (tabs, filters, colours, search, copy, input line) run inside OpenAC, compiled unchanged from the submodules.

**Architecture:** A wrapper project compiles the RynthChat plugin. A new `AcDream.Rynth.ChatWindow` project compiles RynthCore's chat window files (`ChatFace`, `ChatFiltersFace`, `ChatData` and friends) unchanged against stand-ins for the engine types they use. Shared stand-ins stay in `AcDream.Rynth.ScriptWindows`. The Rynth host points the stand-ins at its plugins and game view, polls the chat source each tick, and adds a bar button. OpenAC gains two small seams for extensions: a keyboard sink and clipboard on `IClientOverlay`, and `IClientChat` (claim the chat-entry key, hide OpenAC's chat window).

**Tech Stack:** .NET 10 (`~/.dotnet/dotnet`), ImGui.NET 1.91.6.1, Silk.NET input, xunit v3 (VSTest runner: `--filter "FullyQualifiedName~X"`), RynthCore/RynthSuite submodules.

**Spec:** `docs/superpowers/specs/2026-10-18-rynth-chat-design.md`

## Global Constraints

- Repo `~/code/OpenAC/OpenAC-Rynth`. Create branch `feat/rynth-chat` from `main` before Task 1. Do not push.
- Use `~/.dotnet/dotnet` (the system `dotnet` lacks SDK 10.0.300).
- **Never edit, copy or move anything inside `external/RynthCore` or `external/RynthSuite`.** Wrapper projects include files from there by path. When their code needs an engine type we don't compile, add or grow a stand-in in our project; never change their call.
- RynthCore's `ImGui/Win32Backend.cs`, `Compatibility/ChatHooks.cs`, `UI/Data/UiSources.cs`, `UI/Data/UiDataHub.cs`, `UI/Data/PluginExportBinder.cs` and `UI/Data/Discord/**` are **never compiled**. Our stand-ins of the same names are plain classes with no P/Invoke.
- `AcDream.App`, `AcDream.ClientExtensions` and `AcDream.Rynth.Host` build with warnings as errors and analyzers on: new code there must build clean. `AcDream.Rynth.ChatWindow` and the plugin wrapper have warnings-as-errors and analyzers off (their code), like `AcDream.Rynth.ScriptWindows`.
- Exceptions never cross from a stand-in, the keyboard sink or the pump into OpenAC: catch, log through the host log, carry on.
- Tests that load RynthChat or touch `ChatModel` use `RynthConfigSandbox` (their settings file lives under `ApplicationData/RynthCore`). Rynth host tests run without parallelism (assembly attribute already in `FakeGameView.cs`); App tests use `[Collection("Rynth")]`.
- Known unrelated failures exist in `AcDream.App.Tests` (dat files, live server). Run App tests with the filters each task's Verify shows. `AcDream.Rynth.Host.Tests` must be fully green.
- Never launch a client without asking the user first; only the local ACE server 10.10.20.20, never Dreamweave; never print passwords (`~/OpenAC-dev/dev-client.env`).

**User decisions (already made):**
- Next display plugin is RynthChat.
- Run their code unchanged and keep it pullable: "we are just trying to get rynth running in our client, we are not trying to rewrite their plugin. we cant make changes to their code, and we need to be able to pull in their latest daily changes".
- Full keyboard input in the Rynth chat window now, not a viewer first.
- Keep their chat-ownership rule: "Hide retail chat" ticked + Rynth chat open + logged in → OpenAC's chat window hides and Enter types in Rynth chat; unticked → OpenAC's chat keeps Enter.
- Same approach on every OS; RynthCore's Windows backend is never compiled.

---

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/AcDream.Rynth.Plugin.RynthChat/AcDream.Rynth.Plugin.RynthChat.csproj` | create | compile the RynthChat plugin from the submodule |
| `src/AcDream.Rynth.ChatWindow/AcDream.Rynth.ChatWindow.csproj` | create | compile RynthCore's chat window files from the submodule |
| `src/AcDream.Rynth.ChatWindow/StandIns/ChatEngineStandIns.cs` | create | chat-only engine stand-ins (`ChatHooks`, hooks, `PluginExportBinder`, `UiSources`, Discord, nav, `Win32Backend`, …) |
| `src/AcDream.Rynth.ChatWindow/RynthChatWindow.cs` | create | start-up (as their EntryPoint), per-tick chat source poll (as their UiDataHub), stop |
| `src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiStandIns.cs` | modify | full `UiFont`, `ImGuiFonts.Chat`, `PanelSpec.EdgeToEdge/GripInBody` |
| `src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiPanelHost.cs` | modify | `Register`, `HasFace`, `BodyGripReserve`, `Toggle`; `DllNotFoundException` not counted as a failure |
| `src/AcDream.Rynth.ScriptWindows/AcDream.Rynth.ScriptWindows.csproj` | modify | `InternalsVisibleTo` the chat window |
| `AcDream.slnx` | modify | list both new projects |
| `src/AcDream.Rynth.Host/AcDream.Rynth.Host.csproj` | modify | reference both new projects |
| `src/AcDream.Rynth.Host/RynthPlugin.cs` | modify | `TryGetExport` |
| `src/AcDream.Rynth.Host/RynthChatHookup.cs` | create | points the chat stand-ins at the host and view; ownership → OpenAC chat seam |
| `src/AcDream.Rynth.Host/RynthChatKeyboard.cs` | create | keyboard sink: keys → `Win32Backend.OnChat*`, Enter tail, deferred send, clipboard fallback |
| `src/AcDream.Rynth.Host/RynthBar.cs` | modify | panel buttons (Chat) beside script-window buttons |
| `src/AcDream.Rynth.Host/RynthClientExtension.cs` | modify | load RynthChat, start/tick/stop the chat window, keyboard sink, chat seam |
| `src/AcDream.ClientExtensions/IClientOverlay.cs` | modify | `AddKeyboardSink`, `ClipboardText` |
| `src/AcDream.ClientExtensions/IOverlayKeyboardSink.cs` | create | sink interface, `OverlayKey`, `OverlayModifiers` |
| `src/AcDream.ClientExtensions/IClientChat.cs` | create | `ClaimChatEntry`, `SetChatWindowHidden` |
| `src/AcDream.ClientExtensions/ClientExtensionContext.cs` | modify | `Chat` |
| `src/AcDream.App/Extensions/Overlay/ImGuiOverlay.cs` | modify | sinks, live `WantsKeyboard`, clipboard |
| `src/AcDream.App/Extensions/Overlay/ImGuiInputFeed.cs` | modify | forward keys/chars to the overlay's sinks; clipboard from the keyboard |
| `src/AcDream.App/Extensions/Overlay/OverlayKeyMap.cs` | create | Silk `Key` → `OverlayKey` |
| `src/AcDream.App/Extensions/ClientChatSeam.cs` | create | `IClientChat` for the client |
| `src/AcDream.App/Extensions/ClientExtensionHost.cs` | modify | pass `Chat` |
| `src/AcDream.App/UI/RetailUiRuntime.cs` | modify | claim before `ChatStartCommand`; apply hidden state each tick |
| `src/AcDream.App/UI/Layout/ChatWindowController.cs` | modify | `SetExtensionHidden` |
| `src/AcDream.App/Rendering/GameWindow.cs` | modify | own the seam; hand it to extensions and the retail UI |
| `tests/AcDream.Rynth.Host.Tests/RynthChatPluginTests.cs` | create | plugin wrapper tests |
| `tests/AcDream.Rynth.Host.Tests/ChatPanelHostTests.cs` | create | grown panel-host stand-in tests |
| `tests/AcDream.Rynth.Host.Tests/RynthChatWindowTests.cs` | create | start/poll/stop, source check, end-to-end lines and send |
| `tests/AcDream.Rynth.Host.Tests/RynthChatKeyboardTests.cs` | create | key mapping, tail, send, clipboard, claim, hide |
| `tests/AcDream.Rynth.Host.Tests/RynthBarTests.cs` | modify | Chat panel button |
| `tests/AcDream.App.Tests/Extensions/ImGuiOverlayTests.cs` | modify | sink and clipboard tests |
| `tests/AcDream.App.Tests/Extensions/ClientChatSeamTests.cs` | create | seam tests |

---

### Task 1: RynthChat wrapper project loads in the host

**Goal:** The RynthChat plugin compiles unchanged from the submodule, loads in `RynthHost`, collects chat lines, and its two chat exports can be called through `RynthPlugin.TryGetExport`.

**Files:**
- Create: `src/AcDream.Rynth.Plugin.RynthChat/AcDream.Rynth.Plugin.RynthChat.csproj`
- Modify: `AcDream.slnx`, `src/AcDream.Rynth.Host/AcDream.Rynth.Host.csproj`, `src/AcDream.Rynth.Host/RynthPlugin.cs`
- Test: `tests/AcDream.Rynth.Host.Tests/RynthChatPluginTests.cs`

**Acceptance Criteria:**
- [ ] `RynthHost` with `typeof(RynthCore.Plugin.RynthChat.PluginExports)` has one active plugin.
- [ ] After `OnChatWindowText("Bob says, \"hi\"", 2)`, calling `RynthChatGetScrollbackJson(0)` returns JSON containing `"sender":"Bob"` and `"chan":"Chat"`.
- [ ] Calling `RynthChatSendLine("/hello")` puts `/hello` in the view's submitted commands.
- [ ] `TryGetExport` answers false for an unknown name and for a shut-down plugin.
- [ ] No file under `external/` changed (`git status --porcelain external` empty).

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthChatPluginTests"` → all pass

**Steps:**

- [ ] **Step 1: Create the wrapper project** (same shape as the RynthInventory wrapper)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <!-- RynthSuite's RynthChat, compiled unchanged from the submodule as plain net10.0. -->
  <PropertyGroup>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <RunAnalyzers>false</RunAnalyzers>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
    <IsPackable>false</IsPackable>
    <ChatPlugin>$(MSBuildThisFileDirectory)..\..\external\RynthSuite\Plugins\RynthCore.Plugin.RynthChat\</ChatPlugin>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="$(ChatPlugin)**\*.cs" Exclude="$(ChatPlugin)obj\**;$(ChatPlugin)bin\**" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\AcDream.Rynth.Sdk\AcDream.Rynth.Sdk.csproj" />
  </ItemGroup>
</Project>
```

Add `<Project Path="src/AcDream.Rynth.Plugin.RynthChat/AcDream.Rynth.Plugin.RynthChat.csproj" />` to `AcDream.slnx` beside the other Rynth plugin entries, and a `ProjectReference` to it in `AcDream.Rynth.Host.csproj` after the RynthInventory one.

- [ ] **Step 2: Write the failing tests**

```csharp
using System.Runtime.InteropServices;
using AcDream.Rynth.Host;
using AcDream.TestSupport;

namespace AcDream.Rynth.Host.Tests;

public sealed unsafe class RynthChatPluginTests : IDisposable
{
    private readonly List<string> _log = new();
    private readonly RynthConfigSandbox _sandbox = new("rynth-chat-");
    private readonly FakeGameView _view = new() { PlayerId = 0x50000001u };

    public void Dispose() => _sandbox.Dispose();

    private RynthHost CreateChatHost() =>
        new(_view, _log.Add, [typeof(RynthCore.Plugin.RynthChat.PluginExports)]);

    [Fact]
    public void ChatLoads()
    {
        using RynthHost host = CreateChatHost();
        Assert.True(Assert.Single(host.Plugins).IsActive);
    }

    [Fact]
    public void ALineReachesTheScrollbackExport()
    {
        using RynthHost host = CreateChatHost();
        RynthPlugin chat = host.Plugins[0];
        chat.OnChatWindowText("Bob says, \"hi\"", 2);

        Assert.True(chat.TryGetExport("RynthChatGetScrollbackJson", out IntPtr get));
        IntPtr json = ((delegate* unmanaged[Cdecl]<ulong, IntPtr>)get)(0);
        string text = Marshal.PtrToStringAnsi(json) ?? "";

        Assert.Contains("\"sender\":\"Bob\"", text, StringComparison.Ordinal);
        Assert.Contains("\"chan\":\"Chat\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SendLineRunsThroughTheChatParser()
    {
        using RynthHost host = CreateChatHost();
        Assert.True(host.Plugins[0].TryGetExport("RynthChatSendLine", out IntPtr send));

        IntPtr text = Marshal.StringToHGlobalAnsi("/hello");
        try { ((delegate* unmanaged[Cdecl]<IntPtr, void>)send)(text); }
        finally { Marshal.FreeHGlobal(text); }

        Assert.Contains("/hello", _view.SubmittedCommands);
    }

    [Fact]
    public void TryGetExportIsFalseForUnknownNamesAndAfterShutdown()
    {
        RynthHost host = CreateChatHost();
        RynthPlugin chat = host.Plugins[0];
        Assert.False(chat.TryGetExport("NoSuchExport", out _));
        host.Dispose();
        Assert.False(chat.TryGetExport("RynthChatSendLine", out _));
    }
}
```

- [ ] **Step 3: Run to see them fail**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthChatPluginTests"`
Expected: build error, `RynthPlugin` has no `TryGetExport`.

- [ ] **Step 4: Add `TryGetExport` to `RynthPlugin`** (after `IsActive`)

```csharp
    /// <summary>
    /// A plugin export by entry-point name, for callers that call it directly
    /// (RynthCore's chat window binds RynthChat's scrollback exports this way).
    /// False once the plugin is shut down.
    /// </summary>
    public bool TryGetExport(string entryPoint, out IntPtr pointer)
    {
        pointer = IntPtr.Zero;
        return IsActive && _exports.TryGet(entryPoint, out pointer);
    }
```

If `Shutdown()` doesn't already set `IsActive = false`, make it do so.

- [ ] **Step 5: Run the tests**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthChatPluginTests"`
Expected: 4 passed. If RynthChat logs that it needs a newer engine API, read the log line and report it; do not change the plugin.

- [ ] **Step 6: Commit**

```bash
git add AcDream.slnx src/AcDream.Rynth.Plugin.RynthChat src/AcDream.Rynth.Host tests/AcDream.Rynth.Host.Tests/RynthChatPluginTests.cs
git commit -m "feat(rynth): RynthChat compiles from the submodule and loads in the host"
```

---

### Task 2: The shared stand-ins grow to fit the chat window

**Goal:** `AcDream.Rynth.ScriptWindows`' stand-ins carry the members RynthCore's chat files use (full `UiFont`, `ImGuiFonts.Chat`, `PanelSpec.EdgeToEdge/GripInBody`, `ImGuiPanelHost.Register/HasFace/BodyGripReserve/Toggle`), the chat window assembly can see them, and a `DllNotFoundException` from a panel body is logged once without counting towards closing the window.

**Files:**
- Modify: `src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiStandIns.cs`, `src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiPanelHost.cs`, `src/AcDream.Rynth.ScriptWindows/AcDream.Rynth.ScriptWindows.csproj`
- Test: `tests/AcDream.Rynth.Host.Tests/ChatPanelHostTests.cs`

**Acceptance Criteria:**
- [ ] `ImGuiPanelHost.Register("Chat", spec, factory)` then `Open("Chat")` makes `IsOpen("Chat")` true and calls the panel's `OnShown`.
- [ ] `HasFace` is true for a registered title, false otherwise.
- [ ] `Toggle` opens a closed panel and closes an open one.
- [ ] A panel whose `Draw` throws `DllNotFoundException` is still open after 5 frames, and the log has exactly one line naming it.
- [ ] A panel whose `Draw` throws `InvalidOperationException` still closes after 3 failures (existing behaviour).
- [ ] All existing `AcDream.Rynth.Host.Tests` still pass.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** (use `HeadlessImGui` the way `ImGuiPanelHostTests` does; read it first and copy its frame helper)

```csharp
using System.Numerics;
using RynthCore.Engine.ImGuiBackend;

namespace AcDream.Rynth.Host.Tests;

public sealed class ChatPanelHostTests : IDisposable
{
    private readonly List<string> _log = new();
    private readonly HeadlessImGui _imgui = new();

    public ChatPanelHostTests()
    {
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = _log.Add;
    }

    public void Dispose()
    {
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = null;
        _imgui.Dispose();
    }

    private sealed class Panel(Action draw) : IImGuiPanel
    {
        public int Shown;
        public void OnShown() => Shown++;
        public void Draw() => draw();
    }

    private static readonly PanelSpec Spec = new(new Vector2(460, 300), new Vector2(260, 140), Background: 0, EdgeToEdge: true, GripInBody: true);

    [Fact]
    public void RegisterThenOpenShowsThePanel()
    {
        var panel = new Panel(() => { });
        ImGuiPanelHost.Register("Chat", Spec, () => panel);
        ImGuiPanelHost.Open("Chat");
        Assert.True(ImGuiPanelHost.IsOpen("Chat"));
        Assert.Equal(1, panel.Shown);
    }

    [Fact]
    public void HasFaceKnowsRegisteredTitles()
    {
        ImGuiPanelHost.Register("Chat", Spec, () => new Panel(() => { }));
        Assert.True(ImGuiPanelHost.HasFace("Chat"));
        Assert.False(ImGuiPanelHost.HasFace("RynthNav"));
    }

    [Fact]
    public void ToggleFlipsOpenState()
    {
        ImGuiPanelHost.Register("ChatFilters", Spec, () => new Panel(() => { }));
        ImGuiPanelHost.Toggle("ChatFilters");
        Assert.True(ImGuiPanelHost.IsOpen("ChatFilters"));
        ImGuiPanelHost.Toggle("ChatFilters");
        Assert.False(ImGuiPanelHost.IsOpen("ChatFilters"));
    }

    [Fact]
    public void AMissingWindowsLibraryIsLoggedOnceAndTheWindowStays()
    {
        ImGuiPanelHost.Register("Chat", Spec, () => new Panel(() => throw new DllNotFoundException("user32.dll")));
        ImGuiPanelHost.Open("Chat");
        for (int i = 0; i < 5; i++)
            _imgui.Frame(ImGuiPanelHost.DrawAll);
        Assert.True(ImGuiPanelHost.IsOpen("Chat"));
        Assert.Single(_log, l => l.Contains("Chat", StringComparison.Ordinal) && l.Contains("DllNotFoundException", StringComparison.Ordinal));
    }

    [Fact]
    public void OtherFailuresStillCloseTheWindow()
    {
        ImGuiPanelHost.Register("Chat", Spec, () => new Panel(() => throw new InvalidOperationException("boom")));
        ImGuiPanelHost.Open("Chat");
        for (int i = 0; i < 3; i++)
            _imgui.Frame(ImGuiPanelHost.DrawAll);
        Assert.False(ImGuiPanelHost.IsOpen("Chat"));
    }
}
```

If `HeadlessImGui` has no `Frame(Action)` helper, add one to it (NewFrame, action, Render) and use it.

- [ ] **Step 2: Run to see them fail**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~ChatPanelHostTests"`
Expected: build errors (`Register`, `HasFace`, `Toggle`, `EdgeToEdge` missing).

- [ ] **Step 3: Grow `ImGuiStandIns.cs`**

Replace the `UiFont` enum with RynthCore's full list (copied from `external/RynthCore/src/RynthCore.Engine/ImGui/ImGuiFonts.cs`; same order, so a value means the same font):

```csharp
    internal enum UiFont
    {
        Ui10, Ui11, Ui14,
        UiBold11, UiBold14,
        Mono9, Mono10, Mono12,
        Code12,
        Chat8, Chat10, Chat12, Chat14, Chat16, Chat18,
        Ui9,
        Dash9, Dash10, Dash11, Dash14,
        NumBold24,
        Ui18, NumBold48,
        Count,
    }
```

Add to `ImGuiFonts`:

```csharp
        /// <summary>RynthCore's chat font nearest a point size; here, the overlay's current font.</summary>
        public static ImFontPtr Chat(float points) => ImGui.GetFont();
```

Append two parameters to `PanelSpec` (after `DefaultPos`; callers use names):

```csharp
        Vector2? DefaultPos = null,
        bool EdgeToEdge = false,
        bool GripInBody = false);
```

- [ ] **Step 4: Grow `ImGuiPanelHost.cs`**

Add, after `RegisterDynamic`:

```csharp
    /// <summary>
    /// RynthCore's grip reserve in a body's bottom-right corner. Our windows keep
    /// ImGui's own resize grip, so nothing is reserved.
    /// </summary>
    public static float BodyGripReserve => 0f;

    /// <summary>RynthCore's built-in panels (Chat, ChatFilters) register by title.</summary>
    public static void Register(string title, PanelSpec spec, Func<IImGuiPanel> factory) =>
        RegisterDynamic(title, title, spec, factory);

    public static bool HasFace(string title)
    {
        lock (_gate) return _entries.ContainsKey(title);
    }

    /// <summary>Opens a closed panel, closes an open one (RynthCore's PanelRouter.Toggle).</summary>
    public static void Toggle(string title)
    {
        if (IsOpen(title))
            Close(title);
        else
            Open(title);
    }
```

In `DrawAll`, find the `catch` around a panel body that increments `Failures` and logs. Before it, add a `catch (DllNotFoundException error)` that logs once per entry and does not touch `Failures`. Add `public bool MissingLibraryLogged;` to `Entry`:

```csharp
            catch (DllNotFoundException error)
            {
                // RynthCore's chat calls the Win32 clipboard directly; off Windows that
                // library is missing. The window keeps working without it.
                if (!entry.MissingLibraryLogged)
                {
                    entry.MissingLibraryLogged = true;
                    Log?.Invoke($"[ScriptWindows] {entry.Title}: {error.GetType().Name}: {error.Message} (not available on this system)");
                }
            }
```

Keep the existing Begin/End balancing exactly as it is.

- [ ] **Step 5: Let the chat window see the stand-ins**

In `AcDream.Rynth.ScriptWindows.csproj`, add `<InternalsVisibleTo Include="AcDream.Rynth.ChatWindow" />` to the existing `InternalsVisibleTo` group.

- [ ] **Step 6: Run all host tests**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests`
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add src/AcDream.Rynth.ScriptWindows tests/AcDream.Rynth.Host.Tests
git commit -m "feat(rynth): panel-host stand-ins carry what RynthCore's chat window uses"
```

---

### Task 3: AcDream.Rynth.ChatWindow compiles RynthCore's chat window unchanged

**Goal:** A new project compiles RynthCore's chat window files from the submodule against chat-only stand-ins, and `RynthChatWindow` starts it the way RynthCore's EntryPoint does, polls its chat source the way RynthCore's UiDataHub does, and stops it.

**Files:**
- Create: `src/AcDream.Rynth.ChatWindow/AcDream.Rynth.ChatWindow.csproj`, `src/AcDream.Rynth.ChatWindow/StandIns/ChatEngineStandIns.cs`, `src/AcDream.Rynth.ChatWindow/RynthChatWindow.cs`
- Modify: `AcDream.slnx`, `src/AcDream.Rynth.Host/AcDream.Rynth.Host.csproj`
- Test: `tests/AcDream.Rynth.Host.Tests/RynthChatWindowTests.cs`

**Acceptance Criteria:**
- [ ] The project builds with 0 errors; every `Compile` item outside `StandIns/` and `RynthChatWindow.cs` points into `external/RynthCore`.
- [ ] Source check test: the project file names exactly the RynthCore files listed in Step 1, and the repo has no other copy of any of their file names under `src/`.
- [ ] After `RynthChatWindow.Start()`, `ImGuiPanelHost.HasFace("Chat")` and `HasFace("ChatFilters")` are true; a second `Start()` does nothing.
- [ ] `RynthChatWindow.Tick()` does not poll while nobody subscribes, and polls once a subscriber exists and the source is due.
- [ ] `RynthChatWindow.Stop()` resets the chat source (no current snapshot) and clears `Win32Backend.ChatCaptureActive`.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthChatWindowTests"` → all pass

**Steps:**

- [ ] **Step 1: Create the project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <!-- RynthCore's ImGui chat window (ChatFace, the filter editor, ChatData and
       friends), compiled unchanged from the submodule against our stand-ins for
       the engine types they use (StandIns/, plus the shared ones in
       AcDream.Rynth.ScriptWindows). RynthCore's Win32Backend, ChatHooks,
       UiSources, UiDataHub, PluginExportBinder and Discord relay are NOT
       compiled: the stand-ins of those names are plain classes. Their code,
       their warnings: warnings-as-errors and analyzers are off here. -->
  <PropertyGroup>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <RunAnalyzers>false</RunAnalyzers>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
    <IsPackable>false</IsPackable>
    <RynthEngine>$(MSBuildThisFileDirectory)..\..\external\RynthCore\src\RynthCore.Engine\</RynthEngine>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="$(RynthEngine)UI\Data\Chat*.cs" Link="RynthCore\Data\%(Filename)%(Extension)" />
    <Compile Include="$(RynthEngine)UI\Data\UiSource.cs" Link="RynthCore\Data\UiSource.cs" />
    <Compile Include="$(RynthEngine)UI\Panels\RynthChatPresets.cs" Link="RynthCore\RynthChatPresets.cs" />
    <Compile Include="$(RynthEngine)ImGui\Panels\ChatFace.cs" Link="RynthCore\ChatFace.cs" />
    <Compile Include="$(RynthEngine)ImGui\Panels\ChatFiltersFace.cs" Link="RynthCore\ChatFiltersFace.cs" />
    <Compile Include="$(RynthEngine)ImGui\Panels\FaceKit.cs" Link="RynthCore\FaceKit.cs" />
    <Compile Include="$(RynthEngine)ImGui\Panels\DashboardDrawers.cs" Link="RynthCore\DashboardDrawers.cs" />
    <Compile Include="$(RynthEngine)ImGui\SafeImGui.cs" Link="RynthCore\SafeImGui.cs" />
    <Compile Include="$(RynthEngine)ImGui\RynthTheme.cs" Link="RynthCore\RynthTheme.cs" />
    <Compile Include="$(RynthEngine)Compatibility\TellMemory.cs" Link="RynthCore\TellMemory.cs" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="ImGui.NET" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\AcDream.Rynth.ScriptWindows\AcDream.Rynth.ScriptWindows.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="AcDream.Rynth.Host" />
    <InternalsVisibleTo Include="AcDream.Rynth.Host.Tests" />
    <InternalsVisibleTo Include="AcDream.App.Tests" />
  </ItemGroup>
</Project>
```

Add it to `AcDream.slnx` and a `ProjectReference` from `AcDream.Rynth.Host.csproj`. (`UI\Data\Chat*.cs` matches ChatCoords, ChatData, ChatFilterPresets, ChatHistory, ChatInputPaste, ChatLineColors, ChatRouting, ChatRuleMatching, ChatSettingsDto. If a submodule bump adds a `Chat*.cs` file there, the glob picks it up, which is what we want.)

- [ ] **Step 2: Write the chat-only stand-ins** (`StandIns/ChatEngineStandIns.cs`). These signatures were proven by a throwaway compile of exactly the file list above. Each keeps only the members their files use.

```csharp
// Stand-ins for the RynthCore engine types the compiled chat window files use.
// Each keeps only the members those files touch. None of these is RynthCore's
// real class: there is no P/Invoke, no window procedure and no acclient hook
// here. The host points the delegates at OpenAC (RynthChatHookup).
using System;
using System.Collections.Generic;
using RynthCore.Engine.UI.Data;

namespace RynthCore.Engine.Compatibility
{
    /// <summary>
    /// RynthCore's chat-ownership rule, unchanged: Rynth chat owns Enter (and
    /// OpenAC's chat window hides) while "Hide retail chat" is ticked, the chat
    /// window is shown, and the player is logged in. RynthCore's real ChatHooks
    /// hooks acclient's chat; this one only keeps the flags.
    /// </summary>
    internal static class ChatHooks
    {
        public static bool SuppressOriginalChat;
        public static volatile bool ChatPanelShown;

        public static bool RynthChatOwnsChat =>
            SuppressOriginalChat && ChatPanelShown && LoginLifecycleHooks.HasObservedLoginComplete;
    }

    internal static class ClientHelperHooks
    {
        internal static Func<uint>? PlayerId { get; set; }

        public static uint GetPlayerId() => PlayerId?.Invoke() ?? 0;
    }

    internal static class ClientObjectHooks
    {
        internal delegate bool ItemTypeReader(uint objectId, out uint typeFlags);
        internal delegate bool NameReader(uint objectId, out string name);

        internal static ItemTypeReader? ItemType { get; set; }
        internal static NameReader? Name { get; set; }

        public static bool TryGetItemType(uint objectId, out uint typeFlags)
        {
            typeFlags = 0;
            return ItemType is { } read && read(objectId, out typeFlags);
        }

        public static bool TryGetObjectName(uint objectId, out string name)
        {
            name = string.Empty;
            return Name is { } read && read(objectId, out name);
        }
    }

    internal static class SelectedTargetHooks
    {
        internal static Func<uint>? SelectedId { get; set; }

        public static uint ReadCurrentSelectedId() => SelectedId?.Invoke() ?? 0;
    }

    internal static class SessionStateRegistry
    {
        internal static Func<string>? CharacterName { get; set; }

        internal static string LastCharacterName => CharacterName?.Invoke() ?? string.Empty;
    }
}

namespace RynthCore.Engine.UI
{
    /// <summary>RynthCore opens and closes panels through this; here, our panel host.</summary>
    internal static class PanelRouter
    {
        public static void Toggle(string title) => RynthCore.Engine.ImGuiBackend.ImGuiPanelHost.Toggle(title);
    }

    /// <summary>RynthAi's dashboard drawers; no RynthAi here, so nothing is ever moved.</summary>
    internal static class RynthAiDashboardState
    {
        public static bool DrawerMoved(string key) => false;

        public static void SetDrawerMoved(string key, bool moved)
        {
        }
    }

    /// <summary>RynthCore's Avalonia overlay flag; OpenAC has no Avalonia overlay.</summary>
    internal static class AvaloniaOverlay
    {
        internal static volatile bool ChatCtrlGatedClickThrough;
    }
}

namespace RynthCore.Engine.UI.Data
{
    /// <summary>Finds a loaded plugin's export by name; the host sets <see cref="Resolver"/>.</summary>
    internal static class PluginExportBinder
    {
        internal static Func<string, string, IntPtr>? Resolver { get; set; }

        public static IntPtr Resolve(string pluginNamePart, string exportName) =>
            Resolver?.Invoke(pluginNamePart, exportName) ?? IntPtr.Zero;
    }

    /// <summary>RynthCore's source hub; only the chat source exists here.</summary>
    internal static class UiSources
    {
        public static readonly ChatSource Chat = new();
    }

    /// <summary>RynthNav's "Arrow to" / "Go to"; no RynthNav in OpenAC.</summary>
    internal static class RynthNavCommands
    {
        public static void Arrow(string name, double ns, double ew)
        {
        }

        public static void Go(string name, double ns, double ew)
        {
        }
    }
}

namespace RynthCore.Engine.UI.Data.Discord
{
    /// <summary>RynthCore's Discord relay; off in OpenAC.</summary>
    internal static class ChatDiscord
    {
        internal static void PumpRead(IReadOnlyList<ChatLine> added)
        {
        }

        internal static void PumpSourceReset()
        {
        }

        internal static void PumpTick()
        {
        }

        public static string StatusLine() => "not available in OpenAC";

        public static string SwitchLine() => "Discord relay:";
    }
}

namespace RynthCore.Engine.ImGuiBackend
{
    /// <summary>
    /// Only the chat-input members of RynthCore's Win32Backend, which their
    /// ChatModel fills and reads. Their real Win32Backend subclasses acclient's
    /// window; this is a plain holder that OpenAC's keyboard sink
    /// (RynthChatKeyboard) drives on every OS.
    /// </summary>
    internal static class Win32Backend
    {
        public static volatile bool ChatCaptureActive;
        public static Action? OnChatCaptureActivated;
        public static Action? OnChatTellSelected;
        public static Action<char>? OnChatChar;
        public static Action? OnChatBackspace;
        public static Action? OnChatDelete;
        public static Action? OnChatSend;
        public static Action? OnChatCancel;
        public static Action? OnChatLeft;
        public static Action? OnChatRight;
        public static Action? OnChatHome;
        public static Action? OnChatEnd;
        public static Action? OnChatUp;
        public static Action? OnChatDown;
        public static Action? OnChatPaste;
        public static Action? OnChatCopy;
        public static Action? OnChatCut;

        /// <summary>The Tell button: run <see cref="OnChatTellSelected"/> on the next tick (RynthCore posts it).</summary>
        internal static bool TellRequested;

        public static void RequestTellSelected() => TellRequested = true;
    }

    /// <summary>RynthCore's pop-out windows; OpenAC has none.</summary>
    internal static class ImGuiPopOuts
    {
        public static bool InPopOutFrame => false;
    }
}

namespace RynthCore.Engine.ImGuiBackend.Panels
{
    /// <summary>The Discord relay's panel; off in OpenAC, so its switch draws nothing.</summary>
    internal static class ChatDiscordFace
    {
        public const string Title = "Discord";

        public static void DrawSwitch()
        {
        }
    }

    /// <summary>RynthNav's panel title; never registered here, so coordinate links stay hidden.</summary>
    internal static class RynthNavFace
    {
        public const string Title = "RynthNav";
    }
}
```

- [ ] **Step 3: Build the project**

Run: `~/.dotnet/dotnet build src/AcDream.Rynth.ChatWindow`
Expected: 0 errors. If the submodule moved since this plan and an error names a missing engine member, add that member to the right stand-in (shared ones in ScriptWindows `StandIns/`, chat-only ones here), keeping RynthCore's signature. Never edit their file.

- [ ] **Step 4: Write `RynthChatWindow.cs`**

```csharp
using System.Diagnostics;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.ImGuiBackend.Panels;
using RynthCore.Engine.UI.Data;

namespace AcDream.Rynth.ChatWindow;

/// <summary>
/// Runs RynthCore's chat window: Start does what RynthCore's EntryPoint does when
/// RynthChat is installed, Tick polls the chat source the way UiDataHub.Step does
/// (only while a face subscribes, only when due), Stop is UiDataHub's reset after
/// plugins unload. Game thread.
/// </summary>
internal static class RynthChatWindow
{
    private static bool _started;

    /// <summary>Their chat panel's title (the bar button and the layout key).</summary>
    internal const string Title = ChatFace.Title;

    internal static void Start()
    {
        if (_started)
            return;
        _started = true;
        ChatModel.EnsureSettingsLoaded();
        ChatModel.InstallInput();
        ChatFace.Register();
        ChatFiltersFace.Register();
    }

    /// <summary>One hub step for the chat source. Returns true when it polled.</summary>
    internal static bool Tick()
    {
        if (!_started)
            return false;
        ChatSource source = UiSources.Chat;
        source.PromoteDeferredRefresh();
        long now = Stopwatch.GetTimestamp();
        if (!source.HasSubscribers || !source.IsDue(now))
            return false;
        source.Poll();
        source.MarkPolled(now, Stopwatch.GetTimestamp() - now);
        return true;
    }

    internal static void Stop()
    {
        Win32Backend.ChatCaptureActive = false;
        Win32Backend.TellRequested = false;
        UiSources.Chat.Reset();
    }
}
```

`_started` stays true after `Stop` on purpose: their `InstallInput` and `Register` are once-per-process, as in RynthCore.

- [ ] **Step 5: Write the failing tests**

```csharp
using System.Text.RegularExpressions;
using AcDream.Rynth.ChatWindow;
using AcDream.TestSupport;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.UI.Data;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthChatWindowTests : IDisposable
{
    private readonly RynthConfigSandbox _sandbox = new("rynth-chatwin-");

    public RynthChatWindowTests() => ImGuiPanelHost.Reset();

    public void Dispose()
    {
        RynthChatWindow.Stop();
        PluginExportBinder.Resolver = null;
        ImGuiPanelHost.Reset();
        _sandbox.Dispose();
    }

    [Fact]
    public void StartRegistersTheChatAndFilterPanelsOnce()
    {
        RynthChatWindow.Start();
        RynthChatWindow.Start();
        Assert.True(ImGuiPanelHost.HasFace("Chat"));
        Assert.True(ImGuiPanelHost.HasFace("ChatFilters"));
    }

    [Fact]
    public void TickPollsOnlyWhileSubscribed()
    {
        RynthChatWindow.Start();
        Assert.False(RynthChatWindow.Tick());
        UiSources.Chat.Subscribe();
        try
        {
            UiSources.Chat.RequestRefresh();
            Assert.True(RynthChatWindow.Tick());
        }
        finally { UiSources.Chat.Unsubscribe(); }
    }

    [Fact]
    public void StopClearsCaptureAndTheSnapshot()
    {
        RynthChatWindow.Start();
        Win32Backend.ChatCaptureActive = true;
        RynthChatWindow.Stop();
        Assert.False(Win32Backend.ChatCaptureActive);
        Assert.Null(UiSources.Chat.Current);
    }

    [Fact]
    public void TheProjectCompilesTheirFilesAndHasNoCopies()
    {
        string root = RepoRoot();
        string project = File.ReadAllText(Path.Combine(root, "src/AcDream.Rynth.ChatWindow/AcDream.Rynth.ChatWindow.csproj"));
        string[] theirs =
        [
            @"UI\Data\Chat*.cs", @"UI\Data\UiSource.cs", @"UI\Panels\RynthChatPresets.cs",
            @"ImGui\Panels\ChatFace.cs", @"ImGui\Panels\ChatFiltersFace.cs", @"ImGui\Panels\FaceKit.cs",
            @"ImGui\Panels\DashboardDrawers.cs", @"ImGui\SafeImGui.cs", @"ImGui\RynthTheme.cs",
            @"Compatibility\TellMemory.cs",
        ];
        foreach (string file in theirs)
            Assert.Contains("$(RynthEngine)" + file, project, StringComparison.Ordinal);

        string engine = Path.Combine(root, "external/RynthCore/src/RynthCore.Engine");
        var names = Directory.GetFiles(Path.Combine(engine, "UI/Data"), "Chat*.cs").Select(Path.GetFileName)
            .Concat(["UiSource.cs", "RynthChatPresets.cs", "ChatFace.cs", "ChatFiltersFace.cs", "FaceKit.cs",
                "DashboardDrawers.cs", "SafeImGui.cs", "RynthTheme.cs", "TellMemory.cs"])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var copies = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(p => names.Contains(Path.GetFileName(p)))
            .ToList();
        Assert.Empty(copies);
    }

    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "AcDream.slnx")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repo root not found");
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthChatWindowTests"`
Expected: 4 passed. If `ChatModel.EnsureSettingsLoaded` writes outside the sandbox, the sandbox is wrong; fix the test setup, not their code.

- [ ] **Step 7: Run all host tests and commit**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests` → all pass.

```bash
git add AcDream.slnx src/AcDream.Rynth.ChatWindow src/AcDream.Rynth.Host/AcDream.Rynth.Host.csproj tests/AcDream.Rynth.Host.Tests/RynthChatWindowTests.cs
git commit -m "feat(rynth): RynthCore's chat window compiles unchanged against stand-ins"
```

---

### Task 4: The host runs the chat window end to end

**Goal:** `RynthClientExtension` loads RynthChat, points the chat stand-ins at the host and game view (`RynthChatHookup`), starts and ticks the chat window, and the bar shows a Chat button that opens it; a chat line reaches their chat source, and a line sent from their input reaches OpenAC's chat parser.

**Files:**
- Create: `src/AcDream.Rynth.Host/RynthChatHookup.cs`
- Modify: `src/AcDream.Rynth.Host/RynthClientExtension.cs`, `src/AcDream.Rynth.Host/RynthBar.cs`
- Test: `tests/AcDream.Rynth.Host.Tests/RynthChatWindowTests.cs`, `tests/AcDream.Rynth.Host.Tests/RynthBarTests.cs`

**Acceptance Criteria:**
- [ ] With the hookup attached to a host running RynthChat and the Chat panel open, a line given to the plugin appears in `UiSources.Chat.Current` after one tick.
- [ ] `PluginExportBinder.Resolve("RynthChat", "RynthChatSendLine")` is non-zero while the host runs and zero after it is disposed.
- [ ] `ClientHelperHooks.GetPlayerId()`, `SelectedTargetHooks.ReadCurrentSelectedId()`, `ClientObjectHooks.TryGetObjectName` and `SessionStateRegistry.LastCharacterName` answer from the view; a throwing view gives 0/false/"" and a log line, not an exception.
- [ ] Typing "hi" through `Win32Backend.OnChatChar` and running `OnChatSend` puts `hi` in the view's submitted commands (their ChatModel's send path, through RynthChat).
- [ ] The bar draws a Chat button when the chat window is registered, even with no script windows; clicking it toggles the Chat panel.
- [ ] Disposing the extension clears every stand-in delegate it set and calls `RynthChatWindow.Stop()`.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests` → all pass; `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~Rynth"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing end-to-end tests** (append to `RynthChatWindowTests`)

```csharp
    private readonly List<string> _log = new();
    private readonly FakeGameView _view = new() { PlayerId = 0x50000001u };

    [Fact]
    public void ALineReachesTheChatSource()
    {
        using var host = new RynthHost(_view, _log.Add, [typeof(RynthCore.Plugin.RynthChat.PluginExports)]);
        using var hookup = new RynthChatHookup(host, _view, _log.Add);
        RynthChatWindow.Start();
        UiSources.Chat.Subscribe();
        try
        {
            host.Plugins[0].OnChatWindowText("Bob says, \"hi\"", 2);
            UiSources.Chat.RequestRefresh();
            RynthChatWindow.Tick();
            Assert.Contains(UiSources.Chat.Current!.Value, line => line.Text.Contains("hi", StringComparison.Ordinal));
        }
        finally { UiSources.Chat.Unsubscribe(); }
    }

    [Fact]
    public void TheBinderFindsRynthChatOnlyWhileTheHostRuns()
    {
        var host = new RynthHost(_view, _log.Add, [typeof(RynthCore.Plugin.RynthChat.PluginExports)]);
        var hookup = new RynthChatHookup(host, _view, _log.Add);
        Assert.NotEqual(IntPtr.Zero, PluginExportBinder.Resolve("RynthChat", "RynthChatSendLine"));
        hookup.Dispose();
        host.Dispose();
        Assert.Equal(IntPtr.Zero, PluginExportBinder.Resolve("RynthChat", "RynthChatSendLine"));
    }

    [Fact]
    public void TheHooksAnswerFromTheView()
    {
        _view.SelectedObjectId = 0x7000_0001u;
        _view.Names[0x7000_0001u] = "Drudge";
        _view.Names[_view.PlayerId] = "Golem";
        using var host = new RynthHost(_view, _log.Add, []);
        using var hookup = new RynthChatHookup(host, _view, _log.Add);

        Assert.Equal(_view.PlayerId, RynthCore.Engine.Compatibility.ClientHelperHooks.GetPlayerId());
        Assert.Equal(0x7000_0001u, RynthCore.Engine.Compatibility.SelectedTargetHooks.ReadCurrentSelectedId());
        Assert.True(RynthCore.Engine.Compatibility.ClientObjectHooks.TryGetObjectName(0x7000_0001u, out string name));
        Assert.Equal("Drudge", name);
        Assert.Equal("Golem", RynthCore.Engine.Compatibility.SessionStateRegistry.LastCharacterName);
    }

    [Fact]
    public void AThrowingViewGivesEmptyAnswers()
    {
        using var host = new RynthHost(_view, _log.Add, []);
        using var hookup = new RynthChatHookup(host, _view, _log.Add);
        _view.Throw = true;
        Assert.Equal(0u, RynthCore.Engine.Compatibility.ClientHelperHooks.GetPlayerId());
        Assert.False(RynthCore.Engine.Compatibility.ClientObjectHooks.TryGetObjectName(1, out _));
        Assert.Equal("", RynthCore.Engine.Compatibility.SessionStateRegistry.LastCharacterName);
        Assert.Contains(_log, l => l.Contains("view failed", StringComparison.Ordinal));
    }

    [Fact]
    public void TypedTextIsSentThroughRynthChat()
    {
        using var host = new RynthHost(_view, _log.Add, [typeof(RynthCore.Plugin.RynthChat.PluginExports)]);
        using var hookup = new RynthChatHookup(host, _view, _log.Add);
        RynthChatWindow.Start();
        UiSources.Chat.Subscribe();
        try
        {
            UiSources.Chat.RequestRefresh();
            RynthChatWindow.Tick();   // binds SendLinePtr, as their pump does
            Win32Backend.ChatCaptureActive = true;
            Win32Backend.OnChatCaptureActivated!();
            Win32Backend.OnChatChar!('h');
            Win32Backend.OnChatChar!('i');
            Win32Backend.ChatCaptureActive = false;
            Win32Backend.OnChatSend!();
            Assert.Contains("hi", _view.SubmittedCommands);
        }
        finally { UiSources.Chat.Unsubscribe(); }
    }
```

If `ChatLine`'s text member has another name in their `ChatData.cs`, use theirs. If their send path writes "hi" as `/say hi` or similar, assert on what their code sends (read `ChatModel.OnSend` in `ChatData.cs`) and note it in the test name.

- [ ] **Step 2: Run to see them fail**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthChatWindowTests"`
Expected: build error, no `RynthChatHookup`.

- [ ] **Step 3: Write `RynthChatHookup.cs`**

```csharp
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.UI.Data;

namespace AcDream.Rynth.Host;

/// <summary>
/// Points the chat window's engine stand-ins at this host and game view, and
/// unpoints them on Dispose (only the ones still pointing here). Every answer
/// from the view is guarded: a failure logs and answers empty.
/// </summary>
internal sealed class RynthChatHookup : IDisposable
{
    private readonly RynthHost _host;
    private readonly IRynthGameView _view;
    private readonly Action<string> _log;
    private readonly Func<string, string, IntPtr> _resolver;
    private readonly Func<uint> _playerId;
    private readonly Func<uint> _selectedId;
    private readonly ClientObjectHooks.NameReader _name;
    private readonly ClientObjectHooks.ItemTypeReader _itemType;
    private readonly Func<string> _characterName;

    public RynthChatHookup(RynthHost host, IRynthGameView view, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(log);
        _host = host;
        _view = view;
        _log = log;
        _resolver = Resolve;
        _playerId = () => Guard(nameof(ClientHelperHooks.GetPlayerId), () => _view.PlayerId, 0u);
        _selectedId = () => Guard(nameof(SelectedTargetHooks.ReadCurrentSelectedId), () => _view.SelectedObjectId, 0u);
        _name = TryGetName;
        _itemType = TryGetItemType;
        _characterName = () => Guard(
            nameof(SessionStateRegistry.LastCharacterName),
            () => _view.TryGetObjectName(_view.PlayerId, out string name) ? name : string.Empty,
            string.Empty);

        PluginExportBinder.Resolver = _resolver;
        ClientHelperHooks.PlayerId = _playerId;
        SelectedTargetHooks.SelectedId = _selectedId;
        ClientObjectHooks.Name = _name;
        ClientObjectHooks.ItemType = _itemType;
        SessionStateRegistry.CharacterName = _characterName;
    }

    public void Dispose()
    {
        if (PluginExportBinder.Resolver == _resolver) PluginExportBinder.Resolver = null;
        if (ClientHelperHooks.PlayerId == _playerId) ClientHelperHooks.PlayerId = null;
        if (SelectedTargetHooks.SelectedId == _selectedId) SelectedTargetHooks.SelectedId = null;
        if (ClientObjectHooks.Name == _name) ClientObjectHooks.Name = null;
        if (ClientObjectHooks.ItemType == _itemType) ClientObjectHooks.ItemType = null;
        if (SessionStateRegistry.CharacterName == _characterName) SessionStateRegistry.CharacterName = null;
    }

    private IntPtr Resolve(string pluginNamePart, string exportName)
    {
        foreach (RynthPlugin plugin in _host.Plugins)
        {
            if (plugin.Name.Contains(pluginNamePart, StringComparison.OrdinalIgnoreCase)
                && plugin.TryGetExport(exportName, out IntPtr pointer))
                return pointer;
        }

        return IntPtr.Zero;
    }

    private bool TryGetName(uint objectId, out string name)
    {
        string found = string.Empty;
        bool ok = Guard(nameof(ClientObjectHooks.TryGetObjectName), () => _view.TryGetObjectName(objectId, out found), false);
        name = ok ? found : string.Empty;
        return ok;
    }

    private bool TryGetItemType(uint objectId, out uint typeFlags)
    {
        RynthObjectInfo? info = null;
        bool ok = Guard(nameof(ClientObjectHooks.TryGetItemType), () => _view.TryGetObject(objectId, out info), false);
        typeFlags = ok && info is not null ? info.ItemType : 0u;
        return ok && info is not null;
    }

    private T Guard<T>(string what, Func<T> read, T fallback)
    {
        try { return read(); }
        catch (Exception error)
        {
            _log($"[Rynth] chat {what}: {error.GetType().Name}: {error.Message}");
            return fallback;
        }
    }
}
```

Adjust `RynthObjectInfo`'s nullability to how it is declared (record vs. class, `out` pattern) so it builds clean with analyzers on. If the host needs to know its plugins are gone after `Dispose`, `TryGetExport` already answers false (Task 1).

- [ ] **Step 4: Run the end-to-end tests**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthChatWindowTests"`
Expected: all pass.

- [ ] **Step 5: Panel buttons on the bar — failing test** (append to `RynthBarTests`; follow its existing setup for the layout store and headless frame)

```csharp
    [Fact]
    public void AChatPanelButtonShowsWithoutScriptWindowsAndToggles()
    {
        ImGuiPanelHost.Register("Chat", new PanelSpec(new Vector2(460, 300), new Vector2(260, 140)), () => new NullPanel());
        var bar = new RynthBar(_layout, _log.Add) { PanelButtons = () => ["Chat"] };
        Assert.True(bar.HasAnyContent);
        bar.ClickPanel("Chat");
        Assert.True(ImGuiPanelHost.IsOpen("Chat"));
        bar.ClickPanel("Chat");
        Assert.False(ImGuiPanelHost.IsOpen("Chat"));
    }
```

(`NullPanel` is a minimal `IImGuiPanel` with an empty `Draw`; add it to the test file if none exists.)

- [ ] **Step 6: Implement panel buttons in `RynthBar`**

```csharp
    /// <summary>RynthCore panels with a bar button (the chat window), by panel title. Only registered ones are drawn.</summary>
    internal Func<IReadOnlyList<string>>? PanelButtons { get; init; }

    private IEnumerable<string> RegisteredPanels =>
        (PanelButtons?.Invoke() ?? []).Where(ImGuiPanelHost.HasFace);

    /// <summary>True when anything is on the bar: a panel button or a script window.</summary>
    internal bool HasAnyContent => RegisteredPanels.Any() || HasContent;

    /// <summary>Opens or closes a panel, as a click on its button does.</summary>
    internal void ClickPanel(string title) => ImGuiPanelHost.Toggle(title);
```

In `Draw`, replace `!HasContent` with `!HasAnyContent`. In `DrawContents`, after the drag handle and before the script-window loop, draw one button per registered panel exactly like a script-window button (same open highlight, `RynthBarIcons.Label(title) + "##panel" + title`, `Tooltip(title)`), calling `ClickPanel(title)` after the style pop.

- [ ] **Step 7: Wire the extension** (`RynthClientExtension`)

- Add `typeof(RynthCore.Plugin.RynthChat.PluginExports)` to the plugin list after RynthInventory.
- Fields: `private RynthChatHookup? _chatHookup;`.
- In `Attach`, after `_host` is created: `_chatHookup = new RynthChatHookup(_host, view, context.Log);` and, when `context.Overlay is not null`, `RynthChatWindow.Start();`.
- Create the bar with `PanelButtons = () => [RynthChatWindow.Title]`.
- `Tick`: after `_host?.Tick()`, call `GuardChat(() => RynthChatWindow.Tick())` where `GuardChat` catches and logs `[Rynth] chat: …`.
- `Dispose`: before `_host?.Dispose()`, `RynthChatWindow.Stop(); _chatHookup?.Dispose(); _chatHookup = null;`.
- Update the class summary to name RynthChat and the chat window.

- [ ] **Step 8: Run all host and Rynth App tests**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests` → all pass.
Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~Rynth"` → all pass. (A test that counts loaded plugins may need its expected count raised by one; that is the only expected change.)

- [ ] **Step 9: Commit**

```bash
git add src/AcDream.Rynth.Host tests/AcDream.Rynth.Host.Tests tests/AcDream.App.Tests
git commit -m "feat(rynth): the client runs RynthChat and RynthCore's chat window, with a bar button"
```

---

### Task 5: The overlay gives extensions a keyboard sink and the clipboard

**Goal:** `IClientOverlay` gains `AddKeyboardSink` and `ClipboardText`; `ImGuiOverlay` reports `WantsKeyboard` live while any sink captures (so OpenAC's input ignores keys), forwards key-downs, key-ups and characters to sinks, and reads/writes the system clipboard through the window's keyboard.

**Files:**
- Create: `src/AcDream.ClientExtensions/IOverlayKeyboardSink.cs`, `src/AcDream.App/Extensions/Overlay/OverlayKeyMap.cs`
- Modify: `src/AcDream.ClientExtensions/IClientOverlay.cs`, `src/AcDream.App/Extensions/Overlay/ImGuiOverlay.cs`, `src/AcDream.App/Extensions/Overlay/ImGuiInputFeed.cs`
- Test: `tests/AcDream.App.Tests/Extensions/ImGuiOverlayTests.cs`

**Acceptance Criteria:**
- [ ] With a sink whose `Capturing` is true, `overlay.WantsKeyboard` is true immediately (no frame needed); false again as soon as `Capturing` turns false.
- [ ] `ImGuiInputFeed` forwards Enter key-down as `OverlayKey.Enter`, `V` with Control held as `(OverlayKey.V, OverlayModifiers.Control)`, Super (Cmd) counted as Control, Shift as `Shift`, and characters unchanged, to every sink, whether or not it captures.
- [ ] A sink that throws is removed and logged; other sinks keep receiving keys.
- [ ] Disposing the registration removes the sink.
- [ ] `ClipboardText` set then get round-trips through the feed's keyboard; with no keyboard it reads `""` and ignores writes.
- [ ] `OverlayKeyMap.Map(Key.Unknown)` is `OverlayKey.Other`.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ImGuiOverlay|FullyQualifiedName~OverlayKeyMap"` → all pass

**Steps:**

- [ ] **Step 1: Add the contract** (`IOverlayKeyboardSink.cs`)

```csharp
namespace AcDream.ClientExtensions;

/// <summary>The keys an overlay keyboard sink can tell apart; everything else is <see cref="Other"/>.</summary>
public enum OverlayKey
{
    Other,
    Enter,
    Escape,
    Backspace,
    Delete,
    Left,
    Right,
    Up,
    Down,
    Home,
    End,
    Insert,
    C,
    V,
    X,
}

/// <summary>Modifiers held with a key. Control includes Command on macOS.</summary>
[Flags]
public enum OverlayModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
}

/// <summary>
/// Raw keyboard for an extension that runs its own text entry on the overlay.
/// Every key and character goes to every sink; while a sink reports
/// <see cref="Capturing"/>, the client's own input ignores the keyboard.
/// Game thread.
/// </summary>
public interface IOverlayKeyboardSink
{
    /// <summary>Read on every key: true while this sink owns the keyboard.</summary>
    bool Capturing { get; }

    void KeyDown(OverlayKey key, OverlayModifiers modifiers);

    void KeyUp(OverlayKey key);

    void Character(char value);
}
```

Add to `IClientOverlay` (default implementations so other overlays keep compiling):

```csharp
    /// <summary>Adds a keyboard sink. Dispose the result to remove it.</summary>
    IDisposable AddKeyboardSink(IOverlayKeyboardSink sink) => NoKeyboardSink.Instance;

    /// <summary>The system clipboard's text; "" when there is none or no keyboard.</summary>
    string ClipboardText
    {
        get => string.Empty;
        set { }
    }
```

and below the interface:

```csharp
/// <summary>The registration an overlay without keyboard sinks returns.</summary>
internal sealed class NoKeyboardSink : IDisposable
{
    public static readonly NoKeyboardSink Instance = new();

    public void Dispose()
    {
    }
}
```

- [ ] **Step 2: Write the failing tests** (append to `ImGuiOverlayTests`; add a `RecordingSink` nested class)

```csharp
    private sealed class RecordingSink : AcDream.ClientExtensions.IOverlayKeyboardSink
    {
        public bool Capturing { get; set; }
        public bool ThrowOnKey { get; set; }
        public List<(AcDream.ClientExtensions.OverlayKey, AcDream.ClientExtensions.OverlayModifiers)> Downs { get; } = new();
        public List<char> Chars { get; } = new();
        public void KeyDown(AcDream.ClientExtensions.OverlayKey key, AcDream.ClientExtensions.OverlayModifiers modifiers)
        {
            if (ThrowOnKey) throw new InvalidOperationException("sink failed");
            Downs.Add((key, modifiers));
        }
        public void KeyUp(AcDream.ClientExtensions.OverlayKey key) { }
        public void Character(char value) => Chars.Add(value);
    }

    [Fact]
    public void ACapturingSinkWantsTheKeyboardAtOnce()
    {
        var sink = new RecordingSink();
        using IDisposable registration = _overlay.AddKeyboardSink(sink);
        Assert.False(_overlay.WantsKeyboard);
        sink.Capturing = true;
        Assert.True(_overlay.WantsKeyboard);
        sink.Capturing = false;
        Assert.False(_overlay.WantsKeyboard);
    }

    [Fact]
    public void KeysAndCharactersReachEverySink()
    {
        var a = new RecordingSink();
        var b = new RecordingSink { Capturing = true };
        using IDisposable ra = _overlay.AddKeyboardSink(a);
        using IDisposable rb = _overlay.AddKeyboardSink(b);

        _overlay.SinkKeyDown(Key.Enter, ctrl: false, shift: false);
        _overlay.SinkKeyDown(Key.V, ctrl: true, shift: false);
        _overlay.SinkCharacter('h');

        foreach (RecordingSink sink in new[] { a, b })
        {
            Assert.Equal((AcDream.ClientExtensions.OverlayKey.Enter, AcDream.ClientExtensions.OverlayModifiers.None), sink.Downs[0]);
            Assert.Equal((AcDream.ClientExtensions.OverlayKey.V, AcDream.ClientExtensions.OverlayModifiers.Control), sink.Downs[1]);
            Assert.Equal(['h'], sink.Chars);
        }
    }

    [Fact]
    public void AThrowingSinkIsDroppedAndOthersCarryOn()
    {
        var bad = new RecordingSink { ThrowOnKey = true };
        var good = new RecordingSink();
        _overlay.AddKeyboardSink(bad);
        using IDisposable r = _overlay.AddKeyboardSink(good);
        _overlay.SinkKeyDown(Key.Enter, false, false);
        _overlay.SinkKeyDown(Key.Enter, false, false);
        Assert.Equal(2, good.Downs.Count);
        Assert.Single(_log, l => l.Contains("sink failed", StringComparison.Ordinal));
    }

    [Fact]
    public void DisposingTheRegistrationRemovesTheSink()
    {
        var sink = new RecordingSink { Capturing = true };
        _overlay.AddKeyboardSink(sink).Dispose();
        Assert.False(_overlay.WantsKeyboard);
        _overlay.SinkKeyDown(Key.Enter, false, false);
        Assert.Empty(sink.Downs);
    }

    [Fact]
    public void ClipboardWithoutAKeyboardIsEmpty()
    {
        _overlay.ClipboardText = "ignored";
        Assert.Equal("", _overlay.ClipboardText);
    }

    [Fact]
    public void ClipboardRoundTripsThroughTheKeyboard()
    {
        var keyboard = new FakeClipboardKeyboard();
        _overlay.ClipboardSource = keyboard;
        _overlay.ClipboardText = "hello";
        Assert.Equal("hello", keyboard.ClipboardText);
        Assert.Equal("hello", _overlay.ClipboardText);
    }
```

And in the `ImGuiKeyMap` test class at the bottom of the file, add `[Fact] public void OverlayUnknownKeyIsOther() => Assert.Equal(AcDream.ClientExtensions.OverlayKey.Other, OverlayKeyMap.Map(Key.Unknown));`.

`FakeClipboardKeyboard` is a minimal `IKeyboard` with a settable `ClipboardText`. Every other member throws `NotSupportedException` or is a no-op event. Put it in the test file.

- [ ] **Step 3: Run to see them fail**

Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ImGuiOverlay"`
Expected: build errors (`AddKeyboardSink`, `SinkKeyDown`, `ClipboardSource` missing).

- [ ] **Step 4: Implement `OverlayKeyMap`**

```csharp
using AcDream.ClientExtensions;
using Silk.NET.Input;

namespace AcDream.App.Extensions.Overlay;

/// <summary>Silk.NET keys → the keys an overlay keyboard sink tells apart.</summary>
internal static class OverlayKeyMap
{
    public static OverlayKey Map(Key key) => key switch
    {
        Key.Enter => OverlayKey.Enter,
        Key.Escape => OverlayKey.Escape,
        Key.Backspace => OverlayKey.Backspace,
        Key.Delete => OverlayKey.Delete,
        Key.Left => OverlayKey.Left,
        Key.Right => OverlayKey.Right,
        Key.Up => OverlayKey.Up,
        Key.Down => OverlayKey.Down,
        Key.Home => OverlayKey.Home,
        Key.End => OverlayKey.End,
        Key.Insert => OverlayKey.Insert,
        Key.C => OverlayKey.C,
        Key.V => OverlayKey.V,
        Key.X => OverlayKey.X,
        _ => OverlayKey.Other,
    };
}
```

Numpad Enter (`Key.KeypadEnter`) maps to `Other` on purpose. RynthCore never captures it either.

- [ ] **Step 5: Implement sinks and clipboard in `ImGuiOverlay`**

- Field `private readonly List<IOverlayKeyboardSink> _sinks = new();` and `private bool _imguiWantsKeyboard;`.
- `BeginFrame` sets `_imguiWantsKeyboard = io.WantCaptureKeyboard;` (and `false` where it now sets `WantsKeyboard = false`).
- `public bool WantsKeyboard => _imguiWantsKeyboard || AnySinkCapturing();` where `AnySinkCapturing` loops over a snapshot of `_sinks` and treats a throwing `Capturing` like a throwing key (remove + log).
- `public IDisposable AddKeyboardSink(IOverlayKeyboardSink sink)` adds and returns a disposable that removes it (idempotent).
- `internal void SinkKeyDown(Key key, bool ctrl, bool shift)`, `internal void SinkKeyUp(Key key)`, `internal void SinkCharacter(char value)`: map with `OverlayKeyMap`, build `OverlayModifiers`, call every sink in a snapshot; on exception remove that sink and `_log($"[ImGui] keyboard sink removed: {error.GetType().Name}: {error.Message}")`.
- `internal IKeyboard? ClipboardSource { get; set; }` and

```csharp
    public string ClipboardText
    {
        get => ClipboardSource?.ClipboardText ?? string.Empty;
        set
        {
            if (ClipboardSource is { } keyboard)
                keyboard.ClipboardText = value ?? string.Empty;
        }
    }
```

- `Dispose` clears `_sinks` and `ClipboardSource`.

- [ ] **Step 6: Feed the sinks from `ImGuiInputFeed`**

Give the feed the overlay: change the constructor to `ImGuiInputFeed(IInputContext input, ImGuiInputQueue queue, IView? window = null, ImGuiOverlay? sinks = null)` and store it. In the constructor set `sinks.ClipboardSource = input.Keyboards.FirstOrDefault()` when `sinks` is not null. In `ImGuiOverlay.Start` (line ~246), pass `this`. Then:

```csharp
    private void OnKeyDown(IKeyboard keyboard, Key key, int scancode)
    {
        _queue.KeyChange(key, true);
        _sinks?.SinkKeyDown(key, Held(keyboard, Key.ControlLeft, Key.ControlRight, Key.SuperLeft, Key.SuperRight), Held(keyboard, Key.ShiftLeft, Key.ShiftRight));
    }

    private void OnKeyUp(IKeyboard keyboard, Key key, int scancode)
    {
        _queue.KeyChange(key, false);
        _sinks?.SinkKeyUp(key);
    }

    private void OnKeyChar(IKeyboard keyboard, char value)
    {
        _queue.Character(value);
        _sinks?.SinkCharacter(value);
    }

    private static bool Held(IKeyboard keyboard, params Key[] keys) => keys.Any(keyboard.IsKeyPressed);
```

In `Dispose`, if `_sinks?.ClipboardSource` is this feed's keyboard, set it to null.

- [ ] **Step 7: Run the tests**

Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ImGuiOverlay|FullyQualifiedName~OverlayKeyMap"`
Expected: all pass. Also run `~/.dotnet/dotnet build src/AcDream.App` → 0 warnings, 0 errors.

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.ClientExtensions src/AcDream.App/Extensions/Overlay tests/AcDream.App.Tests/Extensions/ImGuiOverlayTests.cs
git commit -m "feat(overlay): extensions get a keyboard sink and the clipboard"
```

---

### Task 6: OpenAC's chat seam for extensions

**Goal:** Extensions get `IClientChat` on their context: a claim on the chat-entry action (`ChatStartCommand`, Enter by default) that runs before OpenAC's chat bar opens, and a switch that hides OpenAC's main chat window.

**Files:**
- Create: `src/AcDream.ClientExtensions/IClientChat.cs`, `src/AcDream.App/Extensions/ClientChatSeam.cs`
- Modify: `src/AcDream.ClientExtensions/ClientExtensionContext.cs`, `src/AcDream.App/Extensions/ClientExtensionHost.cs`, `src/AcDream.App/UI/RetailUiRuntime.cs`, `src/AcDream.App/UI/Layout/ChatWindowController.cs`, `src/AcDream.App/Rendering/GameWindow.cs`
- Test: `tests/AcDream.App.Tests/Extensions/ClientChatSeamTests.cs`

**Acceptance Criteria:**
- [ ] `TryClaimEntry()` is false with no claims, true when a claim returns true, and stops at the first claim that returns true.
- [ ] A throwing claim is removed and logged, and counts as false.
- [ ] Disposing a claim's registration removes it.
- [ ] `SetChatWindowHidden(true)` makes `ChatWindowHidden` true. The retail UI tick hides the chat window root (`Visible == false`), and `false` shows it again. A chat window that was already hidden for another reason is left hidden.
- [ ] In `RetailUiRuntime`, `ChatStartCommand` with a claim that returns true does not call `StartCommand` and still returns true (handled).
- [ ] `ClientExtensionContext.Chat` reaches extensions (non-null in the client).

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ClientChatSeam"` → all pass; `~/.dotnet/dotnet build src/AcDream.App` → 0 warnings

**Steps:**

- [ ] **Step 1: Add the contract** (`IClientChat.cs`)

```csharp
namespace AcDream.ClientExtensions;

/// <summary>
/// The client's chat, for an extension that runs its own chat window.
/// Game thread.
/// </summary>
public interface IClientChat
{
    /// <summary>
    /// Adds a claim on the chat-entry key (the ChatStartCommand action, Enter
    /// by default). When the player presses it, claims run in order before the
    /// client's chat bar opens; the first that returns true takes the key and
    /// the chat bar stays shut. Dispose the result to remove the claim.
    /// </summary>
    IDisposable ClaimChatEntry(Func<bool> tryClaim);

    /// <summary>Hides (true) or shows again (false) the client's main chat window.</summary>
    void SetChatWindowHidden(bool hidden);
}
```

Add `public IClientChat? Chat { get; init; }` to `ClientExtensionContext` with the doc comment "The client's chat; null without a retail UI (tests, headless)."

- [ ] **Step 2: Write the failing tests**

```csharp
using AcDream.App.Extensions;

namespace AcDream.App.Tests.Extensions;

public sealed class ClientChatSeamTests
{
    private readonly List<string> _log = new();

    [Fact]
    public void NoClaimsMeansTheClientKeepsTheKey()
    {
        var seam = new ClientChatSeam(_log.Add);
        Assert.False(seam.TryClaimEntry());
    }

    [Fact]
    public void TheFirstClaimThatSaysYesWins()
    {
        var seam = new ClientChatSeam(_log.Add);
        int second = 0;
        seam.ClaimChatEntry(() => false);
        seam.ClaimChatEntry(() => true);
        seam.ClaimChatEntry(() => { second++; return true; });
        Assert.True(seam.TryClaimEntry());
        Assert.Equal(0, second);
    }

    [Fact]
    public void AThrowingClaimIsDroppedAndCountsAsNo()
    {
        var seam = new ClientChatSeam(_log.Add);
        seam.ClaimChatEntry(() => throw new InvalidOperationException("claim failed"));
        Assert.False(seam.TryClaimEntry());
        Assert.False(seam.TryClaimEntry());
        Assert.Single(_log, l => l.Contains("claim failed", StringComparison.Ordinal));
    }

    [Fact]
    public void DisposingAClaimRemovesIt()
    {
        var seam = new ClientChatSeam(_log.Add);
        seam.ClaimChatEntry(() => true).Dispose();
        Assert.False(seam.TryClaimEntry());
    }

    [Fact]
    public void HiddenIsRemembered()
    {
        var seam = new ClientChatSeam(_log.Add);
        seam.SetChatWindowHidden(true);
        Assert.True(seam.ChatWindowHidden);
        seam.SetChatWindowHidden(false);
        Assert.False(seam.ChatWindowHidden);
    }
}
```

Add a test for `ChatWindowController.SetExtensionHidden` in the existing chat window controller tests (`grep -rln "ChatWindowController" tests/AcDream.App.Tests` to find them). Bind a controller the way those tests do, then:

```csharp
    [Fact]
    public void ExtensionHiddenHidesAndRestoresTheWindow()
    {
        ChatWindowController controller = BindController();   // the file's existing helper
        controller.SetExtensionHidden(true);
        Assert.False(controller.Root.Visible);
        controller.SetExtensionHidden(false);
        Assert.True(controller.Root.Visible);
    }

    [Fact]
    public void AWindowHiddenBeforeStaysHidden()
    {
        ChatWindowController controller = BindController();
        controller.Root.Visible = false;
        controller.SetExtensionHidden(true);
        controller.SetExtensionHidden(false);
        Assert.False(controller.Root.Visible);
    }
```

- [ ] **Step 3: Run to see them fail**

Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ClientChatSeam|FullyQualifiedName~ChatWindowController"`
Expected: build errors.

- [ ] **Step 4: Implement `ClientChatSeam`**

```csharp
using AcDream.ClientExtensions;

namespace AcDream.App.Extensions;

/// <summary>
/// The client side of <see cref="IClientChat"/>: claims on the chat-entry key,
/// asked by the retail UI before it opens its chat bar, and whether an
/// extension wants the main chat window hidden. Game thread.
/// </summary>
internal sealed class ClientChatSeam : IClientChat
{
    private readonly Action<string> _log;
    private readonly List<Func<bool>> _claims = new();

    public ClientChatSeam(Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    public bool ChatWindowHidden { get; private set; }

    public IDisposable ClaimChatEntry(Func<bool> tryClaim)
    {
        ArgumentNullException.ThrowIfNull(tryClaim);
        _claims.Add(tryClaim);
        return new Registration(() => _claims.Remove(tryClaim));
    }

    public void SetChatWindowHidden(bool hidden) => ChatWindowHidden = hidden;

    /// <summary>True when an extension took the chat-entry key.</summary>
    public bool TryClaimEntry()
    {
        foreach (Func<bool> claim in _claims.ToArray())
        {
            try
            {
                if (claim())
                    return true;
            }
            catch (Exception error)
            {
                _claims.Remove(claim);
                _log($"[Extensions] chat-entry claim removed: {error.GetType().Name}: {error.Message}");
            }
        }

        return false;
    }

    private sealed class Registration(Action remove) : IDisposable
    {
        private Action? _remove = remove;

        public void Dispose() => Interlocked.Exchange(ref _remove, null)?.Invoke();
    }
}
```

- [ ] **Step 5: `ChatWindowController.SetExtensionHidden`**

```csharp
    private bool _extensionHidden;
    private bool _visibleBeforeExtensionHid;

    /// <summary>
    /// An extension's chat window took over (IClientChat.SetChatWindowHidden):
    /// hide this one, and on release put back whatever it was before.
    /// </summary>
    internal void SetExtensionHidden(bool hidden)
    {
        if (hidden == _extensionHidden)
            return;
        _extensionHidden = hidden;
        if (hidden)
        {
            _visibleBeforeExtensionHid = Root.Visible;
            Root.Visible = false;
        }
        else
        {
            Root.Visible = _visibleBeforeExtensionHid;
        }
    }
```

Check how the window is toggled by the player (`grep -n "Visible" src/AcDream.App/UI/RetailUiRuntime.cs | grep -i chat`). If a toggle could show it while hidden, make that toggle a no-op while `_extensionHidden`.

- [ ] **Step 6: `RetailUiRuntime`**

- Add `internal ClientChatSeam? ExtensionChat { get; set; }`.
- In the `ChatStartCommand` case:

```csharp
            case AcDream.UI.Abstractions.Input.InputAction.ChatStartCommand:
                if (ExtensionChat?.TryClaimEntry() == true)
                    return true;
                _chatWindowController?.StartCommand();
                return true;
```

- In the per-frame block, next to `_chatWindowController?.UpdateUnreadIndicator();`, add `_chatWindowController?.SetExtensionHidden(ExtensionChat?.ChatWindowHidden == true);`.

- [ ] **Step 7: Wire it in `GameWindow` and `ClientExtensionHost`**

- `GameWindow`: field `private readonly AcDream.App.Extensions.ClientChatSeam _chatSeam = new(line => Serilog.Log.Information("{ExtensionLine}", line));`. Wherever `_retailUiRuntime` is assigned a runtime (`grep -n "_retailUiRuntime =" src/AcDream.App/Rendering/GameWindow.cs`), set `_retailUiRuntime.ExtensionChat = _chatSeam;` right after. Pass `chat: _chatSeam` to `_extensions.Attach(...)` (line ~538).
- `ClientExtensionHost.Attach`: new optional parameter `IClientChat? chat = null`, set `Chat = chat` in the context.

- [ ] **Step 8: Run the tests and build**

Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ClientChatSeam|FullyQualifiedName~ChatWindowController|FullyQualifiedName~RetailUiRuntime"` → all pass.
Run: `~/.dotnet/dotnet build src/AcDream.App` → 0 warnings, 0 errors.

- [ ] **Step 9: Commit**

```bash
git add src/AcDream.ClientExtensions src/AcDream.App tests/AcDream.App.Tests
git commit -m "feat(extensions): chat seam: claim the chat-entry key, hide the chat window"
```

---

### Task 7: Typing in the Rynth chat window

**Goal:** `RynthChatKeyboard` turns overlay keys into their `Win32Backend.OnChat*` calls exactly as RynthCore's `Win32Backend` does, swallows the Enter/Esc tail, defers the send to the next tick, does paste/copy/cut through the overlay clipboard off Windows, claims OpenAC's chat-entry key under their ownership rule, and hides OpenAC's chat window while Rynth chat owns chat.

**Files:**
- Create: `src/AcDream.Rynth.Host/RynthChatKeyboard.cs`
- Modify: `src/AcDream.Rynth.Host/RynthClientExtension.cs`
- Test: `tests/AcDream.Rynth.Host.Tests/RynthChatKeyboardTests.cs`

**Acceptance Criteria:**
- [ ] Not capturing: keys and characters do nothing.
- [ ] Capturing: Backspace, Delete, Left, Right, Home, End, Up and Down call the matching hook. A printable character calls `OnChatChar`. Control characters (< 0x20 and 0x7F) are not typed.
- [ ] Enter while capturing clears `ChatCaptureActive` at once, keeps `Capturing` true until Enter's key-up, and calls `OnChatSend` on the next `Tick`, not before.
- [ ] Esc while capturing clears capture, calls `OnChatCancel` at once, and keeps `Capturing` true until Esc's key-up.
- [ ] Ctrl+V / Shift+Insert, Ctrl+C / Ctrl+Insert and Ctrl+X: on Windows they call `OnChatPaste/Copy/Cut`. Elsewhere, paste feeds the clipboard text through `OnChatChar` (control characters dropped), copy writes `ChatModel.InputText` to the clipboard, and cut copies then calls `OnChatCancel` while staying in capture.
- [ ] `Win32Backend.TellRequested` runs `OnChatTellSelected` on the next tick and clears.
- [ ] `TryClaimEntry` is false unless `ChatHooks.RynthChatOwnsChat`. When it owns chat and capture is off, it sets `ChatCaptureActive` and calls `OnChatCaptureActivated` and returns true. Already capturing → false.
- [ ] The Enter that was just claimed is not a send, even when the overlay hands it to the sink after OpenAC's input handled it (handler order is not guaranteed): an Enter key-down before the next `Tick` after a claim is ignored.
- [ ] Each tick the extension calls `IClientChat.SetChatWindowHidden(ChatHooks.RynthChatOwnsChat)` (only when it changes).
- [ ] A throwing hook is caught and logged; the sink never throws.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthChatKeyboardTests"` → all pass; then `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

```csharp
using AcDream.ClientExtensions;
using AcDream.Rynth.Host;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.ImGuiBackend;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthChatKeyboardTests : IDisposable
{
    private readonly List<string> _calls = new();
    private readonly List<string> _log = new();
    private string _clipboard = "";
    private readonly RynthChatKeyboard _keys;

    public RynthChatKeyboardTests()
    {
        Win32Backend.ChatCaptureActive = false;
        Win32Backend.OnChatChar = c => _calls.Add("char:" + c);
        Win32Backend.OnChatBackspace = () => _calls.Add("back");
        Win32Backend.OnChatDelete = () => _calls.Add("del");
        Win32Backend.OnChatLeft = () => _calls.Add("left");
        Win32Backend.OnChatRight = () => _calls.Add("right");
        Win32Backend.OnChatHome = () => _calls.Add("home");
        Win32Backend.OnChatEnd = () => _calls.Add("end");
        Win32Backend.OnChatUp = () => _calls.Add("up");
        Win32Backend.OnChatDown = () => _calls.Add("down");
        Win32Backend.OnChatSend = () => _calls.Add("send");
        Win32Backend.OnChatCancel = () => _calls.Add("cancel");
        Win32Backend.OnChatPaste = () => _calls.Add("paste");
        Win32Backend.OnChatCopy = () => _calls.Add("copy");
        Win32Backend.OnChatCut = () => _calls.Add("cut");
        Win32Backend.OnChatCaptureActivated = () => _calls.Add("activated");
        Win32Backend.OnChatTellSelected = () => _calls.Add("tell");
        _keys = new RynthChatKeyboard(_log.Add, () => _clipboard, text => _clipboard = text, isWindows: false, inputText: () => "typed");
    }

    public void Dispose()
    {
        Win32Backend.ChatCaptureActive = false;
        Win32Backend.TellRequested = false;
        ChatHooks.SuppressOriginalChat = false;
        ChatHooks.ChatPanelShown = false;
        LoginLifecycleHooks.HasObservedLoginComplete = false;
    }

    [Fact]
    public void NothingHappensWhileNotCapturing()
    {
        _keys.KeyDown(OverlayKey.Backspace, OverlayModifiers.None);
        _keys.Character('a');
        Assert.Empty(_calls);
        Assert.False(_keys.Capturing);
    }

    [Theory]
    [InlineData(OverlayKey.Backspace, "back")]
    [InlineData(OverlayKey.Delete, "del")]
    [InlineData(OverlayKey.Left, "left")]
    [InlineData(OverlayKey.Right, "right")]
    [InlineData(OverlayKey.Home, "home")]
    [InlineData(OverlayKey.End, "end")]
    [InlineData(OverlayKey.Up, "up")]
    [InlineData(OverlayKey.Down, "down")]
    public void EditingKeysReachTheirHooks(OverlayKey key, string call)
    {
        Win32Backend.ChatCaptureActive = true;
        _keys.KeyDown(key, OverlayModifiers.None);
        Assert.Equal([call], _calls);
    }

    [Fact]
    public void PrintableCharactersAreTypedAndControlCharactersAreNot()
    {
        Win32Backend.ChatCaptureActive = true;
        _keys.Character('h');
        _keys.Character('\r');
        _keys.Character((char)0x7F);
        Assert.Equal(["char:h"], _calls);
    }

    [Fact]
    public void EnterSendsOnTheNextTickAndSwallowsItsTail()
    {
        Win32Backend.ChatCaptureActive = true;
        _keys.KeyDown(OverlayKey.Enter, OverlayModifiers.None);
        Assert.False(Win32Backend.ChatCaptureActive);
        Assert.True(_keys.Capturing);
        Assert.Empty(_calls);
        _keys.Tick();
        Assert.Equal(["send"], _calls);
        _keys.KeyUp(OverlayKey.Enter);
        Assert.False(_keys.Capturing);
    }

    [Fact]
    public void EscapeCancelsAtOnceAndSwallowsItsTail()
    {
        Win32Backend.ChatCaptureActive = true;
        _keys.KeyDown(OverlayKey.Escape, OverlayModifiers.None);
        Assert.False(Win32Backend.ChatCaptureActive);
        Assert.Equal(["cancel"], _calls);
        Assert.True(_keys.Capturing);
        _keys.KeyUp(OverlayKey.Escape);
        Assert.False(_keys.Capturing);
    }

    [Fact]
    public void OffWindowsPasteTypesTheClipboard()
    {
        _clipboard = "a\nb";
        Win32Backend.ChatCaptureActive = true;
        _keys.KeyDown(OverlayKey.V, OverlayModifiers.Control);
        Assert.Equal(["char:a", "char:b"], _calls);
    }

    [Fact]
    public void OffWindowsCopyAndCutUseTheClipboard()
    {
        Win32Backend.ChatCaptureActive = true;
        _keys.KeyDown(OverlayKey.C, OverlayModifiers.Control);
        Assert.Equal("typed", _clipboard);
        _clipboard = "";
        _keys.KeyDown(OverlayKey.X, OverlayModifiers.Control);
        Assert.Equal("typed", _clipboard);
        Assert.Equal(["cancel"], _calls);
        Assert.True(Win32Backend.ChatCaptureActive);
    }

    [Fact]
    public void OnWindowsClipboardKeysUseTheirHooks()
    {
        var keys = new RynthChatKeyboard(_log.Add, () => "", _ => { }, isWindows: true, inputText: () => "");
        Win32Backend.ChatCaptureActive = true;
        keys.KeyDown(OverlayKey.V, OverlayModifiers.Control);
        keys.KeyDown(OverlayKey.Insert, OverlayModifiers.Shift);
        keys.KeyDown(OverlayKey.C, OverlayModifiers.Control);
        keys.KeyDown(OverlayKey.X, OverlayModifiers.Control);
        Assert.Equal(["paste", "paste", "copy", "cut"], _calls);
    }

    [Fact]
    public void TellRunsOnTheNextTick()
    {
        Win32Backend.RequestTellSelected();
        Assert.Empty(_calls);
        _keys.Tick();
        Assert.Equal(["tell"], _calls);
        _keys.Tick();
        Assert.Single(_calls);
    }

    [Fact]
    public void TheClaimFollowsTheirOwnershipRule()
    {
        Assert.False(_keys.TryClaimEntry());
        ChatHooks.SuppressOriginalChat = true;
        ChatHooks.ChatPanelShown = true;
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        Assert.True(_keys.TryClaimEntry());
        Assert.True(Win32Backend.ChatCaptureActive);
        Assert.Equal(["activated"], _calls);
        Assert.False(_keys.TryClaimEntry());
    }

    [Fact]
    public void TheClaimedEnterIsNotASend()
    {
        ChatHooks.SuppressOriginalChat = true;
        ChatHooks.ChatPanelShown = true;
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        Assert.True(_keys.TryClaimEntry());
        _keys.KeyDown(OverlayKey.Enter, OverlayModifiers.None);   // the same keypress, reaching the sink second
        _keys.Tick();
        Assert.True(Win32Backend.ChatCaptureActive);
        Assert.Equal(["activated"], _calls);
        _keys.KeyDown(OverlayKey.Enter, OverlayModifiers.None);   // a later Enter sends
        _keys.Tick();
        Assert.Equal(["activated", "send"], _calls);
    }

    [Fact]
    public void AThrowingHookIsLoggedNotThrown()
    {
        Win32Backend.OnChatBackspace = () => throw new InvalidOperationException("hook failed");
        Win32Backend.ChatCaptureActive = true;
        _keys.KeyDown(OverlayKey.Backspace, OverlayModifiers.None);
        Assert.Contains(_log, l => l.Contains("hook failed", StringComparison.Ordinal));
    }
}
```

- [ ] **Step 2: Run to see them fail**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthChatKeyboardTests"`
Expected: build error, no `RynthChatKeyboard`.

- [ ] **Step 3: Implement `RynthChatKeyboard`**

```csharp
using AcDream.ClientExtensions;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.ImGuiBackend;

namespace AcDream.Rynth.Host;

/// <summary>
/// OpenAC's keyboard for RynthCore's chat input line: the key handling of
/// RynthCore's Win32Backend (which subclasses acclient's window and is not
/// used here), done over the overlay's keyboard sink on every OS. Keys go to
/// their ChatModel through the Win32Backend stand-in's OnChat* hooks. Enter
/// and Esc end capture at once but keep the keyboard until they come back up
/// (their _swallowEnterTail), so the same keypress never reaches OpenAC. The
/// send runs on the next tick (their deferred WM_RYNTHCORE_CHAT). Off Windows,
/// paste/copy/cut use the overlay clipboard: their ChatModel uses the Win32
/// clipboard. Game thread.
/// </summary>
internal sealed class RynthChatKeyboard : IOverlayKeyboardSink
{
    private readonly Action<string> _log;
    private readonly Func<string> _getClipboard;
    private readonly Action<string> _setClipboard;
    private readonly bool _isWindows;
    private readonly Func<string> _inputText;
    private OverlayKey? _swallowUntilUp;
    private bool _sendPending;
    private bool _justClaimed;

    public RynthChatKeyboard(Action<string> log, Func<string> getClipboard, Action<string> setClipboard, bool isWindows, Func<string> inputText)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(getClipboard);
        ArgumentNullException.ThrowIfNull(setClipboard);
        ArgumentNullException.ThrowIfNull(inputText);
        _log = log;
        _getClipboard = getClipboard;
        _setClipboard = setClipboard;
        _isWindows = isWindows;
        _inputText = inputText;
    }

    public bool Capturing => Win32Backend.ChatCaptureActive || _swallowUntilUp is not null;

    public void KeyDown(OverlayKey key, OverlayModifiers modifiers)
    {
        if (!Win32Backend.ChatCaptureActive)
            return;
        // The keypress OpenAC just handed us through the claim may reach this sink
        // after OpenAC's input (handler order isn't fixed): it started typing, it
        // doesn't send.
        if (_justClaimed && key == OverlayKey.Enter)
            return;
        bool ctrl = (modifiers & OverlayModifiers.Control) != 0;
        bool shift = (modifiers & OverlayModifiers.Shift) != 0;
        switch (key)
        {
            case OverlayKey.V when ctrl:
            case OverlayKey.Insert when shift && !ctrl:
                Paste();
                break;
            case OverlayKey.C when ctrl:
            case OverlayKey.Insert when ctrl:
                Copy();
                break;
            case OverlayKey.X when ctrl:
                Cut();
                break;
            case OverlayKey.Enter:
                Win32Backend.ChatCaptureActive = false;
                _swallowUntilUp = OverlayKey.Enter;
                _sendPending = true;
                break;
            case OverlayKey.Escape:
                Win32Backend.ChatCaptureActive = false;
                _swallowUntilUp = OverlayKey.Escape;
                Run(Win32Backend.OnChatCancel, "cancel");
                break;
            case OverlayKey.Backspace: Run(Win32Backend.OnChatBackspace, "backspace"); break;
            case OverlayKey.Delete: Run(Win32Backend.OnChatDelete, "delete"); break;
            case OverlayKey.Left: Run(Win32Backend.OnChatLeft, "left"); break;
            case OverlayKey.Right: Run(Win32Backend.OnChatRight, "right"); break;
            case OverlayKey.Home: Run(Win32Backend.OnChatHome, "home"); break;
            case OverlayKey.End: Run(Win32Backend.OnChatEnd, "end"); break;
            case OverlayKey.Up: Run(Win32Backend.OnChatUp, "up"); break;
            case OverlayKey.Down: Run(Win32Backend.OnChatDown, "down"); break;
        }
    }

    public void KeyUp(OverlayKey key)
    {
        if (_swallowUntilUp == key)
            _swallowUntilUp = null;
    }

    public void Character(char value)
    {
        if (Win32Backend.ChatCaptureActive)
            Type(value);
    }

    /// <summary>Runs the deferred send and Tell. Call once per tick.</summary>
    public void Tick()
    {
        _justClaimed = false;
        if (_sendPending)
        {
            _sendPending = false;
            Run(Win32Backend.OnChatSend, "send");
        }

        if (Win32Backend.TellRequested)
        {
            Win32Backend.TellRequested = false;
            Run(Win32Backend.OnChatTellSelected, "tell");
        }
    }

    /// <summary>
    /// OpenAC's chat-entry key, offered first (IClientChat.ClaimChatEntry):
    /// taken only under their rule (ChatHooks.RynthChatOwnsChat), as their
    /// Win32Backend takes Enter.
    /// </summary>
    public bool TryClaimEntry()
    {
        if (Win32Backend.ChatCaptureActive || !ChatHooks.RynthChatOwnsChat || Win32Backend.OnChatCaptureActivated is null)
            return false;
        Win32Backend.ChatCaptureActive = true;
        _justClaimed = true;
        Run(Win32Backend.OnChatCaptureActivated, "activate");
        return true;
    }

    private void Type(char value)
    {
        if (value < 0x20 || value == (char)0x7F)
            return;
        Action<char>? hook = Win32Backend.OnChatChar;
        if (hook is null)
            return;
        try { hook(value); }
        catch (Exception error) { Fail("char", error); }
    }

    private void Paste()
    {
        if (_isWindows)
        {
            Run(Win32Backend.OnChatPaste, "paste");
            return;
        }

        foreach (char c in _getClipboard())
            Type(c);
    }

    private void Copy()
    {
        if (_isWindows)
        {
            Run(Win32Backend.OnChatCopy, "copy");
            return;
        }

        string text = _inputText();
        if (text.Length > 0)
            _setClipboard(text);
    }

    private void Cut()
    {
        if (_isWindows)
        {
            Run(Win32Backend.OnChatCut, "cut");
            return;
        }

        string text = _inputText();
        if (text.Length == 0)
            return;
        _setClipboard(text);
        Run(Win32Backend.OnChatCancel, "cut");
    }

    private void Run(Action? hook, string what)
    {
        if (hook is null)
            return;
        try { hook(); }
        catch (Exception error) { Fail(what, error); }
    }

    private void Fail(string what, Exception error) =>
        _log($"[Rynth] chat keyboard {what}: {error.GetType().Name}: {error.Message}");
}
```

- [ ] **Step 4: Run the keyboard tests**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthChatKeyboardTests"`
Expected: all pass.

- [ ] **Step 5: Wire the extension**

In `RynthClientExtension`:
- Fields: `private RynthChatKeyboard? _chatKeyboard; private IDisposable? _keyboardSink; private IDisposable? _chatClaim; private IClientChat? _clientChat; private bool? _chatHidden;`.
- In `Attach`, inside the `context.Overlay is { } overlay` block, after `RynthChatWindow.Start()`:

```csharp
            _chatKeyboard = new RynthChatKeyboard(
                context.Log,
                () => overlay.ClipboardText,
                text => overlay.ClipboardText = text,
                OperatingSystem.IsWindows(),
                () => RynthCore.Engine.UI.Data.ChatModel.InputText);
            _keyboardSink = overlay.AddKeyboardSink(_chatKeyboard);
            if (context.Chat is { } chat)
            {
                _clientChat = chat;
                _chatClaim = chat.ClaimChatEntry(_chatKeyboard.TryClaimEntry);
            }
```

- In `Tick`, after the chat window tick: `_chatKeyboard?.Tick();`, then

```csharp
        if (_clientChat is { } clientChat)
        {
            bool hide = RynthCore.Engine.Compatibility.ChatHooks.RynthChatOwnsChat;
            if (_chatHidden != hide)
            {
                _chatHidden = hide;
                clientChat.SetChatWindowHidden(hide);
            }
        }
```

  (all inside the same `GuardChat`).
- In `Dispose`, first: `_chatClaim?.Dispose(); _keyboardSink?.Dispose();`, then if `_chatHidden == true` call `_clientChat?.SetChatWindowHidden(false)`, then null all five fields.

- [ ] **Step 6: Extension wiring test** (append to `tests/AcDream.App.Tests/Extensions/RynthOverlayExtensionTests.cs`; give its `FakeOverlay` an `AddKeyboardSink` that records sinks, and add a `FakeChat : IClientChat` that records claims and hidden calls)

```csharp
    [Fact]
    public void TheChatKeyboardAndClaimAreHookedUpUntilDispose()
    {
        var overlay = new FakeOverlay();
        var chat = new FakeChat();
        var extension = new RynthClientExtension();
        ClientExtensionContext context = Context(GameRuntimeTestFactory.Create(), overlay) with { Chat = chat };
        extension.Attach(context);
        Assert.Single(overlay.Sinks);
        Assert.Single(chat.Claims);

        extension.Dispose();
        Assert.Empty(overlay.Sinks);
        Assert.Empty(chat.Claims);
    }
```

If `ClientExtensionContext` is not a record, build the context with `Chat = chat` set directly in a new `Context` overload instead of `with`.

- [ ] **Step 7: Run everything Rynth**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests` → all pass.
Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~Rynth|FullyQualifiedName~ImGuiOverlay|FullyQualifiedName~ClientChatSeam"` → all pass.
Run: `~/.dotnet/dotnet build -c Release src/AcDream.App` → 0 warnings, 0 errors.

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.Rynth.Host tests/AcDream.Rynth.Host.Tests tests/AcDream.App.Tests
git commit -m "feat(rynth): typing in RynthCore's chat window through the overlay keyboard"
```

---

### Task 8: Live check on local ACE

**Goal:** In a live OpenAC client on the local ACE server, the Rynth chat window works end to end: tabs fill, settings persist, typing and sending work under their ownership rule, and OpenAC's chat behaves as decided.

> **USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in `acceptanceCriteria` has been re-validated independently, with output captured.

**Files:**
- Modify: `docs/superpowers/plans/2026-10-18-rynth-chat.md` (status note at the top)

**Acceptance Criteria:**
- [ ] The user said yes to launching before the client started.
- [ ] The bar shows a Chat button; clicking it opens RynthCore's chat window with channel tabs; local chat and system lines appear in the right tabs.
- [ ] A filter rule and a line colour set in the window are still there after a client restart.
- [ ] "Hide retail chat" unticked: OpenAC's chat window shows, Enter opens OpenAC's chat bar, and the Rynth input box does not start typing.
- [ ] "Hide retail chat" ticked: OpenAC's chat window hides; Enter starts typing in Rynth chat; a typed line is said in game; `/help` (or another `/` command) runs; Esc leaves typing and WASD moves the character again.
- [ ] Closing the Rynth chat window brings OpenAC's chat window back.
- [ ] On macOS: Cmd+V pastes into the Rynth input line. Copying chat lines logs one "not available on this system" line and the window stays open.

**Verify:** each criterion observed in the running client, results written into the plan's status note

**Steps:**

- [ ] **Step 1: Release build and tests**

Run: `~/.dotnet/dotnet build -c Release AcDream.slnx` → 0 errors.
Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests` → all pass.

- [ ] **Step 2: Ask the user before launching** (follow the live-testing memory: local ACE 10.10.20.20 only, never Dreamweave, never print passwords; `source ~/OpenAC-dev/dev-client.env` inside a script that maps `ACDREAM_PLAYER_USER/PASS` to `ACDREAM_TEST_USER/PASS` without echoing; `ACDREAM_DAT_DIR=/Users/davidsmith/AsheronsCall`; `ACDREAM_PAK_PATH="$HOME/Library/Application Support/OpenAC/data/pak/acdream.pak"`; run `~/.dotnet/dotnet src/AcDream.App/bin/Release/net10.0/AcDream.App.dll /Users/davidsmith/AsheronsCall`). "CharacterList not received" on first launch: relaunch.

- [ ] **Step 3: Walk the acceptance criteria with the user**, one at a time, noting pass/fail and any log lines (`grep -i "rynth\|chat" <log>`).

- [ ] **Step 4: Write the results** as a status note at the top of this plan (same style as the RynthInventory plan's), and commit:

```bash
git add docs/superpowers/plans/2026-10-18-rynth-chat.md
git commit -m "docs: RynthChat live check results"
```
