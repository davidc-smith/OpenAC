# Rynth Host PoC Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Run RynthSuite's unmodified RynthTracker plugin inside the OpenAC client on macOS, through a RynthCore-compatible host adapter built into the client.

**Architecture:** RynthCore and RynthSuite are git submodules under `external/`. Wrapper projects compile their sources as plain `net10.0`. `AcDream.Rynth.Host` fills RynthCore's `RynthCoreApiNative` function table from OpenAC's `GameRuntime`, calls the plugin's `[UnmanagedCallersOnly]` exports through function pointers, and turns runtime events into Rynth callbacks. The client reaches it through a one-interface contract, `AcDream.ClientExtensions`, with about 11 changed lines in upstream files.

**Tech Stack:** .NET 10 (SDK 10.0.300, `~/.dotnet/dotnet`), C# unsafe function pointers, xunit v3, git submodules.

**Spec:** `docs/superpowers/specs/2026-10-17-rynth-host-poc-design.md`

## Global Constraints

- Repository: `~/code/OpenAC/OpenAC-Rynth`, branch `feat/rynth-host-poc`. Remotes: `origin` = `davidc-smith/OpenAC-Rynth` (private), `upstream` = `eriknihlen/OpenAC`. Do not push until the user asks.
- Use `~/.dotnet/dotnet` (SDK 10.0.300 per `global.json`).
- New projects go in `AcDream.slnx`. Never create a `.sln`.
- Never edit anything inside `external/RynthCore` or `external/RynthSuite`. If their code needs to differ, exclude the file in our wrapper project and compile a replacement of ours.
- Upstream files (anything not created by this plan) change only where a task says so: `src/AcDream.App/AcDream.App.csproj`, `src/AcDream.App/Rendering/GameWindow.cs` (4 lines), `src/AcDream.Core.Net/GameEventWiring.cs` (1 line), `AcDream.slnx`, and the App/test lock files that restore regenerates.
- Our own projects follow the repo-wide `Directory.Build.props` (warnings as errors, `AnalysisLevel` latest, nullable, implicit usings). Only the two wrapper projects that compile Rynth source turn warnings-as-errors and analyzers off.
- The RynthCore API version we implement is `86` (`RynthCoreHost.CurrentApiVersion` in their SDK at submodule commit `8330793`).
- Static fields use `_camelCase`, the repo's dominant style (24 uses against 13 for `s_`).
- Commit messages end with: `Claude-Session: https://claude.ai/code/session_013qHSfUa7LKyjkciWdcioQr`
- Never launch the OpenAC client without asking the user first. Use the local ACE server 10.10.20.20 only, never Dreamweave.

**User decisions (already made):**
- "Can't we do B but built as an interface with the code that feeds the interface in a separate project so it limits the changes from upstream?" The adapter is built into the client behind `IClientExtension`, in separate projects.
- "just use slnx for any new solutions"
- Standalone private repo `davidc-smith/OpenAC-Rynth` from upstream main, for the Rynth PoC only.
- PoC plugin: RynthTracker (no ImGui). ImGui, RynthAi and folder-based plugin loading are out of scope.

---

## File Structure

```
.gitmodules                                         submodule entries (new)
external/Directory.Build.props                      stops repo build settings reaching the submodules (new)
external/RynthCore                                  submodule, untouched
external/RynthSuite                                 submodule, untouched
src/AcDream.Rynth.Sdk/AcDream.Rynth.Sdk.csproj      compiles their PluginSdk + PluginCore (new)
src/AcDream.Rynth.Plugin.RynthTracker/
    AcDream.Rynth.Plugin.RynthTracker.csproj        compiles their RynthTracker (new)
src/AcDream.ClientExtensions/
    AcDream.ClientExtensions.csproj                 contract project (new)
    IClientExtension.cs                             the interface
    ClientExtensionContext.cs                       what an extension is given
src/AcDream.Rynth.Host/
    AcDream.Rynth.Host.csproj
    IRynthGameView.cs                               the game data the table functions read
    RynthPluginExports.cs                           export discovery (entry point -> function pointer)
    RynthApiTable.cs                                the RynthCoreApiNative table + its static functions
    RynthPlugin.cs                                  one loaded plugin: typed calls into its exports
    RynthEventBridge.cs                             IRuntimeEventObserver -> plugin callbacks
    RynthHost.cs                                    owns table + plugins; init, tick, shutdown
    RuntimeRynthGameView.cs                         IRynthGameView over GameRuntime
    TrackerSnapshotText.cs                          RynthTracker snapshot JSON -> one chat line
    RynthClientExtension.cs                         IClientExtension: wires it all to the client
src/AcDream.App/Extensions/ClientExtensionHost.cs   creates, attaches, ticks, disposes extensions (new)
src/AcDream.App/Rendering/GameWindow.cs             +4 lines (upstream file)
src/AcDream.App/AcDream.App.csproj                  +2 project references (upstream file)
src/AcDream.Core.Net/GameEventWiring.cs             +1 line: raise KillLanded (upstream file)
tests/AcDream.Rynth.Host.Tests/                     host tests (new)
tests/AcDream.App.Tests/Extensions/RuntimeRynthGameViewTests.cs   view over a real GameRuntime (new)
tests/AcDream.Core.Net.Tests/GameEventWiringTests.cs              +1 test (upstream file)
```

---

### Task 1: Submodules and Rynth source wrapper projects

**Goal:** RynthCore and RynthSuite are submodules, and their SDK and RynthTracker sources build unmodified as `net10.0` inside our solution.

**Files:**
- Create: `.gitmodules` (by `git submodule add`), `external/Directory.Build.props`, `external/Directory.Build.targets`
- Create: `src/AcDream.Rynth.Sdk/AcDream.Rynth.Sdk.csproj`
- Create: `src/AcDream.Rynth.Plugin.RynthTracker/AcDream.Rynth.Plugin.RynthTracker.csproj`
- Modify: `AcDream.slnx`

**Acceptance Criteria:**
- [ ] `git submodule status` lists `external/RynthCore` and `external/RynthSuite`.
- [ ] `dotnet build src/AcDream.Rynth.Plugin.RynthTracker` succeeds with 0 errors.
- [ ] `git status --porcelain external/` shows no changes inside either submodule after the build.
- [ ] Both projects appear in `AcDream.slnx`.

**Verify:** `~/.dotnet/dotnet build src/AcDream.Rynth.Plugin.RynthTracker -nologo` → `Build succeeded.` with `0 Error(s)`

**Steps:**

- [ ] **Step 1: Add the submodules**

```bash
cd ~/code/OpenAC/OpenAC-Rynth
git submodule add https://aelrynth.com/git/rynth/RynthCore.git external/RynthCore
git submodule add https://aelrynth.com/git/RynthSuite.git external/RynthSuite
git submodule status
```

Expected: two lines, `external/RynthCore` and `external/RynthSuite`, each with a commit hash. RynthSuite's csproj files expect `../../../RynthCore` beside RynthSuite, and this layout gives them that. We don't build their csproj files anyway.

- [ ] **Step 2: Shield the submodules from the repo build settings**

MSBuild imports the nearest `Directory.Build.props`/`.targets` walking up from a project. These two stop the repo-wide ones from applying if anyone builds a project under `external/`.

`external/Directory.Build.props`:

```xml
<Project>
  <!-- external/ holds third-party submodules (RynthCore, RynthSuite) that are compiled
       through our own wrapper projects under src/. This file stops the repo-wide
       Directory.Build.props (warnings as errors, lock files) from reaching their
       own project files. -->
</Project>
```

`external/Directory.Build.targets`:

```xml
<Project>
  <!-- See Directory.Build.props beside this file. -->
</Project>
```

- [ ] **Step 3: Create the SDK wrapper project**

