# RynthAi Outdoor Detours Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** RynthAi's outdoor stuck/off-track detours plan over RynthNav's baked DotRecast navmesh in OpenAC, as they do under RynthCore.

**Architecture:** The host gains RynthCore's `GetPluginInterfaceFn` (plugin lookup by reported name, then "contains", then the plugin's `RynthPluginQueryInterface`). RynthSuite's RynthNav compiles unchanged as a new wrapper plugin and is loaded beside RynthAi, reading its tiles from `RYNTHNAV_NAVDATA`, which the host points at `<RYNTHCORE_DIR>/NavData`. A new maintainer tool, `tools/RynthNavData`, compiles RynthCore's own launcher NavData code unchanged to pin one archive of theirs (`external/rynth-navdata.json`, our own SHA-256) and install exactly that archive.

**Tech Stack:** .NET 10, C# unsafe function pointers / `[UnmanagedCallersOnly]`, DotRecast 2026.1.3 (Core + Detour), xunit (Rynth.Host.Tests) and xunit.v3 (tool tests), `HttpListener` for offline download tests.

**Spec:** `docs/superpowers/specs/2026-10-19-rynth-nav-detours-design.md`

## Global Constraints

- **Their code, unchanged.** Nothing under `external/` is edited. RynthNav and RynthCore's updater files compile from the submodules as they are.
- `RynthApiTable.ApiVersion` stays **86**. No plugin contract bump (stays `0.1.25-fork.9`).
- `GetPluginInterfaceFn` signature: `delegate* unmanaged[Cdecl]<IntPtr pluginNameAnsi, IntPtr interfaceNameAnsi, uint version, IntPtr>`; returns 0 on any failure. It must never throw across the boundary.
- Plugin lookup order (RynthCore `ResolvePluginExport`): active plugins only; exact `RynthPluginName` (case-insensitive) first, then a case-insensitive "contains" on the reported name or the host's namespace name.
- Navdata folder: `RYNTHNAV_NAVDATA`, set by the host off Windows to `Path.Combine(<RYNTHCORE_DIR>, "NavData")` only when unset. On Windows nothing is set (their lookup is theirs, as `RynthInstallFolders` already does).
- Pin file: `external/rynth-navdata.json`, fields `url, size, sha256, files, bytes, id, rynthSuite, pinned` (camelCase); `id` = the archive's lowercase SHA-256.
- Pinned URL: `https://aelrynth.com/downloads/rynth/releases/2026101501/RynthNav-NavData.zip` (1,572,337,084 bytes on 2026-10-19).
- Tests never touch the network or the real user folders (the existing `RynthConfigSandbox` / `AssemblyConfigSandbox` rules). The real-tile test reads copies of real tiles placed in the sandbox.
- Lock files: refresh with `dotnet restore AcDream.slnx --force-evaluate`, then revert any Launcher lock churn that is only a RID-section swap. Never run `tools/update-package-locks.ps1`.
- Commits end with `Claude-Session: https://claude.ai/code/session_011FtdjFB5dEVWdBQnZmstpz`.
- Branch: `feat/rynth-nav-detours` (exists, spec commit `0c4b3b30`).

**User decisions (already made):**
- Scope A: detours only; RynthNav loaded whole, only its path service built for and tested. `/rn`, its panel, goto/travel = session B.
- The navdata follows OUR release (pinned with the submodule), not RynthCore's updater.
- Use their URL for now; ask the author about mirroring later.
- Their feed has no signed navdata entry, so we pin our own SHA-256.
- Downloading the 1.57 GB archive during implementation is approved.
- Launcher delivery of the navdata is a later sub-project (A2).

---

## File Structure

| File | Responsibility |
|---|---|
| `src/AcDream.Rynth.Host/RynthPluginLookup.cs` (new) | RynthCore's plugin-by-name order, shared by the slot and `RynthChatHookup` |
| `src/AcDream.Rynth.Host/RynthApiTable.Interfaces.cs` (new) | The `GetPluginInterfaceFn` slot |
| `src/AcDream.Rynth.Host/RynthPlugin.cs` | `ReportedName`, `HasExport`, `QueryInterface` |
| `src/AcDream.Rynth.Host/RynthHost.cs` | Hands the plugin list to the table; reads `RynthPluginName`; `Api` for tests |
| `src/AcDream.Rynth.Host/RynthApiTable.cs` | `Plugins` property; `BindInterfaceSlots()` call |
| `src/AcDream.Rynth.Host/RynthChatHookup.cs` | `Resolve` uses `RynthPluginLookup` |
| `src/AcDream.Rynth.Plugin.RynthNav/AcDream.Rynth.Plugin.RynthNav.csproj` (new) | RynthNav compiled unchanged |
| `src/AcDream.Rynth.Host/RynthInstallFolders.cs` | Also sets `RYNTHNAV_NAVDATA` |
| `src/AcDream.Rynth.Host/RynthNavDataHint.cs` (new) | The "no tiles" line |
| `src/AcDream.Rynth.Host/RynthClientExtension.cs` | Loads RynthNav; logs and shows the hint |
| `tools/RynthNavData/*` (new) | `pin`, `install`, `check-feed` over RynthCore's updater code |
| `external/rynth-navdata.json` (new) | The pin |
| `tests/AcDream.Rynth.Host.Tests/RynthPluginInterfaceTests.cs` (new) | Slot tests with fake plugins |
| `tests/AcDream.Rynth.Host.Tests/RynthNavPluginTests.cs` (new) | Wrapper, env var, hint, cross-plugin tables, real tiles |
| `tests/AcDream.Rynth.Host.Tests/AssemblyConfigSandbox.cs` | Sandbox NavData folder (+ copied real tiles) |
| `tests/AcDream.Tools.RynthNavData.Tests/*` (new) | Tool tests over a local `HttpListener` |

---

### Task 1: `GetPluginInterface` slot

**Goal:** Plugins can fetch each other's typed interface tables through the host, as under RynthCore.

**Files:**
- Create: `src/AcDream.Rynth.Host/RynthPluginLookup.cs`
- Create: `src/AcDream.Rynth.Host/RynthApiTable.Interfaces.cs`
- Modify: `src/AcDream.Rynth.Host/RynthPlugin.cs`, `src/AcDream.Rynth.Host/RynthHost.cs`, `src/AcDream.Rynth.Host/RynthApiTable.cs`, `src/AcDream.Rynth.Host/RynthChatHookup.cs:157-167`
- Modify: `tests/AcDream.Rynth.Host.Tests/RynthApiTableTests.cs:315` (drop the `GetPluginInterfaceFn` line from `DeferredSlotsStayUnset`)
- Test: `tests/AcDream.Rynth.Host.Tests/RynthPluginInterfaceTests.cs`

**Acceptance Criteria:**
- [ ] `Native->GetPluginInterfaceFn` is non-zero on every table.
- [ ] Exact reported name wins over a "contains" match; "contains" works on the reported name and on the namespace name.
- [ ] A plugin whose Init failed, or that was shut down, is never asked.
- [ ] Returns 0 for null/empty names, an unknown plugin, a plugin without `RynthPluginQueryInterface`, or a plugin that answers 0.
- [ ] The interface name and version reach the plugin unchanged; its pointer comes back unchanged.
- [ ] `RynthChatHookup.Resolve` keeps working (existing chat/dungeon-map/inventory tests pass).

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -c Release --filter "FullyQualifiedName~RynthPluginInterfaceTests|FullyQualifiedName~RynthApiTableTests"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** — `tests/AcDream.Rynth.Host.Tests/RynthPluginInterfaceTests.cs`:

