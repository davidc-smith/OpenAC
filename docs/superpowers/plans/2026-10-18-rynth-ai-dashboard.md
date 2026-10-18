# RynthAi Dashboard (RynthAi sub-project #1) Implementation Plan

> **Status (2026-10-18):** Tasks 1–5 done, each reviewed, plus a final whole-branch review and one fix wave (re-review clean). Task 6 (live check on local ACE) not yet run: the user deferred it and asked to merge.
> Corrections made during execution: `LoadedPlugin.VersionString` landed in Task 2, not Task 4. RynthAi's snapshot export faulted on osx-arm64 (.NET 10.0.8 can give an 8-byte `[ThreadStatic]` a 4-byte-aligned address, and `Interlocked` faults there). Tried and dropped: AggressiveOptimization on exports, trampoline exports, and `TieredCompilationQuickJitForLoops=false` (that one only worked by shifting thread-static layout). The fix is the redirect tool's `--plain-threadstatic-swaps`, which turns their two `Interlocked.Exchange` calls on thread statics into plain swaps at build time, guarded by `RynthAiInteropTests.NoInterlockedOnAThreadStaticIsLeft`. The dashboard pump isolates each source's faults the way RynthCore's `UiDataHub.Step` does.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** RynthSuite's RynthAi plugin and RynthCore's RynthAi dashboard (`RynthAiFace`, its drawers and settings) run inside OpenAC, compiled unchanged from the submodules, with a "RynthAi" button on the Rynth bar. The bot takes no game actions yet: every action slot stays unset, so their code reads it as "not available".

**Architecture:** A wrapper project compiles the RynthAi plugin, with LootSdk and TerrainData, as plain net10.0. After compile, our Cecil redirect tool turns its Win32 P/Invokes into inert managed methods: `VirtualQuery` answers "not readable", so the fellowship memory reads never touch OpenAC's process. `AcDream.Rynth.ChatWindow` is renamed `AcDream.Rynth.EngineUi`, because RynthCore's engine UI must live in one assembly: `UiSources`, `PanelRouter` and friends are static classes their faces share. It then also compiles the dashboard files against stand-ins for the engine pieces they link to. The host points those stand-ins at itself, pumps the dashboard each tick, and adds the bar button and RynthAi's chat commands.

**Tech Stack:** .NET 10 (`~/.dotnet/dotnet`), ImGui.NET 1.91.6.1, Mono.Cecil (tools/RynthInteropRedirect), xunit v3 (VSTest runner: `--filter "FullyQualifiedName~X"`), RynthCore/RynthSuite submodules.

**Spec:** No separate spec file; the user asked to go straight to the plan. The design was agreed in conversation (2026-10-18) and is summarised under "User decisions" below. RynthAi sub-project split: #1 this plan (compile + dashboard, no actions); #2 action/state slots through `IAutomationSurface`; #3 combat and buffs live; #4 navigation + Nav3D; #5 loot/salvage/vendor/trade; #6 dat raycasting and dungeon pathfinder.

## Global Constraints

- Repo `~/code/OpenAC/OpenAC-Rynth`. Create branch `feat/rynth-ai-dashboard` from `main` before Task 1. Do not push.
- Use `~/.dotnet/dotnet` (the system `dotnet` lacks SDK 10.0.300).
- **Never edit, copy or move anything inside `external/RynthCore` or `external/RynthSuite`.** Wrapper projects include files from there by path. When their code needs an engine type we don't compile, add or grow a stand-in in our project; never change their call. `git status --porcelain external` must stay empty.
- RynthCore's `Plugins/PluginManager.cs`, `UI/Data/UiDataHub.cs`, `UI/Data/UiSources.cs`, `EntryPoint.cs`, `D3D9/**`, `ImGui/Win32Backend.cs`, `UI/Panels/MonstersPanel.cs` (Avalonia), `ImGui/Panels/RangesSlideOut.cs`, `ImGui/Panels/LootEditorFace.cs`, `ImGui/Panels/DungeonMapFace.cs`, `ImGui/Panels/WorldMapFace.cs`, `ImGui/Panels/SettingsFace.cs` and `ImGui/MonsterHud/**` are **not compiled** in this plan. Our stand-ins of those names are plain classes with no P/Invoke.
- `AcDream.App`, `AcDream.ClientExtensions`, `AcDream.Rynth.Host` and `tools/RynthInteropRedirect` build with warnings as errors and analyzers on, so new code there must build clean. `AcDream.Rynth.EngineUi`, `AcDream.Rynth.ScriptWindows` and the plugin wrappers have warnings-as-errors and analyzers off (their code).
- Exceptions never cross from a stand-in, the dashboard pump or a plugin export into OpenAC: catch, log through the host log, carry on.
- No action slot is filled in this plan. `RynthApiTable` is not touched.
- Tests that load RynthAi run inside `RynthConfigSandbox` (Task 2 makes it also set `RYNTHCORE_DIR`/`RYNTHSUITE_DIR`). Rynth host tests run without parallelism (assembly attribute in `FakeGameView.cs`); App tests use `[Collection("Rynth")]`.
- Known unrelated failures exist in `AcDream.App.Tests` (dat files, live server). Run App tests with the filters each task's Verify shows. `AcDream.Rynth.Host.Tests` must be fully green.
- Never launch a client without asking the user first; only the local ACE server 10.10.20.20, never Dreamweave; never print passwords (`~/OpenAC-dev/dev-client.env`).

**User decisions (already made):**
- Automation target is RynthAi (the full bot), split into the six sub-projects above; this plan is #1.
- Run their code unchanged and keep it pullable: "we are just trying to get rynth running in our client, we are not trying to rewrite their plugin. we cant make changes to their code, and we need to be able to pull in their latest daily changes".
- #1 compiles the **core dashboard with stand-ins at the edges**: `RynthAiFace`, its drawers and settings are real. The loot editor, maps, characters, hotkeys, Monsters/Ranges and pop-outs are stand-ins until their sub-project.
- Rename `AcDream.Rynth.ChatWindow` to `AcDream.Rynth.EngineUi` (one assembly for RynthCore's engine UI).
- Win32 calls are handled at build time by the existing Cecil redirect tool (as for the chat clipboard), never by editing their code.

## Probe results this plan relies on (scratch work, 2026-10-18)

- RynthAi + LootSdk + TerrainData + the eight `Plugins/Shared` files compile as plain net10.0 against `AcDream.Rynth.Sdk` with **0 errors**.
- Loaded in `RynthHost` (no action slots), RynthAi initialises ("the engine's panels draw the UI"), ticks, shuts down cleanly. Its snapshot export answers before login (`{"macroRunning":false,…}`) and `RynthPluginVersion` answers `0.5.0-legacy-ui`.
- The dashboard file set in Task 4, with exactly the stand-ins in Task 4, compiles with **0 errors**. `RynthAiFace` registers and draws five headless frames (about 1,940 vertices each) with no failure logged.
- The P/Invokes left in RynthAi are kernel32 (`VirtualQuery`, `GetModuleHandleExW`, `GetModuleFileNameW`, `CloseHandle`, `WaitForSingleObject`, `TerminateProcess`, `GetProcessId`), user32 (`EnumWindows`, `GetWindowThreadProcessId`, `IsWindowVisible`, `PostMessage`), shell32 (`ShellExecuteExW`) and winmm (`PlaySound`). Every call site already treats failure as "not available". The advapi32 registry read in `RynthInstallPaths` stays real (guarded; the environment variable wins anyway).
- RynthAi loads resources by fixed names (`RynthCore.Plugin.RynthAi.Combat.SpellData.txt`, `….CreatureData.creature_resists.tsv`, `….CreatureData.creature_types.txt`, `….Data.creature_weakness.json`), so the wrapper must give those exact `LogicalName`s.
- RynthAi's chat commands are `/ra`, `/mt`, `/ub`, `/vt` (their README's `/na` is stale). OpenAC registers none of them.

---

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/AcDream.Rynth.ChatWindow/` → `src/AcDream.Rynth.EngineUi/` | rename | RynthCore's engine UI faces (chat window, now also the RynthAi dashboard) |
| `src/AcDream.Rynth.EngineUi/AcDream.Rynth.EngineUi.csproj` | rename + modify | compiles their chat and dashboard files; runs the clipboard redirect |
| `src/AcDream.Rynth.EngineUi/StandIns/ChatEngineStandIns.cs` | modify | `UiSources` gains the dashboard's sources; `PanelRouter`, `Win32Backend` grow; the old `RynthAiDashboardState` stand-in goes (their real file is compiled) |
| `src/AcDream.Rynth.EngineUi/StandIns/DashboardStandIns.cs` | create | stand-ins for the engine pieces the dashboard links to (`PluginManager`, `UiDataHub`, `EntryPoint`, `D3D9`, faces not compiled, `RangesSlideOut`, …) |
| `src/AcDream.Rynth.EngineUi/RynthAiDashboard.cs` | create | start (as their EntryPoint), per-tick pump (as their UiDataHub), stop |
| `src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiStandIns.cs` | modify | `ImGuiFonts.CurrentStep/BakedStepNear/RequestRebuild`, `PanelSpec.BorderColor/Rounding` |
| `src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiPanelHost.cs` | modify | `PlacementScale`, `DragWindowWithLastItem`, pop-out members, `TextSizeRows` |
| `src/AcDream.Rynth.ScriptWindows/StandIns/EngineStandIns.cs` | modify | `LoadedPlugin.VersionString`, three `EngineSettings`, `AutoIdMode` |
| `src/AcDream.Rynth.ScriptWindows/AcDream.Rynth.ScriptWindows.csproj` | modify | `InternalsVisibleTo` the renamed project |
| `src/AcDream.Rynth.Plugin.RynthAi/AcDream.Rynth.Plugin.RynthAi.csproj` | create | compile RynthAi (+ LootSdk, TerrainData, shared files) from the submodule; inert redirect |
| `tools/RynthInteropRedirect/Program.cs` | modify | optional `--inert <modules>`: unmatched P/Invokes in those modules become inert; stand-in type may be `-` |
| `src/AcDream.Rynth.Host/RynthInstallFolders.cs` | create | off Windows, points `RYNTHCORE_DIR`/`RYNTHSUITE_DIR` at ApplicationData when unset |
| `src/AcDream.Rynth.Host/RynthHost.cs` | modify | records each plugin's `RynthPluginVersion` on its owner |
| `src/AcDream.Rynth.Host/RynthAiHookup.cs` | create | points the dashboard stand-ins at the host and view; unpoints on Dispose |
| `src/AcDream.Rynth.Host/RynthClientExtension.cs` | modify | loads RynthAi, starts/ticks/stops the dashboard, bar button, `/ra /mt /ub /vt` |
| `src/AcDream.Rynth.Host/AcDream.Rynth.Host.csproj` | modify | references the renamed project and the RynthAi wrapper |
| `AcDream.slnx` | modify | renamed project, new wrapper |
| `tests/AcDream.Rynth.Host.Tests/RynthConfigSandbox.cs` | modify | also sandboxes `RYNTHCORE_DIR`/`RYNTHSUITE_DIR` |
| `tests/AcDream.Rynth.Host.Tests/RynthAiPluginTests.cs` | create | RynthAi loads, ticks, version, resources, install folders |
| `tests/AcDream.Rynth.Host.Tests/RynthAiInteropTests.cs` | create | no Win32 P/Invoke left; fellowship reads see nothing readable |
| `tests/AcDream.Rynth.Host.Tests/RynthAiDashboardTests.cs` | create | file list / no copies, headless draw, pump, macro toggle round trip, hookup |
| `tests/AcDream.App.Tests/Extensions/RynthOverlayExtensionTests.cs` | modify | rename; bar offers RynthAi; commands registered and released |
| `**/packages.*.lock.json` | modify | project rename and new wrapper |

---

### Task 1: Rename AcDream.Rynth.ChatWindow to AcDream.Rynth.EngineUi

**Goal:** The project that compiles RynthCore's engine UI is called `AcDream.Rynth.EngineUi`. Behaviour is identical: same files, same stand-ins, same clipboard redirect, every existing test green.

**Files:**
- Rename: `src/AcDream.Rynth.ChatWindow/` → `src/AcDream.Rynth.EngineUi/`, and its csproj → `AcDream.Rynth.EngineUi.csproj`
- Modify: `src/AcDream.Rynth.EngineUi/AcDream.Rynth.EngineUi.csproj` (header comment, redirect stand-in type name)
- Modify: `src/AcDream.Rynth.EngineUi/RynthChatWindow.cs`, `src/AcDream.Rynth.EngineUi/StandIns/Win32ClipboardStandIn.cs` (namespace)
- Modify: `AcDream.slnx`, `src/AcDream.Rynth.Host/AcDream.Rynth.Host.csproj`, `src/AcDream.Rynth.Host/RynthClientExtension.cs`, `src/AcDream.Rynth.ScriptWindows/AcDream.Rynth.ScriptWindows.csproj`
- Modify: `tests/AcDream.Rynth.Host.Tests/RynthChatWindowTests.cs`, `tests/AcDream.Rynth.Host.Tests/Win32ClipboardStandInTests.cs`, `tests/AcDream.App.Tests/Extensions/RynthOverlayExtensionTests.cs`
- Modify: lock files that name the project (`src/AcDream.App`, `tests/AcDream.App.Tests`, `tests/AcDream.Core.Tests`, `tests/AcDream.HostParity.Tests`, `tests/AcDream.Rynth.Host.Tests` `packages.neutral.lock.json`)

**Acceptance Criteria:**
- [ ] `git grep -n "Rynth.ChatWindow" -- ':!docs'` prints nothing.
- [ ] `~/.dotnet/dotnet build AcDream.slnx` succeeds; the build log shows `RynthInteropRedirect: AcDream.Rynth.EngineUi.dll: 11 P/Invoke(s) redirected to AcDream.Rynth.EngineUi.Win32ClipboardStandIn`.
- [ ] `AcDream.Rynth.Host.Tests` fully green; App `RynthOverlayExtensionTests` green.
- [ ] `git status --porcelain external` empty.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests && ~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~RynthOverlayExtensionTests"` → all pass

