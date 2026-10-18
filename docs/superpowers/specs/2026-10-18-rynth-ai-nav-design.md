# RynthAi #4: navigation and Nav3D — design

Date: 2026-10-18. Status: approved.

## Goal

RynthAi's navigation walks its routes in OpenAC: it moves along route points, handles
arrival and route types, portals and recall, pause and chat points, follow, the
jump/side-step/back-out stuck ladder, and doors. The Nav3D route markers (rings,
lines, labels) show in the world, depth-tested like RynthCore's.

This is sub-project #4 of the RynthAi split (#1 dashboard, #2 state and actions,
#3 combat and buffs, #5 loot/salvage/vendor/trade, #6 dat raycast and pathfinder).

## Decisions

- **Their code, unchanged.** As in #1–#3, RynthAi compiles from the submodule as-is;
  everything here is host slots in `AcDream.Rynth.Host`.
- **Scope:** movement slots, door state and Nav3D. Left out:
  - `WorldToScreen`, `GetViewportSize`, `GetRadarRect`, `SetRadarSuppressed`: RynthAi
    only reaches them from code that never runs (`NavMarkerRenderer.RenderImGuiFallback`,
    `RadarWallRenderer.Render`).
  - `StopMovement` / `DoMovement`: their only caller is the "Tier1" steering, which is
    hard-disabled (`Tier1Movement => false`).
  - Indoor detours, floor-following lines, line of sight (their dat loader) and outdoor
    detours (`GetPluginInterface` + RynthNav): sub-project #6.
- **TurnToHeading snaps** the body to the heading, as RynthCore does. Their nav servo
  sends "current heading + a small step" every 33 ms, even while autorunning, and was
  tuned for an instant turn. OpenAC's gradual MoveTo turn would lag and fight the run.
- **Nav3D draws through the fork's world-triangle layer** (`IPluginWorldTriangles`),
  which is already depth-tested, see-through by alpha, double-sided and range-culled.
  OpenAC's renderer needs no new pass.
- **Per-corner colours** are added to `PluginWorldTriangle` so ring and line walls fade
  toward the top like RynthCore's. This is a fork contract change: `0.1.25-fork.8`.

## Movement and door slots

A new `RynthApiTable.Movement.cs` partial binds the slots. As in #2/#3, the table only
marshals arguments and catches exceptions; the work is on `IRynthGameView` /
`RuntimeRynthGameView`. Every slot runs on the game thread and answers 0 (or does
nothing) when the player is not in the world.

Motion ids (RynthCore `MovementActionHooks.cs`):
WalkForward `0x45000005`, WalkBackward `0x45000006`, RunForward `0x41000003`,
TurnRight `0x6500000D`, TurnLeft `0x6500000E`, SidestepRight `0x6500000F`,
SidestepLeft `0x65000010`.

| Slot | Implementation |
|---|---|
| `SetAutoRunFn(int on)` | Compare with `MovementOwner.AutoRunActive`; toggle with `Execute(ToggleRunLock)` or call `CancelAutoRun()`. A repeat of the current value does nothing (nav repeats it every tick and every 500 ms). Returns 1. |
| `TurnToHeadingFn(float deg)` | New `RuntimeLocalPlayerMovementState.SnapHeading(deg)` over `PlayerMovementController.Yaw`. Like RynthCore it only writes the heading; it cancels nothing (a use-approach MoveTo keeps running). Decal convention (0 = North, clockwise) is converted to OpenAC yaw. The next AutonomousPosition carries the new heading to the server. A test checks that `GetPlayerPose` reads back the same heading by their formula `(-2·atan2(qz,qw)·180/π + 720) % 360`. |
| `SetMotionFn(uint motion, int on)` | The host keeps a set of held motions (the ids above) and maps it to a `MovementInput` through `SetIntent`. Plugin intent replaces keyboard input completely (`DispatcherMovementInputSource.Capture`), so while autorun is on the intent also sets Forward and Run. RunForward sets Forward + Run; WalkForward sets Forward only. An empty set calls `ClearIntent`. Unknown ids return 0. Turning off a motion that is not held does nothing (nav clears both turns every settle tick). |
| `StopCompletelyFn()` | Clear the held set and the intent, then `Execute(StopCompletely)`. |
| `JumpNonAutonomousFn(float extent)` | `MovementOwner.BeginJump(extent)` (a scripted charge to that power, then release), extent clamped to (0, 1]. |
| `TapJumpFn()` | `BeginJump` at the smallest charge (1/30). Native TapJump is Commence + DoJump at once, which is a near-zero charge. |
| `CommenceJumpFn()` / `DoJumpFn(int autonomous)` | The held set gains a jump flag. Commence sets it, so the intent holds Jump and the controller charges at its own rate, exactly as a held space bar does. DoJump clears it, and the controller releases at the charged power. |
| `LaunchJumpWithMotionFn(shift, w, x, z, c)` | Clears the jump flag and, for that one release frame, adds Forward (w), Backward (x), StrafeLeft (z) and StrafeRight (c), with Run unless shift. The next host tick restores the held set. The controller takes the jump velocity from the motion state on the release frame, as RynthCore writes the motion into CMotionInterp before DoJump. |
| `GetObjectStateFn(uint id, uint* state)` | `RawPhysicsState` from the active entity record; 0 if the object is unknown. RynthAi reads ETHEREAL `0x4` as "door open". |
| `GetObjectMotionOnFn(uint id, int* isOn)` | From the object's retained `Snapshot.MotionState`: forward command On (low 16 bits `0x000B` of `0x4000000B`) gives 1, Off (`0x000C`) gives 0. Any other (or no) forward command answers 0, "not known". Doors only ever play On/Off, so reading the latest motion is enough. |

