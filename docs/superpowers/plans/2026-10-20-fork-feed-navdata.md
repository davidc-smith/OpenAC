# Fork Release Feed and Launcher Navdata Install (A2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** OpenAC's launcher updates from the fork's own (Mac-only, locally built) releases and installs/keeps current the pinned RynthNav navdata, asking once.

**Architecture:** The navdata installer moves from `tools/RynthNavData` into a library (`src/AcDream.Rynth.NavData`) that still compiles RynthCore's own updater files unchanged; `AcDream.Launcher.Core` references it. The release repository is one MSBuild property read by the launcher and `publish-bin.ps1`. A release carries `rynth-navdata.json` beside `manifest.json` (fetched like the optional fingerprint), with the pin built into the launcher as fallback. A new `RynthNavDataViewModel` drives a banner, a progress modal and a menu item.

**Tech Stack:** .NET 10, C#, Avalonia 12 (launcher), xunit v3, PowerShell 7, `gh`.

**Spec:** `docs/superpowers/specs/2026-10-20-fork-feed-navdata-design.md`

## Global Constraints

- RynthCore source is compiled unchanged (linked `Compile Include`), never edited or copied.
- `manifest.json` stays exactly as today (strict parser: no new members). The navdata pin is a separate sibling file `rynth-navdata.json`.
- Navdata folder: `RYNTHNAV_NAVDATA` if set, else `<RynthInstallPaths.CoreDir>/NavData` with `RYNTHCORE_DIR` defaulting to `<ApplicationData>/RynthCore` off Windows (same as `src/AcDream.Rynth.Host/RynthInstallFolders.cs`). Cache: `~/Library/Caches/RynthCore/updates` on macOS, `<LocalApplicationData>/RynthCore/updates` elsewhere.
- Release repository: `davidc-smith/OpenAC`, defined once in `Directory.Build.props` as `OpenACReleaseRepository`.
- Repository version becomes `0.1.25-fork.1`.
- Release pin document: `{"schemaVersion":1,"url","size","sha256","files","bytes","id","rynthSuite","pinned"}`, camelCase, parsed strictly (unknown member = invalid), optional (any failure = no release pin).
- The release pin wins over the built-in pin when present.
- A navdata install runs only with no launcher session running, under `UpdateSessionBarrier.AcquireExclusive()`, and blocks launches for its duration.
- Banner text: `RynthNav navdata — 1.6 GB download, 4.7 GB on disk` (sizes computed from the pin: `size`, `bytes`, one decimal, 1 GB = 10⁹ bytes).
- Agreement file: `launcher-navdata.json` in `ApplicationPathSet.ConfigDirectory`, atomic write, `{"agreed": true}`.
- `.NET` SDK: use `~/.dotnet/dotnet` (see repo notes). Lock files: after project-reference changes run `~/.dotnet/dotnet restore AcDream.slnx --force-evaluate`; keep the lock changes for projects you touched, revert unrelated RID-swap churn (`git checkout -- <file>`); never run `tools/update-package-locks.ps1`.
- Commit messages end with `Claude-Session: https://claude.ai/code/session_011FtdjFB5dEVWdBQnZmstpz`.
- Never launch a game client for testing; the live check (Task 7) is the user's.

