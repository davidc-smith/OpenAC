# Command Deck: modern HUD for vitals, combat and jump

Status: design approved in brainstorming, 2026-10-26. Fork-only; not for upstream.
Baseline: fork `main` at `fd2a66f1`.

## Goal

A first "Modern HUD" control: one game-style window, the **Command Deck**,
that replaces four retail windows: vitals, side-by-side vitals, combat
and the jump powerbar. A client setting turns it on and off. With the
setting off the client behaves exactly as today.

**Known limitation (accepted):** the retail `combat` window also holds the
spell bar: one layout (`0x21000073`) with a `BasicPanel` (power, H/M/L,
toggles) shown in melee/missile and a `SpellcastingPanel` (spell tabs and
favourites) shown in magic. The deck replaces the whole window in every
mode, so **with Modern HUD on there is no spell bar**. Casting still works
through the deck's RECAST and the spellbook. A separate modern
spellcasting component will be designed later; until then this gap is
deliberate.

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
  on the bar as `current / max`.
- **Unknown values:** stamina and mana percent are `null` until the max is
  known (`LocalPlayerState` needs the max). A vital whose percent is
  `null` draws an empty track with `–` as its text, and no trail. Health
  percent is never null (`VitalsVM` falls back to `CombatState`); when
  its max is unknown the text is the rounded percent (`61%`).
- **Damage trail:** when a vital drops, a pale segment stays at the old
  value for 0.3 s, then shrinks to the new value over 0.6 s. Rises have
  no trail (and cut a trail short if they pass it).
  - The first observed value, and the first after a session/character
    change, seeds both the fill and the trail with no animation (no
    100% → actual slide at login).
  - A further drop while a trail is active keeps the trail where it is
    currently drawn (no jump back up) and restarts the 0.3 s hold.
  - A value going from known to `null` clears the trail.
- **Low health:** below 25% health the health bar pulses gently.
- Hover: tooltip with exact values.

### Context row

| Mode | Contents |
|---|---|
| Melee / Missile | Label `POWER` (`ACCURACY` for missile). A track whose gold **handle** is the desired power (`RuntimeCombatAttackState.DesiredPower`; drag or mouse wheel calls `SetDesiredPower`, wheel steps by `DesiredPowerStep`). The glow fill is `PowerBarLevel` (live charge). Then **H / M / L** press-and-hold attack buttons: press = `PressAttack(height)`, release = `ReleaseAttack()`. `RequestedHeight` is lit. |
| Magic | Label `CASTING`. Text, in order of precedence: a cast in progress (`HasPendingCast`, new, see below) with a known `PendingSpellId` → that spell's name, lit; a cast in progress with no single known spell (overlapping casts, where `PendingSpellId` is cleared) → `Casting…`, lit; otherwise `LastRequestedSpellId` dimmed; otherwise `No spell cast yet`. Names from `MagicCatalog`, in the small face, shortened with `...` so they stop before the button (the RECAST tooltip carries the full name). A **RECAST** icon button (stud-sized, tooltip `Recast`) calls `RuntimeSpellCastState.Cast(lastSpell, lastTarget)` (or the untargeted overload when there was no target). Disabled while `HasPendingCast` or when there is no last spell. Component/target errors come from the existing cast gate. No windup bar. |
| Peace | Label `RECALL`. Five studs with tooltips: Lifestone, Marketplace, House, Mansion, Allegiance hometown, each sending the matching `RuntimePortalCommand`. Always enabled: the client cannot reliably tell house/mansion/allegiance ownership, and the server replies with the retail error text when a recall does not apply. |

### Attack button release contract

`PressAttack`/`ReleaseAttack` are stateful, so a lost release would leave
an attack charging. The deck:

- captures the pointer on an H/M/L press and remembers which height is
  held, independent of what the context row currently shows;
- calls `ReleaseAttack()` exactly once for that press, on whichever comes
  first: mouse-up (anywhere, inside or outside the button), combat mode
  leaving melee/missile, the deck being hidden (setting off, leaving the
  world), session reset, or dispose.

### Studs

Repeat attacks, auto-target, keep target in view. Each shows and sets its
`CharacterOptionId` (`AutoRepeatAttack`, `AutoTarget`, `ViewCombatTarget`)
through the same bindings `CombatUiController` uses. Lit = on. Tooltips.
Shown only in melee and missile; in peace and magic the space stays empty
(the layout does not move).

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
| Text | The bundled sans (`BundledUiFont`, `DeckFonts`): 14 px values and tooltip body, 12 px labels and casting line, 14 px SemiBold tooltip titles; text is centred on its row's height | Deck and tooltip text |
| Artwork | `assets/hud/*.svg`, each listed as an `EmbeddedResource` in `AcDream.App.csproj` with `LogicalName="AcDream.Hud.<name>.svg"` (same style as the bundled fonts/icons) | Orb bezel, peace/sword/bow/caster icons, stud icons, recall icons |
| `EmbeddedSvgSpriteCache` | `src/AcDream.App/UI/Hud/EmbeddedSvgSpriteCache.cs` | Loads an embedded SVG by logical name, parses it once, rasterizes with `SvgIconRasterizer` per (name, device pixel size), uploads through the same texture backend `PluginSvgIconCache` uses, and releases every texture on pixel-scale change and on dispose. Owned by the deck mount; disposed with it. Small and reusable by later HUD controls rather than a copy of `PluginSvgIconCache` (which is file/plugin-oriented). A missing or unparsable SVG logs once and draws nothing (the deck still works without icons). |
| `ModernHudSwitch` | `src/AcDream.App/UI/Hud/ModernHudSwitch.cs` | Applies the setting (below) |
| Mount | `RetailUiRuntime.MountCommandDeck()`; `WindowNames.CommandDeck = "command-deck"` | Builds the deck, registers it through `RetailWindowFrame` (move/lock/opacity/position persistence), adds it to `stateManagedVisibilityWindows` |