```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AcDream.Rynth.Host;
using RynthCore.PluginSdk;

namespace AcDream.Rynth.Host.Tests;

public sealed unsafe class RynthPluginInterfaceTests
{
    private readonly List<string> _log = new();

    private static IntPtr Call(RynthHost host, string? plugin, string? iface, uint version)
    {
        IntPtr name = plugin is null ? IntPtr.Zero : Marshal.StringToHGlobalAnsi(plugin);
        IntPtr ifaceName = iface is null ? IntPtr.Zero : Marshal.StringToHGlobalAnsi(iface);
        try
        {
            return ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, IntPtr>)host.Api->GetPluginInterfaceFn)(name, ifaceName, version);
        }
        finally
        {
            Marshal.FreeHGlobal(name);
            Marshal.FreeHGlobal(ifaceName);
        }
    }

    [Fact]
    public void TheSlotIsSet()
    {
        using var host = new RynthHost(new FakeGameView(), _log.Add, [typeof(Provider)]);
        Assert.NotEqual(IntPtr.Zero, host.Api->GetPluginInterfaceFn);
    }

    [Fact]
    public void AnExactReportedNameIsAskedWithTheInterfaceAndVersion()
    {
        using var host = new RynthHost(new FakeGameView(), _log.Add, [typeof(Provider)]);
        Assert.Equal((IntPtr)0x1111, Call(host, "provider", "Provider.Table", 3));
        Assert.Equal("Provider.Table", Provider.LastInterface);
        Assert.Equal(3u, Provider.LastVersion);
    }

    [Fact]
    public void AnExactNameWinsOverAContainsMatch()
    {
        // "ProviderPlus" contains "Provider" and loads first; the exact one still answers.
        using var host = new RynthHost(new FakeGameView(), _log.Add, [typeof(ProviderPlus), typeof(Provider)]);
        Assert.Equal((IntPtr)0x1111, Call(host, "Provider", "Provider.Table", 1));
    }

    [Fact]
    public void AContainsMatchFindsAPluginByPartOfItsName()
    {
        using var host = new RynthHost(new FakeGameView(), _log.Add, [typeof(ProviderPlus)]);
        Assert.Equal((IntPtr)0x2222, Call(host, "plus", "Any", 1));
    }

    [Fact]
    public void UnknownMissingOrSilentPluginsGiveZero()
    {
        using var host = new RynthHost(new FakeGameView(), _log.Add, [typeof(Provider), typeof(NoQuery), typeof(Silent)]);
        Assert.Equal(IntPtr.Zero, Call(host, "Nobody", "Provider.Table", 1));
        Assert.Equal(IntPtr.Zero, Call(host, "NoQuery", "Provider.Table", 1));
        Assert.Equal(IntPtr.Zero, Call(host, "Silent", "Provider.Table", 1));
        Assert.Equal(IntPtr.Zero, Call(host, null, "Provider.Table", 1));
        Assert.Equal(IntPtr.Zero, Call(host, "", "Provider.Table", 1));
        Assert.Equal(IntPtr.Zero, Call(host, "Provider", null, 1));
    }

    [Fact]
    public void AFailedOrShutDownPluginIsNotAsked()
    {
        using var host = new RynthHost(new FakeGameView(), _log.Add, [typeof(FailsInit), typeof(Provider)]);
        Assert.Equal(IntPtr.Zero, Call(host, "FailsInit", "Any", 1));

        host.Plugins.Single(p => p.ReportedName == "Provider").Shutdown();
        Assert.Equal(IntPtr.Zero, Call(host, "Provider", "Provider.Table", 1));
    }

    private static class Provider
    {
        private static readonly IntPtr NamePointer = Marshal.StringToHGlobalAnsi("Provider");
        internal static string? LastInterface;
        internal static uint LastVersion;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginInit", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static int Init(RynthCoreApiNative* api) => 0;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginName", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr Name() => NamePointer;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginQueryInterface", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr Query(IntPtr iface, uint version)
        {
            LastInterface = Marshal.PtrToStringAnsi(iface);
            LastVersion = version;
            return 0x1111;
        }
    }

    private static class ProviderPlus
    {
        private static readonly IntPtr NamePointer = Marshal.StringToHGlobalAnsi("ProviderPlus");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginInit", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static int Init(RynthCoreApiNative* api) => 0;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginName", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr Name() => NamePointer;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginQueryInterface", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr Query(IntPtr iface, uint version) => 0x2222;
    }

    private static class NoQuery
    {
        private static readonly IntPtr NamePointer = Marshal.StringToHGlobalAnsi("NoQuery");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginInit", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static int Init(RynthCoreApiNative* api) => 0;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginName", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr Name() => NamePointer;
    }

    private static class Silent
    {
        private static readonly IntPtr NamePointer = Marshal.StringToHGlobalAnsi("Silent");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginInit", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static int Init(RynthCoreApiNative* api) => 0;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginName", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr Name() => NamePointer;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginQueryInterface", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr Query(IntPtr iface, uint version) => IntPtr.Zero;
    }

    private static class FailsInit
    {
        private static readonly IntPtr NamePointer = Marshal.StringToHGlobalAnsi("FailsInit");

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginInit", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static int Init(RynthCoreApiNative* api) => -1;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginName", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr Name() => NamePointer;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginQueryInterface", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr Query(IntPtr iface, uint version) => 0x3333;
    }
}
```

All fake types share the namespace `AcDream.Rynth.Host.Tests`, so their host names are identical: only `ReportedName` tells them apart, which is exactly what the exact-name tests check.

- [ ] **Step 2: Run to see it fail**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -c Release --filter FullyQualifiedName~RynthPluginInterfaceTests`
Expected: compile errors (`RynthHost.Api`, `RynthPlugin.ReportedName` do not exist).

- [ ] **Step 3: `RynthPlugin` additions** — in `src/AcDream.Rynth.Host/RynthPlugin.cs`, after `Name`:

```csharp
    /// <summary>What the plugin's RynthPluginName export reports ("RynthNav"); empty when it has none.</summary>
    public string ReportedName { get; internal set; } = string.Empty;
```

After `TryGetExport`:

```csharp
    /// <summary>True while active when the plugin has <paramref name="entryPoint"/>.</summary>
    public bool HasExport(string entryPoint) => IsActive && _exports.TryGet(entryPoint, out _);

    /// <summary>
    /// The plugin's RynthPluginQueryInterface(iface, version): a typed table it owns, or 0.
    /// RynthCore calls it outside any dispatch scope, and so does this.
    /// </summary>
    public IntPtr QueryInterface(IntPtr interfaceNameAnsi, uint version)
    {
        if (!IsActive || !_exports.TryGet("RynthPluginQueryInterface", out IntPtr export))
            return IntPtr.Zero;
        return ((delegate* unmanaged[Cdecl]<IntPtr, uint, IntPtr>)export)(interfaceNameAnsi, version);
    }
```

- [ ] **Step 4: `RynthPluginLookup`** — `src/AcDream.Rynth.Host/RynthPluginLookup.cs`:

```csharp
namespace AcDream.Rynth.Host;

/// <summary>
/// RynthCore's plugin-by-name order (PluginManager.ResolvePluginExport): among active
/// plugins, an exact reported name first, then a name that contains the asked one. Here a
/// plugin's host name is its exports namespace ("RynthCore.Plugin.RynthNav"), so the
/// "contains" step also looks at that.
/// </summary>
internal static class RynthPluginLookup
{
    internal static IEnumerable<RynthPlugin> Candidates(IReadOnlyList<RynthPlugin> plugins, string name)
    {
        foreach (RynthPlugin plugin in plugins)
        {
            if (plugin.IsActive && plugin.ReportedName.Equals(name, StringComparison.OrdinalIgnoreCase))
                yield return plugin;
        }

        foreach (RynthPlugin plugin in plugins)
        {
            if (plugin.IsActive
                && !plugin.ReportedName.Equals(name, StringComparison.OrdinalIgnoreCase)
                && (plugin.ReportedName.Contains(name, StringComparison.OrdinalIgnoreCase)
                    || plugin.Name.Contains(name, StringComparison.OrdinalIgnoreCase)))
                yield return plugin;
        }
    }
}
```

- [ ] **Step 5: The table knows the plugins** — in `src/AcDream.Rynth.Host/RynthApiTable.cs` add after the `Vendor` property:

```csharp
    /// <summary>The host's loaded plugins, for GetPluginInterface. Set by the host before the first Init.</summary>
    internal IReadOnlyList<RynthPlugin> Plugins { get; set; } = [];
```

and add `BindInterfaceSlots();` after `BindScreenSlots();` in the constructor.

In `src/AcDream.Rynth.Host/RynthHost.cs`, right after `_table = new RynthApiTable(...)`:

```csharp
        // A plugin may ask for another's interface from its own Init (RynthCore's order: the
        // ones loaded before it are there).
        _table.Plugins = _plugins;
```

In `Load`, after the `VersionString` line:

```csharp
        plugin.ReportedName = plugin.CallStringExport("RynthPluginName") ?? string.Empty;
```

And add, next to `Holds`:

```csharp
    /// <summary>The engine API table every plugin got (tests call its slots directly).</summary>
    internal RynthCoreApiNative* Api => _table.Native;
```

(`RynthCoreApiNative` is in `RynthCore.PluginSdk`; add `using RynthCore.PluginSdk;` to `RynthHost.cs`.)

- [ ] **Step 6: The slot** — `src/AcDream.Rynth.Host/RynthApiTable.Interfaces.cs`:

```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AcDream.Rynth.Host;

// GetPluginInterface (API 68): one plugin's typed table for another, as RynthCore's
// PluginManager.GetPluginInterfaceAction does it. RynthAi's outdoor detours ask RynthNav for
// "RynthNav.Path"; RynthLua asks RynthAi for "RynthAi.Script".
public sealed unsafe partial class RynthApiTable
{
    private readonly HashSet<string> _interfaceFailures = new(StringComparer.Ordinal);

    private void BindInterfaceSlots() =>
        Native->GetPluginInterfaceFn = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, IntPtr>)&GetPluginInterface;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static IntPtr GetPluginInterface(IntPtr pluginNameAnsi, IntPtr interfaceNameAnsi, uint version)
    {
        RynthApiTable? table = _current;
        if (table is null || pluginNameAnsi == IntPtr.Zero || interfaceNameAnsi == IntPtr.Zero)
            return IntPtr.Zero;
        string name = string.Empty;
        string iface = string.Empty;
        try
        {
            name = Marshal.PtrToStringAnsi(pluginNameAnsi) ?? string.Empty;
            iface = Marshal.PtrToStringAnsi(interfaceNameAnsi) ?? string.Empty;
            if (name.Length == 0 || iface.Length == 0)
                return IntPtr.Zero;
            foreach (RynthPlugin plugin in RynthPluginLookup.Candidates(table.Plugins, name))
            {
                if (plugin.HasExport("RynthPluginQueryInterface"))
                    return plugin.QueryInterface(interfaceNameAnsi, version);
            }

            return IntPtr.Zero;
        }
        catch (Exception error)
        {
            // Asked every tick by some callers: say it once per plugin/interface pair.
            if (table._interfaceFailures.Add($"{name}/{iface}"))
                table.Fail($"{nameof(GetPluginInterface)}({name}, {iface})", error);
            return IntPtr.Zero;
        }
    }
}
```

RynthCore stops at the first matched plugin even when it lacks the export; here the first candidate *with* the export answers. That only differs when two plugins match and the first has no query export, which no RynthSuite plugin set does.

- [ ] **Step 7: `RynthChatHookup.Resolve` on the shared order** — replace the body at `src/AcDream.Rynth.Host/RynthChatHookup.cs:157-167`:

```csharp
    private IntPtr Resolve(string pluginNamePart, string exportName)
    {
        foreach (RynthPlugin plugin in RynthPluginLookup.Candidates(_host.Plugins, pluginNamePart))
        {
            if (plugin.TryGetExport(exportName, out IntPtr pointer))
                return pointer;
        }

        return IntPtr.Zero;
    }