**User decisions (already made):**
- "OpenAC launcher" (not the user's ACE launcher) installs the navdata.
- "we have our own launcher that gets updates from ourselves until we merge with main" → fork feed `davidc-smith/OpenAC`.
- "keep it local and Mac only for now. But will need to do option 2 [GitHub Actions] when we ready to release" → local osx-arm64 release script; Actions out of scope.
- "Ask once, then keep current" install policy.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/AcDream.Rynth.NavData/AcDream.Rynth.NavData.csproj` | Library; links RynthCore's updater files unchanged |
| `src/AcDream.Rynth.NavData/NavDataPin.cs` | Pin record (moved from tool, now public) + JSON load/parse |
| `src/AcDream.Rynth.NavData/NavDataFolders.cs` | NavData + cache folders (moved, public) |
| `src/AcDream.Rynth.NavData/NavDataInstaller.cs` | Install exactly the pin via RynthCore's `UpdateNavDataAsync` (moved, public, own progress type) |
| `src/AcDream.Rynth.NavData/NavDataProgress.cs` | Our progress record (stage, percent, detail) so RynthCore types don't leak |
| `src/AcDream.Rynth.NavData/NavDataInspector.cs` | Read-only state check (Missing/Current/Outdated/LocalMarker/RynthCoreManaged) |
| `tools/RynthNavData/*` | CLI + pinner only; references the library |
| `src/AcDream.Launcher.Core/NavData/ReleaseNavDataPin.cs` | Strict parse of the release's `rynth-navdata.json`; built-in pin loader |
| `src/AcDream.Launcher.Core/NavData/LauncherNavDataConsentStore.cs` | Agreement file |
| `src/AcDream.Launcher.Core/NavData/LauncherNavDataService.cs` | Check (pin source + state) and guarded install |
| `src/AcDream.Launcher.Core/Updates/ReleaseManifestClient.cs` | Production URI from metadata; `FetchNavDataPinAsync` |
| `src/AcDream.Launcher/ViewModels/RynthNavDataViewModel.cs` | Banner, Not now, progress modal, auto-update after agreement |
| `src/AcDream.Launcher/ViewModels/LauncherWindowViewModel.NavData.cs` | Wiring into the window (startup, after client update, modal, launch block) |
| `src/AcDream.Launcher/MainWindow.axaml` | Banner, modal, menu item |
| `tools/publish-bin.ps1` | Base URL from property; writes `rynth-navdata.json` |
| `tools/launcher-fingerprint.ps1` | New inputs |
| `tools/publish-fork-release.ps1` | Local Mac-only fork release |
| `docs/launcher.md`, `docs/ci-and-releases.md` | User + release docs |

---

### Task 1: Navdata library extracted from the tool, with a state check

**Goal:** `src/AcDream.Rynth.NavData` holds the pin, folders, installer and a new read-only state inspector; the CLI keeps working on top of it.

**Files:**
- Create: `src/AcDream.Rynth.NavData/AcDream.Rynth.NavData.csproj`, `NavDataPin.cs`, `NavDataFolders.cs`, `NavDataInstaller.cs`, `NavDataProgress.cs`, `NavDataInspector.cs`, `packages.neutral.lock.json` (generated by restore)
- Delete: `tools/RynthNavData/NavDataPin.cs`, `NavDataFolders.cs`, `NavDataInstaller.cs`
- Modify: `tools/RynthNavData/AcDream.Tools.RynthNavData.csproj` (drop the RynthCore `Compile Include`s, add `ProjectReference` to the library), `tools/RynthNavData/Program.cs` (progress type), `tools/RynthNavData/NavDataPinner.cs` (only if it uses moved internals), `AcDream.slnx` (add the library + a test project)
- Create: `tests/AcDream.Rynth.NavData.Tests/AcDream.Rynth.NavData.Tests.csproj`, `tests/AcDream.Rynth.NavData.Tests/NavDataInspectorTests.cs`
- Modify: `tests/AcDream.Tools.RynthNavData.Tests/NavDataToolTests.cs` (namespace/usings only, if needed)

**Acceptance Criteria:**
- [ ] The library compiles RynthCore's `RynthUpdater.cs`, `RynthUpdater.NavData.cs`, `RynthUpdater.Available.cs`, `RynthUpdater.MapPacks.cs`, `DownloadProgress.cs`, `UpdateTrust.cs`, `RynthCore.Engine/Plugins/PluginContractVersion.cs`, `Shared/RynthInstallPaths.cs` via `Link`, with no edits under `external/`.
- [ ] Public API in namespace `AcDream.Rynth.NavData`: `NavDataPin`, `NavDataFolders`, `NavDataInstaller.InstallAsync(NavDataPin, string navDataDir, string cacheDir, IProgress<NavDataProgress>?, CancellationToken, bool force)`, `NavDataProgress`, `NavDataInspector.Inspect(NavDataPin, string navDataDir)` returning `NavDataInspection`.
- [ ] Inspector: no `navdata.id` → `Missing`; id == pin id → `Current`; 64-hex id ≠ pin id → `Outdated`; `navdata.local` present → `LocalMarker` (checked first); other non-empty id → `RynthCoreManaged`.
- [ ] `tests/AcDream.Tools.RynthNavData.Tests` still pass (10) and the new inspector tests pass.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.NavData.Tests && ~/.dotnet/dotnet test tests/AcDream.Tools.RynthNavData.Tests` → all pass

**Steps:**

- [ ] **Step 1: Create the library project**

`src/AcDream.Rynth.NavData/AcDream.Rynth.NavData.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <!-- RynthNav's navmesh tiles for OpenAC: the one archive pinned in external/rynth-navdata.json
       (our own SHA-256: the navdata follows our release, not their feed), installed with
       RynthCore's own launcher code compiled unchanged. Used by tools/RynthNavData and the
       OpenAC launcher. Their code, their warnings. -->
  <PropertyGroup>
    <RootNamespace>AcDream.Rynth.NavData</RootNamespace>
    <AssemblyName>AcDream.Rynth.NavData</AssemblyName>
    <IsPackable>false</IsPackable>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <RunAnalyzers>false</RunAnalyzers>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
    <RynthCoreSrc>$(MSBuildThisFileDirectory)..\..\external\RynthCore\src\</RynthCoreSrc>
  </PropertyGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="AcDream.Tools.RynthNavData" />
    <InternalsVisibleTo Include="AcDream.Tools.RynthNavData.Tests" />
    <InternalsVisibleTo Include="AcDream.Rynth.NavData.Tests" />
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

The tool's csproj loses its `RynthCoreSrc` property and `Compile Include` group and gains `<ProjectReference Include="..\..\src\AcDream.Rynth.NavData\AcDream.Rynth.NavData.csproj" />`. The tool's pinner (`NavDataPinner.cs`) may use `RynthUpdater` internals; the `InternalsVisibleTo` above covers it.

- [ ] **Step 2: Move the three files, make them public, add `NavDataProgress`**

`git mv tools/RynthNavData/{NavDataPin,NavDataFolders,NavDataInstaller}.cs src/AcDream.Rynth.NavData/`. Change `namespace AcDream.Tools.RynthNavData;` → `namespace AcDream.Rynth.NavData;`, `internal` → `public` on the types and their `Load`/`Save`/`NavData()`/`Cache()`/`InstallAsync` members. Keep `ToEntry()` `internal` (it returns a RynthCore type). Add to `NavDataPin`:

```csharp
    /// <summary>A pin document's bytes (the same camelCase shape <see cref="Load"/> reads).</summary>
    public static NavDataPin Parse(ReadOnlySpan<byte> utf8) =>
        JsonSerializer.Deserialize<NavDataPin>(utf8, Json)
        ?? throw new InvalidDataException("The navdata pin is empty.");
```

`src/AcDream.Rynth.NavData/NavDataProgress.cs`:

```csharp
namespace AcDream.Rynth.NavData;

public enum NavDataStage { Downloading, Verified, Unpacking, Installing }

/// <summary>One progress report from an install: RynthCore's DownloadProgress, in our own type.</summary>
public sealed record NavDataProgress(NavDataStage Stage, double? Percent, string Detail);
```

In `NavDataInstaller.InstallAsync` the parameter becomes `IProgress<NavDataProgress>? progress` and is adapted before calling `UpdateNavDataAsync`:

```csharp
        IProgress<DownloadProgress>? theirs = progress is null
            ? null
            : new Adapter(progress);
        return await updater.UpdateNavDataAsync(check, theirs, ct);
    }

    private sealed class Adapter(IProgress<NavDataProgress> ours) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) =>
            ours.Report(new NavDataProgress((NavDataStage)(int)value.Stage, value.Percent, value.Detail));
    }
```

(`DownloadStage` order is Downloading, Verified, Unpacking, Installing — same as `NavDataStage`; add a test asserting `Enum.GetNames` match.)

`tools/RynthNavData/Program.cs` install case: `new Progress<NavDataProgress>(p => Console.WriteLine($"  {p.Detail}"))`; add `using AcDream.Rynth.NavData;`.

- [ ] **Step 3: Write the failing inspector tests**

`tests/AcDream.Rynth.NavData.Tests/AcDream.Rynth.NavData.Tests.csproj` — copy `tests/AcDream.Tools.RynthNavData.Tests/AcDream.Tools.RynthNavData.Tests.csproj`, referencing `..\..\src\AcDream.Rynth.NavData\AcDream.Rynth.NavData.csproj` instead.

`tests/AcDream.Rynth.NavData.Tests/NavDataInspectorTests.cs`:

```csharp
using AcDream.Rynth.NavData;

namespace AcDream.Rynth.NavData.Tests;

public sealed class NavDataInspectorTests : IDisposable
{
    private const string PinId = "09b2a02d54e6240a0adf320c0e6b31f2843a2af303e78b3a7feacdab935b7a02";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "navdata-inspect-" + Guid.NewGuid().ToString("N"));
    private static readonly NavDataPin Pin = new("https://example.invalid/n.zip", 10, PinId, 3, 30, PinId, "e6fffb0f", "2026-10-19");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void NoFolderIsMissing() =>
        Assert.Equal(NavDataInstallState.Missing, NavDataInspector.Inspect(Pin, _dir).State);

    [Fact]
    public void MatchingIdIsCurrent()
    {
        Write("navdata.id", PinId.ToUpperInvariant() + "\n");
        Assert.Equal(NavDataInstallState.Current, NavDataInspector.Inspect(Pin, _dir).State);
    }

    [Fact]
    public void OtherOfOurIdsIsOutdated()
    {
        Write("navdata.id", new string('a', 64));
        Assert.Equal(NavDataInstallState.Outdated, NavDataInspector.Inspect(Pin, _dir).State);
    }

    [Fact]
    public void TheirLaunchersIdIsLeftToIt()
    {
        Write("navdata.id", new string('b', 32));
        Assert.Equal(NavDataInstallState.RynthCoreManaged, NavDataInspector.Inspect(Pin, _dir).State);
    }

    [Fact]
    public void LocalMarkerWinsOverEverything()
    {
        Write("navdata.id", PinId);
        Write("navdata.local", "");
        Assert.Equal(NavDataInstallState.LocalMarker, NavDataInspector.Inspect(Pin, _dir).State);
    }

    [Fact]
    public void InspectionCarriesTheFolderAndInstalledId()
    {
        Write("navdata.id", new string('a', 64));
        NavDataInspection inspection = NavDataInspector.Inspect(Pin, _dir);
        Assert.Equal(_dir, inspection.Directory);
        Assert.Equal(new string('a', 64), inspection.InstalledId);
    }

    [Fact]
    public void OurStagesMatchTheirs() =>
        Assert.Equal(
            Enum.GetNames<RynthCore.App.Avalonia.DownloadStage>(),
            Enum.GetNames<NavDataStage>());

    private void Write(string name, string text)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, name), text);
    }
}
```

- [ ] **Step 4: Run to see them fail**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.NavData.Tests`
Expected: build FAIL — `NavDataInspector` / `NavDataInstallState` not defined.

- [ ] **Step 5: Implement the inspector**

`src/AcDream.Rynth.NavData/NavDataInspector.cs`:

```csharp
using System;
using System.IO;
using RynthCore.App.Avalonia;

namespace AcDream.Rynth.NavData;

public enum NavDataInstallState { Missing, Current, Outdated, LocalMarker, RynthCoreManaged }

public sealed record NavDataInspection(NavDataInstallState State, string Directory, string InstalledId);

/// <summary>
/// What a NavData folder holds against a pin, without touching it. The same reading as
/// <see cref="NavDataInstaller"/>: navdata.local = the player's own tiles; our ids are the
/// archive SHA-256 (64 hex); any other id is RynthCore's launcher's (its feed ids).
/// </summary>
public static class NavDataInspector
{
    public static NavDataInspection Inspect(NavDataPin pin, string navDataDir)
    {
        ArgumentNullException.ThrowIfNull(pin);
        if (File.Exists(Path.Combine(navDataDir, RynthUpdater.NavDataLocalMarker)))
            return new(NavDataInstallState.LocalMarker, navDataDir, string.Empty);

        string idFile = Path.Combine(navDataDir, RynthUpdater.NavDataIdFile);
        string installed = File.Exists(idFile) ? File.ReadAllText(idFile).Trim().ToLowerInvariant() : string.Empty;
        NavDataInstallState state = installed.Length == 0 ? NavDataInstallState.Missing
            : installed == pin.Id.ToLowerInvariant() ? NavDataInstallState.Current
            : installed.Length == 64 ? NavDataInstallState.Outdated
            : NavDataInstallState.RynthCoreManaged;
        return new(state, navDataDir, installed);
    }
}
```

Add both projects to `AcDream.slnx` next to the other Rynth entries (`src/AcDream.Rynth.NavData/AcDream.Rynth.NavData.csproj`, `tests/AcDream.Rynth.NavData.Tests/AcDream.Rynth.NavData.Tests.csproj`). Run `~/.dotnet/dotnet restore AcDream.slnx --force-evaluate`; keep lock changes for the new projects and `tools/RynthNavData`, revert unrelated churn.

- [ ] **Step 6: Run the tests**

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.NavData.Tests && ~/.dotnet/dotnet test tests/AcDream.Tools.RynthNavData.Tests`
Expected: PASS (7 new, 10 existing). Also `~/.dotnet/dotnet run --project tools/RynthNavData -- install` prints "already has the pinned tiles" on this Mac (tiles are installed).

- [ ] **Step 7: Commit**

```bash
git add -A src/AcDream.Rynth.NavData tools/RynthNavData tests/AcDream.Rynth.NavData.Tests tests/AcDream.Tools.RynthNavData.Tests AcDream.slnx
git commit -m "refactor(rynth): navdata installer as a library, with a read-only state check

Claude-Session: https://claude.ai/code/session_011FtdjFB5dEVWdBQnZmstpz"
```

---

### Task 2: Fork release feed (one property, fork version)

**Goal:** The launcher's production feed and `publish-bin.ps1`'s asset URLs come from `OpenACReleaseRepository` = `davidc-smith/OpenAC`, and the version is `0.1.25-fork.1`.

**Files:**
- Modify: `Directory.Build.props` (property + version)
- Modify: `src/AcDream.Launcher.Core/AcDream.Launcher.Core.csproj` (assembly metadata)
- Modify: `src/AcDream.Launcher.Core/Updates/ReleaseManifestClient.cs:24-25`
- Modify: `tools/publish-bin.ps1:58-62`
- Modify: `tests/AcDream.Launcher.Core.Tests/Updates/ReleaseTransportTests.cs:96-99`

**Acceptance Criteria:**
- [ ] `ReleaseManifestClient.ProductionManifestUri` is `https://github.com/davidc-smith/OpenAC/releases/latest/download/manifest.json`, built from assembly metadata `OpenAC.ReleaseRepository`.
- [ ] `publish-bin.ps1` with no `-BaseUrl` uses `https://github.com/<OpenACReleaseRepository>/releases/download/v<Version>`.
- [ ] `Directory.Build.props` `<Version>` is `0.1.25-fork.1`.
- [ ] A test asserts the metadata equals the `Directory.Build.props` property (so the two can't drift).
- [ ] `tests/AcDream.Launcher.Core.Tests` passes with no new failures.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Launcher.Core.Tests --filter "FullyQualifiedName~ReleaseTransport"` → PASS

**Steps:**

- [ ] **Step 1: Update the failing test first**

In `ReleaseTransportTests.cs`, replace the literal assertion (lines ~96-99) and its comment with:

```csharp
        // The fork's own feed until it merges back upstream: one property in
        // Directory.Build.props (OpenACReleaseRepository) names the repository.
        Assert.Equal(
            "https://github.com/davidc-smith/OpenAC/releases/latest/download/manifest.json",
            ReleaseManifestClient.ProductionManifestUri.AbsoluteUri);
```

Add a new test in the same class:

```csharp
    [Fact]
    public void ProductionFeedRepositoryIsTheBuildProperty()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "AcDream.slnx")))
            root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("No repository root.");
        string property = System.Xml.Linq.XDocument.Load(Path.Combine(root, "Directory.Build.props"))
            .Descendants("OpenACReleaseRepository").Single().Value;
        Assert.Equal(property, ReleaseManifestClient.ReleaseRepository);
    }
```

- [ ] **Step 2: Run to see it fail**

Run: `~/.dotnet/dotnet test tests/AcDream.Launcher.Core.Tests --filter "FullyQualifiedName~ReleaseTransport"`
Expected: FAIL (build error: `ReleaseRepository` not defined).

- [ ] **Step 3: Implement**

`Directory.Build.props`, in the PropertyGroup holding `<Version>`: set `<Version>0.1.25-fork.1</Version>` and add

```xml
    <!-- The GitHub repository whose releases are the launcher's update feed and the
         publish script's asset addresses: the fork's own until it merges back upstream. -->
    <OpenACReleaseRepository>davidc-smith/OpenAC</OpenACReleaseRepository>
```

`src/AcDream.Launcher.Core/AcDream.Launcher.Core.csproj`:

```xml
  <ItemGroup>
    <AssemblyMetadata Include="OpenAC.ReleaseRepository" Value="$(OpenACReleaseRepository)" />
  </ItemGroup>
```

`ReleaseManifestClient.cs` (add `using System.Reflection;`):

```csharp
    /// <summary>owner/name of the repository whose releases are the update feed
    /// (Directory.Build.props OpenACReleaseRepository).</summary>
    public static string ReleaseRepository { get; } =
        typeof(ReleaseManifestClient).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "OpenAC.ReleaseRepository").Value
        ?? throw new InvalidOperationException("The launcher was built without a release repository.");

    public static Uri ProductionManifestUri { get; } = new(
        $"https://github.com/{ReleaseRepository}/releases/latest/download/manifest.json");
```

(`ReleaseRepository` must be declared before `ProductionManifestUri` — static initialisers run in text order.)

`publish-bin.ps1`, after `$repositoryVersion` is read:

```powershell
$releaseRepository = [string]$buildProperties.Project.PropertyGroup.OpenACReleaseRepository
if ([string]::IsNullOrWhiteSpace($releaseRepository)) {
    throw 'Directory.Build.props has no OpenACReleaseRepository.'
}
```

and the default base becomes `"https://github.com/$releaseRepository/releases/download/v$Version"`. (If `PropertyGroup` is an array in the XML, use `($buildProperties.Project.PropertyGroup | ForEach-Object { $_.OpenACReleaseRepository } | Where-Object { $_ } | Select-Object -First 1)` — check how `$repositoryVersion` is read and match it.)

- [ ] **Step 4: Run the tests**

Run: `~/.dotnet/dotnet test tests/AcDream.Launcher.Core.Tests --filter "FullyQualifiedName~ReleaseTransport"` → PASS.
Then `~/.dotnet/dotnet test tests/AcDream.Launcher.Core.Tests` and `~/.dotnet/dotnet test tests/AcDream.Launcher.Tests`: compare against `git stash`-free baseline by running the same on `main` first if anything fails (only failures also present on `main` are acceptable). Also grep the tests for `0.1.25"` literals tied to the repository version: `grep -rn '"0\.1\.25"' tests --include=*.cs` and fix any that read the repository version.

- [ ] **Step 5: Commit**

```bash
git add Directory.Build.props src/AcDream.Launcher.Core tools/publish-bin.ps1 tests/AcDream.Launcher.Core.Tests
git commit -m "feat(launcher): fork release feed from one OpenACReleaseRepository property; version 0.1.25-fork.1

Claude-Session: https://claude.ai/code/session_011FtdjFB5dEVWdBQnZmstpz"
```

---

### Task 3: Navdata pin travels with the release and the launcher

**Goal:** A release carries `rynth-navdata.json`; the launcher reads it strictly beside the manifest, falls back to its built-in pin, and the fingerprint covers what it now builds from.

**Files:**
- Modify: `src/AcDream.Launcher.Core/AcDream.Launcher.Core.csproj` (ProjectReference to `AcDream.Rynth.NavData`, EmbeddedResource `external/rynth-navdata.json` as `OpenAC.RynthNavDataPin.json`)
- Create: `src/AcDream.Launcher.Core/NavData/ReleaseNavDataPin.cs`
- Modify: `src/AcDream.Launcher.Core/Updates/ReleaseManifestClient.cs` (interface default + `FetchNavDataPinAsync`)
- Modify: `tools/publish-bin.ps1` (write `bin/rynth-navdata.json`)
- Modify: `tools/launcher-fingerprint.ps1` (`$LauncherOwnPaths` += `src/AcDream.Rynth.NavData`, `external/rynth-navdata.json`, `external/RynthCore`)
- Modify: `tests/AcDream.App.Tests/Release/LauncherFingerprintContractTests.cs` (expected inputs + a "pin change changes it" assertion)
- Create: `tests/AcDream.Launcher.Core.Tests/NavData/ReleaseNavDataPinTests.cs`
- Modify: launcher lock files touched by the new reference

**Acceptance Criteria:**
- [ ] `ReleaseNavDataPin.Parse` accepts the documented shape, rejects an unknown member, a non-64-hex `sha256`/`id`, `schemaVersion` ≠ 1, a non-HTTPS `url`, and non-positive `size`/`files`/`bytes` (throws `LauncherUpdateException`).
- [ ] `ReleaseNavDataPin.BuiltIn()` returns the pin embedded from `external/rynth-navdata.json` (test: equals `NavDataPin.Load` of that file).
- [ ] `ReleaseManifestClient.FetchNavDataPinAsync` fetches `new Uri(manifestUri, "rynth-navdata.json")` with the fingerprint's 5 s timeout and returns null on any failure (404, invalid, timeout).
- [ ] `publish-bin.ps1` writes `bin/rynth-navdata.json` = `schemaVersion: 1` + every field of `external/rynth-navdata.json`.
- [ ] Fingerprint contract test: the inputs include the three new paths; changing `external/rynth-navdata.json` changes the fingerprint.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Launcher.Core.Tests --filter "FullyQualifiedName~NavData" && ~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~LauncherFingerprintContract"` → PASS

**Steps:**

- [ ] **Step 1: Write the failing parse tests**

`tests/AcDream.Launcher.Core.Tests/NavData/ReleaseNavDataPinTests.cs`:

```csharp
using System.Text;
using AcDream.Launcher.Core.NavData;
using AcDream.Launcher.Core.Updates;
using AcDream.Rynth.NavData;

namespace AcDream.Launcher.Core.Tests.NavData;

public sealed class ReleaseNavDataPinTests
{
    private const string Sha = "09b2a02d54e6240a0adf320c0e6b31f2843a2af303e78b3a7feacdab935b7a02";

    private static string Document(string extra = "", string sha = Sha, int schema = 1, string url = "https://aelrynth.com/n.zip", long size = 1572337084) =>
        $$"""
        {"schemaVersion":{{schema}},"url":"{{url}}","size":{{size}},"sha256":"{{sha}}","files":40203,
         "bytes":4731616273,"id":"{{sha}}","rynthSuite":"e6fffb0f","pinned":"2026-10-19"{{extra}}}
        """;

    [Fact]
    public void ParsesTheDocumentedShape()
    {
        NavDataPin pin = ReleaseNavDataPin.Parse(Encoding.UTF8.GetBytes(Document()));
        Assert.Equal(Sha, pin.Sha256);
        Assert.Equal(40203, pin.Files);
        Assert.Equal(1572337084, pin.Size);
    }

    [Theory]
    [InlineData(",\"extra\":1")]
    public void RejectsAnUnknownMember(string extra) =>
        Assert.Throws<LauncherUpdateException>(() => ReleaseNavDataPin.Parse(Encoding.UTF8.GetBytes(Document(extra))));

    [Fact]
    public void RejectsABadHash() =>
        Assert.Throws<LauncherUpdateException>(() => ReleaseNavDataPin.Parse(Encoding.UTF8.GetBytes(Document(sha: "abc"))));

    [Fact]
    public void RejectsAnotherSchema() =>
        Assert.Throws<LauncherUpdateException>(() => ReleaseNavDataPin.Parse(Encoding.UTF8.GetBytes(Document(schema: 2))));

    [Fact]
    public void RejectsPlainHttp() =>
        Assert.Throws<LauncherUpdateException>(() => ReleaseNavDataPin.Parse(Encoding.UTF8.GetBytes(Document(url: "http://aelrynth.com/n.zip"))));

    [Fact]
    public void RejectsAZeroSize() =>
        Assert.Throws<LauncherUpdateException>(() => ReleaseNavDataPin.Parse(Encoding.UTF8.GetBytes(Document(size: 0))));

    [Fact]
    public void BuiltInPinIsTheRepositoryPin()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "AcDream.slnx")))
            root = Path.GetDirectoryName(root)!;
        Assert.Equal(NavDataPin.Load(Path.Combine(root, "external", "rynth-navdata.json")), ReleaseNavDataPin.BuiltIn());
    }
}
```

Add a fetch test beside the existing fingerprint-fetch tests (find them with `grep -rn "FetchLauncherFingerprintAsync" tests/AcDream.Launcher.Core.Tests`), using the same loopback fixture they use: one case serving a valid document (returns the pin), one serving 404 (returns null), one serving `{"schemaVersion":2}` (returns null). Copy the fixture setup from the nearest fingerprint test verbatim, changing the served path to `rynth-navdata.json`.

- [ ] **Step 2: Run to see them fail**

Run: `~/.dotnet/dotnet test tests/AcDream.Launcher.Core.Tests --filter "FullyQualifiedName~NavData"`
Expected: build FAIL — `ReleaseNavDataPin` not defined.

- [ ] **Step 3: Implement**

`AcDream.Launcher.Core.csproj`:

```xml
    <ProjectReference Include="..\AcDream.Rynth.NavData\AcDream.Rynth.NavData.csproj" />
```
```xml
  <ItemGroup>
    <!-- The navdata pin this launcher was built with: the fallback when a release has no
         rynth-navdata.json beside its manifest. -->
    <EmbeddedResource Include="..\..\external\rynth-navdata.json" LogicalName="OpenAC.RynthNavDataPin.json" />
  </ItemGroup>
```

`src/AcDream.Launcher.Core/NavData/ReleaseNavDataPin.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using AcDream.Launcher.Core.Updates;
using AcDream.Rynth.NavData;

namespace AcDream.Launcher.Core.NavData;

/// <summary>
/// The RynthNav navdata pin a release carries beside manifest.json (rynth-navdata.json), read as
/// strictly as the manifest, and the pin this launcher was built with.
/// </summary>
public static class ReleaseNavDataPin
{
    public const string FileName = "rynth-navdata.json";
    public const int CurrentSchemaVersion = 1;
    private const string BuiltInResource = "OpenAC.RynthNavDataPin.json";

    private static readonly JsonSerializerOptions Strict = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 4,
    };

    public static NavDataPin Parse(ReadOnlySpan<byte> utf8)
    {
        Document? document;
        try
        {
            document = JsonSerializer.Deserialize<Document>(utf8, Strict);
        }
        catch (JsonException ex)
        {
            throw new LauncherUpdateException($"The navdata pin is invalid: {ex.Message}", ex);
        }

        if (document is null
            || document.SchemaVersion != CurrentSchemaVersion
            || !Uri.TryCreate(document.Url, UriKind.Absolute, out Uri? url) || url.Scheme != Uri.UriSchemeHttps
            || document.Size <= 0 || document.Files <= 0 || document.Bytes <= 0
            || !IsSha256(document.Sha256) || !IsSha256(document.Id)
            || string.IsNullOrWhiteSpace(document.RynthSuite) || string.IsNullOrWhiteSpace(document.Pinned))
        {
            throw new LauncherUpdateException("The navdata pin is invalid.");
        }

        return new NavDataPin(document.Url!, document.Size, document.Sha256!.ToLowerInvariant(), document.Files,
            document.Bytes, document.Id!.ToLowerInvariant(), document.RynthSuite!, document.Pinned!);
    }

    public static NavDataPin BuiltIn()
    {
        using Stream stream = typeof(ReleaseNavDataPin).Assembly.GetManifestResourceStream(BuiltInResource)
            ?? throw new InvalidOperationException("The launcher was built without a navdata pin.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return NavDataPin.Parse(memory.ToArray());
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private sealed record Document(
        int SchemaVersion, string? Url, long Size, string? Sha256, int Files, long Bytes,
        string? Id, string? RynthSuite, string? Pinned);
}
```

`ReleaseManifestClient.cs`: in `IReleaseManifestClient` add

```csharp
    /// <summary>The navdata pin published beside the manifest, or null when the release has
    /// none or it cannot be read; the launcher then uses its built-in pin.</summary>
    Task<NavDataPin?> FetchNavDataPinAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<NavDataPin?>(null);
```

and in the class, mirroring `FetchLauncherFingerprintAsync` exactly (same timeout property, same three catch clauses):

```csharp
    public async Task<NavDataPin?> FetchNavDataPinAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(FingerprintTimeout);
        try
        {
            byte[] bytes = await FetchBytesAsync(
                    new Uri(_manifestUri, ReleaseNavDataPin.FileName), "navdata pin", timeout.Token)
                .ConfigureAwait(false);
            return ReleaseNavDataPin.Parse(bytes);
        }
        // Optional, like the fingerprint: a release without one, or one that is slow,
        // unreachable or unreadable, leaves the launcher on its built-in pin.
        catch (LauncherUpdateException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return null;
        }
    }
```

`publish-bin.ps1`, after the fingerprint document is written:

```powershell
# Beside manifest.json for the same reason as the fingerprint: the RynthNav navdata pin
# this release was tested with. The launcher installs exactly this archive.
$navDataPin = Get-Content -LiteralPath (Join-Path $RepoRoot 'external/rynth-navdata.json') -Raw | ConvertFrom-Json
$navDataDocument = [ordered]@{ schemaVersion = 1 }
foreach ($property in $navDataPin.PSObject.Properties) { $navDataDocument[$property.Name] = $property.Value }
[IO.File]::WriteAllText(
    (Join-Path $BinRoot 'rynth-navdata.json'),
    (($navDataDocument | ConvertTo-Json) + "`n"),
    [Text.UTF8Encoding]::new($false))
```

`launcher-fingerprint.ps1` `$LauncherOwnPaths`: add `'src/AcDream.Rynth.NavData'`, `'external/rynth-navdata.json'`, `'external/RynthCore'` (a submodule path resolves to its commit through `rev-parse`). Comment above the list: append "and the navdata library with the RynthCore source and pin it builds from".

`LauncherFingerprintContractTests.cs`: add the three paths to the expected-inputs list in `TheInputsAreTheLaunchersOwnCodeTheRecipeAndTheBuildNotTheGameCode`, and in `GameCodeAndTheReleaseVersionLeaveItAndTheLauncherOrTheRecipeChangeIt` add

```csharp
        Assert.NotEqual(fingerprint, Fingerprint(WithFile(head, "external/rynth-navdata.json",
            Git("show", "HEAD:external/rynth-navdata.json").Replace("2026-10-19", "2026-10-20"))));
```

Run `~/.dotnet/dotnet restore AcDream.slnx --force-evaluate`; keep the Launcher/Launcher.Core/test lock changes that add `AcDream.Rynth.NavData`, revert unrelated churn.

- [ ] **Step 4: Run the tests**

Run the Verify command → PASS. Also `~/.dotnet/dotnet test tests/AcDream.Launcher.Tests --filter "FullyQualifiedName~LauncherProjectBoundary"` → PASS (Launcher still references only Launcher.Core).

- [ ] **Step 5: Commit**

```bash
git add -A src/AcDream.Launcher.Core tools/publish-bin.ps1 tools/launcher-fingerprint.ps1 tests/AcDream.Launcher.Core.Tests tests/AcDream.App.Tests/Release src/AcDream.Launcher/packages.*.lock.json tests/AcDream.Launcher.Tests/packages.*.lock.json
git commit -m "feat(launcher): navdata pin beside the release manifest, built-in fallback, fingerprint inputs

Claude-Session: https://claude.ai/code/session_011FtdjFB5dEVWdBQnZmstpz"
```

---

### Task 4: Launcher navdata service and agreement store

**Goal:** `LauncherNavDataService` reports which pin applies and what the folder holds, and installs it only with no session running under the exclusive session lease; `LauncherNavDataConsentStore` remembers the agreement.

**Files:**
- Create: `src/AcDream.Launcher.Core/NavData/LauncherNavDataConsentStore.cs`
- Create: `src/AcDream.Launcher.Core/NavData/LauncherNavDataService.cs`
- Create: `tests/AcDream.Launcher.Core.Tests/NavData/LauncherNavDataServiceTests.cs`
- Create: `tests/AcDream.Launcher.Core.Tests/NavData/LauncherNavDataConsentStoreTests.cs`

**Acceptance Criteria:**
- [ ] `CheckAsync` returns `NavDataCheck(Pin, Source: Release|BuiltIn, Inspection)`; release pin used when `FetchNavDataPinAsync` returns one, else built-in.
- [ ] `InstallAsync` throws `LauncherUpdateException("Stop every launcher session before installing the RynthNav navdata.")` when `hasRunningSessions()` is true, before and after taking the exclusive lease (same double check as `LauncherUpdater.InstallClientAsync`).
- [ ] `InstallAsync` passes the check's pin, directory and the cache directory to the installer delegate and returns its message; progress is forwarded.
- [ ] Consent store: missing file → not agreed; `Agree()` writes `{"agreed": true}` atomically; a corrupt file reads as not agreed (no throw).

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Launcher.Core.Tests --filter "FullyQualifiedName~NavData"` → PASS

**Steps:**

- [ ] **Step 1: Write the failing tests**

`LauncherNavDataConsentStoreTests.cs`:

```csharp
using AcDream.Launcher.Core.NavData;

namespace AcDream.Launcher.Core.Tests.NavData;

public sealed class LauncherNavDataConsentStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "navdata-consent-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void NoFileIsNotAgreed() =>
        Assert.False(new LauncherNavDataConsentStore(Path.Combine(_dir, LauncherNavDataConsentStore.FileName)).IsAgreed);

    [Fact]
    public void AgreeIsRemembered()
    {
        string path = Path.Combine(_dir, LauncherNavDataConsentStore.FileName);
        new LauncherNavDataConsentStore(path).Agree();
        Assert.True(new LauncherNavDataConsentStore(path).IsAgreed);
        Assert.Contains("\"agreed\": true", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void CorruptFileIsNotAgreed()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, LauncherNavDataConsentStore.FileName);
        File.WriteAllText(path, "{not json");
        Assert.False(new LauncherNavDataConsentStore(path).IsAgreed);
    }
}
```

`LauncherNavDataServiceTests.cs`:

```csharp
using AcDream.Launcher.Core.NavData;
using AcDream.Launcher.Core.Updates;
using AcDream.Rynth.NavData;

namespace AcDream.Launcher.Core.Tests.NavData;

public sealed class LauncherNavDataServiceTests : IDisposable
{
    private const string A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string B = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static readonly NavDataPin BuiltIn = new("https://x.invalid/a.zip", 10, A, 1, 10, A, "s", "2026-10-19");
    private static readonly NavDataPin Released = new("https://x.invalid/b.zip", 20, B, 2, 20, B, "s", "2026-10-20");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "navdata-service-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private LauncherNavDataService Service(NavDataPin? releasePin, Func<bool>? running = null,
        LauncherNavDataService.Installer? installer = null) =>
        new(new FakeManifestClient(releasePin), () => BuiltIn,
            navDataDirectory: Path.Combine(_root, "NavData"),
            cacheDirectory: Path.Combine(_root, "cache"),
            barrier: new UpdateSessionBarrier(Path.Combine(_root, "data")),
            hasRunningSessions: running ?? (() => false),
            installer: installer ?? ((_, _, _, _, _) => Task.FromResult("installed")));

    [Fact]
    public async Task ReleasePinWins()
    {
        NavDataCheck check = await Service(Released).CheckAsync();
        Assert.Equal(Released, check.Pin);
        Assert.Equal(NavDataPinSource.Release, check.Source);
        Assert.Equal(NavDataInstallState.Missing, check.Inspection.State);
    }

    [Fact]
    public async Task BuiltInPinWithoutARelease()
    {
        NavDataCheck check = await Service(null).CheckAsync();
        Assert.Equal(BuiltIn, check.Pin);
        Assert.Equal(NavDataPinSource.BuiltIn, check.Source);
    }

    [Fact]
    public async Task InstallRefusesWhileASessionRuns()
    {
        LauncherNavDataService service = Service(null, running: () => true);
        NavDataCheck check = await service.CheckAsync();
        var error = await Assert.ThrowsAsync<LauncherUpdateException>(() => service.InstallAsync(check, null));
        Assert.Contains("Stop every launcher session", error.Message);
    }

    [Fact]
    public async Task InstallPassesThePinFolderAndCache()
    {
        (NavDataPin, string, string)? seen = null;
        LauncherNavDataService service = Service(Released, installer: (pin, dir, cache, _, _) =>
        {
            seen = (pin, dir, cache);
            return Task.FromResult("done");
        });
        NavDataCheck check = await service.CheckAsync();
        Assert.Equal("done", await service.InstallAsync(check, null));
        Assert.Equal((Released, Path.Combine(_root, "NavData"), Path.Combine(_root, "cache")), seen);
    }

    private sealed class FakeManifestClient(NavDataPin? pin) : IReleaseManifestClient
    {
        public Task<ReleaseManifest> FetchAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<NavDataPin?> FetchNavDataPinAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(pin);
    }
}
```

- [ ] **Step 2: Run to see them fail**

Run: `~/.dotnet/dotnet test tests/AcDream.Launcher.Core.Tests --filter "FullyQualifiedName~NavData"`
Expected: build FAIL — types not defined.

- [ ] **Step 3: Implement**

`LauncherNavDataConsentStore.cs`:

```csharp
using System.Text.Json;
using AcDream.Platform;

namespace AcDream.Launcher.Core.NavData;

/// <summary>Whether the player agreed to the RynthNav navdata download: asked once, then kept current.</summary>
public sealed class LauncherNavDataConsentStore
{
    public const string FileName = "launcher-navdata.json";
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public LauncherNavDataConsentStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
    }

    public string FilePath { get; }

    public static LauncherNavDataConsentStore ForApplicationPaths(ApplicationPathSet paths) =>
        new(Path.Combine(paths.ConfigDirectory, FileName));

    /// <summary>A missing or unreadable file means not agreed: the banner simply asks again.</summary>
    public bool IsAgreed
    {
        get
        {
            try
            {
                return File.Exists(FilePath)
                    && JsonSerializer.Deserialize<Consent>(File.ReadAllBytes(FilePath), Options)?.Agreed == true;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public void Agree()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(new Consent(true), Options));
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private sealed record Consent(bool Agreed);
}
```

`LauncherNavDataService.cs`:

```csharp
using AcDream.Launcher.Core.Updates;
using AcDream.Rynth.NavData;

namespace AcDream.Launcher.Core.NavData;

public enum NavDataPinSource { Release, BuiltIn }

public sealed record NavDataCheck(NavDataPin Pin, NavDataPinSource Source, NavDataInspection Inspection);

/// <summary>
/// The RynthNav navdata the client's RynthNav reads: which pin applies (the release's, else the
/// one built in) and installing exactly it. The folder swap would pull tiles out from under a
/// running client, so an install runs only with no session, under the exclusive session lease.
/// </summary>
public sealed class LauncherNavDataService
{
    public delegate Task<string> Installer(NavDataPin pin, string navDataDirectory, string cacheDirectory,
        IProgress<NavDataProgress>? progress, CancellationToken cancellationToken);

    private readonly IReleaseManifestClient _manifestClient;
    private readonly Func<NavDataPin> _builtInPin;
    private readonly UpdateSessionBarrier _barrier;
    private readonly Func<bool> _hasRunningSessions;
    private readonly Installer _installer;

    public LauncherNavDataService(
        IReleaseManifestClient manifestClient,
        Func<NavDataPin> builtInPin,
        string navDataDirectory,
        string cacheDirectory,
        UpdateSessionBarrier barrier,
        Func<bool> hasRunningSessions,
        Installer? installer = null)
    {
        _manifestClient = manifestClient ?? throw new ArgumentNullException(nameof(manifestClient));
        _builtInPin = builtInPin ?? throw new ArgumentNullException(nameof(builtInPin));
        NavDataDirectory = navDataDirectory;
        CacheDirectory = cacheDirectory;
        _barrier = barrier ?? throw new ArgumentNullException(nameof(barrier));
        _hasRunningSessions = hasRunningSessions ?? throw new ArgumentNullException(nameof(hasRunningSessions));
        _installer = installer ?? ((pin, dir, cache, progress, ct) =>
            NavDataInstaller.InstallAsync(pin, dir, cache, progress, ct));
    }

    public string NavDataDirectory { get; }

    public string CacheDirectory { get; }

    /// <summary>The service the launcher uses: the client's own folders.</summary>
    public static LauncherNavDataService ForThisMachine(IReleaseManifestClient manifestClient,
        UpdateSessionBarrier barrier, Func<bool> hasRunningSessions) =>
        new(manifestClient, ReleaseNavDataPin.BuiltIn, NavDataFolders.NavData(), NavDataFolders.Cache(),
            barrier, hasRunningSessions);

    public async Task<NavDataCheck> CheckAsync(CancellationToken cancellationToken = default)
    {
        NavDataPin? released = await _manifestClient.FetchNavDataPinAsync(cancellationToken).ConfigureAwait(false);
        NavDataPin pin = released ?? _builtInPin();
        return new NavDataCheck(pin, released is null ? NavDataPinSource.BuiltIn : NavDataPinSource.Release,
            NavDataInspector.Inspect(pin, NavDataDirectory));
    }

    public async Task<string> InstallAsync(NavDataCheck check, IProgress<NavDataProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(check);
        RefuseRunningSessions();
        using UpdateSessionBarrier.ExclusiveLease lease = _barrier.AcquireExclusive();
        RefuseRunningSessions();
        return await _installer(check.Pin, NavDataDirectory, CacheDirectory, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    private void RefuseRunningSessions()
    {
        if (_hasRunningSessions())
            throw new LauncherUpdateException("Stop every launcher session before installing the RynthNav navdata.");
    }
}
```

Check `UpdateSessionBarrier`'s constructor argument (`dataDirectory`) and `ExclusiveLease` disposal against `src/AcDream.Launcher.Core/Updates/UpdateSessionBarrier.cs`; if `AcquireExclusive` needs the directory to exist, create `_root/data` in the test helper.

- [ ] **Step 4: Run the tests**

Run the Verify command → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Launcher.Core/NavData tests/AcDream.Launcher.Core.Tests/NavData
git commit -m "feat(launcher): navdata service (pin source, state, guarded install) and agreement store

Claude-Session: https://claude.ai/code/session_011FtdjFB5dEVWdBQnZmstpz"
```

---

### Task 5: Launcher UI — ask once, then keep current

**Goal:** The launcher shows the navdata banner, installs with a cancellable progress modal, remembers the agreement, updates on its own afterwards, offers a menu item, and blocks launches during an install.

**Files:**
- Create: `src/AcDream.Launcher/ViewModels/RynthNavDataViewModel.cs`
- Create: `src/AcDream.Launcher/ViewModels/LauncherWindowViewModel.NavData.cs`
- Modify: `src/AcDream.Launcher/ViewModels/LauncherWindowViewModel.cs` (constructor parameter, `IsModalOpen`, startup sequence, `OnClientVersionChanged`, `CanLaunch`)
- Modify: `src/AcDream.Launcher/ViewModels/LauncherWindowViewModel.Accounts.cs:299-301` (row launch block)
- Modify: `src/AcDream.Launcher/App.axaml.cs:114-121` (compose service + consent store)
- Modify: `src/AcDream.Launcher/LauncherUpdateComposition.cs` (expose the manifest client as `IReleaseManifestClient? ManifestClient`)
- Modify: `src/AcDream.Launcher/MainWindow.axaml` (banner after the update banner ~line 160; modal beside the update prompt's; menu button in the "Installation & updates" flyout ~line 463)
- Create: `tests/AcDream.Launcher.Tests/RynthNavDataViewModelTests.cs`
- Modify: `tests/AcDream.Launcher.Tests/MainWindowViewTests.cs` (bindings exist)

**Acceptance Criteria:**
- [ ] Banner shown when state is `Missing`/`Outdated`, not agreed, not dismissed this run; text `RynthNav navdata — 1.6 GB download, 4.7 GB on disk` for the current pin; hidden for `Current`/`LocalMarker`/`RynthCoreManaged`.
- [ ] **Install** records the agreement, opens the modal, runs the service install with progress (percent or indeterminate + detail), shows the result message, and re-checks; **Cancel** cancels it; a failure shows the error and leaves the banner.
- [ ] **Not now** hides the banner for this run only (agreement unchanged).
- [ ] When agreed and state is `Missing`/`Outdated`, the check after startup and after a client version change installs on its own if no session runs; with a session running it does nothing (next startup/client update retries).
- [ ] Menu **Install / update RynthNav navdata** runs an install (recording agreement) regardless of the banner; disabled while busy.
- [ ] While installing: `IsModalOpen` true and row launches show "Installing RynthNav navdata…".
- [ ] With no service (tests/older composition) the view model is inert (no banner, no crash).
- [ ] `tests/AcDream.Launcher.Tests` has no new failures.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Launcher.Tests --filter "FullyQualifiedName~RynthNavData|FullyQualifiedName~MainWindowView"` → PASS

**Steps:**

- [ ] **Step 1: Write the failing view-model tests**

Follow the patterns in `tests/AcDream.Launcher.Tests/LauncherUpdateViewModelTests.cs` for the dispatcher fake (find the immediate `IUiDispatcher` used there and reuse it). `RynthNavDataViewModelTests.cs`:

```csharp
using AcDream.Launcher.Core.NavData;
using AcDream.Launcher.Core.Updates;
using AcDream.Launcher.ViewModels;
using AcDream.Rynth.NavData;

namespace AcDream.Launcher.Tests;

public sealed class RynthNavDataViewModelTests : IDisposable
{
    private const string A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly NavDataPin Pin = new("https://x.invalid/a.zip", 1572337084, A, 40203, 4731616273, A, "s", "2026-10-19");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "navdata-vm-" + Guid.NewGuid().ToString("N"));
    private int _installs;
    private bool _sessionRunning;
    private TaskCompletionSource<string>? _pendingInstall;

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string NavDataDir => Path.Combine(_root, "NavData");

    private LauncherNavDataConsentStore Consent => new(Path.Combine(_root, "config", LauncherNavDataConsentStore.FileName));

    private RynthNavDataViewModel Create() =>
        new(new LauncherNavDataService(new PinClient(), () => Pin, NavDataDir, Path.Combine(_root, "cache"),
                new UpdateSessionBarrier(Path.Combine(_root, "data")), () => _sessionRunning,
                (_, dir, _, progress, ct) =>
                {
                    _installs++;
                    progress?.Report(new NavDataProgress(NavDataStage.Downloading, 50, "half"));
                    if (_pendingInstall is { } pending) return pending.Task.WaitAsync(ct);
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, "navdata.id"), A);
                    return Task.FromResult("installed");
                }),
            Consent, new ImmediateDispatcher(), canStartInstall: () => !_sessionRunning);

    [Fact]
    public async Task MissingAndNotAgreedShowsTheBanner()
    {
        RynthNavDataViewModel vm = Create();
        await vm.CheckAsync();
        Assert.True(vm.ShowBanner);
        Assert.Equal("RynthNav navdata — 1.6 GB download, 4.7 GB on disk", vm.BannerText);
        Assert.Equal(0, _installs);
    }

    [Fact]
    public async Task NotNowHidesForThisRunOnly()
    {
        RynthNavDataViewModel vm = Create();
        await vm.CheckAsync();
        vm.NotNowCommand.Execute(null);
        Assert.False(vm.ShowBanner);
        Assert.False(Consent.IsAgreed);
        await vm.CheckAsync();
        Assert.False(vm.ShowBanner);
        RynthNavDataViewModel next = Create();
        await next.CheckAsync();
        Assert.True(next.ShowBanner);
    }

    [Fact]
    public async Task InstallAgreesInstallsAndHidesTheBanner()
    {
        RynthNavDataViewModel vm = Create();
        await vm.CheckAsync();
        await vm.InstallCommand.ExecuteAsync(null);
        Assert.True(Consent.IsAgreed);
        Assert.Equal(1, _installs);
        Assert.False(vm.ShowBanner);
        Assert.Equal("installed", vm.Status);
    }

    [Fact]
    public async Task AgreedAndMissingInstallsOnItsOwn()
    {
        Consent.Agree();
        RynthNavDataViewModel vm = Create();
        await vm.CheckAsync();
        Assert.Equal(1, _installs);
        Assert.False(vm.ShowBanner);
    }

    [Fact]
    public async Task AgreedButASessionRunsWaits()
    {
        Consent.Agree();
        _sessionRunning = true;
        RynthNavDataViewModel vm = Create();
        await vm.CheckAsync();
        Assert.Equal(0, _installs);
        Assert.False(vm.ShowBanner);
    }

    [Fact]
    public async Task CurrentTilesNeedNothing()
    {
        Directory.CreateDirectory(NavDataDir);
        File.WriteAllText(Path.Combine(NavDataDir, "navdata.id"), A);
        RynthNavDataViewModel vm = Create();
        await vm.CheckAsync();
        Assert.False(vm.ShowBanner);
        Assert.Equal(0, _installs);
    }

    [Fact]
    public async Task BusyWhileInstallingAndCancelStopsIt()
    {
        _pendingInstall = new TaskCompletionSource<string>();
        RynthNavDataViewModel vm = Create();
        await vm.CheckAsync();
        Task running = vm.InstallCommand.ExecuteAsync(null);
        Assert.True(vm.IsBusy);
        Assert.True(vm.IsOpen);
        Assert.Equal(50, vm.ProgressPercent);
        vm.CancelCommand.Execute(null);
        await running;
        Assert.False(vm.IsBusy);
        Assert.True(vm.HasError);
        Assert.True(vm.ShowBanner);
    }

    [Fact]
    public async Task WithoutAServiceItIsInert()
    {
        var vm = new RynthNavDataViewModel(null, null, new ImmediateDispatcher(), () => true);
        await vm.CheckAsync();
        Assert.False(vm.ShowBanner);
        Assert.False(vm.InstallCommand.CanExecute(null));
    }

    private sealed class PinClient : IReleaseManifestClient
    {
        public Task<ReleaseManifest> FetchAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
```

(Sizes: 1572337084 / 10⁹ = 1.57 → "1.6 GB"; 4731616273 / 10⁹ = 4.73 → "4.7 GB".) If the dispatcher fake in `LauncherUpdateViewModelTests` has a different name, use it and drop `ImmediateDispatcher`; otherwise add to the test file:

```csharp
internal sealed class ImmediateDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}
```

(match `IUiDispatcher`'s actual members).

- [ ] **Step 2: Run to see them fail**

Run: `~/.dotnet/dotnet test tests/AcDream.Launcher.Tests --filter "FullyQualifiedName~RynthNavData"`
Expected: build FAIL — `RynthNavDataViewModel` not defined.

- [ ] **Step 3: Implement the view model**

`src/AcDream.Launcher/ViewModels/RynthNavDataViewModel.cs` — mirror `LauncherUpdateViewModel`'s style (`ObservableObject`, `SetProperty`, `AsyncRelayCommand`, `RelayCommand`, dispatcher-posted progress via its `UiProgress<T>` helper if accessible, else `new Progress<T>` posted through the dispatcher):

```csharp
using System.Globalization;
using AcDream.Launcher.Core.NavData;
using AcDream.Rynth.NavData;

namespace AcDream.Launcher.ViewModels;

/// <summary>
/// The RynthNav navdata the client's navigation reads: a banner offering the download once, then
/// kept current on its own (at startup and after a client update), and a progress dialog.
/// </summary>
public sealed class RynthNavDataViewModel : ObservableObject
{
    private readonly LauncherNavDataService? _service;
    private readonly LauncherNavDataConsentStore? _consent;
    private readonly IUiDispatcher _dispatcher;
    private readonly Func<bool> _canStartInstall;
    private NavDataCheck? _check;
    private bool _dismissed;
    private bool _isBusy;
    private bool _isOpen;
    private string _status = string.Empty;
    private string? _error;
    private double _progressPercent;
    private bool _isProgressIndeterminate = true;
    private CancellationTokenSource? _cancellation;

    public RynthNavDataViewModel(LauncherNavDataService? service, LauncherNavDataConsentStore? consent,
        IUiDispatcher dispatcher, Func<bool> canStartInstall)
    {
        _service = service;
        _consent = consent;
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _canStartInstall = canStartInstall ?? throw new ArgumentNullException(nameof(canStartInstall));
        InstallCommand = new AsyncRelayCommand(InstallAsync, () => _service is not null && !IsBusy);
        NotNowCommand = new RelayCommand(NotNow, () => !IsBusy);
        CancelCommand = new RelayCommand(() => _cancellation?.Cancel(), () => IsBusy);
        CloseCommand = new RelayCommand(() => IsOpen = false, () => !IsBusy);
    }

    public AsyncRelayCommand InstallCommand { get; }
    public RelayCommand NotNowCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand CloseCommand { get; }

    /// <summary>Offered until agreed; after that only when an install failed.</summary>
    public bool ShowBanner => !_dismissed && !IsBusy && NeedsInstall && (_consent?.IsAgreed != true || HasError);

    public string BannerText => _check is null
        ? string.Empty
        : $"RynthNav navdata — {Gb(_check.Pin.Size)} download, {Gb(_check.Pin.Bytes)} on disk";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(ShowBanner));
                NotifyCommandStates();
            }
        }
    }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string? Error
    {
        get => _error;
        private set
        {
            if (SetProperty(ref _error, value))
            {
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(ShowBanner));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetProperty(ref _progressPercent, value);
    }

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set => SetProperty(ref _isProgressIndeterminate, value);
    }

    private bool NeedsInstall => _check?.Inspection.State is NavDataInstallState.Missing or NavDataInstallState.Outdated;

    /// <summary>Reads the pin and the folder; once agreed, an install that is due starts on its own.</summary>
    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        if (_service is null || IsBusy)
            return;
        try
        {
            _check = await _service.CheckAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = "RynthNav navdata could not be checked: " + ex.Message;
            return;
        }

        OnPropertyChanged(nameof(BannerText));
        OnPropertyChanged(nameof(ShowBanner));
        if (NeedsInstall && _consent?.IsAgreed == true && _canStartInstall())
            await RunInstallAsync().ConfigureAwait(true);
    }

    private async Task InstallAsync()
    {
        _consent?.Agree();
        if (_check is null)
            await CheckAsync().ConfigureAwait(true);
        if (!IsBusy && _check is not null)
            await RunInstallAsync().ConfigureAwait(true);
    }

    private async Task RunInstallAsync()
    {
        if (_service is null || _check is null)
            return;
        _cancellation = new CancellationTokenSource();
        Error = null;
        Status = "Starting the RynthNav navdata install…";
        IsProgressIndeterminate = true;
        IsOpen = true;
        IsBusy = true;
        try
        {
            var progress = new DispatchedProgress(_dispatcher, report =>
            {
                Status = report.Detail;
                IsProgressIndeterminate = report.Percent is null;
                ProgressPercent = report.Percent ?? 0;
            });
            Status = await _service.InstallAsync(_check, progress, _cancellation.Token).ConfigureAwait(true);
            _check = await _service.CheckAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            Error = "The RynthNav navdata install was cancelled; the old tiles are unchanged.";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Error = "The RynthNav navdata install failed: " + ex.Message;
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            IsBusy = false;
            OnPropertyChanged(nameof(ShowBanner));
        }
    }

    private void NotNow()
    {
        _dismissed = true;
        OnPropertyChanged(nameof(ShowBanner));
    }

    public void NotifyCommandStates()
    {
        InstallCommand.NotifyCanExecuteChanged();
        NotNowCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        CloseCommand.NotifyCanExecuteChanged();
    }

    private static string Gb(long bytes) =>
        (bytes / 1e9).ToString("0.0", CultureInfo.InvariantCulture) + " GB";

    private sealed class DispatchedProgress(IUiDispatcher dispatcher, Action<NavDataProgress> apply)
        : IProgress<NavDataProgress>
    {
        public void Report(NavDataProgress value) => dispatcher.Post(() => apply(value));
    }
}
```

Match the actual command type names and `NotifyCanExecuteChanged` member used by `LauncherUpdateViewModel` (this codebase may name it differently — copy whatever `LauncherUpdateViewModel.NotifyCommandStates` calls).

- [ ] **Step 4: Run the view-model tests**

Run: `~/.dotnet/dotnet test tests/AcDream.Launcher.Tests --filter "FullyQualifiedName~RynthNavData"` → PASS.

- [ ] **Step 5: Wire it into the window**

`LauncherWindowViewModel.cs` constructor: add parameter `RynthNavDataViewModel? navData = null` (last), then

```csharp
        NavData = navData ?? new RynthNavDataViewModel(null, null, dispatcher, () => true);
        NavData.PropertyChanged += OnModalPropertyChanged;
```

`IsModalOpen`: add `|| NavData.IsOpen`. In `InitializeInstalledContentAndUpdatesAsync`, after the `if (!_disposed) { … RefreshFromCore(); }` block at the end, add

```csharp
        if (!_disposed && !cancellationToken.IsCancellationRequested)
        {
            await NavData.CheckAsync(cancellationToken).ConfigureAwait(true);
        }
```

`OnClientVersionChanged`: append `_ = NavData.CheckAsync();`.

`LauncherWindowViewModel.NavData.cs`:

```csharp
namespace AcDream.Launcher.ViewModels;

public sealed partial class LauncherWindowViewModel
{
    public RynthNavDataViewModel NavData { get; }

    /// <summary>Why a launch waits while the navdata folder is being swapped.</summary>
    private string? NavDataLaunchBlock => NavData.IsBusy ? "Installing RynthNav navdata…" : null;
}
```

`LauncherWindowViewModel.Accounts.cs` `GetRowLaunchBlock` (~299): return `NavDataLaunchBlock` first when non-null. In `CanLaunch` (~1029): add `&& !NavData.IsBusy`. In `OnModalPropertyChanged`, if it switches on sender, make sure `NavData`'s `IsOpen`/`IsBusy` changes raise the same notifications as the update prompt's (read the method and add `NavData` alongside `UpdatePrompt`), plus `OnPropertyChanged(nameof(ShowNavDataBanner))` where `public bool ShowNavDataBanner => NavData.ShowBanner && !IsInstallationChecking;` lives in the NavData partial.

`LauncherUpdateComposition.cs`: expose `internal IReleaseManifestClient? ManifestClient => _manifestClient;` (the override client when `--update-manifest-uri` is used, so a pre-release test fetches its own pin).

`App.axaml.cs` before `new LauncherWindowViewModel(`:

```csharp
            RynthNavDataViewModel navData = new(
                updates.ManifestClient is { } manifestClient
                    ? LauncherNavDataService.ForThisMachine(manifestClient, updates.Versions.Barrier,
                        () => _orchestrator.HasActiveSessions)
                    : null,
                LauncherNavDataConsentStore.ForApplicationPaths(paths),
                new AvaloniaUiDispatcher(),
                () => !_orchestrator.HasActiveSessions);
```

Use whatever running-sessions predicate `LauncherUpdateComposition.Create(... hasRunningSessions ...)` is given in `App.axaml.cs` (read the call; reuse the same lambda). Pass `navData: navData` to the view model.

`MainWindow.axaml`: after the update banner `Border` (~line 160):

```xml
      <Border Background="#292820" Padding="14,8" IsVisible="{Binding ShowNavDataBanner}">
        <Grid ColumnDefinitions="*,Auto,Auto" ColumnSpacing="10">
          <StackPanel><TextBlock Text="{Binding NavData.BannerText}" FontWeight="SemiBold" /><TextBlock Text="Map tiles for RynthNav's outdoor routes. Once installed, the launcher keeps them current." Classes="muted" TextWrapping="Wrap" /><TextBlock Text="{Binding NavData.Error}" Foreground="#FFB0A0" IsVisible="{Binding NavData.HasError}" TextWrapping="Wrap" /></StackPanel>
          <Button Grid.Column="1" Content="Not now" Command="{Binding NavData.NotNowCommand}" />
          <Button Grid.Column="2" Content="Install" Classes="primary" Command="{Binding NavData.InstallCommand}" />
        </Grid>
      </Border>
```

In the "Installation & updates" flyout after "Check for updates":

```xml
                <Button Content="Install / update RynthNav navdata" Command="{Binding NavData.InstallCommand}" />
                <TextBlock Text="{Binding NavData.Status}" TextWrapping="Wrap" Classes="muted" />
```

The modal: copy the update prompt's overlay markup (find it with `grep -n "UpdatePrompt.IsOpen" src/AcDream.Launcher/MainWindow.axaml`) and bind it to `NavData`: title "RynthNav navdata", `NavData.Status`, a `ProgressBar` with `Value="{Binding NavData.ProgressPercent}" IsIndeterminate="{Binding NavData.IsProgressIndeterminate}"`, `NavData.Error`, a Cancel button (`NavData.CancelCommand`, visible while `NavData.IsBusy`) and a Close button (`NavData.CloseCommand`, visible when `!NavData.IsBusy`).

`MainWindowViewTests.cs`: follow its existing pattern for asserting bindings/controls exist; add one test asserting the window contains a button bound to `NavData.InstallCommand` and one bound to `NavData.CancelCommand`.

- [ ] **Step 6: Run the launcher tests**

Run: `~/.dotnet/dotnet test tests/AcDream.Launcher.Tests` → the new tests pass; any failure must also fail on `main` (check by running the same filter on a `main` checkout via `git worktree add ../OpenAC-main-check main` if needed, then remove the worktree).

- [ ] **Step 7: Commit**

```bash
git add src/AcDream.Launcher tests/AcDream.Launcher.Tests
git commit -m "feat(launcher): ask once for the RynthNav navdata, then keep it current

Claude-Session: https://claude.ai/code/session_011FtdjFB5dEVWdBQnZmstpz"
```

---

### Task 6: Local Mac-only fork release script and docs

**Goal:** `tools/publish-fork-release.ps1` builds the osx-arm64 fork release (two zips + three documents) and, unless `-DryRun`, tags and publishes it to `davidc-smith/OpenAC`; the docs describe the fork feed and the navdata install.

**Files:**
- Create: `tools/publish-fork-release.ps1`
- Modify: `docs/ci-and-releases.md` (new "Fork releases (Mac only, local)" section after "Releases")
- Modify: `docs/launcher.md` (new "RynthNav navdata" section after "Plugins"; a note in "Testing a pre-release" that a fork launcher starts in its own `--root-dir`)

**Acceptance Criteria:**
- [ ] Refuses: PowerShell < 7, not macOS, a dirty tree, a branch other than `main` (unless `-AllowBranch`), a version without `-fork.`, an existing tag `v<version>` (non-dry runs).
- [ ] Runs `tools/publish-bin.ps1 -MacOnly` (base URL from the property) and checks `bin/` holds `client-osx-arm64.zip`, `launcher-osx-arm64.zip`, `manifest.json`, `launcher-fingerprint.json`, `rynth-navdata.json`.
- [ ] `-DryRun` stops there and prints the `git tag`/`git push`/`gh release create` commands it would run.
- [ ] Non-dry: `git tag v<version>`, `git push origin v<version>`, `gh release create v<version> -R <repo> --title "OpenAC <version>" --notes "<…>" <five files>` (no `--prerelease`, `--latest`).
- [ ] A dry run on this Mac succeeds; `manifest.json` parses with the launcher's parser (run `~/.dotnet/dotnet test tests/AcDream.Launcher.Core.Tests --filter "FullyQualifiedName~ReleaseManifest"` unaffected, and parse the produced file with a one-off `ReleaseManifestClient` loopback fixture or by inspection that it matches the schema: `schemaVersion`, `version` = `0.1.25-fork.1`, only `osx-arm64` under `clients`/`launchers`, URLs under `https://github.com/davidc-smith/OpenAC/releases/download/v0.1.25-fork.1/`).

**Verify:** `pwsh tools/publish-fork-release.ps1 -DryRun -AllowBranch` → exits 0, lists the five files and the three commands

**Steps:**

- [ ] **Step 1: Write the script**

```powershell
[CmdletBinding()]
param(
    # Build and check everything, then print the tag/upload commands instead of running them.
    [switch]$DryRun,
    # Publish from a branch other than main (dry runs on a feature branch).
    [switch]$AllowBranch
)

# The fork's own release, built on this Mac (osx-arm64 only) until the fork releases through
# GitHub Actions: the client and launcher zips, manifest.json, launcher-fingerprint.json and
# rynth-navdata.json, published to the OpenACReleaseRepository in Directory.Build.props.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'publish-fork-release requires PowerShell 7 or newer.' }
if (-not $IsMacOS) { throw 'Fork releases are built on macOS (osx-arm64).' }

$RepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[xml]$props = Get-Content -LiteralPath (Join-Path $RepoRoot 'Directory.Build.props') -Raw
$version = [string]$props.Project.PropertyGroup.Version
$repository = [string]$props.Project.PropertyGroup.OpenACReleaseRepository
if ($version -notmatch '-fork\.\d+$') { throw "Version '$version' is not a fork version (x.y.z-fork.n)." }
if ([string]::IsNullOrWhiteSpace($repository)) { throw 'Directory.Build.props has no OpenACReleaseRepository.' }

$status = & git -C $RepoRoot status --porcelain
if ($status) { throw 'The working tree has changes; commit or stash them first.' }
$branch = (& git -C $RepoRoot rev-parse --abbrev-ref HEAD).Trim()
if ($branch -ne 'main' -and -not $AllowBranch) { throw "On '$branch'; fork releases come from main (or pass -AllowBranch)." }
$tag = "v$version"
if (-not $DryRun -and (& git -C $RepoRoot tag --list $tag)) { throw "Tag $tag already exists." }

& (Join-Path $PSScriptRoot 'publish-bin.ps1') -MacOnly
if ($LASTEXITCODE) { throw 'publish-bin failed.' }

$bin = Join-Path $RepoRoot 'bin'
$files = @('client-osx-arm64.zip', 'launcher-osx-arm64.zip', 'manifest.json', 'launcher-fingerprint.json', 'rynth-navdata.json') |
    ForEach-Object { Join-Path $bin $_ }
foreach ($file in $files) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Release file '$file' is missing." }
}

$notes = "OpenAC fork release $version (macOS Apple silicon only). The launcher installs the RynthNav navdata pinned in rynth-navdata.json."
$commands = @(
    "git -C `"$RepoRoot`" tag $tag",
    "git -C `"$RepoRoot`" push origin $tag",
    "gh release create $tag -R $repository --title `"OpenAC $version`" --notes `"$notes`" --latest $($files -join ' ')"
)
if ($DryRun) {
    Write-Host 'Dry run: the release would be published with:' -ForegroundColor Cyan
    $commands | ForEach-Object { Write-Host "  $_" }
    return
}

& git -C $RepoRoot tag $tag
if ($LASTEXITCODE) { throw "git tag $tag failed." }
& git -C $RepoRoot push origin $tag
if ($LASTEXITCODE) { throw "git push of $tag failed." }
& gh release create $tag -R $repository --title "OpenAC $version" --notes $notes --latest @files
if ($LASTEXITCODE) { throw 'gh release create failed; the tag is pushed, re-run gh release create by hand.' }
Write-Host "Published $tag to https://github.com/$repository/releases/tag/$tag" -ForegroundColor Green
```

If `$props.Project.PropertyGroup` is an array (several PropertyGroups), read the values the same way Task 2's `publish-bin.ps1` change does.

- [ ] **Step 2: Dry run**

Run: `pwsh tools/publish-fork-release.ps1 -DryRun -AllowBranch` (commit everything first; the tree must be clean).
Expected: publish-bin's "Feed written" listing with the five files, then the three commands. Inspect `bin/manifest.json` and `bin/rynth-navdata.json` against the acceptance criteria. Discard lock-file churn publish-bin reports (`git checkout -- '*packages.*.lock.json'`) if any.

- [ ] **Step 3: Docs**

`docs/ci-and-releases.md`, new section after "Releases" (before "Two release trains"):

```markdown
### Fork releases (Mac only, local)

Until the fork merges back upstream, its launcher updates from the fork's own releases:
`OpenACReleaseRepository` in `Directory.Build.props` names the repository
(`davidc-smith/OpenAC`), and both the launcher's feed
(`releases/latest/download/manifest.json`) and `publish-bin.ps1`'s asset addresses come
from it. Fork versions are `x.y.z-fork.n`; they are published as ordinary (not pre-release)
releases so the `latest` route serves them.

Build and publish one on a Mac with `pwsh tools/publish-fork-release.ps1` from a clean
`main`: it runs `publish-bin.ps1 -MacOnly`, checks the five files (the osx-arm64 client and
launcher zips, `manifest.json`, `launcher-fingerprint.json`, `rynth-navdata.json`), tags
`v<version>`, pushes the tag and creates the release with `gh` (logged in as an account that
can publish to the repository). `-DryRun` builds and checks, then prints those commands.
Windows and Linux launchers find no payload for their RID in a fork release and report
"Updates could not be checked".

`rynth-navdata.json` is `external/rynth-navdata.json` with a `schemaVersion`: the RynthNav
navdata archive this release was tested with. It lives beside `manifest.json` for the same
reason as the fingerprint. The launcher also carries the pin it was built with, and
`external/rynth-navdata.json`, `external/RynthCore` and `src/AcDream.Rynth.NavData` are
launcher fingerprint inputs, so a new pin updates the launcher.
```

`docs/launcher.md`, new section after "Plugins":

```markdown
## RynthNav navdata

RynthNav's outdoor routes need its map tiles (about 1.6 GB to download, 4.7 GB on disk).
When they are missing or older than the release's pin, a banner offers **Install**; **Not
now** hides it until the launcher next starts. After one install the launcher keeps them
current by itself, at startup and after a client update, whenever no session is running.
**Installation & updates › Install / update RynthNav navdata** installs or updates them at
any time. Launches wait while an install runs; **Cancel** stops it and leaves the old tiles
in place.

The tiles go where the client reads them: `RYNTHNAV_NAVDATA` if set, otherwise the
`NavData` folder under RynthCore's folder (`~/Library/Application Support/RynthCore` on
macOS). A folder holding a `navdata.local` file, or tiles installed by RynthCore's own
launcher, is left alone. A verified download stays in the cache
(`~/Library/Caches/RynthCore/updates` on macOS) and is reused.
```

In "Testing a pre-release", add: "A fork launcher (version `x.y.z-fork.n`) sorts below the upstream release of the same number, so start it in its own root (`--root-dir <new empty folder>`) rather than over an upstream install."

- [ ] **Step 4: Commit**

```bash
git add tools/publish-fork-release.ps1 docs/ci-and-releases.md docs/launcher.md
git commit -m "feat(release): local Mac-only fork release script; docs for the fork feed and navdata install

Claude-Session: https://claude.ai/code/session_011FtdjFB5dEVWdBQnZmstpz"
```

---

### Task 7: Live check — publish a fork release, install through the launcher

**Goal:** A real fork release on `davidc-smith/OpenAC` installs through the fork launcher in a fresh root, the launcher installs the navdata, and the client uses it.

> **USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in `acceptanceCriteria` has been re-validated independently, with output captured.

**Files:** none (merge to main happens first: finishing-a-development-branch, then the user runs the release from `main`).

**Acceptance Criteria:**
- [ ] User ran `pwsh tools/publish-fork-release.ps1` on `main`; `https://github.com/davidc-smith/OpenAC/releases/latest/download/manifest.json` returns version `0.1.25-fork.1` and `.../rynth-navdata.json` returns the pin.
- [ ] The release's `OpenAC.app`, started with `--root-dir <new empty folder>`, installs the client from the fork feed (first-run setup completes).
- [ ] With the existing `~/Library/Application Support/RynthCore/NavData` moved aside (or `RYNTHNAV_NAVDATA` pointed at an empty folder), the banner shows; Install downloads (or reuses the cached zip), unpacks 40,203 files and writes `navdata.id` = the pin id; the banner disappears; relaunching the launcher shows no banner.
- [ ] A client launched from that launcher logs no `RynthNavDataHint` line and an outdoor RynthNav route detours (the detours A check).
- [ ] Not now hides the banner for the run; the menu item works; a launch attempted during an install shows "Installing RynthNav navdata…".

**Verify:** user-run; capture `curl -sL https://github.com/davidc-smith/OpenAC/releases/latest/download/manifest.json`, `cat <NavData>/navdata.id`, and the client log line search `grep -c RynthNavDataHint <client.err.log>` → 0.

**Steps:**

- [ ] **Step 1:** Ask the user when they are available (never launch a client without that). Merge the branch to `main` and push (user's "deployed means pushed" rule) before the release.
- [ ] **Step 2:** User runs `pwsh tools/publish-fork-release.ps1` (their `gh` login). Capture the release URL.
- [ ] **Step 3:** User walks the acceptance criteria; record each PASS/FAIL with the captured output in the ledger.
- [ ] **Step 4:** Fix anything found on a branch from `main`, re-run the failing items.

```json:metadata
{"userGate": true, "tags": ["user-gate"], "files": [], "verifyCommand": "curl -sL https://github.com/davidc-smith/OpenAC/releases/latest/download/manifest.json", "acceptanceCriteria": ["fork release manifest is 0.1.25-fork.1", "fresh-root launcher installs client from fork feed", "banner -> install writes navdata.id = pin id, banner gone after relaunch", "client logs no RynthNavDataHint and an outdoor route detours", "Not now / menu item / launch block behave"], "modelTier": "standard"}
```
