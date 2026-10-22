# Gateway Split, Plan 1: Branches and the Rynth Removal — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split the fork into `upstream-main` (mirror), `main` (OpenAC Gateway, no Rynth) and `rynth` (Gateway + Rynth), with one removal commit on `main` recorded in `rynth` by a `-s ours` merge.

**Architecture:** `rynth` is cut from the current `main` unchanged. The removal is built on a work branch `chore/split-rynth` in a worktree, verified against a recorded test baseline, then squashed onto `main` as one commit. `rynth` merges that commit with `-s ours`, so later `main` → `rynth` merges are ordinary. No history is rewritten.

**Tech Stack:** git (worktrees, submodules), .NET 10 SDK 10.0.300 (`~/.dotnet`), xUnit (`dotnet test` with trx logger), python3 for the trx parse.

**Spec:** `docs/superpowers/specs/2026-10-22-gateway-branch-split-design.md`

## Global Constraints

- Upstream base is `upstream/main` = `391e709afddcf82900a6352581ad8bd084cc07f2` (v0.1.26). When a task says "upstream's form of a line", it means `git show 391e709a:<path>`.
- Merges flow only `upstream-main` → `main` → `rynth`. Never merge `rynth` or `chore/split-rynth`'s pre-squash commits anywhere except as stated.
- `rynth` must stay byte-identical to the pre-split `main` (`PRESPLIT`) until Task 8 merges `BRANCHING.md` into it.
- Kept on `main` (do NOT remove, comments may stay): `RuntimeTradeState`, `TradeAutomation`, `RuntimeLocalPlayerMovementState`, `RuntimeSpellCastState` (incl. explicit-target `Cast(uint, uint)`), `Spellbook` spell-removal reporting, `CombatState` vitals bound, `ServerReplyLog`, pending-motion reads, and their tests.
- The launcher's fork update feed (`ReleaseManifestClient` manifest/fingerprint download from `davidc-smith/OpenAC`) stays on `main`; only navdata goes.
- Do not change `<Version>`, `ForkContractVersion`, product names, tags or the release flow here; that is Plan 2 / Plan 3.
- Never commit lock-file churn from a local build: after every build, `git checkout -- '*.lock.json'` EXCEPT in the explicit lock-regeneration step (Task 5).
- Build/test environment for every build or test command in this plan:
  ```bash
  export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH \
         ACDREAM_DAT_DIR=$HOME/AsheronsCall TMPDIR=/tmp/acdt/ \
         DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib
  mkdir -p /tmp/acdt
  ```
  Test runs need the sandbox disabled (Unix-socket tests under `/tmp/acdt/`).
- Do not launch a game client. Live checks are out of scope for this plan.
- Commit messages end with the session attribution line from the conversation's system reminder.

**User decisions (already made):**
- Three lines: `upstream-main` mirror, `main` = OpenAC Gateway, `rynth` = OpenAC Gateway Rynth; merges only flow down.
- `rynth` is built on `main` (not on clean upstream).
- The ImGui overlay and `AcDream.ClientExtensions` move to `rynth` (only Rynth uses them).
- Launcher navdata moves to `rynth`; the fork update feed stays on `main`.
- Same GitHub repo (`davidc-smith/OpenAC`) for both products.
- Cast-at-target-without-selecting stays available on `main` (runtime part kept here; the plugin API is a later plan).

---

## Removal inventory (reference for Tasks 3–5)

Delete entirely:

| Path | Task |
|---|---|
| `src/AcDream.App/Extensions/` (ClientChatSeam, ClientExtensionHost, ClientInputState, ClientItemDrops, ClientScreen, Overlay/) | 3 |
| `src/AcDream.ClientExtensions/` | 3 |
| `src/AcDream.Rynth.EngineUi/`, `src/AcDream.Rynth.Host/`, `src/AcDream.Rynth.ScriptWindows/`, `src/AcDream.Rynth.Sdk/`, `src/AcDream.Rynth.Plugin.{RynthAi,RynthChat,RynthInventory,RynthLua,RynthNav,RynthOracle,RynthTracker}/` | 3 |
| `tests/AcDream.Rynth.Host.Tests/`, `tests/AcDream.App.Tests/Extensions/` (all 24 files are fork-only extension/overlay/Rynth tests) | 3 |
| `src/AcDream.Rynth.NavData/`, `tests/AcDream.Rynth.NavData.Tests/` | 4 |
| `src/AcDream.Launcher.Core/NavData/`, `tests/AcDream.Launcher.Core.Tests/NavData/` | 4 |
| `src/AcDream.Launcher/ViewModels/RynthNavDataViewModel.cs`, `src/AcDream.Launcher/ViewModels/LauncherWindowViewModel.NavData.cs` | 4 |
| `tests/AcDream.Launcher.Tests/RynthNavDataViewModelTests.cs`, `tests/AcDream.Launcher.Tests/LauncherWindowViewModel.NavData.Tests.cs` | 4 |
| `tools/RynthInteropRedirect/`, `tools/RynthNavData/`, `tools/RynthTypeExtract/` and `tests/AcDream.Tools.Rynth{InteropRedirect,NavData,TypeExtract}.Tests/` | 4 |
| submodules `external/RynthCore`, `external/RynthSuite`; `external/rynth-navdata.json` | 5 |
| `docs/superpowers/specs/*rynth*`, `docs/superpowers/plans/*rynth*` (filenames containing "rynth", any case) | 5 |

Edit (remove only the Rynth/overlay/extension parts; keep every other fork change):