```

- [ ] **Step 8: `DeferredSlotsStayUnset`** — delete the line `Assert.Equal(IntPtr.Zero, table.Native->GetPluginInterfaceFn);` (`RynthApiTableTests.cs:315`).

- [ ] **Step 9: Run the tests**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -c Release`
Expected: all pass (previous count 381 + 6 new).

- [ ] **Step 10: Commit**

```bash
git add src/AcDream.Rynth.Host tests/AcDream.Rynth.Host.Tests
git commit -m "feat(rynth): GetPluginInterface: one plugin's typed table for another, by RynthCore's name order

Claude-Session: https://claude.ai/code/session_011FtdjFB5dEVWdBQnZmstpz"
```

```json:metadata
{"files": ["src/AcDream.Rynth.Host/RynthPluginLookup.cs", "src/AcDream.Rynth.Host/RynthApiTable.Interfaces.cs", "src/AcDream.Rynth.Host/RynthPlugin.cs", "src/AcDream.Rynth.Host/RynthHost.cs", "src/AcDream.Rynth.Host/RynthApiTable.cs", "src/AcDream.Rynth.Host/RynthChatHookup.cs", "tests/AcDream.Rynth.Host.Tests/RynthApiTableTests.cs", "tests/AcDream.Rynth.Host.Tests/RynthPluginInterfaceTests.cs"], "verifyCommand": "~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -c Release", "acceptanceCriteria": ["GetPluginInterfaceFn non-zero", "exact reported name wins over contains; contains works on reported and namespace names", "failed or shut-down plugins never asked", "0 for null/empty names, unknown plugin, missing export, a 0 answer", "interface name, version and pointer passed through unchanged", "RynthChatHookup.Resolve tests still pass"], "modelTier": "standard"}
```

---

### Task 2: RynthNav wrapper, navdata folder and hint

**Goal:** RynthNav loads beside RynthAi, reads its tiles from `<RYNTHCORE_DIR>/NavData`, and RynthAi and RynthLua get real interface tables through Task 1's slot.

**Files:**
- Create: `src/AcDream.Rynth.Plugin.RynthNav/AcDream.Rynth.Plugin.RynthNav.csproj`
- Create: `src/AcDream.Rynth.Host/RynthNavDataHint.cs`
- Modify: `Directory.Packages.props`, `AcDream.slnx`, `src/AcDream.Rynth.Host/AcDream.Rynth.Host.csproj`, `src/AcDream.Rynth.Host/RynthInstallFolders.cs`, `src/AcDream.Rynth.Host/RynthClientExtension.cs`
- Modify: `tests/AcDream.Rynth.Host.Tests/AssemblyConfigSandbox.cs`
- Modify: lock files produced by the restore (`**/packages.*.lock.json`)
- Test: `tests/AcDream.Rynth.Host.Tests/RynthNavPluginTests.cs`

**Acceptance Criteria:**
- [ ] `AcDream.Rynth.Plugin.RynthNav` builds from the submodule with no edits under `external/`.
- [ ] A host with RynthNav + RynthAi + RynthLua loads all three; 30 ticks with no `Exception` log lines; RynthNav's `ReportedName` is `RynthNav`.
- [ ] RynthNav logs `Tiles: <sandbox NavData> (RYNTHNAV_NAVDATA)`.
- [ ] Through the slot: `("RynthNav", "RynthNav.Path", 1)` gives a table with `Version == 1`, `StructSize == 16`, non-zero `FindPath`; `("RynthAi", "RynthAi.Script", 1)` is non-zero.
- [ ] `RynthInstallFolders.Apply` sets `RYNTHNAV_NAVDATA` to `<RYNTHCORE_DIR>/NavData` off Windows when unset, keeps an existing value, sets nothing on Windows.
- [ ] `RynthNavDataHint.For` returns null for null/blank or a folder with a `nav_*.tile`; a line naming the folder and the install command otherwise.
- [ ] `RynthClientExtension` loads RynthNav before RynthAi, logs the hint at start and writes it to chat once after the first login.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -c Release` → all pass

**Steps:**

- [ ] **Step 1: Packages** — in `Directory.Packages.props`, beside `MoonSharp`:

```xml
    <PackageVersion Include="DotRecast.Core" Version="2026.1.3" />
    <PackageVersion Include="DotRecast.Detour" Version="2026.1.3" />
```

- [ ] **Step 2: The wrapper** — `src/AcDream.Rynth.Plugin.RynthNav/AcDream.Rynth.Plugin.RynthNav.csproj` (file list = their csproj's `Compile Include` items):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <!-- RynthSuite's RynthNav (goto, the Atlas, and the "RynthNav.Path" navmesh service that
       RynthAi's outdoor detours use), compiled unchanged from the submodule as plain net10.0,
       with the files its own csproj links. DotRecast is pure C#. Its tiles come from
       RYNTHNAV_NAVDATA, which the host sets (RynthInstallFolders). Their code, their
       warnings: warnings-as-errors and analyzers are off for this project only. -->
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
    <NavPlugin>$(RynthSuite)Plugins\RynthCore.Plugin.RynthNav\</NavPlugin>
    <SuiteShared>$(RynthSuite)Plugins\Shared\</SuiteShared>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="$(NavPlugin)**\*.cs" Exclude="$(NavPlugin)obj\**;$(NavPlugin)bin\**" />
    <Compile Include="$(RynthSuite)Tools\RynthNav.PortalGraph\PortalRoute.cs" Link="Shared\PortalRoute.cs" />
    <Compile Include="$(SuiteShared)RynthNavPathApi.cs" Link="Shared\RynthNavPathApi.cs" />
    <Compile Include="$(SuiteShared)UseAudit.cs" Link="Shared\UseAudit.cs" />
    <Compile Include="$(SuiteShared)Nav3DText.cs" Link="Shared\Nav3DText.cs" />
    <Compile Include="$(SuiteShared)RynthInstallPaths.cs" Link="Shared\RynthInstallPaths.cs" />
    <Compile Include="$(RynthSuite)Plugins\RynthCore.Plugin.RynthAi\LegacyUi\NavRouteParser.cs" Link="Shared\NavRouteParser.cs" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="DotRecast.Core" />
    <PackageReference Include="DotRecast.Detour" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\AcDream.Rynth.Sdk\AcDream.Rynth.Sdk.csproj" />
  </ItemGroup>
</Project>
```

`RynthInstallPaths`' advapi32 call stays, as in the RynthLua wrapper (our `RYNTHCORE_DIR`/`RYNTHSUITE_DIR` are read first). If the build reports a Windows-only API in their RynthNav sources that the explore pass missed, stop and report it; do not edit their files.

Add the project to `AcDream.slnx` beside the other `src/AcDream.Rynth.Plugin.*` entries, and to `src/AcDream.Rynth.Host/AcDream.Rynth.Host.csproj`:

```xml
    <ProjectReference Include="..\AcDream.Rynth.Plugin.RynthNav\AcDream.Rynth.Plugin.RynthNav.csproj" />
```

- [ ] **Step 3: Restore and lock files**

Run: `~/.dotnet/dotnet restore AcDream.slnx --force-evaluate`
Then `git status --short '**/packages.*.lock.json'`; for each `src/AcDream.Launcher*` lock whose diff is only RID sections swapping, `git checkout -- <file>`. Keep the new `src/AcDream.Rynth.Plugin.RynthNav/packages.neutral.lock.json` and the DotRecast additions in dependent locks.
Run: `~/.dotnet/dotnet build src/AcDream.Rynth.Plugin.RynthNav -c Release` → `Build succeeded`.

- [ ] **Step 4: Navdata folder in `RynthInstallFolders`** — in `src/AcDream.Rynth.Host/RynthInstallFolders.cs` add the constant and the last step of `Apply(...)`, and extend the class summary with one sentence ("RynthNav reads its tiles from RYNTHNAV_NAVDATA, not RynthInstallPaths, so off Windows that is pointed at <RYNTHCORE_DIR>/NavData too, where the Atlas already goes."):

```csharp
    internal const string NavDataVariable = "RYNTHNAV_NAVDATA";
```

```csharp
        if (string.IsNullOrWhiteSpace(get(NavDataVariable)) && get(CoreVariable) is { Length: > 0 } core)
            set(NavDataVariable, Path.Combine(core, "NavData"));
```

(after the two existing `set` calls, so a `RYNTHCORE_DIR` set either way is used).

- [ ] **Step 5: The hint** — `src/AcDream.Rynth.Host/RynthNavDataHint.cs`:

```csharp
namespace AcDream.Rynth.Host;

/// <summary>
/// RynthNav's navmesh tiles are not shipped with OpenAC: tools/RynthNavData installs the
/// pinned set. Without them RynthAi's outdoor detours answer "no RynthNav navmesh here" and
/// it keeps its jump ladder; this says once where the tiles go and how to get them.
/// </summary>
internal static class RynthNavDataHint
{
    internal const string InstallCommand = "dotnet run --project tools/RynthNavData -c Release -- install";

    /// <summary>The line to show, or null when <paramref name="navDataDirectory"/> is unknown or has tiles.</summary>
    internal static string? For(string? navDataDirectory)
    {
        if (string.IsNullOrWhiteSpace(navDataDirectory))
            return null;
        try
        {
            if (Directory.Exists(navDataDirectory) && Directory.EnumerateFiles(navDataDirectory, "nav_*.tile").Any())
                return null;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return $"[Rynth] RynthAi's outdoor detours need RynthNav's navmesh tiles in {navDataDirectory}. "
            + $"Install them from an OpenAC checkout with: {InstallCommand}";
    }
}
```

- [ ] **Step 6: Wire it into `RynthClientExtension`**

In `src/AcDream.Rynth.Host/RynthClientExtension.cs`:

- Add fields next to the other private fields:

```csharp
    private IRynthGameView? _view;
    private string? _navDataHint;
```

- Right after `RynthInstallFolders.Apply();`:

```csharp
        _navDataHint = RynthNavDataHint.For(Environment.GetEnvironmentVariable(RynthInstallFolders.NavDataVariable));
        if (_navDataHint is { } navHint)
            context.Log(navHint);
```

- After `var view = new RuntimeRynthGameView(...)`: `_view = view;`
- In the plugin list, before `typeof(RynthCore.Plugin.RynthAi.PluginExports),` add `typeof(RynthCore.Plugin.RynthNav.PluginExports),`.
- In `Tick`, after `_host?.Tick();`:

```csharp
        // The chat bar is not there at the character list: the hint waits for the first login.
        if (_navDataHint is { } hint && RynthCore.Engine.Compatibility.LoginLifecycleHooks.HasObservedLoginComplete)
        {
            _navDataHint = null;
            Guard("RynthNav", () => _view?.WriteChatLine(hint, 0));
        }
```

- [ ] **Step 7: Test sandbox NavData folder** — in `tests/AcDream.Rynth.Host.Tests/AssemblyConfigSandbox.cs`, make `Install()` read:

```csharp
    [ModuleInitializer]
    internal static void Install()
    {
        _ = TestDats.Directory;   // capture the real profile before the sandbox moves HOME
        TestNavData.CaptureRealDirectory();   // likewise the real RynthCore folder
        var sandbox = new RynthConfigSandbox("rynth-host-tests-");
        TestNavData.Prepare(sandbox.Root);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => sandbox.Dispose();
    }
```

and add the class below. Until Task 4 installs real tiles, nothing is copied and `HasTiles` is false.

```csharp
/// <summary>
/// RynthNav resolves its tile folder once per process (a static NavDataConfig), so every test
/// shares one: a sandbox folder, with copies of the real installed tiles around Holtburg when
/// they are there (Task 4's real-tile tests skip otherwise). Never the real folder: RynthNav
/// writes _player.txt into it.
/// </summary>
internal static class TestNavData
{
    // The installed tiles: ACDREAM_NAVDATA_DIR, else the REAL <ApplicationData>/RynthCore/NavData.
    private static string _realDirectory = string.Empty;

    /// <summary>Call before RynthConfigSandbox repoints ApplicationData at a temp folder.</summary>
    internal static void CaptureRealDirectory() =>
        _realDirectory = Environment.GetEnvironmentVariable("ACDREAM_NAVDATA_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RynthCore", "NavData");

    /// <summary>Holtburg's landblock; the copied tiles are the 5x5 block around it.</summary>
    internal const uint HoltburgLandblock = 0xA9B4;

    internal static string Directory { get; private set; } = string.Empty;

    /// <summary>True when the Holtburg tile was copied in.</summary>
    internal static bool HasTiles { get; private set; }

    internal static void Prepare(string sandboxRoot)
    {
        Directory = Path.Combine(sandboxRoot, "NavData");
        System.IO.Directory.CreateDirectory(Directory);
        int cx = (int)(HoltburgLandblock >> 8), cy = (int)(HoltburgLandblock & 0xFF);
        for (int x = cx - 2; x <= cx + 2; x++)
            for (int y = cy - 2; y <= cy + 2; y++)
            {
                string name = $"nav_{(x << 8) | y:X4}.tile";
                string source = Path.Combine(_realDirectory, name);
                if (File.Exists(source))
                    File.Copy(source, Path.Combine(Directory, name), overwrite: true);
            }

        HasTiles = File.Exists(Path.Combine(Directory, $"nav_{HoltburgLandblock:X4}.tile"));
        Environment.SetEnvironmentVariable("RYNTHNAV_NAVDATA", Directory);
    }
}
```

- [ ] **Step 8: Wrapper tests** — `tests/AcDream.Rynth.Host.Tests/RynthNavPluginTests.cs`:

```csharp
using System.Runtime.InteropServices;
using AcDream.Rynth.Host;
using AcDream.TestSupport;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.UI.Data;

namespace AcDream.Rynth.Host.Tests;

public sealed unsafe class RynthNavPluginTests : IDisposable
{
    private readonly List<string> _log = new();
    private readonly RynthConfigSandbox _sandbox = new("rynth-nav-");
    private readonly FakeGameView _view = new() { PlayerId = 0x50000002u };
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public RynthNavPluginTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = _log.Add;
        UiDataHub.Clear();
    }

    public void Dispose()
    {
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = null;
        LoginLifecycleHooks.HasObservedLoginComplete = false;
        _sandbox.Dispose();
    }

    // RynthAi's own types are in two assemblies (RynthAi and RynthNav both compile
    // Shared/RynthNavPathApi.cs), so the test reads the table through its own mirror.
    [StructLayout(LayoutKind.Sequential)]
    private struct PathTable
    {
        public uint Version;
        public uint StructSize;
        public IntPtr FindPath;
    }

    private RynthHost CreateHost() => new(
        _view,
        _log.Add,
        [
            typeof(RynthCore.Plugin.RynthNav.PluginExports),
            typeof(RynthCore.Plugin.RynthAi.PluginExports),
            typeof(RynthCore.Plugin.RynthLua.PluginExports),
        ]);

    internal static IntPtr Interface(RynthHost host, string plugin, string iface, uint version)
    {
        IntPtr name = Marshal.StringToHGlobalAnsi(plugin);
        IntPtr ifaceName = Marshal.StringToHGlobalAnsi(iface);
        try
        {
            return ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, IntPtr>)host.Api->GetPluginInterfaceFn)(name, ifaceName, version);
        }
        finally
        {
            Marshal.FreeHGlobal(name);
            Marshal.FreeHGlobal(ifaceName);
        }
    }

    [Fact]
    public void RynthNavLoadsAndTicksBesideRynthAiAndRynthLua()
    {
        using RynthHost host = CreateHost();
        for (int i = 0; i < 30; i++)
            host.Tick();

        RynthPlugin nav = Assert.Single(host.Plugins, p => p.ReportedName == "RynthNav");
        Assert.True(nav.IsActive);
        Assert.Equal(3, host.Plugins.Count(p => p.IsActive));
        Assert.DoesNotContain(_log, line => line.Contains("Exception", StringComparison.Ordinal));
    }

    [Fact]
    public void RynthNavReadsItsTilesFromTheHostsFolder()
    {
        using RynthHost host = CreateHost();
        Assert.Contains(_log, line => line.Contains($"Tiles: {TestNavData.Directory} (RYNTHNAV_NAVDATA)", StringComparison.Ordinal));
    }

    [Fact]
    public void RynthAiCanFetchRynthNavsPathTable()
    {
        using RynthHost host = CreateHost();
        IntPtr table = Interface(host, "RynthNav", "RynthNav.Path", 1);

        Assert.NotEqual(IntPtr.Zero, table);
        PathTable read = *(PathTable*)table;
        Assert.Equal(1u, read.Version);
        Assert.Equal(16u, read.StructSize);
        Assert.NotEqual(IntPtr.Zero, read.FindPath);
        Assert.Equal(IntPtr.Zero, Interface(host, "RynthNav", "RynthNav.Path", 2));
    }

    [Fact]
    public void RynthLuaCanFetchRynthAisScriptTable()
    {
        using RynthHost host = CreateHost();
        Assert.NotEqual(IntPtr.Zero, Interface(host, "RynthAi", "RynthAi.Script", 1));
    }

    [Fact]
    public void TheNavDataFolderGoesUnderTheCoreFolderOffWindows()
    {
        var env = new Dictionary<string, string?>();
        RynthInstallFolders.Apply(k => env.GetValueOrDefault(k), (k, v) => env[k] = v, isWindows: false, "/home/x/.config");
        Assert.Equal(Path.Combine("/home/x/.config", "RynthCore", "NavData"), env[RynthInstallFolders.NavDataVariable]);

        env.Clear();
        env[RynthInstallFolders.NavDataVariable] = "/tiles";
        RynthInstallFolders.Apply(k => env.GetValueOrDefault(k), (k, v) => env[k] = v, isWindows: false, "/home/x/.config");
        Assert.Equal("/tiles", env[RynthInstallFolders.NavDataVariable]);

        env.Clear();
        RynthInstallFolders.Apply(k => env.GetValueOrDefault(k), (k, v) => env[k] = v, isWindows: true, @"C:\Users\x\AppData\Roaming");
        Assert.False(env.ContainsKey(RynthInstallFolders.NavDataVariable));
    }

    [Fact]
    public void TheHintAppearsOnlyWithoutTiles()
    {
        string empty = Directory.CreateTempSubdirectory("rynth-nav-hint-").FullName;
        string tiled = Directory.CreateTempSubdirectory("rynth-nav-hint-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(tiled, "nav_A9B4.tile"), [1]);
            Assert.Null(RynthNavDataHint.For(null));
            Assert.Null(RynthNavDataHint.For(" "));
            Assert.Null(RynthNavDataHint.For(tiled));
            Assert.Contains(empty, RynthNavDataHint.For(empty), StringComparison.Ordinal);
            Assert.Contains(RynthNavDataHint.InstallCommand, RynthNavDataHint.For(Path.Combine(empty, "missing")), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(empty, true);
            Directory.Delete(tiled, true);
        }
    }
}
```

If `RynthConfigSandbox` per-test resets `RYNTHNAV_NAVDATA`, it does not today (only HOME, XDG, RYNTHCORE_DIR, RYNTHSUITE_DIR), and RynthNav resolves its folder once per process anyway.

