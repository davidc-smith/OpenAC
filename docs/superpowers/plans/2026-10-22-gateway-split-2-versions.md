# Gateway Split, Plan 2: Names and Versions — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give `main` the product name **OpenAC Gateway** and release version `0.1.26-gateway.N`, and `rynth` the name **OpenAC Gateway Rynth** and version `0.1.26-rynth.N`, without lock-file churn on release bumps and without merge conflicts between the two branches.

**Architecture:** `<Version>` returns to upstream's plain base (`0.1.26`). New properties in `Directory.Build.props` carry the product name and release counter; a computed `OpenACReleaseVersion` feeds `InformationalVersion`, which is what the client, the launcher and the release scripts read. `rynth` overrides identity in its own `Directory.Branch.props`, imported only when present. The launcher fingerprint ignores the release counter like it ignores `<Version>`.

**Tech Stack:** MSBuild props, C# (.NET 10), Avalonia XAML, PowerShell 7 release scripts, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-22-gateway-branch-split-design.md` (§3 as revised 2026-10-22)

## Global Constraints

- Exact values on `main`: `<Version>0.1.26</Version>`, `OpenACProduct` = `OpenAC Gateway`, `OpenACReleaseLabel` = `gateway`, `OpenACReleaseNumber` = `1`, so `OpenACReleaseVersion` = `0.1.26-gateway.1`.
- Exact values on `rynth` (Task 6, in `Directory.Branch.props` only): `OpenACProduct` = `OpenAC Gateway Rynth`, `OpenACReleaseLabel` = `rynth`, `OpenACReleaseNumber` = `1`, `OpenACReleaseMetadata` = `+gateway.<main's OpenACReleaseNumber>` → `0.1.26-rynth.1+gateway.1`.
- `ForkContractVersion` (`0.1.26-fork.5`) and the plugin host version (`PluginHostVersion`, reads only MAJOR.MINOR.PATCH) do not change.
- `LauncherVersion` (strict SemVer) does not change; every version string produced must parse with it.
- Tags, release-asset URLs (`releases/download/v…`), app bundle names, bundle IDs, install folders (`ApplicationPathSet`) and `.github/workflows/ci.yml` are Plan 3 — do not touch them.
- Work for `main` happens on branch `feat/gateway-versions` in worktree `~/code/OpenAC/OpenAC/.worktrees/gateway-versions`, merged to `main` with `--no-ff` in Task 5. The `rynth` worktree is `~/code/OpenAC/OpenAC-gateway-rynth` (never `~/code/OpenAC/OpenAC-rynth` or `~/code/OpenAC/OpenAC-Rynth`).
- Build/test environment (sandbox disabled for builds and tests):
  ```bash
  export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH \
         ACDREAM_DAT_DIR=$HOME/AsheronsCall TMPDIR=/tmp/acdt/ \
         DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib
  mkdir -p /tmp/acdt
  ```
- Baseline: `$HOME/code/OpenAC/split-baseline/presplit-failed.txt` (70 names) and `failed-tests.py`. Full-suite failures must stay a subset of it.
- After every build revert local lock churn (`git checkout -- '*.lock.json'`) except in Task 1 Step 3, which rewrites the lock pins on purpose.
- Use the Edit tool for file edits; never `sed -i -E` (on macOS `-E` becomes a backup suffix).
- Do not launch the client or launcher UI. Do not push (Task 7 asks first).
- Commit messages end with the session attribution line from the conversation's system reminder.

**User decisions (already made):**
- Product names "OpenAC Gateway" (main) and "OpenAC Gateway Rynth" (rynth).
- Versions keep upstream's base: `0.1.26-gateway.N` / `0.1.26-rynth.N` (scheme A, chosen over four-part).
- Plugin contract version stays `0.1.26-fork.N`.
- Same GitHub repo; separate launchers per branch (Plan 3).

---

### Task 1: Version and product properties

**Goal:** `Directory.Build.props` defines the product and release version, `<Version>` is `0.1.26`, every lock file pins `0.1.26`, and built assemblies carry `InformationalVersion` `0.1.26-gateway.1` and `Product` `OpenAC Gateway`.

**Files:**
- Modify: `Directory.Build.props`, `src/AcDream.Launcher/AcDream.Launcher.csproj` (drop its `<Product>`), every `packages*.lock.json` containing `0.1.26-fork.1`
- Create: `tests/AcDream.App.Tests/Release/ReleaseIdentityTests.cs`