**Steps:**

- [ ] **Step 1: Branch**

```bash
git switch -c feat/rynth-ai-dashboard main
```

- [ ] **Step 2: Move the project**

```bash
git mv src/AcDream.Rynth.ChatWindow src/AcDream.Rynth.EngineUi
git mv src/AcDream.Rynth.EngineUi/AcDream.Rynth.ChatWindow.csproj src/AcDream.Rynth.EngineUi/AcDream.Rynth.EngineUi.csproj
rm -rf src/AcDream.Rynth.EngineUi/bin src/AcDream.Rynth.EngineUi/obj
```

- [ ] **Step 3: Replace the name everywhere outside docs**

```bash
git grep -l "AcDream.Rynth.ChatWindow" -- ':!docs' | xargs sed -i '' 's/AcDream\.Rynth\.ChatWindow/AcDream.Rynth.EngineUi/g'
git grep -n "AcDream.Rynth.ChatWindow\|Rynth.ChatWindow" -- ':!docs'
```

Expected: the second command prints nothing. This changes the namespaces in `RynthChatWindow.cs` and `Win32ClipboardStandIn.cs`, the `using` lines in `RynthClientExtension.cs`, `RynthChatWindowTests.cs`, `Win32ClipboardStandInTests.cs`, the two qualified names in `RynthOverlayExtensionTests.cs`, the path in `RynthChatWindowTests.TheProjectCompilesTheirFilesAndHasNoCopies`, the redirect `Exec` argument, the ScriptWindows `InternalsVisibleTo`, the Host `ProjectReference`, the `AcDream.slnx` entry and the lock files.

The `AcDream.slnx` entry keeps its place: `EngineUi` still sorts between `ChatWindow`'s neighbours and `src/AcDream.Rynth.Host`.

- [ ] **Step 4: Update the project's header comment** (first comment in `src/AcDream.Rynth.EngineUi/AcDream.Rynth.EngineUi.csproj`)

Replace:

```xml
  <!-- RynthCore's ImGui chat window (ChatFace, the filter editor, ChatData and
       friends), compiled unchanged from the submodule against our stand-ins for
       the engine types they use (StandIns/, plus the shared ones in
       AcDream.Rynth.ScriptWindows). RynthCore's Win32Backend, ChatHooks,
       UiSources, UiDataHub, PluginExportBinder and Discord relay are NOT
       compiled: the stand-ins of those names are plain classes. Their code,
       their warnings: warnings-as-errors and analyzers are off here. -->
```

with:

```xml
  <!-- RynthCore's engine UI faces, compiled unchanged from the submodule against
       our stand-ins for the engine types they use (StandIns/, plus the shared
       ones in AcDream.Rynth.ScriptWindows): the ImGui chat window (ChatFace, the
       filter editor, ChatData and friends). One assembly, as in RynthCore: their
       faces share static engine classes (UiSources, PanelRouter, ...).
       RynthCore's Win32Backend, ChatHooks, UiSources, UiDataHub,
       PluginExportBinder and Discord relay are NOT compiled: the stand-ins of
       those names are plain classes. Their code, their warnings:
       warnings-as-errors and analyzers are off here. -->
```

- [ ] **Step 5: Restore and build**

```bash
~/.dotnet/dotnet restore AcDream.slnx --force-evaluate
~/.dotnet/dotnet build AcDream.slnx 2>&1 | grep -E "RynthInteropRedirect|error|Build succeeded"
git status --porcelain
```

Expected: build succeeds; the redirect line names `AcDream.Rynth.EngineUi.dll` and 11 P/Invokes. `git status` shows only the rename, the edits above and lock files. Every lock-file change must be the `AcDream.Rynth.ChatWindow` → `AcDream.Rynth.EngineUi` entry. If restore changes anything else in a lock file (versions, other packages), revert that file with `git checkout -- <file>`, re-apply only the rename by hand with sed, and say so in the report.

- [ ] **Step 6: Run the tests**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests && ~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~RynthOverlayExtensionTests"`
Expected: all pass (same counts as on `main`).

- [ ] **Step 7: Commit**

```bash
git add -A src/AcDream.Rynth.ChatWindow src/AcDream.Rynth.EngineUi AcDream.slnx src/AcDream.Rynth.Host src/AcDream.Rynth.ScriptWindows tests $(git ls-files -m '*packages.*.lock.json')
git status --porcelain external
git commit -m "refactor(rynth): AcDream.Rynth.ChatWindow becomes AcDream.Rynth.EngineUi"
```

---

### Task 2: RynthAi compiles from the submodule and loads in the host

**Goal:** A wrapper project compiles RynthAi unchanged. `RynthHost` loads it, ticks it, records its version, and shuts it down. Off Windows its install folders point at ApplicationData rather than `C:\Games\…`.

**Files:**
- Create: `src/AcDream.Rynth.Plugin.RynthAi/AcDream.Rynth.Plugin.RynthAi.csproj`
- Create: `src/AcDream.Rynth.Host/RynthInstallFolders.cs`
- Modify: `AcDream.slnx`, `src/AcDream.Rynth.Host/AcDream.Rynth.Host.csproj`, `src/AcDream.Rynth.Host/RynthHost.cs`
- Modify: `tests/AcDream.Rynth.Host.Tests/RynthConfigSandbox.cs`
- Test: `tests/AcDream.Rynth.Host.Tests/RynthAiPluginTests.cs`

**Acceptance Criteria:**
- [ ] `RynthHost` with `typeof(RynthCore.Plugin.RynthAi.PluginExports)` has one active plugin whose name contains `RynthAi`, and it survives 30 `Tick()` calls without logging an exception.
- [ ] Its `Owner.VersionString` equals what its `RynthPluginVersion` export returns and is not empty.
- [ ] Its `RynthPluginGetSnapshotJson` export returns JSON containing `"macroRunning":false` before login.
- [ ] The assembly carries the four resources under the names their code loads.
- [ ] `RynthInstallFolders.Apply`: off Windows sets both variables under ApplicationData when unset; leaves set ones alone; does nothing on Windows. Our variable names equal their `RynthInstallPaths` constants.
- [ ] `git status --porcelain external` empty.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthAiPluginTests"` → all pass

**Steps:**

- [ ] **Step 1: Create the wrapper project**

`src/AcDream.Rynth.Plugin.RynthAi/AcDream.Rynth.Plugin.RynthAi.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <!-- RynthSuite's RynthAi, compiled unchanged from the submodule as plain net10.0,
       with the RynthSuite libraries and shared files its own csproj pulls in
       (LootSdk, TerrainData, Plugins/Shared). Their embedded resources keep the
       names their code loads them by. Their code, their warnings: warnings-as-
       errors and analyzers are off for this project only. -->
  <PropertyGroup>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <RunAnalyzers>false</RunAnalyzers>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
    <IsPackable>false</IsPackable>
    <RynthSuite>$(MSBuildThisFileDirectory)..\..\external\RynthSuite\</RynthSuite>
    <AiPlugin>$(RynthSuite)Plugins\RynthCore.Plugin.RynthAi\</AiPlugin>
    <SuiteShared>$(RynthSuite)Plugins\Shared\</SuiteShared>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="$(AiPlugin)**\*.cs" Exclude="$(AiPlugin)obj\**;$(AiPlugin)bin\**" />
    <Compile Include="$(SuiteShared)RynthAiScriptApi.cs" Link="Shared\RynthAiScriptApi.cs" />
    <Compile Include="$(SuiteShared)RynthAiStatusApi.cs" Link="Shared\RynthAiStatusApi.cs" />
    <Compile Include="$(SuiteShared)RynthNavPathApi.cs" Link="Shared\RynthNavPathApi.cs" />
    <Compile Include="$(SuiteShared)CombatText.cs" Link="Shared\CombatText.cs" />
    <Compile Include="$(SuiteShared)UseAudit.cs" Link="Shared\UseAudit.cs" />
    <Compile Include="$(SuiteShared)MoveAudit.cs" Link="Shared\MoveAudit.cs" />
    <Compile Include="$(SuiteShared)ActionPacer.cs" Link="Shared\ActionPacer.cs" />
    <Compile Include="$(SuiteShared)Nav3DText.cs" Link="Shared\Nav3DText.cs" />
    <Compile Include="$(SuiteShared)RynthInstallPaths.cs" Link="Shared\RynthInstallPaths.cs" />
    <Compile Include="$(RynthSuite)Shared\RynthCore.LootSdk\**\*.cs"
             Exclude="$(RynthSuite)Shared\RynthCore.LootSdk\obj\**;$(RynthSuite)Shared\RynthCore.LootSdk\bin\**"
             Link="LootSdk\%(RecursiveDir)%(Filename)%(Extension)" />
    <Compile Include="$(RynthSuite)Shared\RynthCore.TerrainData\**\*.cs"
             Exclude="$(RynthSuite)Shared\RynthCore.TerrainData\obj\**;$(RynthSuite)Shared\RynthCore.TerrainData\bin\**"
             Link="TerrainData\%(RecursiveDir)%(Filename)%(Extension)" />
  </ItemGroup>

  <ItemGroup>
    <EmbeddedResource Include="$(AiPlugin)Combat\SpellData.txt" LogicalName="RynthCore.Plugin.RynthAi.Combat.SpellData.txt" />
    <EmbeddedResource Include="$(AiPlugin)CreatureData\creature_resists.tsv" LogicalName="RynthCore.Plugin.RynthAi.CreatureData.creature_resists.tsv" />
    <EmbeddedResource Include="$(AiPlugin)CreatureData\creature_types.txt" LogicalName="RynthCore.Plugin.RynthAi.CreatureData.creature_types.txt" />
    <EmbeddedResource Include="$(AiPlugin)Data\creature_weakness.json" LogicalName="RynthCore.Plugin.RynthAi.Data.creature_weakness.json" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="ImGui.NET" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\AcDream.Rynth.Sdk\AcDream.Rynth.Sdk.csproj" />
  </ItemGroup>
</Project>
```