| File | What goes | Task |
|---|---|---|
| `AcDream.slnx` | `<Project>` lines for ClientExtensions, every `AcDream.Rynth.*`, Rynth tools and Rynth test projects | 3, 4 |
| `src/AcDream.App/AcDream.App.csproj` | ProjectReferences to ClientExtensions and Rynth.Host; the three `<None Include="..\..\external\Rynth…">` licence links (lines ~70–75) | 3 |
| `tests/AcDream.App.Tests/AcDream.App.Tests.csproj` | the `RynthConfigSandbox.cs` link and its comment (lines ~30–31) | 3 |
| `src/AcDream.App/Rendering/GameWindow.cs` | `_extensions`, `_overlay`, `_keyGate`, `_chatSeam`, `_clientScreen`, `_itemDrops` fields and every use (~lines 284–286, 364, 482–483, 543–554, 579, 1020–1023, 1246, 1259, 1436, 1634, 1666, 1725–1728, 1877) | 3 |
| `src/AcDream.App/Rendering/GameWindowLifetime.cs` | `OverlayKeyGate` parameter (~136) and its `Hard("overlay key gate", …)` disposal (~520) | 3 |
| `src/AcDream.App/Composition/FrameRootComposition.cs` | `Overlay` property (~69), the `d.Overlay?.Start(...)` line (~809); `devTools: d.Overlay` → `devTools: null` (×2); the failure-recovery argument back to upstream's form | 3 |
| `src/AcDream.App/UI/RetainedUiInputBinding.cs` | `OverlayInputGate` checks (~214, 362–364, 406) back to upstream's form | 3 |
| `src/AcDream.App/Input/SilkKeyboardSource.cs` | `OverlayInputGate.KeyTaken` checks (~134, 150, 160) back to upstream's form | 3 |
| `src/AcDream.App/UI/RetailUiRuntime.cs` | `ExtensionChat` property (~390) and its uses (~780, 1077, 1732) | 3 |
| `src/AcDream.App/UI/Layout/ChatWindowController.cs` | `SetExtensionHidden` / `ExtensionEntryClaim` members and their uses | 3 |
| `src/AcDream.App/RuntimeOptions.cs` | `Rynth`, `RynthPump` properties, env reads (~197–198), session overrides (~250–251) | 3 |
| `src/AcDream.App/Configuration/SessionConfiguration.cs` | `Rynth`, `RynthPump` (~94–105) | 3 |
| `src/AcDream.Headless/Configuration/HeadlessConfiguration.cs` | `Rynth` (~72–76) | 3 |
| `src/AcDream.Launcher.Core/Launching/SessionConfigDocument.cs` | `Rynth`, `RynthPump` (~90–101) | 3 |
| `tests/AcDream.App.Tests/Configuration/RuntimeOptionsSessionConfigTests.cs`, `tests/AcDream.HostParity.Tests/SessionConfigPluginFieldParityTests.cs`, `tests/Fixtures/launcher-session/session-config-shared-fixture.json` | the `rynth` / `rynthPump` cases, asserts and fixture key | 3 |
| `src/AcDream.Launcher.Core/AcDream.Launcher.Core.csproj` | Rynth.NavData ProjectReference (~20) and the `rynth-navdata.json` EmbeddedResource + comment (~24–25) | 4 |
| `src/AcDream.Launcher.Core/Updates/ReleaseManifestClient.cs` | `using` lines 5–6, `FetchNavDataPinAsync` (interface default ~20–23 and implementation ~149–151) | 4 |
| `src/AcDream.Launcher/App.axaml.cs` | the `RynthNavDataViewModel` construction (~116–123) and `navData:` argument (~135) | 4 |
| `src/AcDream.Launcher/ViewModels/LauncherWindowViewModel.cs` | `navData` ctor parameter, `InitializeNavData`, `NavData.*` uses, `ShowNavDataBanner`, `NavDataStartupCheck`, `PostNavDataCheck`, `DisposeNavData` | 4 |
| `src/AcDream.Launcher/ViewModels/LauncherWindowViewModel.Accounts.cs`, `src/AcDream.Launcher/LauncherUpdateComposition.cs`, `src/AcDream.Launcher/MainWindow.axaml(.cs)` | navdata banner/menu/commands | 4 |
| `tests/AcDream.Launcher.Tests/MainWindowViewTests.cs`, `tests/AcDream.Launcher.Core.Tests/LauncherCoreDependencyBoundaryTests.cs`, `tests/AcDream.App.Tests/Release/LauncherFingerprintContractTests.cs` | navdata/Rynth expectations | 4, 5 |
| `.gitmodules`, `external/Directory.Build.props` | Rynth submodule entries / Rynth properties | 5 |
| `tools/publish-bin.ps1` (~422–440), `tools/publish-fork-release.ps1` (~11, 83, 90), `tools/launcher-fingerprint.ps1` (~14–16, 63–72), `tools/README.md` (~13) | navdata pin output, Rynth fingerprint inputs, Rynth tool row | 5 |
| `NOTICE.md` (~125–140) | "RynthCore and RynthSuite" section and the Phosphor paragraph (font came from RynthCore) | 5 |
| `docs/building-and-running.md` (~116, 223–240), `docs/launcher.md` (`## RynthNav navdata`, ~274–294), `docs/ci-and-releases.md` (~210–223) | Rynth/navdata text | 5 |

Allowed `rynth` mentions left on `main` after Task 5 (outside `docs/superpowers/`):
`src/AcDream.Runtime/Gameplay/RuntimeTradeState.cs`, `src/AcDream.Runtime/Gameplay/RuntimeLocalPlayerMovementState.cs`, `src/AcDream.Runtime/Gameplay/RuntimeSpellCastState.cs`, `src/AcDream.Core/Spells/Spellbook.cs`, `src/AcDream.Core/Combat/CombatState.cs`, `src/AcDream.Core/Items/ServerReplyLog.cs`, `src/AcDream.Plugin.Abstractions/TradeAutomation.cs`, `tests/AcDream.Runtime.Tests/Gameplay/RuntimeTradeStateTests.cs`, `tests/AcDream.Runtime.Tests/Gameplay/RuntimeLocalPlayerMovementStateTests.cs`, `docs/plugin-api.md`, `docs/navigation.md`, `BRANCHING.md` (Task 8).

---

### Task 1: Land the docs on main and record the baseline

**Goal:** `main` carries the spec and this plan, its HEAD is recorded as `PRESPLIT`, and the set of failing tests on `PRESPLIT` is saved.

**Files:**
- Create: `$HOME/code/OpenAC/split-baseline/failed-tests.py` (outside the repo)
- Create: `$HOME/code/OpenAC/split-baseline/presplit.sha`, `$HOME/code/OpenAC/split-baseline/presplit-failed.txt`

**Acceptance Criteria:**
- [ ] `main` contains `docs/superpowers/specs/2026-10-22-gateway-branch-split-design.md` and this plan (fast-forward merge of `docs/gateway-branch-split-spec`).
- [ ] `presplit.sha` holds the full SHA of `main` after that merge.
- [ ] `presplit-failed.txt` lists failing test names (one per line, sorted) from a full `dotnet test AcDream.slnx -c Release` run on `PRESPLIT`; the count is about 70 (the known Mac baseline).
- [ ] `git status --short` in `~/code/OpenAC/OpenAC` is empty afterwards.

**Verify:** `wc -l < $HOME/code/OpenAC/split-baseline/presplit-failed.txt` → roughly 70; `git -C ~/code/OpenAC/OpenAC log -1 --format=%H main` equals `cat presplit.sha`.

**Steps:**

- [ ] **Step 1: Merge the docs branch into main**

```bash
cd ~/code/OpenAC/OpenAC
git switch main
git merge --ff-only docs/gateway-branch-split-spec
mkdir -p $HOME/code/OpenAC/split-baseline
git rev-parse main > $HOME/code/OpenAC/split-baseline/presplit.sha
```

- [ ] **Step 2: Write the trx parser**

```bash
cat > $HOME/code/OpenAC/split-baseline/failed-tests.py <<'PY'
import glob, sys, xml.etree.ElementTree as ET
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
names = set()
for path in glob.glob(sys.argv[1] + "/**/*.trx", recursive=True):
    for result in ET.parse(path).getroot().iterfind(".//t:UnitTestResult", ns):
        if result.get("outcome") == "Failed":
            names.add(result.get("testName"))
print("\n".join(sorted(names)))
PY
```

- [ ] **Step 3: Run the full suite on PRESPLIT (background, sandbox disabled, ~15+ min)**

```bash
cd ~/code/OpenAC/OpenAC
# environment block from Global Constraints
dotnet build AcDream.slnx -c Release 2>&1 | tail -3
rm -rf $HOME/code/OpenAC/split-baseline/presplit-trx
dotnet test AcDream.slnx -c Release --no-build --logger trx \
  --results-directory $HOME/code/OpenAC/split-baseline/presplit-trx > $HOME/code/OpenAC/split-baseline/presplit-test.log 2>&1
python3 -I $HOME/code/OpenAC/split-baseline/failed-tests.py $HOME/code/OpenAC/split-baseline/presplit-trx \
  > $HOME/code/OpenAC/split-baseline/presplit-failed.txt
wc -l < $HOME/code/OpenAC/split-baseline/presplit-failed.txt
git checkout -- '*.lock.json'
git status --short
```

Expected: build `0 Error(s)`; roughly 70 failed names; clean status. If the count is far from 70 (say > 90), stop and report before continuing: the baseline itself is suspect.

No commit in this task (the merge in Step 1 is a fast-forward).

```json:metadata
{"files": ["$HOME/code/OpenAC/split-baseline/failed-tests.py", "$HOME/code/OpenAC/split-baseline/presplit.sha", "$HOME/code/OpenAC/split-baseline/presplit-failed.txt"], "verifyCommand": "wc -l < $HOME/code/OpenAC/split-baseline/presplit-failed.txt", "acceptanceCriteria": ["main fast-forwarded to docs/gateway-branch-split-spec", "presplit.sha = main HEAD", "presplit-failed.txt from full Release test run, ~70 lines", "clean working tree"], "modelTier": "mechanical"}
```

---

### Task 2: Create the branches and worktrees

**Goal:** Local branches `rynth` (at `PRESPLIT`), `upstream-main` (at `391e709a`) and `chore/split-rynth` (at `PRESPLIT`) exist, each in its own worktree, and the Rynth worktrees build.

