# Target Deck design

Date: 2026-10-27
Status: approved in brainstorm, awaiting spec review
Scope: fork-only Modern HUD addition (no upstream PR)

## Summary

The Target Deck is a Modern HUD plate that describes whatever is selected. It sits at top centre, shows and hides with the selection, and adapts its rows and actions to ten target kinds. It is purely additive: the toolbar's selected-object strip and the retail examine window are unchanged. Monsters, summons and corpses are identified automatically to show their stamina and mana. Plugins can add info rows and menu actions to the plate.

Mockups: `.superpowers/brainstorm/target-deck/kinds.html` (local), also published at https://claude.ai/artifact/JHEiykm8LMf2qb3hLZGzry.

## Decisions

| Topic | Decision |
|---|---|
| Kinds | Ten: Monster, Summon, Player, NPC, Item, Corpse, Container, Portal, World object, Self |
| Role | Full action hub with context actions; nothing that the Command Deck already does (attack, stance, recast) |
| Visibility | Modern HUD only (`MiscSettings.ModernHud`); appears on select, hides on clear; position saved |
| Retail UI | Nothing replaced or hidden |
| Shape | "B": fixed-size nameplate, info rows on the left, icon action column (4 studs, last is ⋯) on the right |
| Auto-ID | Monsters, summons and corpses only, on select, then on events (at least 2 s apart) |
| Other kinds | Details only after a manual Assess, or from an ID already cached for that object |
| Plugins | Info rows (two-line shared budget) and ⋯ menu actions; no custom markup |

## Per-kind content

Rows that need appraisal data show "Assess for details" until it exists. Rows that depend on data the server may not send (decay timer, summon time left, vendor buy list) are shown only when the data is present. Every plate shows distance in the sub line.

