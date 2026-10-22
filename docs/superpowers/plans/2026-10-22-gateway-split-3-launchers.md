# Gateway Split, Plan 3: Per-Product Launchers and Releases — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** OpenAC Gateway (`main`) and OpenAC Gateway Rynth (`rynth`) each ship their own launcher (app bundle, bundle ID, data folder) and their own releases (`gateway-v…` / `gateway-rynth-v…` tags), and each launcher updates only from its own product's releases in `davidc-smith/OpenAC`.

**Architecture:** Every per-product name is derived in `Directory.Build.props` from `OpenACProduct` (set by Plan 2; `rynth` overrides it in `Directory.Branch.props`), through one slug (`openac-gateway` / `openac-gateway-rynth`). The slug reaches code as assembly metadata; `AcDream.Platform` turns it into data folder and bundle names. The launcher stops using GitHub's single `releases/latest` and instead reads the repo's `releases.atom` feed (already used by the plugin pipeline; the REST API stays forbidden), picks the highest version tagged with its own prefix, and downloads that tag's `manifest.json`. The release scripts tag with the same prefix and publish from the branch that owns the product.

**Tech Stack:** MSBuild props, C# (.NET 10), System.Xml.Linq, PowerShell 7 release scripts, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-22-gateway-branch-split-design.md` (§3 tags, §4 launcher and releases, §6 verification)

## Global Constraints