**Files:**
- Create worktrees: `~/code/OpenAC/OpenAC-rynth` (`rynth`), `~/code/OpenAC/OpenAC-upstream` (`upstream-main`), `~/code/OpenAC/OpenAC/.worktrees/split-rynth` (`chore/split-rynth`)

**Acceptance Criteria:**
- [ ] `git rev-parse rynth` = `PRESPLIT`; `git rev-parse upstream-main` = `391e709afddcf82900a6352581ad8bd084cc07f2`; `git rev-parse chore/split-rynth` = `PRESPLIT`.
- [ ] `git worktree list` shows the three worktrees on those branches.
- [ ] `external/RynthCore` and `external/RynthSuite` are initialised in the `rynth` and `split-rynth` worktrees.
- [ ] `dotnet build AcDream.slnx -c Release` in `~/code/OpenAC/OpenAC-rynth` → `0 Error(s)`.
- [ ] Nothing pushed yet.

**Verify:** `git -C ~/code/OpenAC/OpenAC worktree list` → three extra entries on `rynth`, `upstream-main`, `chore/split-rynth`.

**Steps:**

- [ ] **Step 1: Branches**

```bash
cd ~/code/OpenAC/OpenAC
PRESPLIT=$(cat $HOME/code/OpenAC/split-baseline/presplit.sha)
git fetch upstream
test "$(git rev-parse upstream/main)" = 391e709afddcf82900a6352581ad8bd084cc07f2 \
  || { echo "upstream moved: stop and ask the user"; exit 1; }
git branch rynth "$PRESPLIT"
git branch upstream-main upstream/main
git branch chore/split-rynth "$PRESPLIT"
```

If `upstream/main` has moved past `391e709a`, stop and ask the user: syncing a new upstream release is not part of this plan.