**Acceptance Criteria:**
- [ ] `Directory.Build.props` has `<Version>0.1.26</Version>`, the three `OpenAC*` properties with the exact `main` values, the conditional `Directory.Branch.props` import, and a second `PropertyGroup` computing `OpenACReleaseVersion`, `InformationalVersion` and `Product`.
- [ ] `dotnet msbuild src/AcDream.App/AcDream.App.csproj -getProperty:OpenACReleaseVersion` prints `0.1.26-gateway.1`.
- [ ] `git grep -c "0.1.26-fork.1" -- '*.lock.json'` → nothing; `dotnet restore AcDream.slnx --locked-mode` succeeds.
- [ ] `ReleaseIdentityTests` pass: App and Launcher assemblies' `InformationalVersion` (before `+`) matches `^\d+\.\d+\.\d+-[a-z]+\.\d+$`, parses with `LauncherVersion`, has the same MAJOR.MINOR.PATCH as `PluginHostVersion.FromInformationalVersion`, and their `AssemblyProduct` starts with `OpenAC Gateway`.
- [ ] `dotnet build AcDream.slnx -c Release` → 0 warnings, 0 errors.

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~ReleaseIdentity"` → passed.

**Steps:**

- [ ] **Step 1: Write the failing test** — `tests/AcDream.App.Tests/Release/ReleaseIdentityTests.cs`. Check the project already references the launcher assemblies (`git grep -n "AcDream.Launcher" tests/AcDream.App.Tests/AcDream.App.Tests.csproj`); `LauncherFingerprintContractTests` lives in the same folder. If `AcDream.Launcher.Core` is not referenced, test only the App assembly plus `LauncherVersion` via whichever launcher-core reference exists, and say so in the report.

```csharp
using System.Reflection;
using System.Text.RegularExpressions;
using AcDream.Core.Plugins;
using AcDream.Launcher.Core.Updates;

namespace AcDream.App.Tests.Release;

public sealed class ReleaseIdentityTests
{
    public static TheoryData<Assembly> ShippedAssemblies => new()
    {
        typeof(AcDream.App.Rendering.GameWindow).Assembly, // any public AcDream.App type works
        typeof(LauncherVersion).Assembly,
    };

    [Theory]
    [MemberData(nameof(ShippedAssemblies))]
    public void TheReleaseVersionIsUpstreamsBasePlusAProductCounter(Assembly assembly)
    {
        string informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;
        string release = informational.Split('+')[0];

        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+-[a-z]+\.\d+$"), release);
        Assert.True(LauncherVersion.TryParse(informational, out _), informational);
        PluginHostVersion host = Assert.NotNull(PluginHostVersion.FromInformationalVersion(informational));
        Assert.StartsWith(host.ToString() + "-", release);
    }

    [Theory]
    [MemberData(nameof(ShippedAssemblies))]
    public void TheProductIsAnOpenACGatewayEdition(Assembly assembly)
    {
        string product = assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product;
        Assert.StartsWith("OpenAC Gateway", product);
    }
}
```

Run: `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~ReleaseIdentity"` → FAIL (`0.1.26-fork.1` passes the regex only by accident of shape: it fails the product assertion, `OpenAC` / `AcDream.App`).

- [ ] **Step 2: Properties** — in `Directory.Build.props` replace `<Version>0.1.26-fork.1</Version>` with:

```xml
    <!-- Upstream's version, unchanged between upstream syncs: lock files record it.
         Our release version is OpenACReleaseVersion below (see BRANCHING.md). -->
    <Version>0.1.26</Version>
    <OpenACProduct>OpenAC Gateway</OpenACProduct>
    <OpenACReleaseLabel>gateway</OpenACReleaseLabel>
    <OpenACReleaseNumber>1</OpenACReleaseNumber>
```

and directly after the first `</PropertyGroup>` add:

```xml
  <!-- A product line's own identity (the rynth branch's Gateway Rynth); main has no such file. -->
  <Import Project="$(MSBuildThisFileDirectory)Directory.Branch.props"
          Condition="Exists('$(MSBuildThisFileDirectory)Directory.Branch.props')" />

  <PropertyGroup>
    <OpenACReleaseVersion>$(Version)-$(OpenACReleaseLabel).$(OpenACReleaseNumber)</OpenACReleaseVersion>
    <InformationalVersion>$(OpenACReleaseVersion)$(OpenACReleaseMetadata)</InformationalVersion>
    <Product>$(OpenACProduct)</Product>
  </PropertyGroup>
```