`src/AcDream.Rynth.Sdk/AcDream.Rynth.Sdk.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <!-- RynthCore's plugin SDK and PluginCore, compiled from the untouched submodule as
       plain net10.0 so their plugins run on the normal runtime on any OS (RynthCore
       itself builds them as net10.0-windows NativeAOT x86). Their code, their warnings:
       the repo's warnings-as-errors and analyzers are off for this project only. -->
  <PropertyGroup>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <ImplicitUsings>disable</ImplicitUsings>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <RunAnalyzers>false</RunAnalyzers>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
    <IsPackable>false</IsPackable>
    <RynthCoreRoot>$(MSBuildThisFileDirectory)..\..\external\RynthCore\</RynthCoreRoot>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="$(RynthCoreRoot)src\RynthCore.PluginSdk\**\*.cs"
             Exclude="$(RynthCoreRoot)src\RynthCore.PluginSdk\obj\**;$(RynthCoreRoot)src\RynthCore.PluginSdk\bin\**" />
    <Compile Include="$(RynthCoreRoot)src\RynthCore.PluginCore\**\*.cs"
             Exclude="$(RynthCoreRoot)src\RynthCore.PluginCore\obj\**;$(RynthCoreRoot)src\RynthCore.PluginCore\bin\**" />
    <!-- RynthCoreHost.CurrentApiVersion is compiled from the engine's own constant. -->
    <Compile Include="$(RynthCoreRoot)src\RynthCore.Engine\Plugins\PluginContractVersion.cs" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Create the RynthTracker wrapper project**

`src/AcDream.Rynth.Plugin.RynthTracker/AcDream.Rynth.Plugin.RynthTracker.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <!-- RynthSuite's RynthTracker plugin, compiled from the untouched submodule as plain
       net10.0 (see AcDream.Rynth.Sdk). Its PluginExports class is what the host loads. -->
  <PropertyGroup>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <ImplicitUsings>disable</ImplicitUsings>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <RunAnalyzers>false</RunAnalyzers>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
    <IsPackable>false</IsPackable>
    <RynthSuitePlugins>$(MSBuildThisFileDirectory)..\..\external\RynthSuite\Plugins\</RynthSuitePlugins>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="$(RynthSuitePlugins)RynthCore.Plugin.RynthTracker\**\*.cs"
             Exclude="$(RynthSuitePlugins)RynthCore.Plugin.RynthTracker\obj\**;$(RynthSuitePlugins)RynthCore.Plugin.RynthTracker\bin\**" />
    <Compile Include="$(RynthSuitePlugins)Shared\CombatText.cs" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\AcDream.Rynth.Sdk\AcDream.Rynth.Sdk.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 5: Add both projects to the solution and build**

```bash
cd ~/code/OpenAC/OpenAC-Rynth
~/.dotnet/dotnet sln AcDream.slnx add src/AcDream.Rynth.Sdk/AcDream.Rynth.Sdk.csproj src/AcDream.Rynth.Plugin.RynthTracker/AcDream.Rynth.Plugin.RynthTracker.csproj
~/.dotnet/dotnet build src/AcDream.Rynth.Plugin.RynthTracker -nologo
git status --porcelain external/
```

Expected: `Build succeeded.` with `0 Error(s)`, and no output from `git status --porcelain external/`. The restore writes `packages.neutral.lock.json` in each new project folder (the repo turns on lock files). Commit those files.

If the build reports an error from their code, don't edit the submodule. Read the error and add the minimum `NoWarn` code or `Exclude` to the wrapper project. A spike on 2026-10-17 compiled these exact sources as `net10.0` with 0 warnings, so this is unlikely.

- [ ] **Step 6: Commit**

```bash
git add .gitmodules external/Directory.Build.props external/Directory.Build.targets external/RynthCore external/RynthSuite \
  src/AcDream.Rynth.Sdk src/AcDream.Rynth.Plugin.RynthTracker AcDream.slnx
git commit -m "build: RynthCore and RynthSuite submodules, compiled as net10.0 wrapper projects

Claude-Session: https://claude.ai/code/session_013qHSfUa7LKyjkciWdcioQr"
```

---

### Task 2: Export discovery and the API table

**Goal:** `AcDream.Rynth.Host` can find a plugin's exports and build a `RynthCoreApiNative` table whose filled slots read from an `IRynthGameView` and never throw.

**Files:**
- Create: `src/AcDream.Rynth.Host/AcDream.Rynth.Host.csproj`, `IRynthGameView.cs`, `RynthPluginExports.cs`, `RynthApiTable.cs`
- Create: `tests/AcDream.Rynth.Host.Tests/AcDream.Rynth.Host.Tests.csproj`, `FakeGameView.cs`, `RynthPluginExportsTests.cs`, `RynthApiTableTests.cs`
- Modify: `AcDream.slnx`

**Acceptance Criteria:**
- [ ] Export discovery on RynthTracker's `PluginExports` finds `RynthPluginInit`, `RynthPluginTick`, `RynthPluginOnChatWindowText`, `RynthPluginOnKillNotification` and `RynthTrackerGetSnapshotJson`.
- [ ] A new table has `Version == 86`. `RynthCoreHost` built from it reports `HasGetPlayerId`, `HasGetObjectName`, `HasGetObjectQuadProperty` true and `HasCastSpell` false.
- [ ] Through `RynthCoreHost`, `GetPlayerId`, `TryGetObjectName`, `TryGetObjectQuadProperty`, `WriteToChat` and `Log` return the fake view's values.
- [ ] With a view that throws, `TryGetObjectName` returns false and the failure is logged. The test process doesn't crash.
- [ ] Creating a second table while one is live throws `InvalidOperationException`.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo` → all tests pass

**Steps:**

- [ ] **Step 1: Create the host project**

`src/AcDream.Rynth.Host/AcDream.Rynth.Host.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\AcDream.Runtime\AcDream.Runtime.csproj" />
    <ProjectReference Include="..\AcDream.Rynth.Sdk\AcDream.Rynth.Sdk.csproj" />
    <ProjectReference Include="..\AcDream.Rynth.Plugin.RynthTracker\AcDream.Rynth.Plugin.RynthTracker.csproj" />
  </ItemGroup>
</Project>
```

`src/AcDream.Rynth.Host/IRynthGameView.cs`:

```csharp
namespace AcDream.Rynth.Host;

/// <summary>
/// The game data the RynthCore table functions read. The client implements it
/// over <c>GameRuntime</c> (<see cref="RuntimeRynthGameView"/>); tests use a fake.
/// </summary>
public interface IRynthGameView
{
    /// <summary>The player's object id; 0 before login.</summary>
    uint PlayerId { get; }

    /// <summary>The name of a known object.</summary>
    bool TryGetObjectName(uint objectId, out string name);

    /// <summary>An Int64 ("quad") property of an object, by property id.</summary>
    bool TryGetInt64Property(uint objectId, uint propertyId, out long value);

    /// <summary>Adds a line to the chat window.</summary>
    void WriteChatLine(string text, int chatType);
}
```

- [ ] **Step 2: Create the test project and its fake view**

`tests/AcDream.Rynth.Host.Tests/AcDream.Rynth.Host.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\AcDream.Rynth.Host\AcDream.Rynth.Host.csproj" />
  </ItemGroup>
</Project>
```

The API table is process-wide (see `RynthApiTable`), so tests that create one must not run in parallel. Turn off parallelisation for this assembly.

`tests/AcDream.Rynth.Host.Tests/FakeGameView.cs`:

```csharp
using AcDream.Rynth.Host;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace AcDream.Rynth.Host.Tests;

internal sealed class FakeGameView : IRynthGameView
{
    public uint PlayerId { get; set; }
    public Dictionary<uint, string> Names { get; } = new();
    public Dictionary<(uint Object, uint Property), long> Int64s { get; } = new();
    public List<(string Text, int ChatType)> ChatLines { get; } = new();
    public bool Throw { get; set; }

    public bool TryGetObjectName(uint objectId, out string name)
    {
        if (Throw) throw new InvalidOperationException("view failed");
        return Names.TryGetValue(objectId, out name!);
    }

    public bool TryGetInt64Property(uint objectId, uint propertyId, out long value)
    {
        if (Throw) throw new InvalidOperationException("view failed");
        return Int64s.TryGetValue((objectId, propertyId), out value);
    }