| Kind | Identity line | Rows | Column studs | ⋯ menu |
|---|---|---|---|---|
| Monster | Name, level as con colour vs. player, creature type | Live health bar; stamina and mana bars (from ID) | Assess, Use selected item on it, Follow, ⋯ | Examine, Mark on map, Copy name |
| Summon | Name, YOURS chip or owner name | Vitals; time left if known | Assess, Follow, Dismiss (own only), ⋯ | Examine, Mark |
| Player | Name, level, title, PK chip (PK / NPK / PKL), FELLOW chip | Vitals when ID succeeds or fellow; monarch; allegiance | Trade, Give selected item, Tell (opens chat with `/tell <name>, `), Fellowship invite/leave, ⋯ | Follow, Add friend, Swear allegiance, Squelch, Examine |
| NPC | Name, role (vendor / trainer / town crier / quest) | Role; buy categories for vendors | Use/talk, Give selected item, Follow, ⋯ | Examine |
| Item | Name, location (ground / pack / wielded) | Value, burden, workmanship + spell count, wield requirement | Pick up / use, Wield, Give to selection…, ⋯ | Examine, Split, Salvage |
| Corpse | "Corpse of X", whose kill (yours / fellow's / other) | Item count, looted state, decay timer | Open, Mark on map, ⋯ | Examine |
| Container | Name | Locked/open, lock resistance, in use | Open, Use selected key or lockpick, ⋯ | Examine |
| Portal | Name | Level range (greyed if the player does not qualify), tieable | Use portal, Mark on map, ⋯ | Examine |
| World object | Name, sub-type (door, lifestone, bindstone, lever, house hook, storage) | Sub-type state: door open/locked, lifestone Bind, hook/storage owner | Use, Use selected key, ⋯ | Examine |
| Self | Name, level | Burden %, active buff count | Character sheet, ⋯ | Examine |

A disabled action shows as a dimmed stud with a tooltip giving the reason (for example "Select an item in your pack first"). The plate stays the same size for every kind; long names end in "…".

## Architecture

The pattern follows the Command Deck: pure logic in Core, session behaviour in Runtime, drawing in App.

### Core (pure, unit-tested)

- **`PublicWeenieFlags`**: a `[Flags]` enum with the full PWD bitfield (Openable 0x1, Inscribable 0x2, Stuck 0x4, Player 0x8, Attackable 0x10, PlayerKiller 0x20, … Vendor 0x200, Door 0x1000, Corpse 0x2000, LifeStone 0x4000, Portal 0x40000, FreePkStatus 0x200000, PkLiteStatus 0x2000000, BindStone 0x8000000, …). It replaces the private constants in `SelectedObjectHealthPolicy` and `RadarBlipColors`, without changing behaviour.
- **`TargetKind`** enum and **`TargetKindClassifier.Classify(ClientObject obj, uint playerId)`**, with this precedence:
  1. id == playerId → Self
  2. Player flag → Player
  3. `PetOwnerId != 0` → Summon
  4. Creature item type + Attackable → Monster
  5. Creature item type → NPC
  6. Corpse flag → Corpse
  7. Portal flag → Portal
  8. Door, LifeStone or BindStone flag, a hook (`IsHook`), or a non-item useable world fixture → World object (with a `WorldObjectKind` sub-type)
  9. Container item type → Container
  10. anything else → Item
- **`TargetVitalsCache`**: keyed by object id, it stores health, stamina and mana (current and max), a success flag and a timestamp. Entries are evicted when the object is removed and at session end.
- **`TargetPlateModel`** (immutable) and **`TargetPlateModelBuilder`**: these build the snapshot from `ClientObject`, `CombatState` (live health), `TargetVitalsCache`, player state and plugin content. The snapshot holds the kind, identity line, chips, health, vitals plus their age and state (fresh / stale / failed / unknown), rows, action set and plugin rows.

### Runtime

- **`TargetAutoAppraisal`**: subscribes to selection changes, the player's hit and spell landings on the selected target, and `UpdateHealth` for the selected target. It requests appraisal through the existing transaction layer with a new `HudAuto` origin. Rules are in the auto-ID section below.

### App

- **`UiTargetDeck`** (`UiTargetDeck.cs`, `UiTargetDeck.Draw.cs`) lives in `AcDream.App/UI/Hud/`. It is drawn with `DeckChrome` and `DeckFonts`, never `ctx.DefaultFont`. It has a fixed size, can be dragged, saves its position through the existing saved-layout path, and starts at top centre by default.
- It is registered in `ModernHudSwitch` as a `ModernHudReplacement` with an empty `RetailWindows` list. It is mounted only while Modern HUD is on and the selection is non-empty, and it hides as soon as the selection clears.
- **`ITargetDeckActions`**: routes stud and menu actions to existing paths: use, give, trade, fellowship, the chat input prefill for tell, follow, examine (user-origin appraisal), and world-marker mark.
- **Tooltips** use the existing HUD tooltip style. Stud tooltips name the action; plugin-row tooltips list any overflowed rows.

### Data flow

1. The selection changes.
2. The classifier runs.
3. `TargetAutoAppraisal` may send an ID.
4. The builder rebuilds the model on each relevant event: selection, health update, appraisal received, object property update, plugin `Refresh`, and a 1 s tick for staleness.
5. The deck draws the latest snapshot.

## Auto-ID rules

- **Kinds:** Monster, Summon and Corpse only. The other kinds are never auto-identified.
- **Triggers:**
  1. The target is selected and no cached ID is younger than 2 s.
  2. The player's melee/missile hit or spell lands on the selected target.
  3. An `UpdateHealth` for the selected target differs from the health in the last ID.
- **Throttle:** at most one request in flight per target, with at least 2 s between sends per target. Triggers that arrive during the gap combine into one send when it ends. Nothing is ever sent for a target that is no longer selected; a pending send is cancelled when the selection changes.
- **Origin:** `HudAuto` behaves like the existing Automation origin. The transaction layer accepts the response, the object and profiles update, and plugins receive `AppraisalReceived`, but `PresentInUi` stays false unless the examine window is already showing that object. In that case its content refreshes, but the window does not re-show or re-focus.
- **Failed assess:** when the response is unsuccessful, the plate shows live health only. The stamina and mana bars are dimmed, and their tooltip says "Assess failed". Retries follow the same triggers and throttle.
- **Staleness:** when the last successful ID is more than 10 s old, the stamina and mana bars move to the dimmed style.
- **Manual Assess** (👁 stud, ⋯ → Examine) uses the normal user origin and opens the examine window as retail does. Its reply also fills `TargetVitalsCache` and the plate.

## Plugin hooks

These are added to `AcDream.Plugin.Abstractions` as a default interface member on `IUiRegistry`, so hosts and plugins built against older contracts keep working:

```csharp
IPluginTargetPlate TargetPlate => NoOpPluginTargetPlate.Instance;

public interface IPluginTargetPlate
{
    bool IsAvailable { get; }                                  // false while Modern HUD is off
    IDisposable AddProvider(IPluginTargetPlateProvider provider);
    void Refresh(uint objectId);
}

public interface IPluginTargetPlateProvider
{
    PluginTargetPlateContent? Describe(PluginTargetPlateQuery query);
    void OnAction(uint objectId, string actionId);
}
```

- **`PluginTargetKind`** mirrors the ten kinds.
- **`PluginTargetPlateQuery`** carries the object id, kind, name, weenie class id, level (if known), whether it is the player's own object (summon or self), whether it has been identified, and the vitals if known.
- **`PluginTargetPlateContent`** carries up to two `PluginTargetPlateRow` values (label, value, `PluginColor?`, tooltip) and up to four `PluginTargetPlateAction` values (id, label, enabled).

Rules:

- `Describe` is called on the UI thread when the selection changes, when an appraisal for the target arrives, and on `Refresh`. It is never called per frame. Results are cached per object until the next trigger.
- Providers must return quickly. A provider that needs a slow lookup returns null, then calls `Refresh(objectId)` when it has the answer.
- **Budget:** two plugin lines on the plate in total. Providers are ordered by plugin load order, and each gets one line until the budget runs out. Rows past the budget go into the plugin-row tooltip. Rows past a provider's own limit of two are dropped, with one warning to that plugin's log.
- Plugin actions appear in the ⋯ menu, grouped under the plugin's display name. Clicking one calls `OnAction` on the UI thread.
- An exception from `Describe` or `OnAction` is caught and logged to the plugin's log. The provider is then skipped for that object until the next trigger. It never breaks the plate.
- Disposing the registration removes that provider's rows and actions immediately.
- Plugin rows are drawn with a green left edge to show they come from a plugin.
- **Contract:** `0.1.28-fork.2`. Aethrix needs a repack to use it. The Aethrix creature-db provider is a separate project in the Aethrix repository.

## Error handling

- If the selected object is unknown to the client (no `ClientObject`), the plate shows only the name from the selection, with "Unknown object" and no actions.
- If the object is removed while selected, the plate hides when the selection clears (existing `SelectedObjectRemoved` reason). Its cache entry is evicted.
- If an action's precondition is missing (no item selected for Give, not in range), the stud is disabled with a reason tooltip, and no request is sent.
- Plugin faults are isolated as described above.

## Testing

Unit tests run with `~/code/OpenAC/test-baseline/run-mac-tests.sh`:

- **`TargetKindClassifier`**: a table test with one case per kind, plus precedence edges: a PK player (attackable player), your own summon versus another's, a corpse (container + corpse flag), a lifestone, a house hook, a vendor creature (NPC, not monster) and a door.
- **`TargetAutoAppraisal`**:
  - sends on select for monsters, summons and corpses only;
  - never sends for the other kinds;
  - the 2 s throttle combines bursts;
  - the pending send is cancelled when the selection changes;
  - a failed reply keeps retrying on triggers;
  - the `HudAuto` origin leaves `PresentInUi` false when the examine window shows another object.
- **`PublicWeenieFlags`**: the refactor leaves `SelectedObjectHealthPolicy` and radar blip colour behaviour unchanged (existing tests stay green).
- **`TargetPlateModelBuilder`**: rows and actions per kind, the stale fade after 10 s, the failed-assess state, "Assess for details" for kinds that are not auto-identified, and disabled actions with reasons.
- **Plugin plate**: the two-line budget across providers, overflow to the tooltip, actions grouped per plugin, a provider that throws is skipped, dispose removes rows at once, `Refresh` causes a new `Describe`, and `IsAvailable` follows Modern HUD.
- **`UiTargetDeck`**: a draw smoke test, plus geometry with a fixed size across all ten kinds and long names ending in "…".

**Live check (user gate):** on the local ACE test server (10.10.20.20), only after the user says they are available. In the Holtburg test hub, check each kind:

- a drudge and one other monster;
- your own summon and Sammy's;
- a player, a vendor, an item on the ground, a corpse and a chest;
- a portal, a door and a lifestone;
- yourself.

Also verify that:

- stamina and mana refresh during a fight;
- auto-ID never pops the examine window;
- the Modern HUD toggle hides and shows the plate live;
- the saved position survives a relog.

## Out of scope

- The Aethrix creature-db provider (separate Aethrix work)
- Any upstream PR
- Resizing the plate (a possible follow-up on the Command Deck resize pattern)
- A target-of-target display
- Buff and debuff icons on the target
- Changes to the retail selected-object strip or examine window

## Delivery

- This spec is on branch `docs/target-deck-spec`. Next comes an implementation plan (writing-plans) on the same branch.
- Implementation happens in a worktree on `feat/target-deck`. It merges to `main` and `rynth` after the live check passes, and the contract moves to `0.1.28-fork.2`.