- [ ] **Step 9: Run the tests**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -c Release`
Expected: all pass. If `RynthLuaCanFetchRynthAisScriptTable` fails because RynthAi only answers after login, tick the host once with `host.Bridge` login (see `RynthHostTests.EnterWorld`) before asking, and say so in the commit message.

- [ ] **Step 10: Build the client**

Run: `~/.dotnet/dotnet build AcDream.slnx -c Release` → `Build succeeded`, 0 errors.

- [ ] **Step 11: Commit**

```bash
git add Directory.Packages.props AcDream.slnx src/AcDream.Rynth.Plugin.RynthNav src/AcDream.Rynth.Host tests/AcDream.Rynth.Host.Tests '**/packages.*.lock.json'
git commit -m "feat(rynth): RynthNav compiled unchanged and loaded beside RynthAi; tiles from <RYNTHCORE_DIR>/NavData

Claude-Session: https://claude.ai/code/session_011FtdjFB5dEVWdBQnZmstpz"
```

```json:metadata
{"files": ["Directory.Packages.props", "AcDream.slnx", "src/AcDream.Rynth.Plugin.RynthNav/AcDream.Rynth.Plugin.RynthNav.csproj", "src/AcDream.Rynth.Host/AcDream.Rynth.Host.csproj", "src/AcDream.Rynth.Host/RynthInstallFolders.cs", "src/AcDream.Rynth.Host/RynthNavDataHint.cs", "src/AcDream.Rynth.Host/RynthClientExtension.cs", "tests/AcDream.Rynth.Host.Tests/AssemblyConfigSandbox.cs", "tests/AcDream.Rynth.Host.Tests/RynthNavPluginTests.cs"], "verifyCommand": "~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -c Release", "acceptanceCriteria": ["RynthNav wrapper builds with no edits under external/", "host with RynthNav+RynthAi+RynthLua: 3 active, 30 ticks, no Exception lines, ReportedName RynthNav", "RynthNav logs Tiles: <sandbox NavData> (RYNTHNAV_NAVDATA)", "RynthNav.Path v1 table Version 1 StructSize 16 FindPath non-zero; RynthAi.Script v1 non-zero", "RYNTHNAV_NAVDATA set to <RYNTHCORE_DIR>/NavData off Windows when unset, kept when set, untouched on Windows", "hint null with tiles or no folder name; names folder and install command otherwise", "client extension loads RynthNav before RynthAi, logs hint at start, chat once after first login"], "modelTier": "standard"}
```

---

### Task 3: `tools/RynthNavData` (pin, install, check-feed)

**Goal:** A maintainer can pin one RynthNav archive and anyone can install exactly that archive, with RynthCore's own launcher install code.

**Files:**
- Create: `tools/RynthNavData/AcDream.Tools.RynthNavData.csproj`, `tools/RynthNavData/Program.cs`, `tools/RynthNavData/NavDataPin.cs`, `tools/RynthNavData/NavDataPinner.cs`, `tools/RynthNavData/NavDataInstaller.cs`, `tools/RynthNavData/NavDataFolders.cs`
- Create: `tests/AcDream.Tools.RynthNavData.Tests/AcDream.Tools.RynthNavData.Tests.csproj`, `tests/AcDream.Tools.RynthNavData.Tests/LocalServer.cs`, `tests/AcDream.Tools.RynthNavData.Tests/NavDataToolTests.cs`
- Modify: `AcDream.slnx` (both projects), `tools/README.md` (one table row), lock files from the restore

**Acceptance Criteria:**
- [ ] The tool compiles RynthCore's `RynthUpdater.cs`, `RynthUpdater.NavData.cs`, `RynthUpdater.Available.cs`, `RynthUpdater.MapPacks.cs`, `DownloadProgress.cs`, `UpdateTrust.cs`, `RynthCore.Engine/Plugins/PluginContractVersion.cs` and `Shared/RynthInstallPaths.cs` unchanged (the set their `tools/NavDataUpdateTests` compiles).
- [ ] `pin` writes `size`, `sha256` (lowercase), `files`, `bytes`, `id == sha256`, `url`, `rynthSuite`, `pinned`; it keeps the verified zip in the cache as `navdata-<id>.zip` so `install` reuses it.
- [ ] `pin` refuses an archive with a name their rules do not allow (`InvalidDataException`) and writes no pin.
- [ ] `install` puts the tiles in, writes `navdata.id`, keeps `atlas.txt`, keeps the old folder as `NavData.previous`.
- [ ] `install` refuses a SHA-256 mismatch and leaves the folder as it was.
- [ ] `install` leaves a folder with `navdata.local` alone and does nothing (no request) when `navdata.id` already matches.
- [ ] No test touches the network or the real user folders.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Tools.RynthNavData.Tests -c Release` → all pass

**Steps:**

- [ ] **Step 1: Project** — `tools/RynthNavData/AcDream.Tools.RynthNavData.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <!-- RynthNav's navmesh tiles for OpenAC: pins one archive of theirs
       (external/rynth-navdata.json, our own SHA-256: the navdata follows our release, not
       their feed) and installs exactly that one. The download, checks, unpack and folder
       swap are RynthCore's launcher code, compiled unchanged: the same files their
       tools/NavDataUpdateTests compiles. Their code, their warnings. -->
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <RootNamespace>AcDream.Tools.RynthNavData</RootNamespace>
    <AssemblyName>AcDream.Tools.RynthNavData</AssemblyName>
    <IsPackable>false</IsPackable>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <RunAnalyzers>false</RunAnalyzers>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
    <RynthCoreSrc>$(MSBuildThisFileDirectory)..\..\external\RynthCore\src\</RynthCoreSrc>
  </PropertyGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="AcDream.Tools.RynthNavData.Tests" />
  </ItemGroup>
  <ItemGroup>
    <Compile Include="$(RynthCoreSrc)RynthCore.App.Avalonia\RynthUpdater.cs" Link="Linked\RynthUpdater.cs" />
    <Compile Include="$(RynthCoreSrc)RynthCore.App.Avalonia\RynthUpdater.NavData.cs" Link="Linked\RynthUpdater.NavData.cs" />
    <Compile Include="$(RynthCoreSrc)RynthCore.App.Avalonia\RynthUpdater.Available.cs" Link="Linked\RynthUpdater.Available.cs" />
    <Compile Include="$(RynthCoreSrc)RynthCore.App.Avalonia\RynthUpdater.MapPacks.cs" Link="Linked\RynthUpdater.MapPacks.cs" />
    <Compile Include="$(RynthCoreSrc)RynthCore.App.Avalonia\DownloadProgress.cs" Link="Linked\DownloadProgress.cs" />
    <Compile Include="$(RynthCoreSrc)RynthCore.App.Avalonia\UpdateTrust.cs" Link="Linked\UpdateTrust.cs" />
    <Compile Include="$(RynthCoreSrc)RynthCore.Engine\Plugins\PluginContractVersion.cs" Link="Linked\PluginContractVersion.cs" />
    <Compile Include="$(RynthCoreSrc)Shared\RynthInstallPaths.cs" Link="Linked\RynthInstallPaths.cs" />
  </ItemGroup>
</Project>
```

If their files need implicit usings (their test project has `ImplicitUsings` enabled), switch `ImplicitUsings` to `enable`; do not edit their files.

- [ ] **Step 2: Folders** — `tools/RynthNavData/NavDataFolders.cs`:

```csharp
using System;
using System.IO;

namespace AcDream.Tools.RynthNavData;

/// <summary>
/// The same folders the client uses (AcDream.Rynth.Host.RynthInstallFolders): off Windows
/// RYNTHCORE_DIR defaults under ApplicationData and the tiles go in its NavData folder unless
/// RYNTHNAV_NAVDATA names another. On Windows RynthCore's own lookup decides.
/// </summary>
internal static class NavDataFolders
{
    internal static string NavData()
    {
        if (Environment.GetEnvironmentVariable("RYNTHNAV_NAVDATA") is { Length: > 0 } explicitDir)
            return explicitDir;
        if (!OperatingSystem.IsWindows() && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RYNTHCORE_DIR")))
            Environment.SetEnvironmentVariable("RYNTHCORE_DIR",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RynthCore"));
        return Path.Combine(RynthCore.Install.RynthInstallPaths.CoreDir, "NavData");
    }

    /// <summary>Where downloaded archives wait: ~/Library/Caches on macOS, RynthCore's own cache elsewhere.</summary>
    internal static string Cache() => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Caches", "RynthCore", "updates")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RynthCore", "updates");
}
```

- [ ] **Step 3: The pin** — `tools/RynthNavData/NavDataPin.cs`:

```csharp
using System;
using System.IO;
using System.Text.Json;
using RynthCore.App.Avalonia;

namespace AcDream.Tools.RynthNavData;

/// <summary>
/// external/rynth-navdata.json: the one RynthNav archive this OpenAC build was tested with.
/// <see cref="Id"/> is the archive's SHA-256 and is what the installed navdata.id holds.
/// <see cref="RynthSuite"/> is the RynthSuite commit it was tested against.
/// </summary>
internal sealed record NavDataPin(string Url, long Size, string Sha256, int Files, long Bytes, string Id, string RynthSuite, string Pinned)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    internal static NavDataPin Load(string path) =>
        JsonSerializer.Deserialize<NavDataPin>(File.ReadAllText(path), Json)
        ?? throw new InvalidDataException($"{path} holds no pin");

    internal void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, Json) + "\n");

    /// <summary>Their feed entry for this archive (release 0: not from a feed).</summary>
    internal RynthUpdater.NavDataEntry ToEntry() => new(Id, 0, Url, Size, Sha256, Files, Bytes);
}
```