    public void WriteChatLine(string text, int chatType) => ChatLines.Add((text, chatType));
}
```

Add both new projects to the solution:

```bash
~/.dotnet/dotnet sln AcDream.slnx add src/AcDream.Rynth.Host/AcDream.Rynth.Host.csproj tests/AcDream.Rynth.Host.Tests/AcDream.Rynth.Host.Tests.csproj
```

- [ ] **Step 3: Write the failing tests**

`tests/AcDream.Rynth.Host.Tests/RynthPluginExportsTests.cs`:

```csharp
using AcDream.Rynth.Host;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthPluginExportsTests
{
    [Fact]
    public void FromType_FindsRynthTrackerExports()
    {
        RynthPluginExports exports = RynthPluginExports.FromType(
            typeof(RynthCore.Plugin.RynthTracker.PluginExports));

        Assert.Contains("RynthPluginInit", exports.Names);
        Assert.Contains("RynthPluginTick", exports.Names);
        Assert.Contains("RynthPluginOnChatWindowText", exports.Names);
        Assert.Contains("RynthPluginOnKillNotification", exports.Names);
        Assert.Contains("RynthTrackerGetSnapshotJson", exports.Names);
        Assert.True(exports.TryGet("RynthPluginInit", out IntPtr init));
        Assert.NotEqual(IntPtr.Zero, init);
        Assert.False(exports.TryGet("RynthPluginOnChatBarEnter", out _));
    }
}
```

`tests/AcDream.Rynth.Host.Tests/RynthApiTableTests.cs`:

```csharp
using AcDream.Rynth.Host;
using RynthCore.PluginSdk;

namespace AcDream.Rynth.Host.Tests;

public sealed unsafe class RynthApiTableTests
{
    private readonly List<string> _log = new();

