# Fork release feed and launcher navdata install (A2)

Date: 2026-10-20. Branch `feat/fork-feed-navdata`. Follows outdoor detours A
(`2026-10-19-rynth-nav-detours-design.md`), whose "A2" this is.

## Goal

OpenAC's own launcher (`AcDream.Launcher`) installs and keeps current the pinned
RynthNav navdata (`external/rynth-navdata.json`, 1.57 GB zip, 4.7 GB unpacked), and
the fork gets its own release feed so the pin can follow our releases until the fork
merges back upstream.

Decisions (user, 2026-10-20):

- The OpenAC launcher, not the user's ACE launcher.
- The fork launcher updates from the fork's own releases (`davidc-smith/OpenAC`).
- Releases are built locally on this Mac, osx-arm64 only, for now. GitHub Actions for
  all platforms comes later, when the fork is ready to release properly (out of scope).
- Install policy: ask once, then keep current.

## Part 1: fork release feed

**Release repository in one place.** `Directory.Build.props` gains
`<OpenACReleaseRepository>davidc-smith/OpenAC</OpenACReleaseRepository>`, emitted as
assembly metadata. `ReleaseManifestClient`'s production feed
(`ReleaseManifestClient.cs:25`, today `eriknihlen/OpenAC`) is built from it, and
`tools/publish-bin.ps1`'s default base URL (`:59`) reads it too. Going back upstream is
that one line. The curated plugin list (`PluginCatalog.cs:33`) stays on upstream.

**Version.** The repository version becomes `0.1.25-fork.1`, then `-fork.2`, and so on;
after an upstream sync to 0.1.26 it becomes `0.1.26-fork.1`. Fork releases are created
without GitHub's pre-release flag, so the `releases/latest` route serves them. Because
`0.1.25-fork.1` sorts below `0.1.25`, the fork launcher is run in its own empty root
(`--root-dir`) rather than over an existing upstream 0.1.25 install; the docs say so.

**Local release script.** New `tools/publish-fork-release.ps1`, run on this Mac:

1. Refuses a dirty tree, a branch other than `main`, or a version without `-fork.`.
2. Builds the osx-arm64 client and launcher zips with the existing macOS packaging
   (`package-macos-launcher.ps1`, `package-macos-vulkan.ps1`), the same assets the CI
   `macos-portable` job produces.
3. Writes `manifest.json` (osx-arm64 client and launcher only),
   `launcher-fingerprint.json` and `rynth-navdata.json` into one output folder, reusing
   `publish-bin.ps1`'s writers. If `publish-bin.ps1` cannot skip its Windows and Linux
   payloads, it gains a switch that does (default unchanged).
4. Unless `-DryRun`: tags `v<version>`, pushes the tag, and runs
   `gh release create -R <OpenACReleaseRepository>` with those files, not a pre-release.
   The user runs this step (the `gh` login here is not `davidc-smith`).

A fork launcher on Windows or Linux would find no payload for its RID; that is the
existing "Updates could not be checked" path and is acceptable while the feed is
Mac-only.

## Part 2: launcher installs the navdata

**Shared library.** New `src/AcDream.Rynth.NavData` (class library) takes `NavDataPin`,
`NavDataInstaller` and `NavDataFolders` from `tools/RynthNavData`, with the same linked
RynthCore files compiled unchanged (`RynthUpdater*.cs`, `DownloadProgress.cs`,
`UpdateTrust.cs`, `PluginContractVersion.cs`, `RynthInstallPaths.cs`) and the same
warning/analyzer relaxations. `tools/RynthNavData` keeps only its CLI (`pin`, `install`,
`check-feed`) and the pinner, referencing the library. `AcDream.Launcher.Core`
references the library. Nothing of RynthCore's is rewritten.

The library adds a read-only state check alongside `InstallAsync`:

| State | Meaning | Launcher |
|---|---|---|
| `Missing` | no `navdata.id` and no tiles | offer install |
| `Current` | `navdata.id` = pin id | nothing |
| `Outdated` | 64-hex `navdata.id` ≠ pin id | update (once agreed) |
| `LocalMarker` | `navdata.local` present | leave alone |
| `RynthCoreManaged` | non-64-hex id (their launcher's feed id) | leave alone |

Folder: `RYNTHNAV_NAVDATA` if set, else `<RynthCore>/NavData`, as the client resolves
it (`RynthInstallFolders`). Cache: as the tool (`~/Library/Caches/RynthCore/updates`
on macOS).

**Pin source.** The release's `rynth-navdata.json` beside `manifest.json`, fetched the
way `FetchLauncherFingerprintAsync` fetches the fingerprint (same size cap and strict
parsing, optional: any failure means no release pin). Document: `schemaVersion: 1` plus
the pin's fields. Fallback: the pin built into the launcher from
`external/rynth-navdata.json` (embedded resource). The release pin wins when present.
`external/rynth-navdata.json` and `src/AcDream.Rynth.NavData` are added to the launcher
fingerprint inputs (`tools/launcher-fingerprint.ps1`), so a pin change updates the
launcher.

**Ask once.** After the startup update check, the launcher checks the navdata state.

- `Missing`/`Outdated` and not yet agreed: an installation banner, "RynthNav navdata —
  1.6 GB download, 4.7 GB on disk", with **Install** and **Not now**. Not now hides it
  for this launcher run only.
- Install saves the agreement (`launcher-navdata.json` in the settings directory,
  atomic write). From then on an `Outdated` or `Missing` state installs on its own at
  startup and after a client update.
- **Installation & updates** gains **Install / update RynthNav navdata**, which always
  works (it also records the agreement).

**Running an install.** Only while no launcher session is running (same refusal as the
client update, under the session barrier), with launches blocked for its duration (the
existing "Checking installation…" row block). Progress in a modal like the first-run
installer: phase, percent from RynthCore's `DownloadProgress`, Cancel. A verified cached
zip is reused (RynthCore's behaviour). A failure shows its message and leaves the old
tiles in place (RynthCore's swap keeps them). The automatic update after agreement waits
if sessions are running and tries again at the next startup or client update.

## Testing

- Library: state check for each row of the table; `InstallAsync` behaviour stays covered
  by the moved `RynthNavData.Tests`.
- Launcher.Core: release pin parse (valid, unknown member, bad SHA-256, wrong schema);
  release-then-built-in fallback; agreement store round trip; install refused while a
  session runs.
- Launcher view model: banner shown/hidden by state and agreement; Not now; auto-update
  after agreement; launches blocked during install.
- Fingerprint contract test covers the new inputs; ReleaseManifestClient default feed
  comes from the metadata.
- Release script: `-DryRun` produces the three documents and two zips; manifest parses
  with the launcher's own parser.
- Live (user): publish a fork release; fork launcher in a fresh root installs the client
  and then the navdata; the client stops logging the RynthNavDataHint and an outdoor
  detour routes.

## Out of scope

GitHub Actions fork releases (all platforms); Windows/Linux fork payloads; mirroring the
navdata archive; the user's ACE launcher; RynthCore's own navdata feed.
