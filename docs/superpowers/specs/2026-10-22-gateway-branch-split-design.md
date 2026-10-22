# OpenAC Gateway: three branch lines

Date: 2026-10-22
Status: approved in conversation, awaiting written-spec review

## Goal

Split the fork into three lines of work, each with its own purpose and, for the
two products, its own version, releases and launcher:

| Branch | Product | Contents |
|---|---|---|
| `upstream-main` | none | Exact mirror of `eriknihlen/OpenAC` `main` |
| `main` | **OpenAC Gateway** | Our client: upstream plus our changes, without Rynth |
| `rynth` | **OpenAC Gateway Rynth** | `main` plus the Rynth layer |

Today all three are mixed together on `main`: 830 commits ahead of upstream
0.1.26, 140 of them from the `OpenAC-Rynth` merge (`bc0ac559`, 2026-10-18) and
105 of the 379 later non-merge commits touching Rynth areas.

## 1. Branches and merge flow

- `upstream-main` is only ever fast-forwarded from `upstream/main`. Nothing is
  committed to it. (Not `upstream`: that name would collide with the remote and
  make `upstream/main` ambiguous.)
- Merges flow one way only: `upstream-main` → `main` → `rynth`.
  Nothing from `rynth` is ever merged into `main`, and nothing from `main`
  into `upstream-main`.