    [Fact]
    public void NewTable_HasApiVersionAndOnlyTheImplementedSlots()
    {
        using var table = new RynthApiTable(new FakeGameView(), _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.Equal(86u, host.Version);
        Assert.Equal(RynthCoreHost.CurrentApiVersion, RynthApiTable.ApiVersion);
        Assert.True(host.HasGetPlayerId);
        Assert.True(host.HasGetObjectName);
        Assert.True(host.HasGetObjectQuadProperty);
        Assert.False(host.HasCastSpell);
        Assert.False(host.HasUseObject);
    }

    [Fact]
    public void TableFunctions_ReadTheGameView()
    {
        var view = new FakeGameView { PlayerId = 0x50000001 };
        view.Names[0x80000010] = "Drudge Skulker";
        view.Int64s[(0x50000001, 1)] = 123_456;
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.Equal(0x50000001u, host.GetPlayerId());
        Assert.True(host.TryGetObjectName(0x80000010, out string name));
        Assert.Equal("Drudge Skulker", name);
        Assert.False(host.TryGetObjectName(0x80000011, out _));
        Assert.True(host.TryGetObjectQuadProperty(0x50000001, 1, out long xp));
        Assert.Equal(123_456, xp);
        Assert.False(host.TryGetObjectQuadProperty(0x50000001, 6, out _));

        Assert.True(host.WriteToChat("hello", 3));
        Assert.Equal(("hello", 3), Assert.Single(view.ChatLines));

        host.Log("from the plugin");
        Assert.Contains("from the plugin", _log);
    }

    [Fact]
    public void TableFunctions_ReturnNotAvailableWhenTheViewThrows()
    {
        var view = new FakeGameView { Throw = true };
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.False(host.TryGetObjectName(1, out _));
        Assert.False(host.TryGetObjectQuadProperty(1, 1, out _));
        Assert.Contains(_log, line => line.Contains("GetObjectName threw", StringComparison.Ordinal));
    }

    [Fact]
    public void OnlyOneTableCanBeLive()
    {
        using var first = new RynthApiTable(new FakeGameView(), _log.Add);
        Assert.Throws<InvalidOperationException>(() => new RynthApiTable(new FakeGameView(), _log.Add));
    }
}
```

- [ ] **Step 4: Run the tests to see them fail**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo`
Expected: build errors, because `RynthPluginExports` and `RynthApiTable` don't exist yet.

- [ ] **Step 5: Implement export discovery**

`src/AcDream.Rynth.Host/RynthPluginExports.cs`:

```csharp
using System.Reflection;
using System.Runtime.InteropServices;

namespace AcDream.Rynth.Host;

/// <summary>
/// A RynthCore plugin's exports: each static method marked
/// <c>[UnmanagedCallersOnly(EntryPoint = "...")]</c>, by entry point. RynthCore
/// finds them in the plugin's native DLL; here the plugin is managed code, so
/// they are found by reflection and called through the same function pointers.
/// </summary>
public sealed class RynthPluginExports
{
    private readonly Dictionary<string, IntPtr> _byEntryPoint;

    private RynthPluginExports(Dictionary<string, IntPtr> byEntryPoint) =>
        _byEntryPoint = byEntryPoint;

    /// <summary>The entry point names found.</summary>
    public IReadOnlyCollection<string> Names => _byEntryPoint.Keys;

    /// <summary>Reads the exports of a plugin's exports class.</summary>
    public static RynthPluginExports FromType(Type exportsType)
    {
        ArgumentNullException.ThrowIfNull(exportsType);
        var byEntryPoint = new Dictionary<string, IntPtr>(StringComparer.Ordinal);
        foreach (MethodInfo method in exportsType.GetMethods(
                     BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (method.GetCustomAttribute<UnmanagedCallersOnlyAttribute>()?.EntryPoint
                is { Length: > 0 } entryPoint)
            {
                byEntryPoint[entryPoint] = method.MethodHandle.GetFunctionPointer();
            }
        }

        return new RynthPluginExports(byEntryPoint);
    }

    /// <summary>The function pointer of an export, if the plugin has it.</summary>
    public bool TryGet(string entryPoint, out IntPtr pointer) =>
        _byEntryPoint.TryGetValue(entryPoint, out pointer);
}
```

- [ ] **Step 6: Implement the API table**

`src/AcDream.Rynth.Host/RynthApiTable.cs`:

```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RynthCore.PluginSdk;

namespace AcDream.Rynth.Host;

/// <summary>
/// The RynthCore engine API table handed to every plugin's RynthPluginInit.
/// Only the slots this host implements are set; the rest stay zero, which a
/// plugin's <c>Has*</c> checks read as "not available".
/// </summary>
/// <remarks>
/// The table functions are static (a function pointer carries no instance),
/// so they reach the live table through <see cref="_current"/>: one table per
/// process. An exception must never leave them, because an exception cannot
/// cross an [UnmanagedCallersOnly] boundary (the runtime ends the process), so
/// each one catches everything, logs, and returns 0.
/// </remarks>
public sealed unsafe class RynthApiTable : IDisposable
{
    /// <summary>The RynthCore engine API version this host implements.</summary>
    public const uint ApiVersion = 86;

    private static RynthApiTable? _current;

    private readonly IRynthGameView _view;
    private readonly Action<string> _log;
    private IntPtr _nameBuffer;

    public RynthApiTable(IRynthGameView view, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(log);
        if (Interlocked.CompareExchange(ref _current, this, null) is not null)
            throw new InvalidOperationException("Only one RynthApiTable can be live at a time.");

        _view = view;
        _log = log;
        Native = (RynthCoreApiNative*)NativeMemory.AllocZeroed((nuint)sizeof(RynthCoreApiNative));
        Native->Version = ApiVersion;
        Native->LogFn = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, void>)&Log;
        Native->WriteToChatFn = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, int, int>)&WriteToChat;
        Native->GetPlayerIdFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint>)&GetPlayerId;
        Native->GetObjectNameFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, IntPtr>)&GetObjectName;
        Native->GetObjectQuadPropertyFn =
            (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, long*, int>)&GetObjectQuadProperty;
    }

    /// <summary>The table, in native memory, for as long as this object lives.</summary>
    public RynthCoreApiNative* Native { get; private set; }

    public void Dispose()
    {
        if (Native is null)
            return;
        NativeMemory.Free(Native);
        Native = null;
        if (_nameBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_nameBuffer);
            _nameBuffer = IntPtr.Zero;
        }

        Interlocked.CompareExchange(ref _current, null, this);
    }

    private void Fail(string function, Exception error)
    {
        try { _log($"{function} threw {error.GetType().Name}: {error.Message}"); }
        catch { }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void Log(IntPtr message)
    {
        RynthApiTable? table = _current;
        if (table is null || message == IntPtr.Zero)
            return;
        try { table._log(Marshal.PtrToStringAnsi(message) ?? string.Empty); }
        catch { }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int WriteToChat(IntPtr textUtf16, int chatType)
    {
        RynthApiTable? table = _current;
        if (table is null || textUtf16 == IntPtr.Zero)
            return 0;
        try
        {
            table._view.WriteChatLine(Marshal.PtrToStringUni(textUtf16) ?? string.Empty, chatType);
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(WriteToChat), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static uint GetPlayerId()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table._view.PlayerId; }
        catch (Exception error)
        {
            table.Fail(nameof(GetPlayerId), error);
            return 0;
        }
    }

    // RynthCore hands back a pointer the plugin reads at once (PtrToStringAnsi) and never
    // frees, so the host keeps one buffer and replaces it on the next call.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static IntPtr GetObjectName(uint objectId)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return IntPtr.Zero;
        try
        {
            if (!table._view.TryGetObjectName(objectId, out string name))
                return IntPtr.Zero;
            IntPtr previous = table._nameBuffer;
            table._nameBuffer = Marshal.StringToHGlobalAnsi(name);
            if (previous != IntPtr.Zero)
                Marshal.FreeHGlobal(previous);
            return table._nameBuffer;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectName), error);
            return IntPtr.Zero;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectQuadProperty(uint objectId, uint propertyId, long* value)
    {
        RynthApiTable? table = _current;
        if (table is null || value is null)
            return 0;
        try
        {
            if (!table._view.TryGetInt64Property(objectId, propertyId, out long found))
                return 0;
            *value = found;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectQuadProperty), error);
            return 0;
        }
    }
}
```

The `Assert.Contains(_log, line => line.Contains("GetObjectName threw"))` test matches `Fail(nameof(GetObjectName), …)`, which logs `GetObjectName threw InvalidOperationException: view failed`. The table logs without a prefix. `RynthHost` adds `[Rynth] ` to everything it and the table log (Task 3).

- [ ] **Step 7: Run the tests to see them pass**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo`
Expected: 5 tests pass, 0 failed. If an analyzer warning (an error here) fires on our code, fix the code. Don't suppress it, unless the analyzer objects to the function-pointer pattern itself. In that case add a targeted `#pragma warning disable <id>` with a one-line reason.

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.Rynth.Host tests/AcDream.Rynth.Host.Tests AcDream.slnx
git commit -m "feat(rynth): export discovery and the RynthCore API table

Claude-Session: https://claude.ai/code/session_013qHSfUa7LKyjkciWdcioQr"
```

---

### Task 3: Plugin calls, event bridge and host, running RynthTracker end to end

**Goal:** `RynthHost` initialises RynthTracker against the table, forwards runtime events and ticks to it, and its snapshot reflects a login, an XP rise and a kill. No client is involved.

**Files:**
- Create: `src/AcDream.Rynth.Host/RynthPlugin.cs`, `RynthEventBridge.cs`, `RynthHost.cs`, `TrackerSnapshotText.cs`
- Create: `tests/AcDream.Rynth.Host.Tests/RynthHostTests.cs`, `TrackerSnapshotTextTests.cs`

**Acceptance Criteria:**
- [ ] `RynthHost` with RynthTracker initialises it (`RynthPlugin.IsActive` true) and logs `[RynthTracker] Plugin initialized.`
- [ ] After a lifecycle change into `InWorld`, an XP rise from 1000 to 1600 within a kill window, a kill notification and enough ticks, the snapshot JSON has `"kt":1` and `"xt":600`.
- [ ] A plugin type with no `RynthPluginInit` export is logged as skipped, and the host still runs the other plugins.
- [ ] `OnLeavingWorld` raises `RynthPluginOnLogout`, and the tracker logs nothing more for kills afterwards (its `_loginComplete` is false). This is observed as a later kill leaving `"kt":1` unchanged.
- [ ] `TrackerSnapshotText.Format` turns a snapshot JSON into `Rynth tracker: 1 kills (2.0/h), 600 XP (1,200/h), 0 lum, session 30m 0s`.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo` → all tests pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

`tests/AcDream.Rynth.Host.Tests/RynthHostTests.cs`:

```csharp
using AcDream.Runtime;
using AcDream.Rynth.Host;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthHostTests
{
    private const uint TotalExperience = 1;

    private readonly List<string> _log = new();

    // RynthTracker saves its session on shutdown and restores it on the next login of
    // the same character in the same process, so every test plays a different character.
    private readonly FakeGameView _view = new()
    {
        PlayerId = 0x50000000u | (uint)Random.Shared.Next(1, 0xFFFF),
    };

    private RynthHost CreateTrackerHost() =>
        new(_view, _log.Add, [typeof(RynthCore.Plugin.RynthTracker.PluginExports)]);

    [Fact]
    public void Start_InitialisesRynthTracker()
    {
        using RynthHost host = CreateTrackerHost();

        RynthPlugin plugin = Assert.Single(host.Plugins);
        Assert.True(plugin.IsActive);
        Assert.Contains(_log, line => line.Contains("[RynthTracker] Plugin initialized.", StringComparison.Ordinal));
    }

    [Fact]
    public void TrackerCountsAKillAndTheXpThatFollows()
    {
        using RynthHost host = CreateTrackerHost();
        _view.Int64s[(_view.PlayerId, TotalExperience)] = 1000;

        EnterWorld(host);
        TickHalfASecond(host);            // first poll: baseline 1000, nothing gained yet
        host.Bridge.OnKillLanded("You killed the drudge!");
        _view.Int64s[(_view.PlayerId, TotalExperience)] = 1600;
        TickHalfASecond(host);            // poll inside the 2.5 s kill window

        string json = host.Plugins[0].CallStringExport("RynthTrackerGetSnapshotJson")!;
        Assert.Contains("\"kt\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"xt\":600", json, StringComparison.Ordinal);
    }

    [Fact]
    public void LeavingTheWorldEndsTheSession()
    {
        using RynthHost host = CreateTrackerHost();
        EnterWorld(host);
        host.Bridge.OnKillLanded("You killed the drudge!");

        host.Bridge.OnLeavingWorld();
        host.Bridge.OnKillLanded("You killed another drudge!");
        TickHalfASecond(host);

        string json = host.Plugins[0].CallStringExport("RynthTrackerGetSnapshotJson")!;
        Assert.Contains("\"kt\":1", json, StringComparison.Ordinal);
    }

    [Fact]
    public void APluginWithoutInitIsSkipped()
    {
        using var host = new RynthHost(
            _view,
            _log.Add,
            [typeof(NoExports), typeof(RynthCore.Plugin.RynthTracker.PluginExports)]);

        Assert.Single(host.Plugins);
        Assert.Contains(_log, line => line.Contains("NoExports", StringComparison.Ordinal)
            && line.Contains("no RynthPluginInit", StringComparison.Ordinal));
    }

    private static void EnterWorld(RynthHost host) =>
        host.Bridge.OnLifecycle(new RuntimeLifecycleDelta(
            default, RuntimeLifecycleState.Starting, RuntimeLifecycleState.InWorld));

    // RynthTracker rebuilds its snapshot and polls XP every 30th tick (0.5 s at 60 Hz).
    private static void TickHalfASecond(RynthHost host)
    {
        for (int i = 0; i < 30; i++)
            host.Tick();
    }

    private static class NoExports
    {
    }
}
```

`tests/AcDream.Rynth.Host.Tests/TrackerSnapshotTextTests.cs`:

```csharp
using AcDream.Rynth.Host;

namespace AcDream.Rynth.Host.Tests;

public sealed class TrackerSnapshotTextTests
{
    [Fact]
    public void Format_SummarisesTheSnapshot()
    {
        const string json =
            "{\"ss\":1800.0,\"xt\":600,\"xh\":1200,\"lt\":0,\"lh\":0,\"kt\":1,\"kh\":2.0,\"xk\":600,\"dt\":0}";

        Assert.Equal(
            "Rynth tracker: 1 kills (2.0/h), 600 XP (1,200/h), 0 lum, session 30m 0s",
            TrackerSnapshotText.Format(json));
    }

    [Fact]
    public void Format_SaysSoWhenThereIsNoSnapshot()
    {
        Assert.Equal("Rynth tracker: no data yet", TrackerSnapshotText.Format(null));
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo`
Expected: build errors, because `RynthHost`, `RynthPlugin`, `RynthEventBridge` and `TrackerSnapshotText` don't exist yet.

- [ ] **Step 3: Implement `RynthPlugin`**

`src/AcDream.Rynth.Host/RynthPlugin.cs`:

```csharp
using System.Runtime.InteropServices;
using RynthCore.PluginSdk;

namespace AcDream.Rynth.Host;

/// <summary>
/// One loaded RynthCore plugin: typed calls into its exports, with the
/// signatures RynthCore's PluginManager uses. A missing export is skipped, as
/// RynthCore does. Exceptions inside a plugin never reach here: the plugin's
/// RynthPluginRuntime catches and logs them inside each export.
/// </summary>
public sealed unsafe class RynthPlugin
{
    private readonly RynthPluginExports _exports;

    public RynthPlugin(string name, RynthPluginExports exports)
    {
        Name = name;
        _exports = exports;
    }

    public string Name { get; }

    /// <summary>Initialised and not yet shut down.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Calls RynthPluginInit; 0 is success.</summary>
    public int Initialize(RynthCoreApiNative* api)
    {
        if (!_exports.TryGet("RynthPluginInit", out IntPtr init))
            return -1;
        int result = ((delegate* unmanaged[Cdecl]<RynthCoreApiNative*, int>)init)(api);
        IsActive = result == 0;
        return result;
    }

    public void Tick() => CallVoid("RynthPluginTick");

    public void OnLoginComplete() => CallVoid("RynthPluginOnLoginComplete");

    public void OnLogout() => CallVoid("RynthPluginOnLogout");

    public void OnCreateObject(uint objectId) => CallObject("RynthPluginOnCreateObject", objectId);

    public void OnDeleteObject(uint objectId) => CallObject("RynthPluginOnDeleteObject", objectId);

    /// <summary>OpenAC knows the health ratio only, so current and max are 0.</summary>
    public void OnUpdateHealth(uint targetId, float healthRatio)
    {
        if (IsActive && _exports.TryGet("RynthPluginOnUpdateHealth", out IntPtr export))
            ((delegate* unmanaged[Cdecl]<uint, float, uint, uint, void>)export)(targetId, healthRatio, 0, 0);
    }

    /// <summary>The eat flag is passed but ignored (the client shows the line regardless).</summary>
    public void OnChatWindowText(string text, int chatType)
    {
        if (!IsActive || !_exports.TryGet("RynthPluginOnChatWindowText", out IntPtr export))
            return;
        int eat = 0;
        fixed (char* textUtf16 = text)
        {
            ((delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, void>)export)(
                (IntPtr)textUtf16, chatType, (IntPtr)(&eat));
        }
    }

    public void OnKillNotification(string deathMessage)
    {
        if (!IsActive || !_exports.TryGet("RynthPluginOnKillNotification", out IntPtr export))
            return;
        fixed (char* textUtf16 = deathMessage)
        {
            ((delegate* unmanaged[Cdecl]<IntPtr, void>)export)((IntPtr)textUtf16);
        }
    }

    /// <summary>Calls an export that returns an ANSI string, such as a snapshot.</summary>
    public string? CallStringExport(string entryPoint)
    {
        if (!IsActive || !_exports.TryGet(entryPoint, out IntPtr export))
            return null;
        IntPtr result = ((delegate* unmanaged[Cdecl]<IntPtr>)export)();
        return result == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(result);
    }

    public void Shutdown()
    {
        if (!IsActive)
            return;
        CallVoid("RynthPluginShutdown");
        IsActive = false;
    }

    private void CallVoid(string entryPoint)
    {
        if (IsActive && _exports.TryGet(entryPoint, out IntPtr export))
            ((delegate* unmanaged[Cdecl]<void>)export)();
    }

    private void CallObject(string entryPoint, uint objectId)
    {
        if (IsActive && _exports.TryGet(entryPoint, out IntPtr export))
            ((delegate* unmanaged[Cdecl]<uint, void>)export)(objectId);
    }
}
```

- [ ] **Step 4: Implement `RynthEventBridge`**

`src/AcDream.Rynth.Host/RynthEventBridge.cs`:

```csharp
using AcDream.Runtime;

namespace AcDream.Rynth.Host;

/// <summary>
/// Turns OpenAC runtime events into RynthCore plugin callbacks. Raised on the
/// game update thread, which is the thread RynthCore plugins expect (the
/// retail client's message pump).
/// </summary>
public sealed class RynthEventBridge : IRuntimeEventObserver
{
    private readonly IReadOnlyList<RynthPlugin> _plugins;

    public RynthEventBridge(IReadOnlyList<RynthPlugin> plugins) => _plugins = plugins;

    public void OnLifecycle(in RuntimeLifecycleDelta delta)
    {
        if (delta.Current == RuntimeLifecycleState.InWorld
            && delta.Previous != RuntimeLifecycleState.InWorld)
        {
            foreach (RynthPlugin plugin in _plugins)
                plugin.OnLoginComplete();
        }
    }

    // Raised while the session's state is still intact, once per stay in the world.
    public void OnLeavingWorld()
    {
        foreach (RynthPlugin plugin in _plugins)
            plugin.OnLogout();
    }

    public void OnEntity(in RuntimeEntityDelta delta)
    {
        uint objectId = delta.Entity.Identity.ServerGuid;
        if (delta.Change == RuntimeEntityChange.Registered)
        {
            foreach (RynthPlugin plugin in _plugins)
                plugin.OnCreateObject(objectId);
        }
        else if (delta.Change == RuntimeEntityChange.Deleted && !delta.ReplacedInPlace)
        {
            foreach (RynthPlugin plugin in _plugins)
                plugin.OnDeleteObject(objectId);
        }
    }

    public void OnChat(in RuntimeChatDelta delta)
    {
        RuntimeChatEntry entry = delta.Entry;
        string text = entry.DisplayText.Length > 0 ? entry.DisplayText : entry.Text;
        foreach (RynthPlugin plugin in _plugins)
            plugin.OnChatWindowText(text, entry.LogTextType);
    }

    /// <summary>From <c>CombatState.HealthChanged</c>.</summary>
    public void OnHealthChanged(uint targetId, float healthRatio)
    {
        foreach (RynthPlugin plugin in _plugins)
            plugin.OnUpdateHealth(targetId, healthRatio);
    }

    /// <summary>From <c>CombatState.KillLanded</c>: the server's killer notification text.</summary>
    public void OnKillLanded(string deathMessage)
    {
        foreach (RynthPlugin plugin in _plugins)
            plugin.OnKillNotification(deathMessage);
    }

    public void OnCommand(in RuntimeCommandDelta delta)
    {
    }

    public void OnInventory(in RuntimeInventoryDelta delta)
    {
    }

    public void OnMovement(in RuntimeMovementDelta delta)
    {
    }

    public void OnPortal(in RuntimePortalDelta delta)
    {
    }

    public void OnCombat(in RuntimeCombatDelta delta)
    {
    }
}
```

- [ ] **Step 5: Implement `RynthHost`**

`src/AcDream.Rynth.Host/RynthHost.cs`:

```csharp
namespace AcDream.Rynth.Host;

/// <summary>
/// Hosts RynthCore plugins: owns the API table, initialises each plugin,
/// ticks them and shuts them down. One host per process (the table is
/// process-wide).
/// </summary>
public sealed unsafe class RynthHost : IDisposable
{
    private readonly RynthApiTable _table;
    private readonly List<RynthPlugin> _plugins = new();
    private readonly Action<string> _log;

    /// <param name="log">Gets every host and plugin log line, prefixed "[Rynth] ".</param>
    /// <param name="exportsTypes">Each plugin's exports class (its PluginExports).</param>
    public RynthHost(IRynthGameView view, Action<string> log, IEnumerable<Type> exportsTypes)
    {
        ArgumentNullException.ThrowIfNull(exportsTypes);
        ArgumentNullException.ThrowIfNull(log);
        _log = line => log($"[Rynth] {line}");
        _table = new RynthApiTable(view, _log);
        foreach (Type exportsType in exportsTypes)
            Load(exportsType);
        Bridge = new RynthEventBridge(_plugins);
    }

    public IReadOnlyList<RynthPlugin> Plugins => _plugins;

    /// <summary>The runtime observer that feeds the plugins.</summary>
    public RynthEventBridge Bridge { get; }

    public void Tick()
    {
        foreach (RynthPlugin plugin in _plugins)
            plugin.Tick();
    }

    public void Dispose()
    {
        foreach (RynthPlugin plugin in _plugins)
            plugin.Shutdown();
        _plugins.Clear();
        _table.Dispose();
    }

    private void Load(Type exportsType)
    {
        RynthPluginExports exports = RynthPluginExports.FromType(exportsType);
        string name = exportsType.Namespace ?? exportsType.Name;
        if (!exports.TryGet("RynthPluginInit", out _))
        {
            _log($"{exportsType.FullName}: no RynthPluginInit export, skipped.");
            return;
        }

        var plugin = new RynthPlugin(name, exports);
        int result = plugin.Initialize(_table.Native);
        if (result != 0)
        {
            _log($"{name}: RynthPluginInit returned {result}, skipped.");
            return;
        }

        _plugins.Add(plugin);
        _log($"{name} loaded.");
    }
}
```

- [ ] **Step 6: Implement `TrackerSnapshotText`**

The tracker's snapshot keys (from `RynthTrackerPlugin.BuildSnapshot`): `ss` session seconds, `xt` XP total, `xh` XP per hour, `lt` luminance total, `lh` luminance per hour, `kt` kills, `kh` kills per hour, `xk` XP per kill, `dt` deaths.

`src/AcDream.Rynth.Host/TrackerSnapshotText.cs`:

```csharp
using System.Globalization;
using System.Text.Json;

namespace AcDream.Rynth.Host;

/// <summary>RynthTracker's snapshot JSON as one chat line.</summary>
public static class TrackerSnapshotText
{
    public static string Format(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return "Rynth tracker: no data yet";

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        var session = TimeSpan.FromSeconds(root.GetProperty("ss").GetDouble());
        CultureInfo culture = CultureInfo.InvariantCulture;
        return string.Format(
            culture,
            "Rynth tracker: {0:N0} kills ({1:0.0}/h), {2:N0} XP ({3:N0}/h), {4:N0} lum, session {5}m {6}s",
            root.GetProperty("kt").GetInt64(),
            root.GetProperty("kh").GetDouble(),
            root.GetProperty("xt").GetInt64(),
            root.GetProperty("xh").GetInt64(),
            root.GetProperty("lt").GetInt64(),
            (int)session.TotalMinutes,
            session.Seconds);
    }
}
```

- [ ] **Step 7: Run the tests to see them pass**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo`
Expected: all tests pass (5 from Task 2 plus 6 here).

If `TrackerCountsAKillAndTheXpThatFollows` shows `"xt":0`, read `RynthTrackerPlugin.PollExperience` and `TrackerRise.Take` in `external/RynthSuite/Plugins/RynthCore.Plugin.RynthTracker/`. The first read sets the baseline, so the rise has to come after one poll, which is how the test is written. Adjust the test's tick count to match the plugin. Never change the plugin.

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.Rynth.Host tests/AcDream.Rynth.Host.Tests
git commit -m "feat(rynth): host, plugin calls and event bridge run RynthTracker end to end

Claude-Session: https://claude.ai/code/session_013qHSfUa7LKyjkciWdcioQr"
```

---

### Task 4: Client extension contract and the Rynth client extension

**Goal:** A `IClientExtension` contract exists, and `RynthClientExtension` implements it over a live `GameRuntime`: game view, runtime subscription, combat events and the `/rynth tracker` command.

**Files:**
- Create: `src/AcDream.ClientExtensions/AcDream.ClientExtensions.csproj`, `IClientExtension.cs`, `ClientExtensionContext.cs`
- Create: `src/AcDream.Rynth.Host/RuntimeRynthGameView.cs`, `RynthClientExtension.cs`
- Modify: `src/AcDream.Rynth.Host/AcDream.Rynth.Host.csproj` (add the contract reference)
- Modify: `AcDream.slnx`
- Test: `tests/AcDream.App.Tests/Extensions/RuntimeRynthGameViewTests.cs` (runs once Task 5 makes App reference the host; written here, run in Task 5)

**Acceptance Criteria:**
- [ ] `AcDream.ClientExtensions` references only `AcDream.Runtime`.
- [ ] `RuntimeRynthGameView` returns the player guid from `PlayerIdentity.ServerGuid`, a name from the object table, and Int64 properties from the player's description first, then the object table.
- [ ] `RynthClientExtension.Attach` subscribes the bridge to the runtime, subscribes `HealthChanged` and `KillLanded`, and registers `rynth`. `Dispose` undoes all of them.
- [ ] `dotnet build src/AcDream.Rynth.Host` succeeds with 0 warnings.

**Verify:** `~/.dotnet/dotnet build src/AcDream.Rynth.Host -nologo` → `Build succeeded.` `0 Warning(s)` `0 Error(s)`

**Steps:**

- [ ] **Step 1: Create the contract project**

`src/AcDream.ClientExtensions/AcDream.ClientExtensions.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\AcDream.Runtime\AcDream.Runtime.csproj" />
  </ItemGroup>
</Project>
```

`src/AcDream.ClientExtensions/IClientExtension.cs`:

```csharp
namespace AcDream.ClientExtensions;

/// <summary>
/// Code built into the client that sees the client's own runtime, unlike a
/// plugin, which sees only AcDream.Plugin.Abstractions. The client creates
/// each extension once, attaches it after its runtime exists, ticks it every
/// update frame and disposes it at shutdown, all on the game thread.
/// </summary>
public interface IClientExtension : IDisposable
{
    void Attach(ClientExtensionContext context);

    void Tick(double deltaSeconds);
}
```

`src/AcDream.ClientExtensions/ClientExtensionContext.cs`:

```csharp
using AcDream.Runtime;

namespace AcDream.ClientExtensions;

/// <summary>What the client gives an extension when it attaches it.</summary>
public sealed class ClientExtensionContext
{
    /// <summary>The client's live runtime: session, objects, character, events.</summary>
    public required GameRuntime Runtime { get; init; }

    /// <summary>Writes a line to the client log.</summary>
    public required Action<string> Log { get; init; }

    /// <summary>
    /// Claims a chat command word (without the slash). The handler gets the
    /// text after the word. Dispose the result to give the word up.
    /// </summary>
    public required Func<string, Action<string>, IDisposable> RegisterChatCommand { get; init; }
}
```

Add a reference from the host to the contract in `src/AcDream.Rynth.Host/AcDream.Rynth.Host.csproj`, inside the existing `ItemGroup`:

```xml
    <ProjectReference Include="..\AcDream.ClientExtensions\AcDream.ClientExtensions.csproj" />
```

```bash
~/.dotnet/dotnet sln AcDream.slnx add src/AcDream.ClientExtensions/AcDream.ClientExtensions.csproj
```

- [ ] **Step 2: Implement the game view over `GameRuntime`**

Facts this relies on, checked against upstream `6db05a2d`:
- `GameRuntime.PlayerIdentity.ServerGuid` (uint) is the player's guid.
- `GameRuntime.InventoryOwner.Objects` is the `ClientObjectTable`. `Get(id)` returns `ClientObject?` with `Name` and `Properties.Int64s` (a `Dictionary<uint, long>`).
- `GameRuntime.CharacterOwner.LocalPlayer` (`LocalPlayerState`) has `HasDescription` and `Properties.Int64s`. The plugin API reads TotalExperience from it (`RuntimeAutomationSurface.cs:2170`).
- `GameRuntime.CommunicationOwner.Chat.OnSystemMessage(string text, uint chatType)` adds a chat window line.

`src/AcDream.Rynth.Host/RuntimeRynthGameView.cs`:

```csharp
using AcDream.Core.Items;
using AcDream.Runtime;

namespace AcDream.Rynth.Host;

/// <summary>The RynthCore table's game data, read from the client's runtime.</summary>
public sealed class RuntimeRynthGameView : IRynthGameView
{
    private readonly GameRuntime _runtime;

    public RuntimeRynthGameView(GameRuntime runtime) =>
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    public uint PlayerId => _runtime.PlayerIdentity.ServerGuid;

    public bool TryGetObjectName(uint objectId, out string name)
    {
        name = _runtime.InventoryOwner.Objects.Get(objectId)?.Name ?? string.Empty;
        return name.Length > 0;
    }

    // The player's own properties come from the player description first (what
    // the plugin API's TotalExperience reads), then from the player object.
    public bool TryGetInt64Property(uint objectId, uint propertyId, out long value)
    {
        if (objectId != 0 && objectId == PlayerId
            && _runtime.CharacterOwner.LocalPlayer is { HasDescription: true } player
            && player.Properties.Int64s.TryGetValue(propertyId, out value))
        {
            return true;
        }

        ClientObject? item = _runtime.InventoryOwner.Objects.Get(objectId);
        if (item is not null && item.Properties.Int64s.TryGetValue(propertyId, out value))
            return true;

        value = 0;
        return false;
    }

    public void WriteChatLine(string text, int chatType) =>
        _runtime.CommunicationOwner.Chat.OnSystemMessage(text, unchecked((uint)chatType));
}
```

- [ ] **Step 3: Implement the client extension**

`GameRuntime` is an `IRuntimeEventSource` (`Subscribe(IRuntimeEventObserver)` returns `IDisposable`). `GameRuntime.ActionOwner.Combat` is the `CombatState`, with `event Action<uint, float>? HealthChanged` and `event Action<string, uint>? KillLanded`. Task 5 makes `KillLanded` fire, passing the death message as its first argument.

`src/AcDream.Rynth.Host/RynthClientExtension.cs`:

```csharp
using AcDream.ClientExtensions;
using AcDream.Core.Combat;

namespace AcDream.Rynth.Host;

/// <summary>
/// Runs RynthCore plugins (this PoC: RynthTracker) inside the client:
/// the API table reads the runtime, runtime events become plugin callbacks,
/// and <c>/rynth tracker</c> prints the tracker's numbers.
/// </summary>
public sealed class RynthClientExtension : IClientExtension
{
    private RynthHost? _host;
    private IDisposable? _runtimeSubscription;
    private IDisposable? _command;
    private CombatState? _combat;

    public void Attach(ClientExtensionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var view = new RuntimeRynthGameView(context.Runtime);
        _host = new RynthHost(
            view,
            context.Log,
            [typeof(RynthCore.Plugin.RynthTracker.PluginExports)]);
        _runtimeSubscription = context.Runtime.Subscribe(_host.Bridge);
        _combat = context.Runtime.ActionOwner.Combat;
        _combat.HealthChanged += OnHealthChanged;
        _combat.KillLanded += OnKillLanded;
        _command = context.RegisterChatCommand("rynth", arguments => OnCommand(view, arguments));
    }

    public void Tick(double deltaSeconds) => _host?.Tick();

    public void Dispose()
    {
        _command?.Dispose();
        if (_combat is not null)
        {
            _combat.HealthChanged -= OnHealthChanged;
            _combat.KillLanded -= OnKillLanded;
        }

        _runtimeSubscription?.Dispose();
        _host?.Dispose();
        _host = null;
    }

    private void OnHealthChanged(uint targetId, float healthRatio) =>
        _host?.Bridge.OnHealthChanged(targetId, healthRatio);

    private void OnKillLanded(string deathMessage, uint victimId) =>
        _host?.Bridge.OnKillLanded(deathMessage);

    private void OnCommand(IRynthGameView view, string arguments)
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

- [ ] **Step 4: Write the game view test (it runs in Task 5)**

`tests/AcDream.App.Tests/Extensions/RuntimeRynthGameViewTests.cs`:

```csharp
using AcDream.Core.Items;
using AcDream.Rynth.Host;

namespace AcDream.App.Tests.Extensions;

public sealed class RuntimeRynthGameViewTests
{
    [Fact]
    public void ReadsThePlayerAndObjectsFromTheRuntime()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        var item = new ClientObject { ObjectId = 0x80000010, Name = "Drudge Skulker" };
        item.Properties.Int64s[1] = 42;
        runtime.InventoryOwner.Objects.AddOrUpdate(item);
        var view = new RuntimeRynthGameView(runtime);