Add `<Project Path="src/AcDream.Rynth.Plugin.RynthAi/AcDream.Rynth.Plugin.RynthAi.csproj" />` to `AcDream.slnx` before the RynthChat plugin entry (alphabetical), and `<ProjectReference Include="..\AcDream.Rynth.Plugin.RynthAi\AcDream.Rynth.Plugin.RynthAi.csproj" />` to `AcDream.Rynth.Host.csproj` after the RynthChat one.

Run: `~/.dotnet/dotnet build src/AcDream.Rynth.Plugin.RynthAi 2>&1 | grep -E " error |Build succeeded"`
Expected: `Build succeeded` (the probe compiled this set with 0 errors). If a submodule bump since then breaks it, report the errors; do not edit their files.

- [ ] **Step 2: Sandbox their install folders in tests** (`tests/AcDream.Rynth.Host.Tests/RynthConfigSandbox.cs`)

Add two fields beside `_previousXdg`:

```csharp
    private readonly string? _previousCoreDir = Environment.GetEnvironmentVariable("RYNTHCORE_DIR");
    private readonly string? _previousSuiteDir = Environment.GetEnvironmentVariable("RYNTHSUITE_DIR");
```

In the constructor, after `Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Root);`:

```csharp
        // RynthSuite plugins (RynthAi) find their folders through these first, on every OS.
        // Their RynthInstallPaths resolves once per process: the first sandbox's folders stay
        // in use, which is still a temp folder, never the real C:\Games or ApplicationData one.
        Environment.SetEnvironmentVariable("RYNTHCORE_DIR", Path.Combine(Root, "RynthCore"));
        Environment.SetEnvironmentVariable("RYNTHSUITE_DIR", Path.Combine(Root, "RynthSuite"));
```

In `Dispose`, after restoring `XDG_CONFIG_HOME`:

```csharp
        Environment.SetEnvironmentVariable("RYNTHCORE_DIR", _previousCoreDir);
        Environment.SetEnvironmentVariable("RYNTHSUITE_DIR", _previousSuiteDir);
```

Add to the class `<summary>`: "It also points RYNTHCORE_DIR and RYNTHSUITE_DIR (RynthSuite's install folders) inside the sandbox."

- [ ] **Step 3: Write the failing tests** (`tests/AcDream.Rynth.Host.Tests/RynthAiPluginTests.cs`)

```csharp
using System.Reflection;
using AcDream.Rynth.Host;
using AcDream.TestSupport;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthAiPluginTests : IDisposable
{
    private readonly List<string> _log = new();
    private readonly RynthConfigSandbox _sandbox = new("rynth-ai-");
    private readonly FakeGameView _view = new() { PlayerId = 0x50000001u };

    public void Dispose() => _sandbox.Dispose();

    private RynthHost CreateAiHost() =>
        new(_view, _log.Add, [typeof(RynthCore.Plugin.RynthAi.PluginExports)]);

    private static Assembly Theirs => typeof(RynthCore.Plugin.RynthAi.PluginExports).Assembly;

    [Fact]
    public void RynthAiLoadsAndTicks()
    {
        using RynthHost host = CreateAiHost();
        RynthPlugin ai = Assert.Single(host.Plugins);
        Assert.True(ai.IsActive);
        Assert.Contains("RynthAi", ai.Name, StringComparison.Ordinal);

        for (int i = 0; i < 30; i++)
            host.Tick();

        Assert.True(ai.IsActive);
        Assert.DoesNotContain(_log, line => line.Contains("Exception", StringComparison.Ordinal));
    }

    [Fact]
    public void TheOwnerCarriesTheVersionTheExportReports()
    {
        using RynthHost host = CreateAiHost();
        RynthPlugin ai = host.Plugins[0];
        string? version = ai.CallStringExport("RynthPluginVersion");
        Assert.False(string.IsNullOrEmpty(version));
        Assert.Equal(version, ai.Owner.VersionString);
    }

    [Fact]
    public void TheSnapshotAnswersBeforeLogin()
    {
        using RynthHost host = CreateAiHost();
        string? json = host.Plugins[0].CallStringExport("RynthPluginGetSnapshotJson");
        Assert.NotNull(json);
        Assert.Contains("\"macroRunning\":false", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("RynthCore.Plugin.RynthAi.Combat.SpellData.txt")]
    [InlineData("RynthCore.Plugin.RynthAi.CreatureData.creature_resists.tsv")]
    [InlineData("RynthCore.Plugin.RynthAi.CreatureData.creature_types.txt")]
    [InlineData("RynthCore.Plugin.RynthAi.Data.creature_weakness.json")]
    public void TheirResourcesKeepTheirNames(string name) =>
        Assert.Contains(name, Theirs.GetManifestResourceNames());

    [Fact]
    public void OurVariableNamesAreTheirs()
    {
        Type paths = Theirs.GetType("RynthCore.Install.RynthInstallPaths", throwOnError: true)!;
        Assert.Equal(paths.GetField("CoreEnvironmentVariable")!.GetValue(null), RynthInstallFolders.CoreVariable);
        Assert.Equal(paths.GetField("SuiteEnvironmentVariable")!.GetValue(null), RynthInstallFolders.SuiteVariable);
    }

    [Fact]
    public void OffWindowsUnsetFoldersGoUnderApplicationData()
    {
        var env = new Dictionary<string, string?>();
        RynthInstallFolders.Apply(name => env.GetValueOrDefault(name), (name, value) => env[name] = value, isWindows: false, "/home/me/.config");
        Assert.Equal(Path.Combine("/home/me/.config", "RynthCore"), env[RynthInstallFolders.CoreVariable]);
        Assert.Equal(Path.Combine("/home/me/.config", "RynthSuite"), env[RynthInstallFolders.SuiteVariable]);
    }

    [Fact]
    public void FoldersThePlayerSetAreKept()
    {
        var env = new Dictionary<string, string?> { [RynthInstallFolders.SuiteVariable] = "/srv/rynthsuite" };
        RynthInstallFolders.Apply(name => env.GetValueOrDefault(name), (name, value) => env[name] = value, isWindows: false, "/home/me/.config");
        Assert.Equal("/srv/rynthsuite", env[RynthInstallFolders.SuiteVariable]);
        Assert.Equal(Path.Combine("/home/me/.config", "RynthCore"), env[RynthInstallFolders.CoreVariable]);
    }

    [Fact]
    public void OnWindowsTheirOwnLookupIsLeftAlone()
    {
        var env = new Dictionary<string, string?>();
        RynthInstallFolders.Apply(name => env.GetValueOrDefault(name), (name, value) => env[name] = value, isWindows: true, @"C:\Users\me\AppData\Roaming");
        Assert.Empty(env);
    }
}
```

- [ ] **Step 4: Run to see them fail**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthAiPluginTests"`
Expected: build error, `RynthInstallFolders` does not exist.

- [ ] **Step 5: Write `RynthInstallFolders`** (`src/AcDream.Rynth.Host/RynthInstallFolders.cs`)

```csharp
namespace AcDream.Rynth.Host;

/// <summary>
/// RynthSuite plugins find their folders through RynthInstallPaths: the
/// RYNTHCORE_DIR / RYNTHSUITE_DIR environment variables, then the Windows
/// registry, then C:\Games\RynthCore and C:\Games\RynthSuite. On Windows that
/// lookup is theirs and is left alone (a RynthSuite install shares its
/// profiles). Elsewhere the C:\ defaults mean nothing, so unset variables
/// point under ApplicationData, beside the RynthCore folder our other plugins
/// already use. Call before the first plugin loads: they resolve once.
/// </summary>
internal static class RynthInstallFolders
{
    internal const string CoreVariable = "RYNTHCORE_DIR";
    internal const string SuiteVariable = "RYNTHSUITE_DIR";

    internal static void Apply() => Apply(
        Environment.GetEnvironmentVariable,
        Environment.SetEnvironmentVariable,
        OperatingSystem.IsWindows(),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    internal static void Apply(Func<string, string?> get, Action<string, string?> set, bool isWindows, string applicationData)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        if (isWindows || string.IsNullOrEmpty(applicationData))
            return;
        if (string.IsNullOrWhiteSpace(get(CoreVariable)))
            set(CoreVariable, Path.Combine(applicationData, "RynthCore"));
        if (string.IsNullOrWhiteSpace(get(SuiteVariable)))
            set(SuiteVariable, Path.Combine(applicationData, "RynthSuite"));
    }
}
```

- [ ] **Step 6: Record each plugin's version** (`src/AcDream.Rynth.Host/RynthHost.cs`, in `Load`, right after the `if (result != 0) { … return; }` block and before `_plugins.Add(plugin);`)

```csharp
        // RynthCore's dashboard shows a plugin's version from its loaded-plugin record.
        plugin.Owner.VersionString = plugin.CallStringExport("RynthPluginVersion") ?? string.Empty;
```

- [ ] **Step 7: Run the tests**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthAiPluginTests"`
Expected: 10 passed. If RynthAi logs that it needs a newer engine API, report the log line; do not change the plugin.

Then the whole project: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests` → all pass.

- [ ] **Step 8: Commit**

```bash
~/.dotnet/dotnet restore AcDream.slnx --force-evaluate
git add AcDream.slnx src/AcDream.Rynth.Plugin.RynthAi src/AcDream.Rynth.Host tests/AcDream.Rynth.Host.Tests $(git ls-files -mo --exclude-standard '*packages.*.lock.json')
git status --porcelain external
git commit -m "feat(rynth): RynthAi compiles from the submodule and loads in the host"
```

---

### Task 3: RynthAi's Win32 calls become inert at build time

**Goal:** The redirect tool gains an inert mode, and the RynthAi wrapper runs it. No kernel32/user32/shell32/winmm P/Invoke is left in RynthAi. Each becomes a managed method that zeroes its `out` parameters and returns zero/false, so `FellowshipTracker` never reads memory and the editor launching and PlaySound fail quietly, as their code expects.

**Files:**
- Modify: `tools/RynthInteropRedirect/Program.cs`
- Modify: `src/AcDream.Rynth.Plugin.RynthAi/AcDream.Rynth.Plugin.RynthAi.csproj`
- Test: `tests/AcDream.Rynth.Host.Tests/RynthAiInteropTests.cs`

**Acceptance Criteria:**
- [ ] No method in the RynthAi assembly is still a P/Invoke into `kernel32.dll`, `user32.dll`, `shell32.dll` or `winmm.dll`.
- [ ] Their advapi32 registry read is still a P/Invoke (untouched).
- [ ] `FellowshipTracker.IsReadablePointer(0x00400000)` returns false.
- [ ] `RynthAiPlugin+PluginModule.GetModuleHandleExW(0, 0, out module)` returns false with `module == IntPtr.Zero`.
- [ ] The build log shows `RynthInteropRedirect: AcDream.Rynth.Plugin.RynthAi.dll: 0 P/Invoke(s) redirected to nothing, 13 made inert`, and the EngineUi line is unchanged (11 redirected, 0 inert).
- [ ] `Win32ClipboardStandInTests` still pass.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthAiInteropTests|FullyQualifiedName~Win32ClipboardStandInTests"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** (`tests/AcDream.Rynth.Host.Tests/RynthAiInteropTests.cs`)

```csharp
using System.Reflection;
using System.Runtime.InteropServices;