- Fix routing:
  - A fix Erik should get: branch from `upstream-main`, PR to upstream, and
    merge the same branch into `main`. (Same as the current fix/*-upstream
    practice, but from the mirror branch.)
  - A client fix: on `main`; `rynth` gets it on its next merge from `main`.
  - A Rynth-only fix: on `rynth`.
- A client change Rynth needs goes on `main` when it is a general plugin or
  client API (anyone could use it), and on `rynth` only when it exists solely
  for Rynth. This keeps the `rynth` diff from `main` small.
- Flow-down uses merges, not cherry-picks, so `git log main ^rynth` always
  shows what `rynth` has not yet taken.

## 2. The one-time split

1. Create `rynth` from the current `main` and push it to `origin`.
2. Create `upstream-main` from `upstream/main` (`391e709a`, v0.1.26) and push it.
3. On `main`, one removal commit takes out everything Rynth-only (inventory in
   §2.1), regenerates the NuGet lock files, and leaves the build and test
   suite at the current Mac baseline.
4. In `rynth`, merge that commit with `git merge -s ours`, recording it as
   merged without applying the deletion. From then on ordinary
   `main` → `rynth` merges work. Without this step the next merge would delete
   Rynth from `rynth`.

No history is rewritten. Existing feature branches and tags are left as they
are.

### 2.1 Removal inventory (from the 2026-10-22 survey)

Removed from `main`:

- Projects: `src/AcDream.Rynth.*` (EngineUi, Host, NavData, Sdk, ScriptWindows,
  Plugin.RynthAi/Chat/Inventory/Lua/Nav/Oracle/Tracker),
  `tests/AcDream.Rynth.Host.Tests`, `tests/AcDream.Rynth.NavData.Tests`,
  `tools/RynthInteropRedirect`, `tools/RynthNavData`, `tools/RynthTypeExtract`
  and their `tests/AcDream.Tools.Rynth*.Tests`.
- Submodules `external/RynthCore`, `external/RynthSuite`; their entries in
  `.gitmodules`, `external/Directory.Build.props` and `external/rynth-navdata.json`.
- The ImGui overlay (`src/AcDream.App/Extensions/Overlay/`) and
  `src/AcDream.ClientExtensions`, with `ClientExtensionHost` and their tests.
  Rynth is their only consumer.
- Runtime switches `Rynth` / `RynthPump` in `RuntimeOptions`,
  `SessionConfiguration`, `HeadlessConfiguration`, the launcher's
  `SessionConfigDocument`, and the shared session-config fixture and parity
  tests (`ACDREAM_RYNTH`, `ACDREAM_RYNTH_PUMP`, `rynth`, `rynthPump`).
- `GameWindow` extension-host construction and attach (around line 547).
- Launcher navdata: `src/AcDream.Launcher.Core/NavData/*`, the navdata part of
  `ReleaseManifestClient`, `LauncherWindowViewModel.NavData.cs`, the navdata UI
  in `MainWindow.axaml`, and their tests.
- Rynth lines in `AcDream.slnx`, `AcDream.App.csproj` (project reference,
  licence links), `NOTICE.md`, `tools/publish-bin.ps1`,
  `tools/publish-fork-release.ps1`, `tools/launcher-fingerprint.ps1` and the
  fingerprint contract test.
- Rynth docs: `docs/superpowers/{specs,plans}/*rynth*` and Rynth sections of
  `docs/building-and-running.md`, `docs/launcher.md`, `docs/navigation.md`,
  `docs/ci-and-releases.md`, `docs/plugin-api.md`.

Kept on `main` (general APIs whose comments only cite RynthCore as the
reference behaviour): trade counters (`RuntimeTradeState`, `TradeAutomation`),
autorun / turn-to-heading (`RuntimeLocalPlayerMovementState`), spell-removal
reporting (`Spellbook`), vitals bounds (`CombatState`), `ServerReplyLog`,
explicit-target casting in `RuntimeSpellCastState`, and pending-motion reads.
Comments may be reworded where they only make sense with Rynth present.

The launcher's fork update feed (updating from `davidc-smith/OpenAC`) stays on
`main`; only navdata moves.

## 3. Names and versions

- Product names: **OpenAC Gateway** (`main`), **OpenAC Gateway Rynth**
  (`rynth`). Used in the window title, About, the launcher, app bundle names
  and release titles.
- Client version: four parts, `U1.U2.U3.N`. The first three are upstream's
  version; `N` is our release counter on that base and resets to 0 when an
  upstream release is merged. First Gateway release: `0.1.26.0`.
  `0.1.26.5` > `0.1.26` and `0.1.27.0` > `0.1.26.9`, so update checks compare
  correctly (unlike `0.1.26-fork.N`, which SemVer orders *below* `0.1.26`).
- Rynth version: the same shape with its own counter. Its informational
  version records the Gateway release it was built from, e.g.
  `0.1.26.2+gateway.0.1.26.4`. The part after `+` shows in About and logs and
  is ignored in comparisons.
- Tags: `gateway-v0.1.26.N` and `gateway-rynth-v0.1.26.N`. Never plain `v…`,
  so they cannot be confused with upstream's tags.
- Plugin contract version (`ForkContractVersion` in
  `AcDream.Plugin.Abstractions.csproj`): moves to the same four-part form,
  e.g. `0.1.26.6`. Its counter is independent of the client's and goes up only
  when the plugin API changes. The current value `0.1.26-fork.5` maps to
  `0.1.26.5`. Aethrix's package reference is updated to match.
- Version properties live in `Directory.Build.props`; `rynth` overrides the
  product name, version and tag prefix there, which is its expected
  permanent difference from `main` in that file.

## 4. Launcher and releases

- Both products release from `davidc-smith/OpenAC`.
- Each branch builds its own launcher: separate app name, bundle ID and install
  folder, so both can be installed side by side.
- `ReleaseManifestClient` currently downloads
  `releases/latest/download/manifest.json`. GitHub has one "latest" per repo,
  so this changes to listing releases through the GitHub API and choosing the
  newest non-draft release whose tag starts with the launcher's own prefix
  (`gateway-v` or `gateway-rynth-v`). The prefix is a build property.
- `tools/publish-fork-release.ps1` takes the tag prefix and product name from
  the same properties.
- Navdata download and pinning exist only in the Rynth launcher.
- Existing installs (`~/OpenAC-dev`, `~/OpenAC-dreamweave`,
  OpenAC Dreamweave.app) become Gateway Rynth installs if they should keep
  Rynth; otherwise Gateway.
- Releasing Rynth binaries publicly must follow the RynthCore and RynthSuite
  licences.

## 5. Working setup

- Worktrees: `~/code/OpenAC/OpenAC` on `main`, `~/code/OpenAC/OpenAC-rynth` on
  `rynth`, `~/code/OpenAC/OpenAC-upstream` on `upstream-main`.
- A short `BRANCHING.md` at the repo root records §1 and §3 (merge direction,
  fix routing, version and tag rules, how to sync upstream).
- Upstream sync procedure: fast-forward `upstream-main`, merge it into `main`
  (reset `N` to 0, check the contract version), then merge `main` into
  `rynth`.

## 6. Verification

- `main`: builds; test results match the current Mac baseline; no
  `AcDream.Rynth*` project, no `external/Rynth*` submodule, and
  `git grep -i rynth` hits only the kept reference comments listed in §2.1
  (or none, if they are reworded).
- `rynth`: builds; tests match baseline; after the `-s ours` merge,
  `git diff <pre-split main> rynth` contains only the name/version/tag
  changes.
- `upstream-main` equals `upstream/main`.
- Each launcher, pointed at a repo with both kinds of release, picks only its
  own (unit test against a canned release list).
- A live client check on each branch, only once the user says they are
  available.

## Follow-up: cast at a target without selecting it

`RuntimeSpellCastState.Cast(spellId, targetId)` already casts at a target
without reading or changing the selection, and stays on `main`. But the plugin
API's `IAutomation.Cast(spellId, targetObjectId)`
(`RuntimeAutomationSurface.cs:4224`) selects the target first, so only the
Rynth host can use the no-select path. After the split, as its own small
spec/plan on `main`: add a plugin API member that casts without selecting,
bump the contract version, and use it in Aethrix. It may also be offered
upstream.

## Out of scope

- Getting newer RynthSuite source (not published past 2026-10-09).
- Changes to Rynth features themselves.