Delete `<Product>OpenAC</Product>` from `src/AcDream.Launcher/AcDream.Launcher.csproj`.

- [ ] **Step 3: Lock pins** — every lock file pins project references at `[0.1.26-fork.1, )`. Replace the string in all of them:

```bash
cd ~/code/OpenAC/OpenAC/.worktrees/gateway-versions
git grep -l "0.1.26-fork.1" -- '*.lock.json' | while read f; do
  python3 -I -c 'import sys;p=sys.argv[1];s=open(p).read();open(p,"w").write(s.replace("0.1.26-fork.1","0.1.26"))' "$f"
done
git grep -c "0.1.26-fork.1" -- '*.lock.json'   # expected: nothing
# environment block
dotnet restore AcDream.slnx --locked-mode
```

Expected: locked restore succeeds. `git diff --stat -- '*.lock.json'` shows only the pin lines.

- [ ] **Step 4: Build and test**

```bash
dotnet msbuild src/AcDream.App/AcDream.App.csproj -getProperty:OpenACReleaseVersion   # 0.1.26-gateway.1
dotnet build AcDream.slnx -c Release 2>&1 | tail -3
dotnet test tests/AcDream.App.Tests -c Release --no-build --filter "FullyQualifiedName~ReleaseIdentity|FullyQualifiedName~LocalCrashReportWriter|FullyQualifiedName~CharacterManagementUiController|FullyQualifiedName~LauncherFingerprintContract"
dotnet test tests/AcDream.Core.Tests -c Release --no-build --filter "FullyQualifiedName~PluginHostVersion|FullyQualifiedName~PluginReload"
```

Expected: 0 errors; all listed tests pass (CharacterManagementUiController still expects `OpenAC {version}` and passes; Task 2 changes it).

- [ ] **Step 5: Commit**

```bash
git add Directory.Build.props src/AcDream.Launcher/AcDream.Launcher.csproj tests/AcDream.App.Tests/Release/ReleaseIdentityTests.cs $(git diff --name-only -- '*.lock.json')
git commit -m "build: OpenAC Gateway product and 0.1.26-gateway.N release version

Claude-Session: <session link from the system reminder>"
```

```json:metadata
{"files": ["Directory.Build.props", "src/AcDream.Launcher/AcDream.Launcher.csproj", "tests/AcDream.App.Tests/Release/ReleaseIdentityTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests -c Release --filter \"FullyQualifiedName~ReleaseIdentity\"", "acceptanceCriteria": ["props define Version 0.1.26, OpenACProduct/Label/Number, Branch.props import, computed OpenACReleaseVersion/InformationalVersion/Product", "msbuild -getProperty prints 0.1.26-gateway.1", "no lock file pins 0.1.26-fork.1; locked restore succeeds", "ReleaseIdentityTests pass", "solution builds 0 warnings 0 errors"], "modelTier": "standard"}
```

---

### Task 2: Show the product name in the client and launcher

**Goal:** The client window title, the diagnostics title, the character-screen version line and the launcher's window/header/info-dialog titles show the product name instead of the hard-coded `acdream` / `OpenAC`.

**Files:**
- Create: `src/AcDream.App/ProductIdentity.cs`
- Modify: `src/AcDream.App/Rendering/GameWindow.cs:69`, `src/AcDream.App/Rendering/RenderFrameDiagnosticsController.cs:237`, `src/AcDream.App/UI/Layout/CharacterManagementUiController.cs:117-122`, `src/AcDream.Launcher/MainWindow.axaml:6,129`, `src/AcDream.Launcher/App.axaml.cs:204`, `src/AcDream.Launcher/MainWindow.axaml.cs` (set title/header from code if XAML cannot bind a static)
- Test: `tests/AcDream.App.Tests/UI/Layout/CharacterManagementUiControllerTests.cs:77`, `tests/AcDream.App.Tests/Release/ReleaseIdentityTests.cs` (add one case), `tests/AcDream.Launcher.Tests/MainWindowViewTests.cs` (if it asserts the title)

