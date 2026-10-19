# RynthAi dashboard: Skills panel

Date: 2026-10-19. Branch `feat/rynth-skills`.

## Goal

The dashboard's Skills button opens RynthCore's real Skills panel: skills,
attributes and vitals with ranks, XP spent, buffed and base values, the buffs
on each stat, level / XP / credits / luminance, and working "+1 / +10 / train"
buttons. Their source compiles unchanged (rule: [[rynth-no-rewrite]]);
everything missing is a stand-in in `AcDream.Rynth.EngineUi/StandIns` fed by
the Rynth host.

Out of scope: the Progression tab (needs RynthILT, or a RynthAi that exports
`RynthPluginGetProgressionJson`; ours doesn't, so the tab stays hidden) and
Mastery (Aelrynth only; `ServerInfo.IsAelrynth` is false).

## RynthCore files compiled unchanged

Added to `AcDream.Rynth.EngineUi.csproj` (a probe build on 2026-10-19 confirmed
the frontier below):

- `ImGui/Panels/SkillsFace.cs`, `SkillsFace.Progression.cs`, `SkillsData.cs`
- `UI/Data/ProgressionData.cs` (its `ProgressionSource` binds the export
  through the existing `PluginExportBinder` stand-in and finds nothing)
- `Compatibility/TrainingCosts.cs`, `Compatibility/PlayerProgressHooks.cs`
- `ImGui/MonsterHud/PortalSpellTable.cs` (replaces our never-ready stand-in, so
  spell names and icons also reach the panels already compiled)
- `UI/ScriptWindows/AcIconDecoder.cs` (`AcDatFile`) and
  `../RynthCore.StatusAgent/Dat/DxtUtil.cs` (linked the same way their own
  engine csproj links it)
- RynthTypeExtract: `AelrynthWire.cs` `=MasterySkill,MasteryData` and
  `MasteryFeed.cs` `=MasterySnapshot`. `MasteryFeed` itself is a stand-in: its
  real file needs their chat hooks and wire.

The `SkillsFace` and `PortalSpellTable` placeholders leave
`DashboardStandIns.cs`; `RynthAiDashboard` registers `SkillsFace` like the
other faces.

## Finding client_portal.dat

`AcDatFile.FindPortalDat` and `PortalSpellTable.FindPortalDat` look only in
`Path.GetDirectoryName(Environment.ProcessPath)` and the current directory.
These are the only two `Environment.ProcessPath` reads in the engine source.

`RynthInteropRedirect` gains `--redirect-member <type>::<member>=<stand-in
type>`: every call to that one member (here the `System.Environment::get_ProcessPath`
getter) becomes a call to the same-named static member on the stand-in type
in the assembly. Unlike `--redirect-type`, the other `Environment` members
(`TickCount64`, `GetFolderPath`, ...) are untouched. A stand-in lacking the
member fails the run; a rerun finds nothing to do.

`StandIns/ProcessPathStandIn.ProcessPath` returns
`Path.Combine(DatDirectory, "acclient.exe")` when the host has set
`DatDirectory`, else the real `Environment.ProcessPath`. `RynthClientExtension`
sets it from `ClientExtensionContext.DatDirectory`, next to
`RegistryStandIn.InstallPath` (the same dat folder the #6 LOS/collision work uses).

## Data path

Their `PlayerProgressHooks.Prefetch` reads only while the panel wants data
(500 ms panel, 2 s slow) and publishes on change. Its calls land in stand-ins:

- `ClientObjectHooks.ReadPlayerProgressLive(PlayerProgress)`: copies a host
  record (below) into their `PlayerProgress`; false when not in the world.
- `EnchantmentHooks.CopyPlayerStatMods(spell[], expiry[], type[], key[], val[],
  category[])`: fills the arrays from the host's stat-mod list, returns the
  count (capped at the array length).
- `MainThreadGuard.IsOnMainThread()`: true (OpenAC updates and draws on one thread).
- `TimeSyncHooks.GetCurrentServerTime()`: the view's `ServerTime`.

Host (`AcDream.Rynth.Host`):

- `RynthPlayerProgress` (new, `RynthStateTypes.cs`): the same fields as their
  `PlayerProgress` minus buffs (skills 1..54: in-table, class, XP spent, ranks,
  init, level-known, buffed, base; attributes 1..6 and vitals 1..3: ranks,
  init, XP spent, buffed, base; level, skill credits, total / unassigned XP,
  luminance / max, vitae).
- `IRynthGameView.TryReadPlayerProgress(RynthPlayerProgress)` and
  `IRynthGameView.PlayerStatMods` (`IReadOnlyList<RynthStatMod>?`, record of
  spell id, expiry in server time, StatMod type, key, value, spell category).
- `RuntimeRynthGameView` fills them from the character data the plugin API's
  skill / attribute / vital lists already use, init levels from
  `LocalPlayerState`, credits from `AvailableSkillCredits`, and the spellbook's
  `ActiveEnchantmentSnapshot` (same buckets and expiry conversion as
  `PlayerEnchantments`).
- `RynthChatHookup` sets and clears the new delegates with the existing ones.
  The host tick calls `PlayerProgressHooks.Prefetch()` each frame (cheap when
  not wanted).

## Raising and training

`PlayerTraining` stand-in (their file binds acclient senders by pattern scan):
`TrainKind` with their values, `Available` / `TrainAvailable` true when the
host has set a sender, `Raise(kind, stype, xp, defer)` and
`TrainSkill(stype, credits, defer)` forward to it and call
`PlayerProgressHooks.RefreshSoon()` on success. The host's sender maps
Attribute/Vital/Skill/TrainWithCredits to `PluginAdvancementKind` and calls the
existing `RequestAdvancement` (vitals: RynthCore's stype is the vital's maximum,
1/3/5, the same as `PluginVitalInfo.StatId`). `defer` is ignored: the call
already runs on the game thread.

## Other stand-ins

`UiSources.Progression = new ProgressionSource()`; `IltCommands.Progression`
no-op; `MasteryFeed` (Snapshot null, Unsupported/NoChatHook/Awaiting false,
PanelOpened/RequestRefresh no-op, QueueRaise false with a reason,
EstimateRadiance 0). `ServerInfo.IsAelrynth` stays false, so the mastery paths
never run.

## Testing

- Redirect tool: `--redirect-member` rewrites a getter call, leaves other
  members alone, fails on a missing stand-in member, is idempotent.
- Host: `RuntimeRynthGameView` progress and stat-mod reads; the stand-ins'
  copy into `PlayerProgress`; `PlayerTraining` maps each kind to the right
  `RequestAdvancement` call; `ProcessPathStandIn` with and without a dat dir;
  the dashboard registers and draws the Skills face headless (`HeadlessImGui`).
- Dat: with `ACDREAM_DAT_DIR`/`~/AsheronsCall`, `SkillsData` and
  `PortalSpellTable` load (skip when the dats are absent).
- Live check (user, later): panel values match the retail character sheet;
  buffs list with countdowns and icons; +1 raise on an attribute, vital and
  skill; train an untrained skill.