        Assert.Equal(0x50000001u, view.PlayerId);
        Assert.True(view.TryGetObjectName(0x80000010, out string name));
        Assert.Equal("Drudge Skulker", name);
        Assert.False(view.TryGetObjectName(0x80000011, out _));
        Assert.True(view.TryGetInt64Property(0x80000010, 1, out long value));
        Assert.Equal(42, value);
        Assert.False(view.TryGetInt64Property(0x80000010, 6, out _));
    }

    [Fact]
    public void WriteChatLine_AddsASystemLine()
    {
        var runtime = GameRuntimeTestFactory.Create();
        var view = new RuntimeRynthGameView(runtime);

        view.WriteChatLine("Rynth tracker: no data yet", 0);

        Assert.Contains(
            runtime.CommunicationOwner.Chat.Snapshot(),
            entry => entry.Text == "Rynth tracker: no data yet");
    }
}
```

If `ClientObject.Properties` has no public setter path for `Int64s` (it's a get-only `PropertyBundle` whose `Int64s` is a get-only `Dictionary`), the indexer assignment above still works. If `ChatLog.Snapshot()` has a different name, use the one `GameEventWiringTests` uses (`chat.Snapshot()`).

- [ ] **Step 5: Build**

Run: `~/.dotnet/dotnet build src/AcDream.Rynth.Host -nologo`
Expected: `Build succeeded.` `0 Warning(s)` `0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.ClientExtensions src/AcDream.Rynth.Host tests/AcDream.App.Tests/Extensions AcDream.slnx
git commit -m "feat(rynth): client extension contract and the Rynth client extension