**Acceptance Criteria:**
- [ ] `ProductIdentity.Name` returns the App assembly's `AssemblyProductAttribute` (falls back to `"OpenAC"` only if the attribute is missing).
- [ ] Client window title is `ProductIdentity.Name` (no `— Vulkan` suffix); the diagnostics title starts with `"{ProductIdentity.Name} {ProductVersion}"`.
- [ ] Character screen shows `"{product} {version}"`; its test expects the product from the assembly attribute, not a literal.
- [ ] Launcher main window `Title` and the header `TextBlock`, and the info dialog at `App.axaml.cs:204`, show the launcher assembly's product.
- [ ] Not changed: Vulkan application/engine names, `VulkanBringUpHost` probe title, `InstallFolderViewModel.PickerTitle`, `ApplicationPathSet` folder names, journal/texture-cache `"acdream"` strings.
- [ ] App.Tests and Launcher.Tests build; the touched tests pass.

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~CharacterManagementUiController|FullyQualifiedName~ReleaseIdentity|FullyQualifiedName~RenderFrameDiagnostics"` and `dotnet test tests/AcDream.Launcher.Tests -c Release --filter "FullyQualifiedName~MainWindow"` → passed.

**Steps:**

- [ ] **Step 1: Failing tests** — in `CharacterManagementUiControllerTests.cs` change the expectation at line 77 to read the product attribute:

```csharp
        string product = typeof(CharacterManagementUiController).Assembly
            .GetCustomAttribute<AssemblyProductAttribute>()!.Product;
        Assert.Equal($"{product} {expectedVersion}", Assert.Single(version.LinesProvider()).Text);
```

and add to `ReleaseIdentityTests`:

```csharp
    [Fact]
    public void TheClientNamesItselfByProduct() =>
        Assert.Equal(
            typeof(AcDream.App.Rendering.GameWindow).Assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product,
            AcDream.App.ProductIdentity.Name);
```

Run them → the character-screen test FAILS (`OpenAC 0.1.26-gateway.1` vs `OpenAC Gateway 0.1.26-gateway.1`), the new fact does not compile.

- [ ] **Step 2: ProductIdentity**

```csharp
using System.Reflection;

namespace AcDream.App;

/// <summary>The product name this build ships as (Directory.Build.props OpenACProduct).</summary>
internal static class ProductIdentity
{
    internal static string Name { get; } =
        typeof(ProductIdentity).Assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product
        ?? "OpenAC";
}
```

If `AcDream.App.Tests` cannot see `internal` members (check for `InternalsVisibleTo` in `AcDream.App.csproj`), make the class `public`.

- [ ] **Step 3: Use it**
  - `GameWindow.cs:69`: `Title = "acdream — Vulkan",` → `Title = AcDream.App.ProductIdentity.Name,`
  - `RenderFrameDiagnosticsController.cs:237`: `$"acdream {ProductVersion} | …"` → `$"{AcDream.App.ProductIdentity.Name} {ProductVersion} | …"` (rest of the string unchanged).
  - `CharacterManagementUiController.cs`: `[new($"OpenAC {version}", …)]` → `[new($"{AcDream.App.ProductIdentity.Name} {version}", …)]`.
  - Launcher: add the same helper as `src/AcDream.Launcher/LauncherProduct.cs` (namespace `AcDream.Launcher`, reading `typeof(LauncherProduct).Assembly`), then in `MainWindow.axaml.cs` constructor after `InitializeComponent()` set `Title = LauncherProduct.Name;` and the header TextBlock's `Text` (give it `x:Name="ProductHeader"` in `MainWindow.axaml:129`); keep `Title="OpenAC"` in XAML as the designer default or remove it, matching how other named controls are set. `App.axaml.cs:204`: `Title = "OpenAC",` → `Title = LauncherProduct.Name,`.
  - If `MainWindowViewTests` asserts named controls or the title, add `ProductHeader` / update expectations.

- [ ] **Step 4: Build and test** — the Verify commands above plus `dotnet build AcDream.slnx -c Release` (0 warnings, 0 errors); revert lock churn.

- [ ] **Step 5: Commit** — `feat: show the product name (OpenAC Gateway) in the client and launcher`, ending with the attribution line.

