# Command Deck: modern HUD for vitals, combat and jump

Status: design approved in brainstorming, 2026-10-26. Fork-only; not for upstream.
Baseline: fork `main` at `fd2a66f1`.

## Goal

A first "Modern HUD" control: one game-style window, the **Command Deck**,
that replaces four retail windows: vitals, side-by-side vitals, combat
(power/attack height/toggles) and the jump powerbar. A client setting turns
it on and off. With the setting off the client behaves exactly as today.

Later modern controls (target card, buff tray, …) reuse the same setting,
folder and switch pattern. They are out of scope here.

## Decisions

- **Client-only.** Built in `AcDream.App` as a `UiElement` window. No
  plugin contract change, no `-fork.n` bump. (Plugin markup was ruled out
  in the 2026-10-07 modern-panels research: plugins cannot take over retail
  windows or drive `PressAttack`.)
- **Look: game window, not plugin panel.** Dark bevelled frame with a thin
  bronze inner edge, a round "orb" on the left, thick labelled bars with
  numbers always visible. (Chosen layout "D1". Radial/ring/crescent
  layouts were rejected as hard to read.)
- **No stance chips.** You are only ever in peace or combat. Melee, missile
  or magic follows from what you wield, so the deck only shows that, it
  does not offer it as a choice.
- **Fixed height in every mode.** Switching peace / melee / missile / magic
  never changes the window size. A fixed-height *context row* changes its
  contents per mode instead of appearing and disappearing.
- **Live switch.** Toggling the setting takes effect immediately, no
  restart.

## Layout

```
 ┌stud┐ ╭────╮ ┌──────────────────────────────────────┐
 │ ⟳  │ │    │ │ HEALTH            412 / 675  ███████░░ │
 │ ⌖  │ │ ⚔  │ │ STAMINA           390 / 443  ████████░ │
 │ 👁 │ │MELEE│ │ MANA              180 / 400  ████░░░░░ │
 └────┘ ╰────╯ │ <context row: power | casting | recall> │
               │ JUMP  ─────────────────────────────────  │
               └──────────────────────────────────────────┘
```

From top to bottom inside the frame:

1. Health bar (with damage trail)
2. Stamina bar
3. Mana bar
4. **Context row** (fixed height; contents per mode, below)
5. **Jump strip** (thin, always present)

Left of the frame: the **orb** (peace/combat toggle) and, further left,
three **studs** (combat toggles).

Default position: bottom-centre. A normal movable, lockable retail window
whose position is saved.

## Behaviour

### Orb

- Shows the current mode from `CombatState.CurrentMode`: peace (calm emblem,
  soft green glow) or combat with the wielded style: melee (sword),
  missile (bow), magic (caster), with a red glow and a label (`MELEE`,
  `MISSILE`, `MAGIC`, `PEACE`).
- Click: the same combat-mode toggle as `InputAction.CombatToggleCombat`.

### Vitals bars

- Source: `VitalsVM` (percent, current, max for each vital). Numbers drawn
  on the bar as `current / max`. When max is unknown, show the percent
  only, as the retail vitals do.
- **Damage trail:** when a vital drops, a pale segment stays at the old
  value for 0.3 s, then shrinks to the new value over 0.6 s. Rises have
  no trail.
- **Low health:** below 25% health the health bar pulses gently.
- Hover: tooltip with exact values.

### Context row

| Mode | Contents |
|---|---|
| Melee / Missile | Label `POWER` (`ACCURACY` for missile). A track whose gold **handle** is the desired power (`RuntimeCombatAttackState.DesiredPower`; drag or mouse wheel calls `SetDesiredPower`, wheel steps by `DesiredPowerStep`). The glow fill is `PowerBarLevel` (live charge). Then **H / M / L** press-and-hold attack buttons: press = `PressAttack(height)`, release = `ReleaseAttack()`. `RequestedHeight` is lit. |
| Magic | Label `CASTING`. The spell name: `PendingSpellId` lit while a cast is in progress, otherwise `LastRequestedSpellId` dimmed, otherwise "No spell cast yet". Names from `MagicCatalog`. A **RECAST** button calls `RuntimeSpellCastState.Cast(lastSpell, lastTarget)` (or the untargeted overload when there was no target). Disabled while a cast is pending or when there is no last spell. Component/target errors come from the existing cast gate. No windup bar. |
| Peace | Label `RECALL`. Five studs with tooltips: Lifestone, Marketplace, House, Mansion, Allegiance hometown, each sending the matching `RuntimePortalCommand`. Always enabled: the client cannot reliably tell house/mansion/allegiance ownership, and the server replies with the retail error text when a recall does not apply. |

### Studs

Repeat attacks, auto-target, keep target in view. Each shows and sets its
`CharacterOptionId` (`AutoRepeatAttack`, `AutoTarget`, `ViewCombatTarget`)
through the same bindings `CombatUiController` uses. Lit = on. Tooltips.
Shown only in combat; in peace the space stays empty (the layout does not
move).

### Jump strip

Always visible, dim when idle. Fills green from `JumpChargeSnapshot`
(`IsCharging`, `Power`) while charging a jump, in any mode.

### Visibility and edge cases

- Hidden while not in the world (character select, portal space), as the
  retail vitals are.