- [ ] **Step 4: Pinning** — `tools/RynthNavData/NavDataPinner.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using RynthCore.App.Avalonia;

namespace AcDream.Tools.RynthNavData;

/// <summary>
/// Downloads an archive once, checks it with RynthCore's own unpack rules (allowed names,
/// entry size limit, no duplicates) and returns its pin. The verified zip stays in the cache
/// under the name RynthCore's install looks for, so the install that follows reuses it.
/// </summary>
internal static class NavDataPinner
{
    // RynthUpdater.NavData's MaxNavDataBytes (private there): the largest archive accepted.
    private const long MaxArchiveBytes = 3L << 30;

    internal static async Task<NavDataPin> PinAsync(string url, string cacheDir, string rynthSuite, DateOnly today,
        HttpClient http, TextWriter log, CancellationToken ct = default)
    {
        Directory.CreateDirectory(cacheDir);
        string part = Path.Combine(cacheDir, "navdata-pin.part");
        long size = 0;
        string sha256;
        using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            using HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using (Stream body = await response.Content.ReadAsStreamAsync(ct))
            await using (var file = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[81920];
                int read;
                long nextReport = 64L << 20;
                while ((read = await body.ReadAsync(buffer, ct)) > 0)
                {
                    size += read;
                    if (size > MaxArchiveBytes)
                        throw new InvalidDataException($"{url} is larger than {MaxArchiveBytes >> 20} MB");
                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                    if (size >= nextReport)
                    {
                        log.WriteLine($"  {size >> 20} MB");
                        nextReport += 64L << 20;
                    }
                }
            }

            sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }

        string staging = Path.Combine(cacheDir, "navdata-pin-check");
        try
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
            Directory.CreateDirectory(staging);
            int files = RynthUpdater.ExtractNavData(part, staging, ct);
            long bytes = Directory.GetFiles(staging).Sum(f => new FileInfo(f).Length);
            File.Move(part, Path.Combine(cacheDir, $"navdata-{sha256}.zip"), overwrite: true);
            return new NavDataPin(url, size, sha256, files, bytes, sha256, rynthSuite, today.ToString("yyyy-MM-dd"));
        }
        catch
        {
            try { File.Delete(part); } catch { }
            throw;
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); } catch { }
        }
    }
}
```

- [ ] **Step 5: Installing** — `tools/RynthNavData/NavDataInstaller.cs`:

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RynthCore.App.Avalonia;

namespace AcDream.Tools.RynthNavData;

/// <summary>
/// Installs exactly the pinned archive with RynthCore's own UpdateNavDataAsync: disk space,
/// download checked against our size and SHA-256 (a verified cached zip is reused), unpack
/// beside the folder with the file count check, the player's own files copied across,
/// navdata.id written, folders swapped with one NavData.previous kept. The state check is
/// theirs too (RynthUpdater.CheckNavData, which needs their feed): navdata.local = left
/// alone, a matching navdata.id = nothing to do.
/// </summary>
internal static class NavDataInstaller
{
    internal static async Task<string> InstallAsync(NavDataPin pin, string navDataDir, string cacheDir,
        IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        RynthUpdater.NavDataEntry entry = pin.ToEntry();
        if (File.Exists(Path.Combine(navDataDir, RynthUpdater.NavDataLocalMarker)))
            return $"{navDataDir} has {RynthUpdater.NavDataLocalMarker}: its tiles are left alone.";

        string idFile = Path.Combine(navDataDir, RynthUpdater.NavDataIdFile);
        string installed = File.Exists(idFile) ? File.ReadAllText(idFile).Trim().ToLowerInvariant() : string.Empty;
        if (installed == entry.Id)
            return $"{navDataDir} already has the pinned tiles ({entry.Files} files).";

        var updater = new RynthUpdater(trustedKeys: Array.Empty<string>(), stateDir: cacheDir) { NavDataCacheDir = cacheDir };
        var manifest = new RynthUpdater.Manifest(0, string.Empty, DateTime.UnixEpoch, string.Empty,
            new RynthUpdater.InstallerEntry(0, string.Empty, string.Empty, 0, string.Empty),
            Array.Empty<RynthUpdater.PluginEntry>(), entry);
        var check = new RynthUpdater.CheckResult(manifest, 0, Array.Empty<RynthUpdater.PluginStatus>())
        {
            NavData = new RynthUpdater.NavDataStatus(entry, navDataDir, installed, RynthUpdater.NavDataState.NeedsUpdate),
        };
        return await updater.UpdateNavDataAsync(check, progress, ct);
    }
}
```

- [ ] **Step 6: Commands** — `tools/RynthNavData/Program.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using AcDream.Tools.RynthNavData;
using RynthCore.App.Avalonia;

// pin --url <zip> --rynthsuite <commit> [--pin <file>] [--cache <dir>]
// install [--dir <NavData>] [--pin <file>] [--cache <dir>]
// check-feed
return await Run(args);