Claude-Session: https://claude.ai/code/session_013qHSfUa7LKyjkciWdcioQr"
```

---

### Task 5: Client seam and kill notifications

**Goal:** The client creates, attaches, ticks and disposes extensions. The killer notification raises `CombatState.KillLanded`. Upstream files change only as listed.

**Files:**
- Create: `src/AcDream.App/Extensions/ClientExtensionHost.cs`
- Modify: `src/AcDream.App/AcDream.App.csproj` (+2 project references)
- Modify: `src/AcDream.App/Rendering/GameWindow.cs` (+4 lines: field, attach, tick, dispose)
- Modify: `src/AcDream.Core.Net/GameEventWiring.cs:524` (+1 line)
- Modify: `tests/AcDream.Core.Net.Tests/GameEventWiringTests.cs` (+1 test)
- Modify: the `packages.*.lock.json` files that restore regenerates (App, App.Tests and any other project that references App)

**Acceptance Criteria:**
- [ ] `WireAll_KillerNotification_RaisesKillLanded` passes, and the existing `WireAll_KillerNotification_AppendsCombatLine` still passes.
- [ ] `RuntimeRynthGameViewTests` (2 tests) pass in `AcDream.App.Tests`.
- [ ] The whole solution builds: `dotnet build AcDream.slnx` reports 0 errors.
- [ ] `git diff upstream/main --stat -- src/AcDream.App/Rendering/GameWindow.cs src/AcDream.Core.Net/GameEventWiring.cs src/AcDream.App/AcDream.App.csproj` shows at most 4 + 1 + 2 inserted lines.
- [ ] Rynth host tests, `AcDream.Core.Net.Tests` and `AcDream.Runtime.Tests` pass. `AcDream.App.Tests` shows no new failures compared with `upstream/main` (some App tests fail on this Mac for environment reasons, so compare the failing test names before and after).

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Core.Net.Tests -nologo --filter "FullyQualifiedName~KillerNotification"` → 2 passed