```json:metadata
{"files": ["src/AcDream.App/ProductIdentity.cs", "src/AcDream.App/Rendering/GameWindow.cs", "src/AcDream.App/Rendering/RenderFrameDiagnosticsController.cs", "src/AcDream.App/UI/Layout/CharacterManagementUiController.cs", "src/AcDream.Launcher/LauncherProduct.cs", "src/AcDream.Launcher/MainWindow.axaml", "src/AcDream.Launcher/MainWindow.axaml.cs", "src/AcDream.Launcher/App.axaml.cs", "tests/AcDream.App.Tests/UI/Layout/CharacterManagementUiControllerTests.cs", "tests/AcDream.App.Tests/Release/ReleaseIdentityTests.cs", "tests/AcDream.Launcher.Tests/MainWindowViewTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests -c Release --filter \"FullyQualifiedName~CharacterManagementUiController|FullyQualifiedName~ReleaseIdentity\"", "acceptanceCriteria": ["ProductIdentity.Name from AssemblyProduct", "client title and diagnostics title use product", "character screen shows product + version; test reads attribute", "launcher title/header/info dialog use product", "listed non-goals unchanged", "touched tests pass; solution builds clean"], "modelTier": "standard"}
```

---

### Task 3: Release scripts and the launcher fingerprint

**Goal:** The publish scripts take the release version from `OpenACReleaseVersion`, accept only `-gateway.N` / `-rynth.N`, never pass `-p:Version` with a release version, and the launcher fingerprint ignores `OpenACReleaseNumber` (and includes `Directory.Branch.props` when present, with its number ignored).

**Files:**
- Modify: `tools/publish-bin.ps1` (~lines 34–43, 120–125), `tools/publish-fork-release.ps1` (~lines 23–25, 37, 90, 94, 109), `tools/run-launcher-trial.ps1` (~lines 38–45), `tools/launcher-fingerprint.ps1` (~lines 19, 63–95)
- Test: `tests/AcDream.App.Tests/Release/LauncherFingerprintContractTests.cs` (~line 57)