`RynthApiTableTests.DeferredSlotsStayUnset` is updated: these slots are now set; the
left-out slots above stay zero.

## Nav3D

### RynthCore's behaviour

Each plugin tick the engine clears a pending buffer, plugins add rings, lines and
triangles, and the engine commits the buffer. A tick in which the player changed
landblock is dropped. Caps: 256 rings, 512 lines, 8192 triangles. Coordinates are
metres relative to the player's landblock: X east, Y up, Z north. Colours are ARGB.
RynthAi submits every tick (`NavMarkerRenderer.SubmitNav3D`) and never calls
`Nav3DClear` itself.

### Host side (`AcDream.Rynth.Host`, `RynthNav3D` + `RynthApiTable.Nav3D.cs`)

- **Slots:** `Nav3DClear`, `Nav3DAddRing`, `Nav3DAddRingEx`, `Nav3DAddLine`,
  `Nav3DAddTriangle` write into the pending buffer with RynthCore's caps; anything past
  a cap is ignored. `AddRing` uses their default wall height of 0.5 m.
- **Frame cycle:** `RynthHost.Tick` clears the pending buffer before plugins tick and
  commits after. The buffer records the player's landblock when the first primitive is
  added; the commit is dropped if the landblock changed during the tick.
- **Shapes** (matching `DX9Backend.DrawNavRing` / `DrawNavLine`):
  - Ring: a 32-segment cylinder wall of the given height (minimum 0.1 m), bottom corners
    in the ring colour, top corners at about 6% alpha (`(c & 0x00FFFFFF) | 0x10000000`);
    plus a flat annulus on the ground from `radius - thickness` (minimum 0.01 m) to
    `radius + thickness`.
  - Line: a flat quad on the ground `thickness` wide, plus a 0.15 m vertical wall that
    fades to the top the same way.
  - Triangle: passed through.
- **Coordinates:** each corner converts from (X east, Y up, Z north, metres in the
  submission landblock) to a `PluginNavigationPosition` through a new public
  `RuntimeNavigationPositions.FromCellLocal(cellId, origin)` (over the internal
  `RuntimeNavigationProjection.Position`), using a cell id in the submission landblock.
  Indoors that is the dungeon landblock, so dungeon routes draw too.
- **Drawing:** one `IPluginWorldTriangleLayer`, created through a new `WorldTriangles`
  member on `ClientExtensionContext` (passed in from `GameWindow.WorldTriangles` at
  `ClientExtensionHost.Attach`). The host calls `SetTriangles` only when the committed
  set differs from the last one. The layer is emptied when the player pose is not available (logoff, character select),
  and disposed when the host shuts down. With no window (null layer) the slots still
  accept primitives and draw nothing.
- **Budget:** the renderer draws about 12k triangles a frame, nearest first. A typical
  route (about 20 rings plus legs) is about 3k.

### Per-corner colours (contract `0.1.25-fork.8`)

- `PluginWorldTriangle` gains optional `ColorB` and `ColorC` init properties
  (`PluginColor?`). When unset, a corner uses `Color`. The positional constructor is
  unchanged, so existing callers (Aethrix terrain shading) keep compiling and
  drawing as before.
- `PluginWorldTriangleRenderer` and `PlannedTriangle` carry three colours;
  `GroundShapeBatch` gets an `AddTriangle` overload with a colour per corner (its
  vertices already carry their own colour).
- `ForkContractVersion` goes to `0.1.25-fork.8`; `docs/plugin-api.md` documents the
  new properties and the version that honours them. An older host draws the triangle in
  `Color` only.

## Testing

Unit tests, no live client:

- Autorun repeat is a no-op; toggling on and off works.
- SnapHeading round-trips through `GetPlayerPose` by RynthAi's formula, for headings
  in all four quadrants.
- SetMotion held set → intent mapping, with and without autorun; unknown ids; turning
  off an unheld turn; empty set clears the intent.
- StopCompletely clears the held set and the intent.
- Jump slots: Commence/DoJump hold and release Jump in the intent; LaunchJumpWithMotion
  adds its motion for one tick, then the held set is restored; JumpNonAutonomous and
  TapJump begin a scripted jump.
- GetObjectState / GetObjectMotionOn: unknown object, known state, On/Off seen and
  not seen.
- Nav3D: clear/commit cycle; dropped tick on a landblock change; caps; ring and line
  triangle counts and fade colours; coordinate round-trip against `GetPlayerPose`;
  no `SetTriangles` when nothing changed; layer emptied on logoff.
- Per-corner colours: the renderer uses `ColorB`/`ColorC` when set and `Color` when not.
- `DeferredSlotsStayUnset` updated.

Live check (with the user, later, alongside the pending #2/#3 checks): load a short
outdoor route, start nav, watch it walk, turn and arrive; Nav3D rings and legs show
and are hidden by hills; a door on the route opens; a recall point portals and nav
resumes; stuck ladder jumps when blocked.