static async Task<int> Run(string[] args)
{
    string verb = args.FirstOrDefault() ?? string.Empty;
    string? Option(string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    string pinPath = Option("--pin") ?? FindPin();
    string cache = Option("--cache") ?? NavDataFolders.Cache();
    try
    {
        switch (verb)
        {
            case "pin":
            {
                string url = Option("--url") ?? throw new ArgumentException("pin needs --url <zip>");
                string suite = Option("--rynthsuite") ?? throw new ArgumentException("pin needs --rynthsuite <commit>");
                using var http = new HttpClient { Timeout = TimeSpan.FromHours(2) };
                NavDataPin pin = await NavDataPinner.PinAsync(url, cache, suite, DateOnly.FromDateTime(DateTime.UtcNow), http, Console.Out);
                pin.Save(pinPath);
                Console.WriteLine($"Pinned {pin.Files} files ({pin.Bytes >> 20} MB unpacked), sha256 {pin.Sha256}, into {pinPath}.");
                return 0;
            }
            case "install":
            {
                NavDataPin pin = NavDataPin.Load(pinPath);
                string dir = Option("--dir") ?? NavDataFolders.NavData();
                var progress = new Progress<DownloadProgress>(p => Console.WriteLine($"  {p.Detail}"));
                Console.WriteLine(await NavDataInstaller.InstallAsync(pin, dir, cache, progress));
                return 0;
            }
            case "check-feed":
            {
                var updater = new RynthUpdater(stateDir: Path.Combine(cache, "feed-state"));
                RynthUpdater.CheckResult result = await updater.CheckAsync(Array.Empty<string>());
                if (result.Manifest.NavData is not { } feed)
                {
                    Console.WriteLine($"Their feed (release {result.Manifest.Release}) has no navdata entry.");
                    return 0;
                }

                Console.WriteLine($"Their feed (release {result.Manifest.Release}) navdata: release {feed.Release}, {feed.Files} files, {feed.Size} bytes, sha256 {feed.Sha256}, {feed.Url}");
                if (File.Exists(pinPath))
                    Console.WriteLine(string.Equals(NavDataPin.Load(pinPath).Sha256, feed.Sha256, StringComparison.OrdinalIgnoreCase)
                        ? "Same archive as our pin."
                        : "A different archive from our pin: test it against the pinned RynthSuite before re-pinning.");
                return 0;
            }
            default:
                Console.Error.WriteLine("usage: pin --url <zip> --rynthsuite <commit> | install [--dir <NavData>] | check-feed  [--pin <file>] [--cache <dir>]");
                return 2;
        }
    }
    catch (Exception error) when (error is IOException or InvalidDataException or HttpRequestException or ArgumentException or InvalidOperationException)
    {
        Console.Error.WriteLine($"{verb} failed: {error.Message}");
        return 1;
    }
}

// external/rynth-navdata.json from the current folder or any folder above it.
static string FindPin()
{
    for (DirectoryInfo? dir = new(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
    {
        string candidate = Path.Combine(dir.FullName, "external", "rynth-navdata.json");
        if (File.Exists(candidate) || Directory.Exists(Path.Combine(dir.FullName, "external", "RynthCore")))
            return candidate;
    }

    return Path.Combine("external", "rynth-navdata.json");
}
```

`DownloadProgress.Detail` is their launcher's line under the progress bar; its "verified against the signed feed" wording is theirs (here the check is against our pin).

- [ ] **Step 7: Test project** — `tests/AcDream.Tools.RynthNavData.Tests/AcDream.Tools.RynthNavData.Tests.csproj` (same shape as `tests/AcDream.Tools.RynthTypeExtract.Tests`):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="xunit.v3" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\tools\RynthNavData\AcDream.Tools.RynthNavData.csproj" />
  </ItemGroup>
</Project>
```

`tests/AcDream.Tools.RynthNavData.Tests/LocalServer.cs` (their NavDataUpdateTests serve from an `HttpListener` the same way):

```csharp
using System.Net;
using System.Net.Sockets;

namespace AcDream.Tools.RynthNavData.Tests;

/// <summary>Serves byte arrays from http://localhost:&lt;free port&gt;/ and counts requests.</summary>
internal sealed class LocalServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public LocalServer()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        Base = $"http://localhost:{port}/";
        _listener.Prefixes.Add(Base);
        _listener.Start();
        _ = Task.Run(Serve);
    }

    public string Base { get; }

    public int Requests;

    public string Add(string name, byte[] content)
    {
        lock (_files)
            _files[name] = content;
        return Base + name;
    }

    private async Task Serve()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch { return; }
            Interlocked.Increment(ref Requests);
            byte[]? body;
            lock (_files)
                _files.TryGetValue(context.Request.Url!.AbsolutePath.TrimStart('/'), out body);
            context.Response.StatusCode = body is null ? 404 : 200;
            if (body is not null)
            {
                context.Response.ContentLength64 = body.Length;
                await context.Response.OutputStream.WriteAsync(body);
            }

            context.Response.Close();
        }
    }

    public void Dispose() => _listener.Stop();
}
```

- [ ] **Step 8: Tool tests** — `tests/AcDream.Tools.RynthNavData.Tests/NavDataToolTests.cs`:

```csharp
using System.IO.Compression;
using System.Security.Cryptography;

namespace AcDream.Tools.RynthNavData.Tests;

public sealed class NavDataToolTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("rynth-navdata-tool-").FullName;
    private readonly LocalServer _server = new();
    private readonly HttpClient _http = new();

    private string Cache => Path.Combine(_root, "cache");
    private string NavData => Path.Combine(_root, "NavData");

    public void Dispose()
    {
        _http.Dispose();
        _server.Dispose();
        try { Directory.Delete(_root, true); } catch { }
    }

    private static byte[] Archive(params (string Name, string Text)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string text) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(text);
            }
        }

        return buffer.ToArray();
    }

    private static readonly byte[] Good = Archive(("nav_A9B4.tile", "tile a"), ("nav_A9B5.tile", "tile b"), ("locations.json", "{}"));

    private Task<NavDataPin> Pin(byte[] archive) =>
        NavDataPinner.PinAsync(_server.Add("RynthNav-NavData.zip", archive), Cache, "e6fffb0f", new DateOnly(2026, 10, 19), _http, TextWriter.Null);

    [Fact]
    public async Task PinRecordsTheArchive()
    {
        NavDataPin pin = await Pin(Good);

        Assert.Equal(Good.Length, pin.Size);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Good)).ToLowerInvariant(), pin.Sha256);
        Assert.Equal(pin.Sha256, pin.Id);
        Assert.Equal(3, pin.Files);
        Assert.Equal("tile a".Length + "tile b".Length + 2, pin.Bytes);
        Assert.Equal("e6fffb0f", pin.RynthSuite);
        Assert.Equal("2026-10-19", pin.Pinned);
        Assert.True(File.Exists(Path.Combine(Cache, $"navdata-{pin.Id}.zip")));
    }

    [Fact]
    public async Task PinRefusesANameRynthNavWouldNotRead()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => Pin(Archive(("nav_A9B4.tile", "a"), ("evil.exe", "x"))));
        Assert.Empty(Directory.GetFiles(Cache, "navdata-*.zip"));
    }

    [Fact]
    public async Task ThePinRoundTripsThroughItsFile()
    {
        NavDataPin pin = await Pin(Good);
        string path = Path.Combine(_root, "rynth-navdata.json");
        pin.Save(path);

        Assert.Equal(pin, NavDataPin.Load(path));
        Assert.Contains("\"sha256\"", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallPutsTheTilesInAndKeepsThePlayersFiles()
    {
        NavDataPin pin = await Pin(Good);
        Directory.CreateDirectory(NavData);
        File.WriteAllText(Path.Combine(NavData, "atlas.txt"), "my places");

        await NavDataInstaller.InstallAsync(pin, NavData, Cache);

        Assert.True(File.Exists(Path.Combine(NavData, "nav_A9B4.tile")));
        Assert.Equal("my places", File.ReadAllText(Path.Combine(NavData, "atlas.txt")));
        Assert.Equal(pin.Id, File.ReadAllText(Path.Combine(NavData, "navdata.id")).Trim());
        Assert.True(Directory.Exists(NavData + ".previous"));
    }

    [Fact]
    public async Task InstallRefusesAnArchiveThatIsNotThePinnedOne()
    {
        NavDataPin pin = await Pin(Good) with { Sha256 = new string('0', 64), Id = new string('0', 64) };
        File.Delete(Path.Combine(Cache, $"navdata-{Convert.ToHexString(SHA256.HashData(Good)).ToLowerInvariant()}.zip"));
        Directory.CreateDirectory(NavData);
        File.WriteAllText(Path.Combine(NavData, "atlas.txt"), "my places");

        await Assert.ThrowsAsync<InvalidDataException>(() => NavDataInstaller.InstallAsync(pin, NavData, Cache));

        Assert.Equal(["atlas.txt"], Directory.GetFiles(NavData).Select(Path.GetFileName));
    }

    [Fact]
    public async Task InstallLeavesLocalTilesAlone()
    {
        NavDataPin pin = await Pin(Good);
        Directory.CreateDirectory(NavData);
        File.WriteAllText(Path.Combine(NavData, "navdata.local"), "");

        string said = await NavDataInstaller.InstallAsync(pin, NavData, Cache);

        Assert.Contains("navdata.local", said, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(NavData, "nav_A9B4.tile")));
    }

    [Fact]
    public async Task InstallDoesNothingWhenThePinnedTilesAreThere()
    {
        NavDataPin pin = await Pin(Good);
        Directory.CreateDirectory(NavData);
        File.WriteAllText(Path.Combine(NavData, "navdata.id"), pin.Id);
        int before = _server.Requests;

        await NavDataInstaller.InstallAsync(pin, NavData, Cache);

        Assert.Equal(before, _server.Requests);
        Assert.False(File.Exists(Path.Combine(NavData, "nav_A9B4.tile")));
    }
}
```

(`InstallRefusesAnArchiveThatIsNotThePinnedOne` deletes the cached good zip so their install downloads again and its size+hash check fails; `DownloadVerifiedAsync` deletes the partial file.)

- [ ] **Step 9: Solution, README, restore** — add both projects to `AcDream.slnx` (`/tools/` and the tests folder, beside `RynthTypeExtract`). Add a row to `tools/README.md`:

```markdown
| `RynthNavData/` | RynthNav's navmesh tiles: `pin` records one archive of theirs in `external/rynth-navdata.json` (our own SHA-256), `install` installs exactly that one into `<RYNTHCORE_DIR>/NavData` with RynthCore's launcher code, `check-feed` shows what their signed feed offers. Run with `dotnet run --project tools/RynthNavData -c Release -- <verb>`. |
```

Run `~/.dotnet/dotnet restore AcDream.slnx --force-evaluate`; revert Launcher RID-swap-only lock churn as in Task 2.

- [ ] **Step 10: Run the tests**

Run: `~/.dotnet/dotnet test tests/AcDream.Tools.RynthNavData.Tests -c Release`
Expected: 7 passed.

- [ ] **Step 11: Commit**

```bash
git add tools/RynthNavData tests/AcDream.Tools.RynthNavData.Tests AcDream.slnx tools/README.md '**/packages.*.lock.json'
git commit -m "feat(rynth): tools/RynthNavData pins one RynthNav archive and installs exactly it, with RynthCore's launcher code

Claude-Session: https://claude.ai/code/session_011FtdjFB5dEVWdBQnZmstpz"
```

```json:metadata
{"files": ["tools/RynthNavData/AcDream.Tools.RynthNavData.csproj", "tools/RynthNavData/Program.cs", "tools/RynthNavData/NavDataPin.cs", "tools/RynthNavData/NavDataPinner.cs", "tools/RynthNavData/NavDataInstaller.cs", "tools/RynthNavData/NavDataFolders.cs", "tests/AcDream.Tools.RynthNavData.Tests/AcDream.Tools.RynthNavData.Tests.csproj", "tests/AcDream.Tools.RynthNavData.Tests/LocalServer.cs", "tests/AcDream.Tools.RynthNavData.Tests/NavDataToolTests.cs", "AcDream.slnx", "tools/README.md"], "verifyCommand": "~/.dotnet/dotnet test tests/AcDream.Tools.RynthNavData.Tests -c Release", "acceptanceCriteria": ["compiles RynthCore's 8 updater files unchanged", "pin writes size/sha256/files/bytes/id==sha256/url/rynthSuite/pinned and keeps navdata-<id>.zip", "pin refuses a disallowed name and writes no pin", "install adds tiles, writes navdata.id, keeps atlas.txt, keeps NavData.previous", "install refuses a SHA-256 mismatch and leaves the folder as it was", "install leaves navdata.local alone and makes no request on a matching navdata.id", "no network or real user folders in tests"], "modelTier": "standard"}
```

---

### Task 4: Pin the real archive and prove the tiles work with our RynthNav

**Goal:** The real 15 Oct archive is pinned and installed locally, and a test shows our compiled RynthNav plans a path over those tiles through `GetPluginInterface`. (The spec's "a building between" is left to the live check: the test picks two vertices of Holtburg's own tile so both ends are on the mesh without hand-picked coordinates.)

> **USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in `acceptanceCriteria` has been re-validated independently, with output captured.

**Files:**
- Create: `external/rynth-navdata.json` (written by the tool)
- Modify: `tests/AcDream.Rynth.Host.Tests/RynthNavPluginTests.cs` (real-tile tests)

**Acceptance Criteria:**
- [ ] `pin` against the pinned URL succeeds: `size` = 1572337084, `files` > 30000, every name allowed by their rules; output captured.
- [ ] `install` puts the tiles into `~/Library/Application Support/RynthCore/NavData` (`nav_A9B4.tile` present, `navdata.id` = pin id); the cached zip is gone afterwards; output captured.
- [ ] With the tiles copied into the test sandbox, `FindPath` through `GetPluginInterface("RynthNav","RynthNav.Path",1)` returns Ok (1) with count ≥ 2 between two mesh points about 40+ units apart in A9B4, with the first corner within 1 unit of the start and the last within 6 units of the goal.
- [ ] `FindPath` returns NoMesh (-1) before RynthNav has seen a pose and NoPath (0) for a goal more than 25 tiles' block away.
- [ ] No `[RynthNav] tile 0x.... not added` or `failed to load` lines in the test log.
- [ ] **Stop point:** if the tiles do not load or link (any of the last three items fails), stop, capture the RynthNav log lines, and ask the user before going on. Do not change the bake, the pin URL or their code to make it pass.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -c Release --filter FullyQualifiedName~RynthNavPluginTests` → all pass, including the real-tile tests (not skipped)

**Steps:**

- [ ] **Step 1: Pin** (1.57 GB download; run in the background and wait for it)

```bash
~/.dotnet/dotnet run --project tools/RynthNavData -c Release -- pin \
  --url https://aelrynth.com/downloads/rynth/releases/2026101501/RynthNav-NavData.zip \
  --rynthsuite "$(git -C external/RynthSuite rev-parse --short=8 HEAD)"
cat external/rynth-navdata.json
```

Expected: `Pinned N files (... MB unpacked), sha256 ...`; the JSON shows `"size": 1572337084`. If `ExtractNavData` refuses a name, stop and report the name (their archive and their rules disagree).

- [ ] **Step 2: Install locally**

```bash
~/.dotnet/dotnet run --project tools/RynthNavData -c Release -- install
ls ~/Library/Application\ Support/RynthCore/NavData | head; ls ~/Library/Application\ Support/RynthCore/NavData | wc -l
cat ~/Library/Application\ Support/RynthCore/NavData/navdata.id
ls ~/Library/Caches/RynthCore/updates
```

Expected: the "RynthNav navmesh tiles updated: N files into ..." line; `nav_A9B4.tile` listed; `navdata.id` equals the pin's `id`; no `navdata-*.zip` left in the cache. The earlier `locations.json` there is replaced by the archive's and the old folder kept as `NavData.previous`.

- [ ] **Step 3: Real-tile tests** — add to `RynthNavPluginTests`:

```csharp
    private const int Ok = 1, NoPath = 0, NoMesh = -1;

    // Two vertices of Holtburg's own tile, far apart: both on the mesh by construction. Tile
    // vertices are world units, x east, y up, z north (DotRecast's frame); RynthNav takes
    // east, north, height.
    private static ((double E, double N, double H) Start, (double E, double N, double H) Goal) TwoMeshPoints()
    {
        string path = Path.Combine(TestNavData.Directory, $"nav_{TestNavData.HoltburgLandblock:X4}.tile");
        using var reader = new BinaryReader(File.OpenRead(path));
        DotRecast.Detour.DtMeshData mesh = new DotRecast.Detour.Io.DtMeshDataReader().Read(reader, 6);
        float[] v = mesh.verts;
        (double, double, double) Vertex(int i) => (v[i * 3], v[i * 3 + 2], v[i * 3 + 1]);
        var start = Vertex(0);
        int best = 0;
        double bestDistance = 0;
        for (int i = 1; i < mesh.header.vertCount; i++)
        {
            var p = Vertex(i);
            double d = Math.Sqrt((p.Item1 - start.Item1) * (p.Item1 - start.Item1) + (p.Item2 - start.Item2) * (p.Item2 - start.Item2));
            if (d > bestDistance && d < 120)
            {
                bestDistance = d;
                best = i;
            }
        }

        return (start, Vertex(best));
    }

    private static int FindPath(RynthHost host, (double E, double N, double H) start, (double E, double N, double H) goal, out double[] corners)
    {
        PathTable table = *(PathTable*)Interface(host, "RynthNav", "RynthNav.Path", 1);
        double[] points = new double[128 * 3];
        int count = 0;
        int result;
        fixed (double* p = points)
            result = ((delegate* unmanaged[Cdecl]<double, double, double, double, double, double, double*, int, int*, int>)table.FindPath)(
                start.E, start.N, start.H, goal.E, goal.N, goal.H, p, 128, &count);
        corners = points[..(count * 3)];
        return result;
    }

    private void StandAt((double E, double N, double H) point)
    {
        uint lbX = (uint)(point.E / 192.0), lbY = (uint)(point.N / 192.0);
        float x = (float)(point.E - lbX * 192.0), y = (float)(point.N - lbY * 192.0);
        uint cell = (lbX << 24) | (lbY << 16) | (uint)((int)(x / 24) * 8 + (int)(y / 24) + 1);
        _view.Pose = (cell, new System.Numerics.Vector3(x, y, (float)point.H), System.Numerics.Quaternion.Identity);
    }

    // xunit 2 (this project) has no dynamic skip; the note shows in the test's output, as the
    // dat-backed tests do (RynthAiInteropTests.GeometryLoaderOpensTheInstalledDatsThroughTheStandIn).
    private bool NoTiles()
    {
        if (TestNavData.HasTiles)
            return false;
        _output.WriteLine("skipped: no RynthNav tiles installed (tools/RynthNavData install)");
        return true;
    }

    [Fact]
    public void RynthNavPlansAPathOverTheInstalledTiles()
    {
        if (NoTiles())
            return;
        using RynthHost host = CreateHost();
        var (start, goal) = TwoMeshPoints();
        Assert.True(Math.Sqrt((goal.E - start.E) * (goal.E - start.E) + (goal.N - start.N) * (goal.N - start.N)) >= 40);
        StandAt(start);
        host.Tick();

        int result = FindPath(host, start, goal, out double[] corners);

        Assert.Equal(Ok, result);
        Assert.True(corners.Length >= 6);
        Assert.True(Math.Abs(corners[0] - start.E) < 1 && Math.Abs(corners[1] - start.N) < 1);
        Assert.True(Math.Abs(corners[^3] - goal.E) < 6 && Math.Abs(corners[^2] - goal.N) < 6);
        Assert.DoesNotContain(_log, line => line.Contains("not added", StringComparison.Ordinal) || line.Contains("failed to load", StringComparison.Ordinal));
    }

    [Fact]
    public void RynthNavHasNoMeshBeforeAPoseAndNoPathTooFarAway()
    {
        if (NoTiles())
            return;
        using RynthHost host = CreateHost();
        var (start, goal) = TwoMeshPoints();

        Assert.Equal(NoMesh, FindPath(host, start, goal, out _));

        StandAt(start);
        host.Tick();
        Assert.Equal(NoPath, FindPath(host, start, (start.E + 10 * 192, start.N + 10 * 192, start.H), out _));
    }
```

Without tiles these two tests return early with a "skipped:" output line (xunit 2.9 has no dynamic skip); Step 4 checks that line is absent. `DtMeshData` member names (`verts`, `header.vertCount`) are DotRecast 2026.1.3's; check them with the IDE or `RynthNavPlugin.OnTileLoaded` if they differ. The test project gets DotRecast through the Host → RynthNav reference; if it does not flow, add `<PackageReference Include="DotRecast.Detour" />` to the test csproj.

- [ ] **Step 4: Run them, with tiles present**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -c Release --filter FullyQualifiedName~RynthNavPluginTests --logger "console;verbosity=detailed"`
Expected: all pass and no `skipped: no RynthNav tiles` line in the output (the two real-tile tests really ran). If either fails: STOP (see Acceptance Criteria), capture the `[RynthNav]` log lines, report to the user.

- [ ] **Step 5: Full suites**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -c Release` and `~/.dotnet/dotnet test tests/AcDream.Tools.RynthNavData.Tests -c Release` → all pass.

- [ ] **Step 6: Commit**

```bash
git add external/rynth-navdata.json tests/AcDream.Rynth.Host.Tests/RynthNavPluginTests.cs
git commit -m "feat(rynth): pin RynthNav's 2026-10-15 navdata; our RynthNav plans over it

Claude-Session: https://claude.ai/code/session_011FtdjFB5dEVWdBQnZmstpz"
```

```json:metadata
{"files": ["external/rynth-navdata.json", "tests/AcDream.Rynth.Host.Tests/RynthNavPluginTests.cs"], "verifyCommand": "~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests -c Release --filter FullyQualifiedName~RynthNavPluginTests", "acceptanceCriteria": ["pin succeeds: size 1572337084, files > 30000, output captured", "install: nav_A9B4.tile present, navdata.id == pin id, cached zip removed, output captured", "FindPath via GetPluginInterface returns 1 with >=2 corners between two A9B4 mesh points >=40 units apart; first corner within 1 of start, last within 6 of goal", "FindPath returns -1 before a pose and 0 for a goal beyond the 25-tile block", "no 'not added' / 'failed to load' RynthNav log lines", "stop and ask the user if tiles do not load or link"], "modelTier": "standard", "userGate": true, "tags": ["user-gate"]}
```

---

### Task 5: Live check (user, later)

**Goal:** In the real client on the local ACE server, RynthAi's outdoor detour plans over the navmesh and walks round an obstacle.

> **USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in `acceptanceCriteria` has been re-validated independently, with output captured.

**Files:**
- None (results recorded in the ledger and this plan's task status)

**Acceptance Criteria:**
- [ ] Ask the user first; never launch a client without their go-ahead (local ACE 10.10.20.20 only, never Dreamweave).
- [ ] At login the client log has `[RynthNav] Initialized ... Tiles: <~/Library/Application Support/RynthCore/NavData> (RYNTHNAV_NAVDATA)` and no navdata hint line.
- [ ] An outdoor RynthAi nav route that snags on a building or fence: the log shows a RynthNav `path request: N corners` line and RynthAi starting a detour, and the character walks round and rejoins the route.
- [ ] A goal with no route (e.g. inside a closed building): RynthAi says it found no walkable way and keeps its stuck escapes.
- [ ] RynthLua: a script using RynthAi's bridge (e.g. reading macro state) works with no "RynthAi isn't loaded" error.

**Verify:** user's live session; client log lines quoted in the ledger

**Steps:**

- [ ] **Step 1:** Ask the user whether they are available for a live check. Stop until they say yes.
- [ ] **Step 2:** Release build (`~/.dotnet/dotnet build AcDream.slnx -c Release`), user launches their client as usual.
- [ ] **Step 3:** User runs the three checks above; record the quoted log lines and the user's verdict in the ledger.

```json:metadata
{"files": [], "verifyCommand": "user live session on local ACE; log lines quoted", "acceptanceCriteria": ["ask the user before any client launch", "RynthNav Initialized line with the NavData folder from RYNTHNAV_NAVDATA, no hint line", "outdoor snag: RynthNav path request line, RynthAi detour, character walks round", "unroutable goal: no walkable way message, stuck escapes continue", "RynthLua RynthAi bridge works"], "modelTier": "standard", "userGate": true, "tags": ["user-gate"]}
```