**Steps:**

- [ ] **Step 1: Write the failing kill test**

In `tests/AcDream.Core.Net.Tests/GameEventWiringTests.cs`, add this right after `WireAll_KillerNotification_AppendsCombatLine`:

```csharp
    [Fact]
    public void WireAll_KillerNotification_RaisesKillLanded()
    {
        var (d, _, combat, _, _) = MakeAll();
        string? landed = null;
        combat.KillLanded += (deathMessage, _) => landed = deathMessage;
        byte[] payload = MakeString16L("You killed the drudge!");

        var env = GameEventEnvelope.TryParse(WrapEnvelope(GameEventType.KillerNotification, payload));
        d.Dispatch(env!.Value);

        Assert.Equal("You killed the drudge!", landed);
    }
```

Run: `~/.dotnet/dotnet test tests/AcDream.Core.Net.Tests -nologo --filter "FullyQualifiedName~KillerNotification"`
Expected: `WireAll_KillerNotification_RaisesKillLanded` fails (`landed` is null).

- [ ] **Step 2: Raise `KillLanded` from the killer notification**

In `src/AcDream.Core.Net/GameEventWiring.cs`, the KillerNotification handler (about line 520) currently reads:

```csharp
        registrar.Register(GameEventType.KillerNotification, e =>
        {
            var p = GameEvents.ParseKillerNotification(e.Payload.Span);
            // Same handler/type as VictimNotification above — 0x00 Default.
            if (p is not null) chat.OnCombatLine(p.Value.DeathMessage, logTextType: 0x00u, kind: CombatLineKind.Info);
        });
```

Add one line after the `chat.OnCombatLine` line:

```csharp
            if (p is not null) combat.OnKillerNotification(p.Value.DeathMessage, 0u);
```