### ModernHudSwitch

The four retail windows are state-managed: their controllers show and hide
them (e.g. `CombatUiController` shows the combat window on entering combat,
`JumpPowerbarController` while charging, `VitalsSideBySideController` picks
`vitals` or `side-vitals`). So hiding them once is not enough.

- **On (fail-safe order):** first confirm the deck is mounted (the mount
  succeeded and the window is registered). If it is not, leave retail
  untouched, log one diagnostic line, and treat the switch as off for this
  session (the saved setting is kept so a later start can try again).
  Only then: set the gate, hide `vitals`, `side-vitals`, `combat`,
  `jump-powerbar`, and show `command-deck` (subject to the in-world rule).
- **Off:** hide `command-deck` (which releases any held attack, see the
  release contract); lift the gate; call `SyncVisibility()` on the combat,
  jump and side-by-side vitals controllers so each returns to the state it
  should be in now (e.g. combat window back if in combat).
- **Startup:** with `modernHud=true` saved, the switch is applied once
  after all retail windows and the deck are mounted, before the first
  gameplay frame, so retail windows never flash up.

Where the gate goes:

- `CombatUiController` and `JumpPowerbarController` already receive a
  `setWindowVisible` delegate from `RetailUiRuntime`; the gate wraps those
  delegates. No change to either controller.
- `VitalsSideBySideController` does **not**: it calls
  `UiRoot.ShowWindow/HideWindow` directly on each option change and has no
  `SyncVisibility()`. Small change: it takes `show`/`hide` callbacks
  (`RetailUiRuntime` passes gated ones; behaviour with Modern HUD off is
  unchanged) and gains `SyncVisibility()`, which clears its `_applied`
  cache and re-applies the current option.

## Small changes outside the deck

| File | Change |
|---|---|
| `VitalsSideBySideController` | show/hide callbacks instead of direct `UiRoot` calls; `SyncVisibility()` |
| `RuntimeSpellCastState` | `public bool HasPendingCast => _outstandingCastReceipts > 0;` because `PendingSpellId` is cleared when overlapping casts differ, so it cannot say "a cast is in progress" |

## Data sources (all existing; no new network messages)

| Need | Source |
|---|---|
| Vitals | `VitalsVM` |
| Combat mode | `CombatState.CurrentMode` |
| Power, height, attacks | `RuntimeCombatAttackState` (`DesiredPower`, `PowerBarLevel`, `RequestedHeight`, `SetDesiredPower`, `PressAttack`, `ReleaseAttack`) |
| Jump | `JumpChargeSnapshot` |
| Spell cast | `RuntimeSpellCastState` (`HasPendingCast` (new), `PendingSpellId`, `LastRequestedSpellId`, `LastRequestedTargetId`, `Cast`, `StateChanged`) |
| Spell names | `MagicCatalog` |
| Recalls | `RuntimePortalCommand` (RecallLifestone, RecallMarketplace, RecallHouse, RecallMansion, RecallAllegiance) |
| Toggles | `CharacterOptionId` current-value / set-option bindings |

## Testing

- **`CommandDeckModel` unit tests:** context-row mode per combat mode;
  `ACCURACY` label for missile; damage-trail hold and shrink timing; no
  trail on rises; first sample and session change seed without
  animation; drop during an active trail keeps the drawn trail edge;
  null vitals draw `–` with no trail; low-health threshold; recast
  disabled when `HasPendingCast` or no last spell; casting text (pending
  spell / `Casting…` for overlapping casts / last / none); jump fill only
  while charging.
- **Attack release tests:** press then release outside the button; press
  then mode change; press then hide; press then dispose. Each gives
  exactly one `ReleaseAttack()`.
- **`ModernHudSwitch` tests:** on hides and gates the four windows and
  shows the deck; a gated controller's show call while on does nothing;
  repeated `VitalsSideBySideController` ticks (and option flips) while on
  never show a retail vitals window; off restores by state (combat window
  back when in combat, side vitals when that option is set); deck mount
  failure leaves retail untouched; startup with `modernHud=true` never
  shows the retail windows.
- **`RuntimeSpellCastState.HasPendingCast`:** true across two overlapping
  casts with different spells (where `PendingSpellId` is null), false once
  all receipts complete.
- **`VitalsSideBySideController.SyncVisibility`:** re-applies after the
  cache is cleared.
- **Setting round-trip:** `SettingsStore` reads/writes `modernHud`.
- Tests run via `~/code/OpenAC/test-baseline/run-mac-tests.sh`.
- **Live check** on the local ACE server (10.10.20.20), only when the user
  says they are available: switch on/off in each mode, power drag and wheel,
  H/M/L attacks, recast, each recall, jump strip, damage trail, Retina
  sharpness, position saved across relog.

## Out of scope

- Other modern controls (target card, buff tray, compass, quickbar).
- Spell bar / spell favourites: a separate modern spellcasting component,
  designed later. Until then Modern HUD on = no spell bar (see Goal).
- Cast windup bar, component counts, greying recalls by ownership.
- Resizing the deck (fixed size first; scale option later if wanted).
- Upstream PR.