**Acceptance Criteria:**
- [ ] A shared way to read the release version: each script runs `dotnet msbuild "<repo>/src/AcDream.App/AcDream.App.csproj" -getProperty:OpenACReleaseVersion` (and `-getProperty:OpenACProduct` where it needs the name) instead of reading `PropertyGroup.Version` from XML.
- [ ] `publish-bin.ps1`: `$Version` defaults to `OpenACReleaseVersion`; a different `-Version` throws ("differs from the repository's release version …"); `Invoke-Publish` no longer passes `-p:Version=$Version`; manifest/fingerprint `version` fields and the existing SemVer checks use `$Version` as before; the asset base URL stays `releases/download/v$Version` (Plan 3 changes it).
- [ ] `publish-fork-release.ps1`: version check regex `-(gateway|rynth)\.\d+$` with message "is not an OpenAC Gateway release version (x.y.z-gateway.n or x.y.z-rynth.n)"; release title `"$product $version"`; notes `"$product release $version (macOS Apple silicon only)."`; tag stays `v$version`.
- [ ] `run-launcher-trial.ps1`: `$TrialVersion = "$releaseVersion+trial"` with `$releaseVersion` from `OpenACReleaseVersion`.
- [ ] `launcher-fingerprint.ps1`: blanks `<OpenACReleaseNumber>…</OpenACReleaseNumber>` as it blanks `<Version>`; if `Directory.Branch.props` exists at the fingerprinted tree it is an input, with the same two blanks applied; header comment updated.
- [ ] `LauncherFingerprintContractTests`: a change to `OpenACReleaseNumber` alone leaves the fingerprint equal (new assertion beside the `<Version>` one); a change to `OpenACProduct` changes it.
- [ ] `pwsh -NoProfile -File tools/publish-fork-release.ps1 -DryRun` (or the script's documented dry-run switch) gets past the version check and prints `OpenAC Gateway 0.1.26-gateway.1` in the planned commands; if a dry run needs credentials or builds that cannot run here, run only up to the version check and say so.

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~LauncherFingerprintContract"` → passed.

**Steps:**

- [ ] **Step 1: Failing test** — after the `<Version>` assertion in `LauncherFingerprintContractTests` add:

```csharp
        Assert.Equal(fingerprint, Fingerprint(WithFile(head, "Directory.Build.props",
            Regex.Replace(props, "<OpenACReleaseNumber>[^<]*</OpenACReleaseNumber>",
                "<OpenACReleaseNumber>99</OpenACReleaseNumber>"))));
        Assert.NotEqual(fingerprint, Fingerprint(WithFile(head, "Directory.Build.props",
            Regex.Replace(props, "<OpenACProduct>[^<]*</OpenACProduct>",
                "<OpenACProduct>Another Product</OpenACProduct>"))));
```

Run → the first new assertion FAILS (the number is fingerprinted today).

- [ ] **Step 2: Fingerprint script** — next to line 92:

```powershell
    $props = Read-CommittedText $At 'Directory.Build.props'
    $props = [regex]::Replace($props, '<Version>[^<]*</Version>', '<Version />')
    $inputs['Directory.Build.props'] = [regex]::Replace($props, '<OpenACReleaseNumber>[^<]*</OpenACReleaseNumber>', '<OpenACReleaseNumber />')
```

and, if the tree at `$At` has `Directory.Branch.props` (use the script's existing committed-file helpers; check how it tests for a path), add it the same way. Update the header comment (line ~19) to say the release number is ignored too.

- [ ] **Step 3: Publish scripts** — make the changes listed in Acceptance Criteria. A helper used in each script:

```powershell
function Get-OpenACProperty([string]$Name) {
    $value = & dotnet msbuild (Join-Path $RepoRoot 'src/AcDream.App/AcDream.App.csproj') "-getProperty:$Name"
    if ($LASTEXITCODE -or [string]::IsNullOrWhiteSpace($value)) { throw "Could not read $Name from the build." }
    return $value.Trim()
}
```

(Use the script's existing repo-root variable name; `publish-bin.ps1` and `run-launcher-trial.ps1` use `$RepoRoot`.)

- [ ] **Step 4: Check** — run the Verify test; then `pwsh -NoProfile -Command '. ./tools/publish-fork-release.ps1 -?'` to find its dry-run switch and run the dry run as far as it goes without publishing (it must never tag or upload). Revert any lock churn and delete any generated `bin/` release output the dry run made under the repo's gitignored `bin/` only if it did not exist before.

- [ ] **Step 5: Commit** — `build(release): scripts read OpenACReleaseVersion; fingerprint ignores the release number`, ending with the attribution line.

```json:metadata
{"files": ["tools/publish-bin.ps1", "tools/publish-fork-release.ps1", "tools/run-launcher-trial.ps1", "tools/launcher-fingerprint.ps1", "tests/AcDream.App.Tests/Release/LauncherFingerprintContractTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests -c Release --filter \"FullyQualifiedName~LauncherFingerprintContract\"", "acceptanceCriteria": ["scripts read OpenACReleaseVersion via msbuild -getProperty", "publish-bin defaults/validates Version and no longer passes -p:Version", "publish-fork-release accepts -gateway.N/-rynth.N, titles use product, tag unchanged", "run-launcher-trial uses release version", "fingerprint blanks OpenACReleaseNumber, includes Branch.props when present", "fingerprint contract test covers number (equal) and product (different)", "dry run passes the version check"], "modelTier": "standard"}
```

---

### Task 4: Docs

**Goal:** `BRANCHING.md` and the release/launcher docs describe the new names and versions.

**Files:**
- Modify: `BRANCHING.md` (the "Versions and releases" section), `docs/ci-and-releases.md` (fork release version text), `docs/launcher.md:345` (example manifest URL version), `docs/building-and-running.md` (session-config section: Gateway rejects `rynth` keys)

**Acceptance Criteria:**
- [ ] `BRANCHING.md` "Versions and releases" states: `<Version>` = upstream base, changed only on upstream syncs; `OpenACReleaseNumber` is bumped for each release of that product and reset to 1 on an upstream sync; `main` ships `x.y.z-gateway.N` as OpenAC Gateway, `rynth` ships `x.y.z-rynth.N` as OpenAC Gateway Rynth via `Directory.Branch.props` (which must never be added to `main`); `ForkContractVersion` keeps its own `-fork.N` counter; tags and launchers per product arrive with Plan 3.
- [ ] `BRANCHING.md` upstream-sync steps add: set `<Version>` to the new upstream version, reset `OpenACReleaseNumber` to 1 (and on `rynth`, its own number in `Directory.Branch.props`), reset `ForkContractVersion` to `<Version>-fork.1`.
- [ ] `docs/ci-and-releases.md` describes fork releases with `OpenACReleaseVersion` and `-gateway.N` / `-rynth.N` instead of `-fork.N`.
- [ ] `docs/launcher.md:345` example uses `v0.1.26-gateway.1`.
- [ ] `docs/building-and-running.md` notes that the session document accepts no `rynth`/`rynthPump` keys on OpenAC Gateway.
- [ ] `git grep -n "fork\.[0-9]" -- BRANCHING.md docs/ci-and-releases.md docs/launcher.md docs/building-and-running.md` → only contract-version mentions (if any).

**Verify:** `git grep -n -E "gateway\.[0-9N]|-rynth\." -- BRANCHING.md docs/ci-and-releases.md docs/launcher.md` → the new text.

**Steps:**

- [ ] **Step 1:** Replace `BRANCHING.md`'s "Versions and releases" section with:

```markdown
## Versions and releases

- `<Version>` in `Directory.Build.props` is upstream's version (e.g. `0.1.26`). It changes only
  when an upstream release is merged, because every lock file records it.
- Each product has its own release number:
  - `main` ships **OpenAC Gateway** as `x.y.z-gateway.N` (`OpenACProduct`, `OpenACReleaseLabel`,
    `OpenACReleaseNumber` in `Directory.Build.props`).
  - `rynth` ships **OpenAC Gateway Rynth** as `x.y.z-rynth.N`, set in `Directory.Branch.props`,
    which exists only on `rynth` and must never be added to `main`.
  - Bump the product's `OpenACReleaseNumber` for each release; it goes back to 1 when an upstream
    release is merged.
- The plugin contract (`ForkContractVersion` in `AcDream.Plugin.Abstractions.csproj`) keeps its own
  `x.y.z-fork.N` counter, bumped only when the plugin API changes.
- Release tags and the two launchers' update feeds are described in
  `docs/superpowers/specs/2026-10-22-gateway-branch-split-design.md` §4.
```

and extend the "Syncing a new upstream release" step 2 with: "set `<Version>` to the new upstream version, `OpenACReleaseNumber` to 1 and `ForkContractVersion` to `<Version>-fork.1`", and step 3 with: "reset `OpenACReleaseNumber` in `Directory.Branch.props` to 1".

- [ ] **Step 2:** Edit the three docs per the Acceptance Criteria, keeping surrounding text grammatical.

- [ ] **Step 3: Commit** — `docs: Gateway product names and release versions`, ending with the attribution line.

```json:metadata
{"files": ["BRANCHING.md", "docs/ci-and-releases.md", "docs/launcher.md", "docs/building-and-running.md"], "verifyCommand": "git grep -n -E \"gateway\\.[0-9N]|-rynth\\.\" -- BRANCHING.md docs/ci-and-releases.md docs/launcher.md", "acceptanceCriteria": ["BRANCHING versions section as specified", "upstream sync steps include version resets", "ci-and-releases uses OpenACReleaseVersion and -gateway/-rynth", "launcher.md example uses v0.1.26-gateway.1", "building-and-running notes rynth keys rejected", "no stale -fork.N release mentions"], "modelTier": "mechanical"}
```

---

### Task 5: Verify and merge to main

**Goal:** The branch passes a full build and test run against the baseline and is merged into `main` with `--no-ff`.

**Files:** none edited by hand

**Acceptance Criteria:**
- [ ] Clean Release build: 0 warnings, 0 errors.
- [ ] Full test run: failures ⊆ `presplit-failed.txt` (new-failure list empty, or only known flaky classes that pass alone).
- [ ] `main` has a merge commit `Merge branch 'feat/gateway-versions': OpenAC Gateway names and 0.1.26-gateway.N versions`.
- [ ] `dotnet msbuild src/AcDream.App/AcDream.App.csproj -getProperty:OpenACReleaseVersion` on `main` prints `0.1.26-gateway.1`.

**Verify:** `comm -23 <new failures> presplit-failed.txt` → empty.

**Steps:**

- [ ] **Step 1:** In the `gateway-versions` worktree: `git clean -xdf -e .vs -q -- src tests tools`, build, then the full suite with trx into `$HOME/code/OpenAC/split-baseline/plan2-trx`, parse with `failed-tests.py` into `plan2-failed.txt`, `comm -23 plan2-failed.txt presplit-failed.txt > plan2-new-failures.txt`. Triage any entry as in Plan 1 Task 6 (run the class alone; real regressions go back to the task that caused them).
- [ ] **Step 2:** `cd ~/code/OpenAC/OpenAC && git switch main && git merge --no-ff feat/gateway-versions -m "Merge branch 'feat/gateway-versions': OpenAC Gateway names and 0.1.26-gateway.N versions" -m "Claude-Session: <link>"`, then the `-getProperty` check.

```json:metadata
{"files": [], "verifyCommand": "wc -l < $HOME/code/OpenAC/split-baseline/plan2-new-failures.txt", "acceptanceCriteria": ["clean build 0 warnings 0 errors", "no new failures vs baseline", "--no-ff merge on main", "main reports 0.1.26-gateway.1"], "modelTier": "mechanical"}
```

---

### Task 6: Give rynth its own identity

**Goal:** `rynth` takes the merge from `main` and adds `Directory.Branch.props`, so it builds as **OpenAC Gateway Rynth** `0.1.26-rynth.1+gateway.1`.

**Files:**
- Create (on `rynth` only): `Directory.Branch.props`

**Acceptance Criteria:**
- [ ] `git merge main` in `~/code/OpenAC/OpenAC-gateway-rynth` completes without conflicts.
- [ ] `Directory.Branch.props` exists only on `rynth` (`git ls-tree main -- Directory.Branch.props` is empty).
- [ ] `dotnet msbuild src/AcDream.App/AcDream.App.csproj -getProperty:OpenACReleaseVersion` → `0.1.26-rynth.1`; `-getProperty:InformationalVersion` starts with `0.1.26-rynth.1+gateway.1`; `-getProperty:Product` → `OpenAC Gateway Rynth`.
- [ ] Release build 0 errors; `ReleaseIdentityTests` and `LauncherFingerprintContractTests` pass on `rynth`.

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~ReleaseIdentity|FullyQualifiedName~LauncherFingerprintContract"` in the rynth worktree → passed.

**Steps:**

- [ ] **Step 1:** `cd ~/code/OpenAC/OpenAC-gateway-rynth && git merge main -m "Merge main into rynth" -m "Claude-Session: <link>"`.
- [ ] **Step 2:** Create `Directory.Branch.props`:

```xml
<Project>
  <!-- OpenAC Gateway Rynth: the rynth branch's product identity (see BRANCHING.md).
       Imported by Directory.Build.props; this file never exists on main. -->
  <PropertyGroup>
    <OpenACGatewayReleaseNumber>$(OpenACReleaseNumber)</OpenACGatewayReleaseNumber>
    <OpenACProduct>OpenAC Gateway Rynth</OpenACProduct>
    <OpenACReleaseLabel>rynth</OpenACReleaseLabel>
    <OpenACReleaseNumber>1</OpenACReleaseNumber>
    <OpenACReleaseMetadata>+gateway.$(OpenACGatewayReleaseNumber)</OpenACReleaseMetadata>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3:** Build (environment block, sandbox disabled), run the `-getProperty` checks and the Verify tests; revert lock churn.
- [ ] **Step 4: Commit** — `build: OpenAC Gateway Rynth identity (0.1.26-rynth.N)`, ending with the attribution line.

```json:metadata
{"files": ["Directory.Branch.props"], "verifyCommand": "dotnet test tests/AcDream.App.Tests -c Release --filter \"FullyQualifiedName~ReleaseIdentity|FullyQualifiedName~LauncherFingerprintContract\"", "acceptanceCriteria": ["main merges into rynth without conflicts", "Directory.Branch.props only on rynth", "rynth reports 0.1.26-rynth.1, +gateway.1 metadata, product OpenAC Gateway Rynth", "rynth builds; identity and fingerprint tests pass"], "modelTier": "mechanical"}
```

---

### Task 7: Push, after the user confirms

**Goal:** `main` and `rynth` are on `origin` with the user's go-ahead.

**Files:** none

**Acceptance Criteria:**
- [ ] The user confirmed the push in this session.
- [ ] `git ls-remote origin refs/heads/main refs/heads/rynth` equals `git rev-parse main rynth`.
- [ ] `feat/gateway-versions` worktree removed; the branch deleted locally (it is merged).
- [ ] The user is told: Aethrix needs no change (contract unchanged); a new dev build shows "OpenAC Gateway" / "OpenAC Gateway Rynth" and the new version line.

**Verify:** `git ls-remote origin refs/heads/main refs/heads/rynth`

**Steps:**

- [ ] **Step 1:** AskUserQuestion "Push main and rynth to origin?" (Push / Hold), stating what changed on each.
- [ ] **Step 2:** On Push: `git push origin main rynth`, compare SHAs, `git worktree remove .worktrees/gateway-versions`, `git branch -d feat/gateway-versions`.

```json:metadata
{"files": [], "verifyCommand": "git ls-remote origin refs/heads/main refs/heads/rynth", "acceptanceCriteria": ["user confirmed", "origin matches local main and rynth", "feature worktree and branch removed", "user told about Aethrix and dev builds"], "modelTier": "mechanical"}
```