namespace AcDream.Rynth.Host.Tests;

/// <summary>
/// RynthAi's Win32 P/Invokes, made inert at build time (tools/RynthInteropRedirect
/// --inert): in OpenAC they would read our own process's memory (FellowshipTracker
/// walks acclient structures at fixed addresses) or fail off Windows.
/// </summary>
public sealed class RynthAiInteropTests
{
    private static readonly string[] InertModules = ["kernel32.dll", "user32.dll", "shell32.dll", "winmm.dll"];

    private static Assembly Theirs => typeof(RynthCore.Plugin.RynthAi.PluginExports).Assembly;

    private static IEnumerable<(MethodInfo Method, string Module)> PInvokes() =>
        Theirs.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => (method.Attributes & MethodAttributes.PinvokeImpl) != 0)
            .Select(method => (method, method.GetCustomAttribute<DllImportAttribute>()?.Value ?? string.Empty));

    [Fact]
    public void NoWin32CallIsLeft()
    {
        string[] left = PInvokes()
            .Where(p => InertModules.Contains(p.Module, StringComparer.OrdinalIgnoreCase))
            .Select(p => $"{p.Method.DeclaringType?.Name}.{p.Method.Name} ({p.Module})")
            .ToArray();
        Assert.Empty(left);
    }

    [Fact]
    public void TheirRegistryReadIsUntouched() =>
        Assert.Contains(PInvokes(), p => p.Module.Equals("advapi32.dll", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void FellowshipMemoryReadsSeeNothingReadable()
    {
        Type tracker = Theirs.GetType("RynthCore.Plugin.RynthAi.FellowshipTracker", throwOnError: true)!;
        MethodInfo readable = tracker.GetMethod("IsReadablePointer", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.False((bool)readable.Invoke(null, [new IntPtr(0x00400000)])!);
    }

    [Fact]
    public void AnInertCallFailsWithItsOutputsZeroed()
    {
        Type module = Theirs.GetType("RynthCore.Plugin.RynthAi.RynthAiPlugin+PluginModule", throwOnError: true)!;
        MethodInfo getHandle = module.GetMethod("GetModuleHandleExW", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] args = [0u, new IntPtr(1234), new IntPtr(5678)];
        Assert.False((bool)getHandle.Invoke(null, args)!);
        Assert.Equal(IntPtr.Zero, args[2]);
    }
}
```

- [ ] **Step 2: Run to see them fail**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthAiInteropTests"`
Expected: `NoWin32CallIsLeft` fails listing 13 P/Invokes. `FellowshipMemoryReadsSeeNothingReadable` and `AnInertCallFailsWithItsOutputsZeroed` fail: on macOS/Linux with `DllNotFoundException` (inside `TargetInvocationException`); on Windows they make real calls, which is the hazard being fixed. `TheirRegistryReadIsUntouched` passes.

- [ ] **Step 3: Add the inert mode to the tool** (`tools/RynthInteropRedirect/Program.cs`). Replace the whole file with:

```csharp
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace AcDream.Tools.RynthInteropRedirect;

/// <summary>
/// <c>AcDream.Tools.RynthInteropRedirect &lt;assembly&gt; &lt;stand-in type|-&gt; &lt;references file&gt; [--inert &lt;module,...&gt;]</c>:
/// every user32/kernel32 P/Invoke in the assembly that the stand-in type (in
/// the same assembly; <c>-</c> for none) has a public static method for, same
/// name and signature, becomes a managed call to that method. With
/// <c>--inert</c>, every other P/Invoke into one of the listed modules
/// becomes a managed method that zeroes its out parameters and returns zero
/// (false, null): a call that failed. The assembly is rewritten in place, its
/// symbols with it; the references file lists the assembly's references, one
/// path a line, for resolving its types. A rerun finds nothing to do.
/// </summary>
internal static class Program
{
    private static readonly string[] Win32Modules = ["user32.dll", "kernel32.dll"];

    private static int Main(string[] args)
    {
        bool inertGiven = args.Length == 5 && args[3] == "--inert";
        if (args.Length != 3 && !inertGiven)
        {
            Console.Error.WriteLine("usage: AcDream.Tools.RynthInteropRedirect <assembly> <stand-in type|-> <references file> [--inert <module,...>]");
            return 2;
        }

        string? standIn = args[1] == "-" ? null : args[1];
        string[] inert = inertGiven
            ? args[4].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
        try
        {
            (int redirected, int inerted) = Redirect(args[0], standIn, File.ReadAllLines(args[2]), inert);
            Console.WriteLine(
                $"RynthInteropRedirect: {Path.GetFileName(args[0])}: {redirected} P/Invoke(s) redirected to {standIn ?? "nothing"}, {inerted} made inert");
            return 0;
        }
        catch (Exception error) when (error is IOException or BadImageFormatException or InvalidOperationException or AssemblyResolutionException)
        {
            Console.Error.WriteLine($"RynthInteropRedirect: error: {error.Message}");
            return 1;
        }
    }

    private static (int Redirected, int Inerted) Redirect(
        string assemblyPath, string? standInTypeName, IEnumerable<string> references, string[] inertModules)
    {
        bool hasSymbols = File.Exists(Path.ChangeExtension(assemblyPath, ".pdb"));
        using var resolver = new DefaultAssemblyResolver();
        foreach (string directory in references.Where(r => r.Length > 0).Select(r => Path.GetDirectoryName(r) ?? ".").Distinct())
            resolver.AddSearchDirectory(directory);
        using ModuleDefinition module = ModuleDefinition.ReadModule(
            assemblyPath,
            new ReaderParameters { ReadWrite = true, ReadSymbols = hasSymbols, AssemblyResolver = resolver });
        TypeDefinition? standIn = standInTypeName is null
            ? null
            : module.GetType(standInTypeName) ?? throw new InvalidOperationException($"{standInTypeName} is not in {assemblyPath}");

        int redirected = 0, inerted = 0;
        foreach (TypeDefinition type in module.GetTypes())
        {
            if (type == standIn)
                continue;
            foreach (MethodDefinition method in type.Methods)
            {
                if (!method.IsPInvokeImpl || method.PInvokeInfo is not { } pinvoke)
                    continue;
                string moduleName = pinvoke.Module.Name;
                string entryPoint = string.IsNullOrEmpty(pinvoke.EntryPoint) ? method.Name : pinvoke.EntryPoint;
                MethodDefinition? target = standIn is not null && Win32Modules.Contains(moduleName, StringComparer.OrdinalIgnoreCase)
                    ? standIn.Methods.FirstOrDefault(
                        candidate => candidate.IsPublic && candidate.IsStatic && candidate.Name == entryPoint
                            && SameSignature(candidate, method))
                    : null;
                if (target is not null)
                {
                    CallInstead(method, target);
                    redirected++;
                }
                else if (inertModules.Contains(moduleName, StringComparer.OrdinalIgnoreCase))
                {
                    MakeInert(method);
                    inerted++;
                }
            }
        }

        if (redirected + inerted > 0)
            module.Write(new WriterParameters { WriteSymbols = hasSymbols, DeterministicMvid = true });
        return (redirected, inerted);
    }

    private static bool SameSignature(MethodDefinition a, MethodDefinition b) =>
        a.ReturnType.FullName == b.ReturnType.FullName
        && a.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(b.Parameters.Select(p => p.ParameterType.FullName));

    /// <summary>The extern becomes a managed method that passes its arguments to <paramref name="target"/>.</summary>
    private static void CallInstead(MethodDefinition method, MethodDefinition target)
    {
        ILProcessor il = ManagedBody(method);
        foreach (ParameterDefinition parameter in method.Parameters)
            il.Emit(OpCodes.Ldarg, parameter);
        il.Emit(OpCodes.Call, target);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>The extern becomes a managed method that zeroes its out parameters and returns zero.</summary>
    private static void MakeInert(MethodDefinition method)
    {
        ILProcessor il = ManagedBody(method);
        foreach (ParameterDefinition parameter in method.Parameters)
        {
            if (!parameter.IsOut || parameter.ParameterType is not ByReferenceType byReference)
                continue;
            il.Emit(OpCodes.Ldarg, parameter);
            il.Emit(OpCodes.Initobj, byReference.ElementType);
        }

        if (method.ReturnType.MetadataType != MetadataType.Void)
        {
            var result = new VariableDefinition(method.ReturnType);
            method.Body.Variables.Add(result);
            method.Body.InitLocals = true;
            il.Emit(OpCodes.Ldloca, result);
            il.Emit(OpCodes.Initobj, method.ReturnType);
            il.Emit(OpCodes.Ldloc, result);
        }

        il.Emit(OpCodes.Ret);
    }

    private static ILProcessor ManagedBody(MethodDefinition method)
    {
        method.PInvokeInfo = null;
        method.IsPInvokeImpl = false;
        method.ImplAttributes = MethodImplAttributes.IL | MethodImplAttributes.Managed;
        method.Body = new MethodBody(method);
        return method.Body.GetILProcessor();
    }
}
```

- [ ] **Step 4: Run the tool after RynthAi compiles** (`src/AcDream.Rynth.Plugin.RynthAi/AcDream.Rynth.Plugin.RynthAi.csproj`, before `</Project>`, same shape as EngineUi's clipboard target)

```xml
  <!-- Their Win32 P/Invokes (fellowship memory reads, launching their editors,
       PlaySound, the version command's module lookup) become inert after
       compile: each answers "failed", which their code already handles. In
       OpenAC's process those calls have no acclient to read and no editors to
       launch, on any OS. advapi32 (their install-folder registry read) stays. -->
  <ItemGroup>
    <ProjectReference Include="..\..\tools\RynthInteropRedirect\AcDream.Tools.RynthInteropRedirect.csproj"
                      ReferenceOutputAssembly="false" Private="false" />
  </ItemGroup>

  <Target Name="MakeWin32Inert"
          AfterTargets="CoreCompile"
          Inputs="@(IntermediateAssembly)"
          Outputs="$(IntermediateOutputPath)win32-inert.stamp"
          Condition="'$(DesignTimeBuild)' != 'true'">
    <MSBuild Projects="..\..\tools\RynthInteropRedirect\AcDream.Tools.RynthInteropRedirect.csproj"
             Targets="GetTargetPath"
             Properties="Configuration=$(Configuration)">
      <Output TaskParameter="TargetOutputs" PropertyName="_RedirectTool" />
    </MSBuild>
    <PropertyGroup>
      <_RedirectHost>$(DOTNET_HOST_PATH)</_RedirectHost>
      <_RedirectHost Condition="'$(_RedirectHost)' == ''">dotnet</_RedirectHost>
    </PropertyGroup>
    <WriteLinesToFile File="$(IntermediateOutputPath)win32-inert.refs" Lines="@(ReferencePath->'%(FullPath)')" Overwrite="true" />
    <Exec Command="&quot;$(_RedirectHost)&quot; &quot;$(_RedirectTool)&quot; &quot;@(IntermediateAssembly->'%(FullPath)')&quot; - &quot;$(IntermediateOutputPath)win32-inert.refs&quot; --inert kernel32.dll,user32.dll,shell32.dll,winmm.dll" />
    <Touch Files="$(IntermediateOutputPath)win32-inert.stamp" AlwaysCreate="true" />
  </Target>
```

- [ ] **Step 5: Build and run the tests**

```bash
~/.dotnet/dotnet build AcDream.slnx 2>&1 | grep -E "RynthInteropRedirect|error|Build succeeded"
```

Expected: `… AcDream.Rynth.EngineUi.dll: 11 P/Invoke(s) redirected to AcDream.Rynth.EngineUi.Win32ClipboardStandIn, 0 made inert` and `… AcDream.Rynth.Plugin.RynthAi.dll: 0 P/Invoke(s) redirected to nothing, 13 made inert`. If the inert count differs because a submodule bump added or removed a call, update the expected count here and report the new list from `NoWin32CallIsLeft`'s first run.

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthAiInteropTests|FullyQualifiedName~Win32ClipboardStandInTests|FullyQualifiedName~RynthAiPluginTests"`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
~/.dotnet/dotnet restore AcDream.slnx --force-evaluate
git add tools/RynthInteropRedirect/Program.cs src/AcDream.Rynth.Plugin.RynthAi tests/AcDream.Rynth.Host.Tests/RynthAiInteropTests.cs $(git ls-files -m '*packages.*.lock.json')
git commit -m "feat(rynth): RynthAi's Win32 calls are inert in OpenAC"
```

---

### Task 4: RynthCore's RynthAi dashboard compiles unchanged in EngineUi

**Goal:** `AcDream.Rynth.EngineUi` compiles RynthCore's `RynthAiFace` with its drawers, `RynthAiData`, the settings it edits and their small helpers, against grown shared stand-ins plus a new `DashboardStandIns.cs`. The face registers and draws headless with no failure.

**Files:**
- Modify: `src/AcDream.Rynth.EngineUi/AcDream.Rynth.EngineUi.csproj` (12 `Compile` items)
- Modify: `src/AcDream.Rynth.EngineUi/StandIns/ChatEngineStandIns.cs`
- Create: `src/AcDream.Rynth.EngineUi/StandIns/DashboardStandIns.cs`
- Modify: `src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiStandIns.cs`, `src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiPanelHost.cs`, `src/AcDream.Rynth.ScriptWindows/StandIns/EngineStandIns.cs`
- Test: `tests/AcDream.Rynth.Host.Tests/RynthAiDashboardTests.cs`

**Acceptance Criteria:**
- [ ] `~/.dotnet/dotnet build src/AcDream.Rynth.EngineUi` succeeds with the 12 files below compiled from `$(RynthEngine)`.
- [ ] No file of those names exists under `src/` (no copies).
- [ ] `RynthAiFace.Register()` gives `ImGuiPanelHost.HasFace("RynthAi")`; opened, it draws 3 headless frames with more than 100 vertices each and nothing logged.
- [ ] The chat tests (`RynthChatWindowTests`, `ChatPanelHostTests`, `Win32ClipboardStandInTests`) still pass.
- [ ] `git status --porcelain external` empty.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthAiDashboardTests|FullyQualifiedName~RynthChatWindowTests|FullyQualifiedName~ChatPanelHostTests"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** (`tests/AcDream.Rynth.Host.Tests/RynthAiDashboardTests.cs`). Task 5 adds more tests to this class.

```csharp
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.ImGuiBackend.Panels;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthAiDashboardTests : IDisposable
{
    private static readonly string[] TheirFiles =
    [
        @"ImGui\Panels\RynthAiFace.cs", @"ImGui\Panels\RynthAiFace.Drawers.cs", @"ImGui\Panels\SettingsRows.cs",
        @"ImGui\Panels\QuickSettingsDrawer.cs", @"UI\Data\RynthAiData.cs", @"UI\Data\SettingsData.cs",
        @"UI\Data\NavData.cs", @"UI\Data\RynthSortData.cs", @"UI\RynthAiDashboardState.cs",
        @"UI\PanelTextScale.cs", @"UI\QuickSettings.cs", @"UI\BarLayout.cs",
    ];

    private readonly List<string> _log = new();

    public RynthAiDashboardTests()
    {
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = _log.Add;
    }

    public void Dispose()
    {
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = null;
        LoginLifecycleHooks.HasObservedLoginComplete = false;
    }

    [Fact]
    public void TheProjectCompilesTheirFilesAndHasNoCopies()
    {
        string root = RepoRoot();
        string project = File.ReadAllText(Path.Combine(root, "src/AcDream.Rynth.EngineUi/AcDream.Rynth.EngineUi.csproj"));
        foreach (string file in TheirFiles)
            Assert.Contains("$(RynthEngine)" + file, project, StringComparison.Ordinal);

        var names = TheirFiles.Select(f => f[(f.LastIndexOf('\\') + 1)..]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var copies = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(p => names.Contains(Path.GetFileName(p)))
            .ToList();
        Assert.Empty(copies);
    }

    [Fact]
    public void TheDashboardRegistersAndDrawsHeadless()
    {
        using var imgui = new HeadlessImGui();
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        RynthAiFace.Register();
        Assert.True(ImGuiPanelHost.HasFace(RynthAiFace.Title));
        ImGuiPanelHost.Open(RynthAiFace.Title);

        for (int i = 0; i < 3; i++)
        {
            int vertices = imgui.Frame(ImGuiPanelHost.DrawAll);
            Assert.True(vertices > 100, $"frame {i}: only {vertices} vertices");
        }

        Assert.Empty(_log);
    }

    internal static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "AcDream.slnx")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repo root not found");
    }
}
```

- [ ] **Step 2: Run to see them fail**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthAiDashboardTests"`
Expected: build error, `RynthAiFace` does not exist.

- [ ] **Step 3: Compile their dashboard files** (`src/AcDream.Rynth.EngineUi/AcDream.Rynth.EngineUi.csproj`, inside the first `<ItemGroup>`, before the `TellMemory.cs` item)

```xml
    <Compile Include="$(RynthEngine)ImGui\Panels\RynthAiFace.cs" Link="RynthCore\RynthAiFace.cs" />
    <Compile Include="$(RynthEngine)ImGui\Panels\RynthAiFace.Drawers.cs" Link="RynthCore\RynthAiFace.Drawers.cs" />
    <Compile Include="$(RynthEngine)ImGui\Panels\SettingsRows.cs" Link="RynthCore\SettingsRows.cs" />
    <Compile Include="$(RynthEngine)ImGui\Panels\QuickSettingsDrawer.cs" Link="RynthCore\QuickSettingsDrawer.cs" />
    <Compile Include="$(RynthEngine)UI\Data\RynthAiData.cs" Link="RynthCore\Data\RynthAiData.cs" />
    <Compile Include="$(RynthEngine)UI\Data\SettingsData.cs" Link="RynthCore\Data\SettingsData.cs" />
    <Compile Include="$(RynthEngine)UI\Data\NavData.cs" Link="RynthCore\Data\NavData.cs" />
    <Compile Include="$(RynthEngine)UI\Data\RynthSortData.cs" Link="RynthCore\Data\RynthSortData.cs" />
    <Compile Include="$(RynthEngine)UI\RynthAiDashboardState.cs" Link="RynthCore\RynthAiDashboardState.cs" />
    <Compile Include="$(RynthEngine)UI\PanelTextScale.cs" Link="RynthCore\PanelTextScale.cs" />
    <Compile Include="$(RynthEngine)UI\QuickSettings.cs" Link="RynthCore\QuickSettings.cs" />
    <Compile Include="$(RynthEngine)UI\BarLayout.cs" Link="RynthCore\BarLayout.cs" />
```

In the header comment, after "the ImGui chat window (ChatFace, the filter editor, ChatData and friends)", add: "and the RynthAi dashboard (RynthAiFace, its drawers, RynthAiData and the settings it edits)". After "Discord relay are NOT compiled", add: ", nor PluginManager, EntryPoint, D3D9, MonstersPanel, RangesSlideOut or the faces the dashboard only links to (see StandIns/DashboardStandIns.cs)".

- [ ] **Step 4: Grow the shared stand-ins in ScriptWindows** (exact diffs, proven in the probe)

Apply this diff (`git apply` from the repo root, or edit by hand):

```diff
--- a/src/AcDream.Rynth.ScriptWindows/StandIns/EngineStandIns.cs
+++ b/src/AcDream.Rynth.ScriptWindows/StandIns/EngineStandIns.cs
@@ -34,12 +34,23 @@
     {
         public string DisplayName { get; set; } = "";
         public bool Failed { get; set; }
+
+        /// <summary>The plugin's version, as RynthPluginVersion reports it.</summary>
+        public string VersionString { get; set; } = "";
     }
 
     internal static class EngineSettings
     {
         /// <summary>True while an ImGui overlay is drawing script windows.</summary>
         public static bool EnableImGuiBackend { get; set; }
+
+        // RynthCore engine settings that their Settings panel shows. They steer
+        // acclient hooks OpenAC doesn't have, so here they are only remembered.
+        public static bool InsertReleasesUi { get; set; }
+
+        public static bool CloseOnServerDisconnect { get; set; }
+
+        public static Compatibility.AutoIdMode AutoIdMode { get; set; } = Compatibility.AutoIdMode.Limited;
     }
 
     /// <summary>Layout copied from RynthCore.Engine/Plugins/PluginContract.cs (480 bytes).</summary>
@@ -71,6 +82,9 @@
 
 namespace RynthCore.Engine.Compatibility
 {
+    /// <summary>RynthCore's background-identify modes (Compatibility/AutoIdPolicy.cs).</summary>
+    internal enum AutoIdMode { All = 0, Limited = 1, Off = 2 }
+
     internal static class LoginLifecycleHooks
     {
         /// <summary>Set by the host's event bridge on login and logout.</summary>
--- a/src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiPanelHost.cs
+++ b/src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiPanelHost.cs
@@ -96,6 +96,42 @@
     /// </summary>
     public static float BodyGripReserve => 0f;
 
+    /// <summary>Client pixels per logical unit; the overlay's UI scale.</summary>
+    internal static float PlacementScale()
+    {
+        float s = EngineFrameController.FontScale;
+        return s > 0f ? s : 1f;
+    }
+
+    private static Vector2 _pendingDragDelta;
+
+    /// <summary>
+    /// Makes the last item a drag handle for its panel window (chromeless
+    /// panels drag from their own title row). DrawAll moves the window after
+    /// the face has drawn.
+    /// </summary>
+    public static void DragWindowWithLastItem()
+    {
+        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 0f))
+            _pendingDragDelta += ImGui.GetIO().MouseDelta;
+    }
+
+    /// <summary>RynthCore's pop-out button; OpenAC has no pop-out windows (PanelRouter.CanPopOut is false).</summary>
+    public static string PopOutGlyph => RynthCore.Engine.ImGuiBackend.PhosphorIcons.ArrowSquareOut;
+
+    public static string PopOutTooltip => "Pop out (not available in OpenAC)";
+
+    public static void TogglePopOut(string title)
+    {
+    }
+
+    /// <summary>RynthCore's per-panel text size rows; the overlay has one font size, so this says so.</summary>
+    public static void TextSizeRows(string title, float s, string heading = "Text size")
+    {
+        ImGui.TextUnformatted(heading);
+        ImGui.TextDisabled("Uses OpenAC's UI scale.");
+    }
+
     /// <summary>RynthCore's built-in panels (Chat, ChatFilters) register by title.</summary>
     public static void Register(string title, PanelSpec spec, Func<IImGuiPanel> factory) =>
         RegisterDynamic(title, title, spec, factory);
@@ -314,6 +350,11 @@
                         ImGui.TextDisabled("This window failed and was stopped.");
                     else
                         entry.Instance?.Draw();
+                    if (_pendingDragDelta != Vector2.Zero)
+                    {
+                        ImGui.SetWindowPos(ImGui.GetWindowPos() + _pendingDragDelta);
+                        _pendingDragDelta = Vector2.Zero;
+                    }
                     TrackPlacement(key, entry, placed, s);
                 }
             }
--- a/src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiStandIns.cs
+++ b/src/AcDream.Rynth.ScriptWindows/StandIns/ImGuiStandIns.cs
@@ -59,6 +59,17 @@
 
         /// <summary>RynthCore's chat font nearest a point size; here, the overlay's current font.</summary>
         public static ImFontPtr Chat(float points) => ImGui.GetFont();
+
+        /// <summary>RynthCore's baked text-size step; the overlay has one font size, so 0 (normal).</summary>
+        public static int CurrentStep;
+
+        /// <summary>The baked step nearest a panel's text size; only normal is baked here.</summary>
+        public static int BakedStepNear(int step) => 0;
+
+        /// <summary>RynthCore re-bakes its fonts for a new text size; the overlay's font doesn't change.</summary>
+        public static void RequestRebuild(int delayMs = 0)
+        {
+        }
     }
 
     internal enum PanelChrome
@@ -67,7 +78,7 @@
         None,
     }
 
-    /// <summary>The members of RynthCore's PanelSpec that the registry sets.</summary>
+    /// <summary>The members of RynthCore's PanelSpec that their faces set (of Background, BorderColor and Rounding only a zero Background is honoured).</summary>
     internal sealed record PanelSpec(
         Vector2 DefaultSize,
         Vector2 MinSize,
@@ -77,7 +88,9 @@
         ImGuiWindowFlags BodyFlags = ImGuiWindowFlags.None,
         Vector2? DefaultPos = null,
         bool EdgeToEdge = false,
-        bool GripInBody = false);
+        bool GripInBody = false,
+        uint? BorderColor = null,
+        float Rounding = 0f);
 
     internal interface IImGuiPanel
     {
```

- [ ] **Step 5: Grow the chat stand-ins** (`src/AcDream.Rynth.EngineUi/StandIns/ChatEngineStandIns.cs`). The old `RynthAiDashboardState` stand-in is deleted: their real file is now compiled and has the same two members.

```diff
--- a/src/AcDream.Rynth.EngineUi/StandIns/ChatEngineStandIns.cs
+++ b/src/AcDream.Rynth.EngineUi/StandIns/ChatEngineStandIns.cs
@@ -72,16 +72,17 @@
     internal static class PanelRouter
     {
         public static void Toggle(string title) => RynthCore.Engine.ImGuiBackend.ImGuiPanelHost.Toggle(title);
-    }
 
-    /// <summary>RynthAi's dashboard drawers; no RynthAi here, so nothing is ever moved.</summary>
-    internal static class RynthAiDashboardState
-    {
-        public static bool DrawerMoved(string key) => false;
+        /// <summary>OpenAC has no pop-out windows.</summary>
+        public static bool CanPopOut => false;
 
-        public static void SetDrawerMoved(string key, bool moved)
+        /// <summary>RynthCore's Monsters panel is not in OpenAC yet; its buttons do nothing.</summary>
+        public static void ToggleMonsters()
         {
         }
+
+        /// <summary>RynthCore maps old panel names to current ones; ours are already current.</summary>
+        public static string Canonical(string title) => title;
     }
 
     /// <summary>RynthCore's Avalonia overlay flag; OpenAC has no Avalonia overlay.</summary>
@@ -102,10 +103,15 @@
             Resolver?.Invoke(pluginNamePart, exportName) ?? IntPtr.Zero;
     }
 
-    /// <summary>RynthCore's source hub; only the chat source exists here.</summary>
+    /// <summary>RynthCore's source hub; only the sources of the faces OpenAC compiles.</summary>
     internal static class UiSources
     {
         public static readonly ChatSource Chat = new();
+        public static readonly RynthAiSource RynthAi = new();
+        public static readonly PatrolSource Patrol = new();
+        public static readonly NavSource Nav = new();
+        public static readonly SettingsSource Settings = new();
+        public static readonly RynthSortWatch RynthSortWatch = new();
     }
 
     /// <summary>RynthNav's "Arrow to" / "Go to"; no RynthNav in OpenAC.</summary>
@@ -176,6 +182,11 @@
         internal static bool TellRequested;
 
         public static void RequestTellSelected() => TellRequested = true;
+
+        /// <summary>RynthCore takes keyboard focus back from acclient; OpenAC's overlay already owns it.</summary>
+        public static void ReclaimUi()
+        {
+        }
     }
 
     /// <summary>RynthCore's pop-out windows; OpenAC has none.</summary>
```

- [ ] **Step 6: Create `src/AcDream.Rynth.EngineUi/StandIns/DashboardStandIns.cs`**

```csharp
// Stand-ins for the RynthCore engine types RynthAi's dashboard (RynthAiFace and
// the settings it edits) links to but OpenAC doesn't compile yet. Each keeps
// only the members their files touch. Panels named here are not in OpenAC:
// their buttons and menu items do nothing until a later RynthAi sub-project
// compiles the real face (loot editor: #5; maps: #4 and #6).
using System;
using System.Collections.Generic;
using System.Numerics;

namespace RynthCore.Engine
{
    /// <summary>RynthCore's engine entry; only the start time the dashboard's uptime shows.</summary>
    internal static class EntryPoint
    {
        internal static DateTime InitStartedUtc { get; set; } = DateTime.MinValue;
    }
}

namespace RynthCore.Engine.D3D9
{
    /// <summary>RynthCore measures acclient's frame rate in its EndScene hook; OpenAC has none, so 0.</summary>
    internal static class EndSceneHook
    {
        internal static volatile float MeasuredFps;
    }

    internal enum VideoPatchMode
    {
        Off = 0,
        Unfocused = 1,
        World = 2,
        All = 3,
    }

    internal static class VideoPatchPolicy
    {
        public static readonly string[] ComboItems = { "Off (not in OpenAC)" };
    }

    /// <summary>RynthCore's acclient video patch; OpenAC renders itself, so it stays off.</summary>
    internal static class VideoPatch
    {
        public static VideoPatchMode Mode => VideoPatchMode.Off;

        public static int FrameCapFps => 0;

        public static string SetMode(VideoPatchMode mode, bool save, string source) => "not available in OpenAC";

        public static string SetFrameCap(int fps) => "not available in OpenAC";
    }
}

namespace RynthCore.Engine.Plugins
{
    /// <summary>
    /// The loaded plugins as RynthCore's dashboard reads them: the host sets
    /// <see cref="Plugins"/> and raises <see cref="RaisePluginsUnloaded"/> when
    /// it stops them. The plugins themselves live in AcDream.Rynth.Host.
    /// </summary>
    internal static class PluginManager
    {
        internal const int EmbedDrawn = 1, EmbedNotReady = 0, EmbedNoPlugin = -1, EmbedFailed = -2;

        public static IReadOnlyList<LoadedPlugin> Plugins { get; internal set; } = Array.Empty<LoadedPlugin>();

        public static event Action? PluginsUnloaded;

        internal static void RaisePluginsUnloaded() => PluginsUnloaded?.Invoke();

        /// <summary>RynthCore draws a plugin's embedded widgets; RynthAi exports none, so "no plugin".</summary>
        internal static int RenderEmbed(string pluginName, ReadOnlySpan<byte> surfaceUtf8, Vector2 size, out Vector2 want)
        {
            want = Vector2.Zero;
            return EmbedNoPlugin;
        }
    }
}

namespace RynthCore.Engine.Compatibility
{
    /// <summary>RynthCore queues work for acclient's thread; only the chat write their dashboard code uses.</summary>
    internal static class AcMainThreadQueue
    {
        internal static Action<string, int>? WriteToChat { get; set; }

        public static bool EnqueueWriteToChat(string text, int color)
        {
            WriteToChat?.Invoke(text, color);
            return WriteToChat is not null;
        }
    }

    /// <summary>RynthCore hides acclient's vitals bar; OpenAC's own bar is unaffected, the box is only remembered.</summary>
    internal static class RetailVitalsHooks
    {
        public static bool HideRetailVitals { get; set; }
    }
}

namespace RynthCore.Engine.UI
{
    /// <summary>RynthCore's server menu; its commands go to the chat parser like a typed line.</summary>
    internal static class ServerMenus
    {
        internal static Action<string>? Runner { get; set; }

        public static void Run(string command) => Runner?.Invoke(command);

        /// <summary>RynthCore's per-server menu items; none in OpenAC.</summary>
        public static bool DrawImGuiMenu(bool separatorFirst = false) => false;
    }
}

namespace RynthCore.Engine.UI.Data
{
    /// <summary>
    /// RynthCore's UI pump: work posted here runs on the next dashboard tick
    /// (RynthAiDashboard.Tick), on the game thread, in order.
    /// </summary>
    internal static class UiDataHub
    {
        private static readonly Queue<(string Label, Action Action)> Posted = new();

        public static bool Post(string label, Action action)
        {
            lock (Posted)
                Posted.Enqueue((label, action));
            return true;
        }

        /// <summary>Runs everything posted so far; a failure is logged and the rest still run.</summary>
        internal static int Drain(Action<string> log)
        {
            int ran = 0;
            while (true)
            {
                (string Label, Action Action) next;
                lock (Posted)
                {
                    if (Posted.Count == 0)
                        return ran;
                    next = Posted.Dequeue();
                }
                try { next.Action(); }
                catch (Exception error) { log($"[RynthAi] {next.Label}: {error.GetType().Name}: {error.Message}"); }
                ran++;
            }
        }

        internal static void Clear()
        {
            lock (Posted)
                Posted.Clear();
        }
    }
}

namespace RynthCore.Engine.ImGuiBackend
{
    /// <summary>RynthCore's own toolbar; OpenAC has the Rynth bar instead, so the rows are only remembered.</summary>
    internal static class ImGuiBar
    {
        public static int Rows { get; private set; } = 1;

        public static int MaxPerRow { get; private set; } = 12;

        public static void SetRowLayout(int rows, int maxPerRow)
        {
            Rows = rows;
            MaxPerRow = maxPerRow;
        }
    }
}

namespace RynthCore.Engine.ImGuiBackend.Panels
{
    /// <summary>RynthCore's hotkeys editor; OpenAC binds its own keys, so the Hotkeys settings tab is empty.</summary>
    internal sealed class HotkeysSettingsUi
    {
        public const string TabName = "Hotkeys";

        public void Leave()
        {
        }

        public bool Draw(float w) => false;
    }

    /// <summary>RynthCore's server picker (launcher profiles); OpenAC picks the server at login.</summary>
    internal static class ServerPickSettingsUi
    {
        public static void Draw(float width, string? tooltip) =>
            ImGuiNET.ImGui.TextDisabled("Server: chosen at OpenAC's login.");
    }

    internal static class LootEditorFace
    {
        public const string Title = "Loot Editor";

        public static void Register()
        {
        }

        public static void ToggleFor(string? path)
        {
        }
    }

    internal static class DungeonMapFace
    {
        public const string Title = "Dungeon Map";

        public static void Register()
        {
        }
    }

    internal static class WorldMapFace
    {
        public const string Title = "World Map";
    }

    internal static class MiniRemoteFace
    {
        public const string Title = "Mini Remote";

        public static bool ShowsSection(string sectionId) => false;
    }

    /// <summary>RynthCore's Settings panel; the dashboard's own settings drawer works, this window is not in OpenAC.</summary>
    internal static class SettingsFace
    {
        public const string Title = "Settings";

        public static void RequestTab(string name)
        {
        }
    }

    internal static class CharactersFace
    {
        public const string Title = "Characters";
    }

    internal static class InventoryFace
    {
        public const string Title = "Inventory";
    }

    internal static class ItemCountFace
    {
        public const string Title = "Item Count";
    }

    internal static class SkillsFace
    {
        public const string Title = "Skills";
    }

    /// <summary>
    /// The dashboard's Ranges drawer. RynthCore's real one reads the monster-damage
    /// rules (MonstersPanel, DamageFace), which come with combat; until then the
    /// drawer tab is there and the panel says so.
    /// </summary>
    internal sealed class RangesSlideOut : RynthAiDrawer
    {
        public override string Key => "ranges";

        public override string Icon => PhosphorIcons.Ruler;

        public override string Name => "ranges";

        public override bool SavedOpen => RynthCore.Engine.UI.RynthAiDashboardState.RangesOpen;

        public override void SaveOpen(bool open) => RynthCore.Engine.UI.RynthAiDashboardState.SetRangesOpen(open);

        public override string TabTooltip(bool right) => "Ranges: not available in OpenAC yet.";

        public override float Width(float k) => 250 * k;

        public override float Height(float k) => 60 * k;

        public override void DrawPanel(Vector2 origin, float width, float height, float k)
        {
            ImGuiNET.ImGui.SetCursorScreenPos(origin + new Vector2(8 * k, 8 * k));
            ImGuiNET.ImGui.TextDisabled("Ranges: not available in OpenAC yet.");
        }
    }
}

namespace RynthCore.Engine.ImGuiBackend.Hud
{
    /// <summary>
    /// RynthCore reads spell names from client_portal.dat; not wired in OpenAC yet,
    /// so recall spells show by id.
    /// </summary>
    internal static class PortalSpellTable
    {
        public static void EnsureLoadQueued()
        {
        }

        public static bool TryGetName(uint spellId, out string name)
        {
            name = string.Empty;
            return false;
        }
    }
}
```

- [ ] **Step 7: Build and run the tests**

Run: `~/.dotnet/dotnet build src/AcDream.Rynth.EngineUi 2>&1 | grep -E " error |RynthInteropRedirect|Build succeeded"`
Expected: `Build succeeded`, clipboard redirect still 11. If a submodule bump since 2026-10-18 adds a missing member, add it to the stand-in it belongs to, following the file's pattern (a no-op or an "only remembered" property, with a doc comment saying why), and list it in the report.

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthAiDashboardTests|FullyQualifiedName~RynthChatWindowTests|FullyQualifiedName~ChatPanelHostTests|FullyQualifiedName~Win32ClipboardStandInTests"`
Expected: all pass. Then the whole project: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests` → all pass.

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.Rynth.EngineUi src/AcDream.Rynth.ScriptWindows tests/AcDream.Rynth.Host.Tests/RynthAiDashboardTests.cs
git status --porcelain external
git commit -m "feat(rynth): RynthCore's RynthAi dashboard compiles unchanged against stand-ins"
```

---

### Task 5: The client runs RynthAi and its dashboard, with a bar button

**Goal:** `RynthClientExtension` loads RynthAi beside the other plugins. With an overlay it starts the dashboard, pumps it every tick (posted work, then the subscribed sources), and offers a "RynthAi" bar button. `/ra`, `/mt`, `/ub` and `/vt` reach RynthAi. Dispose stops the dashboard and unpoints every stand-in.

**Files:**
- Create: `src/AcDream.Rynth.EngineUi/RynthAiDashboard.cs`
- Create: `src/AcDream.Rynth.Host/RynthAiHookup.cs`
- Modify: `src/AcDream.Rynth.Host/RynthClientExtension.cs`
- Test: `tests/AcDream.Rynth.Host.Tests/RynthAiDashboardTests.cs` (add tests)
- Test: `tests/AcDream.App.Tests/Extensions/RynthOverlayExtensionTests.cs` (add tests)

**Acceptance Criteria:**
- [ ] With RynthAi loaded and the hookup up, subscribing `UiSources.RynthAi` and ticking the dashboard gives a snapshot whose `Raw.MacroRunning` is false and whose `Version` equals the plugin's `Owner.VersionString`.
- [ ] `RynthAiCommands.ToggleMacro()` and the next dashboard tick → the snapshot's `Raw.MacroRunning` is true; toggling again → false.
- [ ] Posted work that throws is logged (`[RynthAi] <label>: InvalidOperationException: …`) and later posted work still runs.
- [ ] `RynthAiDashboard.Stop()` clears posted work and the RynthAi snapshot.
- [ ] Hookup Dispose unpoints `PluginManager.Plugins`, `ServerMenus.Runner`, `AcMainThreadQueue.WriteToChat`, and only those still pointing at it.
- [ ] App: with an overlay, after Attach `ImGuiPanelHost.HasFace("RynthAi")` is true, `ra`/`mt`/`ub`/`vt` are registered, and after Dispose they are gone and `PluginManager.Plugins` is empty.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests && ~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~RynthOverlayExtensionTests"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing host tests.** Add to `RynthAiDashboardTests` (fields and helpers at the top of the class; the constructor and `Dispose` gain the lines shown):

```csharp
    private readonly RynthConfigSandbox _sandbox = new("rynth-ai-dash-");
    private readonly FakeGameView _view = new() { PlayerId = 0x50000001u };

    // constructor: add after ImGuiPanelHost.Log = _log.Add;
    //     RynthCore.Engine.UI.Data.UiDataHub.Clear();

    // Dispose: add at the top
    //     AcDream.Rynth.EngineUi.RynthAiDashboard.Stop();
    //     RynthCore.Engine.UI.Data.PluginExportBinder.Resolver = null;
    // and at the end
    //     _sandbox.Dispose();

    private (RynthHost Host, RynthChatHookup Exports, RynthAiHookup Ai) StartWithRynthAi()
    {
        var host = new RynthHost(_view, _log.Add, [typeof(RynthCore.Plugin.RynthAi.PluginExports)]);
        var exports = new RynthChatHookup(host, _view, _log.Add);
        var ai = new RynthAiHookup(host, _view, _log.Add);
        AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
        return (host, exports, ai);
    }

    private static RynthCore.Engine.UI.Data.RynthAiView? Snapshot() => RynthCore.Engine.UI.Data.UiSources.RynthAi.Current?.Value;

    [Fact]
    public void TheSnapshotReachesTheRynthAiSource()
    {
        (RynthHost host, RynthChatHookup exports, RynthAiHookup ai) = StartWithRynthAi();
        using (host) using (exports) using (ai)
        {
            RynthCore.Engine.UI.Data.UiSources.RynthAi.Subscribe();
            try
            {
                RynthCore.Engine.UI.Data.UiSources.RynthAi.RequestRefresh();
                AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();
                Assert.NotNull(Snapshot());
                Assert.False(Snapshot()!.Raw.MacroRunning);
                Assert.Equal(host.Plugins[0].Owner.VersionString, Snapshot()!.Version);
            }
            finally { RynthCore.Engine.UI.Data.UiSources.RynthAi.Unsubscribe(); }
        }
    }

    [Fact]
    public void TheMacroToggleRoundTripsThroughThePump()
    {
        (RynthHost host, RynthChatHookup exports, RynthAiHookup ai) = StartWithRynthAi();
        using (host) using (exports) using (ai)
        {
            RynthCore.Engine.UI.Data.UiSources.RynthAi.Subscribe();
            try
            {
                RynthCore.Engine.UI.Data.RynthAiCommands.ToggleMacro();
                AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();
                Assert.True(Snapshot()!.Raw.MacroRunning);

                RynthCore.Engine.UI.Data.RynthAiCommands.ToggleMacro();
                AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();
                Assert.False(Snapshot()!.Raw.MacroRunning);
            }
            finally { RynthCore.Engine.UI.Data.UiSources.RynthAi.Unsubscribe(); }
        }
    }

    [Fact]
    public void PostedWorkThatThrowsIsLoggedAndTheRestRuns()
    {
        using var host = new RynthHost(_view, _log.Add, []);
        using var ai = new RynthAiHookup(host, _view, _log.Add);
        AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
        bool ran = false;
        RynthCore.Engine.UI.Data.UiDataHub.Post("boom", () => throw new InvalidOperationException("bad"));
        RynthCore.Engine.UI.Data.UiDataHub.Post("after", () => ran = true);

        AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();

        Assert.True(ran);
        Assert.Contains(_log, l => l.Contains("[RynthAi] boom: InvalidOperationException: bad", StringComparison.Ordinal));
    }

    [Fact]
    public void StopClearsPostedWorkAndTheSnapshot()
    {
        (RynthHost host, RynthChatHookup exports, RynthAiHookup ai) = StartWithRynthAi();
        using (host) using (exports) using (ai)
        {
            RynthCore.Engine.UI.Data.UiSources.RynthAi.Subscribe();
            try
            {
                RynthCore.Engine.UI.Data.UiSources.RynthAi.RequestRefresh();
                AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();
                Assert.NotNull(Snapshot());
                bool ran = false;
                RynthCore.Engine.UI.Data.UiDataHub.Post("late", () => ran = true);

                AcDream.Rynth.EngineUi.RynthAiDashboard.Stop();
                Assert.Null(Snapshot());

                AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
                AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();
                Assert.False(ran);
            }
            finally { RynthCore.Engine.UI.Data.UiSources.RynthAi.Unsubscribe(); }
        }
    }

    [Fact]
    public void TheHookupPointsTheStandInsUntilDispose()
    {
        using var host = new RynthHost(_view, _log.Add, [typeof(RynthCore.Plugin.RynthAi.PluginExports)]);
        var ai = new RynthAiHookup(host, _view, _log.Add);

        Assert.Same(host.Plugins[0].Owner, Assert.Single(RynthCore.Engine.Plugins.PluginManager.Plugins));
        RynthCore.Engine.UI.ServerMenus.Run("/ro fellowship");
        Assert.Contains("/ro fellowship", _view.SubmittedCommands);
        Assert.True(RynthCore.Engine.Compatibility.AcMainThreadQueue.EnqueueWriteToChat("hello", 1));
        Assert.Contains(("hello", 1), _view.ChatLines);

        ai.Dispose();
        Assert.Empty(RynthCore.Engine.Plugins.PluginManager.Plugins);
        Assert.Null(RynthCore.Engine.UI.ServerMenus.Runner);
        Assert.False(RynthCore.Engine.Compatibility.AcMainThreadQueue.EnqueueWriteToChat("gone", 1));
    }

    [Fact]
    public void AThrowingViewIsLoggedNotThrown()
    {
        using var host = new RynthHost(_view, _log.Add, []);
        using var ai = new RynthAiHookup(host, _view, _log.Add);
        _view.Throw = true;
        RynthCore.Engine.Compatibility.AcMainThreadQueue.EnqueueWriteToChat("x", 1);
        Assert.Contains(_log, l => l.Contains("view failed", StringComparison.Ordinal));
    }
```

Add `using AcDream.Rynth.Host;` and `using AcDream.TestSupport;` at the top of the file.

- [ ] **Step 2: Write the failing App tests** (`tests/AcDream.App.Tests/Extensions/RynthOverlayExtensionTests.cs`, after `WithAnOverlayTheLayoutIsHookedUpUntilDispose`)

```csharp
    [Fact]
    public void WithAnOverlayTheBarOffersTheRynthAiDashboard()
    {
        var extension = new RynthClientExtension();
        extension.Attach(Context(GameRuntimeTestFactory.Create(), new FakeOverlay()));
        try
        {
            Assert.True(ImGuiPanelHost.HasFace("RynthAi"));
            Assert.Contains(RynthCore.Engine.Plugins.PluginManager.Plugins, p => p.DisplayName.Contains("RynthAi", StringComparison.Ordinal));
        }
        finally
        {
            extension.Dispose();
        }

        Assert.Empty(RynthCore.Engine.Plugins.PluginManager.Plugins);
    }

    [Fact]
    public void RynthAisChatCommandsAreRegisteredUntilDispose()
    {
        var extension = new RynthClientExtension();
        extension.Attach(Context(GameRuntimeTestFactory.Create(), overlay: null));
        foreach (string verb in new[] { "ra", "mt", "ub", "vt" })
            Assert.True(_commands.ContainsKey(verb), verb);

        extension.Dispose();
        Assert.Empty(_commands);
    }
```

- [ ] **Step 3: Run to see them fail**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthAiDashboardTests"`
Expected: build error, `RynthAiHookup` and `RynthAiDashboard` do not exist.

- [ ] **Step 4: Write `RynthAiDashboard`** (`src/AcDream.Rynth.EngineUi/RynthAiDashboard.cs`)

```csharp
using System;
using System.Diagnostics;
using RynthCore.Engine;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.ImGuiBackend.Panels;
using RynthCore.Engine.Plugins;
using RynthCore.Engine.UI.Data;

namespace AcDream.Rynth.EngineUi;

/// <summary>
/// Runs RynthCore's RynthAi dashboard: Start does what RynthCore's EntryPoint
/// does when RynthAi is installed (register the face), Tick is UiDataHub's
/// step (run posted work, then poll each subscribed source when due), Stop is
/// the hub's reset after plugins unload. Game thread.
/// </summary>
internal static class RynthAiDashboard
{
    private static bool _started;

    /// <summary>Their dashboard's title (the bar button and the layout key).</summary>
    internal const string Title = RynthAiFace.Title;

    /// <summary>Where posted work that throws is logged; the host sets it.</summary>
    internal static Action<string> Log { get; set; } = _ => { };

    internal static void Start()
    {
        if (!_started)
        {
            EntryPoint.InitStartedUtc = DateTime.UtcNow;
            _started = true;
        }

        // The panel host can be reset (tests, an extension restart): register again then.
        if (!ImGuiPanelHost.HasFace(RynthAiFace.Title))
            RynthAiFace.Register();
    }

    /// <summary>One hub step. Returns how many posted actions ran.</summary>
    internal static int Tick()
    {
        if (!_started)
            return 0;
        int ran = UiDataHub.Drain(Log);
        Poll(UiSources.RynthAi);
        Poll(UiSources.Patrol);
        Poll(UiSources.Nav);
        Poll(UiSources.Settings);
        Poll(UiSources.RynthSortWatch);
        return ran;
    }

    private static void Poll(UiSource source)
    {
        source.PromoteDeferredRefresh();
        long now = Stopwatch.GetTimestamp();
        if (!source.HasSubscribers || !source.IsDue(now))
            return;
        source.Poll();
        source.MarkPolled(now, Stopwatch.GetTimestamp() - now);
    }

    internal static void Stop()
    {
        UiDataHub.Clear();
        PluginManager.RaisePluginsUnloaded();
        UiSources.RynthAi.Reset();
        UiSources.Patrol.Reset();
        UiSources.Nav.Reset();
        UiSources.Settings.Reset();
        UiSources.RynthSortWatch.Reset();
        _started = false;
    }
}
```

- [ ] **Step 5: Write `RynthAiHookup`** (`src/AcDream.Rynth.Host/RynthAiHookup.cs`)

```csharp
using AcDream.Rynth.EngineUi;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.Plugins;
using RynthCore.Engine.UI;

namespace AcDream.Rynth.Host;

/// <summary>
/// Points the RynthAi dashboard's engine stand-ins at this host and game view
/// (the loaded-plugin list, the server-menu commands, chat writes, the pump's
/// log) and unpoints on Dispose, only the ones still pointing here. Plugin
/// exports are found through RynthChatHookup's binder, which serves every
/// plugin. Every call into the view is guarded: a failure logs and carries on.
/// </summary>
internal sealed class RynthAiHookup : IDisposable
{
    private readonly IRynthGameView _view;
    private readonly Action<string> _log;
    private readonly IReadOnlyList<LoadedPlugin> _plugins;
    private readonly Action<string> _runner;
    private readonly Action<string, int> _writeToChat;
    private readonly Action<string> _pumpLog;

    public RynthAiHookup(RynthHost host, IRynthGameView view, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(log);
        _view = view;
        _log = log;
        _plugins = host.Plugins.Select(plugin => plugin.Owner).ToArray();
        _runner = command => Guard(nameof(ServerMenus.Run), () => _view.SubmitChatCommand(command));
        _writeToChat = (text, chatType) => Guard(nameof(AcMainThreadQueue.EnqueueWriteToChat), () =>
        {
            _view.WriteChatLine(text, chatType);
            return true;
        });
        _pumpLog = log;

        PluginManager.Plugins = _plugins;
        ServerMenus.Runner = _runner;
        AcMainThreadQueue.WriteToChat = _writeToChat;
        RynthAiDashboard.Log = _pumpLog;
    }

    public void Dispose()
    {
        if (ReferenceEquals(PluginManager.Plugins, _plugins)) PluginManager.Plugins = Array.Empty<LoadedPlugin>();
        if (ServerMenus.Runner == _runner) ServerMenus.Runner = null;
        if (AcMainThreadQueue.WriteToChat == _writeToChat) AcMainThreadQueue.WriteToChat = null;
        if (RynthAiDashboard.Log == _pumpLog) RynthAiDashboard.Log = _ => { };
    }

    private void Guard(string what, Func<bool> call)
    {
        try { call(); }
        catch (Exception error)
        {
            _log($"[Rynth] RynthAi {what}: view failed: {error.GetType().Name}: {error.Message}");
        }
    }
}
```

`AcMainThreadQueue.EnqueueWriteToChat` returns true whenever a writer is set, as RynthCore's does when it queues. The guard catches a failing view.

- [ ] **Step 6: Wire it into `RynthClientExtension`** (`src/AcDream.Rynth.Host/RynthClientExtension.cs`)

1. Field beside `_chatHookup`: `private RynthAiHookup? _aiHookup;`
2. Class summary: in the plugin list write "(RynthTracker, RynthOracle, RynthInventory, RynthChat, RynthAi)"; after "with a Chat button on the bar," add "RynthCore's RynthAi dashboard with a RynthAi button,"; in the commands list add "and RynthAi's <c>/ra</c>, <c>/mt</c>, <c>/ub</c>, <c>/vt</c>".
3. Bar: `PanelButtons = () => [RynthChatWindow.Title, RynthAiDashboard.Title]`.
4. Before `var view = new RuntimeRynthGameView(…)`: `RynthInstallFolders.Apply();`
5. Plugin list: add `typeof(RynthCore.Plugin.RynthAi.PluginExports),` after the RynthChat entry.
6. After `_chatHookup = new RynthChatHookup(_host, view, context.Log);`:

```csharp
        _aiHookup = new RynthAiHookup(_host, view, context.Log);
        if (context.Overlay is not null)
            Guard("RynthAi", RynthAiDashboard.Start);
```

7. After the `ginv` command registration:

```csharp
        foreach (string verb in new[] { "ra", "mt", "ub", "vt" })
            _commands.Add(context.RegisterChatCommand(verb, arguments => OfferChatBarText($"/{verb} " + arguments)));
```

8. Generalise the guard: rename `GuardChat`'s body into

```csharp
    /// <summary>Runs extension work; a throw is logged under <paramref name="area"/>. True when it ran clean.</summary>
    private bool Guard(string area, Action work)
    {
        try
        {
            work();
            return true;
        }
        catch (Exception error)
        {
            _log?.Invoke($"[Rynth] {area}: {error.GetType().Name}: {error.Message}");
            return false;
        }
    }

    /// <summary>Runs chat work; a throw is logged as a chat failure. True when it ran clean.</summary>
    private bool GuardChat(Action work) => Guard("chat", work);
```

9. `Tick`: after `GuardChat(TickChat);` add `Guard("RynthAi", () => RynthAiDashboard.Tick());`
10. `Dispose`, inside the `try` before `GuardChat(RynthChatWindow.Stop);`:

```csharp
            Guard("RynthAi", RynthAiDashboard.Stop);
            _aiHookup?.Dispose();
            _aiHookup = null;
```

Add `using AcDream.Rynth.EngineUi;` if Task 1's rename didn't already leave it there; it should have.

- [ ] **Step 7: Run the tests**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests`
Expected: all pass.

Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~RynthOverlayExtensionTests"`
Expected: all pass, the two new tests included.

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.Rynth.EngineUi/RynthAiDashboard.cs src/AcDream.Rynth.Host tests/AcDream.Rynth.Host.Tests/RynthAiDashboardTests.cs tests/AcDream.App.Tests/Extensions/RynthOverlayExtensionTests.cs
git status --porcelain external
git commit -m "feat(rynth): the client runs RynthAi and its dashboard, with a bar button"
```

---

### Task 6: Live check on local ACE

**Goal:** Confirm in a real client against the local ACE server that the RynthAi dashboard opens from the bar, shows RynthAi's live state, its macro toggle and settings drawer work, it can be dragged and its placement is saved, and nothing logs a failure.

> **USER-ORDERED GATE — NON-SKIPPABLE.** The user's standing rule: never launch a client without asking first; only the local ACE server 10.10.20.20, never Dreamweave. Close only after every item in `acceptanceCriteria` has been re-validated independently, with output captured, and the user has seen it.

**Files:**
- Modify: this plan (status note at the top with the results)

**Acceptance Criteria:**
- [ ] The user said yes to launching, in this session, before launch.
- [ ] Client log after login has `RynthCore.Plugin.RynthAi loaded.` and `RynthAi: initialized`, and no `[Rynth] RynthAi:` failure line and no `[ScriptWindows] RynthAi:` exception line.
- [ ] Clicking the bar's RynthAi button opens the dashboard; a screenshot shows it with the character's state.
- [ ] The dashboard's macro button flips its state on screen and back.
- [ ] `/ra help` prints RynthAi's command list in chat.
- [ ] After dragging the dashboard by its title row and relogging, it reopens at the dragged spot.

**Verify:** the client log, `grep -E "RynthAi|\[Rynth\]" <client log>`, and screenshots, shown to the user.

**Steps:**

- [ ] **Step 1: Ask the user** whether to launch a client now (local ACE 10.10.20.20 only). Wait for yes.
- [ ] **Step 2: Build Release and launch** following the memory note "Live testing rules": `source ~/OpenAC-dev/dev-client.env` inside a script (never echo it), `ACDREAM_LIVE=1`, a scratch `ACDREAM_ROOT_DIR`, `ACDREAM_DAT_DIR=/Users/davidsmith/AsheronsCall`, `ACDREAM_PAK_PATH="$HOME/Library/Application Support/OpenAC/data/pak/acdream.pak"`, then `~/.dotnet/dotnet src/AcDream.App/bin/Release/net10.0/AcDream.App.dll /Users/davidsmith/AsheronsCall`. "CharacterList not received" on first launch is intermittent: relaunch.
- [ ] **Step 3: Check each criterion** and capture the log lines and screenshots.
- [ ] **Step 4: Record the results** in a status note at the top of this plan, and commit:

```bash
git add docs/superpowers/plans/2026-10-18-rynth-ai-dashboard.md
git commit -m "docs: RynthAi dashboard live check"
```

```json:metadata
{"userGate": true, "tags": ["user-gate"]}
```