- Session down: keeps the last values; buttons do nothing when the
  existing `canSend` / in-world gates say no.
- Keybinds are unchanged; the deck and the keys drive the same state.

## Components

| Unit | Location | Responsibility |
|---|---|---|
| Setting | `MiscSettings.ModernHud` (bool, default `false`), `SettingsStore` key `modernHud` | Saved on/off, same pattern as `ExtendedLevels` |
| Config row | `ConfigOptionsPageController`, `ModernHudBindings(Func<bool> Load, Action<bool> Save)` | "Modern HUD" checkbox on Options → Config; applies at once |
| `CommandDeckModel` | `src/AcDream.App/UI/Hud/CommandDeckModel.cs` | Pure logic, no UI. Inputs: a snapshot of vitals, combat mode, attack state, cast state, jump state, and the current time. Output: a `CommandDeckView` record (bar fractions, numbers, trail fractions, low-health pulse, orb mode, context-row mode and contents, recast enabled, jump fill). Owns the damage-trail timing. |
| `UiCommandDeck` | `src/AcDream.App/UI/Hud/UiCommandDeck.cs` | `UiElement` that draws a `CommandDeckView` with the existing `FillRoundedRect` / `StrokeRoundedRect` / `FillVerticalGradient` helpers and SVG sprites, does hit-testing, and forwards input to an `ICommandDeckActions` interface |
| `ICommandDeckActions` | same folder | ToggleCombat, PressAttack(height), ReleaseAttack, SetDesiredPower, SetOption(id, on), Recall(command), Recast. Implemented in the runtime wiring over the existing handlers. |
| Artwork | `src/AcDream.App/UI/Hud/Assets/*.svg` (embedded) | Orb bezel, peace/sword/bow/caster icons, stud icons, recall icons. Rasterized with `SvgIconRasterizer` at the current pixel scale (Retina). |
| `ModernHudSwitch` | `src/AcDream.App/UI/Hud/ModernHudSwitch.cs` | Applies the setting (below) |
| Mount | `RetailUiRuntime.MountCommandDeck()`; `WindowNames.CommandDeck = "command-deck"` | Builds the deck, registers it through `RetailWindowFrame` (move/lock/opacity/position persistence), adds it to `stateManagedVisibilityWindows` |

### ModernHudSwitch

The four retail windows are state-managed: their controllers show and hide
them (e.g. `CombatUiController` shows the combat window on entering combat,
`JumpPowerbarController` while charging, `VitalsSideBySideController` picks
`vitals` or `side-vitals`). So hiding them once is not enough.

- **On:** hide `vitals`, `side-vitals`, `combat`, `jump-powerbar`; set a
  gate that turns those controllers' show requests into no-ops; show
  `command-deck` (subject to the in-world rule).
- **Off:** hide `command-deck`; lift the gate; call `SyncVisibility()` on
  the combat, jump and side-by-side vitals controllers so each returns to
  the state it should be in now (e.g. combat window back if in combat).

The gate sits in the `setWindowVisible` / show delegates those controllers
already receive from `RetailUiRuntime`, so the retail controllers need no
changes.

## Data sources (all existing; no new network messages)

| Need | Source |
|---|---|
| Vitals | `VitalsVM` |
| Combat mode | `CombatState.CurrentMode` |
| Power, height, attacks | `RuntimeCombatAttackState` (`DesiredPower`, `PowerBarLevel`, `RequestedHeight`, `SetDesiredPower`, `PressAttack`, `ReleaseAttack`) |
| Jump | `JumpChargeSnapshot` |
| Spell cast | `RuntimeSpellCastState` (`PendingSpellId`, `LastRequestedSpellId`, `LastRequestedTargetId`, `Cast`, `StateChanged`) |
| Spell names | `MagicCatalog` |
| Recalls | `RuntimePortalCommand` (RecallLifestone, RecallMarketplace, RecallHouse, RecallMansion, RecallAllegiance) |
| Toggles | `CharacterOptionId` current-value / set-option bindings |

## Testing

- **`CommandDeckModel` unit tests:** context-row mode per combat mode;
  `ACCURACY` label for missile; damage-trail hold and shrink timing; no
  trail on rises; low-health threshold; recast disabled when pending or no
  last spell; casting text (pending / last / none); jump fill only while
  charging.
- **`ModernHudSwitch` tests:** on hides and gates the four windows and
  shows the deck; a gated controller's show call while on does nothing;
  off restores by state (combat window back when in combat, side vitals
  when that option is set).
- **Setting round-trip:** `SettingsStore` reads/writes `modernHud`.
- Tests run via `~/code/OpenAC/test-baseline/run-mac-tests.sh`.
- **Live check** on the local ACE server (10.10.20.20), only when the user
  says they are available: switch on/off in each mode, power drag and wheel,
  H/M/L attacks, recast, each recall, jump strip, damage trail, Retina
  sharpness, position saved across relog.

## Out of scope

- Other modern controls (target card, buff tray, compass, quickbar).
- Spell bar / spell favourites (retail spell bar stays).
- Cast windup bar, component counts, greying recalls by ownership.
- Resizing the deck (fixed size first; scale option later if wanted).
- Upstream PR.