- [ ] **Step 2: Worktrees with submodules (reference the main clone's objects)**

```bash
cd ~/code/OpenAC/OpenAC
git worktree add ~/code/OpenAC/OpenAC-rynth rynth
git worktree add ~/code/OpenAC/OpenAC-upstream upstream-main
git worktree add .worktrees/split-rynth chore/split-rynth
for wt in ~/code/OpenAC/OpenAC-rynth ~/code/OpenAC/OpenAC/.worktrees/split-rynth; do
  for name in RynthCore RynthSuite; do
    git -C "$wt" submodule update --init \
      --reference ~/code/OpenAC/OpenAC/external/$name -- external/$name
  done
done
```

- [ ] **Step 3: Build the rynth worktree**

```bash
cd ~/code/OpenAC/OpenAC-rynth
# environment block from Global Constraints
dotnet build AcDream.slnx -c Release 2>&1 | tail -3
git checkout -- '*.lock.json'; git status --short
```

Expected: `0 Error(s)`, clean status.

No commit in this task.

```json:metadata
{"files": [], "verifyCommand": "git -C ~/code/OpenAC/OpenAC worktree list", "acceptanceCriteria": ["rynth and chore/split-rynth at PRESPLIT", "upstream-main at 391e709a", "three worktrees exist", "Rynth submodules initialised in rynth and split-rynth worktrees", "rynth worktree builds with 0 errors", "nothing pushed"], "modelTier": "mechanical"}
```

---

### Task 3: Remove Rynth, the overlay and the extension seam from the client

**Goal:** In the `split-rynth` worktree, the graphical client, headless client and session configuration no longer contain or reference Rynth, the ImGui overlay or `AcDream.ClientExtensions`, and the client and its tests build.

**Files:** (all paths relative to `~/code/OpenAC/OpenAC/.worktrees/split-rynth`)
- Delete: `src/AcDream.App/Extensions/`, `src/AcDream.ClientExtensions/`, `src/AcDream.Rynth.EngineUi/`, `src/AcDream.Rynth.Host/`, `src/AcDream.Rynth.ScriptWindows/`, `src/AcDream.Rynth.Sdk/`, `src/AcDream.Rynth.Plugin.*/`, `tests/AcDream.Rynth.Host.Tests/`, `tests/AcDream.App.Tests/Extensions/`
- Modify: `AcDream.slnx`, `src/AcDream.App/AcDream.App.csproj`, `tests/AcDream.App.Tests/AcDream.App.Tests.csproj`, `src/AcDream.App/Rendering/GameWindow.cs`, `src/AcDream.App/Rendering/GameWindowLifetime.cs`, `src/AcDream.App/Composition/FrameRootComposition.cs`, `src/AcDream.App/UI/RetainedUiInputBinding.cs`, `src/AcDream.App/Input/SilkKeyboardSource.cs`, `src/AcDream.App/UI/RetailUiRuntime.cs`, `src/AcDream.App/UI/Layout/ChatWindowController.cs`, `src/AcDream.App/RuntimeOptions.cs`, `src/AcDream.App/Configuration/SessionConfiguration.cs`, `src/AcDream.Headless/Configuration/HeadlessConfiguration.cs`, `src/AcDream.Launcher.Core/Launching/SessionConfigDocument.cs`
- Test: `tests/AcDream.App.Tests/Configuration/RuntimeOptionsSessionConfigTests.cs`, `tests/AcDream.HostParity.Tests/SessionConfigPluginFieldParityTests.cs`, `tests/Fixtures/launcher-session/session-config-shared-fixture.json`

**Acceptance Criteria:**
- [ ] The deleted folders are gone and `AcDream.slnx` lists none of them.
- [ ] `git grep -n -E "ClientExtension|ImGui|OverlayInputGate|OverlayKeyGate|ExtensionChat|ExtensionEntryClaim|SetExtensionHidden|_chatSeam|_clientScreen|_itemDrops|RynthPump|ACDREAM_RYNTH" -- src tests` → no matches.
- [ ] Every hunk removed from a shared file is one whose fork-side version (`git diff 391e709a -- <file>`) exists only for the overlay/extension/Rynth; all other fork hunks in those files are unchanged (`git diff PRESPLIT -- <file>` shows only removals of those hunks).
- [ ] `FrameRootComposition` passes `devTools: null` exactly as upstream does.
- [ ] A session-config JSON containing `"rynth"` is now rejected by the graphical loader (strict `UnmappedMemberHandling.Disallow` already in `SessionConfigurationLoader`); the shared fixture no longer carries the key.
- [ ] `dotnet build src/AcDream.App/AcDream.App.csproj -c Release` and `dotnet build tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release` → `0 Error(s)`.
- [ ] `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~Configuration"` and `dotnet test tests/AcDream.HostParity.Tests -c Release --filter "FullyQualifiedName~SessionConfigPluginFieldParity"` pass.

**Verify:** the `git grep` above → no output; `dotnet build tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release 2>&1 | tail -3` → `0 Error(s)`.

**Steps:**

- [ ] **Step 1: Delete the projects and folders, drop them from the solution and project files**

```bash
cd ~/code/OpenAC/OpenAC/.worktrees/split-rynth
git rm -r -q src/AcDream.App/Extensions src/AcDream.ClientExtensions \
  src/AcDream.Rynth.EngineUi src/AcDream.Rynth.Host src/AcDream.Rynth.ScriptWindows \
  src/AcDream.Rynth.Sdk src/AcDream.Rynth.Plugin.* \
  tests/AcDream.Rynth.Host.Tests tests/AcDream.App.Tests/Extensions
sed -i '' -E '/AcDream\.ClientExtensions|AcDream\.Rynth\.(EngineUi|Host|ScriptWindows|Sdk|Plugin\.)/d' AcDream.slnx
rm -rf src/AcDream.App/Extensions src/AcDream.ClientExtensions src/AcDream.Rynth.EngineUi \
  src/AcDream.Rynth.Host src/AcDream.Rynth.ScriptWindows src/AcDream.Rynth.Sdk \
  src/AcDream.Rynth.Plugin.* tests/AcDream.Rynth.Host.Tests   # leftover bin/obj
grep -n -i "rynth\|ClientExtensions" AcDream.slnx
```

Expected remaining slnx Rynth lines: only `AcDream.Rynth.NavData`, the three `tools/Rynth*` and the `NavData`/`Tools.Rynth*` test projects (Task 4 removes them).

In `src/AcDream.App/AcDream.App.csproj` delete the two `<ProjectReference>` lines for `AcDream.ClientExtensions` and `AcDream.Rynth.Host`, and the three `<None Include="..\..\external\Rynth…" …/>` elements (Phosphor, RynthCore, RynthSuite licences, with any continuation lines). In `tests/AcDream.App.Tests/AcDream.App.Tests.csproj` delete the `RynthConfigSandbox.cs` `<Compile>` and its comment line. If an `<ItemGroup>` becomes empty, delete it.

- [ ] **Step 2: Unwire the shared client files, guided by upstream**

For each file below, run `git diff 391e709a -- <file>` and remove exactly the fork hunks that exist for the overlay, the extension seam or Rynth; restore upstream's form of those lines. Keep every other fork hunk.

- `src/AcDream.App/Rendering/GameWindow.cs`: delete fields `_extensions`, `_overlay`, `_keyGate`, `_chatSeam`, `_clientScreen`, `_itemDrops`; delete their construction (`_clientScreen = new(...)`, `_itemDrops = new(...)`, `_overlay.ExternalDragActive = ...`, `_extensions = new ClientExtensionHost(...)`, the Rynth log lines, `_extensions.Attach(...)`); restore `new AcDream.App.Input.DevToolsInputCaptureSource(options.DevTools)` (upstream form); delete `_retailUiRuntime.ExtensionChat = _chatSeam;`, `_clientScreen.BindWindows/BindCamera(...)`, `_uiHost.Root.DragReleaseInterceptor = _itemDrops.InterceptRelease;`, `_overlay.BindIcons(...)`, `_keyGate = _overlay.AttachKeyGate(...)` and the `_keyGate` disposals, `{ Overlay = _overlay }` in the frame-root dependencies, `_extensions.Tick(dt)`, `_extensions.Dispose()`, and the `OverlayKeyGate:` argument passed into `GameWindowLifetime`. If `DevToolsInputCaptureSource` gained overlay parameters on the fork, restore its upstream constructor too (it lives under `src/AcDream.App/`; find it with `git grep -n "class DevToolsInputCaptureSource"`).
- `src/AcDream.App/Rendering/GameWindowLifetime.cs`: remove the `IDisposable? OverlayKeyGate = null` record parameter and the `Hard("overlay key gate", ...)` entry.
- `src/AcDream.App/Composition/FrameRootComposition.cs`: remove the `Overlay` init property and the `d.Overlay?.Start(...)` line; `devTools: d.Overlay` → `devTools: null` (both places); `(IRenderFrameFailureRecovery?)d.Overlay ?? NullRenderFrameFailureRecovery.Instance` → upstream's argument at that position (`git show 391e709a:src/AcDream.App/Composition/FrameRootComposition.cs | grep -n "NullRenderFrameFailureRecovery"`).
- `src/AcDream.App/UI/RetainedUiInputBinding.cs` and `src/AcDream.App/Input/SilkKeyboardSource.cs`: every `OverlayInputGate.*` condition back to upstream's form.
- `src/AcDream.App/UI/RetailUiRuntime.cs`: remove `ExtensionChat` and the three uses (`SetExtensionHidden(...)` call, `TryClaimEntry()` early-out, `controller.ExtensionEntryClaim = ...`).
- `src/AcDream.App/UI/Layout/ChatWindowController.cs`: remove `SetExtensionHidden` and `ExtensionEntryClaim` and the code paths that only they drive.

Then:

```bash
git grep -n -E "ClientExtension|ImGui|OverlayInputGate|OverlayKeyGate|ExtensionChat|ExtensionEntryClaim|SetExtensionHidden|_chatSeam|_clientScreen|_itemDrops" -- src tests
```

Expected: no output.

- [ ] **Step 3: Remove the Rynth session switches**

- `src/AcDream.App/RuntimeOptions.cs`: delete the `Rynth` and `RynthPump` properties with their doc comments, the two `env("ACDREAM_RYNTH…")` initialisers, and the two `session.Rynth… ?? baseOptions.Rynth…` lines.
- `src/AcDream.App/Configuration/SessionConfiguration.cs`: delete `Rynth` and `RynthPump` with doc comments.
- `src/AcDream.Headless/Configuration/HeadlessConfiguration.cs`: delete `Rynth` with its doc comment.
- `src/AcDream.Launcher.Core/Launching/SessionConfigDocument.cs`: delete `Rynth` and `RynthPump` with doc comments, and any place the document writes them (`git grep -n "Rynth" src/AcDream.Launcher.Core/Launching`).
- `tests/Fixtures/launcher-session/session-config-shared-fixture.json`: delete the `"rynth": false,` line (keep the JSON valid: check the preceding line's comma).
- `tests/AcDream.HostParity.Tests/SessionConfigPluginFieldParityTests.cs`: delete the `Assert.Equal(windowless.Rynth, windowed.Rynth);` and `Assert.False(windowed.Rynth);` lines.
- `tests/AcDream.App.Tests/Configuration/RuntimeOptionsSessionConfigTests.cs`: delete the tests (whole `[Fact]`/`[Theory]` methods) that exist only for `Rynth`/`RynthPump`/`ACDREAM_RYNTH*`; in mixed tests remove only the Rynth lines.

```bash
git grep -n -E "RynthPump|ACDREAM_RYNTH|\.Rynth\b|\"rynth\"" -- src tests
```

Expected: no output.

- [ ] **Step 4: Build and run the affected tests**

```bash
# environment block from Global Constraints
dotnet build tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release 2>&1 | tail -3
dotnet build tests/AcDream.HostParity.Tests/AcDream.HostParity.Tests.csproj -c Release 2>&1 | tail -3
dotnet test tests/AcDream.App.Tests -c Release --no-build --filter "FullyQualifiedName~Configuration"
dotnet test tests/AcDream.HostParity.Tests -c Release --no-build --filter "FullyQualifiedName~SessionConfigPluginFieldParity"
git checkout -- '*.lock.json'
```

Expected: `0 Error(s)` twice; both filtered runs pass. Fix compile errors by finding the fork hunk responsible (`git diff 391e709a -- <file>`), never by adding new code. (The launcher still builds against `Rynth.NavData` at this point; that is Task 4.)

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "chore(split): remove Rynth, the ImGui overlay and the extension seam from the client

Claude-Session: <session link from the system reminder>"
```

```json:metadata
{"files": ["AcDream.slnx", "src/AcDream.App/AcDream.App.csproj", "tests/AcDream.App.Tests/AcDream.App.Tests.csproj", "src/AcDream.App/Rendering/GameWindow.cs", "src/AcDream.App/Rendering/GameWindowLifetime.cs", "src/AcDream.App/Composition/FrameRootComposition.cs", "src/AcDream.App/UI/RetainedUiInputBinding.cs", "src/AcDream.App/Input/SilkKeyboardSource.cs", "src/AcDream.App/UI/RetailUiRuntime.cs", "src/AcDream.App/UI/Layout/ChatWindowController.cs", "src/AcDream.App/RuntimeOptions.cs", "src/AcDream.App/Configuration/SessionConfiguration.cs", "src/AcDream.Headless/Configuration/HeadlessConfiguration.cs", "src/AcDream.Launcher.Core/Launching/SessionConfigDocument.cs", "tests/AcDream.App.Tests/Configuration/RuntimeOptionsSessionConfigTests.cs", "tests/AcDream.HostParity.Tests/SessionConfigPluginFieldParityTests.cs", "tests/Fixtures/launcher-session/session-config-shared-fixture.json"], "verifyCommand": "git grep -n -E \"ClientExtension|ImGui|OverlayInputGate|OverlayKeyGate|ExtensionChat|ExtensionEntryClaim|SetExtensionHidden|_chatSeam|_clientScreen|_itemDrops|RynthPump|ACDREAM_RYNTH\" -- src tests; dotnet build tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release", "acceptanceCriteria": ["deleted folders gone and absent from slnx", "grep for overlay/extension/Rynth-switch identifiers empty", "only overlay/extension/Rynth hunks removed from shared files", "FrameRootComposition devTools: null as upstream", "rynth key removed from fixture and configs", "App and App.Tests build with 0 errors", "Configuration and SessionConfigPluginFieldParity tests pass"], "modelTier": "standard"}
```

---

### Task 4: Remove navdata from the launcher, and the Rynth tools

**Goal:** The launcher and launcher core build without navdata and without `AcDream.Rynth.NavData`, and the Rynth tool projects are gone, while the fork update feed still works.

**Files:** (relative to the `split-rynth` worktree)
- Delete: `src/AcDream.Rynth.NavData/`, `tests/AcDream.Rynth.NavData.Tests/`, `src/AcDream.Launcher.Core/NavData/`, `tests/AcDream.Launcher.Core.Tests/NavData/`, `src/AcDream.Launcher/ViewModels/RynthNavDataViewModel.cs`, `src/AcDream.Launcher/ViewModels/LauncherWindowViewModel.NavData.cs`, `tests/AcDream.Launcher.Tests/RynthNavDataViewModelTests.cs`, `tests/AcDream.Launcher.Tests/LauncherWindowViewModel.NavData.Tests.cs`, `tools/RynthInteropRedirect/`, `tools/RynthNavData/`, `tools/RynthTypeExtract/`, `tests/AcDream.Tools.RynthInteropRedirect.Tests/`, `tests/AcDream.Tools.RynthNavData.Tests/`, `tests/AcDream.Tools.RynthTypeExtract.Tests/`
- Modify: `AcDream.slnx`, `src/AcDream.Launcher.Core/AcDream.Launcher.Core.csproj`, `src/AcDream.Launcher.Core/Updates/ReleaseManifestClient.cs`, `src/AcDream.Launcher/App.axaml.cs`, `src/AcDream.Launcher/ViewModels/LauncherWindowViewModel.cs`, `src/AcDream.Launcher/ViewModels/LauncherWindowViewModel.Accounts.cs`, `src/AcDream.Launcher/LauncherUpdateComposition.cs`, `src/AcDream.Launcher/MainWindow.axaml`, `src/AcDream.Launcher/MainWindow.axaml.cs`
- Test: `tests/AcDream.Launcher.Tests/MainWindowViewTests.cs`, `tests/AcDream.Launcher.Core.Tests/LauncherCoreDependencyBoundaryTests.cs`

**Acceptance Criteria:**
- [ ] `git grep -n -i -E "navdata|rynth" -- src/AcDream.Launcher src/AcDream.Launcher.Core tests/AcDream.Launcher.Tests tests/AcDream.Launcher.Core.Tests AcDream.slnx` → no matches.
- [ ] `ReleaseManifestClient` still downloads `manifest.json` and `launcher-fingerprint.json` exactly as before (only the navdata member is gone: `git diff PRESPLIT -- src/AcDream.Launcher.Core/Updates/ReleaseManifestClient.cs` shows only removals of navdata lines).
- [ ] `dotnet build AcDream.slnx -c Release` → `0 Error(s)`.
- [ ] `dotnet test tests/AcDream.Launcher.Tests -c Release` and `dotnet test tests/AcDream.Launcher.Core.Tests -c Release` → no failures outside `presplit-failed.txt`.

**Verify:** `dotnet build AcDream.slnx -c Release 2>&1 | tail -3` → `0 Error(s)`.

**Steps:**

- [ ] **Step 1: Delete**

```bash
cd ~/code/OpenAC/OpenAC/.worktrees/split-rynth
git rm -r -q src/AcDream.Rynth.NavData tests/AcDream.Rynth.NavData.Tests \
  src/AcDream.Launcher.Core/NavData tests/AcDream.Launcher.Core.Tests/NavData \
  src/AcDream.Launcher/ViewModels/RynthNavDataViewModel.cs \
  src/AcDream.Launcher/ViewModels/LauncherWindowViewModel.NavData.cs \
  tests/AcDream.Launcher.Tests/RynthNavDataViewModelTests.cs \
  tests/AcDream.Launcher.Tests/LauncherWindowViewModel.NavData.Tests.cs \
  tools/RynthInteropRedirect tools/RynthNavData tools/RynthTypeExtract \
  tests/AcDream.Tools.RynthInteropRedirect.Tests tests/AcDream.Tools.RynthNavData.Tests \
  tests/AcDream.Tools.RynthTypeExtract.Tests
sed -i '' -E '/Rynth/d' AcDream.slnx
rm -rf src/AcDream.Rynth.NavData tests/AcDream.Rynth.NavData.Tests tools/Rynth* tests/AcDream.Tools.Rynth*
grep -n -i rynth AcDream.slnx   # expected: nothing
```

- [ ] **Step 2: Unwire the launcher**

- `src/AcDream.Launcher.Core/AcDream.Launcher.Core.csproj`: delete the `AcDream.Rynth.NavData` ProjectReference and the `rynth-navdata.json` `EmbeddedResource` with its comment.
- `src/AcDream.Launcher.Core/Updates/ReleaseManifestClient.cs`: delete `using AcDream.Launcher.Core.NavData;`, `using AcDream.Rynth.NavData;`, the interface's `FetchNavDataPinAsync` default member with its doc comment, and the implementation's `FetchNavDataPinAsync`. If the comment near line 156 lists "the built-in pin for navdata", drop that phrase.
- `src/AcDream.Launcher/App.axaml.cs`: delete `using AcDream.Launcher.Core.NavData;`, the `var navData = new RynthNavDataViewModel(...)` statement and the `navData: navData` argument.
- `src/AcDream.Launcher/ViewModels/LauncherWindowViewModel.cs`: delete the `RynthNavDataViewModel? navData = null` parameter, `InitializeNavData(...)`, the `NavData.IsOpen` / `NavData.Close()` / `NavData.IsBusy` / `NavData.NotifyCommandStates()` uses, `ShowNavDataBanner` notifications, `NavDataStartupCheck` and `PostNavDataCheck` (method and calls), `DisposeNavData()`. Where `NavData.IsOpen` is one operand of a `||` chain or `else if`, remove just that operand/branch.
- `src/AcDream.Launcher/ViewModels/LauncherWindowViewModel.Accounts.cs`, `src/AcDream.Launcher/LauncherUpdateComposition.cs`, `src/AcDream.Launcher/MainWindow.axaml.cs`: remove navdata references (`git grep -n -i navdata -- <file>`).
- `src/AcDream.Launcher/MainWindow.axaml`: remove the navdata banner and the "Install / update RynthNav navdata" menu item / panel (every element bound to `NavData.*` or `ShowNavDataBanner`).
- `tests/AcDream.Launcher.Tests/MainWindowViewTests.cs`: remove the navdata expectations.
- `tests/AcDream.Launcher.Core.Tests/LauncherCoreDependencyBoundaryTests.cs`: remove `AcDream.Rynth.NavData` from the allowed-dependency list.

- [ ] **Step 3: Build and test**

```bash
# environment block from Global Constraints
dotnet build AcDream.slnx -c Release 2>&1 | tail -3
rm -rf /tmp/acdt/launcher-trx
dotnet test tests/AcDream.Launcher.Tests -c Release --no-build --logger trx --results-directory /tmp/acdt/launcher-trx
dotnet test tests/AcDream.Launcher.Core.Tests -c Release --no-build --logger trx --results-directory /tmp/acdt/launcher-trx
python3 -I $HOME/code/OpenAC/split-baseline/failed-tests.py /tmp/acdt/launcher-trx \
  | comm -23 - $HOME/code/OpenAC/split-baseline/presplit-failed.txt
git checkout -- '*.lock.json'
```

Expected: `0 Error(s)`; the `comm` output (failures not in the baseline) is empty.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "chore(split): remove launcher navdata and the Rynth tools

Claude-Session: <session link from the system reminder>"
```

```json:metadata
{"files": ["AcDream.slnx", "src/AcDream.Launcher.Core/AcDream.Launcher.Core.csproj", "src/AcDream.Launcher.Core/Updates/ReleaseManifestClient.cs", "src/AcDream.Launcher/App.axaml.cs", "src/AcDream.Launcher/ViewModels/LauncherWindowViewModel.cs", "src/AcDream.Launcher/ViewModels/LauncherWindowViewModel.Accounts.cs", "src/AcDream.Launcher/LauncherUpdateComposition.cs", "src/AcDream.Launcher/MainWindow.axaml", "src/AcDream.Launcher/MainWindow.axaml.cs", "tests/AcDream.Launcher.Tests/MainWindowViewTests.cs", "tests/AcDream.Launcher.Core.Tests/LauncherCoreDependencyBoundaryTests.cs"], "verifyCommand": "dotnet build AcDream.slnx -c Release", "acceptanceCriteria": ["no navdata/rynth in launcher src/tests or slnx", "ReleaseManifestClient diff only removes navdata", "solution builds with 0 errors", "launcher test failures subset of baseline"], "modelTier": "standard"}
```

---

### Task 5: Remove the submodules, scripts, notices and docs; regenerate lock files

**Goal:** No Rynth submodule, release-script step, notice or doc remains on the work branch, the lock files match the smaller solution, and only the allowed `rynth` mentions are left.

**Files:** (relative to the `split-rynth` worktree)
- Delete: submodules `external/RynthCore`, `external/RynthSuite`; `external/rynth-navdata.json`; `docs/superpowers/specs/*rynth*`, `docs/superpowers/plans/*rynth*` (case-insensitive, including `.tasks.json`)
- Modify: `.gitmodules`, `external/Directory.Build.props`, `tools/publish-bin.ps1`, `tools/publish-fork-release.ps1`, `tools/launcher-fingerprint.ps1`, `tools/README.md`, `NOTICE.md`, `docs/building-and-running.md`, `docs/launcher.md`, `docs/ci-and-releases.md`, every `packages*.lock.json` that changes on a locked-mode-off restore
- Test: `tests/AcDream.App.Tests/Release/LauncherFingerprintContractTests.cs`

**Acceptance Criteria:**
- [ ] `git submodule status` → empty; `.gitmodules` deleted if it held only the two Rynth entries.
- [ ] `git grep -il rynth -- . ':!docs/superpowers'` prints exactly the allowed list in this plan (no other file).
- [ ] `publish-bin.ps1` no longer writes `rynth-navdata.json`; `publish-fork-release.ps1` no longer uploads it or mentions navdata in its notes; `launcher-fingerprint.ps1` no longer fingerprints `src/AcDream.Rynth.NavData`, `external/rynth-navdata.json` or `external/RynthCore`; `LauncherFingerprintContractTests` passes against the edited script.
- [ ] Lock files regenerated: `dotnet restore AcDream.slnx --locked-mode` succeeds after the regeneration commit.
- [ ] `dotnet build AcDream.slnx -c Release` → `0 Error(s)`.

**Verify:** `git grep -il rynth -- . ':!docs/superpowers'` → only the allowed files; `dotnet restore AcDream.slnx --locked-mode` → success.

**Steps:**

- [ ] **Step 1: Submodules and external files**

```bash
cd ~/code/OpenAC/OpenAC/.worktrees/split-rynth
git submodule deinit -f external/RynthCore external/RynthSuite
git rm -q external/RynthCore external/RynthSuite external/rynth-navdata.json
cat .gitmodules 2>/dev/null; grep -n -i rynth external/Directory.Build.props
```

If `.gitmodules` still exists and is empty, `git rm .gitmodules`. In `external/Directory.Build.props` remove the Rynth-only properties/conditions shown by the grep (if the file then has nothing left that applies to any remaining `external/` content, delete it: `ls external` tells you what remains).

- [ ] **Step 2: Release scripts and the fingerprint test**

- `tools/publish-bin.ps1`: delete the block (around lines 422–440) that reads `external/rynth-navdata.json` and writes `$BinRoot/rynth-navdata.json`, including its leading comment.
- `tools/publish-fork-release.ps1`: drop `'rynth-navdata.json'` from the `$files` list (line ~83), drop "rynth-navdata.json," from the header comment (line ~11), and change the notes (line ~90) to `"OpenAC fork release $version (macOS Apple silicon only)."`.
- `tools/launcher-fingerprint.ps1`: remove the navdata bullet from the header comment (~14–16), the comment at ~63, and the three entries `'src/AcDream.Rynth.NavData'`, `'external/rynth-navdata.json'`, `'external/RynthCore'` (~70–72).
- `tests/AcDream.App.Tests/Release/LauncherFingerprintContractTests.cs`: remove the expectations for those three inputs.
- `tools/README.md`: delete the `RynthNavData/` table row (and rows for `RynthInteropRedirect/`, `RynthTypeExtract/` if present).

- [ ] **Step 3: NOTICE and docs**

- `NOTICE.md`: delete the `## RynthCore and RynthSuite` section and the Phosphor icon-font paragraph (it describes a font embedded from `external/RynthCore`).
- `docs/building-and-running.md`: delete the `ACDREAM_RYNTH=0` table row (~116) and the session-config sentences about `rynth` (~223–226, ~234, ~239–240), keeping each surrounding sentence grammatical.
- `docs/launcher.md`: delete the whole `## RynthNav navdata` section (from its heading up to `## Installation and updates`), and the "Install / update RynthNav navdata" menu mention if it appears elsewhere.
- `docs/ci-and-releases.md`: drop `rynth-navdata.json` from the release file list (~210) and delete the navdata-pin paragraph (~220–223).
- Delete Rynth-named history docs:

```bash
git ls-files docs/superpowers | grep -i rynth | xargs git rm -q
```

- [ ] **Step 4: Sweep**

```bash
git grep -il rynth -- . ':!docs/superpowers' | sort
```

Expected exactly:

```
BRANCHING.md            # absent until Task 8; ignore if missing
docs/navigation.md
docs/plugin-api.md
src/AcDream.Core/Combat/CombatState.cs
src/AcDream.Core/Items/ServerReplyLog.cs
src/AcDream.Core/Spells/Spellbook.cs
src/AcDream.Plugin.Abstractions/TradeAutomation.cs
src/AcDream.Runtime/Gameplay/RuntimeLocalPlayerMovementState.cs
src/AcDream.Runtime/Gameplay/RuntimeSpellCastState.cs
src/AcDream.Runtime/Gameplay/RuntimeTradeState.cs
tests/AcDream.Runtime.Tests/Gameplay/RuntimeLocalPlayerMovementStateTests.cs
tests/AcDream.Runtime.Tests/Gameplay/RuntimeTradeStateTests.cs
```

plus any `packages*.lock.json` (handled in Step 5). Anything else: remove its Rynth part (or, if it is a reference comment on a kept API, report it so the allowed list can be extended; do not silently leave it).

- [ ] **Step 5: Regenerate lock files and build**

```bash
# environment block from Global Constraints
dotnet restore AcDream.slnx --force-evaluate
git status --short '*.lock.json'
```

Review the lock diffs: every change must be a removal of a Rynth/ClientExtensions/ImGui project or package entry (e.g. `ImGui.NET`, `acdream.rynth.*`, `acdream.clientextensions`). Revert anything else per file (`git checkout -- <file>`), including the known RID churn in `src/AcDream.Launcher/packages.neutral.lock.json` (RID section rewritten to `osx-arm64`) if that is its only change. Then:

```bash
dotnet restore AcDream.slnx --locked-mode
dotnet build AcDream.slnx -c Release 2>&1 | tail -3
dotnet test tests/AcDream.App.Tests -c Release --no-build --filter "FullyQualifiedName~LauncherFingerprintContract"
git grep -il rynth -- '*.lock.json'   # expected: nothing
```

Expected: locked restore succeeds, `0 Error(s)`, fingerprint contract tests pass, no Rynth in lock files.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "chore(split): drop the Rynth submodules, release steps, notices and docs; regenerate lock files

Claude-Session: <session link from the system reminder>"
```

```json:metadata
{"files": [".gitmodules", "external/Directory.Build.props", "tools/publish-bin.ps1", "tools/publish-fork-release.ps1", "tools/launcher-fingerprint.ps1", "tools/README.md", "NOTICE.md", "docs/building-and-running.md", "docs/launcher.md", "docs/ci-and-releases.md", "tests/AcDream.App.Tests/Release/LauncherFingerprintContractTests.cs"], "verifyCommand": "git grep -il rynth -- . ':!docs/superpowers'; dotnet restore AcDream.slnx --locked-mode", "acceptanceCriteria": ["no submodules", "only allowed rynth mentions remain", "release scripts no longer handle navdata; fingerprint contract test passes", "lock files regenerated with only Rynth removals; locked restore succeeds", "solution builds with 0 errors"], "modelTier": "standard"}
```

---

### Task 6: Full verification of the work branch against the baseline

**Goal:** Prove the Rynth-free branch builds and its full test run fails nothing that did not already fail on `PRESPLIT`.

**Files:**
- Create: `$HOME/code/OpenAC/split-baseline/split-failed.txt`, `$HOME/code/OpenAC/split-baseline/split-new-failures.txt`

**Acceptance Criteria:**
- [ ] Clean build of `AcDream.slnx` in the `split-rynth` worktree → `0 Error(s)`.
- [ ] `split-new-failures.txt` (failures not in `presplit-failed.txt`) is empty, or every entry is shown to be a known flaky test (listed in Global Constraints' memory note: `GraphicalPluginSessionTests.ReloadCommand…`, `LiveEntityNetworkBranchRoutingTests…GenericTail`, `GameWindowRenderLeafCompositionTests.Paperdoll…`) that passes when its class is run alone.
- [ ] `git diff --stat PRESPLIT chore/split-rynth -- src/AcDream.Runtime src/AcDream.Core src/AcDream.Core.Net src/AcDream.Plugin.Abstractions src/AcDream.UI.Abstractions` → empty (kept APIs untouched).

**Verify:** `wc -l < $HOME/code/OpenAC/split-baseline/split-new-failures.txt` → `0` (or only re-run-green flaky tests).

**Steps:**

- [ ] **Step 1: Clean build and full test run (background, sandbox disabled)**

```bash
cd ~/code/OpenAC/OpenAC/.worktrees/split-rynth
# environment block from Global Constraints
git clean -xdf -e .vs -q -- src tests tools
dotnet build AcDream.slnx -c Release 2>&1 | tail -3
rm -rf $HOME/code/OpenAC/split-baseline/split-trx
dotnet test AcDream.slnx -c Release --no-build --logger trx \
  --results-directory $HOME/code/OpenAC/split-baseline/split-trx > $HOME/code/OpenAC/split-baseline/split-test.log 2>&1
python3 -I $HOME/code/OpenAC/split-baseline/failed-tests.py $HOME/code/OpenAC/split-baseline/split-trx \
  > $HOME/code/OpenAC/split-baseline/split-failed.txt
comm -23 $HOME/code/OpenAC/split-baseline/split-failed.txt $HOME/code/OpenAC/split-baseline/presplit-failed.txt \
  > $HOME/code/OpenAC/split-baseline/split-new-failures.txt
wc -l $HOME/code/OpenAC/split-baseline/split-failed.txt $HOME/code/OpenAC/split-baseline/split-new-failures.txt
git checkout -- '*.lock.json'
```

- [ ] **Step 2: Triage any new failure**

For each line in `split-new-failures.txt`, run its test class alone (`dotnet test <project> -c Release --no-build --filter "FullyQualifiedName~<Class>"`). If it passes alone and is one of the known flaky classes, note it. Otherwise it is a regression from Tasks 3–5: fix it on `chore/split-rynth` (restore the fork hunk that was wrongly removed; use superpowers-extended-cc:systematic-debugging), commit, and repeat Step 1.

- [ ] **Step 3: Kept-API check**

```bash
PRESPLIT=$(cat $HOME/code/OpenAC/split-baseline/presplit.sha)
git diff --stat "$PRESPLIT" chore/split-rynth -- src/AcDream.Runtime src/AcDream.Core src/AcDream.Core.Net src/AcDream.Plugin.Abstractions src/AcDream.UI.Abstractions
```

Expected: no output.

No commit unless Step 2 needed a fix.

```json:metadata
{"files": [], "verifyCommand": "wc -l < $HOME/code/OpenAC/split-baseline/split-new-failures.txt", "acceptanceCriteria": ["clean Release build 0 errors", "no new failures vs presplit baseline (or only known flaky, green alone)", "Runtime/Core/Core.Net/Plugin.Abstractions/UI.Abstractions unchanged vs PRESPLIT"], "modelTier": "standard"}
```

---

### Task 7: Land the removal on main as one commit and record it in rynth

**Goal:** `main` = `PRESPLIT` + one squashed removal commit; `rynth` has that commit merged with `-s ours` and its tree still equals `PRESPLIT`'s.

**Files:** none edited by hand (git operations only)

**Acceptance Criteria:**
- [ ] `git log --oneline PRESPLIT..main` shows exactly one commit, `chore: split Rynth out of OpenAC Gateway`.
- [ ] `git diff PRESPLIT main` equals `git diff PRESPLIT chore/split-rynth` (same tree): `git rev-parse main^{tree}` = `git rev-parse chore/split-rynth^{tree}`.
- [ ] `git rev-parse rynth^{tree}` = `git rev-parse PRESPLIT^{tree}`, and `git merge-base --is-ancestor main rynth` succeeds.
- [ ] A dry-run merge of `main` into `rynth` reports "Already up to date."

**Verify:** `git rev-parse rynth^{tree} $(cat $HOME/code/OpenAC/split-baseline/presplit.sha)^{tree}` → two identical hashes.

**Steps:**

- [ ] **Step 1: Squash onto main**

```bash
cd ~/code/OpenAC/OpenAC
git switch main
git status --short    # must be empty
git merge --squash chore/split-rynth
git commit -m "chore: split Rynth out of OpenAC Gateway

Rynth now lives on the rynth branch (OpenAC Gateway Rynth). This removes
from main: the Rynth host, plugins, SDK and script windows; the ImGui
overlay and the AcDream.ClientExtensions seam; the rynth/rynthPump session
switches; launcher navdata and AcDream.Rynth.NavData; the Rynth tools; the
RynthCore/RynthSuite submodules; and their release steps, notices and docs.
Kept: the general plugin/runtime APIs modelled on RynthCore behaviour.
See docs/superpowers/specs/2026-10-22-gateway-branch-split-design.md.

Claude-Session: <session link from the system reminder>"
test "$(git rev-parse main^{tree})" = "$(git rev-parse chore/split-rynth^{tree})" && echo "trees match"
git submodule status   # expected: empty; remove stale external/RynthCore, external/RynthSuite dirs if git left them untracked
```

If untracked `external/RynthCore` / `external/RynthSuite` directories remain in this checkout, move them aside rather than deleting (`mv external/RynthCore $HOME/code/OpenAC/split-baseline/RynthCore.checkout`), since the `rynth` worktree has its own copies.

- [ ] **Step 2: Record it in rynth with -s ours**

```bash
cd ~/code/OpenAC/OpenAC-rynth
git status --short    # must be empty
git merge -s ours main -m "Merge main (Rynth removal) into rynth, keeping Rynth

The removal commit belongs to OpenAC Gateway only; recording it here with
-s ours lets later main -> rynth merges apply normally.

Claude-Session: <session link from the system reminder>"
PRESPLIT=$(cat $HOME/code/OpenAC/split-baseline/presplit.sha)
git rev-parse rynth^{tree} "$PRESPLIT^{tree}"
git merge-base --is-ancestor main rynth && echo "main is in rynth"
git merge --no-commit --no-ff main 2>&1 | head -1   # expected: Already up to date.
```

No further commit.

```json:metadata
{"files": [], "verifyCommand": "git rev-parse rynth^{tree} $(cat $HOME/code/OpenAC/split-baseline/presplit.sha)^{tree}", "acceptanceCriteria": ["exactly one commit PRESPLIT..main", "main tree equals chore/split-rynth tree", "rynth tree equals PRESPLIT tree", "main is ancestor of rynth", "merging main into rynth is already up to date"], "modelTier": "mechanical"}
```

---

### Task 8: Add BRANCHING.md on main and prove a normal merge down

**Goal:** `BRANCHING.md` documents the three lines on `main`, and an ordinary merge of `main` into `rynth` brings it across without removing any Rynth file.

**Files:**
- Create: `BRANCHING.md` (on `main`)

**Acceptance Criteria:**
- [ ] `BRANCHING.md` covers: the three branches and their products; merge direction; fix routing (upstream-worthy / client / Rynth-only); the "general API on main, Rynth-only on rynth" rule; the upstream sync procedure; that versioning and release rules arrive with Plan 2/3.
- [ ] After `git merge main` in the `rynth` worktree: `BRANCHING.md` exists there, and `git diff --stat PRESPLIT rynth` lists only `BRANCHING.md`.
- [ ] `src/AcDream.Rynth.Host` and `external/RynthCore` still exist on `rynth`; `dotnet build AcDream.slnx -c Release` in the `rynth` worktree → `0 Error(s)`.

**Verify:** `git -C ~/code/OpenAC/OpenAC-rynth diff --stat $(cat $HOME/code/OpenAC/split-baseline/presplit.sha) rynth` → only `BRANCHING.md`.

**Steps:**

- [ ] **Step 1: Write BRANCHING.md on main**

```bash
cd ~/code/OpenAC/OpenAC
cat > BRANCHING.md <<'MD'
# Branches

This fork keeps three lines of work.

| Branch | Product | What it is |
|---|---|---|
| `upstream-main` | none | An exact mirror of `eriknihlen/OpenAC` `main`. Only ever fast-forwarded; never committed to. |
| `main` | OpenAC Gateway | Our client: upstream plus our changes. No Rynth. |
| `rynth` | OpenAC Gateway Rynth | `main` plus the Rynth host, plugins, overlay and navdata. |

## Merge direction

Merges only flow down: `upstream-main` → `main` → `rynth`.
Never merge `rynth` into `main`, or `main` into `upstream-main`.
Use merges, not cherry-picks, so `git log main ^rynth` shows what `rynth` has not taken yet.

## Where a change goes

- **A fix Erik should get:** branch from `upstream-main`, open the upstream PR from it,
  and merge the same branch into `main`. `rynth` gets it on its next merge from `main`.
- **A client change or fix:** on `main`.
- **A Rynth-only change or fix:** on `rynth`.
- **Something Rynth needs from the client:** if it is a general plugin or client API anyone
  could use, put it on `main`; only code that exists solely for Rynth goes on `rynth`.

## Syncing a new upstream release

1. `git fetch upstream` and fast-forward `upstream-main` to `upstream/main`; push it.
2. Merge `upstream-main` into `main`, resolve, build and test; push.
3. Merge `main` into `rynth`, resolve, build and test; push.

## Versions and releases

Version numbers, tags and the two launchers are described in
`docs/superpowers/specs/2026-10-22-gateway-branch-split-design.md` (sections 3 and 4)
and are being put in place; until then releases follow `docs/ci-and-releases.md`.
MD
git add BRANCHING.md
git commit -m "docs: BRANCHING.md for the upstream-main / main / rynth lines

Claude-Session: <session link from the system reminder>"
```

- [ ] **Step 2: Merge down and check**

```bash
cd ~/code/OpenAC/OpenAC-rynth
git merge main -m "Merge main into rynth

Claude-Session: <session link from the system reminder>"
PRESPLIT=$(cat $HOME/code/OpenAC/split-baseline/presplit.sha)
git diff --stat "$PRESPLIT" rynth
ls -d src/AcDream.Rynth.Host external/RynthCore
# environment block from Global Constraints
dotnet build AcDream.slnx -c Release 2>&1 | tail -3
git checkout -- '*.lock.json'; git status --short
```

Expected: the diff stat shows only `BRANCHING.md`; both paths exist; `0 Error(s)`; clean status.

```json:metadata
{"files": ["BRANCHING.md"], "verifyCommand": "git -C ~/code/OpenAC/OpenAC-rynth diff --stat $(cat $HOME/code/OpenAC/split-baseline/presplit.sha) rynth", "acceptanceCriteria": ["BRANCHING.md covers branches, merge direction, fix routing, API rule, upstream sync, pointer to version/release plans", "normal main->rynth merge adds only BRANCHING.md", "Rynth sources and submodules still on rynth; rynth builds 0 errors"], "modelTier": "mechanical"}
```

---

### Task 9: Push, after the user confirms

**Goal:** `main`, `rynth` and `upstream-main` are on `origin`, with the user's go-ahead, and the work branch is cleaned up.

**Files:** none

**Acceptance Criteria:**
- [ ] The user has explicitly confirmed the push in this session (ask with the summary: one removal commit on `main`, `-s ours` merge and `BRANCHING.md` merge on `rynth`, new `upstream-main`, test result vs baseline).
- [ ] `git ls-remote origin main rynth upstream-main` shows the three local SHAs.
- [ ] `chore/split-rynth` worktree removed; branch kept locally (not pushed) for reference.

**Verify:** `git ls-remote origin refs/heads/main refs/heads/rynth refs/heads/upstream-main` → SHAs equal to `git rev-parse main rynth upstream-main`.

**Steps:**

- [ ] **Step 1: Ask the user** (AskUserQuestion: "Push main, rynth and upstream-main to origin?" with options Push / Hold). On Hold, stop here and report.

- [ ] **Step 2: Push and tidy**

```bash
cd ~/code/OpenAC/OpenAC
git push origin main rynth upstream-main
git ls-remote origin refs/heads/main refs/heads/rynth refs/heads/upstream-main
git rev-parse main rynth upstream-main
git worktree remove .worktrees/split-rynth
```

- [ ] **Step 3: Tell the user what changes for their installs**

Report: builds from `main` no longer contain Rynth; the dev/Dreamweave installs keep Rynth only if rebuilt from `~/code/OpenAC/OpenAC-rynth`; Aethrix is unaffected (plugin contract unchanged in this plan).

```json:metadata
{"files": [], "verifyCommand": "git ls-remote origin refs/heads/main refs/heads/rynth refs/heads/upstream-main", "acceptanceCriteria": ["user explicitly confirmed the push", "origin has main, rynth, upstream-main at local SHAs", "split-rynth worktree removed, branch kept locally", "user told how installs and Aethrix are affected"], "modelTier": "mechanical"}
```

---

## Later plans (not part of this one)

- **Plan 2 — names and versions:** product names; four-part `0.1.26.N` client version without lock-file churn (`<Version>` today is recorded in ~47 lock files, so the release counter must live in a separate property feeding `FileVersion`/`InformationalVersion`/tags); host and launcher version comparison currently looks only at MAJOR.MINOR.PATCH; four-part `ForkContractVersion`; Aethrix package bump.
- **Plan 3 — launcher releases and identity:** tag-prefix release lookup replacing `releases/latest`; per-branch app name, bundle ID and install folder; `publish-fork-release.ps1` per product.
- **Plan 4 — cast at a target without selecting it:** its own short spec, then plugin API + contract bump + Aethrix.