`CombatState.OnKillerNotification(string victimName, uint victimGuid)` existed but had no caller. The notification carries only the death message, so the message goes in as the first argument and the guid is 0.

Run the filter again. Expected: 2 passed.

- [ ] **Step 3: Create the App-side extension host**

`_automation.PluginCommands` is `internal` in AcDream.Runtime, and App already uses it (`GameWindow.cs:902`), so App can see it. `Serilog.Log` is the App's logger.

`src/AcDream.App/Extensions/ClientExtensionHost.cs`:

```csharp
using AcDream.ClientExtensions;
using AcDream.Plugin.Abstractions;
using AcDream.Runtime;

namespace AcDream.App.Extensions;

/// <summary>
/// The client's built-in extensions (<see cref="IClientExtension"/>): created
/// with the window, attached once its runtime exists, ticked every update
/// frame and disposed at shutdown. A failing extension is logged and dropped;
/// it never stops the client.
/// </summary>
internal sealed class ClientExtensionHost : IDisposable
{
    private readonly List<IClientExtension> _extensions =
    [
        new AcDream.Rynth.Host.RynthClientExtension(),
    ];

    public void Attach(GameRuntime runtime, IPluginCommandRegistry? commands)
    {
        var context = new ClientExtensionContext
        {
            Runtime = runtime,
            Log = line => Serilog.Log.Information("{ExtensionLine}", line),
            RegisterChatCommand = (verb, handler) =>
                commands?.Register(verb, command => handler(command.Arguments))
                ?? new NoRegistration(),
        };

        foreach (IClientExtension extension in _extensions.ToArray())
            Guard(extension, "attach", () => extension.Attach(context));
    }

    public void Tick(double deltaSeconds)
    {
        foreach (IClientExtension extension in _extensions.ToArray())
            Guard(extension, "tick", () => extension.Tick(deltaSeconds));
    }

    public void Dispose()
    {
        foreach (IClientExtension extension in _extensions)
        {
            try { extension.Dispose(); }
            catch (Exception error)
            {
                Serilog.Log.Error(error, "Client extension {Extension} failed to dispose", extension.GetType().Name);
            }
        }

        _extensions.Clear();
    }

    private void Guard(IClientExtension extension, string stage, Action action)
    {
        try
        {
            action();
        }
        catch (Exception error)
        {
            Serilog.Log.Error(error, "Client extension {Extension} failed to {Stage}; dropped", extension.GetType().Name, stage);
            _extensions.Remove(extension);
            try { extension.Dispose(); }
            catch { }
        }
    }

    private sealed class NoRegistration : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
```

- [ ] **Step 4: Reference the projects from App**

In `src/AcDream.App/AcDream.App.csproj`, add these two lines to the `ItemGroup` that holds the other `ProjectReference` entries (after `AcDream.UI.Abstractions`):

```xml
    <ProjectReference Include="..\AcDream.ClientExtensions\AcDream.ClientExtensions.csproj" />
    <ProjectReference Include="..\AcDream.Rynth.Host\AcDream.Rynth.Host.csproj" />
```

- [ ] **Step 5: Wire `GameWindow` (4 lines)**

In `src/AcDream.App/Rendering/GameWindow.cs`:

1. Field, next to `private readonly GameRuntime _runtime;` (about line 282):

```csharp
    private readonly AcDream.App.Extensions.ClientExtensionHost _extensions = new();
```

2. Attach, right after `_automation?.Bind(_runtime, _runtime.CharacterOwner, _runtime.ActionOwner.SpellCast);` (about line 535):

```csharp
        _extensions.Attach(_runtime, _automation?.PluginCommands);
```

3. Tick, in `OnUpdate`, right after `_pluginTick.Feed(dt);` (about line 1606):

```csharp
        _extensions.Tick(dt);
```

4. Dispose, in `CompleteShutdown`, inside `if (!_lifetime.HasShutdownRoots)`, right after `PersistKeyBindingsAtShutdown();` (about line 1665). That block runs once:

```csharp
            _extensions.Dispose();
```

- [ ] **Step 6: Regenerate lock files, build the solution, run the tests**

```bash
cd ~/code/OpenAC/OpenAC-Rynth
for rid in "" linux-x64 osx-arm64 osx-x64 win-x64; do
  if [ -z "$rid" ]; then ~/.dotnet/dotnet restore AcDream.slnx --force-evaluate -nologo -v q
  else ~/.dotnet/dotnet restore AcDream.slnx --force-evaluate -r "$rid" -nologo -v q; fi
done
git status --porcelain | grep lock.json
~/.dotnet/dotnet build AcDream.slnx -nologo -v q 2>&1 | tail -3
~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -nologo
~/.dotnet/dotnet test tests/AcDream.Core.Net.Tests -nologo
~/.dotnet/dotnet test tests/AcDream.Runtime.Tests -nologo
~/.dotnet/dotnet test tests/AcDream.App.Tests -nologo --filter "FullyQualifiedName~RuntimeRynthGameViewTests"
```

Expected: changed lock files only for projects that now reach the new projects, a build with `0 Error(s)`, and every listed test run passing.

Then compare the full App suite against `upstream/main`. Some App tests fail on this Mac because of the environment (socket path length, peer IPC timeouts), so compare failure names rather than counts:

```bash
TMPDIR=/tmp/oac ~/.dotnet/dotnet test tests/AcDream.App.Tests -nologo 2>&1 | grep -E "^\s+Failed " | sort > /tmp/oac-app-after.txt
git stash -u && git checkout -q upstream/main
TMPDIR=/tmp/oac ~/.dotnet/dotnet test tests/AcDream.App.Tests -nologo 2>&1 | grep -E "^\s+Failed " | sort > /tmp/oac-app-before.txt
git checkout -q feat/rynth-host-poc && git stash pop
diff /tmp/oac-app-before.txt /tmp/oac-app-after.txt
```

Create `/tmp/oac` first with `mkdir -p /tmp/oac`. Expected: `diff` prints nothing, apart from `RuntimeRynthGameViewTests`, which only exists on the branch and passes.

- [ ] **Step 7: Check the upstream diff size**

```bash
git diff upstream/main --stat -- src/AcDream.App/Rendering/GameWindow.cs src/AcDream.Core.Net/GameEventWiring.cs src/AcDream.App/AcDream.App.csproj
```

Expected: `GameWindow.cs` 4 insertions, `GameEventWiring.cs` 1 insertion, `AcDream.App.csproj` 2 insertions, 0 deletions.

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.App src/AcDream.Core.Net/GameEventWiring.cs tests/AcDream.Core.Net.Tests/GameEventWiringTests.cs
git add $(git status --porcelain | awk '/lock.json$/ {print $2}')
git commit -m "feat(rynth): client extension seam; killer notifications raise KillLanded

Claude-Session: https://claude.ai/code/session_013qHSfUa7LKyjkciWdcioQr"
```

---

### Task 6: Live check on the local ACE server

**Goal:** In the real client on macOS, against the local ACE server 10.10.20.20, RynthTracker counts kills and XP, and `/rynth tracker` shows them.

**Files:**
- None (no code). Record the result in the PR description or the session report.

**Acceptance Criteria:**
- [ ] The user has said they are available before the client is launched.
- [ ] The client log contains `[Rynth] [RynthTracker] Plugin initialized.` and `[Rynth] RynthCore.Plugin.RynthTracker loaded.`
- [ ] After login, the log contains `[RynthTracker] Session started.`
- [ ] After the user kills 3 monsters, `/rynth tracker` prints `3 kills`, and its XP total equals the XP the character gained (check `/xp` before and after, or the character sheet).
- [ ] No exception from `[Rynth]` appears in the log.

**Verify:** the user types `/rynth tracker` in game → a line starting `Rynth tracker: 3 kills`

**Steps:**

- [ ] **Step 1: Ask the user whether they are available.** Don't launch anything before they say yes.

- [ ] **Step 2: Build and launch the dev client against the local server**

Read `~/OpenAC-dev/dev-client.env` and the repo's `docs/building-and-running.md` for the current launch command. Point it at this repo (`~/code/OpenAC/OpenAC-Rynth`) and the local ACE server 10.10.20.20. Never use Dreamweave. Capture the client log.

- [ ] **Step 3: The user logs in, notes their XP, kills 3 monsters, notes their XP again, and types `/rynth tracker`.**

- [ ] **Step 4: Check the log and the chat line against the acceptance criteria, and report the result with the exact lines.**