- Derived values, exact. `main`: slug `openac-gateway`, tag prefix `gateway-v`, bundle ID `org.openac.gateway`, macOS bundle `OpenAC Gateway.app`, data folder `OpenAC Gateway` (Windows `%LOCALAPPDATA%`, macOS `~/Library/Application Support`) and `openac-gateway` (Linux XDG). `rynth`: slug `openac-gateway-rynth`, prefix `gateway-rynth-v`, bundle ID `org.openac.gateway-rynth`, bundle `OpenAC Gateway Rynth.app`, folders `OpenAC Gateway Rynth` / `openac-gateway-rynth`.
- Tags are `<prefix><OpenACReleaseVersion>`: `gateway-v0.1.26-gateway.1`, `gateway-rynth-v0.1.26-rynth.1`. Never a plain `v…` tag.
- The launcher must never call `api.github.com`. It reads `https://github.com/<OpenACReleaseRepository>/releases.atom` and `https://github.com/<repo>/releases/download/<tag>/<file>` only.
- Do not edit upstream-owned files beyond what each task lists. In particular `src/AcDream.Launcher.Core/Plugins/PluginReleaseFeed.cs` and `.github/workflows/ci.yml` stay unchanged (ci.yml is upstream's release workflow; fork tags no longer match its `v*` trigger, and Actions do not run on the fork).
- `rynth` gets no new identity properties: everything derives from its existing `OpenACProduct`. Its only Plan 3 work is the merge from `main` and the navdata pin URI (Task 6).
- `<Version>`, `OpenACReleaseNumber`, `ForkContractVersion` (`0.1.26-fork.5`) and the lock files' pinned versions do not change.
- Work for `main` happens on branch `feat/gateway-launchers` in worktree `~/code/OpenAC/OpenAC/.worktrees/gateway-launchers`, merged to `main` with `--no-ff` in Task 5. The `rynth` worktree is `~/code/OpenAC/OpenAC-gateway-rynth` (never `~/code/OpenAC/OpenAC-rynth` or `~/code/OpenAC/OpenAC-Rynth`). Task 6 runs in-session (background subagents are denied in the rynth worktree).
- Build/test environment (sandbox disabled for builds and tests):
  ```bash
  export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH \
         ACDREAM_DAT_DIR=$HOME/AsheronsCall TMPDIR=/tmp/acdt/ \
         DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib
  mkdir -p /tmp/acdt
  ```
- Baseline: `$HOME/code/OpenAC/split-baseline/presplit-failed.txt` (70 names) and `failed-tests.py`. Full-suite failures must stay a subset of it.
- After every build or publish revert lock churn: `git checkout -- '*.lock.json'` and delete untracked `packages.*.lock.json`.
- Use the Edit tool for file edits; never `sed -i -E`.
- Do not launch the client or launcher UI. Do not push and do not publish a release (Task 7 asks first; publishing a real release is out of scope).
- Commit messages end with the session attribution line from the conversation's system reminder.

**User decisions (already made):**
- Product names "OpenAC Gateway" (main) and "OpenAC Gateway Rynth" (rynth); versions `0.1.26-gateway.N` / `0.1.26-rynth.N` (Plan 2).
- Same GitHub repo (`davidc-smith/OpenAC`) for both products; a separate launcher per branch that can be installed side by side (spec §4).
- Tags `gateway-v…` and `gateway-rynth-v…` (spec §3).
- Navdata download stays only in the Rynth launcher (spec §4; already true after Plan 1).

**Decisions made in this plan (defaults, no user input needed):**
- Release lookup uses the Atom feed, not the REST API the spec mentions: the codebase forbids `api.github.com` (`GitHubReleaseLocator`), and the feed is how the plugin pipeline already finds non-latest releases. Limitation: the feed lists the 10 newest releases, so a product with no release among the repo's newest 10 reports "no release found". Both products release together, so this does not arise in practice; it is documented.
- No data migration from the old `OpenAC` data folder: the fork has published no releases yet (`gh release list -R davidc-smith/OpenAC` is empty), and the dev installs use explicit roots.

---

### Task 1: Product slug, tag prefix, bundle ID and per-product data folders

**Goal:** `Directory.Build.props` derives `OpenACProductSlug`, `OpenACReleaseTagPrefix` and `OpenACBundleIdentifier` from `OpenACProduct`; `AcDream.Platform` exposes them through `OpenACProductIdentity`; the default per-user data root is named after the product.

**Files:**
- Modify: `Directory.Build.props` (second `PropertyGroup`)
- Modify: `src/AcDream.Platform/AcDream.Platform.csproj` (assembly metadata)
- Create: `src/AcDream.Platform/OpenACProductIdentity.cs`
- Modify: `src/AcDream.Platform/ApplicationPathSet.cs:177-219` (`ResolveDefaultRoot` and its doc comment)
- Modify: `tests/AcDream.Platform.Tests/ApplicationPathSetTests.cs` (lines 22, 45, 50, 70-72, 84, 336), `tests/AcDream.Headless.Tests/HeadlessPathSetTests.cs` (lines 34, 119, 136)
- Create: `tests/AcDream.Platform.Tests/OpenACProductIdentityTests.cs`

**Acceptance Criteria:**
- [ ] `dotnet msbuild src/AcDream.App/AcDream.App.csproj -getProperty:OpenACProductSlug -getProperty:OpenACReleaseTagPrefix -getProperty:OpenACBundleIdentifier` prints `openac-gateway`, `gateway-v`, `org.openac.gateway`.
- [ ] `OpenACProductIdentity.Name` = `OpenAC Gateway`, `Slug` = `openac-gateway`, `MacAppBundleName` = `OpenAC Gateway.app`, `DataFolderName` = `OpenAC Gateway`, `LinuxDataFolderName` = `openac-gateway`.
- [ ] `ApplicationPathSet.ResolveDefaultRoot` returns `<LocalApplicationData>/OpenAC Gateway` (Windows), `~/Library/Application Support/OpenAC Gateway` (macOS), `<XDG_DATA_HOME>/openac-gateway` (Linux); no `"OpenAC"`/`"openac"` literal remains in `ApplicationPathSet.cs`.
- [ ] Platform and Headless path tests pass using `OpenACProductIdentity` instead of literals.

**Verify:** `dotnet test tests/AcDream.Platform.Tests -c Release && dotnet test tests/AcDream.Headless.Tests -c Release --filter "FullyQualifiedName~HeadlessPathSet"` → all passed.

**Steps:**

- [ ] **Step 1: Write the failing identity test** — `tests/AcDream.Platform.Tests/OpenACProductIdentityTests.cs`. The rules are written so the same test passes on `rynth` (product `OpenAC Gateway Rynth`).

```csharp
using AcDream.Platform;

namespace AcDream.Platform.Tests;

public sealed class OpenACProductIdentityTests
{
    [Fact]
    public void TheProductIsAnOpenACGatewayEdition() =>
        Assert.StartsWith("OpenAC Gateway", OpenACProductIdentity.Name, StringComparison.Ordinal);

    /// <summary>Mutation: a slug that keeps spaces or capitals, or is not the product's, fails this.</summary>
    [Fact]
    public void TheSlugIsTheProductInLowerCaseJoinedByHyphens() =>
        Assert.Equal(
            OpenACProductIdentity.Name.ToLowerInvariant().Replace(' ', '-'),
            OpenACProductIdentity.Slug);

    [Fact]
    public void FolderAndBundleNamesComeFromTheProduct()
    {
        Assert.Equal(OpenACProductIdentity.Name, OpenACProductIdentity.DataFolderName);
        Assert.Equal(OpenACProductIdentity.Slug, OpenACProductIdentity.LinuxDataFolderName);
        Assert.Equal(OpenACProductIdentity.Name + ".app", OpenACProductIdentity.MacAppBundleName);
    }

    /// <summary>Two products must never share a data folder with each other or with upstream's
    /// OpenAC, so side-by-side installs cannot see each other's clients and settings.</summary>
    [Fact]
    public void TheDataFolderIsNotUpstreamsOpenACFolder()
    {
        Assert.NotEqual("OpenAC", OpenACProductIdentity.DataFolderName);
        Assert.NotEqual("openac", OpenACProductIdentity.LinuxDataFolderName);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/AcDream.Platform.Tests -c Release --filter "FullyQualifiedName~OpenACProductIdentity"`
Expected: build error, `OpenACProductIdentity` does not exist.

- [ ] **Step 3: Derive the properties** — in `Directory.Build.props`, extend the second `PropertyGroup` (the one after the `Directory.Branch.props` import, so `rynth`'s product is already set):

```xml
  <PropertyGroup>
    <OpenACReleaseVersion>$(Version)-$(OpenACReleaseLabel).$(OpenACReleaseNumber)</OpenACReleaseVersion>
    <InformationalVersion>$(OpenACReleaseVersion)$(OpenACReleaseMetadata)</InformationalVersion>
    <Product>$(OpenACProduct)</Product>
    <!-- Per-product names, all from the product so each branch line installs side by side:
         openac-gateway / openac-gateway-rynth. -->
    <OpenACProductSlug>$(OpenACProduct.ToLowerInvariant().Replace(' ', '-'))</OpenACProductSlug>
    <!-- Release tags and the launcher's feed filter: gateway-v / gateway-rynth-v. -->
    <OpenACReleaseTagPrefix>$(OpenACProductSlug.Replace('openac-', ''))-v</OpenACReleaseTagPrefix>
    <OpenACBundleIdentifier>org.openac.$(OpenACProductSlug.Replace('openac-', ''))</OpenACBundleIdentifier>
  </PropertyGroup>
```

Check: `dotnet msbuild src/AcDream.App/AcDream.App.csproj -getProperty:OpenACProductSlug -getProperty:OpenACReleaseTagPrefix -getProperty:OpenACBundleIdentifier` prints the three `main` values from the Global Constraints.

- [ ] **Step 4: Pass the slug to the Platform assembly** — in `src/AcDream.Platform/AcDream.Platform.csproj` add (the same pattern `AcDream.Launcher.Core.csproj` uses for `OpenAC.ReleaseRepository`):

```xml
  <ItemGroup>
    <AssemblyMetadata Include="OpenAC.ProductSlug" Value="$(OpenACProductSlug)" />
  </ItemGroup>
```

First check `git grep -n "<Product>" -- 'src/*.csproj'` prints nothing (Plan 2 removed the launcher's override), so `AssemblyProduct` is `OpenACProduct` for Platform too.

- [ ] **Step 5: Create `src/AcDream.Platform/OpenACProductIdentity.cs`**

```csharp
using System.Reflection;

namespace AcDream.Platform;

/// <summary>
/// The product this build ships as (Directory.Build.props <c>OpenACProduct</c>) and the names
/// derived from it. OpenAC Gateway and OpenAC Gateway Rynth differ only here, so the two can be
/// installed side by side without sharing a data folder or an app bundle.
/// </summary>
public static class OpenACProductIdentity
{
    private static readonly Assembly Self = typeof(OpenACProductIdentity).Assembly;

    public static string Name { get; } =
        Self.GetCustomAttribute<AssemblyProductAttribute>()?.Product
        ?? throw new InvalidOperationException("AcDream.Platform was built without a product name.");

    /// <summary>The product in lower case, words joined by hyphens (<c>openac-gateway</c>).</summary>
    public static string Slug { get; } =
        Self.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "OpenAC.ProductSlug")?.Value
        ?? throw new InvalidOperationException("AcDream.Platform was built without a product slug.");

    /// <summary>The per-user data folder on Windows and macOS.</summary>
    public static string DataFolderName => Name;

    /// <summary>The per-user data folder under the XDG data home on Linux.</summary>
    public static string LinuxDataFolderName => Slug;

    /// <summary>The macOS launcher bundle's folder name.</summary>
    public static string MacAppBundleName { get; } = Name + ".app";
}
```

- [ ] **Step 6: Use it in `ApplicationPathSet.ResolveDefaultRoot`** — replace the two `"OpenAC"` arguments with `OpenACProductIdentity.DataFolderName` and the `"openac"` argument with `OpenACProductIdentity.LinuxDataFolderName`. Reword the summary:

```csharp
    /// <summary>
    /// The per-user default root, named after the product
    /// (<see cref="OpenACProductIdentity.DataFolderName"/>): <c>%LOCALAPPDATA%\OpenAC Gateway</c> on
    /// Windows, <c>~/Library/Application Support/OpenAC Gateway</c> on macOS and
    /// <c>$XDG_DATA_HOME/openac-gateway</c> (or <c>~/.local/share/openac-gateway</c>) on Linux.
    /// It is the only place the pointer file is read from.
    /// </summary>
```

- [ ] **Step 7: Update the path tests** — in `ApplicationPathSetTests.cs` and `HeadlessPathSetTests.cs` replace every default-root literal `"OpenAC"` with `OpenACProductIdentity.DataFolderName` and `"openac"` with `OpenACProductIdentity.LinuxDataFolderName` (add `using AcDream.Platform;` where missing). Leave `ApplicationPathSetTests.cs:134` (`Path.Combine(platform.Base, "Games", "OpenAC")`, a user-chosen pointer target, not the default) unchanged. Update the two mutation comments to say "the product's folder" instead of "OpenAC". Find any other hit with `git grep -n '"OpenAC")\|"openac")' tests/AcDream.Platform.Tests tests/AcDream.Headless.Tests` and decide each by the same rule (default root → constant; arbitrary user path → leave).

- [ ] **Step 8: Run the tests**

Run: `dotnet test tests/AcDream.Platform.Tests -c Release && dotnet test tests/AcDream.Headless.Tests -c Release --filter "FullyQualifiedName~HeadlessPathSet"`
Expected: all passed. Then `git checkout -- '*.lock.json'`.

- [ ] **Step 9: Commit**

```bash
git add Directory.Build.props src/AcDream.Platform tests/AcDream.Platform.Tests tests/AcDream.Headless.Tests
git commit -m "feat(platform): per-product slug, tag prefix, bundle ID and data folder"
```

---

### Task 2: macOS launcher bundle named after the product

**Goal:** The launcher runs from, self-updates and is packaged as `<Product>.app` with bundle ID `OpenACBundleIdentifier`, so `OpenAC Gateway.app` and `OpenAC Gateway Rynth.app` sit side by side.

**Files:**
- Modify: `src/AcDream.Launcher.Core/Updates/LauncherInstallationLayout.cs:36-73`
- Modify: `src/AcDream.Launcher.Core/Updates/LauncherSelfUpdateManager.cs:669,775`
- Modify: `tools/package-macos-launcher.ps1` (bundle folder, plist names and ID, messages, zip check)
- Modify: `tools/publish-bin.ps1:84` (message only)
- Modify: `tests/AcDream.Launcher.Core.Tests/Updates/LauncherSelfUpdateManagerTests.cs` (16 `OpenAC.app` literals)
- Create: `tests/AcDream.Launcher.Core.Tests/Updates/LauncherInstallationLayoutProductTests.cs`

**Acceptance Criteria:**
- [ ] `LauncherInstallationLayout.MacBundle` accepts `…/OpenAC Gateway.app` and rejects `…/OpenAC.app`; `Detect` requires `<Product>.app/Contents/MacOS`; error messages name the product bundle.
- [ ] `git grep -n '"OpenAC.app"' -- src` → nothing.
- [ ] `package-macos-launcher.ps1` creates `<OpenACProduct>.app` with `CFBundleName`/`CFBundleDisplayName` = product and `CFBundleIdentifier` = `OpenACBundleIdentifier`, and its zip check requires entries under `<OpenACProduct>.app/`.
- [ ] `LauncherSelfUpdateManagerTests` and the new layout tests pass.

**Verify:** `dotnet test tests/AcDream.Launcher.Core.Tests -c Release --filter "FullyQualifiedName~LauncherSelfUpdateManager|FullyQualifiedName~LauncherInstallationLayout"` → all passed.

**Steps:**

- [ ] **Step 1: Write the failing layout tests** — `tests/AcDream.Launcher.Core.Tests/Updates/LauncherInstallationLayoutProductTests.cs`:

```csharp
using AcDream.Launcher.Core.Updates;
using AcDream.Platform;

namespace AcDream.Launcher.Core.Tests.Updates;

public sealed class LauncherInstallationLayoutProductTests
{
    private static readonly string Container =
        Path.GetFullPath(Path.Combine(Path.GetTempPath(), "layout-product"));

    [Fact]
    public void TheMacBundleIsNamedAfterTheProduct()
    {
        string bundle = Path.Combine(Container, OpenACProductIdentity.MacAppBundleName);

        LauncherInstallationLayout layout = LauncherInstallationLayout.MacBundle(bundle, "osx-arm64");

        Assert.Equal(OpenACProductIdentity.MacAppBundleName, layout.PayloadRoot);
        Assert.Equal(
            $"{OpenACProductIdentity.MacAppBundleName}/Contents/MacOS/acdream-launcher",
            layout.PayloadLauncherPath);
    }

    /// <summary>Mutation: still accepting upstream's OpenAC.app would let one product's launcher
    /// run (and self-update) inside the other's or upstream's bundle.</summary>
    [Fact]
    public void UpstreamsBundleNameIsRejected()
    {
        LauncherUpdateException error = Assert.Throws<LauncherUpdateException>(() =>
            LauncherInstallationLayout.MacBundle(Path.Combine(Container, "OpenAC.app"), "osx-arm64"));

        Assert.Contains(OpenACProductIdentity.MacAppBundleName, error.Message, StringComparison.Ordinal);
    }
}
```

Run: `dotnet test tests/AcDream.Launcher.Core.Tests -c Release --filter "FullyQualifiedName~LauncherInstallationLayoutProduct"` → FAIL (`OpenAC.app` expected; the second test does not throw).

- [ ] **Step 2: Use the product bundle name in the layout** — in `LauncherInstallationLayout.cs` add `using AcDream.Platform;`, then replace each `"OpenAC.app"` with `OpenACProductIdentity.MacAppBundleName` (three places) and the two messages with:

```csharp
                $"The macOS launcher must run from {OpenACProductIdentity.MacAppBundleName}.");
...
                $"The macOS launcher must run from {OpenACProductIdentity.MacAppBundleName}/Contents/MacOS.");
```

- [ ] **Step 3: Same in `LauncherSelfUpdateManager.cs`** — add `using AcDream.Platform;`; line 669 compares with `OpenACProductIdentity.MacAppBundleName`; the line-775 message becomes

```csharp
                $"The macOS launcher ZIP must contain exactly the {layout.PayloadRoot} bundle root and launcher executable.");
```

- [ ] **Step 4: Update `LauncherSelfUpdateManagerTests.cs`** — add `using AcDream.Platform;` and a field `private static readonly string Bundle = OpenACProductIdentity.MacAppBundleName;`; replace each `"OpenAC.app"` with `Bundle` and each `"OpenAC.app/…"` string with `$"{Bundle}/…"`. Check none is left: `git grep -n "OpenAC\.app" tests/AcDream.Launcher.Core.Tests` → nothing.

- [ ] **Step 5: Run the launcher-core tests**

Run: `dotnet test tests/AcDream.Launcher.Core.Tests -c Release --filter "FullyQualifiedName~LauncherSelfUpdateManager|FullyQualifiedName~LauncherInstallationLayout"`
Expected: all passed.

- [ ] **Step 6: Package the bundle under the product name** — in `tools/package-macos-launcher.ps1`, after the `$zip` line, read the properties (the same helper `publish-bin.ps1` uses):

```powershell
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
function Get-OpenACProperty([string]$Name) {
    $value = & dotnet msbuild (Join-Path $repoRoot 'src/AcDream.App/AcDream.App.csproj') "-getProperty:$Name"
    if ($LASTEXITCODE -or [string]::IsNullOrWhiteSpace($value)) { throw "Could not read $Name from the build." }
    return ([string]($value -join "`n")).Trim()
}
# Each product line ships its own bundle so OpenAC Gateway and OpenAC Gateway Rynth install side by side.
$product = Get-OpenACProperty 'OpenACProduct'
$bundleIdentifier = Get-OpenACProperty 'OpenACBundleIdentifier'
$bundleName = "$product.app"
```

Then: remove the later duplicate `$repoRoot = …` line (it moves up); `$bundle = Join-Path $output $bundleName`; in the plist, `CFBundleDisplayName` and `CFBundleName` become `$product` and `CFBundleIdentifier` becomes `$bundleIdentifier`; every message saying `OpenAC.app` says `$bundleName` (use double-quoted strings); the zip check becomes

```powershell
            -not $_.FullName.StartsWith("$bundleName/", [StringComparison]::Ordinal)
        }).Count -ne 0) {
        throw "The macOS launcher ZIP must contain only the $bundleName bundle."
```

Keep `OpenAC.icns` and `OpenAC.iconset` as they are (internal file names). Check: `grep -n "OpenAC.app\|org.openac.launcher" tools/package-macos-launcher.ps1` → nothing.

- [ ] **Step 7: `publish-bin.ps1` message** — line 84: `throw '-IncludeMacOS packages a macOS app bundle and must run on macOS.'`

- [ ] **Step 8: Commit** (the packaging script runs for real in Task 4's dry run)

```bash
git add src/AcDream.Launcher.Core tests/AcDream.Launcher.Core.Tests tools/package-macos-launcher.ps1 tools/publish-bin.ps1
git commit -m "feat(launcher): macOS bundle and bundle ID per product"
```

---

### Task 3: Launcher finds its own product's newest release on the feed

**Goal:** In production the launcher reads `releases.atom`, picks the highest-version release whose tag is `OpenACReleaseTagPrefix` + SemVer, downloads that tag's `manifest.json` (and fingerprint beside it), and rejects a manifest whose version differs from its tag.

**Files:**
- Create: `src/AcDream.Launcher.Core/Updates/ProductReleaseFeed.cs`
- Modify: `src/AcDream.Launcher.Core/AcDream.Launcher.Core.csproj` (metadata `OpenAC.ReleaseTagPrefix`)
- Modify: `src/AcDream.Launcher.Core/Updates/ReleaseManifestClient.cs`
- Modify: `src/AcDream.Launcher/LauncherStartupOptions.cs:76,172`, `src/AcDream.Launcher/LauncherUpdateComposition.cs:59,137` (rename `ProductionManifestUri` → `ProductionUpdateFeedUri`)
- Create: `tests/AcDream.Launcher.Core.Tests/Updates/ProductReleaseFeedTests.cs`
- Modify: `tests/AcDream.Launcher.Core.Tests/Updates/ReleaseTransportTests.cs:95-99,162-170` and add feed tests; `tests/AcDream.Launcher.Tests/LauncherStartupOptionsTests.cs:64`

**Acceptance Criteria:**
- [ ] `ProductReleaseFeed.Newest(feed, repo, "gateway-v")` returns the highest `gateway-v…` release and ignores `gateway-rynth-v…`, plain `v…`, foreign-repo, non-https, build-metadata and unparsable tags; malformed XML or a DTD throws `LauncherUpdateException`.
- [ ] `ReleaseManifestClient.ReleaseTagPrefix` equals `OpenACProductIdentity.Slug` without `openac-`, plus `-v`; `ProductionUpdateFeedUri` = `https://github.com/davidc-smith/OpenAC/releases.atom`.
- [ ] Feed-mode `FetchAsync` requests exactly the feed, then `…/releases/download/<newest own tag>/manifest.json`; the fingerprint is then fetched from the same tag folder; a manifest whose version differs from the tag throws; a feed without an own-product release throws `LauncherUpdateException` naming the prefix.
- [ ] `--update-manifest-uri` overrides still fetch the given manifest directly (existing tests unchanged apart from the rename).
- [ ] `git grep -n "releases/latest" -- src/AcDream.Launcher.Core/Updates src/AcDream.Launcher` → nothing.

**Verify:** `dotnet test tests/AcDream.Launcher.Core.Tests -c Release --filter "FullyQualifiedName~ProductReleaseFeed|FullyQualifiedName~ReleaseManifestClient" && dotnet test tests/AcDream.Launcher.Tests -c Release --filter "FullyQualifiedName~LauncherStartupOptions"` → all passed.

**Steps:**

- [ ] **Step 1: Write the failing feed parser tests** — `tests/AcDream.Launcher.Core.Tests/Updates/ProductReleaseFeedTests.cs`:

```csharp
using System.Text;
using AcDream.Launcher.Core.Updates;

namespace AcDream.Launcher.Core.Tests.Updates;

public sealed class ProductReleaseFeedTests
{
    private const string Repo = "davidc-smith/OpenAC";

    [Fact]
    public void TheHighestOwnReleaseWinsWhateverTheFeedOrder()
    {
        ProductFeedRelease? release = Parse(
            Entry(Link("gateway-v0.1.26-gateway.9")),
            Entry(Link("gateway-v0.1.26-gateway.10")),
            Entry(Link("gateway-v0.1.26-gateway.2")));

        Assert.Equal("gateway-v0.1.26-gateway.10", release!.Tag);
        Assert.Equal("0.1.26-gateway.10", release.Version.Value);
    }

    /// <summary>Mutation: a plain StartsWith on "gateway" (no "-v") lets the Gateway launcher take a
    /// Rynth release; every case here must be ignored.</summary>
    [Theory]
    [InlineData("gateway-rynth-v0.1.27-rynth.1")]
    [InlineData("v0.1.27")]
    [InlineData("gateway-v0.1.27-gateway.1+meta")]
    [InlineData("gateway-vnot-a-version")]
    [InlineData("gateway-v")]
    public void OtherProductsAndMalformedTagsAreIgnored(string tag)
    {
        Assert.Null(Parse(Entry(Link(tag))));
    }

    [Fact]
    public void TheRynthPrefixTakesOnlyRynthReleases()
    {
        ProductFeedRelease? release = Parse(
            "gateway-rynth-v",
            Entry(Link("gateway-v0.1.27-gateway.1")),
            Entry(Link("gateway-rynth-v0.1.26-rynth.3")));

        Assert.Equal("gateway-rynth-v0.1.26-rynth.3", release!.Tag);
    }

    [Theory]
    [InlineData("https://github.com/someone-else/OpenAC/releases/tag/gateway-v0.1.27-gateway.1")]
    [InlineData("http://github.com/davidc-smith/OpenAC/releases/tag/gateway-v0.1.27-gateway.1")]
    [InlineData("https://github.com/davidc-smith/OpenAC/releases/tag/gateway-v0.1.27-gateway.1?x=1")]
    [InlineData("https://example.test/davidc-smith/OpenAC/releases/tag/gateway-v0.1.27-gateway.1")]
    public void LinksOutsideThisRepoOrHttpsAreIgnored(string href)
    {
        Assert.Null(Parse(Entry($"""<link rel="alternate" href="{href}"/>""")));
    }

    [Fact]
    public void AReleaseLinkInsideContentDoesNotCount()
    {
        Assert.Null(Parse(Entry(
            $"""<content type="html"><link href="https://github.com/{Repo}/releases/tag/gateway-v0.1.27-gateway.1"/></content>""")));
    }

    [Theory]
    [InlineData("<feed")]
    [InlineData("""<!DOCTYPE feed [<!ENTITY x "y">]><feed xmlns="http://www.w3.org/2005/Atom"/>""")]
    [InlineData("<other/>")]
    public void MalformedFeedsThrow(string xml)
    {
        Assert.Throws<LauncherUpdateException>(() =>
            ProductReleaseFeed.Newest(Encoding.UTF8.GetBytes(xml), Repo, "gateway-v"));
    }

    internal static string Feed(params string[] entries) =>
        $"""<?xml version="1.0" encoding="UTF-8"?><feed xmlns="http://www.w3.org/2005/Atom">{string.Concat(entries)}</feed>""";

    internal static string Entry(string body) => $"<entry>{body}</entry>";

    internal static string Link(string tag) =>
        $"""<link rel="alternate" type="text/html" href="https://github.com/{Repo}/releases/tag/{tag}"/>""";

    private static ProductFeedRelease? Parse(params string[] entries) => Parse("gateway-v", entries);

    private static ProductFeedRelease? Parse(string prefix, params string[] entries) =>
        ProductReleaseFeed.Newest(Encoding.UTF8.GetBytes(Feed(entries)), Repo, prefix);
}
```

Run: `dotnet test tests/AcDream.Launcher.Core.Tests -c Release --filter "FullyQualifiedName~ProductReleaseFeed"` → build error, `ProductReleaseFeed` does not exist.

- [ ] **Step 2: Create `src/AcDream.Launcher.Core/Updates/ProductReleaseFeed.cs`**

```csharp
using System.Xml;
using System.Xml.Linq;

namespace AcDream.Launcher.Core.Updates;

/// <summary>One release of this launcher's product found on the repo's releases feed.</summary>
public sealed record ProductFeedRelease(string Tag, LauncherVersion Version);

/// <summary>
/// Reads a repo's <c>releases.atom</c> feed for the newest release of one product: the highest
/// version among entries whose tag is the product's prefix (<c>gateway-v</c>,
/// <c>gateway-rynth-v</c>) followed by a SemVer version without build metadata. Both products
/// release from one repository, so GitHub's single "latest" release cannot serve either. A
/// <c>link</c> only counts as a direct child of an <c>entry</c> that names, on <c>github.com</c>
/// over https with no query or fragment, exactly <c>/&lt;owner&gt;/&lt;repo&gt;/releases/tag/&lt;tag&gt;</c>
/// for this repo. The feed lists the newest ten releases. Kept apart from upstream's
/// <c>PluginReleaseFeed</c> (prerelease plugin tags) so upstream syncs do not touch it.
/// </summary>
public static class ProductReleaseFeed
{
    private static readonly XNamespace AtomNamespace = "http://www.w3.org/2005/Atom";

    /// <summary>Ten entries with full release notes; the same allowance the plugin feed has.</summary>
    private const long MaxCharactersInDocument = 4_000_000;

    /// <summary>The newest release tagged with <paramref name="tagPrefix"/>, or null when the feed
    /// has none. Malformed XML, a DTD or a non-Atom root throws
    /// <see cref="LauncherUpdateException"/>.</summary>
    public static ProductFeedRelease? Newest(byte[] content, string repo, string tagPrefix)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentException.ThrowIfNullOrWhiteSpace(tagPrefix);

        XDocument document;
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxCharactersInDocument,
        };
        try
        {
            using var stream = new MemoryStream(content);
            using XmlReader reader = XmlReader.Create(stream, settings);
            document = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new LauncherUpdateException("The releases feed is not well-formed XML.", ex);
        }

        XElement? feed = document.Root;
        if (feed is null || feed.Name != AtomNamespace + "feed")
        {
            throw new LauncherUpdateException("The releases feed has no Atom <feed> root.");
        }

        ProductFeedRelease? newest = null;
        foreach (XElement entry in feed.Elements(AtomNamespace + "entry"))
        {
            foreach (XElement link in entry.Elements(AtomNamespace + "link"))
            {
                if (TryMatchTag((string?)link.Attribute("href"), repo, tagPrefix, out ProductFeedRelease? candidate)
                    && (newest is null || candidate!.Version.CompareTo(newest.Version) > 0))
                {
                    newest = candidate;
                }
            }
        }

        return newest;
    }

    private static bool TryMatchTag(
        string? href, string repo, string tagPrefix, out ProductFeedRelease? release)
    {
        release = null;
        if (string.IsNullOrEmpty(href)
            || !Uri.TryCreate(href, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Port != 443
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string[] repoParts = repo.Split('/');
        if (segments.Length != 5
            || !string.Equals(segments[0], repoParts[0], StringComparison.OrdinalIgnoreCase)
            || !string.Equals(segments[1], repoParts[1], StringComparison.OrdinalIgnoreCase)
            || segments[2] != "releases"
            || segments[3] != "tag")
        {
            return false;
        }

        string tag = Uri.UnescapeDataString(segments[4]);
        if (!tag.StartsWith(tagPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        string versionText = tag[tagPrefix.Length..];
        if (versionText.Contains('+')
            || !LauncherVersion.TryParse(versionText, out LauncherVersion? version))
        {
            return false;
        }

        release = new ProductFeedRelease(tag, version);
        return true;
    }
}
```

Note `gateway-v` is not a prefix of `gateway-rynth-v`, so the plain `StartsWith` keeps the two products apart; the Theory in Step 1 pins that.

Run the Step 1 tests → all passed.

- [ ] **Step 3: Tag prefix metadata** — in `src/AcDream.Launcher.Core/AcDream.Launcher.Core.csproj`, next to the existing `OpenAC.ReleaseRepository` item:

```xml
    <AssemblyMetadata Include="OpenAC.ReleaseTagPrefix" Value="$(OpenACReleaseTagPrefix)" />
```

- [ ] **Step 4: Write the failing client tests** — in `ReleaseTransportTests.cs` (class `ReleaseManifestClientTests`), add `using AcDream.Platform;` at the top and:
  - In the loopback-manifest test (lines 95-99) replace the `ProductionManifestUri` assertions with:

```csharp
        // Both products release from one repository; each launcher reads the releases feed and
        // takes only its own tag prefix (Directory.Build.props OpenACReleaseRepository/OpenACProduct).
        Assert.Equal(
            "https://github.com/davidc-smith/OpenAC/releases.atom",
            ReleaseManifestClient.ProductionUpdateFeedUri.AbsoluteUri);
        Assert.Equal(
            OpenACProductIdentity.Slug["openac-".Length..] + "-v",
            ReleaseManifestClient.ReleaseTagPrefix);
```

  - In `ProductionRedirectToLoopbackIsRejectedBeforePlaintextRequest` (lines 162-170) replace `ReleaseManifestClient.ProductionManifestUri` with `ReleaseManifestClient.ProductionUpdateFeedUri` in both places (it still exercises a fixed-URI client, so it keeps calling `CreateForTransportTest`).
  - Add these tests and helpers (the helpers sit beside `ValidJson()`/`Redirect`):

```csharp
    private static readonly Uri FeedUri = new("https://github.com/davidc-smith/OpenAC/releases.atom");

    private static string Download(string tag, string file) =>
        $"https://github.com/davidc-smith/OpenAC/releases/download/{tag}/{file}";

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)) };

    private static string FeedOf(params string[] tags) =>
        ProductReleaseFeedTests.Feed(tags.Select(tag => ProductReleaseFeedTests.Entry(ProductReleaseFeedTests.Link(tag))).ToArray());

    [Fact]
    public async Task TheFeedPicksThisProductsNewestReleaseAndItsManifest()
    {
        var handler = new SequenceHandler((request, index) => index switch
        {
            0 => Ok(FeedOf("gateway-rynth-v3.0.0", "gateway-v2.0.0", "gateway-v1.9.0")),
            1 => Ok(ValidJson()),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        using var source = ReleaseManifestClient.CreateFeedForTransportTest(
            FeedUri, "davidc-smith/OpenAC", "gateway-v", handler);

        ReleaseManifest manifest = await source.FetchAsync();
        _ = await source.FetchLauncherFingerprintAsync();

        Assert.Equal("2.0.0", manifest.Version.Value);
        Assert.Equal(
            [FeedUri, new Uri(Download("gateway-v2.0.0", "manifest.json")),
             new Uri(Download("gateway-v2.0.0", LauncherFingerprintDocument.FileName))],
            handler.Requests);
    }

    /// <summary>Mutation: skipping the tag/version check lets a mis-tagged release (or a manifest
    /// uploaded to the wrong release) install a different version than the tag promised.</summary>
    [Fact]
    public async Task AManifestWhoseVersionDiffersFromItsTagIsRejected()
    {
        var handler = new SequenceHandler((request, index) => index == 0
            ? Ok(FeedOf("gateway-v2.0.1"))
            : Ok(ValidJson()));
        using var source = ReleaseManifestClient.CreateFeedForTransportTest(
            FeedUri, "davidc-smith/OpenAC", "gateway-v", handler);

        LauncherUpdateException error = await Assert.ThrowsAsync<LauncherUpdateException>(
            () => source.FetchAsync());

        Assert.Contains("gateway-v2.0.1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFeedWithoutThisProductsReleaseIsAnError()
    {
        var handler = new SequenceHandler((request, _) => Ok(FeedOf("gateway-rynth-v2.0.0", "v2.0.0")));
        using var source = ReleaseManifestClient.CreateFeedForTransportTest(
            FeedUri, "davidc-smith/OpenAC", "gateway-v", handler);

        LauncherUpdateException error = await Assert.ThrowsAsync<LauncherUpdateException>(
            () => source.FetchAsync());

        Assert.Contains("gateway-v", error.Message, StringComparison.Ordinal);
        Assert.Equal([FeedUri], handler.Requests);
    }

    [Fact]
    public async Task NoFingerprintIsFetchedBeforeAReleaseIsKnown()
    {
        var handler = new SequenceHandler((request, _) => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var source = ReleaseManifestClient.CreateFeedForTransportTest(
            FeedUri, "davidc-smith/OpenAC", "gateway-v", handler);

        Assert.Null(await source.FetchLauncherFingerprintAsync());
        Assert.Empty(handler.Requests);
    }
```

`ValidJson()` has `"version":"2.0.0"`; check that and that `ProductReleaseFeedTests`' helpers are `internal` (Step 1 makes them so). In `LauncherStartupOptionsTests.cs:64` rename to `ProductionUpdateFeedUri`.

Run: `dotnet test tests/AcDream.Launcher.Core.Tests -c Release --filter "FullyQualifiedName~ReleaseManifestClient"` → build errors (`ProductionUpdateFeedUri`, `ReleaseTagPrefix`, `CreateFeedForTransportTest` missing).

- [ ] **Step 5: Feed mode in `ReleaseManifestClient`** — make these changes:

  1. Replace `ProductionManifestUri` with:

```csharp
    /// <summary>The tag prefix of this product's releases (Directory.Build.props
    /// OpenACReleaseTagPrefix: <c>gateway-v</c> or <c>gateway-rynth-v</c>).</summary>
    public static string ReleaseTagPrefix { get; } =
        typeof(ReleaseManifestClient).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "OpenAC.ReleaseTagPrefix")?.Value
        ?? throw new InvalidOperationException("The launcher was built without a release tag prefix.");

    /// <summary>The production update feed: the repository's releases Atom feed, read for the newest
    /// release tagged <see cref="ReleaseTagPrefix"/>. Not <c>releases/latest</c>: both products
    /// release from one repository and GitHub has one "latest" per repository.</summary>
    public static Uri ProductionUpdateFeedUri { get; } =
        GitHubReleaseLocator.ReleasesFeed(ReleaseRepository);

    /// <summary>Ten entries with full release notes (see <see cref="ProductReleaseFeed"/>).</summary>
    public const int MaximumFeedBytes = 4 * 1024 * 1024;
```

  Add `using AcDream.Launcher.Core.Plugins;` for `GitHubReleaseLocator`.

  2. Fields: `_manifestUri` becomes `private readonly Uri? _manifestUri;` (a fixed manifest: overrides and fixtures) and add

```csharp
    /// <summary>Feed mode (production): where to look and which tags are this product's.</summary>
    private readonly (Uri FeedUri, string Repository, string TagPrefix)? _feed;

    /// <summary>The manifest the last feed-mode <see cref="FetchAsync"/> downloaded; files published
    /// beside it (the fingerprint) are read from the same release.</summary>
    private Uri? _releaseManifestUri;
```

  3. Constructors. The public constructor becomes feed mode:

```csharp
    public ReleaseManifestClient(TimeSpan? timeout = null)
        : this(
            manifestUri: null,
            (ProductionUpdateFeedUri, ReleaseRepository, ReleaseTagPrefix),
            allowLoopbackHttp: false,
            CreateRedirectDisabledHandler(),
            timeout)
    {
    }
```

  The existing private constructor `(Uri manifestUri, bool allowLoopbackHttp, HttpMessageHandler handler, TimeSpan? timeout)` keeps its signature and delegates: `: this(manifestUri ?? throw new ArgumentNullException(nameof(manifestUri)), feed: null, allowLoopbackHttp, handler, timeout)`. The new private constructor holds today's body:

```csharp
    private ReleaseManifestClient(
        Uri? manifestUri,
        (Uri FeedUri, string Repository, string TagPrefix)? feed,
        bool allowLoopbackHttp,
        HttpMessageHandler handler,
        TimeSpan? timeout)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if ((manifestUri is null) == (feed is null))
        {
            throw new ArgumentException("Exactly one of a manifest URI and a release feed is required.");
        }

        _manifestUri = manifestUri;
        _feed = feed;
        _allowLoopbackHttp = allowLoopbackHttp;
        RequireTransport(manifestUri ?? feed!.Value.FeedUri, "manifest", _allowLoopbackHttp);
        _httpClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(15),
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OpenAC-launcher/1");
    }
```

  Add the test factory beside `CreateForTransportTest`:

```csharp
    internal static ReleaseManifestClient CreateFeedForTransportTest(
        Uri feedUri,
        string repository,
        string tagPrefix,
        HttpMessageHandler handler) => new(
            manifestUri: null,
            (feedUri, repository, tagPrefix),
            allowLoopbackHttp: false,
            handler,
            TimeSpan.FromSeconds(15));
```

  4. `FetchAsync`:

```csharp
    public async Task<ReleaseManifest> FetchAsync(
        CancellationToken cancellationToken = default)
    {
        if (_feed is not { } feed)
        {
            return Parse(
                await FetchBytesAsync(_manifestUri!, "release manifest", MaximumManifestBytes, cancellationToken)
                    .ConfigureAwait(false),
                _allowLoopbackHttp);
        }

        ProductFeedRelease release = ProductReleaseFeed.Newest(
                await FetchBytesAsync(feed.FeedUri, "releases feed", MaximumFeedBytes, cancellationToken)
                    .ConfigureAwait(false),
                feed.Repository,
                feed.TagPrefix)
            ?? throw new LauncherUpdateException(
                $"No '{feed.TagPrefix}' release is among the newest releases of {feed.Repository}.");
        Uri manifestUri = GitHubReleaseLocator.TaggedAsset(feed.Repository, release.Tag, "manifest.json");
        ReleaseManifest manifest = Parse(
            await FetchBytesAsync(manifestUri, "release manifest", MaximumManifestBytes, cancellationToken)
                .ConfigureAwait(false),
            _allowLoopbackHttp);
        if (manifest.Version.CompareTo(release.Version) != 0)
        {
            throw new LauncherUpdateException(
                $"Release {release.Tag} carries a manifest for version {manifest.Version.Value}.");
        }

        _releaseManifestUri = manifestUri;
        return manifest;
    }

    /// <summary>A file published beside the release manifest, or null in feed mode before
    /// <see cref="FetchAsync"/> has found the release.</summary>
    private Uri? ReleaseFileUri(string fileName) =>
        (_manifestUri ?? _releaseManifestUri) is { } manifestUri ? new Uri(manifestUri, fileName) : null;
```

  5. `FetchLauncherFingerprintAsync`: before creating the timeout, add `if (ReleaseFileUri(LauncherFingerprintDocument.FileName) is not { } fingerprintUri) return null;` and fetch `fingerprintUri` with `MaximumManifestBytes` instead of `new Uri(_manifestUri, …)`. (The method stays `async`.)

  6. `FetchBytesAsync` and `ReadBytesAsync` take an `int maximumBytes` parameter (after `description`) used in place of `MaximumManifestBytes` in both size checks and their messages. Update every caller.

- [ ] **Step 6: Rename in the launcher** — `LauncherStartupOptions.cs:76,172` and `LauncherUpdateComposition.cs:59,137`: `ProductionManifestUri` → `ProductionUpdateFeedUri`. `CreateManifestClient` keeps its shape: the production sentinel builds the feed-mode client, anything else is a manifest override. Check `git grep -n "ProductionManifestUri" -- src tests` → nothing, and `git grep -n "releases/latest" -- src/AcDream.Launcher.Core/Updates src/AcDream.Launcher` → nothing.

- [ ] **Step 7: Run the tests**

Run: `dotnet test tests/AcDream.Launcher.Core.Tests -c Release --filter "FullyQualifiedName~ProductReleaseFeed|FullyQualifiedName~ReleaseManifestClient" && dotnet test tests/AcDream.Launcher.Tests -c Release --filter "FullyQualifiedName~LauncherStartupOptions"`
Expected: all passed. Then `dotnet build AcDream.slnx -c Release` → 0 warnings, 0 errors; `git checkout -- '*.lock.json'`.

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.Launcher.Core src/AcDream.Launcher tests/AcDream.Launcher.Core.Tests tests/AcDream.Launcher.Tests
git commit -m "feat(launcher): update from this product's newest tagged release on the feed"
```

---

### Task 4: Release scripts tag per product and publish from the product's branch

**Goal:** `publish-bin.ps1` addresses assets under `releases/download/<prefix><version>`, and `publish-fork-release.ps1` tags `<prefix><version>`, publishes from `main` for Gateway and `rynth` for Gateway Rynth, and no longer marks releases latest; a dry run on this branch proves the whole osx-arm64 publish, bundle and manifest.

**Files:**
- Modify: `tools/publish-bin.ps1:66-70`
- Modify: `tools/publish-fork-release.ps1`

**Acceptance Criteria:**
- [ ] `pwsh tools/publish-fork-release.ps1 -DryRun -AllowBranch` (on `feat/gateway-launchers`) completes and prints `git … tag gateway-v0.1.26-gateway.1` and `gh release create gateway-v0.1.26-gateway.1 -R davidc-smith/OpenAC --title "OpenAC Gateway 0.1.26-gateway.1"` with no `--latest`.
- [ ] Every `url` in `bin/manifest.json` starts with `https://github.com/davidc-smith/OpenAC/releases/download/gateway-v0.1.26-gateway.1/`.
- [ ] `unzip -l bin/launcher-osx-arm64.zip` lists only `OpenAC Gateway.app/…`; its `Info.plist` has `CFBundleIdentifier` `org.openac.gateway` and `CFBundleName` `OpenAC Gateway`.
- [ ] After the dry run `git status --porcelain` is empty (lock files restored by the script).

**Verify:** the dry run plus `jq -r '.clients[].url, .launchers[].url' bin/manifest.json` and the `unzip`/`plutil` checks in Step 4.

**Steps:**

- [ ] **Step 1: `publish-bin.ps1` asset base** — after `$repositoryVersion = …` (line 39) add `$releaseTagPrefix = Get-OpenACProperty 'OpenACReleaseTagPrefix'`, and change the default base:

```powershell
$RawBase = if ([string]::IsNullOrWhiteSpace($BaseUrl)) {
    # Fork releases are tagged per product (gateway-v… / gateway-rynth-v…, see BRANCHING.md).
    "https://github.com/$releaseRepository/releases/download/$releaseTagPrefix$Version"
} else {
    $BaseUrl.TrimEnd('/')
}
```

- [ ] **Step 2: `publish-fork-release.ps1`** — after `$product = …`:

```powershell
$tagPrefix = Get-OpenACProperty 'OpenACReleaseTagPrefix'
$label = Get-OpenACProperty 'OpenACReleaseLabel'
# Each product releases from the branch line that owns it (BRANCHING.md).
$releaseBranch = switch ($label) {
    'gateway' { 'main' }
    'rynth' { 'rynth' }
    default { throw "No release branch is known for release label '$label'." }
}
if ($tagPrefix -notmatch '^[a-z]+(-[a-z]+)*-v$') { throw "Tag prefix '$tagPrefix' is not '<product>-v'." }
```

Then: the `-AllowBranch` message says `publish from $releaseBranch`; the branch check becomes `if ($branch -ne $releaseBranch -and -not $AllowBranch) { throw "On '$branch'; $product releases come from $releaseBranch (or pass -AllowBranch for a dry run)." }`; `$tag = "$tagPrefix$version"`; `git fetch origin $releaseBranch`, `rev-parse "origin/$releaseBranch"` and the message `"HEAD is not origin/$releaseBranch; push $releaseBranch first."` replace the hard-coded `main` ones. Drop `--latest` from both `gh release create` lines and replace the comment above `$notes` with:

```powershell
# Not marked latest on purpose: both products release here and each launcher finds its own
# newest release by tag prefix on the releases feed, never through releases/latest.
```

Also update the header comment to say the release is tagged `<prefix><version>` from the product's branch.

- [ ] **Step 3: Dry run** (long: full osx-arm64 publish; run in the background and wait once)

Run: `pwsh tools/publish-fork-release.ps1 -DryRun -AllowBranch`
Expected: ends with "Dry run: the release would be published with:" and the three commands from the Acceptance Criteria. If `package-macos-launcher.ps1` fails, fix it there (Task 2 code) and commit the fix with this task.

- [ ] **Step 4: Check the outputs**

```bash
jq -r '.clients[].url, .launchers[].url' bin/manifest.json
unzip -l bin/launcher-osx-arm64.zip | awk 'NR>3 {print $4}' | grep -v '^OpenAC Gateway.app/' | grep -v '^$' ; echo "stray entries above (expect none)"
rm -rf /tmp/acdt/lz && mkdir -p /tmp/acdt/lz && unzip -q bin/launcher-osx-arm64.zip -d /tmp/acdt/lz
plutil -extract CFBundleIdentifier raw "/tmp/acdt/lz/OpenAC Gateway.app/Contents/Info.plist"
plutil -extract CFBundleName raw "/tmp/acdt/lz/OpenAC Gateway.app/Contents/Info.plist"
git status --porcelain
```

Expected: every URL under `…/releases/download/gateway-v0.1.26-gateway.1/`; no stray entries (the trailing summary lines of `unzip -l` may show as numbers only, ignore those); `org.openac.gateway`; `OpenAC Gateway`; empty status.

- [ ] **Step 5: Commit**

```bash
git add tools/publish-bin.ps1 tools/publish-fork-release.ps1
git commit -m "build(release): per-product tags, release branch and feed-friendly releases"
```

---

### Task 5: Docs, lock-file check, full suite and merge to main

**Goal:** Docs describe the per-product launchers, tags and feed lookup; every committed per-RID lock file restores in locked mode; the full suite stays at baseline; `feat/gateway-launchers` is merged to `main`.

**Files:**
- Modify: `BRANCHING.md:47-49`, `docs/ci-and-releases.md:199-224` (Fork releases) and `:239-243` (launcher feed sentence), `docs/launcher.md:311-314` (default data folders), spec status note at the end of §4 of `docs/superpowers/specs/2026-10-22-gateway-branch-split-design.md`

**Acceptance Criteria:**
- [ ] Docs name the tags `gateway-v<version>` / `gateway-rynth-v<version>`, the feed lookup (`releases.atom`, newest ten releases), per-product bundle/ID/data folders, publishing from `main`/`rynth`, that releases are not marked latest, and that `ci.yml` is upstream's workflow left unchanged and not triggered by fork tags.
- [ ] Every committed `packages.<rid>.lock.json` restores with `--locked-mode` for its RID (script in Step 2 prints no `FAIL`).
- [ ] Full suite failures ⊆ the 70-name baseline; `dotnet build AcDream.slnx -c Release` 0 warnings.
- [ ] `main` has a `--no-ff` merge of `feat/gateway-launchers`; `git log main ^feat/gateway-launchers` empty.

**Verify:** `python3 $HOME/code/OpenAC/split-baseline/failed-tests.py <trx dir>` compared against `presplit-failed.txt` → no new names.

**Steps:**

- [ ] **Step 1: Docs**
  - `BRANCHING.md` lines 47-49 become: "Releases are tagged `gateway-v<version>` (from `main`) and `gateway-rynth-v<version>` (from `rynth`), never plain `v…`. Each launcher reads the repository's `releases.atom` feed and installs only the newest release with its own prefix; see `docs/ci-and-releases.md` (Fork releases)."
  - `docs/ci-and-releases.md` Fork releases: replace the `releases/latest/download/manifest.json` description with the feed lookup (prefix from `OpenACReleaseTagPrefix`, highest SemVer among the feed's newest ten releases, manifest from `releases/download/<tag>/`, manifest version must equal the tag's); the script tags `<prefix><version>`, publishes from `main` (Gateway) or `rynth` (Gateway Rynth) and no longer passes `--latest`; delete "For now the script publishes from `main` only; … Plan 3." and the "(still, until the per-product tags arrive)" aside; replace the paragraph about `v*` tags triggering the fork's `ci.yml` with: "Fork tags do not match `ci.yml`'s `v*` trigger; `ci.yml` is upstream's release workflow and is left unchanged in the fork (its macOS checks still name `OpenAC.app`)." In the "Two release trains" section's feed sentence (around line 239-243), note that the fork's launchers use the feed instead (link to Fork releases).
  - `docs/launcher.md` lines 311-314: the default root is named after the product: `%LOCALAPPDATA%\OpenAC Gateway`, `~/Library/Application Support/OpenAC Gateway`, `$XDG_DATA_HOME/openac-gateway` (Gateway Rynth: `OpenAC Gateway Rynth` / `openac-gateway-rynth`); the macOS app is `OpenAC Gateway.app` (bundle ID `org.openac.gateway`), so both products and upstream OpenAC can be installed side by side; an older fork build's data in `OpenAC` is not moved automatically.
  - Spec §4: append "*Plan 3 (2026-10-22): the lookup reads `releases.atom`, not the REST API, which the launcher never calls; see `docs/ci-and-releases.md`.*"

- [ ] **Step 2: Locked-mode restore of every per-RID lock file**

```bash
for f in $(git ls-files '*packages.*-*.lock.json'); do
  d=$(dirname "$f"); rid=$(basename "$f" .lock.json); rid=${rid#packages.}
  proj=$(ls "$d"/*.csproj | head -1)
  dotnet restore "$proj" -r "$rid" --locked-mode -v q >/dev/null 2>&1 || echo "FAIL $f"
done; echo done
git status --porcelain '*.lock.json'
```

Expected: only `done`; empty status. A `FAIL` line means that lock file is stale: re-run that one restore without `-v q` to see the NU1004 detail, regenerate it with `dotnet restore "$proj" -r "$rid" --force-evaluate`, review the diff (only the expected package lines should change), and commit it as `build: refresh <project> <rid> lock file`.

- [ ] **Step 3: Commit the docs**

```bash
git add BRANCHING.md docs/ci-and-releases.md docs/launcher.md docs/superpowers/specs/2026-10-22-gateway-branch-split-design.md
git commit -m "docs: per-product launchers, tags and release feed"
```

- [ ] **Step 4: Full build and suite** (background; wait once)

```bash
dotnet build AcDream.slnx -c Release 2>&1 | tail -3
dotnet test AcDream.slnx -c Release --no-build --logger trx --results-directory /tmp/acdt/plan3-trx 2>&1 | tail -15
python3 $HOME/code/OpenAC/split-baseline/failed-tests.py /tmp/acdt/plan3-trx > /tmp/acdt/plan3-failed.txt
comm -23 <(sort /tmp/acdt/plan3-failed.txt) <(sort $HOME/code/OpenAC/split-baseline/presplit-failed.txt)
git checkout -- '*.lock.json'
```

Expected: 0 warnings, 0 errors; the `comm` output (new failures) is empty. A new failure that mentions `OpenAC.app`, `Application Support/OpenAC`, `openac` or `releases/latest` is a missed literal from Tasks 1-3: fix it there and commit.

- [ ] **Step 5: Merge to main** (from the main checkout `~/code/OpenAC/OpenAC`)

```bash
git -C ~/code/OpenAC/OpenAC merge --no-ff feat/gateway-launchers -m "Merge branch 'feat/gateway-launchers': per-product launchers, tags and release feed"
```

Then `dotnet build AcDream.slnx -c Release` in the main checkout → 0 warnings; revert lock churn.

---

### Task 6: Bring the change to rynth (in-session)

**Goal:** `rynth` takes Plan 3 through an ordinary merge from `main`, builds as OpenAC Gateway Rynth with prefix `gateway-rynth-v`, its navdata pin is read from the same release as its manifest, and a dry run publishes from `rynth`.

**Files:** (all in `~/code/OpenAC/OpenAC-gateway-rynth`)
- Merge: `main` into `rynth`
- Resolve: `src/AcDream.Launcher.Core/Updates/ReleaseManifestClient.cs` (rynth's `FetchOptionalAsync`), `tools/publish-fork-release.ps1`, `tools/publish-bin.ps1`, `src/AcDream.Launcher/LauncherUpdateComposition.cs`, `docs/ci-and-releases.md` and lock files if they conflict
- Test (rynth only, if a navdata pin test fetches with a fixed manifest): `tests/AcDream.Launcher.Core.Tests` navdata pin tests

**Acceptance Criteria:**
- [ ] `git -C ~/code/OpenAC/OpenAC-gateway-rynth log rynth..main` is empty after the merge.
- [ ] In the rynth worktree `dotnet msbuild src/AcDream.App/AcDream.App.csproj -getProperty:OpenACReleaseTagPrefix -getProperty:OpenACBundleIdentifier -getProperty:OpenACProductSlug` prints `gateway-rynth-v`, `org.openac.gateway-rynth`, `openac-gateway-rynth`, and `Directory.Branch.props` is unchanged by this task.
- [ ] Rynth's `FetchOptionalAsync` uses `ReleaseFileUri(fileName)` and returns null when it is null; `git grep -n "new Uri(_manifestUri" -- src` → nothing.
- [ ] Build 0 warnings; full-suite failures ⊆ the rynth baseline (the same 70-name file).
- [ ] `pwsh tools/publish-fork-release.ps1 -DryRun` on `rynth` (no `-AllowBranch`) prints `gateway-rynth-v0.1.26-rynth.1` commands, the file list includes `rynth-navdata.json`, the launcher zip holds `OpenAC Gateway Rynth.app/` only, and `git status --porcelain` is empty afterwards.

**Verify:** the msbuild property print, the dry run, and the suite comparison above.

**Steps:**

- [ ] **Step 1: Merge**

```bash
cd ~/code/OpenAC/OpenAC-gateway-rynth
git status --porcelain   # must be empty
git merge main --no-ff -m "Merge branch 'main' into rynth: per-product launchers, tags and release feed"
git diff --name-only --diff-filter=U
```

- [ ] **Step 2: Resolve conflicts** as "rynth content + main's change" (the rule recorded for the Plan 2 merge):
  - `ReleaseManifestClient.cs`: keep rynth's `FetchOptionalAsync` / `FetchNavDataPinAsync` structure and main's feed mode. `FetchOptionalAsync` starts with `if (ReleaseFileUri(fileName) is not { } fileUri) return null;` and fetches `fileUri` with `MaximumManifestBytes`; `FetchLauncherFingerprintAsync` stays rynth's one-line delegation. Keep both `using` blocks (rynth's NavData usings and main's `AcDream.Launcher.Core.Plugins`).
  - `publish-fork-release.ps1`: main's prefix, `$releaseBranch` and no-`--latest` logic; rynth's `rynth-navdata.json` in `$files` and its navdata sentence in `$notes`.
  - `publish-bin.ps1`, `LauncherUpdateComposition.cs`: both sides' lines (main's rename to `ProductionUpdateFeedUri` everywhere, rynth's navdata additions).
  - Docs: main's text plus rynth's navdata sentences.
  - Lock files: take rynth's version, then confirm with `dotnet restore AcDream.slnx --locked-mode`.

  Then `git grep -n "ProductionManifestUri\|new Uri(_manifestUri" -- src tests` → nothing. Build (`dotnet build AcDream.slnx -c Release`, 0 warnings), revert lock churn, and commit the merge with only the resolutions in it: `git add -A && git commit --no-edit`.

- [ ] **Step 3: Navdata pin follows the release** (a separate commit after the merge) — if a rynth test of `FetchNavDataPinAsync` builds a fixed-manifest client, it still passes unchanged. Add one feed-mode test next to the Task 3 feed tests in `ReleaseTransportTests.cs` (rynth only):

```csharp
    [Fact]
    public async Task TheNavDataPinIsReadFromTheSameReleaseAsTheManifest()
    {
        var handler = new SequenceHandler((request, index) => index switch
        {
            0 => Ok(FeedOf("gateway-rynth-v2.0.0")),
            1 => Ok(ValidJson()),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        using var source = ReleaseManifestClient.CreateFeedForTransportTest(
            FeedUri, "davidc-smith/OpenAC", "gateway-rynth-v", handler);

        _ = await source.FetchAsync();
        Assert.Null(await source.FetchNavDataPinAsync());

        Assert.Equal(
            new Uri(Download("gateway-rynth-v2.0.0", ReleaseNavDataPin.FileName)),
            handler.Requests[^1]);
    }
```

(Add `using AcDream.Launcher.Core.NavData;` if `ReleaseNavDataPin` is not already in scope.)

- [ ] **Step 4: Build, properties, suite** (environment from Global Constraints; background, wait once)

```bash
dotnet msbuild src/AcDream.App/AcDream.App.csproj -getProperty:OpenACReleaseTagPrefix -getProperty:OpenACBundleIdentifier -getProperty:OpenACProductSlug
dotnet build AcDream.slnx -c Release 2>&1 | tail -3
dotnet test AcDream.slnx -c Release --no-build --logger trx --results-directory /tmp/acdt/plan3-rynth-trx 2>&1 | tail -15
python3 $HOME/code/OpenAC/split-baseline/failed-tests.py /tmp/acdt/plan3-rynth-trx > /tmp/acdt/plan3-rynth-failed.txt
comm -23 <(sort /tmp/acdt/plan3-rynth-failed.txt) <(sort $HOME/code/OpenAC/split-baseline/presplit-failed.txt)
git checkout -- '*.lock.json'
```

Expected: the three rynth values; 0 warnings; no new failures.

- [ ] **Step 5: Commit the test**

```bash
git add tests/AcDream.Launcher.Core.Tests
git commit -m "test(launcher): navdata pin follows the feed release"
```

- [ ] **Step 6: Dry run from rynth**

Run: `pwsh tools/publish-fork-release.ps1 -DryRun`
Then the Task 4 Step 4 checks with `gateway-rynth-v0.1.26-rynth.1`, `OpenAC Gateway Rynth.app`, `org.openac.gateway-rynth`, plus `ls bin/rynth-navdata.json`; `git status --porcelain` empty.

---

### Task 7: Push and record

**Goal:** With the user's go-ahead, `main` and `rynth` are pushed to `origin`, and project memory records Plan 3 as done.

**Files:**
- Modify: `~/.claude/projects/-Users-davidsmith-code-OpenAC-OpenAC/memory/gateway-branch-split.md`, `MEMORY.md` index line

**Acceptance Criteria:**
- [ ] The user approved the push in this conversation before it ran.
- [ ] `git ls-remote origin main rynth` shows the local `main` and `rynth` heads.
- [ ] The memory note says Plan 3 is done with both commit hashes, the tag/feed/bundle/folder decisions, and that Plan 4 (cast without selecting) is next; no release has been published.

**Verify:** `git ls-remote origin refs/heads/main refs/heads/rynth` matches `git rev-parse main rynth`.

**Steps:**

- [ ] **Step 1: Ask** — "Plan 3 is merged on main (<hash>) and rynth (<hash>). Push both to origin?" Wait for yes.
- [ ] **Step 2: Push** — `git -C ~/code/OpenAC/OpenAC push origin main` and `git -C ~/code/OpenAC/OpenAC-gateway-rynth push origin rynth`; push `feat/gateway-launchers` too only if the user wants feature branches on origin (Plan 2's was not required).
- [ ] **Step 3: Memory** — update `gateway-branch-split.md` (Plan 3 done + pushed, hashes, Atom-feed lookup, tags, bundle/ID/folders, no release published yet, dev installs unaffected because they use explicit roots, Plan 4 next) and its `MEMORY.md` line.
