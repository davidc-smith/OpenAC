# Rynth host PoC: design

Status: approved design, not built. Date: 2026-10-17.
Repository: `davidc-smith/OpenAC-Rynth` (private), based on upstream
`eriknihlen/OpenAC` main `6db05a2d`.

## Goal

Run RynthSuite plugins inside the OpenAC client without modifying their
source, so that new RynthSuite and RynthCore releases can be pulled in as
they come. This PoC hosts one plugin, **RynthTracker** (kills, XP per hour
and luminance per hour), on macOS.

## Background

RynthSuite (`aelrynth.com/git/RynthSuite.git`, MIT) plugins are built for
RynthCore (`aelrynth.com/git/rynth/RynthCore.git`, MIT), a NativeAOT
injection framework for the retail `acclient.exe` on Windows. At start-up the
engine hands each plugin one flat table of function pointers,
`RynthCoreApiNative` (about 150 `*Fn` slots, API version 86, baseline 66). It
then calls the plugin's `[UnmanagedCallersOnly]` exports for events
(`RynthPluginInit`, `RynthPluginTick`, `RynthPluginOnChatWindowText`, and so
on). A plugin checks every optional call with `Has*` (null slot = missing),
so a host that fills only part of the table still works.

The plugin source is managed C#. It targets `net10.0-windows` and
NativeAOT x86 only because of how RynthCore loads it.

## Approach

The adapter is built into the client (option B), so it can reach everything
(the runtime state now, and the renderer and input later for ImGui). It sits
behind a small extension interface in its own projects, so the changes to
upstream files stay minimal.

### Repository layout

```
external/Directory.Build.props        ours: stops the repo-wide build settings
                                      (warnings as errors, central package
                                      management) from reaching the submodules
external/RynthCore                    git submodule, untouched
external/RynthSuite                   git submodule, untouched (expects
                                      ../RynthCore beside it, as here)
src/AcDream.ClientExtensions          contract: IClientExtension,
                                      ClientExtensionContext
src/AcDream.Rynth.Sdk                 compiles RynthCore.PluginSdk +
                                      RynthCore.PluginCore sources as net10.0
src/AcDream.Rynth.Plugin.RynthTracker compiles RynthTracker sources (+
                                      Plugins/Shared/CombatText.cs) as net10.0
src/AcDream.Rynth.Host                the adapter
tests/AcDream.Rynth.Host.Tests        xunit v3
```

New projects are added to `AcDream.slnx`. No `.sln` files.

### Client seam

`AcDream.ClientExtensions` references only `AcDream.Runtime`:

```csharp
public interface IClientExtension : IDisposable
{
    void Attach(ClientExtensionContext context);
    void Tick(double dt);   // once per update frame, game thread
}
```

`ClientExtensionContext` carries the live `GameRuntime`, a log action and
a way to register a chat command.

Changes to upstream files (target: about 11 lines, including the `GameEventWiring.cs` line below):

- `src/AcDream.App/AcDream.App.csproj`: project references to the
  contract and to `AcDream.Rynth.Host`.
- `src/AcDream.App/Rendering/GameWindow.cs`: create the extensions and
  call `Attach` after `_runtime` exists, call `Tick` from `OnUpdate`, and
  dispose them on shutdown.
- `AcDream.slnx`: the new projects.

New App file: `src/AcDream.App/Extensions/ClientExtensionHost.cs`, which
lists the extensions to create.

### Building their code

- `AcDream.Rynth.Sdk` uses `Compile Include` globs over
  `external/RynthCore/src/RynthCore.PluginSdk/**/*.cs` and
  `RynthCore.PluginCore/**/*.cs`, plus the linked
  `RynthCore.Engine/Plugins/PluginContractVersion.cs`. It targets
  `net10.0` with `AllowUnsafeBlocks`.
- `AcDream.Rynth.Plugin.RynthTracker` does the same over
  `external/RynthSuite/Plugins/RynthCore.Plugin.RynthTracker/**/*.cs` and
  `Plugins/Shared/CombatText.cs`, and references `AcDream.Rynth.Sdk`.
- Both turn off warnings as errors and the repo's code analysis for that
  code only. Their plugin-manifest MSBuild targets are not imported.
- Windows-only files are excluded and replaced at build time if needed.
  RynthTracker needs none.

### The adapter (`AcDream.Rynth.Host`)

**Loading.** The host reflects over the plugin assembly for static methods
with `[UnmanagedCallersOnly(EntryPoint = "...")]` and maps each export name
to `MethodHandle.GetFunctionPointer()`. It calls them through
`delegate* unmanaged[Cdecl]` pointers, as RynthCore does. The plugin's own
`PluginExports.cs` runs unchanged. The plugin assembly is a project
reference of the host for this PoC.

**API table.** `RynthCoreApiNative` is allocated once, zeroed, in native
memory and kept for the plugin's lifetime, with `Version = 86`. The
filled slots point to static `[UnmanagedCallersOnly]` functions in the
host, which read the live `GameRuntime`:

| Slot | Source |
|---|---|
| `LogFn` | Serilog, prefixed `[Rynth] ` (plugin lines read `[Rynth] [RynthTracker] ...`) |
| `WriteToChatFn` | the client chat window |
| `GetPlayerIdFn` | `GameRuntime` player identity |
| `GetObjectNameFn` | the client object table |
| `GetObjectQuadPropertyFn` | the character's Int64 properties (TotalExperience 1, AvailableLuminance 6); any other known object's Int64 properties come from the client object table; unknown objects and ids return 0 (not found) |

All other slots stay zero.

**Callbacks.** The host implements `IRuntimeEventObserver` and subscribes to
`GameRuntime`:

| Rynth export | OpenAC source |
|---|---|
| `RynthPluginOnLoginComplete` | `OnLifecycle`, on entering `InWorld` |
| `RynthPluginOnLogout` | `OnLifecycle`, on leaving `InWorld` |
| `RynthPluginOnChatWindowText` | `OnChat` (text as UTF-16, chat type from the entry); the eat flag is passed but ignored |
| `RynthPluginOnCreateObject` / `OnDeleteObject` | `OnEntity` |
| `RynthPluginOnUpdateHealth` | `CombatState.HealthChanged` via `GameRuntime.ActionOwner.Combat` |
| `RynthPluginOnKillNotification` | the killer notification text (see open detail) |
| `RynthPluginTick` | `IClientExtension.Tick` |

**Threading.** Everything runs on the game update thread, the thread the
runtime events are raised on. This matches RynthCore's "AC pump thread"
model, so no locking is needed.

**Seeing it work.** The host adds a `/rynth tracker` chat command. It
calls `RynthTrackerGetSnapshotJson` and prints kills, XP per hour and
luminance per hour into chat.

**Errors.** An exception cannot cross an `[UnmanagedCallersOnly]` boundary:
the runtime terminates the process instead. So the host cannot catch a
plugin's exceptions. Their `RynthPluginRuntime` already catches and logs
inside every export, as it must under RynthCore too. The host's own table
functions catch everything and return 0 ("not available"). A non-zero
`RynthPluginInit` is logged and the plugin is skipped.

### Kill notifications (settled while planning)

OpenAC turns the killer notification (`GameEventType.KillerNotification`)
into a chat combat line (`GameEventWiring.cs`, about line 520). A second
handler is not possible, because `GameEventDispatcher.RegisterOwned`
shadows the previous one. `CombatState` already has a `KillLanded` event
that nothing raises. One added line in `GameEventWiring` raises it with
the death message, and the host forwards that to
`RynthPluginOnKillNotification`. This makes about 11 changed upstream lines
in all.

### Settled while planning

- `ClientExtensionContext` carries the runtime, a log action and a
  chat-command registration. The host writes chat lines itself through
  `GameRuntime.CommunicationOwner.Chat`. The data directory was dropped
  (YAGNI).
- Logout is raised from `IRuntimeEventObserver.OnLeavingWorld`, while the
  session state is still intact. Login is raised on the lifecycle change
  into `InWorld`.

## Testing

`tests/AcDream.Rynth.Host.Tests`:

1. **Export discovery** against the real RynthTracker assembly finds the
   expected exports.
2. **API table:** the implemented slots are set, the rest are zero,
   `Version == 86`, and the plugin's `Has*` checks agree.
3. **End to end without a client:** a test `GameRuntime` (as in
   `tests/AcDream.App.Tests/GameRuntimeTestFactory.cs`), the host
   attached, then login, an XP property change, a kill and ticks. The
   snapshot JSON shows the expected kill and XP counts.
4. **Fault isolation:** a table function whose game view throws returns 0
   and logs, and a plugin whose `RynthPluginInit` fails is skipped while
   the others run.

The test runtime cannot reach `InWorld` without a live session, so the
lifecycle-to-login join (`RuntimeLifecycleDelta` into `OnLoginComplete`) is
covered only by the live check; the host tests raise login directly on the
bridge. `RynthClientExtensionTests` covers the rest of the real-runtime join
(player id, XP property, kill event, ticks, the `rynth` command).

Live check: RynthTracker on the local ACE server (10.10.20.20). Kill a
few monsters, then `/rynth tracker` matches. The client is only launched
when the user is available.

## Taking their updates

**Cloning.** Clone with `git clone --recursive`, or run
`git submodule update --init` in an existing clone. The upstream CI workflow
(`.github/workflows/ci.yml`) does not check out submodules, so the wrapper
globs match nothing and CI on this repo builds red until that changes.

```
git submodule update --remote external/RynthCore external/RynthSuite
dotnet build && dotnet test tests/AcDream.Rynth.Host.Tests
git commit -am "chore: update Rynth submodules"
```

- The struct layout cannot drift, because `RynthCoreApiNative` is
  compiled from their source. New slots stay zero.
- The test pinning `Version == 86` fails when their API version moves on.
  That is the signal to review the new slots and bump it.
- The globs pick up added files. A removed or renamed file the wrappers
  depend on breaks the build.

## Done

- RynthTracker's unmodified source runs inside OpenAC on macOS.
- The tests above pass.
- The live check matches.
- The diff to upstream files stays at about 11 lines.

## Out of scope

ImGui and other rendering hooks, RynthAi, loading plugins from a folder at
runtime, and RynthCore plugin manifests.
